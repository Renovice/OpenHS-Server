// HsServer addition (not upstream SabberStone): Caverns of Time (WONDERS, client set 1898) — chunk 2: the Druid, Hunter and
// Mage cards (shared pieces: WondersCardsGenHs.cs, engine rules: Actions/Wonders.cs). Every number goes through CardValues (V).
//
// The "counts as a copy" reprints (client tag 858, same current text: Loader/ReprintCards.cs CountsAsCopyOf) are NOT scripted
// under their WON id: each plays its Wild original's script. The originals whose 2021 script was stale are rewritten here under
// their own id (an HsServer definition lifts the reprint gate): AT_041 (WON_003), OG_313 (WON_009), GVG_035 (WON_011), AT_063
// (WON_024), AT_063t (WON_025), GVG_123 (WON_033), GVG_004 (WON_035), OG_087 (WON_036), OG_293 (WON_304), AT_001 (WON_341),
// AT_007 + AT_007e (WON_344; WON_344e is its twin) and LOE_105e (WON_022: which Hat comes back). King of Beasts GVG_046
// (WON_162) is already an HsServer card (WildPoolCardsGen.cs) with the old Battlecry: its current script is
// WondersC2KingOfBeasts below, for the integrator to put in that entry's place.
// Reprints whose original is current and needs nothing: WON_010 = OG_188, WON_018 = EX1_609, WON_021 = AT_062, WON_022 =
// LOE_105, WON_023 = AT_061 (+ the LOCK_AND_LOAD tag on AT_061e), WON_029 = AT_006, WON_037 = OG_090, WON_038 = GVG_007,
// WON_300 = CFM_816 (+3/+3 from the client text).
using System.Collections.Generic;
using System.Linq;
using SabberStoneCore.Actions;
using SabberStoneCore.Auras;
using SabberStoneCore.Conditions;
using SabberStoneCore.Enchants;
using SabberStoneCore.Enums;
using SabberStoneCore.Loader;
using SabberStoneCore.src.Loader;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks;
using SabberStoneCore.Tasks.SimpleTasks;
using SabberStoneCore.Triggers;

namespace SabberStoneCore.CardSets.HsServer
{
	public static partial class CurrentCoreCardsGen
	{
		/// <summary>Chromie's time portal (live HEIST 12491, 4 of 4 plays: Source 0, Targets = Chromie). Not in the recorded
		/// table (its name gives the engine no role to place it by), so the script names it; a RecordedSubSpells row wins.</summary>
		private const string WondersC2TimePortal = "GILAFX_Toki_TimePortal_ImpactOnly_FX:b4f5360300c2084469f65b383bf5c084";

		/// <summary>Chromie's Historical Epochs in the order live offers them (HEIST, 4 of 4 plays: t2, t4, t3, t — not the
		/// RelatedCards order).</summary>
		private static readonly string[] WondersC2Epochs = { "WON_041t2", "WON_041t4", "WON_041t3", "WON_041t" };

		/// <summary>Rat Sensei's four Monk Turtles (RelatedCards order; the wiki's Green / Yellow / Pink / Grey — trivia: "its Monk
		/// Turtle tokens representing the Ninja Turtles").</summary>
		private static readonly string[] WondersC2Turtles = { "WON_013t", "WON_013t2", "WON_013t3", "WON_013t4" };

		/// <summary>A live pause after a card's opening effect: ARTIFICIAL_PAUSE and SLUSH_TIME of <paramref name="ms"/> on
		/// <paramref name="source"/> (Disco 500, live SECR 5730; Chromie 1500, HEIST 12534). History only.</summary>
		private static void WondersC2Pause(Game g, IPlayable source, int ms)
		{
			if (!g.History || source == null) return;
			g.PowerHistory.Add(new SabberStoneCore.Kettle.PowerHistoryMetaData { Type = MetaDataType.ARTIFICIAL_PAUSE, Data = ms, Info = new List<int> { source.Id } });
			g.PowerHistory.Add(new SabberStoneCore.Kettle.PowerHistoryMetaData { Type = MetaDataType.SLUSH_TIME, Data = ms, Info = new List<int> { source.Id } });
		}

