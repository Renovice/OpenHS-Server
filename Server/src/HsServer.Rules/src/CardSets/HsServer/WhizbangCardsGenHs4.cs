// HsServer addition (not upstream SabberStone): Whizbang's Workshop (WHIZBANG, set 1897) — chunk 4: Warlock, Shaman, Warrior, The Headless
// Horseman (spec: research spec_chunk4). Shared pieces: Shudderblock's Battlecries 3 times (Controller.BattlecryRuns, also for weapons),
// Generic.SetRemainingHealth (INFERNAL!, and Alexstrasza now sets the REMAINING Health — wiki), Imps by the client IMP tag (IsImp),
// Miniaturize on weapons (Boom Wrench). Every number goes through CardValues (V).
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
		/// <summary>The Minis (client MINI 3319 on the card itself: Botface's pool, every class).</summary>
		private static List<Card> MiniCards() => Cards.All.Where(x => x[Generic.MINI] > 0 && !x.NeverGenerated && (x.Type == CardType.MINION || x.Type == CardType.WEAPON)).ToList();

		/// <summary>The Headless Horseman's Battlecry: destroy the enemy minion with the most Attack (ties: random), shuffle the Head.</summary>
		private static ISimpleTask HeadlessHorseman => new CustomTask((g, c, s, t, stack) =>
		{
			var enemies = c.Opponent.BoardZone.GetAll().Where(m => !(m is Location) && !m.IsDead && !m.ToBeDestroyed && !m.Untouchable).ToList();
			if (enemies.Count > 0)
			{
				int most = enemies.Max(m => m.AttackDamage);
				var top = enemies.Where(m => m.AttackDamage == most).ToList();
				Generic.Destroy(top[g.Random.Next(top.Count)], s);
			}
			Generic.ShuffleIntoDeck.Invoke(c, s, Entity.FromCard(c, Cards.FromId("TOY_829t"), new EntityData { { GameTag.CREATOR, s.Id } }));
		});

		private static void WhizbangChunk4(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Warlock
			// [MIS_027] Domino Effect (3) — Deal $2 damage to a minion. Repeat to the left or right, dealing 1 more damage each time. (wiki: a random
			// direction when both exist, then only that way until the end of the side or an invalid target)
			cards.Add("MIS_027", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion first)) return;
					var row = first.Controller.BoardZone.GetAll().ToList();
					int at = row.IndexOf(first);
					bool left = at > 0, right = at >= 0 && at < row.Count - 1;
					int step = left && right ? (g.Random.Next(2) == 0 ? -1 : 1) : left ? -1 : 1;
					int damage = V("MIS_027", "damage", 2);
					Generic.DamageCharFunc((IPlayable)s, first, damage, true);
					for (int i = at + step; i >= 0 && i < row.Count; i += step)
					{
						Minion next = row[i];
						if (next is Location || next.Untouchable) break;
						damage += V("MIS_027", "more", 1);
						Minion from = row[i - step], hit = next;
						int amount = damage;
						Generic.MissileFx((IPlayable)s, hit, () => Generic.DamageCharFunc((IPlayable)s, hit, amount, true));
					}
				})
			}));
			// [MIS_703] INFERNAL! (4) — Taunt. Battlecry: Set your hero's remaining Health to 15. (not damage, not healing; Armor stays — wiki)
			cards.Add("MIS_703", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int before = c.Hero.Health;
					Generic.SetRemainingHealth(c.Hero, V("MIS_703", "health", 15));
					if (c.Hero.Health != before) Generic.AddEnchantmentBlock(g, Cards.FromId("MIS_703e"), (IPlayable)s, c.Hero, 0, 0, 0);
				})
			}));
			cards.Add("MIS_703e", new CardDef(new Power { Enchant = new Enchant() }));
			// [EX1_561] Alexstrasza (9) — Battlecry: Set a hero's remaining Health to 15. (WHIZBANG: the shared rule — upstream set the MAXIMUM)
			cards.Add("EX1_561", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_IF_AVAILABLE, 0 }, { PlayReq.REQ_HERO_TARGET, 0 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Hero hero)) return;
					Generic.SetRemainingHealth(hero, V("EX1_561", "health", 15));
					Generic.AddEnchantmentBlock(g, Cards.FromId("EX1_561e"), (IPlayable)s, hero, 0, 0, 0);
				})
			}));
			cards.Add("EX1_561e", new CardDef(new Power { Enchant = new Enchant() }));
			// [MIS_707] Mass Production (1) — Draw 2 cards. Deal $3 damage to your hero. Shuffle 2 copies of this into your deck.
			cards.Add("MIS_707", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("MIS_707", "cards", 2); i++) Generic.Draw(c);
					Generic.DamageCharFunc((IPlayable)s, c.Hero, V("MIS_707", "damage", 3), true);
					for (int i = 0; i < V("MIS_707", "copies", 2); i++)
						Generic.ShuffleIntoDeck.Invoke(c, s, Entity.FromCard(c, s.Card, new EntityData { { GameTag.CREATOR, s.Id } }));
				})
			}));
			// [TOY_524] Game Master Nemsy (5) — Battlecry: Draw a Demon. Deathrattle: Swap places with it. (from hand only: forum 126073)
			cards.Add("TOY_524", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (DrawOne(g, c, p => IsDemon(p)) is IPlayable demon) s[GameTag.TAG_SCRIPT_DATA_ENT_1] = demon.Id; }),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!g.IdEntityDic.TryGetValue(s[GameTag.TAG_SCRIPT_DATA_ENT_1], out IPlayable linked) || !(linked is Minion demon) || demon.Zone?.Type != Zone.HAND || demon.Controller != c) return;
					if (Generic.SummonFromHand(c, demon, s)) GetCard(c, s.Card, s);
				})
			}));
			// [TOY_526] Malefic Rook (3) — Battlecry: Attack YOUR hero.
			cards.Add("TOY_526", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => { if (s is Minion rook) EffectAttack(g, rook, c.Hero); }) }));
			// [TOY_527] Cursed Campaign (3) — Give a friendly minion "Deathrattle: Summon two copies of this minion that are Dormant for 2 turns."
			// (copies of a dead minion are basic: rule book Copy.txt)
			cards.Add("TOY_527", new CardDef(TargetFriendlyMinion, new Power { PowerTask = new AddEnchantmentTask("TOY_527e", EntityType.TARGET) }));
			cards.Add("TOY_527e", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("TOY_527", "copies", 2); i++)
						if (SummonNew(g, c, s.Card, s) is Minion copy) Generic.MakeDormant(copy, V("TOY_527", "turns", 2), "TOY_527e1", (IPlayable)s);
				})
			}));
			// [TOY_529] Wheel of DEATH!!! (8) — Destroy your deck. In 5 turns, destroy the enemy hero. (patch 29.2.2: counts at the start of your
			// turns; the counter on the enemy hero: "Your hero dies in @ turns")
			cards.Add("TOY_529", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable card in c.DeckZone.GetAll().ToList()) Generic.DeckCardToGraveyard(card, s.Id, false);
					Generic.AddEnchantmentBlock(g, Cards.FromId("TOY_529e1"), (IPlayable)s, c.Opponent.Hero, V("TOY_529", "turns", 5), c.PlayerId, 0);
				})
			}));
			cards.Add("TOY_529e1", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					EitherTurn = true,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Enchantment e) || g.CurrentPlayer.PlayerId != e[GameTag.TAG_SCRIPT_DATA_NUM_2]) return;
						e[GameTag.TAG_SCRIPT_DATA_NUM_1] = Math.Max(0, e[GameTag.TAG_SCRIPT_DATA_NUM_1] - 1);
						if (e[GameTag.TAG_SCRIPT_DATA_NUM_1] == 0 && e.Target is Hero doomed) Generic.Destroy(doomed, e);
					})
				}
			}));
			// [TOY_883] Table Flip (10) — Deal $3 damage to all enemy minions. Costs (1) less for each other card in your hand.
			cards.Add("TOY_883", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => p.Controller.HandZone.Count(x => x.Id != p.Id)),
				PowerTask = new CustomTask((g, c, s, t, stack) => DamageAll(s, c.Opponent.BoardZone.GetAll().Where(m => !(m is Location)).Cast<ICharacter>(), V("TOY_883", "damage", 3), true))
			}));
			// [TOY_884] Crane Game (8) — Summon copies of two Demons in your deck.
			cards.Add("TOY_884", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable demon in c.DeckZone.GetAll().Where(p => IsDemon(p)).OrderBy(_ => g.Random.Next()).Take(V("TOY_884", "demons", 2)).ToList())
						if (!c.BoardZone.IsFull) Generic.Copy(c, s, demon, Zone.PLAY);
				})
			}));
			// [TOY_886] Endgame (2) — Resurrect your last Demon that died.
			cards.Add("TOY_886", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					IPlayable last = c.DiedThisGame.AsEnumerable().Reverse().Select(id => g.IdEntityDic.TryGetValue(id, out IPlayable p) ? p : null).FirstOrDefault(p => p is Minion && IsDemon(p));
					if (last == null || c.BoardZone.IsFull) return;
					if (Generic.NewFromGraveyard(c, last) is Minion back) Generic.SummonBlock.Invoke(g, back, -1, s);
				})
			}));
			// [TOY_914] Wretched Queen (8) — Taunt. Deathrattle: Summon two 4/6 Knights with Taunt.
			cards.Add("TOY_914", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => { for (int i = 0; i < V("TOY_914", "knights", 2); i++) Summon(g, c, Cards.FromId("TOY_914t"), s); })
			}));
			// [TOY_915] Tabletop Roleplayer (4) — Miniaturize. Battlecry: Give a friendly Demon +2 Attack and Immune this turn.
			cards.Add("TOY_915", new CardDef(new Dictionary<PlayReq, int>
				{ { PlayReq.REQ_TARGET_IF_AVAILABLE, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_TARGET_WITH_RACE, (int)Race.DEMON } },
				new Power { PowerTask = new AddEnchantmentTask("TOY_915e", EntityType.TARGET) }));
			cards.Add("TOY_915e", OneTurn(Effects.Attack_N(V("TOY_915", "attack", 2)), new Effect(GameTag.IMMUNE, EffectOperator.ADD, 1)));
			// [TOY_916] Sketch Artist (3) — Battlecry: Draw a Shadow spell. Get a Temporary copy of it.
			cards.Add("TOY_916", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (DrawOne(g, c, p => p is Spell && p[SPELL_SCHOOL] == SHADOW) is IPlayable drawn && Generic.Copy(c, s, drawn, Zone.HAND) is IPlayable copy && copy.Zone?.Type == Zone.HAND)
						Generic.MakeTemporary(copy, s);
				})
			}));

			// ---------------------------------------------------------------- Shaman
			// [MIS_307] Murloc Growfin (1) — Gigantify. Battlecry: Summon a Tinyfin with Rush and stats equal to this minion's.
			cards.Add("MIS_307", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (s is Minion me) SummonWithStats(g, c, Cards.FromId("MIS_307t"), me.AttackDamage, me.Health, s); })
			}));
			// [MIS_701] Wave of Nostalgia (5) — Transform ALL minions into random Legendary ones from the past.
			cards.Add("MIS_701", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var pool = Cards.All.Where(x => x.Collectible && x.Type == CardType.MINION && x.Rarity == Rarity.LEGENDARY && !x.NeverRandomlyOnBoard && Cards.FromThePast(x)).ToList();
					if (pool.Count == 0) return;
					foreach (Minion m in c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll()).Where(m => !(m is Location) && !m.Untouchable).ToList())
						Generic.TransformBlock.Invoke(m.Controller, pool[g.Random.Next(pool.Count)], m);
				})
			}));
			// [TOY_046] Incredible Value (3) — Discover a 4-Cost minion. Set its Attack and Health to 7.
			cards.Add("TOY_046", new CardDef(new Power
			{
				PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.MINION && x.Cost == V("TOY_046", "cost", 4), "TOY_046e")
			}));
			cards.Add("TOY_046e", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttackHealth(V("TOY_046", "stats", 7))) }));
			// [TOY_500] Baking Soda Volcano (4) — Lifesteal. Deal $10 damage randomly split among all minions. Overload: (1)
			cards.Add("TOY_500", new CardDef(new Power
			{
				PowerTask = new EnqueueTask(V("TOY_500", "damage", 10), ComplexTask.DamageRandomTargets(1, EntityType.ALLMINIONS, 1), true)
			}));
			// [TOY_501] Shudderblock (6) — Miniaturize. Battlecry: Your next Battlecry triggers 3 times, but can't damage the enemy hero. (live vfx
			// 72245: TOY_501e on the player, EXTRA_BATTLECRIES_BASE 2; consumed by the next Battlecry card played; wiki: damage set to 0 only while
			// that Battlecry resolves)
			cards.Add("TOY_501", new CardDef(new Power { PowerTask = new AddEnchantmentTask("TOY_501e", EntityType.CONTROLLER) }));
			cards.Add("TOY_501e", new CardDef(new Power
			{
				Aura = new Aura(AuraType.CONTROLLER, new Effect(GameTag.EXTRA_BATTLECRIES_BASE, EffectOperator.ADD, 2)),
				Trigger = new MultiTrigger(
					new Trigger(TriggerType.PLAY_CARD)
					{
						TriggerSource = TriggerSource.FRIENDLY,
						SingleTask = new CustomTask((g, c, s, t, stack) => { if (t != null && t.Card[GameTag.BATTLECRY] > 0) c[HsTag.SHUDDERBLOCK_CARD] = t.Id; })
					},
					new Trigger(TriggerType.PREDAMAGE)
					{
						TriggerSource = TriggerSource.ALL,
						FastExecution = true,
						SingleTask = new CustomTask((g, c, s, t, stack) =>
						{
							EventMetaData d = g.CurrentEventData;
							if (d != null && c[HsTag.SHUDDERBLOCK_CARD] > 0 && d.EventSource?.Id == c[HsTag.SHUDDERBLOCK_CARD] && d.EventTarget?.Id == c.Opponent.Hero.Id)
								d.EventNumber = 0;
						})
					},
					new Trigger(TriggerType.AFTER_PLAY_CARD)
					{
						TriggerSource = TriggerSource.FRIENDLY,
						SingleTask = new CustomTask((g, c, s, t, stack) =>
						{
							if (t == null || t.Id != c[HsTag.SHUDDERBLOCK_CARD] || !(s is Enchantment e)) return;
							c[HsTag.SHUDDERBLOCK_CARD] = 0;
							RemoveEnchantmentTask.Task.Process(g, c, e, null);
						})
					})
			}));
			// [TOY_503] Shining Sentinel (7) — Taunt, Elusive. Battlecry: Summon a copy of this.
			cards.Add("TOY_503", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => { if (s is Minion me && !c.BoardZone.IsFull) Generic.Copy(c, s, me, Zone.PLAY); }) }));
			// [TOY_504] Hagatha the Fabled (4) — Battlecry: Draw 2 spells that cost (5) or more. Transform them into Slimes that cast the spells.
			// (wiki: a 7-Cost spell makes a 7-mana 7/7 Slime; its "Cast {0}" = the spell: Generic.CarrySpell — the Slime targets like
			// its spell, as every "Cast {0}" carrier)
			cards.Add("TOY_504", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("TOY_504", "spells", 2); i++)
					{
						if (!(DrawOne(g, c, p => p is Spell && p.Cost >= V("TOY_504", "cost", 5)) is IPlayable spell) || spell.Zone?.Type != Zone.HAND) continue;
						int cost = spell.Cost;
						IPlayable stored = Entity.FromCard(c, spell.Card, new EntityData { { GameTag.CREATOR, s.Id } }, c.SetasideZone);
						if (!(Generic.ChangeEntityBlock(c, spell, Cards.FromId("TOY_504t"), true) is Minion slime)) continue;
						Generic.CarrySpell(slime, stored);
						slime.AttackDamage = cost;
						slime.BaseHealth = cost;
						slime.Cost = cost;
					}
				})
			}));
			cards.Add("TOY_504t", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => Generic.CastCarriedSpells(c, (IPlayable)s, t)) }));
			// [TOY_506] Once Upon a Time... (6) — Summon a random 3-Cost Beast, Dragon, Elemental, and Murloc.
			cards.Add("TOY_506", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Race race in new[] { Race.BEAST, Race.DRAGON, Race.ELEMENTAL, Race.MURLOC })
						Summon(g, c, RandomMinionOfCost(g, V("TOY_506", "cost", 3), x => x.IsRace(race)), s);
				})
			}));
			// [TOY_507] Fairy Tale Forest (3, Location) — Draw a Battlecry minion. It costs (1) less.
			cards.Add("TOY_507", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (DrawOne(g, c, p => p is Minion && p.Card[GameTag.BATTLECRY] > 0) is IPlayable drawn) Give(g, s, drawn, "TOY_507e"); })
			}));
			cards.Add("TOY_507e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("TOY_507", "discount", 1))) }));
			// [TOY_508] Pop-Up Book (1) — Deal $2 damage. Summon two 0/1 Frogs with Taunt.
			cards.Add("TOY_508", new CardDef(TargetAny, new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("TOY_508", "damage", 2), EntityType.TARGET, true),
					new CustomTask((g, c, s, t, stack) => { for (int i = 0; i < V("TOY_508", "frogs", 2); i++) Summon(g, c, Cards.FromId("hexfrog"), s); }))
			}));
			// [TOY_513] Sand Art Elemental (4) — Miniaturize. Battlecry: Give your hero +1 Attack and Windfury this turn.
			cards.Add("TOY_513", new CardDef(new Power { PowerTask = new AddEnchantmentTask("TOY_513e", EntityType.HERO) }));
			cards.Add("TOY_513e", OneTurn(Effects.Attack_N(V("TOY_513", "attack", 1)), new Effect(GameTag.WINDFURY, EffectOperator.ADD, 1)));
			// [TOY_877] Wish Upon a Star (7) — Give +2/+3 to all minions in your hand, deck, and battlefield.
			cards.Add("TOY_877", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					GiveAll(g, s, c.HandZone.GetAll().Concat(c.DeckZone.GetAll()).Concat(c.BoardZone.GetAll().Where(m => !(m is Location))).Where(p => p is Minion).ToList(), "TOY_877e1"))
			}));
			cards.Add("TOY_877e1", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("TOY_877", "attack", 2)), Effects.Health_N(V("TOY_877", "health", 3))) }));

			// ---------------------------------------------------------------- Warrior
			// [MIS_705] Standardized Pack (1) — Add 5 random Taunt minions to your hand. They are Temporary. (5 different: live)
			cards.Add("MIS_705", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var picks = DiscoverFromTask.PickDistinct(g, Cards.GenerationPool(g, c).Where(x => x.Type == CardType.MINION && x[GameTag.TAUNT] > 0), V("MIS_705", "minions", 5));
					foreach (Card card in picks) if (GetCard(c, card, s) is IPlayable got) Generic.MakeTemporary(got, s);
				})
			}));
			// [MIS_711] Safety Expert (10) — Rush. Deathrattle: Shuffle three Bombs into your opponent's deck. (live: Deathrattle index 1)
			cards.Add("MIS_711", new CardDef(new Power
			{
				DeathrattleEffectIndex = 1,
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("MIS_711", "bombs", 3); i++)
						Generic.ShuffleIntoDeck.Invoke(c.Opponent, s, Entity.FromCard(c.Opponent, Cards.FromId("BOT_511t"), new EntityData { { GameTag.CREATOR, s.Id } }));
				})
			}));
			// [MIS_902] Part Scrapper (2) — Lose up to 5 Armor. Your next Mech costs that much less. (one "next Mech costs (1) less" per Armor lost)
			cards.Add("MIS_902", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int lost = Math.Min(V("MIS_902", "armor", 5), c.Hero.Armor);
					if (lost <= 0) return;
					c.Hero.Armor -= lost;
					for (int i = 0; i < lost; i++) new AddEnchantmentTask("MIS_902e", EntityType.CONTROLLER).Process(g, c, s, null);
				})
			}));
			cards.Add("MIS_902e", NextCardEffect(Effects.ReduceCost(1), new SelfCondition(p => IsMech(p)), thisTurnOnly: false));
			// [TOY_602] Chemical Spill (6) — Summon the highest Cost minion from your hand, then deal $5 damage to it. (ties: random)
			cards.Add("TOY_602", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var minions = c.HandZone.GetAll().OfType<Minion>().Where(m => Generic.IsMinionCard(m)).ToList();
					if (minions.Count == 0 || c.BoardZone.IsFull) return;
					int top = minions.Max(m => m.Cost);
					var pick = minions.Where(m => m.Cost == top).ToList();
					Minion chosen = pick[g.Random.Next(pick.Count)];
					if (Generic.SummonFromHand(c, chosen, s) && chosen.Zone?.Type == Zone.PLAY)
						Generic.DamageCharFunc((IPlayable)s, chosen, V("TOY_602", "damage", 5), true);
				})
			}));
			// [TOY_603] Wreck'em and Deck'em (3) — Choose a friendly Mech. Summon a copy of it that attacks a random enemy, then dies.
			cards.Add("TOY_603", new CardDef(new Dictionary<PlayReq, int>
				{ { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_TARGET_WITH_RACE, (int)Race.MECHANICAL } },
				new Power
				{
					PowerTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(t is Minion mech) || c.BoardZone.IsFull || !(Generic.Copy(c, s, mech, Zone.PLAY) is Minion copy) || copy.Zone?.Type != Zone.PLAY) return;
						var enemies = AttackableEnemies(c);
						if (enemies.Count > 0) EffectAttack(g, copy, enemies[g.Random.Next(enemies.Count)]);
						if (copy.Zone?.Type == Zone.PLAY && !copy.ToBeDestroyed) Generic.Destroy(copy, s);
					})
				}));
			// [TOY_604] Boom Wrench (3, weapon) — Miniaturize. Deathrattle: Trigger the Deathrattle of a random friendly Mech. (one with a
			// Deathrattle: live vfx 243423 empty with only a Deathrattle-less Mech)
			cards.Add("TOY_604", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					var mechs = c.BoardZone.GetAll().Where(m => m.IsRace(Race.MECHANICAL) && m.Card.Power?.DeathrattleTask != null && !m.IsSilenced).ToList();
					if (mechs.Count > 0) Generic.TriggerDeathrattleOf(s, mechs[g.Random.Next(mechs.Count)]);
				})
			}));
			// [TOY_605] Quality Assurance (2) — Draw 2 Taunt minions.
			cards.Add("TOY_605", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { for (int i = 0; i < V("TOY_605", "cards", 2); i++) DrawOne(g, c, p => p is Minion && p.Card[GameTag.TAUNT] > 0); })
			}));
			// [TOY_606] Testing Dummy (6) — Taunt. Deathrattle: Deal 8 damage randomly split among all enemy minions.
			cards.Add("TOY_606", new CardDef(new Power
			{
				DeathrattleTask = new EnqueueTask(V("TOY_606", "damage", 8), ComplexTask.DamageRandomTargets(1, EntityType.OP_MINIONS, 1))
			}));
			// [TOY_607] Inventor Boom (8) — Battlecry: Resurrect two different friendly Mechs that cost (5) or more. They attack random enemies.
			cards.Add("TOY_607", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var dead = c.DiedThisGame.Select(id => g.IdEntityDic.TryGetValue(id, out IPlayable p) ? p : null)
						.Where(p => p is Minion && p.Card.IsRace(Race.MECHANICAL) && p.Card.Cost >= V("TOY_607", "cost", 5))
						.GroupBy(p => p.Card.Id).Select(x => x.First()).OrderBy(_ => g.Random.Next()).Take(V("TOY_607", "mechs", 2)).ToList();
					var back = new List<Minion>();
					foreach (IPlayable d in dead)
					{
						if (c.BoardZone.IsFull) break;
						if (Generic.NewFromGraveyard(c, d) is Minion m) { Generic.SummonBlock.Invoke(g, m, -1, s); if (m.Zone?.Type == Zone.PLAY) back.Add(m); }
					}
					foreach (Minion m in back)
					{
						if (m.IsDead || m.ToBeDestroyed || m.Zone?.Type != Zone.PLAY) continue;
						var enemies = AttackableEnemies(c);
						if (enemies.Count == 0) break;
						EffectAttack(g, m, enemies[g.Random.Next(enemies.Count)]);
						g.DeathProcessingAndAuraUpdate();
					}
				})
			}));
			// [TOY_651] Lab Patron (4) — Whenever you gain Armor, summon another Lab Patron (once per turn). (per Patron — doubt)
			cards.Add("TOY_651", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ARMOR)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s[HsTag.ONCE_PER_TURN] == g.Turn) return;
						s[HsTag.ONCE_PER_TURN] = g.Turn;
						SummonNew(g, c, s.Card, s);
					})
				}
			}));
			// [TOY_906] Botface (7) — Taunt. After this takes damage, get two random Minis.
			cards.Add("TOY_906", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TAKE_DAMAGE)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						var minis = MiniCards();
						for (int i = 0; i < V("TOY_906", "minis", 2) && minis.Count > 0; i++) GetCard(c, minis[g.Random.Next(minis.Count)], s);
					})
				}
			}));
			// [TOY_907] Safety Goggles (2) — Gain 6 Armor. Costs (0) if you don't have any Armor.
			cards.Add("TOY_907", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => p.Controller.Hero.Armor > 0 ? 0 : p.Card.Cost),
				PowerTask = new ArmorTask(V("TOY_907", "armor", 6))
			}));
			// [TOY_908] Fireworker (5) — Deathrattle: Summon two 1/1 Boom Bots. WARNING: Bots may explode.
			cards.Add("TOY_908", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => { for (int i = 0; i < V("TOY_908", "bots", 2); i++) Summon(g, c, Cards.FromId("GVG_110t"), s); })
			}));

			// ---------------------------------------------------------------- Death Knight: The Headless Horseman (hero)
			// [TOY_829] The Headless Horseman (6, hero) — Battlecry: Destroy the enemy minion with the most Attack! Shuffle my Head into your deck.
			// Its Hero Power TOY_829hp3 (Deal 3 damage); the Head (Casts When Drawn) upgrades it to TOY_829hp (+ Discover an Undead).
			cards.Add("TOY_829", new CardDef(new Power { PowerTask = HeadlessHorseman }));
			cards.Add("TOY_829t2", new CardDef(new Power { PowerTask = HeadlessHorseman }));
			cards.Add("TOY_829t", new CardDef(new Power { TopdeckTask = new CustomTask((g, c, s, t, stack) => new ReplaceHeroPower(Cards.FromId("TOY_829hp")).Process(g, c, s, null)) }));
			cards.Add("TOY_829hp3", new CardDef(TargetAny, new Power { PowerTask = new DamageTask(V("TOY_829hp3", "damage", 3), EntityType.TARGET) }));
			cards.Add("TOY_829hp", new CardDef(TargetAny, new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("TOY_829hp", "damage", 3), EntityType.TARGET),
					DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.MINION && x.IsRace(Race.UNDEAD)))
			}));
		}
	}
}
