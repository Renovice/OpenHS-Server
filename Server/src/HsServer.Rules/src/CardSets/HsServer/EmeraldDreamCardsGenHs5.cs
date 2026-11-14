// HsServer addition (not upstream SabberStone): Into the Emerald Dream — chunk 4: Shaman, Warlock and Warrior (spec: research
// spec_chunk4), with the Shaman Imbue Hero Power. Shared pieces: Actions/Imbue.cs, Generic.PlayFromDeck (Ohn'ahra), Playable.
// CostsOpponentHealth (Agamaggan), Power.TransformCostsMore (Plucky Podling), Power.CopiesDarkGifts + DarkGiftGiven (Wallow),
// FireCounter(anywhere) (Keeper of Flame). Every number goes through CardValues (V).
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
		/// <summary>Collectible minions of the player's class or neutral matching <paramref name="where"/> (the Discover default pool).</summary>
		private static IEnumerable<Card> ClassOrNeutralMinions(Game g, Controller c, Func<Card, bool> where) =>
			Cards.GenerationPool(g, c).Where(x => x.Type == CardType.MINION && x.Collectible &&
				(x.HasClass(c.HeroClass) || x.HasClass(CardClass.NEUTRAL)) && where(x));

		/// <summary>The player's minions (not Locations) on the battlefield.</summary>
		private static List<ICharacter> BoardMinions(Controller c) =>
			c.BoardZone.GetAll().Where(m => !(m is Location) && !m.IsDead).Cast<ICharacter>().ToList();

		/// <summary>The friendly minions that died this turn, in death order (the last NumFriendlyMinionsThatDiedThisTurn deaths).</summary>
		private static List<IPlayable> DiedThisTurn(Controller c)
		{
			var dead = c.DiedThisGameCards.Where(p => p is Minion).ToList();
			int n = Math.Min(dead.Count, c.NumFriendlyMinionsThatDiedThisTurn);
			return dead.Skip(dead.Count - n).ToList();
		}

		private static void EmeraldChunk4(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- the Shaman Imbue Hero Power
			// [EDR_448p] Blessing of the Wind (2) — Transform a friendly minion into a random one that costs (@) more. (the evolve rules;
			// live darkmoon 76334: CREATOR / DISPLAYED_CREATOR = the Hero Power)
			cards.Add("EDR_448p", new CardDef(TargetFriendlyMinionToPlay, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m) || !(Cards.EvolvePick(g.FormatType, m, Math.Max(1, s[GameTag.TAG_SCRIPT_DATA_NUM_1]), g.Random, c) is Card into)) return;
					IPlayable changed = Generic.ChangeEntityBlock(c, m, into, true);
					if (changed != null) { changed[GameTag.CREATOR] = s.Id; changed[GameTag.DISPLAYED_CREATOR] = s.Id; }
				})
			}));

			// ---------------------------------------------------------------- the Rogue / Death Knight Imbue Hero Powers (TIME_TRAVEL cards)
			// [END_000p] Blessing of the Bronze (1) — Rewind. Get a random minion from another class. It costs (@) less.
			// (Rewind: RewindPower — Keep / Rewind; the adapter rewinds a Hero Power use too. Unrecorded for a Hero Power: live-checks.)
			cards.Add("END_000p", new CardDef(new Power
			{
				PowerTask = RewindPower((g, c, s, t) =>
				{
					if (c.HandZone.IsFull || !(Pick(g, OtherClassCards(g, c).Where(x => x.Type == CardType.MINION), 1).FirstOrDefault() is Card card)) return;
					IPlayable got = Entity.FromCard(c, card, new EntityData { [GameTag.CREATOR] = s.Id, [GameTag.DISPLAYED_CREATOR] = s.Id });
					if (!Generic.AddHandPhase.Invoke(c, got)) return;
					int n = Math.Max(1, s[GameTag.TAG_SCRIPT_DATA_NUM_1]);
					if (GenericCostLess(n) is string less) Give(g, s, got, less);
					else for (int i = 0; i < n; i++) Give(g, s, got, "GBL_003e");
				})
			}));
			// [END_003p] Blessing of the Infinite — Passive. The first Undead you play each turn gains +@ Attack.
			cards.Add("END_003p", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.PLAY_MINION)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p is Minion m && m.IsRace(Race.UNDEAD) &&
						p.Controller.PlayHistory.Count(h => h.Turn == p.Game.Turn && h.SourceCard?.Type == CardType.MINION && h.SourceCard.IsRace(Race.UNDEAD)) == 1),
					SingleTask = new CustomTask((g, c, s, t, stack) => { if (t is Minion m) Give(g, s, m, "END_003pe", Math.Max(1, s[GameTag.TAG_SCRIPT_DATA_NUM_1])); })
				}
			}));
			cards.Add("END_003pe", new CardDef(new Power { Enchant = Enchants.Enchants.AddAttackScriptTag }));

			// ---------------------------------------------------------------- Shaman
			// [EDR_031] Ohn'ahra (9) — At the end of your turn, play the top 3 cards from your deck. (Generic.PlayFromDeck; an empty
			// deck stops it: not a draw)
			cards.Add("EDR_031", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						var played = new List<int>();
						for (int i = 0; i < V("EDR_031", "cards", 3); i++)
						{
							if (!(c.DeckZone.TopCard is IPlayable top) || c.Hero.IsDead || c.Opponent.Hero.IsDead) break;
							played.Add(top.Id);
							Generic.PlayFromDeck(c, s, top);
							g.DeathProcessingAndAuraUpdate();
						}
						// (live install 195600 9127: its tile naming itself and the cards it played, at the end of its block)
						if (played.Count > 0) Generic.SelfTriggerTile((IPlayable)s, played);
					})
				}
			}));
			// [EDR_230] Beanstalk Brute (5) — Battlecry: Give +4/+4 to the top 3 minions in your deck. (from the top, not random)
			cards.Add("EDR_230", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var deck = c.DeckZone.GetAll().ToList();
					deck.Reverse(); // the top first
					foreach (IPlayable m in deck.Where(p => p is Minion).Take(V("EDR_230", "minions", 3)).ToList()) Give(g, s, m, "EDR_230e");
				})
			}));
			cards.Add("EDR_230e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("EDR_230", "stats", 4))) }));
			// [EDR_231] Aspect's Embrace (2) — Restore #4 Health. Draw a card. Imbue your Hero Power.
			cards.Add("EDR_231", new CardDef(TargetAny, new Power
			{
				PowerTask = ComplexTask.Create(
					new HealTask(V("EDR_231", "heal", 4), EntityType.TARGET),
					new CustomTask((g, c, s, t, stack) => { Generic.Draw(c); Generic.Imbue(c, s); }))
			}));
			// [EDR_232] Typhoon (10) — Each minion gets shuffled into a random player's deck. (Generic.MoveToDeck: owner change, backward
			// move, a full deck kills it)
			cards.Add("EDR_232", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll()).Where(m => !(m is Location)).ToList())
						Generic.MoveToDeck(g.Random.Next(2) == 0 ? c : c.Opponent, s, m);
				})
			}));
			// [EDR_233] Spirits of the Forest (5) — Choose One - Summon three 2/3 Wolves with Taunt; or Summon two 4/3 Falcons with Windfury.
			cards.Add("EDR_233", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power()));
			cards.Add("EDR_233a", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { for (int i = 0; i < V("EDR_233", "wolves", 3); i++) SummonGet(g, c, Cards.FromId("EX1_tk11"), s); })
			}));
			cards.Add("EDR_233b", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { for (int i = 0; i < V("EDR_233", "falcons", 2); i++) SummonGet(g, c, Cards.FromId("EDR_233t2"), s); })
			}));
			// [EDR_234] Emerald Bounty (2) — Draw 2 cards. You can't play them for 2 turns. (this turn and your next: doubt, unrecorded)
			cards.Add("EDR_234", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("EDR_234", "draws", 2); i++)
						if (Generic.Draw(c) is IPlayable drawn && drawn.Zone?.Type == Zone.HAND)
							Generic.LockInHand(drawn, s, g.Turn + 2, "EDR_234e2");
				})
			}));
			cards.Add("EDR_234e2", new CardDef(new Power { NoScript = true }));
			// [EDR_238] Merithra (6) — Battlecry: Resurrect all different friendly minions that cost (8) or more.
			cards.Add("EDR_238", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var groups = c.DiedThisGameCards.Where(p => p is Minion && p.Card.Cost >= V("EDR_238", "cost", 8)).GroupBy(p => p.Card.Id)
						.Select(x => x.ToList()).OrderBy(_ => g.Random.Next()).ToList();
					foreach (var same in groups)
					{
						if (c.BoardZone.IsFull) break;
						Generic.SummonBlock.Invoke(g, (Minion)Generic.NewFromGraveyard(c, same[g.Random.Next(same.Count)]), -1, s);
					}
				})
			}));
			// [EDR_477] Glowroot Lure (6) — Taunt. Costs (1) less for each time you used your Hero Power this game.
			cards.Add("EDR_477", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => p.Controller.NumTimesHeroPowerUsedThisGame * V("EDR_477", "discount", 1))
			}));
			// [EDR_518] Living Garden (3) — Battlecry: Imbue your Hero Power. Reduce the Cost of a minion in your hand by (1). (a hand
			// target; live: the Imbue first)
			cards.Add("EDR_518", new CardDef(new Power
			{
				HandTarget = SelfCondition.IsMinion,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Generic.Imbue(c, s);
					if (t != null && t.Zone?.Type == Zone.HAND && t.Controller == c) Give(g, s, t, GenericCostLess(V("EDR_518", "less", 1)));
				})
			}));
			// [EDR_529] Plucky Podling (1) — If this would transform into a minion, it transforms into one that costs (2) more.
			cards.Add("EDR_529", new CardDef(new Power { TransformCostsMore = V("EDR_529", "more", 2) }));
			// [FIR_778] Avatar of Destruction (9) — Taunt. Deathrattle: Deal 9 damage to all enemy minions.
			cards.Add("FIR_778", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => DamageAll(s, BoardMinions(c.Opponent), V("FIR_778", "damage", 9), false))
			}));
			// [FIR_923] Flames of the Firelord (2) — Deal $4 damage to a random enemy minion. If you're holding a card that costs (8) or
			// more, deal $8 instead.
			cards.Add("FIR_923", new CardDef(new Power
			{
				GlowWhile = p => p.Controller.HandZone.Any(x => x.Id != p.Id && x.Cost >= V("FIR_923", "card cost", 8)),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var enemies = BoardMinions(c.Opponent);
					if (enemies.Count == 0) return;
					int amount = c.HandZone.Any(x => x.Cost >= V("FIR_923", "card cost", 8)) ? V("FIR_923", "more", 8) : V("FIR_923", "damage", 4);
					Generic.DamageCharFunc((IPlayable)s, enemies[g.Random.Next(enemies.Count)], amount, true);
				})
			}));
			// [FIR_927] Emberscarred Whelp (3) — Battlecry: Discover a 5-Cost card. Gain 1 Mana Crystal next turn only. (live darkmoon
			// 44205: FIR_921e "Mana Bloom" on the player after the pick; next turn it gives the Mana and leaves)
			cards.Add("FIR_927", new CardDef(new Power
			{
				PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.Cost == V("FIR_927", "cost", 5), afterPick: new CustomTask((g, c, s, t, stack) =>
					Generic.AddEnchantmentBlock(g, Cards.FromId("FIR_921e"), (IPlayable)s, c, 0, 0, 0)))
			}));
			cards.Add("FIR_921e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						c.TemporaryMana += V("FIR_927", "mana", 1);
						if (s is Enchantment e) e.Remove();
					})
				}
			}));

			// ---------------------------------------------------------------- Warlock
			// [EDR_482] Rotten Apple (2) — Restore #12 Health to your hero. For the next 2 turns, deal $3 damage to your hero.
			// (live s20261001-130515 96418: the first hit at the end of the cast turn, the second at the end of your next)
			cards.Add("EDR_482", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					new HealTask(V("EDR_482", "heal", 12), EntityType.HERO),
					new CustomTask((g, c, s, t, stack) => Generic.AddEnchantmentBlock(g, Cards.FromId("EDR_482e"), (IPlayable)s, c, 0, 0, 0)))
			}));
			cards.Add("EDR_482e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					EffectIndex = 1,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Enchantment e)) return;
						e[GameTag.TAG_SCRIPT_DATA_NUM_1] = e[GameTag.TAG_SCRIPT_DATA_NUM_1] + 1;
						Generic.DamageCharFunc(e, c.Hero, V("EDR_482", "damage", 3), true);
						if (e[GameTag.TAG_SCRIPT_DATA_NUM_1] >= V("EDR_482", "turns", 2)) e.Remove();
					})
				}
			}));
			// [EDR_483] Fractured Power (2) — Destroy one of your Mana Crystals. In 2 turns, gain two. (counted at the start of your turns:
			// Rhythm and Roots' live count; doubt)
			cards.Add("EDR_483", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					new ManaCrystalEmptyTask(-V("EDR_483", "destroy", 1)),
					new CustomTask((g, c, s, t, stack) => Generic.AddEnchantmentBlock(g, Cards.FromId("EDR_483e"), (IPlayable)s, c, 0, 0, 0)))
			}));
			cards.Add("EDR_483e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Enchantment e)) return;
						e[GameTag.TAG_SCRIPT_DATA_NUM_1] = e[GameTag.TAG_SCRIPT_DATA_NUM_1] + 1;
						if (e[GameTag.TAG_SCRIPT_DATA_NUM_1] < V("EDR_483", "turns", 2)) return;
						new ManaCrystalFullTask(V("EDR_483", "gain", 2)).Process(g, c, e, null);
						e.Remove();
					})
				}
			}));
			// [EDR_485] Rotheart Dryad (1) — Deathrattle: Draw a minion that costs (7) or more.
			cards.Add("EDR_485", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => DrawWhere(g, c, p => p is Minion && p.Cost >= V("EDR_485", "cost", 7)))
			}));
			// [EDR_487] Wallow, the Wretched (7) — While this is in your hand or deck, it gains a copy of every Dark Gift given to your minions.
			cards.Add("EDR_487", new CardDef(new Power { CopiesDarkGifts = true }));
			// [EDR_488] Avant-Gardening (2) — Discover a Deathrattle minion with a Dark Gift.
			cards.Add("EDR_488", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => DiscoverWithDarkGift(g, c, s, ClassOrNeutralMinions(g, c, x => x[GameTag.DEATHRATTLE] > 0)))
			}));
			// [EDR_489] Agamaggan (10) — Battlecry: The next card you play costs your OPPONENT'S Health instead of Mana (up to 10).
			// (Playable.CostsOpponentHealth; live: EDR_489e1 on the player, AGAMAGGAN_CURSE on the opponent's hero)
			cards.Add("EDR_489", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					c[HsTag.NEXT_CARD_COSTS_OPPONENT_HEALTH] = V("EDR_489", "up to", 10);
					c.Opponent.Hero[Generic.AGAMAGGAN_CURSE] = 1;
					Generic.AddEnchantmentBlock(g, Cards.FromId("EDR_489e1"), (IPlayable)s, c, 0, 0, 0);
				})
			}));
			cards.Add("EDR_489e1", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.PLAY_CARD)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) => { if (s is Enchantment e && c[HsTag.NEXT_CARD_COSTS_OPPONENT_HEALTH] == 0) e.Remove(); })
				}
			}));
			cards.Add("EDR_489e2", new CardDef(new Power { NoScript = true }));
			// [EDR_490] Sleep Paralysis (5) — Choose One - Summon two 3/6 Demons with Taunt that can't attack; or Destroy an enemy minion.
			cards.Add("EDR_490", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_IF_AVAILABLE, 0 }, { PlayReq.REQ_ENEMY_TARGET, 0 }, { PlayReq.REQ_MINION_TARGET, 0 } }, new Power()));
			cards.Add("EDR_490a", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { for (int i = 0; i < V("EDR_490", "demons", 2); i++) SummonGet(g, c, Cards.FromId("EDR_490t"), s); })
			}));
			cards.Add("EDR_490b", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_ENEMY_TARGET, 0 }, { PlayReq.REQ_MINION_TARGET, 0 } },
				new Power { PowerTask = new DestroyTask(EntityType.TARGET) }));
			// [EDR_491] Archdruid of Thorns (2) — Battlecry: Gain the Deathrattles of your minions that died this turn. (EDR_491e captures
			// each dead minion's card — the AV_317e pattern; their own Deathrattles)
			cards.Add("EDR_491", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Minion me) || me.Zone?.Type != Zone.PLAY) return;
					foreach (IPlayable dead in DiedThisTurn(c).Where(p => p.Card[GameTag.DEATHRATTLE] > 0 && p.Card.Power?.DeathrattleTask != null))
						Generic.AddEnchantmentBlock(g, Cards.FromId("EDR_491e"), me, me, dead.Id, 0, dead.Id);
				})
			}));
			cards.Add("EDR_491e", new CardDef(new Power
			{
				Enchant = new Enchant(new Effect(GameTag.DEATHRATTLE, EffectOperator.SET, 1)),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is Enchantment e && e.CapturedCard?.Power?.DeathrattleTask is ISimpleTask copied) copied.Process(g, c, s, null);
				})
			}));
			// [EDR_494] Hungering Ancient (8) — At the end of your turn, eat a minion in your deck and gain its stats. Deathrattle: Add them
			// to your hand. (the eaten card leaves the deck for the graveyard — not a death; new copies come back)
			cards.Add("EDR_494", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Minion me) || me.Zone?.Type != Zone.PLAY) return;
						var minions = c.DeckZone.GetAll().Where(p => Generic.IsMinionCard(p)).ToList();
						if (minions.Count == 0) return;
						var eaten = (Minion)minions[g.Random.Next(minions.Count)];
						int atk = eaten.AttackDamage, health = eaten.Health;
						c.GraveyardZone.Add(c.DeckZone.Remove(eaten));
						Generic.AddEnchantmentBlock(g, Cards.FromId("EDR_494e"), me, me, atk, health, eaten.Id);
						int n = me[HsTag.EATEN_COUNT];
						if (n < 16) { me[HsTag.EATEN_BASE + n] = eaten.Id; me[HsTag.EATEN_COUNT] = n + 1; }
					})
				},
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < s[HsTag.EATEN_COUNT]; i++)
						if (g.IdEntityDic.TryGetValue(s[HsTag.EATEN_BASE + i], out IPlayable eaten)) AddToHand(c, eaten.Card);
				})
			}));
			cards.Add("EDR_494e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(0), Effects.Health_N(0)) { UseScriptTag = true } }));
			// [EDR_654] Overgrown Horror (4) — Taunt. Battlecry: Reduce the Cost of minions in your hand with Dark Gifts by (2).
			cards.Add("EDR_654", new CardDef(new Power
			{
				GlowWhile = p => p.Controller.HandZone.Any(x => Generic.IsMinionCard(x) && HasDarkGift(x)),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					GiveAll(g, s, c.HandZone.GetAll().Where(x => Generic.IsMinionCard(x) && HasDarkGift(x)).ToList(), GenericCostLess(V("EDR_654", "less", 2))))
			}));
			// [FIR_924] Shadowflame Stalker (4) — Battlecry: Discover a Demon with a Dark Gift. Get a copy of it.
			cards.Add("FIR_924", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => DiscoverWithDarkGift(g, c, s, ClassOrNeutralMinions(g, c, x => x.IsRace(Race.DEMON)), picked =>
				{
					if (picked.Zone?.Type != Zone.HAND || c.HandZone.IsFull) return;
					if (Generic.Copy(picked.Controller, s, picked, Zone.HAND) is IPlayable copy && HasDarkGift(copy)) copy[(GameTag)3880] = 1;
				}))
			}));
			// [FIR_954] Conflagrate (1) — Deal $5 damage to a minion. Its owner draws a card.
			cards.Add("FIR_954", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					Controller owner = m.Controller;
					Generic.DamageCharFunc((IPlayable)s, m, V("FIR_954", "damage", 5), true);
					Generic.Draw(owner);
				})
			}));
			// [FIR_955] Emberroot Destroyer (3) — Whenever your hero takes damage on your turn, deal 3 damage to a random enemy minion.
			cards.Add("FIR_955", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TAKE_DAMAGE)
				{
					TriggerSource = TriggerSource.HERO,
					Condition = new SelfCondition(p => p.Game.CurrentPlayer == p.Controller),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						var enemies = BoardMinions(c.Opponent).Where(m => m.Health > 0 && !m.ToBeDestroyed).ToList();
						if (enemies.Count > 0) Generic.DamageCharFunc((IPlayable)s, enemies[g.Random.Next(enemies.Count)], V("FIR_955", "damage", 3), false);
					})
				}
			}));

			// ---------------------------------------------------------------- Warrior
			// [EDR_454] Clutch of Corruption (2) — Location: Choose a friendly Dragon. Summon a 0/2 Egg that hatches into a copy of it.
			// (the Egg's TAG_SCRIPT_DATA_NUM_1 = the Dragon: its text names it; it hatches into a copy of it then — doubt: copy at
			// activation vs at hatch)
			cards.Add("EDR_454", new CardDef(new Dictionary<PlayReq, int>
				{ { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_TARGET_WITH_RACE, (int)Race.DRAGON } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion dragon) || c.BoardZone.IsFull) return;
					var egg = (Minion)Entity.FromCard(c, Cards.FromId("EDR_454t"), new EntityData { [GameTag.CREATOR] = s.Id, [GameTag.TAG_SCRIPT_DATA_NUM_1] = dragon.Id });
					Generic.SummonBlock.Invoke(g, egg, -1, s);
				})
			}));
			cards.Add("EDR_454t", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c.BoardZone.IsFull || !g.IdEntityDic.TryGetValue(s[GameTag.TAG_SCRIPT_DATA_NUM_1], out IPlayable dragon) || dragon.Card.Type != CardType.MINION) return;
					Generic.Copy(c, s, dragon, Zone.PLAY);
				})
			}));
			// [EDR_455] Succumb to Madness (3) — Discover a friendly Dragon that died this game. Resummon it.
			cards.Add("EDR_455", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var deadIds = c.DiedThisGameCards.Where(p => p is Minion m && m.IsRace(Race.DRAGON)).Select(p => p.Id).ToList();
					Card[] offer = DiscoverFromTask.PickDistinct(g, deadIds.Select(id => g.IdEntityDic[id].Card), 3).ToArray();
					if (offer.Length == 0) return;
					Generic.CreateChoiceCards.Invoke(c, s, null, ChoiceType.GENERAL, ChoiceAction.STACK, offer, new CustomTask((g2, c2, s2, pick, st) =>
					{
						if (pick == null) return;
						var same = deadIds.Where(id => g2.IdEntityDic.TryGetValue(id, out IPlayable d) && d.Card.Id == pick.Card.Id).ToList();
						if (pick.Zone?.Type == Zone.SETASIDE) c2.GraveyardZone.Add(pick.Zone.Remove(pick));
						if (same.Count == 0 || c2.BoardZone.IsFull) return;
						Generic.SummonBlock.Invoke(g2, (Minion)Generic.NewFromGraveyard(c2, g2.IdEntityDic[same[g2.Random.Next(same.Count)]]), -1, s2);
					}));
				})
			}));
			// [EDR_456] Darkrider (1) — Battlecry: If you're holding a Dragon, Discover a Dragon with a Dark Gift.
			cards.Add("EDR_456", new CardDef(new Power
			{
				PowerTask = IfHoldingDragon(new CustomTask((g, c, s, t, stack) =>
					DiscoverWithDarkGift(g, c, s, ClassOrNeutralMinions(g, c, x => x.IsRace(Race.DRAGON)))))
			}));
			// [EDR_457] Brood Keeper (2) — Battlecry: If you're holding a Dragon, equip a 2/2 Sword.
			cards.Add("EDR_457", new CardDef(new Power { PowerTask = IfHoldingDragon(new WeaponTask("EDR_457t")) }));
			// [EDR_459] Afflicted Devastator (4) — Battlecry: Deal 3 damage to all other friendly minions. Deathrattle: Deal 3 damage to all
			// enemy minions.
			cards.Add("EDR_459", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					DamageAll(s, BoardMinions(c).Where(m => m.Id != s.Id).ToList(), V("EDR_459", "damage", 3), false)),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => DamageAll(s, BoardMinions(c.Opponent), V("EDR_459", "deathrattle", 3), false))
			}));
			// [EDR_465] Ysondre (7) — Taunt. Deathrattle: Summon a random Dragon for each time Ysondre has died this game. (this death counts)
			cards.Add("EDR_465", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					int deaths = Math.Max(1, c.DiedThisGameCards.Count(p => p.Card.Id == ((IPlayable)s).Card.Id));
					for (int i = 0; i < deaths && !c.BoardZone.IsFull; i++)
						SummonGet(g, c, RandomCard(g, x => x.Type == CardType.MINION && x.IsRace(Race.DRAGON) && !x.NeverRandomlyOnBoard), s);
				})
			}));
			// [EDR_468] Eggbasher (4) — Battlecry: Deal 1 damage to a minion and give it +4 Attack.
			cards.Add("EDR_468", new CardDef(BattlecryMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					Generic.DamageCharFunc((IPlayable)s, m, V("EDR_468", "damage", 1), false);
					if (m.Zone?.Type == Zone.PLAY && !m.ToBeDestroyed && m.Health > 0) Give(g, s, m, "EDR_468e1");
				})
			}));
			cards.Add("EDR_468e1", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("EDR_468", "attack", 4))) }));
			// [EDR_471] Tortolla (10) — Taunt, Elusive. After this takes damage, gain 1 Armor and give this minion +1 Attack.
			cards.Add("EDR_471", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TAKE_DAMAGE)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						new ArmorTask(V("EDR_471", "armor", 1)).Process(g, c, s, null);
						if (s is Minion me) Give(g, s, me, "EDR_471e");
					})
				}
			}));
			cards.Add("EDR_471e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("EDR_471", "attack", 1))) }));
			// [EDR_531] Siphoning Growth (1) — Destroy a friendly minion to gain 8 Armor.
			cards.Add("EDR_531", new CardDef(TargetFriendlyMinionToPlay, new Power
			{
				PowerTask = ComplexTask.Create(new DestroyTask(EntityType.TARGET), new ArmorTask(V("EDR_531", "armor", 8)))
			}));
			// [EDR_570] Ominous Nightmares (1) — Choose One - Deal $1 damage to all minions; or Give a damaged minion +2/+2.
			cards.Add("EDR_570", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_IF_AVAILABLE, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_DAMAGED_TARGET, 0 } }, new Power()));
			cards.Add("EDR_570A", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => DamageAll(s, BoardMinions(c).Concat(BoardMinions(c.Opponent)).ToList(), V("EDR_570", "damage", 1), true))
			}));
			cards.Add("EDR_570B", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_DAMAGED_TARGET, 0 } },
				new Power { PowerTask = new AddEnchantmentTask("EDR_570e", EntityType.TARGET) }));
			cards.Add("EDR_570e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("EDR_570", "stats", 2))) }));
			// [FIR_928] Keeper of Flame (5) — Battlecry: Give all minions in your hand +3/+3. They are destroyed in 3 turns. (the Immolate
			// count: the end of each of your turns, this one first — user ruling 2026-10-06; they burn wherever they are)
			cards.Add("FIR_928", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var minions = c.HandZone.GetAll().Where(p => Generic.IsMinionCard(p)).ToList();
					if (minions.Count == 0) return;
					GiveAll(g, s, minions, "FIR_928e");
					GiveAll(g, s, minions, "FIR_928e3");
					Generic.AddEnchantmentBlock(g, Cards.FromId("FIR_928e2"), (IPlayable)s, c, 0, 0, 0);
				})
			}));
			cards.Add("FIR_928e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("FIR_928", "stats", 3))) }));
			cards.Add("FIR_928e3", new CardDef(new Power()));
			cards.Add("FIR_928e2", FireCounter("FIR_928e3", "FIR_928", opponentsHand: false, anywhere: true));
			// [FIR_939] Shadowflame Suffusion (2) — Deal $2 damage. Discover a Warrior minion with a Dark Gift. (live pool: Warrior only)
			cards.Add("FIR_939", new CardDef(TargetAny, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is ICharacter ch) Generic.DamageCharFunc((IPlayable)s, ch, V("FIR_939", "damage", 2), true);
					DiscoverWithDarkGift(g, c, s, Cards.GenerationPool(g, c).Where(x => x.Type == CardType.MINION && x.Collectible && x.HasClass(CardClass.WARRIOR)));
				})
			}));
			// [FIR_956] Dragon Turtle (4) — Battlecry: If you're holding a minion with a Dark Gift, give your hero +3 Attack this turn and 6 Armor.
			cards.Add("FIR_956", new CardDef(new Power
			{
				GlowWhile = p => p.Controller.HandZone.Any(x => Generic.IsMinionCard(x) && HasDarkGift(x)),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!c.HandZone.Any(x => Generic.IsMinionCard(x) && HasDarkGift(x))) return;
					Give(g, s, c.Hero, "FIR_956e");
					new ArmorTask(V("FIR_956", "armor", 6)).Process(g, c, s, null);
				})
			}));
			cards.Add("FIR_956e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("FIR_956", "attack", 3))) { IsOneTurnEffect = true } }));
		}
	}
}
