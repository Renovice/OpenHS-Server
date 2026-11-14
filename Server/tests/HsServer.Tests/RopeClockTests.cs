using HsServer.Game;

namespace HsServer.Tests;

/// <summary>A4 turn timer: live's rope (TurnTimer 19 s before the player's TIMEOUT runs out) and the wiki's rules
/// (idle fuse) and Nozdormu's limit (live-20261008-issues, Advanced rulebook). <see cref="RopeClock"/>.</summary>
public class RopeClockTests
{
    private static readonly DateTime T0 = new(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void The_rope_starts_19_seconds_before_the_turn_runs_out_then_the_turn_ends()
    {
        var clock = new RopeClock();
        var turn = new TurnClock(Turn: 4, Seconds: 75, Limit: null);
        DateTime? rope = clock.Next(turn, timed: true, T0);
        Assert.Equal(T0 + RopeClock.Grace + TimeSpan.FromSeconds(75 - 19), rope);
        Assert.Null(clock.RopeDue(rope!.Value.AddSeconds(-1)));
        var timer = clock.RopeDue(rope.Value);
        Assert.Equal(19, timer.Seconds);
        Assert.Equal(4, timer.Turn);
        Assert.True(timer.Show);
        DateTime? end = clock.Next(turn, true, rope.Value);
        Assert.Equal(rope.Value.AddSeconds(19) + RopeClock.EndGrace, end); // after the client's burn-out
        Assert.True(clock.EndDue(end!.Value));
        Assert.False(clock.EndDue(end.Value.AddSeconds(5))); // once
    }

    [Fact]
    public void Solo_modes_have_no_rope_unless_a_card_limits_the_turn()
    {
        var clock = new RopeClock();
        Assert.Null(clock.Next(new TurnClock(2, 75, null), timed: false, T0));
        // Nozdormu in a solo game (live GT_VS_AI: TIMEOUT 20): the whole 20 s turn on the rope
        var limited = clock.Next(new TurnClock(4, 20, 20), timed: false, T0);
        Assert.Equal(T0 + RopeClock.Grace, limited);
        var rope = clock.RopeDue(limited!.Value);
        Assert.Equal(20, rope.Seconds);
        Assert.True(clock.EndDue(limited.Value.AddSeconds(20) + RopeClock.EndGrace));
        // the next limited turn: 20 s again
        Assert.Equal(T0.AddMinutes(1) + RopeClock.Grace, clock.Next(new TurnClock(6, 20, 20), false, T0.AddMinutes(1)));
    }

    [Fact]
    public void A_turn_limit_shorter_than_the_rope_burns_from_the_start()
    {
        // Nozdormu in a timed game: 15 s turns, the whole turn on the rope
        var clock = new RopeClock();
        DateTime? rope = clock.Next(new TurnClock(5, 75, 15), timed: true, T0);
        Assert.Equal(T0 + RopeClock.Grace, rope);
        Assert.Equal(15, clock.RopeDue(rope!.Value).Seconds);
    }

    [Fact]
    public void An_idle_turn_that_ran_out_gives_a_fast_fuse_until_the_player_acts()
    {
        var clock = new RopeClock();
        var t = clock.Next(new TurnClock(2, 75, null), true, T0)!.Value;
        clock.RopeDue(t);
        Assert.True(clock.EndDue(t.AddSeconds(19) + RopeClock.EndGrace)); // no action all turn
        // next turn: about 7 seconds, on the rope at once
        DateTime start = T0.AddMinutes(2);
        DateTime? fuse = clock.Next(new TurnClock(4, 75, null), true, start);
        Assert.Equal(start + RopeClock.Grace, fuse);
        Assert.Equal(RopeClock.IdleFuseSeconds, clock.RopeDue(fuse!.Value).Seconds);
        // an action: the fuse goes, the whole turn again (from the turn's start)
        var hide = clock.Acted(start.AddSeconds(5));
        Assert.NotNull(hide);
        Assert.False(hide.Show);
        Assert.True(hide.Seconds > 0);
        Assert.Equal(start + RopeClock.Grace + TimeSpan.FromSeconds(75 - 19), clock.Next(new TurnClock(4, 75, null), true, start.AddSeconds(5)));
    }

    [Fact]
    public void Nozdormu_cuts_the_turn_at_once_and_leaving_gives_the_time_back()
    {
        var clock = new RopeClock();
        Assert.NotNull(clock.Next(new TurnClock(6, 75, null), true, T0));
        // he lands 10 s into the turn: the timer resets to his 20 s (rulebook), the rope at once (live: the rope with the summon)
        DateTime landed = T0.AddSeconds(10);
        Assert.Equal(landed, clock.Next(new TurnClock(6, 20, 20), true, landed));
        var rope = clock.RopeDue(landed);
        Assert.Equal(20, rope.Seconds);
        Assert.True(rope.Show);
        // he dies 5 s later: the timer resets to the turn's own time, from then (rulebook), the rope hidden
        DateTime died = landed.AddSeconds(5);
        Assert.Equal(died, clock.Next(new TurnClock(6, 75, null), true, died));
        var hide = clock.RopeDue(died);
        Assert.False(hide.Show);
        Assert.Equal(75, hide.Seconds); // never 0: the client takes 0 as a timeout and drops its options (softlock)
        Assert.Equal(died + TimeSpan.FromSeconds(75 - 19), clock.Next(new TurnClock(6, 75, null), true, died));
    }

    [Fact]
    public void Nozdormu_leaving_in_a_solo_game_takes_the_rope_away()
    {
        var clock = new RopeClock();
        Assert.Null(clock.Next(new TurnClock(6, 0, null), false, T0));
        Assert.Equal(T0, clock.Next(new TurnClock(6, 20, 20), false, T0));
        Assert.True(clock.RopeDue(T0).Show);
        DateTime died = T0.AddSeconds(3);
        Assert.Equal(died, clock.Next(new TurnClock(6, 0, null), false, died));
        var hide = clock.RopeDue(died);
        Assert.False(hide.Show);
        Assert.True(hide.Seconds > 0); // not a timeout (local-test-20261007-233100: Nozdormu's death softlocked the client)
        Assert.Null(clock.Next(new TurnClock(6, 0, null), false, died));
    }
}
