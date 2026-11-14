namespace HsServer.Engine;

/// <summary>
/// Straightforward opponent: spend mana on the most expensive playable cards, use the hero power with leftover
/// mana, then attack (forced into taunts, favourable trades first, otherwise face). One action per call so the
/// server can pace the AI's turn like a real opponent.
/// </summary>
internal static class SimpleAi
{
    /// <summary>Performs one action for the current (AI) player. Returns false when the AI wants to end its turn.</summary>
    public static bool Step(GameState g)
    {
        PlayerState p = g.Current;
        PlayerState foe = p.Opponent;

        Entity card = p.Hand
            .Where(c => c.Cost <= p.AvailableMana && CardScripts.IsSupported(c.Card))
            .Where(c => c.Type != TAG_CARDTYPE.MINION || p.Board.Count < GameState.MaxBoard)
            .OrderByDescending(c => c.Cost).FirstOrDefault();
        if (card != null)
        {
            Entity target = CardScripts.NeedsTarget(card) ? BestDamageTarget(foe) : null;
            if (g.PerformAction(p, card, target)) return true;
        }

        if (!p.HeroPower.Has(GAME_TAG.EXHAUSTED) && p.HeroPower.Cost <= p.AvailableMana && HeroPowers.IsSupported(g, p))
        {
            Entity target = HeroPowers.NeedsTarget(p)
                ? ((TAG_CLASS)p.HeroPower[GAME_TAG.CLASS] == TAG_CLASS.PRIEST ? p.Hero : BestDamageTarget(foe))
                : null;
            if (target == null || !(target == p.Hero && target[GAME_TAG.DAMAGE] == 0))
                if (g.PerformAction(p, p.HeroPower, target)) return true;
        }

        foreach (Entity attacker in p.Board.Append(p.Hero).Where(g.CanAttackNow).ToList())
        {
            var targets = g.AttackTargets(p, attacker);
            if (targets.Count == 0) continue;
            Entity target = targets.FirstOrDefault(t => t.Type == TAG_CARDTYPE.MINION && attacker.Atk >= t.Health && t.Atk < attacker.Health)
                ?? (targets.Contains(foe.Hero) ? foe.Hero : targets.OrderBy(t => t.Health).First());
            if (attacker == p.Hero && target.Type == TAG_CARDTYPE.MINION && target.Atk >= p.Hero.Health) continue;
            if (g.PerformAction(p, attacker, target)) return true;
        }
        return false;
    }

    private static Entity BestDamageTarget(PlayerState foe) =>
        foe.Board.OrderByDescending(m => m.Atk).FirstOrDefault(m => m.Has(GAME_TAG.TAUNT)) ?? foe.Hero;
}
