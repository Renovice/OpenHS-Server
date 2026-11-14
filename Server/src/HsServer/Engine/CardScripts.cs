namespace HsServer.Engine;

/// <summary>
/// Server-side card effects. Base stats/keywords come from the client database; only text effects need code here.
/// Starter set: vanilla minions work implicitly, plus a few simple damage/draw spells and battlecries. Cards whose
/// effect is unknown stay unplayable (spells) or play as vanilla (minions) until scripted.
/// </summary>
internal static class CardScripts
{
    private sealed record Script(bool Targeted, Action<GameState, PlayerState, Entity, Entity> Effect);

    private static readonly Dictionary<string, Script> Spells = new()
    {
        ["GAME_005"] = new(false, (g, p, _, _) => g.Set(p.Player, GAME_TAG.TEMP_RESOURCES, p.Player[GAME_TAG.TEMP_RESOURCES] + 1)), // The Coin
        ["CS2_029"] = new(true, (g, _, c, t) => g.Damage(t, 6, c)),  // Fireball
        ["CS2_024"] = new(true, (g, _, c, t) => { g.Damage(t, 3, c); g.Freeze(t); }), // Frostbolt
        ["CS2_008"] = new(true, (g, _, c, t) => g.Damage(t, 1, c)),  // Moonfire
        ["DS1_185"] = new(true, (g, _, c, t) => g.Damage(t, 2, c)),  // Arcane Shot
        ["CS1_130"] = new(true, (g, _, c, t) => g.Damage(t, 2, c)),  // Holy Smite
        ["CS2_023"] = new(false, (g, p, _, _) => g.DrawFor(p, 2)),    // Arcane Intellect
    };

    private static readonly Dictionary<string, Script> Battlecries = new()
    {
        ["CS2_189"] = new(true, (g, _, c, t) => g.Damage(t, 1, c)),  // Elven Archer
        ["CS2_141"] = new(true, (g, _, c, t) => g.Damage(t, 1, c)),  // Ironforge Rifleman
        ["EX1_015"] = new(false, (g, p, _, _) => g.DrawFor(p, 1)),    // Novice Engineer
        ["EX1_082"] = new(false, (g, p, c, _) => { for (int i = 0; i < 3; i++) g.RandomEnemyDamage(p, 1, c); }), // Mad Bomber (enemies only)
    };

    // Reprints (Core, Legacy/Vanilla, tutorial) share the original card's effect: CORE_CS2_024 → CS2_024.
    private static readonly string[] ReprintPrefixes = { "CORE_", "VAN_", "TUTR_", "TU5_" };

    internal static string Canon(string cardId)
    {
        foreach (string p in ReprintPrefixes)
            if (cardId.StartsWith(p, StringComparison.Ordinal)) return cardId.Substring(p.Length);
        return cardId;
    }

    public static bool CanPlaySpell(Entity card) => Spells.ContainsKey(Canon(card.CardId));

    public static bool NeedsTarget(Entity card) =>
        (Spells.TryGetValue(Canon(card.CardId), out var s) && s.Targeted) || (Battlecries.TryGetValue(Canon(card.CardId), out var b) && b.Targeted);

    public static void CastSpell(GameState g, PlayerState p, Entity card, Entity target)
    {
        if (Spells.TryGetValue(Canon(card.CardId), out var s) && (!s.Targeted || target != null)) s.Effect(g, p, card, target);
    }

    public static void Battlecry(GameState g, PlayerState p, Entity card, Entity target)
    {
        if (Battlecries.TryGetValue(Canon(card.CardId), out var b) && (!b.Targeted || target != null)) b.Effect(g, p, card, target);
    }

    /// <summary>Cards the engine can fully play today: vanilla/keyword minions and scripted cards.</summary>
    public static bool IsSupported(CardDef card) =>
        card.Type == TAG_CARDTYPE.MINION
            ? Battlecries.ContainsKey(Canon(card.CardId)) || !HasUnscriptedText(card)
            : Spells.ContainsKey(Canon(card.CardId));

    // Keyword-only minions (Taunt, Charge, Divine Shield...) are handled by tags; anything with other text needs code.
    private static bool HasUnscriptedText(CardDef card) =>
        card.Tag(GAME_TAG.BATTLECRY) != 0 || card.Tag(GAME_TAG.DEATHRATTLE) != 0 || card.Tag(GAME_TAG.TRIGGER_VISUAL) != 0
        || card.Tag(GAME_TAG.AURA) != 0 || card.Tag(GAME_TAG.SPELLPOWER) != 0 || card.Tag(GAME_TAG.COMBO) != 0;
}

/// <summary>Basic hero powers by class (the client's hero card names its power; the class decides the effect).</summary>
internal static class HeroPowers
{
    private static TAG_CLASS Class(PlayerState p) => (TAG_CLASS)p.HeroPower[GAME_TAG.CLASS];

    public static bool IsSupported(GameState g, PlayerState p) => Class(p) switch
    {
        TAG_CLASS.MAGE or TAG_CLASS.HUNTER or TAG_CLASS.WARRIOR or TAG_CLASS.PRIEST or TAG_CLASS.WARLOCK or TAG_CLASS.DRUID => true,
        TAG_CLASS.PALADIN or TAG_CLASS.SHAMAN => p.Board.Count < GameState.MaxBoard,
        _ => false,
    };

    public static bool NeedsTarget(PlayerState p) => Class(p) is TAG_CLASS.MAGE or TAG_CLASS.PRIEST;

    public static void Use(GameState g, PlayerState p, Entity target)
    {
        Entity power = p.HeroPower;
        switch (Class(p))
        {
            case TAG_CLASS.MAGE: if (target != null) g.Damage(target, 1, power); break;
            case TAG_CLASS.PRIEST: if (target != null) g.Heal(target, 2, power); break;
            case TAG_CLASS.HUNTER: g.Damage(p.Opponent.Hero, 2, power); break;
            case TAG_CLASS.WARRIOR: g.GainArmor(p.Hero, 2); break;
            case TAG_CLASS.DRUID: g.GainArmor(p.Hero, 1); break; // +1 Attack not yet modelled
            case TAG_CLASS.WARLOCK: g.DrawFor(p, 1); g.Damage(p.Hero, 2, power); break;
            case TAG_CLASS.PALADIN: g.Summon(p, g.Cards["CS2_101t"]); break;
            case TAG_CLASS.SHAMAN:
                string[] totems = { "CS2_050", "CS2_051", "CS2_052", "NEW1_009" };
                var missing = totems.Where(t => !p.Board.Any(m => m.CardId == t)).ToList();
                if (missing.Count > 0) g.Summon(p, g.Cards[missing[g.Random.Next(missing.Count)]]);
                break;
        }
    }
}
