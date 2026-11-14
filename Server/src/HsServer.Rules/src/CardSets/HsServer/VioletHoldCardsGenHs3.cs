// HsServer addition (not upstream SabberStone): Escape from Violet Hold (VIOLET_HOLD, set 1988) — chunk 3: Rogue, Priest, Paladin (spec:
// research spec_chunk3). Shared pieces (Actions/VioletHold.cs): full-Health Reborn (Game.RebornTask), "Reborn this game", a Stealthed
// attacker (HsTag.ATTACKED_STEALTHED), copies of the opponent's cards (Generic.Copy), "played for 2 Mana" (PayPhase), Aya's counterfeit
// Coins (AddHandPhase), the Follow the X effect, "After this gains stats" (Power.ExtraOnStatGain), "End your turn" (Game.EndTurnPending).
// Every number goes through CardValues (V).
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
		/// <summary>"Whenever a friendly Stealthed minion attacks, …" (SI:7 Slayer, Mathias Shaw): the attacker, as its attack begins.</summary>
		private static Trigger StealthedAttacks(Action<Game, Controller, IEntity, Minion> effect) => new Trigger(TriggerType.ATTACK)
		{
			TriggerSource = TriggerSource.MINIONS,
			SingleTask = new CustomTask((g, c, s, t, stack) =>
			{
				if (g.CurrentEventData?.EventSource is Minion attacker && attacker.Controller == c && Generic.AttackedStealthed(attacker))
					effect(g, c, s, attacker);
			})
		};

		/// <summary>"It costs (N) less" with the client's generic cost enchantments (GBL "Cost - N"; past 7: N times "Cost - 1"; live Jade
		/// Guardians: a hidden enchantment per card).</summary>
		private static void GiveCostLess(Game g, IEntity source, IPlayable card, int n)
		{
			if (n <= 0 || card == null) return;
			if (GenericCostLess(n) is string less) Give(g, source, card, less);
			else for (int i = 0; i < n; i++) Give(g, source, card, "GBL_003e");
		}

		/// <summary>The Jade Coin's Golems (JAIL_504tt01/04/07/20: the 1, 4, 7 and 20 art; live p130515 5701).</summary>
		private static readonly string[] LotusJadeGolems = { "JAIL_504tt01", "JAIL_504tt04", "JAIL_504tt07", "JAIL_504tt20" };

		/// <summary>"Get a random 1-Cost Kazakus Potion" (Kabal Coin): JAIL_504t3p with two different random 1-Cost ingredients (Kazakus's);
		/// its "{0} {1}" are the ingredients (TAG_SCRIPT_DATA_NUM_1 / _2: their dbf ids — doubt, unrecorded).</summary>
		private static void GetKazakusPotion(Controller c, IEntity source)
		{
			Game g = c.Game;
			var pool = KazakusPower.OneCostIngredients().ToList();
			if (pool.Count < 2 || !(Cards.TryFromId("JAIL_504t3p") is Card potion)) return;
			Card a = pool[g.Random.Next(pool.Count)];
			pool.Remove(a);
			Card b = pool[g.Random.Next(pool.Count)];
			if (a[GameTag.TAG_SCRIPT_DATA_NUM_1] > b[GameTag.TAG_SCRIPT_DATA_NUM_1]) (a, b) = (b, a);
			Card made = Card.CreateKazakusPotion(potion, a, b, g.History);
			IPlayable p = Entity.FromCard(c, made, new EntityData
			{
				{ GameTag.CREATOR, source.Id }, { GameTag.DISPLAYED_CREATOR, source.Id },
				{ GameTag.TAG_SCRIPT_DATA_NUM_1, a.AssetId }, { GameTag.TAG_SCRIPT_DATA_NUM_2, b.AssetId },
			});
			Generic.AddHandPhase.Invoke(c, p);
		}

		/// <summary>A Lotus counterfeit Coin: The Coin's Mana, then its own effect.</summary>
		private static CardDef LotusCoin(ISimpleTask then) => new CardDef(new Power { PowerTask = ComplexTask.Create(new TempManaTask(1), then) });

		private static void VioletHoldChunk3(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Rogue
			// [CAP_000] SI:7 Slayer (3) — Stealth. Whenever a friendly Stealthed minion attacks, give it +2/+2. (in the preparation: before damage)
			cards.Add("CAP_000", new CardDef(new Power { Trigger = StealthedAttacks((g, c, s, attacker) => Give(g, s, attacker, "CAP_000e")) }));
			cards.Add("CAP_000e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("CAP_000", "stats", 2))) }));
			// [CAP_001] Silent Strike (2) — Give a minion +3 Attack. If it's Stealthed, deal its Attack damage to a random enemy minion.
			cards.Add("CAP_001", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					Give(g, s, m, "CAP_001e");
					if (!m.HasStealth) return;
					var enemies = c.Opponent.BoardZone.GetAll().Where(x => !x.IsDead && !x.ToBeDestroyed && !x.Untouchable).ToList();
					if (enemies.Count > 0) Generic.DamageCharFunc((IPlayable)s, enemies[g.Random.Next(enemies.Count)], m.AttackDamage, false);
				})
			}));
			cards.Add("CAP_001e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("CAP_001", "attack", 3))) }));
			// [CAP_002] Follow the Footsteps (1) — Discover a Stealth minion. Give it this effect for a turn.
			cards.Add("CAP_002", new CardDef(new Power
			{
				PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.MINION && x[GameTag.STEALTH] > 0, null,
					new CustomTask((g, c, s, t, stack) => Generic.GiveFollowEffect(s, t, Cards.FromId("CAP_002"), "CAP_002e")))
			}));
			cards.Add("CAP_002e", new CardDef(new Power { Enchant = new Enchant { IsOneTurnEffect = true } }));
			// [CAP_101e2] Following the X Player Enchant — this turn: after a card carrying a Follow effect is played, that effect.
			cards.Add("CAP_101e2", new CardDef(new Power
			{
				Enchant = new Enchant { IsOneTurnEffect = true },
				Trigger = new Trigger(TriggerType.AFTER_PLAY_CARD)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) => Generic.RunFollowEffects(c, t))
				}
			}));
			// [CAP_003] SI:7 Supplier (2) — Stealth. After this attacks, draw a card. (live 204312 35797: effect index 2)
			cards.Add("CAP_003", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK) { TriggerSource = TriggerSource.SELF, EffectIndex = 2, SingleTask = new DrawTask() }
			}));
			// [CAP_004] Disguised Operator (1) — Can be played on either side. Rush. Deathrattle: Your opponent draws 2 cards. (its controller's
			// opponent: live 183021 13520)
			cards.Add("CAP_004", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => { for (int i = 0; i < V("CAP_004", "cards", 2); i++) Generic.Draw(c.Opponent); })
			}));
			// [CAP_005] Mathias Shaw (4) — Stealth. Whenever a friendly Stealthed minion attacks, reduce the Cost of a random card in your hand by (3).
			cards.Add("CAP_005", new CardDef(new Power
			{
				Trigger = StealthedAttacks((g, c, s, attacker) =>
				{
					var hand = c.HandZone.GetAll();
					if (hand.Length > 0) Give(g, s, hand[g.Random.Next(hand.Length)], "CAP_005e4");
				})
			}));
			cards.Add("CAP_005e4", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("CAP_005", "discount", 3))) }));
			// [CAP_006] Tricks of the Trade (1) — Deal $1 damage. If a Stealthed minion attacked while holding this, deal $3 instead. (any side's
			// minion: the text says "a" — doubt)
			cards.Add("CAP_006", new CardDef(TargetAny, new Power
			{
				Trigger = WhileHolding(TriggerType.ATTACK, p => p is Minion && Generic.AttackedStealthed(p), TriggerSource.ALL),
				ThresholdValue = 1,
				PowerTask = ComplexTask.If((g, c, s) => Generic.ThresholdMet(s),
					new DamageTask(V("CAP_006", "more", 3), EntityType.TARGET, true), new DamageTask(V("CAP_006", "damage", 1), EntityType.TARGET, true))
			}));
			// [JAIL_225] Nab (2) — Deal $3 damage to a minion. If it dies, shuffle a copy of it into your deck that costs (2). (a new card)
			cards.Add("JAIL_225", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("JAIL_225", "damage", 3), EntityType.TARGET, true),
					new CustomTask((g, c, s, t, stack) =>
					{
						if (!(t is Minion m) || !(m.IsDead || m.ToBeDestroyed)) return;
						IPlayable copy = Entity.FromCard(c, m.Card, new EntityData { { GameTag.CREATOR, s.Id } });
						Generic.AddEnchantmentBlock(g, Cards.FromId("GBL_006e"), (IPlayable)s, copy, 0, 0, 0);
						Generic.ShuffleIntoDeck.Invoke(c, s, copy);
					}))
			}));
			// [JAIL_470] Lotus Troublemaker (3) — Battlecry: Deal 1 damage to a random enemy. Shoot @ times! (While in hand or deck play cards for 2
			// Mana to shoot more!) (live 115158: an empty TRIGGER effect index 3 after each play while held, NUM_1 + 1 for a 2-Mana play)
			cards.Add("JAIL_470", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_CARD)
				{
					TriggerActivation = TriggerActivation.HAND_OR_DECK,
					TriggerSource = TriggerSource.FRIENDLY,
					EffectIndex = 3,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is IPlayable me && t?.Id != me.Id && Generic.PlayedFor2Mana(t))
							me[GameTag.TAG_SCRIPT_DATA_NUM_1] = me[GameTag.TAG_SCRIPT_DATA_NUM_1] + 1;
					})
				},
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var me = (IPlayable)s;
					int shots = Math.Max(1, me[GameTag.TAG_SCRIPT_DATA_NUM_1]);
					me[GameTag.TAG_SCRIPT_DATA_NUM_1] = me.Card[GameTag.TAG_SCRIPT_DATA_NUM_1];
					for (int i = 0; i < shots; i++)
					{
						var enemies = Enemies(c).Where(x => !x.IsDead && !x.ToBeDestroyed && x.Health > 0).ToList();
						if (enemies.Count == 0) break;
						ICharacter victim = enemies[g.Random.Next(enemies.Count)];
						Generic.MissileFx(me, victim, () => Generic.DamageCharFunc(me, victim, V("JAIL_470", "damage", 1), false));
					}
				})
			}));
			// [JAIL_474] Jade Guardians (5) — Get two random 8-Cost minions. They cost (1) less for each card you played for 2 Mana this game.
			cards.Add("JAIL_474", new CardDef(new Power
			{
				HandCounter = p => p.Controller[HsTag.PLAYED_FOR_2_MANA],
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int less = c[HsTag.PLAYED_FOR_2_MANA];
					for (int i = 0; i < V("JAIL_474", "minions", 2); i++)
						if (GetCard(c, RandomCard(g, x => x.Type == CardType.MINION && x.Cost == V("JAIL_474", "cost", 8)), s) is IPlayable got)
							GiveCostLess(g, s, got, less);
				})
			}));
			// [JAIL_500] Slice and Dice (5) — Replay all other cards played this turn (targeting enemies if possible). End your turn. (wiki: random
			// order; its owner's plays only; no turn end when cast outside the owner's turn)
			cards.Add("JAIL_500", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var plays = c.PlayHistory.Where(h => h.Turn == g.Turn && h.SourceId != s.Id && h.SourceController == c.PlayerId && h.SourceCard != null)
						.Select(h => h.SourceCard).OrderBy(_ => g.Random.Next()).ToList();
					if (s is IPlayable me && Cards.TryFromId("JAIL_500e") is Card replaying) Generic.AddEnchantmentBlock(g, replaying, me, c, 0, 0, 0);
					foreach (Card card in plays)
					{
						if (c.Hero.IsDead || c.Opponent.Hero.IsDead) break;
						if (card.Type == CardType.SPELL)
							new CastRandomSpellTask(enemiesIfPossible: true) { Spell = card }.Process(g, c, s, null);
						else
							Generic.PlayByEffect(g, c, s, card);
					}
					if (g.CurrentPlayer == c) g.EndTurnPending = true;
				})
			}));
			cards.Add("JAIL_500e", new CardDef(new Power { Enchant = new Enchant { IsOneTurnEffect = true } }));
			// [JAIL_501] Picklock (1) — All numbers on this card equal your remaining Mana. Battlecry: Deal @ damage to an enemy minion. (wiki: at
			// least 1; only in hand — locked once played)
			cards.Add("JAIL_501", new CardDef(BattlecryEnemyMinion, new Power
			{
				HandCounter = p =>
				{
					if (p.Zone?.Type != Zone.HAND) return p[GameTag.TAG_SCRIPT_DATA_NUM_1];
					int n = Math.Max(V("JAIL_501", "minimum", 1), p.Controller.RemainingMana);
					if (p is Minion m)
					{
						if (m.AttackDamage != n) m.AttackDamage = n;
						if (m.BaseHealth != n) m.BaseHealth = n;
					}
					return n;
				},
				Aura = new AdaptiveCostEffect(p => Math.Max(V("JAIL_501", "minimum", 1), p.Controller.RemainingMana), EffectOperator.SET),
				PowerTask = ComplexTask.Create(new CustomTask((g, c, s, t, stack) => stack.Number = s[GameTag.TAG_SCRIPT_DATA_NUM_1]),
					new DamageNumberTask(EntityType.TARGET))
			}));
			// [JAIL_503] Blackpaw's Whip (3, weapon) — Costs (1) less for each Coin you're holding. Deathrattle: Draw a card.
			cards.Add("JAIL_503", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => p.Controller.HandZone.Count(x => x.Card[Generic.COIN_CARD] > 0)),
				DeathrattleTask = new DrawTask()
			}));
			// [JAIL_504] Aya, Lotus Kingpin (6) — You always go second. Battlecry: Pick an upgraded counterfeit to replace your Coins this game. Get 3.
			// (live p130515 5543: the 3 Coins first, then the pick; every Coin in hand becomes it; later Coins too: Generic.CounterfeitCoin)
			cards.Add("JAIL_504", new CardDef(new Power
			{
				AlwaysGoSecond = true,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("JAIL_504", "coins", 3); i++) GetCard(c, Cards.FromId("GAME_005"), s);
					Card[] offer = new[] { "JAIL_504t", "JAIL_504t2", "JAIL_504t3" }.Select(Cards.TryFromId).Where(x => x != null).ToArray();
					Generic.CreateChoiceCards.Invoke(c, s, null, ChoiceType.GENERAL, ChoiceAction.STACK, offer, new CustomTask((g2, c2, s2, pick, st) =>
					{
						if (pick == null) return;
						c2[HsTag.COUNTERFEIT_COIN] = pick.Card.AssetId;
						pick.Zone?.Remove(pick);
						c2.GraveyardZone.Add(pick);
						foreach (IPlayable coin in c2.HandZone.GetAll().Where(p => p.Card[Generic.COIN_CARD] > 0 && p.Card.AssetId != pick.Card.AssetId).ToList())
							Generic.NamedFx((IPlayable)s2, "CardInHand_Transform", new[] { coin.Id }, () => Generic.ChangeEntityBlock(c2, coin, pick.Card, true));
					}));
				})
			}));
			// [JAIL_504t] Jade Coin — Gain 1 Mana Crystal this turn only. Summon a Jade Golem. (the Lotus art)
			cards.Add("JAIL_504t", LotusCoin(ComplexTask.SummonJadeGolem(SummonSide.SPELL, LotusJadeGolems)));
			// [JAIL_504t2] Grimy Coin — … Deal $2 damage to a random enemy minion.
			cards.Add("JAIL_504t2", LotusCoin(ComplexTask.DamageRandomTargets(1, EntityType.OP_MINIONS, V("JAIL_504t2", "damage", 2), true)));
			// [JAIL_504t3] Kabal Coin — … Get a random 1-Cost Kazakus Potion.
			cards.Add("JAIL_504t3", LotusCoin(new CustomTask((g, c, s, t, stack) => GetKazakusPotion(c, s))));
			// [JAIL_504t5] Kazakus Potion Generator — Get a random 1-Cost Potion.
			cards.Add("JAIL_504t5", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => GetKazakusPotion(c, s)) }));
			// [JAIL_504t3p] Kazakus Potion — {0} {1}: both ingredients' effects (TAG_SCRIPT_DATA_NUM_1 / _2).
			cards.Add("JAIL_504t3p", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (GameTag k in new[] { GameTag.TAG_SCRIPT_DATA_NUM_1, GameTag.TAG_SCRIPT_DATA_NUM_2 })
						if (Cards.FromAssetId(s[k])?.Power?.PowerTask is ISimpleTask part)
						{
							g.TaskQueue.StartEvent();
							g.TaskQueue.Execute(part, c, (IPlayable)s, t, 0, 0, 0);
							g.ProcessTasks();
							g.TaskQueue.EndEvent();
						}
				})
			}));
			// [JAIL_706] Thief's Tools (3) — Get two random 4-Cost spells. Reduce their Costs by (2). (any class: the text names none)
			cards.Add("JAIL_706", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("JAIL_706", "spells", 2); i++)
						if (GetCard(c, RandomCard(g, x => x.Type == CardType.SPELL && x.Cost == V("JAIL_706", "cost", 4)), s) is IPlayable got)
							Give(g, s, got, GenericCostLess(V("JAIL_706", "less", 2)));
				})
			}));
			// [JAIL_720] Lotus Bookie (2) — Deathrattle: Get a Coin. (Aya's counterfeit when picked: AddHandPhase)
			cards.Add("JAIL_720", new CardDef(new Power { DeathrattleTask = new CustomTask((g, c, s, t, stack) => GetCard(c, Cards.FromId("GAME_005"), s)) }));
			// [JAIL_909] Defias Wannabe (4) — Prepare. Combo: Gain +1/+1 for each other card played this turn. (live p130515 5949: NUM_1 in hand)
			cards.Add("JAIL_909", new CardDef(new Power
			{
				HandCounter = p => p.Zone?.Type == Zone.HAND ? p.Controller.NumCardsPlayedThisTurn : p[GameTag.TAG_SCRIPT_DATA_NUM_1],
				ComboTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = c.NumCardsPlayedThisTurn - 1;
					if (n > 0) Give(g, s, (IPlayable)s, "JAIL_909e", n);
				})
			}));
			cards.Add("JAIL_909e", new CardDef(new Power { Enchant = Enchants.Enchants.AddAttackHealthScriptTag }));

			// ---------------------------------------------------------------- Priest
			// [CAP_800] Sinful Steed (3) — Reborn. This is Reborn with full Health and enchantments. (its card tag 2872: Game.RebornTask)
			cards.Add("CAP_800", new CardDef(new Power { NoScript = true }));
			cards.Add("CAP_800e", new CardDef(new Power
			{
				Enchant = new Enchant(new Effect(GameTag.REBORN, EffectOperator.SET, 1), new Effect(Generic.FULL_HEALTH_REBORN, EffectOperator.SET, 1))
			}));
			// [CAP_801] Haunt (3) — Give a minion +2/+3, Reborn, and Taunt.
			cards.Add("CAP_801", new CardDef(TargetMinion, new Power { PowerTask = new AddEnchantmentTask("CAP_801e2", EntityType.TARGET) }));
			cards.Add("CAP_801e2", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.Attack_N(V("CAP_801", "attack", 2)), Effects.Health_N(V("CAP_801", "health", 3)), Effects.TauntEff,
					new Effect(GameTag.REBORN, EffectOperator.SET, 1))
			}));
			// [CAP_802] Follow the Ghosts (2) — Summon a 2/1 Ghost with Reborn. Give a playable card in your hand this effect for a turn. (random:
			// wiki "Random", the pass is automatic — doubt)
			cards.Add("CAP_802", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Summon(g, c, Cards.FromId("CAP_802t"), s);
					var playable = c.HandZone.GetAll().Where(p => p.Id != s.Id && p.IsPlayable).ToList();
					if (playable.Count > 0) Generic.GiveFollowEffect(s, playable[g.Random.Next(playable.Count)], Cards.FromId("CAP_802"), "CAP_802e1");
				})
			}));
			cards.Add("CAP_802e1", new CardDef(new Power { Enchant = new Enchant { IsOneTurnEffect = true } }));
			// [CAP_803] Lingering Spirit (4) — Reborn. Deathrattle: Restore #3 Health to your hero. Excess healing damages a random enemy.
			cards.Add("CAP_803", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					int before = c[HsTag.OVERHEALED_AMOUNT]; // (the healer's player counts what it Overhealed)
					new HealTask(V("CAP_803", "heal", 3), EntityType.HERO).Process(g, c, s, null);
					int excess = c[HsTag.OVERHEALED_AMOUNT] - before;
					var enemies = Enemies(c).Where(x => !x.IsDead && !x.ToBeDestroyed && x.Health > 0).ToList();
					if (excess > 0 && enemies.Count > 0) Generic.DamageCharFunc((IPlayable)s, enemies[g.Random.Next(enemies.Count)], excess, false);
				})
			}));
			// [CAP_804] Specter Specialist (3) — Battlecry: Give a friendly minion Reborn. If it already has Reborn, summon a copy of it. (live: an
			// exact copy right of it, darkmoon 51712)
			cards.Add("CAP_804", new CardDef(BattlecryFriendlyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m) || m.Zone?.Type != Zone.PLAY) return;
					if (m[GameTag.REBORN] > 0)
					{
						if (!c.BoardZone.IsFull) Generic.Copy(c, s, m, Zone.PLAY, position: m.ZonePosition + 1);
					}
					else
						m[GameTag.REBORN] = 1;
				})
			}));
			// [CAP_805] Slime 'em! (4) — Destroy all minions. Each player gets a 3-Cost spell that resummons theirs. (wiki: plain copies, left to
			// right; the player who casts the Ectoplasm summons their own slimed minions)
			cards.Add("CAP_805", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Controller p in new[] { c, c.Opponent })
					{
						Generic.RecordSlimed(p);
						GetCard(p, Cards.FromId("CAP_805t"), s);
						if (Cards.TryFromId("CAP_805e") is Card slimed) Generic.AddEnchantmentBlock(g, slimed, (IPlayable)s, p, 0, 0, 0);
					}
					foreach (Minion m in c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll()).Where(m => !(m is Location)).ToList())
						Generic.Destroy(m, s);
				})
			}));
			cards.Add("CAP_805e", new CardDef(new Power { Enchant = new Enchant() }));
			cards.Add("CAP_805t", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { foreach (Card card in Generic.Slimed(c)) Summon(g, c, card, s); })
			}));
			// [CAP_806] Raith Van Geist (7) — Battlecry: Resurrect your minions that were Reborn this game. They attack random enemy minions.
			cards.Add("CAP_806", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var back = new List<Minion>();
					foreach (Card card in Generic.RebornThisGame(c))
					{
						if (c.BoardZone.IsFull) break;
						var m = (Minion)Entity.FromCard(c, card);
						Generic.SummonBlock.Invoke(g, m, -1, s);
						if (m.Zone?.Type == Zone.PLAY) back.Add(m);
					}
					foreach (Minion m in back)
					{
						if (m.IsDead || m.ToBeDestroyed || m.Zone?.Type != Zone.PLAY) continue;
						var enemies = c.Opponent.BoardZone.GetAll().Where(x => !x.IsDead && !x.ToBeDestroyed && !x.Untouchable).ToList();
						if (enemies.Count == 0) break;
						EffectAttack(g, m, enemies[g.Random.Next(enemies.Count)]);
						g.DeathProcessingAndAuraUpdate();
					}
				})
			}));
			// [JAIL_430] Azalina Soulsever (7) — Your starting Health is 40. Your deck is 20 cards, plus 20 copied from your enemy. Battlecry: Draw
			// until your hand is full. (the copies before the first player — Generic.SideboardEffects time, live p20261001-195600; Health 40 with
			// the other Start of Game effects: TLC_835e)
			cards.Add("JAIL_430", new CardDef(new Power
			{
				SideboardTask = new CustomTask((g, c, s, t, stack) =>
				{
					var enemy = c.Opponent.DeckZone.GetAll().ToList();
					for (int i = 0; i < V("JAIL_430", "copies", 20) && enemy.Count > 0; i++)
						if (Generic.JoinDeck(c, s, enemy[g.Random.Next(enemy.Count)].Card) is IPlayable copy)
							copy[HsTag.COPIED_FROM_OPPONENT] = 1;
					if (Cards.TryFromId("JAIL_430e1") is Card hint) Generic.AddEnchantmentBlock(g, hint, (IPlayable)s, c, 0, 0, 0);
				}),
				Trigger = new Trigger(TriggerType.GAME_START)
				{
					TriggerActivation = TriggerActivation.DECK,
					RemoveAfterTriggered = true,
					SingleTask = new CustomTask((g, c, s, t, stack) => Give(g, s, c.Hero, "TLC_835e", V("JAIL_430", "health", 40)))
				},
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int guard = 0; guard < 10 && !c.HandZone.IsFull && c.DeckZone.Count > 0; guard++) Generic.Draw(c);
				})
			}));
			cards.Add("JAIL_430e1", new CardDef(new Power { Enchant = new Enchant() }));
			// [JAIL_432] Mind Sweeper (2) — Battlecry: If you played a copy of an opponent's card while holding this, deal 2 damage to all enemy minions.
			cards.Add("JAIL_432", new CardDef(new Power
			{
				Trigger = WhileHolding(TriggerType.PLAY_CARD, Generic.CopiedFromOpponent),
				ThresholdValue = 1,
				PowerTask = ComplexTask.If((g, c, s) => Generic.ThresholdMet(s),
					new CustomTask((g, c, s, t, stack) => DamageAll(s, c.Opponent.BoardZone.GetAll().Cast<ICharacter>(), V("JAIL_432", "damage", 2), false)))
			}));
			// [JAIL_433] Unshackle Soul (5) — Destroy a minion. If you played a copy of an opponent's card while holding this, this costs (1).
			cards.Add("JAIL_433", new CardDef(TargetMinion, new Power
			{
				Trigger = WhileHolding(TriggerType.PLAY_CARD, Generic.CopiedFromOpponent),
				ThresholdValue = 1,
				Aura = new AdaptiveCostEffect(p => V("JAIL_433", "cost", 1), EffectOperator.SET, new SelfCondition(p => Generic.ThresholdMet(p))),
				PowerTask = new DestroyTask(EntityType.TARGET)
			}));
			// [JAIL_434] Enthralled Shade (2) — Deathrattle: Reduce the Cost of cards in your hand that were copied from your opponent by (1).
			cards.Add("JAIL_434", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable p in c.HandZone.GetAll().Where(Generic.CopiedFromOpponent).ToList()) Give(g, s, p, GenericCostLess(V("JAIL_434", "less", 1)));
				})
			}));
			// [JAIL_448] Karov the Broken (6) — Taunt. Deathrattle: Get three 1/1 copies of random Legendary minions. They cost (1).
			cards.Add("JAIL_448", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("JAIL_448", "minions", 3); i++)
						if (GetCard(c, RandomCard(g, x => x.Type == CardType.MINION && x.Rarity == Rarity.LEGENDARY), s) is IPlayable got)
							Give(g, s, got, "JAIL_448e");
				})
			}));
			cards.Add("JAIL_448e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.SetAttackHealth(V("JAIL_448", "stats", 1)).Append(Effects.SetCost(V("JAIL_448", "cost", 1))).ToArray())
			}));
			// [JAIL_912] Soothsayer (7) — Prepare, Taunt. Deathrattle: Restore #6 Health to your hero. Summon a random 6-Cost minion.
			cards.Add("JAIL_912", new CardDef(new Power
			{
				DeathrattleTask = ComplexTask.Create(new HealTask(V("JAIL_912", "heal", 6), EntityType.HERO),
					new CustomTask((g, c, s, t, stack) => Summon(g, c, RandomMinionOfCost(g, V("JAIL_912", "cost", 6)), s)))
			}));
			// [JAIL_913] Hold Them Off! (5) — Prepare. Give a minion +5/+5 and Lifesteal.
			cards.Add("JAIL_913", new CardDef(TargetMinion, new Power { PowerTask = new AddEnchantmentTask("JAIL_913e", EntityType.TARGET) }));
			cards.Add("JAIL_913e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("JAIL_913", "stats", 5)).Append(Effects.Lifesteal).ToArray()) }));
			// [JAIL_940] Undeath Sentence (1) — Trigger the Deathrattle of a random friendly minion that died this game. (live darkmoon 52015: a
			// set-aside copy runs it — Generic.TriggerDeathrattleOf)
			cards.Add("JAIL_940", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var dead = c.DiedThisGame.Select(id => g.IdEntityDic.TryGetValue(id, out IPlayable p) ? p : null)
						.Where(p => p is Minion && p.Card.Power?.DeathrattleTask != null).ToList();
					if (dead.Count > 0) Generic.TriggerDeathrattleOf(s, dead[g.Random.Next(dead.Count)]);
				})
			}));
			// [JAIL_941] Holy Embrace (2) — Restore #4 Health. Get a 'Dark Embrace' that deals 4 damage.
			cards.Add("JAIL_941", new CardDef(TargetAny, new Power
			{
				PowerTask = ComplexTask.Create(new HealTask(V("JAIL_941", "heal", 4), EntityType.TARGET), new AddCardTo("JAIL_941t", EntityType.HAND))
			}));
			cards.Add("JAIL_941t", new CardDef(TargetAny, new Power { PowerTask = new DamageTask(V("JAIL_941t", "damage", 4), EntityType.TARGET, true) }));
			// [JAIL_942] Specter of Despair (2) — Taunt. Can't attack. (keywords only: CANT_ATTACK from the loader's text rule)
			cards.Add("JAIL_942", new CardDef(new Power { NoScript = true }));

			// ---------------------------------------------------------------- Paladin
			// [JAIL_035] Vigilant Sentry (3) — Taunt. Battlecry: If your deck has no Neutral cards, summon two Vigilant Sentries.
			cards.Add("JAIL_035", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => Generic.NoNeutralInDeck(c), new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("JAIL_035", "sentries", 2); i++) Summon(g, c, Cards.FromId("JAIL_035"), s);
				}))
			}));
			// [JAIL_118] V'ama, Looming Death (9) — Battlecry: Destroy all non-Paladin minions. (live 195600 25684: one full-board effect)
			cards.Add("JAIL_118", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var doomed = c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll())
						.Where(m => m.Id != s.Id && !(m is Location) && !m.Card.HasClass(CardClass.PALADIN)).ToList();
					Generic.NamedFx((IPlayable)s, "FullboardAE", null, () => { foreach (Minion m in doomed) Generic.Destroy(m, s); });
				})
			}));
			// [JAIL_326] Judgment (6) — Prepare. Choose a friendly minion. Set all minions' stats equal to that minion's. (the others: "another
			// minion's"; rule book: a set Health is the new maximum and the current Health)
			cards.Add("JAIL_326", new CardDef(TargetFriendlyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion chosen)) return;
					int atk = chosen.AttackDamage, health = chosen.Health;
					foreach (Minion m in c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll()).Where(m => m.Id != chosen.Id && !(m is Location)).ToList())
						Generic.AddEnchantmentBlock(g, Cards.FromId("JAIL_326e"), (IPlayable)s, m, atk, health, 0);
				})
			}));
			cards.Add("JAIL_326e", new CardDef(new Power { Enchant = Enchants.Enchants.SetAttackHealthScriptTag }));
			// [JAIL_327] Reinforcement Aura (3) — At the end of your turn, summon a minion from your deck that costs (2) or less. Lasts @ turns.
			cards.Add("JAIL_327", new CardDef(PaladinAura(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) => SummonFromDeck(g, c, s, p => p.Cost <= V("JAIL_327", "cost", 2)))
				}
			})));
			// [JAIL_328] Scarlet Bruiser (2) — Deathrattle: If your deck has no Neutral cards, get a random Paladin card. It costs (2) less.
			cards.Add("JAIL_328", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (Generic.NoNeutralInDeck(c) && GetCard(c, RandomCard(g, x => x.HasClass(CardClass.PALADIN)), s) is IPlayable got)
						Give(g, s, got, GenericCostLess(V("JAIL_328", "less", 2)));
				})
			}));
			// [JAIL_329] Truth Seeker (7, weapon) — After your hero attacks, give your Paladin minions +2/+2.
			cards.Add("JAIL_329", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						foreach (Minion m in c.BoardZone.GetAll().Where(m => m.Card.HasClass(CardClass.PALADIN)).ToList()) Give(g, s, m, "JAIL_329e");
					})
				}
			}));
			cards.Add("JAIL_329e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("JAIL_329", "stats", 2))) }));
			// [JAIL_330] Dalaran Champion (4) — Divine Shield, Taunt. After this gains stats, gain an extra +1/+1 (wherever this is).
			cards.Add("JAIL_330", new CardDef(new Power { ExtraOnStatGain = "JAIL_330e" }));
			cards.Add("JAIL_330e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("JAIL_330", "extra", 1))) }));
			// [JAIL_377] Holy Bola! (2) — Draw a card. If it costs (2) or less, draw another.
			cards.Add("JAIL_377", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (Generic.Draw(c) is IPlayable drawn && drawn.Zone?.Type == Zone.HAND && drawn.Cost <= V("JAIL_377", "cost", 2)) Generic.Draw(c);
				})
			}));
			// [JAIL_516] Scarlet Recruiter (5) — Battlecry: Summon two minions from your deck that cost (2) or less. Give them Rush.
			cards.Add("JAIL_516", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("JAIL_516", "minions", 2); i++)
						if (SummonFromDeck(g, c, s, p => p.Cost <= V("JAIL_516", "cost", 2)) is Minion m && m.Zone?.Type == Zone.PLAY) Give(g, s, m, "JAIL_516e");
				})
			}));
			cards.Add("JAIL_516e", new CardDef(new Power { Enchant = new Enchant(Effects.Rush) }));
		}
	}
}
