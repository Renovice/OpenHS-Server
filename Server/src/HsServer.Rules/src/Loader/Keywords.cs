// HsServer addition (not upstream SabberStone): the keyword library — which Hearthstone keywords this engine supports.
using System.Collections.Generic;

namespace SabberStoneCore.Loader
{
	/// <summary>
	/// Support status of every keyword the current client's cards use, keyed by the client's tag id (the engine's own
	/// GameTag enum is from 2021 and lacks newer keywords). `HsServer keywords` lists usage against this table; a keyword
	/// missing here shows as Unreviewed.
	/// </summary>
	public static class Keywords
	{
		public enum Support
		{
			/// <summary>Not looked at yet.</summary>
			Unreviewed,
			/// <summary>Not implemented: cards with it lack that part of their behaviour.</summary>
			Missing,
			/// <summary>Works in some cases / approximated (see the note).</summary>
			Partial,
			/// <summary>Implemented generically (any card with the tag gets it).</summary>
			Supported,
			/// <summary>Only a label for the player: the card's script does the work (e.g. Battlecry).</summary>
			ScriptDriven,
		}

		public static readonly IReadOnlyDictionary<int, (Support Support, string Note)> Status =
			new Dictionary<int, (Support, string)>
			{
				// Evergreen keywords: generic engine rules.
				[189] = (Support.Supported, "Windfury"),
				[190] = (Support.Supported, "Taunt"),
				[191] = (Support.Supported, "Stealth (lost on attack, live position)"),
				[192] = (Support.Supported, "Spell Damage"),
				[194] = (Support.Supported, "Divine Shield"),
				[197] = (Support.Supported, "Charge"),
				[208] = (Support.Supported, "Freeze (own tag = freezes what it damages; reference tags ignored)"),
				[215] = (Support.Supported, "Overload (amount from the client's tag)"),
				[219] = (Support.Supported, "Secret"),
				[220] = (Support.Supported, "Combo"),
				[363] = (Support.Supported, "Poisonous"),
				[1944] = (Support.Supported, "Poisonous without the keyword label"),
				[370] = (Support.Supported, "affected by Spell Damage"),
				[396] = (Support.Supported, "hero power damage bonus"),
				[443] = (Support.Supported, "Choose One (options <id>a / <id>b)"),
				[685] = (Support.Supported, "Lifesteal"),
				[791] = (Support.Supported, "Rush"),
				[849] = (Support.Supported, "Magnetic (from the client data: enchantment tag 2787, MAGNETIC_TO_RACE 2859, MAGNETIZE trigger; Generic.Magnetize)"),
				[2821] = (Support.Supported, "Overheal: TriggerType.OVERHEAL (HsServer, 2026-09-30)"),
				// Labels: the effect is in each card's script.
				[32] = (Support.ScriptDriven, "trigger icon (\"whenever...\")"),
				[217] = (Support.ScriptDriven, "Deathrattle"),
				[218] = (Support.ScriptDriven, "Battlecry"),
				[212] = (Support.ScriptDriven, "Enrage"),
				[339] = (Support.ScriptDriven, "Silence"),
				[350] = (Support.ScriptDriven, "adjacent buff"),
				[362] = (Support.ScriptDriven, "Aura"),
				[415] = (Support.ScriptDriven, "Discover"),
				[335] = (Support.ScriptDriven, "display (Suffocating Shadows, Jeklik); the TriggerKeyword of the card's own discard / destroyed block (Trigger.Keyword, Generic.RunDestroyedTask)"),
				[349] = (Support.ScriptDriven, "display (Devouring Plague, Minefield...)"),
				[542] = (Support.ScriptDriven, "display (Frostmourne)"),
				[886] = (Support.ScriptDriven, "display (Doomsayer)"),
				[1724] = (Support.ScriptDriven, "display (Feast of Souls)"),
				[2165] = (Support.ScriptDriven, "display (Elitist Snob)"),
				// Partial / approximated.
				[1211] = (Support.Supported, "Elusive: the client's ELUSIVE tag becomes CANT_BE_TARGETED_BY_SPELLS + HERO_POWERS when cards load (ElusiveTags)"),
				[968] = (Support.Partial, "Start of Game: only where an original card scripts it"),
				// Missing: the rules engine has no mechanic for these yet.
				[1720] = (Support.Supported, "Tradeable: TradeTask, option marked PowerKeyword=TRADEABLE, DECK_ACTION block (HsServer, 2026-09-30)"),
				[1085] = (Support.Supported, "Reborn: returns once after its Deathrattle with 1 Health (Game.GraveYard, HsServer 2026-09-30)"),
				[1333] = (Support.ScriptDriven, "Outcast: PlayCard records a left- or right-most play (Controller.OutcastPlayId); scripts test it (Demon Hunter class added 2026-09-30)"),
				[3934] = (Support.Supported, "Rewind: a choice to replay the card's effect once per play (Generic.OfferRewind; versions after each rewind: Generic.RewindVersion)"),
				// HsServer 2026-10-01: keywords the client's KeywordText table lists that the old IsPowerKeywordTag filter missed
				[2456] = (Support.Supported, "Infuse: counts friendly deaths in hand, transforms in place (tests: Huntsman Altimor, Sire Denathrius)"),
				[2559] = (Support.Supported, "Corpse: Controller.Corpses, gained on friendly deaths, spent by scripts (Death Knight)"),
				[1518] = (Support.Supported, "Dormant: sleeps untouchable, awakens like just summoned"),
				[1193] = (Support.Supported, "Twinspell: <id>ts into the played slot before cast triggers, pink replacement on the wire (live 2026-10-01)"),
				[3627] = (Support.ScriptDriven, "Dark Gift: scripted per card; keeps its buff when shuffled"),
				[546] = (Support.ScriptDriven, "Adapt: AdaptTask"),
				[763] = (Support.ScriptDriven, "Recruit: RecruitTask"),
				[441] = (Support.ScriptDriven, "Jade Golem: JadeGolemTask"),
				[2247] = (Support.Supported, "Colossal: appendages summoned with the body (Generic.SummonAppendages)"),
				[2498] = (Support.Supported, "Manathirst: bonus when you have N mana crystals (Generic.Manathirst, Actions/LichKing.cs)"),
				[1749] = (Support.Supported, "Sigil: waits in the hero (Secret) zone, acts at the start of its owner's next turn (Generic.SigilPower; the shared Sigil(id, effect) helper)"),
				[2772] = (Support.Supported, "Titan: three abilities, one per turn instead of attacking (Actions/Titans.cs: options keyword 4786, hidden Charge, used pips; each ability's effect is its card's script)"),
				[3318] = (Support.Supported, "Miniaturize: a 1-Cost copy in hand (Generic.OnPlayReplacement: ReplacementsWhenPlayed; CREATED_BY_MINIATURIZE, SLUSH_TIME 1000; the Mini uses its original's script)"),
				[3399] = (Support.Supported, "Gigantify: an 8-Cost copy in hand (as Miniaturize, CREATED_BY_GIGANTIFY)"),
				[2332] = (Support.ScriptDriven, "Dredge: look at the bottom 3 cards of the deck, put one on top (Generic.Dredge)"),
				[3114] = (Support.Supported, "Excavate: ExcavateTask / Generic.Excavate, common → rare → epic → class Legendary (MAX_EXCAVATE_TIER from the class's treasures), live wire (2026-10-02)"),
				[4354] = (Support.Supported, "Prepare: PrepareTask / Generic.Prepare, option marked PowerKeyword=PREPARE, DECK_ACTION block, JAIL_907e discount + JAIL_907e02 lock"),
				[2785] = (Support.Supported, "Forge: ForgeTask / Generic.Forge, option marked PowerKeyword=FORGE, DECK_ACTION block, transforms into FORGES_INTO (live 2026-10-02)"),
				[3626] = (Support.Supported, "Imbue: upgrades the class Hero Power (Generic.Imbue / ImbueHeroPower, Actions/Imbue.cs)"),
				[1725] = (Support.Supported, "Questline: QuestlineStageTask — reward, then the next stage as a new card (QUEST_REWARD_DATABASE_ID); all 10 Stormwind Questlines (live 2026-10-01)"),
				[4028] = (Support.Supported, "Kindred: a card you played last turn shares a minion type or spell school (CurrentCoreCardsGen.Kindred, HsServer 2026-09-30)"),
				// HsServer (WILD_WEST, 2026-10-07): Actions/WildWest.cs
				[2905] = (Support.Supported, "Quickdraw: entered the hand this turn and played from hand (Generic.IsQuickdrawNow, HsTag.QUICKDRAW_PLAY, glow; starting hand off, mulligan replacements on; PlayReq 95 target; tag 2433 target drop)"),
				[389] = (Support.ScriptDriven, "Forgetful: 50% chance to attack another enemy (Forgetful trigger, KeywordOverride FORGETFUL)"),
				[2934] = (Support.Supported, "Bonus Effects: Generic.RandomBonusEffect / GiveBonusEffect / GainBonusEffects (8 keywords, the Rush rule)"),
				[3482] = (Support.Supported, "IGNORE_SUPPRESS_MILL_ANIMATION (Barrel of Sludge, Furnace Fuel): client display; their destroyed effect is Power.DestroyedTask"),
			};
	}
}
