using PegasusGame;

namespace HsServer.Game;

/// <summary>
/// The human's turn timer (A4): when the rope starts and when the turn ends by itself.
/// - Live (live-20261003-dk-classxp): a TurnTimer {Seconds 19, Turn, Show} when 19 s are left of the player's TIMEOUT
///   (45 s first turn, 55 s second, then 75 s: the engine's Controller.TimeOut); it arrived ~3 s later than TIMEOUT - 19
///   after the turn began (the turn-start animation): <see cref="Grace"/>.
/// - Wiki Turn: "If a player takes no actions during their turn and the time limit expires, that player's next turn will
///   begin with an even faster burning fuse, giving them around 7 seconds to play. However, as soon as the player takes
///   any action, the fuse will disappear, and the player's total turn time will be extended to the usual 75 seconds."
/// - Animations eat the player's time (user, live): only the turn's start is allowed for (<see cref="Grace"/>; wiki Turn's
///   "slush": "the difference between when the player's turn starts on the server and when it appears to start in the
///   client"; live's rope came ~3 s after TIMEOUT - 19 whatever was played). When the client's rope burns out it plays the End Turn button's burn-out
///   and waits for the server (TurnTimer TIMEOUT → EndTurnButton.OnTurnTimerEnded): the turn ends <see cref="EndGrace"/> after.
/// - A card's turn-time limit (Nozdormu: Controller.TurnLimitSeconds, the players' TIMEOUT while it lasts) restarts the turn's
///   time whenever it starts or ends (Advanced rulebook, Nozdormu and AFK: "This is implemented by changing the TIMEOUT tag
///   between these two numbers - whenever it changes, the timer is reset"), in solo games too (wiki: "Solo Adventures does not
///   normally feature a turn timer; however, Nozdormu's effect is able to introduce a turn timer"). Live (live-20261008-issues,
///   GT_VS_AI): TIMEOUT 20, the rope at once (TurnTimer spell BIRTH with the summon) and the turn ended by itself 20 s after
///   the summon; the wiki's "40 seconds when played" for solo modes did not happen.
/// - The client's rope is at most <see cref="RopeCapSeconds"/> long (TurnTimer.m_RopeCapSeconds): a shorter turn ropes at once.
/// </summary>
internal sealed class RopeClock
{
    public const int RopeSeconds = 19, RopeCapSeconds = 20, IdleFuseSeconds = 7, NoTimerSeconds = 86400;
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(3), EndGrace = TimeSpan.FromSeconds(2);

    private int turn = -1;
    private bool active, ropeSent, acted, fastFuse;
    private DateTime start, ropeAt, endAt;
    private int ropeSeconds, normalSeconds;
    private int? limitSeen;
    private TurnTimer pending;
    private bool lastTimedOutIdle;

    /// <summary>The clock's next moment for this human turn (the rope, then the end), or null: no timer now.</summary>
    public DateTime? Next(TurnClock clock, bool timed, DateTime now)
    {
        if (clock == null) return null;
        if (clock.Turn != turn) Begin(clock, timed, now);
        else if (clock.Limit != limitSeen) LimitChanged(clock, timed, now);
        if (pending != null) return now;
        if (!active) return null;
        return ropeSent ? endAt : ropeAt;
    }

    private void Begin(TurnClock clock, bool timed, DateTime now)
    {
        turn = clock.Turn;
        start = now;
        pending = null;
        limitSeen = clock.Limit;
        acted = ropeSent = fastFuse = false;
        bool limited = clock.Limit.HasValue;
        active = timed || limited;
        normalSeconds = clock.Limit ?? clock.Seconds;
        fastFuse = active && timed && lastTimedOutIdle && !limited;
        Schedule(fastFuse ? IdleFuseSeconds : normalSeconds, now);
        lastTimedOutIdle = false;
    }

    // the rope burns the last RopeSeconds (or the whole of a shorter turn)
    private void Schedule(int seconds, DateTime from, bool grace = true)
    {
        ropeSeconds = seconds <= RopeCapSeconds ? seconds : RopeSeconds;
        ropeAt = from + (grace ? Grace : TimeSpan.Zero) + TimeSpan.FromSeconds(seconds - ropeSeconds);
        endAt = ropeAt + TimeSpan.FromSeconds(ropeSeconds) + EndGrace;
        ropeSent = false;
    }

    // a turn-time limit started or ended during the turn (Nozdormu entering or leaving play)
    private void LimitChanged(TurnClock clock, bool timed, DateTime now)
    {
        limitSeen = clock.Limit;
        bool shown = ropeSent && active;
        fastFuse = false;
        if (clock.Limit is int limit)
        {
            active = true;
            normalSeconds = limit;
            Schedule(limit, now, grace: false); // the timer resets (rulebook): the rope at once for a short limit
            return;
        }
        // the limit is gone: no timer in a solo game, else the turn's own time, reset from now (rulebook)
        if (!timed)
        {
            active = false;
            if (shown) pending = Hide(now);
            return;
        }
        normalSeconds = clock.Seconds;
        Schedule(normalSeconds, now, grace: false);
        if (shown && ropeAt > now) pending = Hide(now);
    }

    /// <summary>The rope goes away: Show false with the time still left (no timer: <see cref="NoTimerSeconds"/>). Never 0 s — the
    /// client takes 0 as the turn timing out (GameState.TriggerTurnTimerUpdate → OnTimeout: ClearResponseMode, ClearOptions;
    /// InputManager.CancelOption(timeout)) and waits for the server to end the turn: Nozdormu's death left the player unable
    /// to play or end the turn (local-test-20261007-233100, session seq 54153: no option was sent after it). The client's own
    /// hide (GameState.HandleWaitForOpponentReconnectPeriod) is Show false with infinite seconds.</summary>
    private TurnTimer Hide(DateTime now) => new()
    {
        Seconds = active ? Math.Max(1, (int)Math.Ceiling((endAt - EndGrace - now).TotalSeconds)) : NoTimerSeconds,
        Turn = turn, Show = false,
    };


    /// <summary>The rope to send now, if it is due.</summary>
    public TurnTimer RopeDue(DateTime now)
    {
        if (pending != null) { var p = pending; pending = null; return p; }
        if (!active || ropeSent || now < ropeAt) return null;
        ropeSent = true;
        return new TurnTimer { Seconds = ropeSeconds, Turn = turn, Show = true };
    }

    /// <summary>The turn's time is up (once).</summary>
    public bool EndDue(DateTime now)
    {
        if (!active || !ropeSent || now < endAt) return false;
        active = false;
        lastTimedOutIdle = !acted;
        return true;
    }

    /// <summary>The human did something: on a fast fuse, the fuse goes and the turn gets its whole time (counted from the
    /// turn's start); returns the TurnTimer hiding the fuse then.</summary>
    public TurnTimer Acted() => Acted(DateTime.UtcNow);

    public TurnTimer Acted(DateTime now)
    {
        acted = true;
        if (!fastFuse || !active) return null;
        fastFuse = false;
        bool wasShown = ropeSent;
        Schedule(normalSeconds, start);
        return wasShown ? Hide(now) : null;
    }

    /// <summary>The human ended the turn (End Turn).</summary>
    public void EndedByPlayer()
    {
        active = false;
        lastTimedOutIdle = false;
    }
}
