// HsServer addition (not upstream SabberStone): TITANS (client set 1858) with Fall of Ulduar — chunk 4: Shaman, Warlock and
// Warrior cards (Khaz'goroth, Golganneth, Sargeras and their abilities; Odyn, Thorim, Loken, General Vezax…). Shared pieces:
// Actions/Titans.cs (Titans, Armor paid / lost, Overload unlocked, "if you play it this turn", removal from the game, the
// damage-taken counter) and CardSets/HsServer/TitansCardsGenHs.cs (TitansTitan / TitansAbility / TitansNextTurnCost). Every
// number goes through CardValues (V). Research: scratchpad sets/TITANS/spec_chunk4.md.
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
		/// <summary>Sargeras' portal (the card his Battlecry opens; his abilities work through it).</summary>
		private const string TitansC4TwistingNether = "TTN_960t";

		/// <summary>The player's Twisting Nethers in play (wiki Sargeras: "Any friendly Twisting Nether is enough … even if it was
		/// summoned by a different copy"; "Prior to Patch 27.4.0.185749, Sargeras could also interact with enemy Twisting Nethers.
		/// This is no longer the case").</summary>
		private static List<Minion> TitansC4Portals(Controller c) =>
			// (a permanent: untouchable, so the whole board — enumerating it leaves untouchables out, rule BE2)
			c.BoardZone.GetSpan().ToArray().Where(m => m.Card.Id == TitansC4TwistingNether && !m.IsDead && !m.ToBeDestroyed).ToList();

		/// <summary>The portal a Sargeras' ability works through: the one he opened (HsTag.OPENED_PERMANENT) while it is in play
		/// (wiki: "both copies of Sargeras will prioritize their own portal"), else any friendly one; null for none (wiki: "if
		/// Sargeras is not played but summoned, its abilities will be usable but they will do nothing without The Twisting
		/// Nether"; "No matter how many Twisting Nethers are in play, Sargeras' abilities only trigger once").</summary>
		private static Minion TitansC4Portal(Controller c, IEntity titan)
		{
			List<Minion> portals = TitansC4Portals(c);
			int own = titan?[HsTag.OPENED_PERMANENT] ?? 0;
			return portals.FirstOrDefault(p => p.Id == own) ?? portals.FirstOrDefault();
		}

		/// <summary>
		/// <paramref name="n"/> minions of <paramref name="demon"/> "from the Twisting Nether": summoned by the portal (beside it,
		/// left then right: the Nether Portal UNG_829t2 pattern, Generic.BeginSummonGroup); after Legion Invasion! on this portal
		/// (its HsTag.PORTAL_DEMONS_EMPOWERED, once per use) each Demon gets "Fel Fueled" TTN_960t4e (+2 Health and Taunt).
		/// </summary>
		private static void TitansC4SummonFromPortal(Game g, Controller c, Minion portal, Card demon, int n)
		{
			if (portal == null || demon == null || n <= 0) return;
			Generic.BeginSummonGroup(g, portal, n);
			for (int i = 0; i < n && !c.BoardZone.IsFull; i++)
			{
				var m = (Minion)Entity.FromCard(c, demon);
				Generic.SummonBlock.Invoke(g, m, -1, portal);
				if (m.Zone?.Type != Zone.PLAY || !m.IsRace(Race.DEMON)) continue;
				for (int k = 0; k < portal[HsTag.PORTAL_DEMONS_EMPOWERED]; k++)
					Give(g, portal, m, "TTN_960t4e");
			}
		}

		/// <summary>"Summon a &lt;token&gt; with its stats" that also copies the Cost (Loken: wiki "The summoned Tentacle also copies
		/// the cost of the chosen card"; live-20261007-vfx-decks 108973-109200: EDR_654 3/5 cost 4 → Tentacle 3/5 cost 4). The
		/// summoned minion, or null.</summary>
		private static Minion TitansC4SummonWithStats(Game g, Controller c, Card card, int attack, int health, int cost, IEntity source)
		{
			if (card == null || c.BoardZone.IsFull) return null;
			var m = (Minion)Entity.FromCard(c, card, new EntityData { [GameTag.ATK] = attack, [GameTag.HEALTH] = health, [GameTag.COST] = cost });
			Generic.SummonBlock.Invoke(g, m, -1, source);
			return m.Zone?.Type == Zone.PLAY ? m : null;
		}

		/// <summary>A step of a Titan ability inside the ability card's own effect named <paramref name="part"/>, played from
		/// nowhere with what it hits collected as its targets (live Roaring Oceans, live-20261007-vfx-decks 305087-305160:
		/// TTNFX_RoaringOceans_BoardAE_Super then TTNFX_RoaringOceans_Heal_Impact_Super, Source=0, Targets=[], META TARGET = what
		/// each one hit / healed). No such effect: just the step.</summary>
		private static void TitansC4AbilityStep(Game g, IPlayable titan, string abilityId, string part, System.Action action)
		{
			string prefab = g.History ? SabberStoneCore.Loader.SubSpells.FindNamed(abilityId, part) : null;
			bool open = prefab != null && g.OpenSubSpell(titan, prefab, null, Game.NoSubSpellSource, collectTargets: true);
			action();
			if (open) g.CloseSubSpells(titan.Id);
		}

		private static void TitansChunk4(IDictionary<string, CardDef> cards)
		{
			// ================================================================ Shaman

			// "Discover a Nature spell" (the rule's default pool: your class or Neutral; client SPELL_SCHOOL 4)
			DiscoverFromTask DiscoverNature(ISimpleTask afterPick = null) =>
				DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.SPELL && x[SPELL_SCHOOL] == NATURE, null, afterPick);

			// [TTN_317] Lightning Reflexes (1) — Discover a Nature spell. If you play it this turn, Discover another.
			// Live (live-20261001-questline 1942-2102, 6678-7083): the pick gets the "Play This Turn" mark (NEW_1308e + its highlight:
			// Generic.MarkPlayThisTurn); TTN_317e goes on the player with TAG_SCRIPT_DATA_NUM_1 = the pick; when that card is played
			// this turn, at the end of its PLAY block, TRIGGER on TTN_317e (effect index 0, TriggerKeyword DISCOVER) Discovers
			// another Nature spell (CREATOR = TTN_317e; the second pick isn't marked). TTN_317e stays until the turn ends (its
			// TAG_ONE_TURN_EFFECT).
			cards.Add("TTN_317", new CardDef(new Power
			{
				PowerTask = DiscoverNature(new CustomTask((g, c, s, t, stack) =>
				{
					if (t == null || t.Zone?.Type != Zone.HAND) return;
					Generic.MarkPlayThisTurn(t, s);
					Generic.AddEnchantmentBlock(g, Cards.FromId("TTN_317e"), (IPlayable)s, c, t.Id, 0, 0);
				}))
			}));
			// [TTN_317e] Lightning Reflexes — If played this turn, Discover a Nature spell. (The player's mark; NUM_1 = the card.)
			cards.Add("TTN_317e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_CARD)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					KeywordOverride = GameTag.DISCOVER,
					// only the marked card (no empty blocks for the player's other cards)
					Condition = new SelfCondition(p => p.Controller.AppliedEnchantments?.Any(e =>
						e.Card.Id == "TTN_317e" && e[GameTag.TAG_SCRIPT_DATA_NUM_1] == p.Id) == true),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						IPlayable played = t ?? g.CurrentEventData?.EventSource as IPlayable;
						if (!(s is Enchantment mark) || played == null || mark[GameTag.TAG_SCRIPT_DATA_NUM_1] != played.Id) return;
						DiscoverNature().Process(g, c, mark, null);
					})
				}
			}));

			// [TTN_722] Turn the Tides (3) — Give your hero +3 Attack this turn. Summon a 3/3 Elemental with Rush. Overload: (1)
			// Live (live-20261007-vfx-decks 68846-68930): the hero gets TTN_833e1 "Wavecrash" (+3 Attack this turn), then TTN_833t
			// "Waveling" (the card's COLLECTION_RELATED card) is summoned; the Overload is the engine's (its POWER block, effect
			// index 1). The server-picked ReuseFX_Water_BuffImpact_Heal_Super / ReuseFX_Water_OverrideSpawn_Hop_Super come from
			// the recorded tables (TaskProcessor's buff effect, Summon's spawn effect) once they are refreshed.
			cards.Add("TTN_722", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					new AddEnchantmentTask("TTN_833e1", EntityType.HERO),
					new SummonTask("TTN_833t", SummonSide.SPELL))
			}));
			// [TTN_833e1] Wavecrash — +3 Attack this turn.
			cards.Add("TTN_833e1", OneTurn(Effects.Attack_N(V("TTN_722", "attack", 3))));

			// [TTN_725] Tempest Hammer (4) — After your hero attacks, deal 3 damage to the lowest Health enemy. (Shared
			// LowestHealthEnemy: Health only, ties at random, the enemy hero included; the weapon is the source.)
			cards.Add("TTN_725", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (LowestHealthEnemy(g, c) is ICharacter lowest)
							Generic.DamageCharFunc((IPlayable)s, lowest, V("TTN_725", "damage", 3), false);
					})
				}
			}));

			// [TTN_727] Thorignir Drake (7) — Rush. Whenever this attacks, summon two 3/1 Whelps to attack the target first.
			// In the attack's preparation (ATTACK trigger, Combat Preparation): two Juvenile Thorignir TTN_727t beside the Drake,
			// each makes an effect attack on the Drake's defender while it lives (an effect attack: Taunt and summoning sickness
			// don't matter). A defender killed by them cancels the Drake's attack (rule book Advanced_rulebook.txt:644 "If the
			// attack is cancelled …"; Actions/Attack.cs). Community (vicioussyndicate TITANS preview, via search): "if the enemy
			// doesn't die from the whelp attacks, the main body will attack the target as well". The Drake is the proposed attacker
			// again after them (the client's swing).
			cards.Add("TTN_727", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ATTACK)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Minion drake) || drake.Zone?.Type != Zone.PLAY || drake.ToBeDestroyed ||
						    !(g.CurrentEventData?.EventTarget is ICharacter defender) || defender.IsDead)
							return;
						int n = V("TTN_727", "whelps", 2);
						var whelps = new List<Minion>();
						Generic.BeginSummonGroup(g, drake, n);
						for (int i = 0; i < n && !c.BoardZone.IsFull; i++)
						{
							var whelp = (Minion)Entity.FromCard(c, Cards.FromId("TTN_727t"));
							Generic.SummonBlock.Invoke(g, whelp, -1, drake);
							if (whelp.Zone?.Type == Zone.PLAY) whelps.Add(whelp);
						}
						foreach (Minion whelp in whelps)
						{
							if (defender.IsDead || defender.Zone?.Type != Zone.PLAY && !(defender is Hero)) break;
							if (whelp.Zone?.Type != Zone.PLAY || whelp.IsDead) continue;
							EffectAttack(g, whelp, defender);
						}
						if (g.History && drake.Zone?.Type == Zone.PLAY && !defender.IsDead && g.ProposedAttacker != drake.Id)
						{
							g.ProposedAttacker = drake.Id;
							g.ProposedDefender = defender.Id;
						}
					})
				}
			}));

			// [TTN_800] Golganneth, the Thunderer (6) — Titan. Your first spell each turn costs (3) less.
			// The aura (Love Everlasting's ETC_335e rule): spells in hand cost (3) less while no spell was played this turn, shown
			// as TTN_800e2 "Storm of the Titan" on each (live 71576-71720: on play TTN_800e2 on every spell in hand, CREATOR =
			// Golganneth; all leave when the first spell is played). TTN_800e1 "Golganneth's Presence" is unused (not in live).
			// Abilities (client RelatedCards order): Lord of Skies, Shargahn's Wrath, Roaring Oceans; no after-ability text (the
			// engine sends the empty TRIGGER block: its voice line).
			cards.Add("TTN_800", TitansTitan(new Power
			{
				Aura = new Aura(AuraType.HAND, Effects.ReduceCost(V("TTN_800", "discount", 3)))
				{
					Condition = new SelfCondition(p => p is Spell && !p.Controller.CardsPlayedThisTurn.Any(x => x.Type == CardType.SPELL)),
					Restless = true,
					ShowAs = "TTN_800e2"
				}
			}));
			// [TTN_800t2] Lord of Skies — Deal 20 damage to another minion. (Any minion but Golganneth; live with no other minion:
			// REQ_TARGET_TO_PLAY, live 290440-290480. The engine wraps it in the ability's TTNFX_LordOfSkies_Missile_Idle_Super from
			// the Titan.)
			cards.Add("TTN_800t2", TitansAbility(new CustomTask((g, c, s, t, stack) =>
				{
					if (s is IPlayable titan && t is ICharacter target)
						Generic.DamageCharFunc(titan, target, V("TTN_800t2", "damage", 20), false);
				}),
				new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_NONSELF_TARGET, 0 } }));
			// [TTN_800t3] Shargahn's Wrath — Draw 3 Overload cards. (Random ones from the deck with their own "Overload: (N)":
			// Card.HasOverload. Live 71557-71890: all three draws inside TTNFX_ShargahnsWrath_SpawnFromDeck_Super from Golganneth.)
			cards.Add("TTN_800t3", TitansAbility(new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is IPlayable titan)) return;
					Generic.AbilityFx(titan, Cards.FromId("TTN_800t3"), "ShargahnsWrath", null,
						() => DrawWhere(g, c, p => p.Card.HasOverload, V("TTN_800t3", "cards", 3)));
				}), ownFx: true, notInHistory: true)); // (live 71778 / 304257: no ShowInHistory)
			// [TTN_800t] Roaring Oceans — Deal 3 damage to all enemies and restore 6 Health to all friendly characters. (Golganneth
			// and the hero included; live 305087-305160: the damage inside TTNFX_RoaringOceans_BoardAE_Super, then the heals inside
			// TTNFX_RoaringOceans_Heal_Impact_Super, both from nowhere.)
			cards.Add("TTN_800t", TitansAbility(new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is IPlayable titan)) return;
					TitansC4AbilityStep(g, titan, "TTN_800t", "BoardAE",
						() => new DamageTask(V("TTN_800t", "damage", 3), EntityType.ENEMIES).Process(g, c, titan, null));
					TitansC4AbilityStep(g, titan, "TTN_800t", "Heal_Impact",
						() => new HealTask(V("TTN_800t", "heal", 6), EntityType.FRIENDS).Process(g, c, titan, null));
				}), ownFx: true, notInHistory: true)); // (live 305086: no ShowInHistory)

			// [TTN_830] Flash of Lightning (2) — Draw a card. Next turn, your Nature spells cost (1) less.
			// TTN_830e on the player turns the discount on at the start of your next turn (Nature spells that arrive that turn too:
			// vicioussyndicate preview "as an aura effect applying to discovered spells too") and leaves at that turn's end; the
			// cards show TTN_830e2 "Flash of Lightning Discount" (TitansNextTurnCost; which enchantment sits where is unrecorded).
			cards.Add("TTN_830", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new DrawTask(), new AddEnchantmentTask("TTN_830e", EntityType.CONTROLLER))
			}));
			cards.Add("TTN_830e", TitansNextTurnCost(Effects.ReduceCost(V("TTN_830", "discount", 1)),
				new SelfCondition(p => IsSchoolSpell(p, NATURE)), "TTN_830e2"));

			// [TTN_831] Crash of Thunder (5) — Deal $3 damage to all enemies. Costs (1) less for each Nature spell you've cast this
			// turn. (The Nature spells the player played this game turn: PlayHistory.)
			cards.Add("TTN_831", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => V("TTN_831", "discount", 1) * p.Controller.PlayHistory.Count(h =>
					h.Turn == p.Game.Turn && h.SourceCard?.Type == CardType.SPELL && h.SourceCard[SPELL_SCHOOL] == NATURE)),
				PowerTask = new DamageTask(V("TTN_831", "damage", 3), EntityType.ENEMIES, true)
			}));

			// [TTN_833] Disciple of Golganneth (2) — At the end of your turn, reduce the Cost of a random Overload card in your hand
			// by (1). (A card with its own "Overload: (N)": Card.HasOverload. Live 70304-70330: TRIGGER effect index 0, the
			// enchantment TTN_833e "Shocking" inside ReuseFX_Generic_HandBuff_ScaleUp_OpponentAE_Blue_Super from the Disciple at
			// the card — the named effect once the recorded tables are refreshed.)
			cards.Add("TTN_833", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						var pool = c.HandZone.Where(p => p.Card.HasOverload).ToList();
						if (pool.Count == 0) return;
						IPlayable card = pool[g.Random.Next(pool.Count)];
						Generic.NamedFx((IPlayable)s, "HandBuff", new[] { card.Id }, () => Give(g, s, card, "TTN_833e"));
					})
				}
			}));
			// [TTN_833e] Shocking — Costs (1) less.
			cards.Add("TTN_833e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("TTN_833", "discount", 1))) }));

			// [TTN_835] Thorim, Stormlord (3) — Battlecry: Unlock your Overloaded Mana Crystals. Draw that many cards.
			// Rule book (Advanced_rulebook.txt:1071): "Unlocking Overloaded Mana Crystals … will set OVERLOAD_OWED to 0, decrease
			// OVERLOAD_LOCKED to 0, and decrease RESOURCES_USED by the same amount" (Generic.UnlockOverload). Live
			// (live-20261007-vfx-decks 84808, 289682, 295981): locked + owed counted, the draws first (Fatigue from an empty deck),
			// the tags last.
			cards.Add("TTN_835", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = Generic.OverloadToUnlock(c);
					for (int i = 0; i < n; i++) Generic.Draw(c);
					Generic.UnlockOverload(c);
				})
			}));

			// [YOG_523] Infested Watcher (4) — Taunt. Deathrattle: Get two 1/1 Chaotic Tendrils. (Their level: YOG_514's own
			// HandCounter, the player's TENDRIL_IMPROVEMENTS.)
			cards.Add("YOG_523", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("YOG_523", "tendrils", 2); i++) AddToHand(c, Cards.FromId("YOG_514"));
				})
			}));

			// [YOG_524] Shock Hopper (1) — Battlecry: Get a random Overload card. (Any class of the game's format with its own
			// "Overload: (N)"; live picks CORE_EX1_238, EX1_241, BRM_012, END_028, SW_033, DAL_433. The to-hand effect
			// ReuseFX_Lightning_SpawnToHand_Super is the engine's from the recorded table.)
			cards.Add("YOG_524", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (RandomCard(g, card => card.HasOverload) is Card pick) AddToHand(c, pick);
				})
			}));

			// ================================================================ Warlock

			// [TTN_456] Thornveil Tentacle (2) — Lifesteal. Battlecry: Deal 2 damage to a random enemy minion. (A living enemy
			// minion that can be hit — not a permanent / Dormant one; the Tentacle is the source, so its Lifesteal heals. Live
			// 113367-113416: the damage and the Lifesteal inside ReuseFX_Shadow_Missile_Blast_Small_Super_Simultaneous from the
			// Tentacle — the engine's per-hit missile once the recorded table has it. TTN_456e / TTN_456e2 are unused leftovers.)
			cards.Add("TTN_456", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Minion[] pool = c.Opponent.BoardZone.GetAll(m => !m.IsDead && !m.ToBeDestroyed);
					if (pool.Length > 0)
						Generic.DamageCharFunc((IPlayable)s, pool[g.Random.Next(pool.Length)], V("TTN_456", "damage", 2), false);
				})
			}));

			// [TTN_460] Mortal Eradication (3) — Deal $5 damage randomly split among all enemy minions. Restore #2 Health to your
			// hero for each killed. (Split like Arcane Missiles: each hit a random living enemy minion, Spell Damage adds hits;
			// "killed" = the enemy minions alive before it that it left dead; one heal of 2 per kill.)
			cards.Add("TTN_460", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					List<Minion> alive = c.Opponent.BoardZone.GetAll(m => !m.IsDead).ToList();
					new EnqueueTask(V("TTN_460", "damage", 5), ComplexTask.DamageRandomTargets(1, EntityType.OP_MINIONS, 1), true)
						.Process(g, c, s, t);
					int killed = alive.Count(m => m.IsDead);
					if (killed > 0) c.Hero.TakeHeal((IPlayable)s, V("TTN_460", "heal", 2) * killed);
				})
			}));

			// [TTN_462] Imprisoned Horror (9) — Costs (1) less for each damage you've taken on your turns this game. (The player's
			// client tag 3110, Generic.DAMAGE_TAKEN_ON_OWN_TURNS: the amount, Armor-absorbed damage included — wiki: "this does not
			// count the number of times … but the amount of damage".)
			cards.Add("TTN_462", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => V("TTN_462", "per damage", 1) * p.Controller[Generic.DAMAGE_TAKEN_ON_OWN_TURNS])
			}));

			// [TTN_463] Wing Welding (4) — Discard your highest Cost card. Deal damage equal to its Cost to all minions. (Its Cost in
			// hand; ties at random; Spell Damage adds. Recorded (session-20260929-183021): the discard first, then
			// ReuseFX_Fire_AE_Shockwave_Small_BothSides_Super from the hero around the damage only.)
			cards.Add("TTN_463", new CardDef(new Power
			{
				OwnFx = true,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					List<IPlayable> hand = c.HandZone.ToList();
					if (hand.Count == 0) return;
					int top = hand.Max(p => p.Cost);
					List<IPlayable> highest = hand.Where(p => p.Cost == top).ToList();
					IPlayable discarded = highest[g.Random.Next(highest.Count)];
					int damage = discarded.Cost;
					new DiscardTask(EntityType.TARGET).Process(g, c, s, discarded);
					Generic.WholeFx((IPlayable)s, () => new DamageTask(damage, EntityType.ALLMINIONS, true).Process(g, c, s, null));
				})
			}));

			// [TTN_465] Forge of Wills (3, Location) — Choose a friendly minion. Summon a Giant with its stats and Rush. (The Ironbound
			// Giant TTN_465t has Rush; summoned by the Location. DOUBT, unsourced — the wiki page has no note on it: "its stats" read
			// as its current Attack and Health (not max Health), the Cost not copied, the Giant placed beside the Location;
			// live-checks.)
			cards.Add("TTN_465", new CardDef(LocationFriendlyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is Minion chosen) SummonWithStats(g, c, Cards.FromId("TTN_465t"), chosen.AttackDamage, chosen.Health, s);
				})
			}));

			// [TTN_486] Monstrous Form (1) — Give a friendly minion +3/+3 until your next turn.
			cards.Add("TTN_486", new CardDef(TargetFriendlyMinion, new Power { PowerTask = new AddEnchantmentTask("TTN_486e", EntityType.TARGET) }));
			// [TTN_486e] Gift of Flesh — +3/+3 until next turn. (Leaves at the start of the caster's next turn.)
			cards.Add("TTN_486e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.AttackHealth_N(V("TTN_486", "stats", 3))),
				Trigger = new Trigger(TriggerType.TURN_START) { SingleTask = RemoveEnchantmentTask.Task }
			}));

			// [TTN_487] Loken, Jailer of Yogg-Saron (6) — Battlecry: Discover a minion from your deck. Summon a Tentacle with its
			// stats and Taunt. Discover from the deck draws the pick (wiki Discover; live 108973-109200: the pick drawn, then the
			// Escaped Tentacle TTN_487t with its Attack, Health and Cost — wiki Loken: "also copies the cost").
			cards.Add("TTN_487", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					Generic.DiscoverFromDeck(c, s, Generic.PickFromDeck(c, p => p is Minion), (g2, c2, s2, card) =>
					{
						int attack = card is Minion m ? m.AttackDamage : card.Card.ATK;
						int health = card is Minion h ? h.Health : card.Card.Health;
						int cost = card.Cost;
						Generic.Draw(c2, card);
						TitansC4SummonWithStats(g2, c2, Cards.FromId("TTN_487t"), attack, health, cost, s2);
					}))
			}));

			// [TTN_932] Chaotic Consumption (1) — Destroy a friendly minion to destroy an enemy minion.
			// Both are the player's picks (wiki: "Both the friendly minion and the enemy minion can be chosen by the player"; user
			// rule: "a minion" = a pick): the spell's target is the friendly minion, then a second targeting arrow
			// (Generic.PickTargets) picks an enemy minion the spell could target; then the friendly one is destroyed, then the
			// enemy one. Not castable without an enemy minion. TTN_932e "Shattered" is an unused leftover.
			cards.Add("TTN_932", new CardDef(new Dictionary<PlayReq, int>
				{
					{ PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 }, { PlayReq.REQ_MINION_TARGET, 0 },
					{ PlayReq.REQ_MINIMUM_ENEMY_MINIONS, 1 }
				}, new Power
				{
					// playable only with an enemy minion the spell could pick (Generic.Pickable by a spell; the client's
					// REQ_MINIMUM_ENEMY_MINIONS counted untouchables and Stealth too — wiki patch 32.0.0.217964: "Fixed a bug where
					// Chaotic Consumption wouldn't be playable in certain edge situations")
					PlayableIf = (PlayReq.REQ_MINIMUM_ENEMY_MINIONS, ctl => Generic.HasPickableEnemyMinion(ctl, true)),
					PowerTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(t is Minion friendly)) return;
						int friendlyId = friendly.Id, me = c.PlayerId; // (values: the pick may resolve on an AI's copy of the game)
						Generic.PickTargets(c, s, 1,
							ch => ch is Minion m && m.Controller.PlayerId != me && Generic.Pickable(m.Controller.Opponent, m, true),
							(g2, c2, s2, picked) =>
							{
								if (!(picked is Minion enemy)) return;
								if (g2.IdEntityDic.TryGetValue(friendlyId, out IPlayable mine) && mine.Zone?.Type == Zone.PLAY)
									Generic.Destroy(mine, s2);
								if (enemy.Zone?.Type == Zone.PLAY) Generic.Destroy(enemy, s2);
							});
					})
				}));

			// [TTN_960] Sargeras, the Destroyer (9) — Titan. Battlecry: Open a portal that summons two 3/2 Imps each turn.
			// The portal is The Twisting Nether TTN_960t, a board permanent (UNTOUCHABLE, the Nether Portal UNG_829t2 shape: not a
			// Secret-zone entity), summoned beside Sargeras; he remembers it (HsTag.OPENED_PERMANENT). A new portal starts
			// unempowered (Legion Invasion! is the portal's: wiki Sargeras). Abilities (RelatedCards order): To the Void!, Inferno!,
			// Legion Invasion!; no after-ability text.
			cards.Add("TTN_960", TitansTitan(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c.BoardZone.IsFull) return;
					var portal = (Minion)Entity.FromCard(c, Cards.FromId(TitansC4TwistingNether));
					Generic.SummonBlock.Invoke(g, portal, -1, s);
					if (portal.Zone?.Type != Zone.PLAY) return;
					s[HsTag.OPENED_PERMANENT] = portal.Id;
				})
			}));
			// [TTN_960t] The Twisting Nether — At the end of your turn, summon two 3/2 Imps. (Felblaze Imps TTN_960t6, left and right
			// of it; its TRIGGER block's effect index 0 plays the client's TTNFX_TwistingNether_Demon_OverrideSpawn.)
			cards.Add("TTN_960t", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is Minion portal && portal.Zone?.Type == Zone.PLAY)
							TitansC4SummonFromPortal(g, c, portal, Cards.FromId("TTN_960t6"), V("TTN_960t", "imps", 2));
					})
				}
			}));
			// [TTN_960t2] To the Void! — Send all other minions into the Twisting Nether. (Wiki: "does not destroy minions, but
			// rather removes all other minions from the game" — Generic.RemoveFromGame: no deaths, no Deathrattles; both boards,
			// not Sargeras, not permanents / Dormant minions / Locations; nothing without a friendly portal.)
			cards.Add("TTN_960t2", TitansAbility(new CustomTask((g, c, s, t, stack) =>
			{
				if (TitansC4Portal(c, s) == null) return;
				List<Minion> others = c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll())
					.Where(m => m.Id != s.Id && !(m is Location) && !m.Untouchable && !m.IsDead && !m.ToBeDestroyed).ToList();
				foreach (Minion m in others)
					Generic.RemoveFromGame(m, s);
			})));
			// [TTN_960t3] Inferno! — Summon two 6/6 Infernals from the Twisting Nether. (Felblaze Infernals TTN_960t5 beside the
			// portal, once whatever the number of portals; Fel Fueled after Legion Invasion!; wiki: "It does not replace any future
			// Imps".)
			cards.Add("TTN_960t3", TitansAbility(new CustomTask((g, c, s, t, stack) =>
				TitansC4SummonFromPortal(g, c, TitansC4Portal(c, s), Cards.FromId("TTN_960t5"), V("TTN_960t3", "infernals", 2)))));
			// [TTN_960t4] Legion Invasion! — Future Demons summoned from the Twisting Nether have +2 Health and Taunt. (Wiki: "only
			// affects demons summoned from the Twisting Nether on later turns"; the portal's: wiki Sargeras "If there is one Sargeras
			// and 2 friendly Twisting Nethers in play, the Sargeras will only affect the one that it summoned" — TitansC4Portal, its
			// HsTag.PORTAL_DEMONS_EMPOWERED, read at each summon from it; TTN_960t4e2 "Demons summoned from this …" on it.)
			cards.Add("TTN_960t4", TitansAbility(new CustomTask((g, c, s, t, stack) =>
			{
				if (!(TitansC4Portal(c, s) is Minion portal)) return;
				portal[HsTag.PORTAL_DEMONS_EMPOWERED] = portal[HsTag.PORTAL_DEMONS_EMPOWERED] + 1;
				Give(g, s, portal, "TTN_960t4e2");
			})));
			// [TTN_960t4e] Fel Fueled — +2 Health and Taunt.
			cards.Add("TTN_960t4e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.Health_N(V("TTN_960t4", "health", 2)), Effects.TauntEff)
			}));
			// [TTN_960t4e2] Fel Fueled — Demons summoned from this have +2 Health and Taunt. (On the portal: the visual; the rule is
			// the portal's tag.)
			cards.Add("TTN_960t4e2", new CardDef(new Power { Enchant = new Enchant(new IEffect[0]) }));

			// [YOG_301] Encroaching Insanity (3) — Both players take Fatigue damage, twice. (Generic.TakeFatigue: each counter +1,
			// rules damage; caster first, then the opponent, twice — forum us.forums.blizzard.com/t/insanity-and-ties/129300:
			// "everything … resolve fully, then check deaths … That's why encroaching insanity can cause ties".)
			cards.Add("YOG_301", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("YOG_301", "times", 2); i++)
					{
						Generic.TakeFatigue(c);
						Generic.TakeFatigue(c.Opponent);
					}
				})
			}));

			// [YOG_503] Curse of Flesh (1) — Enemy minions cost Health instead of Mana next turn.
			// Commander Ulthok's TID_719e rule for minions: YOG_503e1 on the opponent ("Your cards cost Health instead of Mana this
			// turn"), its aura on their minion cards while it is their turn; it leaves at the end of their turn.
			cards.Add("YOG_503", new CardDef(new Power { PowerTask = new AddEnchantmentTask("YOG_503e1", EntityType.OP_CONTROLLER) }));
			cards.Add("YOG_503e1", new CardDef(new Power
			{
				Aura = new Aura(AuraType.OP_HAND, new Effect(GameTag.CARD_COSTS_HEALTH, EffectOperator.SET, 1))
				{
					Condition = new SelfCondition(p => p is Minion && p.Game.CurrentPlayer == p.Controller), Restless = true
				},
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					EitherTurn = true,
					SingleTask = new CustomTask((g, c, s, t, stack) => { if (s is Enchantment e && g.CurrentPlayer == c) e.Remove(); }) // (c: the player it is on — Trigger.Side)
				}
			}));

			// [YOG_517] Tentacle Tender (3) — At the end of your turn, get a 1/1 Chaotic Tendril. (Its level: YOG_514's HandCounter.)
			cards.Add("YOG_517", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) => AddToHand(c, Cards.FromId("YOG_514")))
				}
			}));

			// ================================================================ Warrior

			// [TTN_415] Khaz'goroth (6) — Titan. After this uses an ability, gain Immune this turn and attack a random enemy minion.
			// After each ability (the Titan's TRIGGER block, Power.TitanAbilityTask): TTN_415e "Heat Treatment" (Immune this turn),
			// then an effect attack on a random enemy minion it could attack (none: only the Immune — engine-rules "no target means
			// no attack"; forum reveal us.forums.blizzard.com/t/new-warrior-titan-khazgoroth/109771: "strikes one random enemy
			// minion"). Abilities (RelatedCards order): Titanforge, Tempering, Heart of Flame.
			cards.Add("TTN_415", TitansTitan(afterAbility: new CustomTask((g, c, s, t, stack) =>
			{
				if (!(s is Minion khaz) || khaz.Zone?.Type != Zone.PLAY || khaz.ToBeDestroyed) return;
				Give(g, khaz, khaz, "TTN_415e");
				List<Minion> enemies = AttackableEnemies(c).OfType<Minion>().ToList();
				if (enemies.Count > 0) EffectAttack(g, khaz, enemies[g.Random.Next(enemies.Count)]);
			})));
			// [TTN_415e] Heat Treatment — Immune this turn.
			cards.Add("TTN_415e", OneTurn(Effects.Immune));
			// [TTN_415t] Titanforge — Gain +2/+2. Draw a weapon. ("Gain" = Khaz'goroth: the ability's source.)
			cards.Add("TTN_415t", TitansAbility(new CustomTask((g, c, s, t, stack) =>
			{
				Give(g, s, (IPlayable)s, "TTN_415te");
				DrawOne(g, c, p => p is Weapon);
			})));
			// [TTN_415te] Titanforged — +2/+2.
			cards.Add("TTN_415te", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TTN_415t", "stats", 2))) }));
			// [TTN_415t2] Tempering — Gain +5 Attack. Give your hero +5 Attack this turn. (Khaz'goroth: TTN_415t2e "Warforged". The
			// client has no one-turn hero enchantment for it: the hero's +5 is a one-turn Attack effect without one — DOUBT.)
			cards.Add("TTN_415t2", TitansAbility(new CustomTask((g, c, s, t, stack) =>
			{
				Give(g, s, (IPlayable)s, "TTN_415t2e");
				Effects.Attack_N(V("TTN_415t2", "hero attack", 5)).ApplyTo(c.Hero, true);
			}), notInHistory: true /* live: never ShowInHistory (titans-vfx 190857 / 174206 captures) */));
			// [TTN_415t2e] Warforged — +5 Attack.
			cards.Add("TTN_415t2e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("TTN_415t2", "attack", 5))) }));
			// [TTN_415t3] Heart of Flame — Gain +5 Health. Give your hero 5 Armor. (Khaz'goroth: TTN_415t2e2 "Battle Hardened".)
			cards.Add("TTN_415t3", TitansAbility(new CustomTask((g, c, s, t, stack) =>
			{
				Give(g, s, (IPlayable)s, "TTN_415t2e2");
				c.Hero.GainArmor((IPlayable)s, V("TTN_415t3", "armor", 5));
			}), notInHistory: true /* live: never ShowInHistory (titans-vfx 190857 / 174206 captures) */));
			// [TTN_415t2e2] Battle Hardened — +5 Health.
			cards.Add("TTN_415t2e2", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(V("TTN_415t3", "health", 5))) }));

			// [TTN_466] Minotauren (6) — Rush. Whenever this minion deals damage, gain that much Armor. (Vicious Scraphound DAL_759.)
			cards.Add("TTN_466", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DEAL_DAMAGE)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = ComplexTask.Create(new GetEventNumberTask(), new ArmorTask())
				}
			}));

			// [TTN_467] Craftsman's Hammer (4) — Whenever your hero attacks, gain 4 Armor. Live (live-20261004-night/222041
			// 16205-16221, 7 of 7 in RecordedBlockMeta): TRIGGER effect index 1 (TRIGGER_VISUAL) in the attack's preparation,
			// the Armor inside ReuseFX_BuffImpact_Generic_Armor_Super from the weapon at the hero.
			cards.Add("TTN_467", new CardDef(new Power
			{
				OwnFx = true,
				Trigger = new Trigger(TriggerType.ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					EffectIndex = 1,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
						Generic.WholeFx((IPlayable)s, () => c.Hero.GainArmor((IPlayable)s, V("TTN_467", "armor", 4))))
				}
			}));

			// [TTN_468] Steam Guardian (3) — Battlecry: Draw a spell. Reduce the Cost of a random Fire spell in your hand by (1).
			// (Any Fire spell in hand, the drawn one included.)
			cards.Add("TTN_468", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					new CustomTask((g, c, s, t, stack) => DrawOne(g, c, p => p is Spell)),
					BuffRandomInHand(p => IsSchoolSpell(p, FIRE), "TTN_468e"))
			}));
			// [TTN_468e] Steam Treatment — Costs (1) less.
			cards.Add("TTN_468e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("TTN_468", "discount", 1))) }));

			// [TTN_469] Stoneskin Armorer (2) — Battlecry: If your Armor changed this turn, draw 2 cards. (Gained or lost this game
			// turn: Generic.ArmorChangedThisTurn; the check first, so it glows — live-20261005/184804 1634-1828.)
			cards.Add("TTN_469", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => Generic.ArmorChangedThisTurn(c), new DrawTask(V("TTN_469", "cards", 2)))
			}));

			// [TTN_470] Trial by Fire (7) — Summon five 1/1 Val'kyr with Rush. When one dies, give the others +1/+1.
			cards.Add("TTN_470", new CardDef(new Power { PowerTask = new SummonTask("TTN_470t", V("TTN_470", "valkyr", 5), SummonSide.SPELL) }));
			// [TTN_470t] Val'kyr Champion — Rush. Deathrattle: Give your other Val'kyr Champions +1/+1. (TTN_470e "Burning Loyalty"
			// on each living Val'kyr Champion of its player's.)
			cards.Add("TTN_470t", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
					GiveAll(g, s, c.BoardZone.Where(m => m.Card.Id == "TTN_470t" && m.Id != s.Id && !m.IsDead && !m.ToBeDestroyed), "TTN_470e"))
			}));
			// [TTN_470e] Burning Loyalty — +1/+1
			cards.Add("TTN_470e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TTN_470t", "stats", 1))) }));

			// [TTN_471] Furious Furnace (2) — Magnetic. Also damages minions next to whomever this attacks. (Foe Reaper's Cleave;
			// Magnetic is the engine's from the client data: its TTN_471e takes the stats, the Cleave trigger goes with it.)
			cards.Add("TTN_471", new CardDef(new Power { Trigger = Cleave() }));

			// [TTN_803] Smelt (2) — Give a random minion in your hand +3/+3. Lose 3 Armor to do it again. (A new random pick, maybe
			// the same minion; Generic.PayArmor: with less Armor nothing is paid or repeated — DOUBT; no minion: nothing.)
			ISimpleTask smelted = BuffRandomInHand(p => p is Minion, "TTN_803e");
			cards.Add("TTN_803", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!c.HandZone.Any(p => Generic.IsMinionCard(p))) return;
					smelted.Process(g, c, s, t);
					if (Generic.PayArmor(c, s, V("TTN_803", "armor", 3)))
						smelted.Process(g, c, s, t);
				})
			}));
			// [TTN_803e] Smelted — +3/+3.
			cards.Add("TTN_803e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TTN_803", "stats", 3))) }));

			// [TTN_811] Odyn, Prime Designate (8) — Battlecry: For the rest of the game, after your hero gains Armor, they gain that
			// much Attack for that turn. (TTN_811e "Impenetrable" on the player; after each Armor gain — either turn, every source,
			// Razorfen Rockstar's bonus its own — TTN_811e1 "The Best Offense..." on the hero: + that much Attack, a one-turn ADD.)
			cards.Add("TTN_811", new CardDef(new Power { PowerTask = new AddEnchantmentTask("TTN_811e", EntityType.CONTROLLER) }));
			cards.Add("TTN_811e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ARMOR)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						int gained = g.CurrentEventData?.EventNumber ?? 0;
						if (gained <= 0 || !(s is Enchantment odyn)) return;
						Generic.AddEnchantmentBlock(g, Cards.FromId("TTN_811e1"), odyn.Creator ?? odyn, c.Hero, gained, 0, 0);
					})
				}
			}));
			// [TTN_811e1] The Best Offense... — Increased Attack this turn. (The amount: its TAG_SCRIPT_DATA_NUM_1.)
			cards.Add("TTN_811e1", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(0)) { UseScriptTag = true, IsOneTurnEffect = true } }));

			// [YOG_500] General Vezax (7) — Rush. Battlecry: Gain 4 Armor. Deathrattle: Lose 4 Armor to resummon this.
			// Live (live-20261007-vfx-decks 244945-245087): the Deathrattle loses the Armor (Generic.PayArmor: needs 4 — DOUBT for
			// less) and summons a NEW General Vezax (its own card, no Battlecry) where it was. YOG_500e2 is unused in live.
			cards.Add("YOG_500", new CardDef(new Power
			{
				PowerTask = new ArmorTask(V("YOG_500", "armor", 4)),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is IPlayable vezax) || !Generic.PayArmor(c, s, V("YOG_500", "armor", 4))) return;
					Summon(g, c, vezax.Card, s);
				})
			}));

			// [YOG_501] Battleworn Faceless (2) — Battlecry: Transform into a copy of a damaged minion. (Any damaged minion; the copy
			// keeps its state — live-20261004-night/224614 81600ff: TransformCopyTask, same entity in place.)
			cards.Add("YOG_501", new CardDef(new Dictionary<PlayReq, int>
				{
					{ PlayReq.REQ_TARGET_IF_AVAILABLE, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_DAMAGED_TARGET, 0 }
				}, new Power
				{
					// live (live-20261004-night/224614 82556-82566): its ReuseFX_Shadow_OldGods_Impact_Transform_Super from and AT itself
					// (Targets = the Faceless, not the chosen minion), played empty, then the CHANGE_ENTITY — the recorded Transform
					// prefab (RecordedSubSpells.tsv, filed under the card the block began as: engine_subspells.py)
					// (2026-10-08: its recorded marker — at itself, before the change — is placed by the shared recorded effects, Game.RecordedFxAt;
					// the hand-made lookup asked for a "whole effect" and found none)
					PowerTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(t is Minion target) || !(s is Minion me) || me.Zone?.Type != Zone.PLAY) return;
						new TransformCopyTask().Process(g, c, me, target);
					})
				}));
		}
	}
}