		/// <summary><paramref name="n"/> random cards matching <paramref name="where"/> to the hand (independent picks).</summary>
		private static void WondersC2Get(Game g, Controller c, System.Func<Card, bool> where, int n)
		{
			for (int i = 0; i < n; i++)
				if (RandomCard(g, where) is Card card) AddToHand(c, card);
		}

		/// <summary>Rat Sensei's "Add two 1/1 Turtles with Taunt to your hand": the Battlecry (<paramref name="pair"/> 0) gives the
		/// first two Monk Turtles, the Deathrattle (1) the other two — one Sensei hands out all four (DOUBT: unrecorded; the wiki
		/// names no order).</summary>
		private static ISimpleTask WondersC2TurtleTask(int pair) => new CustomTask((g, c, s, t, stack) =>
		{
			int n = V("WON_013", "turtles", 2);
			for (int i = 0; i < n; i++)
				AddToHand(c, Cards.FromId(WondersC2Turtles[(pair * n + i) % WondersC2Turtles.Length]));
		});

		/// <summary>
		/// [GVG_046] King of Beasts (3) — Taunt. Has +1 Attack for each other Beast you control. (WON_162 counts as a copy.) Wiki
		/// King of Beasts, patch 27.2.0: "Taunt. Battlecry: Gain +1 Attack for each other Beast you have." → "Has +1 Attack for each
		/// other Beast you control". A continuous self-aura (Old Murk-Eye's pattern, LegacyCardsGenHs EX1_062): the other friendly
		/// Beasts on the board (BoardZone.GetAll: no Dormant minions, no Locations); Silence ends it.
		/// INTEGRATOR: GVG_046 is already registered in WildPoolCardsGen.cs (~line 509-518, the old Battlecry + its GVG_046e
		/// Give): give that registration this Power (new CardDef(WondersC2KingOfBeasts)) in place of the Battlecry — an id can't
		/// be registered twice. DOUBT: whether live shows GVG_046e "The King: Increased Attack" for the aura (unrecorded).
		/// </summary>
		private static Power WondersC2KingOfBeasts => new Power
		{
			Aura = new AdaptiveEffect(GameTag.ATK, EffectOperator.ADD, p =>
				p.Controller.BoardZone.GetAll(m => m.Id != p.Id && m.IsRace(Race.BEAST)).Length * V("GVG_046", "attack per beast", 1))
		};

		/// <summary>
		/// Chromie's pick (live HEIST 12491-12714): META TARGET on the picked option, a NEW entity of that Epoch cast (CREATOR and
		/// 1715 = Chromie; shown big, POWER block, graveyard — CastRandomSpellTask, no death phase of its own: live's DEATHS block
		/// follows Chromie's whole PLAY block), then the other three options (new entities Chromie made: a zone move keeps them)
		/// shuffled into the deck inside Chromie's recorded SpawnToDeckQuick effect (Source = Chromie, no targets). The picked
		/// option stays set aside. DOUBT: live also moves the picked option DECK → SETASIDE before the cast (not reproduced).
		/// </summary>
		private static void WondersC2Visit(Game g, Controller c, IPlayable chromie, IPlayable picked, int[] offered)
		{
			if (g.History)
				g.PowerHistory.Add(new SabberStoneCore.Kettle.PowerHistoryMetaData { Type = MetaDataType.TARGET, Data = 0, Info = new List<int> { picked.Id } });
			IPlayable cast = Entity.FromCard(c, picked.Card, new EntityData { [GameTag.CREATOR] = chromie.Id, [(GameTag)1715] = chromie.Id });
			new CastRandomSpellTask(phaseShift: false).Process(g, c, chromie, cast);
			var others = offered.Where(id => id != picked.Id)
				.Select(id => g.IdEntityDic.TryGetValue(id, out IPlayable p) ? p : null)
				.Where(p => p != null && p.Zone?.Type == Zone.SETASIDE).ToList();
			Generic.NamedFx(chromie, "SpawnToDeck", null, () =>
			{
				foreach (IPlayable other in others)
					if (!c.DeckZone.IsFull && other.Zone?.Type == Zone.SETASIDE)
						Generic.ShuffleIntoDeck.Invoke(c, chromie, c.SetasideZone.Remove(other));
			});
		}

