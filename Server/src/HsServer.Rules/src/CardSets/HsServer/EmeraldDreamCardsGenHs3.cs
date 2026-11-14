// HsServer addition (not upstream SabberStone): Into the Emerald Dream — chunk 2: Druid, Hunter and Mage (spec: research
// spec_chunk2), with the Druid / Hunter / Mage Imbue Hero Powers. Shared pieces: Actions/Imbue.cs (Imbue, triggering the Hero Power,
// Smoldering, source-keyed double damage), Actions/Paradise.cs (Choose Thrice). Every number goes through CardValues (V).
using System;
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
		/// <summary>A Wisp (client tag 3881: the seven Wisp minions — Divination, Merry Moonkin).</summary>
		private static bool IsWisp(IPlayable p) => p != null && p.Card[(GameTag)3881] > 0;

		/// <summary>Discard a random card of the hand matching <paramref name="what"/>; whether one was discarded.</summary>
		private static bool DiscardRandom(Game g, Controller c, Func<IPlayable, bool> what)
		{
			var cards = c.HandZone.GetAll().Where(what).ToList();
			if (cards.Count == 0) return false;
			Generic.DiscardBlock.Invoke(c, cards[g.Random.Next(cards.Count)]);
			return true;
		}

		/// <summary><paramref name="amount"/> damage one at a time among the living enemies (a TARGET per hit: DamageCharFunc).</summary>
		private static void SplitAmongEnemies(Game g, Controller c, IPlayable source, int amount)
		{
			for (int i = 0; i < amount; i++)
			{
				var enemies = Enemies(c).Where(x => !x.IsDead && !x.ToBeDestroyed && x.Health > 0).ToList();
				if (enemies.Count == 0) break;
				Generic.DamageCharFunc(source, enemies[g.Random.Next(enemies.Count)], 1, false);
			}
		}

		private static void EmeraldChunk2(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- the Imbue Hero Powers (Druid, Hunter, Mage)
			// [EDR_847p] Blessing of the Golem (2) — Summon a @/@ Plant Golem. (its number: TAG_SCRIPT_DATA_NUM_1, Generic.Imbue)
			// (the Plant Golem's look by size — small / medium / large tokens — doubt: unrecorded)
			cards.Add("EDR_847p", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = Math.Max(1, s[GameTag.TAG_SCRIPT_DATA_NUM_1]);
					string token = n <= 3 ? "EDR_847pt2" : n <= 6 ? "EDR_847pt3" : "EDR_847pt4";
					if (c.BoardZone.IsFull) return;
					var golem = (Minion)Entity.FromCard(c, Cards.FromId(token), new EntityData { [GameTag.ATK] = n, [GameTag.HEALTH] = n, [GameTag.CREATOR] = s.Id });
					Generic.SummonBlock.Invoke(g, golem, -1, s);
				})
			}));
			foreach (string golem in new[] { "EDR_847pt2", "EDR_847pt3", "EDR_847pt4" }) cards.Add(golem, new CardDef(new Power { NoScript = true }));
			// [EDR_850p] Blessing of the Wolf (2) — Give a random Beast in your hand +@ Attack. It costs (@) less.
			// (live vfx 216854: EDR_850pe1 and EDR_850pe5 carrying the number)
			cards.Add("EDR_850p", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var beasts = c.HandZone.GetAll().Where(p => p is Minion m && m.IsRace(Race.BEAST)).ToList();
					if (beasts.Count == 0) return;
					int n = Math.Max(1, s[GameTag.TAG_SCRIPT_DATA_NUM_1]);
					IPlayable beast = beasts[g.Random.Next(beasts.Count)];
					Give(g, s, beast, "EDR_850pe1", n);
					Give(g, s, beast, "EDR_850pe5", n);
				})
			}));
			cards.Add("EDR_850pe1", new CardDef(new Power { Enchant = Enchants.Enchants.AddAttackScriptTag }));
			cards.Add("EDR_850pe5", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(0)) { UseScriptTag = true } }));
			cards.Add("EDR_850pe", new CardDef(new Power { NoScript = true })); // Playful Pup (the Wolves)
			// [EDR_851p] Blessing of the Wisp (2) — Summon @ Wisp(s). Deal $@ damage randomly split among all enemies.
			cards.Add("EDR_851p", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = Math.Max(1, s[GameTag.TAG_SCRIPT_DATA_NUM_1]);
					for (int i = 0; i < n && !c.BoardZone.IsFull; i++) Summon(g, c, Cards.FromId("EDR_851t"), s);
					SplitAmongEnemies(g, c, (IPlayable)s, n + c.Hero.HeroPowerDamage);
				})
			}));
			cards.Add("EDR_851t", new CardDef(new Power { NoScript = true }));

			// ---------------------------------------------------------------- Druid
			// [EDR_060] Ward of Earth (5) — Gain 5 Armor. Summon a random 5-Cost minion and give it Taunt.
			cards.Add("EDR_060", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					c.Hero.Armor += V("EDR_060", "armor", 5);
					if (c.BoardZone.IsFull || !(RandomMinionOfCost(g, V("EDR_060", "cost", 5)) is Card card)) return;
					var m = (Minion)Entity.FromCard(c, card);
					Generic.SummonBlock.Invoke(g, m, -1, s);
					if (m.Zone?.Type == Zone.PLAY) m[GameTag.TAUNT] = 1;
				})
			}));
			// [EDR_209] Forest Lord Cenarius (10) — Taunt. Choose Thrice - Give your other minions +1/+3; or Summon a 5/5 Ancient with Taunt.
			cards.Add("EDR_209", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.ChooseMultiple(c, (IPlayable)s, Generic.ChooseCount(((IPlayable)s).Card)))
			}));
			cards.Add("EDR_209a", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					GiveAll(g, s, c.BoardZone.GetAll().Where(m => m.Id != s[GameTag.CREATOR] && !m.IsDead).ToList(), "EDR_209e2"))
			}));
			cards.Add("EDR_209e2", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("EDR_209a", "attack", 1)), Effects.Health_N(V("EDR_209a", "health", 3))) }));
			cards.Add("EDR_209b", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					IEntity from = g.IdEntityDic.TryGetValue(s[GameTag.CREATOR], out IPlayable cenarius) ? cenarius : s;
					Summon(g, c, Cards.FromId("EDR_209t5"), from);
				})
			}));
			cards.Add("EDR_209t5", new CardDef(new Power { NoScript = true }));
			// [EDR_270] Horn of Plenty (2) — Discover a Nature spell. It costs (2) less. (live: GBL_002e)
			cards.Add("EDR_270", new CardDef(new Power { PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.SPELL && x[SPELL_SCHOOL] == NATURE, "GBL_002e") }));
			// [EDR_271] Grove Shaper (5) — After you cast a Nature spell, summon a 2/2 Treant with "Deathrattle: Get a copy of that spell."
			// (the Treant names the spell: its TAG_SCRIPT_DATA_NUM_1 = the cast spell; its data lacks DEATHRATTLE — set on it)
			cards.Add("EDR_271", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = IsSchool(NATURE),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!((t ?? g.CurrentEventData?.EventSource as IPlayable) is IPlayable spell) || !(s is Minion me) || c.BoardZone.IsFull) return;
						var treant = (Minion)Entity.FromCard(c, Cards.FromId("EDR_271t"), new EntityData { [GameTag.TAG_SCRIPT_DATA_NUM_1] = spell.Id, [GameTag.DEATHRATTLE] = 1 });
						Generic.SummonBlock.Invoke(g, treant, me.ZonePosition + 1, s);
					})
				}
			}));
			cards.Add("EDR_271t", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (g.IdEntityDic.TryGetValue(s[GameTag.TAG_SCRIPT_DATA_NUM_1], out IPlayable spell)) AddToHand(c, spell.Card);
				})
			}));
			// [EDR_273] Symbiosis (1) — Discover a Choose One card from another class.
			cards.Add("EDR_273", new CardDef(new Power
			{
				PowerTask = DiscoverFromTask.Where((c, x) => x[GameTag.CHOOSE_ONE] > 0 && !x.HasClass(c.HeroClass) && !x.HasClass(CardClass.NEUTRAL))
			}));
			// [EDR_843] Reforestation (2) — Choose One - Draw a spell; or Draw a minion. (Hold this for @ turns to do both!)
			// (in hand its count goes down at the end of each of your turns; at 0 it becomes EDR_843t1 — the Smoldering timing, doubt)
			cards.Add("EDR_843", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					TriggerActivation = TriggerActivation.HAND,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is IPlayable card) || card.Zone?.Type != Zone.HAND) return;
						int left = card[GameTag.TAG_SCRIPT_DATA_NUM_1] - 1;
						card[GameTag.TAG_SCRIPT_DATA_NUM_1] = Math.Max(0, left);
						if (left <= 0 && Cards.TryFromId(card.Card.Id + "t1") is Card both) Generic.ChangeInHand(c, card, both, 0);
					})
				}
			}));
			cards.Add("EDR_843a", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => DrawWhere(g, c, p => p is Spell)) }));
			cards.Add("EDR_843b", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => DrawWhere(g, c, p => p is Minion)) }));
			cards.Add("EDR_843t1", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => { DrawWhere(g, c, p => p is Spell); DrawWhere(g, c, p => p is Minion); }) }));
			// [EDR_845] Hamuul Runetotem (5) — Start of Game: If each spell in your deck is Nature, Imbue your Hero Power. Repeat this every 3
			// spells you cast. (client HAMUUL_ACTIVE 4167 on the player: only once — wiki "can no longer be stacked"; IMBUE_SUB_COUNTER 4168)
			cards.Add("EDR_845", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.GAME_START)
				{
					TriggerActivation = TriggerActivation.DECK,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is IPlayable me) || !Generic.StartOfGameHolds(me) || c[(GameTag)4167] > 0) return;
						// HsServer (2026-10-08): the deck it started with — Start_of_Game.txt: "triggers before the mulligan … nothing the player does during the mulligan can affect whether or not any Start of Game effects will trigger" (our trigger resolves after the opening draw)
						if (!c.DeckCards.Where(x => x.Type == CardType.SPELL).All(x => x[SPELL_SCHOOL] == NATURE)) return;
						c[(GameTag)4167] = 1;
						Generic.Imbue(c, s);
						Generic.AddEnchantmentBlock(g, Cards.FromId("EDR_845e1"), me, c, V("EDR_845", "spells", 3), 0, 0);
					})
				}
			}));
			cards.Add("EDR_845e1", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Enchantment e)) return;
						int every = V("EDR_845", "spells", 3);
						int count = c[(GameTag)4168] + 1;
						if (count >= every) { count = 0; Generic.Imbue(c, e); }
						c[(GameTag)4168] = count;
						e[GameTag.TAG_SCRIPT_DATA_NUM_1] = every - count;
					})
				}
			}));
			// [EDR_847] Dreambound Disciple (3) — Battlecry and Deathrattle: Your next Hero Power costs (0).
			cards.Add("EDR_847", new CardDef(new Power
			{
				PowerTask = new AddEnchantmentTask("EDR_847e", EntityType.CONTROLLER),
				DeathrattleTask = new AddEnchantmentTask("EDR_847e", EntityType.CONTROLLER)
			}));
			cards.Add("EDR_847e", new CardDef(NextHeroPowerFree()));
			// [EDR_848] Photosynthesis (3) — Restore #6 Health. Get 3 random Druid spells. (a targeted heal — doubt)
			cards.Add("EDR_848", new CardDef(TargetAny, new Power
			{
				PowerTask = ComplexTask.Create(new HealTask(V("EDR_848", "heal", 6), EntityType.TARGET), new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("EDR_848", "spells", 3); i++)
						if (RandomCard(g, x => x.Type == CardType.SPELL && x.HasClass(CardClass.DRUID)) is Card spell) AddToHand(c, spell);
				}))
			}));
			// [FIR_906] Overheat (3) — Give your minions +1/+1. Discard a random Nature spell to give them +1/+1 more.
			cards.Add("FIR_906", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					GiveAll(g, s, c.BoardZone.GetAll().Where(m => !m.IsDead).ToList(), "FIR_906e");
					if (DiscardRandom(g, c, p => p is Spell && p.Card[SPELL_SCHOOL] == NATURE))
						GiveAll(g, s, c.BoardZone.GetAll().Where(m => !m.IsDead).ToList(), "FIR_906e");
				})
			}));
			cards.Add("FIR_906e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("FIR_906", "stats", 1))) }));
			// [FIR_907] Amirdrassil (5, Location) — Summon a @-Cost minion. Gain @ Armor. Draw @ card(s). Refresh @ Mana Crystal(s). (Improves each use!)
			cards.Add("FIR_907", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = Math.Max(1, s[GameTag.TAG_SCRIPT_DATA_NUM_1]);
					Summon(g, c, RandomMinionOfCost(g, n), s);
					c.Hero.Armor += n;
					for (int i = 0; i < n; i++) Generic.Draw(c);
					RefreshMana(c, n);
					s[GameTag.TAG_SCRIPT_DATA_NUM_1] = n + V("FIR_907", "improve", 1);
				})
			}));
			// [FIR_908] Charred Chameleon (1) — Rush. Battlecry: If you've used your Hero Power this turn, give a friendly minion +1/+2 and Rush.
			cards.Add("FIR_908", new CardDef(BattlecryFriendlyMinion, new Power
			{
				TargetIf = (PlayReq.REQ_TARGET_IF_AVAILABLE, (src, target) => src.Controller.HeroPowerActivationsThisTurn > 0 && target.Id != src.Id),
				GlowWhile = p => p.Controller.HeroPowerActivationsThisTurn > 0,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is Minion m && c.HeroPowerActivationsThisTurn > 0) Give(g, s, m, "FIR_908e3");
				})
			}));
			cards.Add("FIR_908e3", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("FIR_908", "attack", 1)), Effects.Health_N(V("FIR_908", "health", 2)), Effects.Rush) }));

			// ---------------------------------------------------------------- Hunter
			// [EDR_014] Verdant Dreamsaber (5) — Battlecry: If this costs (3) or less, attack two random enemy minions.
			// (wiki: "can attack the same minion twice"; the cost it was played for)
			cards.Add("EDR_014", new CardDef(new Power
			{
				GlowWhile = p => p.Cost <= V("EDR_014", "cost", 3),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Minion me) || CostWhenPlayed(s) > V("EDR_014", "cost", 3)) return;
					for (int i = 0; i < V("EDR_014", "attacks", 2); i++)
					{
						if (me.IsDead || me.ToBeDestroyed || me.Zone?.Type != Zone.PLAY) break;
						var enemies = c.Opponent.BoardZone.GetAll().Where(m => !m.IsDead && !m.ToBeDestroyed).ToList();
						if (enemies.Count == 0) break;
						EffectAttack(g, me, enemies[g.Random.Next(enemies.Count)]);
						g.DeathProcessingAndAuraUpdate();
					}
				})
			}));
			// [EDR_226] Exotic Houndmaster (2) — Battlecry: Draw a Beast. Imbue your Hero Power.
			cards.Add("EDR_226", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { DrawWhere(g, c, p => p is Minion m && m.IsRace(Race.BEAST)); Generic.Imbue(c, s); })
			}));
			// [EDR_227] Umbraclaw (4) — Rush. Deathrattle: Imbue your Hero Power.
			cards.Add("EDR_227", new CardDef(new Power { DeathrattleTask = new CustomTask((g, c, s, t, stack) => Generic.Imbue(c, s)) }));
			// [EDR_261] Amphibian's Spirit (3) — Give a minion +2/+2 and "Deathrattle: Give a friendly minion +2/+2 and this Deathrattle."
			cards.Add("EDR_261", new CardDef(TargetMinion, new Power { PowerTask = new AddEnchantmentTask("EDR_261e", EntityType.TARGET) }));
			cards.Add("EDR_261e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.AttackHealth_N(V("EDR_261", "stats", 2))),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					var friends = c.BoardZone.GetAll().Where(m => m.Id != s.Id && !m.IsDead && !m.ToBeDestroyed).ToList();
					if (friends.Count > 0) Give(g, s, friends[g.Random.Next(friends.Count)], "EDR_261e");
				})
			}));
			// [EDR_262] Spirit Bond (3) — Deal $3 damage to a minion. If it dies, summon a 3/2 Wolf with Rush.
			cards.Add("EDR_262", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					Generic.DamageCharFunc((IPlayable)s, m, V("EDR_262", "damage", 3), true);
					if (m.IsDead || m.ToBeDestroyed) Summon(g, c, Cards.FromId("EDR_850pe"), s);
				})
			}));
			// [EDR_263] Grace of the Greatwolf (4) — Choose One - Deal $4 damage to the enemy hero; or Summon two 3/2 Wolves with Rush.
			cards.Add("EDR_263", new CardDef(new Power()));
			cards.Add("EDR_263a", new CardDef(new Power { PowerTask = new DamageTask(V("EDR_263a", "damage", 4), EntityType.OP_HERO, true) }));
			cards.Add("EDR_263b", new CardDef(new Power { PowerTask = new SummonTask("EDR_850pe", V("EDR_263b", "wolves", 2), SummonSide.SPELL) }));
			// [EDR_416] Shepherd's Crook (3, weapon) — After your hero attacks, summon a 3/3 Sheep that's Dormant for 2 turns.
			cards.Add("EDR_416", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK) { TriggerSource = TriggerSource.HERO, SingleTask = new SummonTask("EDR_416t") }
			}));
			cards.Add("EDR_416t", new CardDef(new Power { NoScript = true }));
			// [EDR_480] Goldrinn (9) — Rush. Friendly Beasts deal double damage. (live night 19335: EDR_480e shown on each friendly Beast)
			cards.Add("EDR_480", new CardDef(new Power
			{
				Aura = new Aura(AuraType.BOARD, Effects.Attack_N(0)) { Condition = new SelfCondition(p => p is Minion m && m.IsRace(Race.BEAST)), ShowAs = "EDR_480e" },
				Trigger = Generic.DoublesDamageOf("EDR_480", p => p is Minion m && m.IsRace(Race.BEAST))
			}));
			// [EDR_481] Mythical Runebear (4) — Taunt. Battlecry: If this has 4 or more Attack, summon a copy of this.
			cards.Add("EDR_481", new CardDef(new Power
			{
				GlowWhile = p => p[GameTag.ATK] >= V("EDR_481", "attack", 4),
				PowerTask = ComplexTask.If((g, c, s) => s is Minion me && me.AttackDamage >= V("EDR_481", "attack", 4), new SummonCopyTask(EntityType.SOURCE))
			}));
			// [EDR_853] Broll Bearmantle (5) — After you cast a spell, summon a random Animal Companion. (its related cards)
			cards.Add("EDR_853", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					// (CATACLYSM: the player's Animal Companions — Generic.SummonCompanions)
					SingleTask = new CustomTask((g, c, s, t, stack) => Generic.SummonCompanions(c, s, 1, position: ((Minion)s).ZonePosition + 1))
				}
			}));
			// [FIR_909] Bursting Shot (2) — Deal $2 damage to three random enemies.
			cards.Add("FIR_909", new CardDef(new Power { PowerTask = ComplexTask.DamageRandomTargets(V("FIR_909", "targets", 3), EntityType.ENEMIES, V("FIR_909", "damage", 2), true) }));
			// [FIR_953] Magma Hound (8) — Rush. After this attacks a minion and survives, deal this minion's Attack damage split among all enemies.
			cards.Add("FIR_953", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Minion me) || me.IsDead || me.ToBeDestroyed || !(g.CurrentEventData?.EventTarget is Minion)) return;
						SplitAmongEnemies(g, c, me, me.AttackDamage);
					})
				}
			}));
			// [FIR_960] Tending Dragonkin (5) — Battlecry: Copy the lowest Cost Beast in your hand.
			cards.Add("FIR_960", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var beasts = c.HandZone.GetAll().Where(p => p is Minion m && m.IsRace(Race.BEAST)).ToList();
					if (beasts.Count == 0 || c.HandZone.IsFull) return;
					int min = beasts.Min(p => p.Cost);
					var lowest = beasts.Where(p => p.Cost == min).ToList();
					Generic.Copy(c, s, lowest[g.Random.Next(lowest.Count)], Zone.HAND);
				})
			}));

			// ---------------------------------------------------------------- Mage
			// [EDR_430] Aessina (8) — Battlecry: If 20 friendly minions have died this game, deal 20 damage split among all enemies.
			cards.Add("EDR_430", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (Generic.ThresholdMet((IPlayable)s)) SplitAmongEnemies(g, c, (IPlayable)s, V("EDR_430", "damage", 20)); })
			}));
			// [EDR_517] Q'onzu (3) — Battlecry: Discover a spell. Choose to keep it or put it on top of your opponent's deck.
			// (the pick waits aside; then Tranquil Breeze EDR_517A keeps it, Winds of Change EDR_517B gives it to the opponent's deck top —
			// doubt: unrecorded)
			cards.Add("EDR_517", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var pool = Cards.GenerationPool(g, c).Where(x => x.Type == CardType.SPELL && x.Collectible && (x.HasClass(c.HeroClass) || x.HasClass(CardClass.NEUTRAL)));
					Card[] offer = DiscoverFromTask.PickDistinct(g, pool, 3).ToArray();
					if (offer.Length == 0) return;
					string own = ((IPlayable)s).Card.Id;
					Generic.CreateChoiceCards.Invoke(c, s, null, ChoiceType.GENERAL, ChoiceAction.STACK, offer, new CustomTask((g2, c2, s2, pick, st) =>
					{
						if (pick == null) return;
						int spellId = pick.Id;
						Card[] ways = { Cards.TryFromId(own + "A"), Cards.TryFromId(own + "B") };
						if (ways.Any(x => x == null)) { pick.Zone?.Remove(pick); Generic.AddHandPhase.Invoke(c2, pick); return; }
						Generic.CreateChoiceCards.Invoke(c2, s2, null, ChoiceType.GENERAL, ChoiceAction.STACK, ways, new CustomTask((g3, c3, s3, way, st3) =>
						{
							if (way == null || !g3.IdEntityDic.TryGetValue(spellId, out IPlayable spell)) return;
							spell.Zone?.Remove(spell);
							if (way.Card.Id.EndsWith("A", StringComparison.Ordinal)) { if (!c3.HandZone.IsFull) Generic.AddHandPhase.Invoke(c3, spell); return; }
							Generic.GiveTo(spell, c3.Opponent);
							if (!c3.Opponent.DeckZone.IsFull) c3.Opponent.DeckZone.Add(spell);
						}));
					}));
				})
			}));
			cards.Add("EDR_517A", new CardDef(new Power { NoScript = true }));
			cards.Add("EDR_517B", new CardDef(new Power { NoScript = true }));
			// [EDR_519] Wisprider (5) — Battlecry: Imbue your Hero Power, then trigger it.
			cards.Add("EDR_519", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => { Generic.Imbue(c, s); Generic.TriggerHeroPower(c, s); }) }));
			// [EDR_520] Forbidden Shrine (1, Location) — Spend all your Mana. Cast a random spell that costs that much.
			cards.Add("EDR_520", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int mana = Math.Max(0, c.RemainingMana);
					c.UsedMana += mana;
					if (RandomCard(g, x => x.Type == CardType.SPELL && x.Cost == mana) is Card spell)
						new CastRandomSpellTask { Spell = spell }.Process(g, c, s, null);
				})
			}));
			// [EDR_804] Divination (2) — Destroy a friendly Wisp to draw 3 cards.
			cards.Add("EDR_804", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 } }, new Power
			{
				TargetIf = (PlayReq.REQ_TARGET_WITH_RACE, (src, target) => IsWisp(target)),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion wisp)) return;
					Generic.Destroy(wisp, s);
					for (int i = 0; i < V("EDR_804", "draw", 3); i++) Generic.Draw(c);
				})
			}));
			// [EDR_871] Spirit Gatherer (2) — Battlecry: Get a Wisp. Imbue your Hero Power.
			cards.Add("EDR_871", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => { AddToHand(c, Cards.FromId("EDR_851t")); Generic.Imbue(c, s); }) }));
			// [EDR_872] Spark of Life (1) — Choose One - Discover a Mage spell; or Discover a Druid spell.
			cards.Add("EDR_872", new CardDef(new Power()));
			cards.Add("EDR_872A", new CardDef(new Power { PowerTask = DiscoverFromTask.Where((c, x) => x.Type == CardType.SPELL && x.HasClass(CardClass.MAGE)) }));
			cards.Add("EDR_872B", new CardDef(new Power { PowerTask = DiscoverFromTask.Where((c, x) => x.Type == CardType.SPELL && x.HasClass(CardClass.DRUID)) }));
			// [EDR_874] Stellar Balance (2) — Get a Moonfire and a Starfire. Give them Spell Damage +1.
			cards.Add("EDR_874", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (string id in new[] { "CS2_008", "EX1_173" })
					{
						if (c.HandZone.IsFull) break;
						IPlayable spell = Entity.FromCard(c, Cards.FromId(id), new EntityData { [GameTag.CREATOR] = s.Id });
						if (Generic.AddHandPhase.Invoke(c, spell)) Give(g, s, spell, "EDR_874e");
					}
				})
			}));
			cards.Add("EDR_874e", new CardDef(new Power { Enchant = new Enchant(new Effect(GameTag.SPELLPOWER, EffectOperator.ADD, V("EDR_874", "spell damage", 1))) }));
			// [EDR_940] Merry Moonkin (4) — At the end of your turn, gain @ Armor. (Improved by Wisps you control!)
			int moonkin(IPlayable p) => p.Card[GameTag.TAG_SCRIPT_DATA_NUM_1] + (p.Controller?.BoardZone.GetAll().Count(m => IsWisp(m) && !m.IsDead) ?? 0);
			cards.Add("EDR_940", new CardDef(new Power
			{
				HandCounter = moonkin,
				Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = new CustomTask((g, c, s, t, stack) => c.Hero.Armor += moonkin((IPlayable)s)) }
			}));
			// [EDR_941] Starsurge (3) — Deal $@ damage to a minion. (Improved by each friendly minion that died this game.)
			int starsurge(IPlayable p) => p.Card[GameTag.TAG_SCRIPT_DATA_NUM_1] + (p.Controller?[GameTag.NUM_FRIENDLY_MINIONS_THAT_DIED_THIS_GAME] ?? 0);
			cards.Add("EDR_941", new CardDef(TargetMinion, new Power
			{
				HandCounter = starsurge,
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (t is Minion m) Generic.DamageCharFunc((IPlayable)s, m, starsurge((IPlayable)s), true); })
			}));
			// [FIR_910] Scorching Winds (3) — Deal $3 damage. Discard a random Fire spell to deal $3 more.
			cards.Add("FIR_910", new CardDef(TargetAny, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is ICharacter target)) return;
					Generic.DamageCharFunc((IPlayable)s, target, V("FIR_910", "damage", 3), true);
					if (DiscardRandom(g, c, p => p.Id != s.Id && p is Spell && p.Card[SPELL_SCHOOL] == FIRE_SCHOOL))
						Generic.DamageCharFunc((IPlayable)s, target, V("FIR_910", "more", 3), true);
				})
			}));
			// [FIR_911] Smoldering Grove (2) — Draw {0} card(s). (Upgrades each turn, but discards after {1}!)
			cards.Add("FIR_911", new CardDef(new Power
			{
				Trigger = Generic.SmolderingInHand(),
				PowerTask = new CustomTask((g, c, s, t, stack) => { for (int i = 0; i < Math.Max(1, s[GameTag.TAG_SCRIPT_DATA_NUM_1]); i++) Generic.Draw(c); })
			}));
			// [FIR_913] Inferno Herald (4) — After you cast a Fire spell, get a random Elemental and reduce its Cost by (3). (live: trigger index 2)
			cards.Add("FIR_913", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = IsSchool(FIRE_SCHOOL),
					EffectIndex = 2,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (c.HandZone.IsFull || !(RandomCard(g, x => x.Type == CardType.MINION && x.IsRace(Race.ELEMENTAL)) is Card elemental)) return;
						IPlayable got = Entity.FromCard(c, elemental, new EntityData { [GameTag.CREATOR] = s.Id });
						if (Generic.AddHandPhase.Invoke(c, got)) Give(g, s, got, "GBL_004e");
					})
				}
			}));
		}
	}
}
