using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks.PlayerTasks;
using SGame = SabberStoneCore.Model.Game;

namespace HsServer.Sabber;

/// <summary>
/// First AI for the rules engine: one-move look-ahead. Every legal move is tried on a clone of the game and the resulting
/// board is scored; the best move is played if it beats ending the turn. The seed of roadmap item A3 (search AI).
/// </summary>
internal static class SabberAi
{
    public static PlayerTask Step(SGame game, Controller me)
    {
        List<PlayerTask> options = me.Options();
        PlayerTask best = options.FirstOrDefault(t => t.PlayerTaskType == PlayerTaskType.END_TURN) ?? EndTurnTask.Any(me);
        double bestScore = Score(game, me.PlayerId);
        foreach (PlayerTask option in options)
        {
            if (option.PlayerTaskType == PlayerTaskType.END_TURN) continue;
            double score;
            try
            {
                SGame clone = game.Clone();
                PlayerTask copy = Rebuild(option, clone);
                if (copy == null || !clone.Process(copy)) continue;
                // A move that opens a choice (Discover...) is scored after taking the first option.
                Controller cloneMe = clone.ControllerById(me.Id);
                if (cloneMe.Choice is { } pending) clone.Process(ChooseTask.Pick(cloneMe, pending.Choices[0]));
                score = Score(clone, me.PlayerId);
            }
            catch (Exception)
            {
                continue; // a clone the engine can't simulate: skip that move
            }
            if (score > bestScore + 0.01) { bestScore = score; best = option; }
        }
        return best;
    }

    /// <summary>The AI's pick for an open choice: its mulligan (keep cheap cards) or a Discover-style pick (the cheapest).</summary>
    public static PlayerTask Choose(SGame game, Controller me, Choice choice)
    {
        if (choice.ChoiceType == ChoiceType.MULLIGAN)
            return ChooseTask.Mulligan(me, choice.Choices.Where(id => game.IdEntityDic[id].Cost <= 3).ToList());
        return ChooseTask.Pick(me, choice.Choices.OrderBy(id => game.IdEntityDic[id].Cost).First());
    }

    // Board evaluation from one player's side: win/loss, hero health, and each side's minions (attack weighs more).
    private static double Score(SGame game, int playerId)
    {
        Controller me = game.Player1.PlayerId == playerId ? game.Player1 : game.Player2;
        Controller them = me == game.Player1 ? game.Player2 : game.Player1;
        if (them.Hero.Health <= 0 || them.PlayState == PlayState.LOST || them.PlayState == PlayState.CONCEDED) return 100000;
        if (me.Hero.Health <= 0 || me.PlayState == PlayState.LOST) return -100000;
        double Side(Controller c) => c.BoardZone.Where(m => !(m is Location)).Sum(m => m.AttackDamage * 1.5 + m.Health + (m.HasTaunt ? 1 : 0) + (m.HasDivineShield ? 1.5 : 0));
        return (me.Hero.Health + me.Hero.Armor) * 0.8 - (them.Hero.Health + them.Hero.Armor)
               + Side(me) - Side(them) * 1.1 + me.HandZone.Count * 0.3;
    }

    // The same move on a cloned game (entities keep their ids across clones).
    internal static PlayerTask Rebuild(PlayerTask t, SGame clone)
    {
        Controller me = clone.ControllerById(t.Controller.Id);
        ICharacter target = t.Target == null ? null : (ICharacter)clone.IdEntityDic[t.Target.Id];
        IPlayable source = t.Source == null ? null : clone.IdEntityDic[t.Source.Id];
        return t.PlayerTaskType switch
        {
            // the board slot too: where a minion goes matters (Magnetic attaches to the Mech on its right, adjacency)
            PlayerTaskType.PLAY_CARD => PlayCardTask.Any(me, source, t.TargetEntity == null ? null : clone.IdEntityDic[t.TargetEntity.Id],
                (t as PlayCardTask)?.ZonePosition ?? -1, t.ChooseOne),
            PlayerTaskType.MINION_ATTACK => MinionAttackTask.Any(me, source, target),
            PlayerTaskType.HERO_ATTACK => HeroAttackTask.Any(me, target),
            PlayerTaskType.HERO_POWER => HeroPowerTask.Any(me, target, t.ChooseOne, power: source as HeroPower),
            PlayerTaskType.TRADE => TradeTask.Any(me, source),
            PlayerTaskType.FORGE => ForgeTask.Any(me, source),
            PlayerTaskType.PREPARE => PrepareTask.Any(me, source),
            PlayerTaskType.LOCATION => LocationTask.Any(me, (Location)source,
                t is LocationTask { ActivationTarget: { } at } ? clone.IdEntityDic[at.Id] : null),
            PlayerTaskType.TITAN_ABILITY => TitanAbilityTask.Any(me, (Minion)source, t.ChooseOne - 1,
                t.TargetEntity == null ? null : clone.IdEntityDic[t.TargetEntity.Id]),
            PlayerTaskType.STARSHIP_LAUNCH => StarshipLaunchTask.Any(me, (Minion)source),
            PlayerTaskType.INTERACT => InteractTask.Any(me, (Minion)source, clone.IdEntityDic[t.TargetEntity.Id]),
            _ => null,
        };
    }
}