		private static void WondersChunk2(IDictionary<string, CardDef> cards)
		{
			// ================================================================ Druid

			// [WON_003] Knight of the Wild (7) — Costs (1) less for each Beast you've summoned this game. (Counts as a copy of
			// AT_041, rewritten here.) Wiki Knight of the Wild: reduced "whenever the controlling player summons a Beast (by any
			// means)"; patch 27.2.0 from "Whenever you summon a Beast, reduce the Cost of this card by (1)". Every Beast its owner
			// summoned this game counts, before it was drawn too (Controller.SummonedThisGame) — Frostsaber Matriarch's AV_291
			// pattern. DOUBT: whether live attaches AT_041e / WON_003e "Call of the Wild: Cost reduced." (the cost shows anyway).
			cards.Add("AT_041", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => p.Controller.SummonedThisGame.Count(x => x.IsRace(Race.BEAST)) * V("AT_041", "discount", 1))
			}));

			// [WON_009] Addled Grizzly (2) — After you summon a Beast, give it +1/+1. (Counts as a copy of OG_313, rewritten here.)
			// Wiki Addled Grizzly, patch 27.2.0: "After you summon a Beast..." (previously "...a minion..."). OG_313e +1/+1 (from
			// its client text); a WON_009 gives its twin WON_009e by itself (ReprintCards.TwinEnchantment).
			cards.Add("OG_313", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_SUMMON)
				{
					TriggerSource = TriggerSource.MINIONS_EXCEPT_SELF,
					Condition = SelfCondition.IsRace(Race.BEAST),
					SingleTask = new AddEnchantmentTask("OG_313e", EntityType.TARGET)
				}
			}));

			// [WON_011] Malorne (7) — Deathrattle: Go Dormant. Revive after 2 friendly Beasts die. (Counts as a copy of GVG_035,
			// rewritten here: wiki Malorne, patch 27.2.0 "Shuffle this minion into your deck" → "Go Dormant. Revive after 2 friendly
			// Beasts die".) The shared rule (Generic.GoDormantOnDeath, as The Rat King SW_323): a new Malorne in its slot, Dormant
			// under WON_011e "Malorne's Slumber" (the client's counter: SCORE_VALUE_1 = 2, the total; GVG_035 has no counter of its
			// own), each other friendly Beast that dies counts, then it awakens. DOUBT (unrecorded): Beasts dying in the same death
			// phase as Malorne; a full board = no revive (as SW_323).
			cards.Add("GVG_035", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => Generic.GoDormantOnDeath(c, s, "WON_011e")),
				Trigger = Generic.AwakenAfterDeaths(m => m.IsRace(Race.BEAST))
			}));

			// [WON_013] Rat Sensei (2) — Battlecry and Deathrattle: Add two 1/1 Turtles with Taunt to your hand. (WON_013t..t4:
			// four 1/1 Taunt Beasts, keywords only.)
			cards.Add("WON_013", new CardDef(new Power
			{
				PowerTask = WondersC2TurtleTask(0),
				DeathrattleTask = WondersC2TurtleTask(1)
			}));

			// [WON_014] Invigorate (2) — Choose One - Gain an empty Mana Crystal; or Draw a card. The options (the client's names;
			// the wiki swaps them): [WON_014s] Rejuvenate — Gain an empty Mana Crystal (as Mire Keeper's OG_202b); [WON_014s2]
			// Enliven — Draw a card. Invigorate itself has no effect of its own (a Choose One card: CardContainer).
			cards.Add("WON_014s", new CardDef(new Power { PowerTask = new ManaCrystalEmptyTask(V("WON_014", "crystals", 1)) }));
			cards.Add("WON_014s2", new CardDef(new Power { PowerTask = new DrawTask(V("WON_014", "draw", 1)) }));

			// [WON_015] Cenarion Hold (1) — Location. Your next Choose One card this turn has both effects combined. The shared
			// rule (Generic.NextChooseOneCardBothThisTurn: player HsTag, cards only — wiki Cenarion Hold 28.6.0 "Fixed a bug where
			// Cenarion Hold's effect could be used up by a Choose One Hero Power" — this turn only). [WON_015e] "Cenari On Hold" on
			// the player shows it until spent or the end of the turn. DOUBT: whether live also sets the client's CHOOSE_BOTH.
			cards.Add("WON_015", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.NextChooseOneCardBothThisTurn(c, (IPlayable)s, "WON_015e"))
			}));
			cards.Add("WON_015e", new CardDef(Generic.NextChooseOneCardBothShownPower));

			// [WON_304] Dark Arakkoa (6) — Taunt. Battlecry: Give your C'Thun +4/+4 (wherever it is). (Counts as a copy of OG_293,
			// rewritten here: wiki Dark Arakkoa, patch 27.2.0 +3/+3 → +4/+4.) The shared ritual (RitualTask → Generic.CthunRitual,
			// live's 2023 model); Taunt is a client tag.
			cards.Add("OG_293", new CardDef(new Power { PowerTask = new RitualTask(V("OG_293", "stats", 4)) }));

			// ================================================================ Hunter

			// [WON_022] Explorer's Hat (1) — Give a minion +1/+1 and "Deathrattle: Get an Explorer's Hat." (Counts as a copy of
			// LOE_105, whose script is current.) The Hat that comes back is the card that gave the enchantment (its creator: a WON_022
			// gives a WON_022, a LOE_105 a LOE_105; anything else an LOE_105), to the hand of whoever controls the minion when it
			// dies (wiki Explorer's Hat: "will add a copy of Explorer's Hat to the hand of whichever player controls the minion
			// when it dies"). DOUBT: unrecorded which Hat live returns (both look the same).
			cards.Add("LOE_105e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.AttackHealth_N(V("LOE_105", "stats", 1))),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					Card made = (t as Enchantment)?.Creator?.Card;
					bool hat = made != null && (made.Id == "LOE_105" || ReprintCards.CountsAsCopyOf(made.Id) == "LOE_105");
					AddToHand(c, hat ? made : Cards.FromId("LOE_105"));
				})
			}));

			// [WON_024] Acidmaw (3) — Whenever an enemy minion takes damage, destroy it. (Counts as a copy of AT_063, rewritten here:
			// wiki Acidmaw, patch 27.2.0 "another minion" → "enemy minion".) Prevented damage (Divine Shield, Immune) is no damage
			// taken (wiki Notes). Its client effect (ReuseFX_Decay_Missile_AcidGlob) flies to the damaged minion.
			cards.Add("AT_063", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TAKE_DAMAGE)
				{
					TriggerSource = TriggerSource.OP_MINIONS,
					SingleTask = new DestroyTask(EntityType.TARGET)
				}
			}));

			// [WON_025] Dreadscale (3) — At the end of your turn, deal 1 damage to all enemies. (Counts as a copy of AT_063t,
			// rewritten here: wiki Dreadscale, patch 27.2.0 "all other minions" → "all enemies" — the enemy hero too.)
			cards.Add("AT_063t", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = new DamageTask(V("AT_063t", "damage", 1), EntityType.ENEMIES) }
			}));

			// [WON_026] Durnholde Imposter (3) — Each turn this is in your hand, transform it into a random 3-Cost minion that
			// gains Poisonous. [WON_039] Black Morass Imposter (2) — … a random 2-Cost minion that gains Spell Damage +1. The shared
			// Imposter (WondersImposter: the card's hand trigger, "<id>e" Shifting with DEFINING_ENCHANTMENT → the Imposter, "<id>e2"
			// the keyword kept on play; Generic.ShiftInHand). DOUBT (A2): SHIFTING_MINION vs SHIFTING; the e2's order on the wire.
			WondersImposter(cards, "WON_026");
			WondersImposter(cards, "WON_039");

			// [WON_027] Time-Lost Raptor (2) — Echo. Battlecry: Adapt your Time-Lost Raptors. One Adapt choice for every friendly
			// minion on the board with its name, itself included (Gentle Megasaur's UNG_089 pattern: AdaptTask over the stack). Echo
			// copies in hand are not minions in play. DOUBT: the wiki is silent on Raptors outside the board (not adapted here).
			cards.Add("WON_027", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					new IncludeTask(EntityType.MINIONS),
					new FilterStackTask(EntityType.SOURCE, new RelaCondition((me, other) => other.Card.Name == me.Card.Name)),
					new AdaptTask(EntityType.STACK))
			}));

			// [WON_028] Trial of the Jormungars (6) — Summon copies of two Beasts in your deck that cost (3) or less. Two different
			// deck Beasts picked at random (a hidden zone), each summoned as a copy (Generic.SummonCopiesFromDeck; the deck is
			// unchanged). Live (INST 62733-62738, cast by Yogg-Saron): SUB_SPELL ReuseFX_Nature_DeckAE_DeckBuff_Super (Source 0, no
			// targets), then ReuseFX_Beast_OverrideSpawn_Paw_Super (Source = the hero) around the summons — both recorded
			// (RecordedSubSpells.tsv), placed by the script in that order (OwnFx: the buff effect would otherwise wait for a buff).
			cards.Add("WON_028", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var trial = (IPlayable)s;
					Generic.NamedFx(trial, "DeckBuff", null, () => { });
					Generic.NamedFx(trial, "OverrideSpawn", null, () =>
						Generic.SummonCopiesFromDeck(c, s, p => IsRaceCard(p, Race.BEAST) && p.Cost <= V("WON_028", "cost", 3), V("WON_028", "beasts", 2)));
				}),
				OwnFx = true
			}));

			// ================================================================ Mage

			// [WON_033] Soot Spewer (3) — Spell Damage +1. Battlecry: If you control another Mech, get a random Fire spell. (Counts
			// as a copy of GVG_123, rewritten here: wiki Soot Spewer, patch 27.2.0 added the Battlecry.) Spell Damage +1 is its
			// client tag; the Fire spell is any class's; its recorded to-hand effect (WON_033 ReuseFX_Fire_SpawnToHand_Motes_Super)
			// serves GVG_123 too (SubSpells: ReprintCards.Variants).
			cards.Add("GVG_123", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c.BoardZone.GetAll(m => m.Id != s.Id && m.IsRace(Race.MECHANICAL)).Length == 0) return;
					if (RandomCard(g, x => x.Type == CardType.SPELL && x[SPELL_SCHOOL] == FIRE) is Card fire) AddToHand(c, fire);
				})
			}));

			// [WON_035] Goblin Blastmage (4) — Battlecry: If you control a Mech, deal 6 damage randomly split among all enemies.
			// (Counts as a copy of GVG_004, rewritten here: wiki Goblin Blastmage, patch 27.2.0 4 → 6.) Each missile resolves before
			// the next (wiki Notes: "Each missile is resolved ... before the next one is shot"): EnqueueTask, one missile each.
			cards.Add("GVG_004", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					new ConditionTask(EntityType.SOURCE, SelfCondition.IsControllingRace(Race.MECHANICAL)),
					new FlagTask(true, new EnqueueTask(V("GVG_004", "damage", 6), ComplexTask.DamageRandomTargets(1, EntityType.ENEMIES, 1))))
			}));

			// [WON_036] Servant of Yogg-Saron (5) — Battlecry: Cast a random spell that costs (5) or MORE (targets chosen randomly).
			// (Counts as a copy of OG_087, rewritten here: wiki, patch 27.2.0 "(5) or less" → "(5) or MORE".) Only valid targets;
			// none → it fizzles (wiki Notes; CastRandomSpellTask).
			cards.Add("OG_087", new CardDef(new Power { PowerTask = new CastRandomSpellTask(x => x.Cost >= V("OG_087", "cost", 5)) }));

			// [WON_040] Disco at the End of Time (6) — Cast 5 random Secrets from the past. At the start of your turn, destroy them.
			// Live (SECR 5703-5868, 6632-6697): the recorded Confetti effect (Source 0, Targets = your hero), ARTIFICIAL_PAUSE /
			// SLUSH_TIME 500, [WON_040e] "End of the Disco" on the player (CREATOR = Disco), then five Secrets of any class from
			// outside the current Standard rotation PUT into the Secret zone (not cast: no spell counters; Generic.PutRandomSecrets,
			// the one-copy rule, stops when full). They are marked (Generic.MarkSecretsForDestroy, the rule state on the Secrets);
			// at its owner's next turn start WON_040e destroys those still up without a reveal (Generic.DestroyMarkedSecretsPower).
			cards.Add("WON_040", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var disco = (IPlayable)s;
					Generic.NamedFx(disco, "Confetti", new[] { c.Hero.Id }, () => { });
					WondersC2Pause(g, disco, 500);
					Generic.AddEnchantmentBlock(g, Cards.FromId("WON_040e"), disco, c, 0, 0, 0);
					Generic.MarkSecretsForDestroy(Generic.PutRandomSecrets(c, disco, Cards.FromThePast, V("WON_040", "secrets", 5)), disco);
				}),
				OwnFx = true
			}));
			cards.Add("WON_040e", new CardDef(Generic.DestroyMarkedSecretsPower));

			// [WON_041] Chromie, Timehopper (6) — Battlecry: Visit a Historical Epoch. Shuffle the others into your deck. Wiki
			// Chromie, Timehopper Notes: "The player selects one of four Historical Epoch spells to cast immediately. The remaining
			// three are shuffled into the deck and cast upon being drawn." Live (HEIST 12491-12714, 4 plays): the time portal at
			// Chromie (Source 0), ARTIFICIAL_PAUSE / SLUSH_TIME 1500, the four Epochs made in SETASIDE (CREATOR / DISPLAYED_CREATOR =
			// Chromie, REVEALED) and offered in a GENERAL choice (no WAS_DISCOVER_OPTION: not a Discover), then the pick
			// (WondersC2Visit). WON_041e "Runback" (no text) was never sent: not scripted. OwnFx: Chromie's recorded SpawnToDeck
			// effect wraps only the shuffle, not the whole Battlecry.
			cards.Add("WON_041", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is IPlayable chromie)) return;
					string portal = SabberStoneCore.Loader.SubSpells.FindNamed(chromie.Card.Id, "TimePortal") ?? WondersC2TimePortal;
					if (g.OpenSubSpell(chromie, portal, new[] { chromie.Id }, shownSource: Game.NoSubSpellSource))
						g.CloseSubSpells(chromie.Id);
					WondersC2Pause(g, chromie, 1500);
					List<IPlayable> offered = OfferCards(c, chromie, WondersC2Epochs);
					foreach (IPlayable epoch in offered)
						epoch[GameTag.REVEALED] = 1;
					int[] offeredIds = offered.Select(p => p.Id).ToArray(); // ids, not entities: an AI copy of the game runs this too
					Generic.CreateChoiceEntities(c, chromie, offered, ChoiceAction.STACK, new CustomTask((g2, c2, s2, picked, st2) =>
					{
						if (picked != null && s2 is IPlayable me) WondersC2Visit(g2, c2, me, picked, offeredIds);
					}));
				}),
				OwnFx = true
			}));
			// The Epochs — each "Casts When Drawn" (the client text marker: Draw.OnDrawn sets it aside, casts it, draws the
			// replacement, TRIGGER block with TriggerKeyword CASTS_WHEN_DRAWN) and the same effect when Chromie casts it.
			// [WON_041t] Opening the Dark Portal (4) — Casts When Drawn. Get two random Legendary minions. (Any class; live HEIST
			// 13729 gave OG_122 and WC_030.)
			ISimpleTask darkPortal = new CustomTask((g, c, s, t, stack) =>
				WondersC2Get(g, c, x => x.Type == CardType.MINION && x.Rarity == Rarity.LEGENDARY, V("WON_041t", "minions", 2)));
			cards.Add("WON_041t", new CardDef(new Power { PowerTask = darkPortal, TopdeckTask = darkPortal }));
			// [WON_041t2] Battle for Mount Hyjal (4) — Casts When Drawn. Give your minions +2/+2. (Live HEIST 22984: WON_041t2e on
			// each friendly minion, one META TARGET each — RecordedBlockMeta "WON_041t2 TRIGGER 0 TARGETS".)
			ISimpleTask hyjal = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.BoardZone.GetAll(), "WON_041t2e"));
			cards.Add("WON_041t2", new CardDef(new Power { PowerTask = hyjal, TopdeckTask = hyjal }));
			cards.Add("WON_041t2e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("WON_041t2", "stats", 2))) }));
			// [WON_041t3] Escape from Durnholde (4) — Casts When Drawn. Get two random spells. (Any class.) DOUBT: live (HEIST
			// 15518, 2 of 2) also plays DEDFX_JerryRigCarpenter_DrawAndHold_Super (META HOLD_DRAWN_CARD + TARGET = itself) BEFORE
			// the card is set aside; Draw.OnDrawn sets it aside before the script runs, so it is not reproduced here.
			ISimpleTask durnholde = new CustomTask((g, c, s, t, stack) =>
				WondersC2Get(g, c, x => x.Type == CardType.SPELL, V("WON_041t3", "spells", 2)));
			cards.Add("WON_041t3", new CardDef(new Power { PowerTask = durnholde, TopdeckTask = durnholde }));
			// [WON_041t4] Culling of Stratholme (4) — Casts When Drawn. Deal $2 damage to all enemies. (Live HEIST 12639: 2 to the
			// enemy hero and each enemy minion; "$": Spell Damage counts.)
			ISimpleTask culling = new DamageTask(V("WON_041t4", "damage", 2), EntityType.ENEMIES, true);
			cards.Add("WON_041t4", new CardDef(new Power { PowerTask = culling, TopdeckTask = culling }));

			// [WON_341] Flame Lance (5) — Deal $25 damage to a minion. (Counts as a copy of AT_001, rewritten here: patch 27.2.0
			// 8 → 25.)
			cards.Add("AT_001", new CardDef(TargetMinion, new Power { PowerTask = new DamageTask(V("AT_001", "damage", 25), EntityType.TARGET, true) }));

			// [WON_344] Spellslinger (3) — Battlecry: Both players get a random spell. Yours costs (2) less. (Counts as a copy of
			// AT_007, rewritten here: wiki Spellslinger, patch 27.2.0 from "Add a random spell to each player's hand".) Any class;
			// yours first (the 2021 order; unrecorded), with [AT_007e] "Spellslung: Costs (2) less." — a WON_344 gives its twin
			// WON_344e (client tag 858 → AT_007e) by itself.
			cards.Add("AT_007", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (RandomCard(g, x => x.Type == CardType.SPELL) is Card mine && !c.HandZone.IsFull)
					{
						IPlayable yours = Entity.FromCard(c, mine);
						if (Generic.AddHandPhase.Invoke(c, yours) && yours.Zone?.Type == Zone.HAND) Give(g, s, yours, "AT_007e");
					}
					if (RandomCard(g, x => x.Type == CardType.SPELL) is Card theirs) AddToHand(c.Opponent, theirs);
				})
			}));
			cards.Add("AT_007e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("AT_007", "discount", 2))) }));
		}
	}
}
