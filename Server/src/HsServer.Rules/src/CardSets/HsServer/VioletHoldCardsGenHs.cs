// HsServer addition (not upstream SabberStone): Escape from Violet Hold (VIOLET_HOLD, set 1988; JAIL_ + CAP_ mini-set) — chunk 1:
// neutrals, Death Knight, Demon Hunter (spec: research spec_chunk1). Shared pieces: Prepare (Actions/Prepare.cs: PrepareTask, the DECK_ACTION
// block, JAIL_907e / JAIL_907e02), Free placement (PlayCardTask.Disguised, PlayMinion), a second Hero Power (Generic.AddSecondHeroPower,
// HeroPowerTask.Power), Generic.SummonThatAttack / RefreshHeroPower / SendDeckToVoid / TakeFromVoid / ShuffleHandsTogether /
// AnywhereDeathrattle (Actions/VioletHold.cs), Power.HeroDamageShuffles / AfterPrepareTask. Every number goes through CardValues (V).
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
		/// <summary>Client player tag 4662: the Void Souls cast ("Improve your future Void Souls"; live dk-classxp 5812).</summary>
		private const GameTag VOID_SOUL_IMPROVEMENTS = (GameTag)4662;

		/// <summary>A Void Soul for the hand (its "@-Cost" = 1 + the player's improvements: Power.HandCounter).</summary>
		private static ISimpleTask GetVoidSoul => new CustomTask((g, c, s, t, stack) => GetCard(c, Cards.FromId("JAIL_732"), s));

		private static void VioletHold(IDictionary<string, CardDef> cards)
		{
			VioletHoldChunk1(cards);
			VioletHoldChunk2(cards);
			VioletHoldChunk3(cards);
			VioletHoldChunk4(cards);
		}

		private static void VioletHoldChunk1(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Prepare (the keyword's enchantments)
			// [JAIL_907e] Prepared — Cost reduced. (its number; gone when the card is played: live darkmoon 64986)
			cards.Add("JAIL_907e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(0)) { UseScriptTag = true, RemoveWhenPlayed = true } }));
			// [JAIL_907e02] Preparing — Can't be played this turn. (Generic.LockInHand shows it and ends it)
			cards.Add("JAIL_907e02", new CardDef(new Power { Enchant = new Enchant() }));

			// ---------------------------------------------------------------- Neutral
			// [CAP_407] Wanted Poster (2) — Discover a minion that costs (5) or more. Give it Prepare. (wiki: no Tradeable / Forge minions)
			cards.Add("CAP_407", new CardDef(new Power
			{
				PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.MINION && x.Cost >= V("CAP_407", "cost", 5) &&
					x[Generic.TRADEABLE] == 0 && x[Generic.FORGE] == 0, "CAP_407e")
			}));
			cards.Add("CAP_407e", new CardDef(new Power
			{
				Enchant = new Enchant(new Effect(Generic.PREPARE, EffectOperator.SET, 1), new Effect(Generic.DECK_ACTION_COST, EffectOperator.SET, 1))
			}));
			// [JAIL_007] Sewer Imp (6) — Taunt. Deathrattle: Deal 2 damage to all enemies.
			cards.Add("JAIL_007", new CardDef(new Power { DeathrattleTask = new CustomTask((g, c, s, t, stack) => DamageAll(s, Enemies(c), V("JAIL_007", "damage", 2), false)) }));
			// [JAIL_030] Escape Artist (2) — After this attacks and survives, draw a card and escape the game! (escape = removed from the game)
			cards.Add("JAIL_030", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.SELF,
					Condition = new SelfCondition(p => p is Minion m && !m.IsDead && !m.ToBeDestroyed && m.Health > 0 && m.Zone?.Type == Zone.PLAY),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						Generic.Draw(c);
						Generic.RemoveFromGame((IPlayable)s, s);
					})
				}
			}));
			// [JAIL_101] Violet Punisher (3) — Battlecry: Choose an enemy minion. Steal its Bonus Effects and gain +1/+1 for each stolen. (wiki:
			// whatever gave them)
			cards.Add("JAIL_101", new CardDef(BattlecryEnemyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion victim) || !(s is Minion me)) return;
					foreach (GameTag k in Generic.BonusEffectKeywords.Where(k => Generic.HasBonusEffect(victim, k)).ToList())
					{
						Generic.ClearBonusEffect(victim, k);
						Give(g, s, me, "JAIL_101e");
						Generic.GiveBonusEffect(me, k);
					}
				})
			}));
			cards.Add("JAIL_101e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("JAIL_101", "stats", 1))) }));
			// [JAIL_204] Solitary Prisoner (5) — Costs (2) if there are no minions on the battlefield. (Dormant ones count — doubt)
			cards.Add("JAIL_204", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => V("JAIL_204", "cost", 2), EffectOperator.SET,
					new SelfCondition(p => !p.Controller.BoardZone.Any(m => !(m is Location)) && !p.Controller.Opponent.BoardZone.Any(m => !(m is Location))))
			}));
			// [JAIL_205] Rat Burglar (4) — At the end of your turn, steal all cards that entered your opponent's hand during your turn.
			cards.Add("JAIL_205", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						foreach (IPlayable card in c.Opponent.HandZone.GetAll().Where(p => p[HsTag.ADDED_TO_HAND_TURN] == g.Turn).ToList())
							Generic.TakeIntoHand(c, card);
					})
				}
			}));
			// [JAIL_398] IMPFERNAL! (6) — Deathrattle: Deal 3 damage to all other characters. (Also triggers in hand or deck.)
			cards.Add("JAIL_398", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
					DamageAll(s, Enemies(c).Concat(c.BoardZone.GetAll().Where(m => !(m is Location))).Append(c.Hero).Where(x => x.Id != s.Id).Distinct(), V("JAIL_398", "damage", 3), false))
			}));
			// [JAIL_407] Vanessa the Ringleader (7) — Prepare. After you play a card, get a random Battlecry minion. It costs (2) less. (any class:
			// live 63 pulls)
			cards.Add("JAIL_407", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_CARD)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (GetCard(c, RandomCard(g, x => x.Type == CardType.MINION && x[GameTag.BATTLECRY] > 0), s) is IPlayable got)
							Give(g, s, got, GenericCostLess(V("JAIL_407", "less", 2)));
					})
				}
			}));
			// [JAIL_447] Reckless Detective (4) — Rush. Deathrattle: Get Detective's Clothes that give +4/+4 and Rush.
			cards.Add("JAIL_447", new CardDef(new Power { DeathrattleTask = new CustomTask((g, c, s, t, stack) => GetCard(c, Cards.FromId("JAIL_447t"), s)) }));
			cards.Add("JAIL_447t", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					Give(g, s, m, "JAIL_447e");
					Generic.GiveBonusEffect(m, GameTag.RUSH);
				})
			}));
			cards.Add("JAIL_447e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("JAIL_447t", "stats", 4))) }));
			// [JAIL_453] Jailbird (5) — Taunt. When you Prepare while holding this, reduce this card's Cost by the same amount. (the Mana spent —
			// doubt)
			cards.Add("JAIL_453", new CardDef(new Power
			{
				AfterPrepareTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (s is IPlayable me && me.Zone?.Type == Zone.HAND && c[HsTag.LAST_PREPARE_SPENT] > 0) Give(g, s, me, "JAIL_453e", c[HsTag.LAST_PREPARE_SPENT]);
				})
			}));
			cards.Add("JAIL_453e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(0)) { UseScriptTag = true } }));
			// [JAIL_456] P1CK-P0K3T (2) — Battlecry: If your deck has 25 or more cards, draw a card.
			cards.Add("JAIL_456", new CardDef(new Power { PowerTask = ComplexTask.If((g, c, s) => c.DeckZone.Count >= V("JAIL_456", "deck", 25), new DrawTask()) }));
			// [JAIL_459] Arachnathid (7) — Rush. All friendly minions are Poisonous.
			cards.Add("JAIL_459", new CardDef(new Power { Aura = new Aura(AuraType.BOARD, "JAIL_459e") }));
			cards.Add("JAIL_459e", new CardDef(new Power { Enchant = new Enchant(new Effect(GameTag.POISONOUS, EffectOperator.SET, 1)) }));
			// [JAIL_460] Concealing Confection (1) — Deathrattle: Get a random weapon. (any class — doubt)
			cards.Add("JAIL_460", new CardDef(new Power { DeathrattleTask = new CustomTask((g, c, s, t, stack) => GetCard(c, RandomCard(g, x => x.Type == CardType.WEAPON), s)) }));
			// [JAIL_461] Disguised Executioner (3) — Can be played on either side. Battlecry: Destroy a random adjacent minion.
			cards.Add("JAIL_461", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Minion me)) return;
					var next = Neighbors(me).Where(m => !m.Untouchable && !m.IsDead && !m.ToBeDestroyed && !(m is Location)).ToList();
					if (next.Count > 0) Generic.Destroy(next[g.Random.Next(next.Count)], s);
				})
			}));
			// [JAIL_462] Getaway Hogdriver (5) — Battlecry: Draw 2 cards. If they're both minions, gain Charge. (a burnt card isn't drawn)
			cards.Add("JAIL_462", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var drawn = new List<IPlayable>();
					for (int i = 0; i < V("JAIL_462", "draws", 2); i++)
						if (Generic.Draw(c) is IPlayable p && p.Zone?.Type == Zone.HAND) drawn.Add(p);
					if (drawn.Count == 2 && drawn.All(p => p is Minion) && s is Minion me && me.Zone?.Type == Zone.PLAY)
					{
						me[GameTag.CHARGE] = 1;
						if (me.NumAttacksThisTurn == 0) me.IsExhausted = false;
					}
				})
			}));
			// [JAIL_502] Alarm-o-Matic (3) — At the start of your turn, swap this minion with a random one in your opponent's hand.
			cards.Add("JAIL_502", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Minion me) || me.Zone?.Type != Zone.PLAY) return;
						var minions = c.Opponent.HandZone.OfType<Minion>().Where(m => Generic.IsMinionCard(m)).ToList();
						if (minions.Count == 0) return;
						Minion pick = minions[g.Random.Next(minions.Count)];
						int slot = me.ZonePosition;
						c.Opponent.HandZone.Remove(pick);
						Generic.TakeIntoHand(c.Opponent, me);
						Generic.GiveTo(pick, c);
						Generic.SummonBlock.Invoke(g, pick, Math.Min(slot, c.BoardZone.Count), s);
					})
				}
			}));
			// [JAIL_703] Gullible Guard (1) — Deathrattle: You can say Sorry this game. (live 204312 75760: the player's OVERRIDE_EMOTE_3 = 21)
			cards.Add("JAIL_703", new CardDef(new Power { DeathrattleTask = new CustomTask((g, c, s, t, stack) => c[(GameTag)743] = V("JAIL_703", "emote", 21)) }));
			// [JAIL_718] Black Market Auctioneer (9) — Prepare. Whenever you cast a spell, draw a card.
			cards.Add("JAIL_718", new CardDef(new Power { Trigger = new Trigger(TriggerType.CAST_SPELL) { TriggerSource = TriggerSource.FRIENDLY, SingleTask = new DrawTask() } }));
			// [JAIL_850] Warden Maiev (2) — After you play a minion, give it +3/+3 and make it go Dormant for 1 turn.
			cards.Add("JAIL_850", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_MINION)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!((t ?? g.CurrentEventData?.EventSource as IPlayable) is Minion m) || m.Id == s.Id || m.Zone?.Type != Zone.PLAY || m.IsDead) return;
						Give(g, s, m, "JAIL_850e2");
						Generic.MakeDormant(m, V("JAIL_850", "turns", 1), "JAIL_850e", (IPlayable)s);
					})
				}
			}));
			cards.Add("JAIL_850e2", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("JAIL_850", "stats", 3))) }));
			// [JAIL_851] Inspector Murloc Holmes (4) — Battlecry: Investigate a card in the enemy hand. If they play a card with that name next turn,
			// get 3 Coins. (live 204312 87603: 4 linked copies offered; the pick stays set aside; wiki: the same name counts)
			cards.Add("JAIL_851", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var picks = Generic.PickFrom(c.Opponent.HandZone.GetAll().ToList(), g, null, V("JAIL_851", "cards", 4));
					if (picks.Count == 0) return;
					var offered = picks.Select(p => Entity.FromCard(c, p.Card, new EntityData
					{
						{ GameTag.CREATOR, s.Id }, { GameTag.LINKED_ENTITY, p.Id }, { HsTag.COPIED_FROM_ENTITY_ID, p.Id }
					}, c.SetasideZone)).ToList();
					Generic.CreateChoiceEntities(c, s, offered, ChoiceAction.STACK, new CustomTask((g2, c2, s2, pick, st) =>
					{
						if (pick == null) return;
						c2[HsTag.INVESTIGATED_CARD] = pick.Card.AssetId;
						c2[HsTag.INVESTIGATE_TURN] = g2.Turn + 1;
						if (s2 is Minion holmes && holmes.Zone?.Type == Zone.PLAY) Give(g2, s2, holmes, "JAIL_851e1");
						Generic.AddEnchantmentBlock(g2, Cards.FromId("JAIL_851e"), (IPlayable)s2, c2, 0, 0, 0);
					}));
				})
			}));
			cards.Add("JAIL_851e1", new CardDef(new Power { Enchant = new Enchant() }));
			cards.Add("JAIL_851e", new CardDef(new Power
			{
				Enchant = new Enchant(),
				Trigger = new MultiTrigger(
					new Trigger(TriggerType.AFTER_PLAY_CARD)
					{
						TriggerSource = TriggerSource.ENEMY,
						SingleTask = new CustomTask((g, c, s, t, stack) =>
						{
							IPlayable played = t ?? g.CurrentEventData?.EventSource as IPlayable;
							if (played == null || g.Turn != c[HsTag.INVESTIGATE_TURN] || !(Cards.FromAssetId(c[HsTag.INVESTIGATED_CARD]) is Card suspect) ||
							    played.Card.Name != suspect.Name) return;
							c[HsTag.INVESTIGATED_CARD] = 0;
							for (int i = 0; i < V("JAIL_851", "coins", 3); i++) GetCard(c, Cards.FromId("GAME_005"), s);
							if (s is Enchantment e) RemoveEnchantmentTask.Task.Process(g, c, e, null);
						})
					},
					new Trigger(TriggerType.TURN_END)
					{
						EitherTurn = true,
						SingleTask = new CustomTask((g, c, s, t, stack) =>
						{
							if (g.Turn < c[HsTag.INVESTIGATE_TURN]) return;
							c[HsTag.INVESTIGATED_CARD] = 0;
							if (s is Enchantment e) RemoveEnchantmentTask.Task.Process(g, c, e, null);
						})
					})
			}));
			// [JAIL_852] Togwaggle, Smuggler King (8) — Battlecry: Shuffle both players' hands together.
			cards.Add("JAIL_852", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => Generic.ShuffleHandsTogether(g)) }));
			// [JAIL_883] Activated Golem (4) — At the end of each turn, gain Reborn.
			cards.Add("JAIL_883", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					EitherTurn = true,
					SingleTask = new CustomTask((g, c, s, t, stack) => { if (s is Minion me && me[GameTag.REBORN] == 0) me[GameTag.REBORN] = 1; })
				}
			}));
			// [JAIL_974] Captured Archmage (2) — Deathrattle: If you had 4 other Captured Archmages die this game, cast 'Fireball' at a random
			// enemy. (@/4) (wiki: counts the deaths, not the Deathrattles; same name)
			Func<IPlayable, int> archmages = p => p.Controller.DiedThisGameCards.Count(x => x.Card.Name == p.Card.Name && x.Id != p.Id);
			cards.Add("JAIL_974", new CardDef(new Power
			{
				HandCounter = p => Math.Min(archmages(p), V("JAIL_974", "needed", 4)),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (archmages((IPlayable)s) < V("JAIL_974", "needed", 4) || !(Cards.TryFromId("CS2_029") is Card fireball)) return;
					new CastRandomSpellTask(enemiesIfPossible: true) { Spell = fireball }.Process(g, c, s, null);
				})
			}));
			// [JAIL_986] Frantic Forger (2) — Battlecry: Get a random playable spell. It is Temporary. (Cost within the Mana left, its play
			// requirements met — doubt)
			cards.Add("JAIL_986", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int mana = c.RemainingMana;
					if (GetCard(c, RandomCard(g, x => x.Type == CardType.SPELL && x.Cost <= mana && x.IsPlayableByCardReq(c)), s) is IPlayable spell)
						Generic.MakeTemporary(spell, s);
				})
			}));
			// [JAIL_998] Defias Smuggler (3) — Prepare. Battlecry: Give a friendly minion +2 Attack and Rush.
			cards.Add("JAIL_998", new CardDef(BattlecryFriendlyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					Give(g, s, m, "JAIL_998e");
					Generic.GiveBonusEffect(m, GameTag.RUSH);
				})
			}));
			cards.Add("JAIL_998e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("JAIL_998", "attack", 2))) }));

			// ---------------------------------------------------------------- Death Knight
			// [JAIL_440] Tower of Ghouls (4) — After this minion takes damage, summon two 1/1 Frail Ghouls.
			cards.Add("JAIL_440", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TAKE_DAMAGE)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						for (int i = 0; i < V("JAIL_440", "ghouls", 2); i++) Summon(g, c, Cards.FromId("HERO_11bpt"), s);
					})
				}
			}));
			// [JAIL_441] Drink Blood (2) — Lifesteal. Deal $3 damage to a minion. Refresh your Hero Power.
			cards.Add("JAIL_441", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("JAIL_441", "damage", 3), EntityType.TARGET, true),
					new CustomTask((g, c, s, t, stack) => Generic.RefreshHeroPower(c)))
			}));
			// [JAIL_442] Disguised Doctor (1) — Can be played on either side. Deathrattle: Shuffle 4 Blights into your deck that deal 2 damage when
			// drawn. (its controller's deck)
			cards.Add("JAIL_442", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("JAIL_442", "blights", 4); i++)
						Generic.ShuffleIntoDeck.Invoke(c, s, Entity.FromCard(c, Cards.FromId("JAIL_443t"), new EntityData { { GameTag.CREATOR, s.Id } }));
				})
			}));
			// [JAIL_443t] Blight — Casts When Drawn. Deal 2 damage to your hero.
			var blight = new DamageTask(V("JAIL_443t", "damage", 2), EntityType.HERO);
			cards.Add("JAIL_443t", new CardDef(new Power { PowerTask = blight, TopdeckTask = blight }));
			// [JAIL_443] The Living Plague (6) — Charge. Instead of damaging heroes, this shuffles that many Blights into their deck.
			cards.Add("JAIL_443", new CardDef(new Power { HeroDamageShuffles = "JAIL_443t" }));
			// [JAIL_444] Sawbones (5) — Prepare. Battlecry: Destroy all your other minions. Draw a card and refresh a Mana for each one destroyed.
			cards.Add("JAIL_444", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var others = c.BoardZone.GetAll().Where(m => m.Id != s.Id && !(m is Location) && !m.Untouchable && !m.IsDead && !m.ToBeDestroyed).ToList();
					foreach (Minion m in others) Generic.Destroy(m, s, many: true);
					for (int i = 0; i < others.Count; i++)
					{
						Generic.Draw(c);
						RefreshMana(c, 1);
					}
				})
			}));
			// [JAIL_445] Bone Flurry (2) — Deal $3 damage randomly split among enemies. If a friendly minion died this turn, deal $3 more.
			ISimpleTask flurry = new EnqueueTask(V("JAIL_445", "damage", 3), ComplexTask.DamageRandomTargets(1, EntityType.ENEMIES, 1), true);
			cards.Add("JAIL_445", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(flurry, ComplexTask.If((g, c, s) => c.NumFriendlyMinionsThatDiedThisTurn > 0, flurry))
			}));
			// [JAIL_446] Blood Doctor Thal'ena (4) — Battlecry: Get a second Hero Power that costs Corpses.
			cards.Add("JAIL_446", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.AddSecondHeroPower(c, Cards.FromId("JAIL_446hp"), (IPlayable)s))
			}));
			// [JAIL_446hp] Vampyr's Kiss (3 Corpses) — Give a minion +3 Attack.
			cards.Add("JAIL_446hp", new CardDef(TargetMinion, new Power { PowerTask = new AddEnchantmentTask("JAIL_446hpe6", EntityType.TARGET) }));
			cards.Add("JAIL_446hpe6", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("JAIL_446hp", "attack", 3))) }));
			// [JAIL_450] Corpse Cannon (2, weapon) — After your hero attacks, summon a 1/1 Frail Ghoul.
			cards.Add("JAIL_450", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK) { TriggerSource = TriggerSource.HERO, SingleTask = new CustomTask((g, c, s, t, stack) => Summon(g, c, Cards.FromId("HERO_11bpt"), s)) }
			}));
			// [JAIL_454] Emergency Surgery (5) — Choose an enemy minion. Summon four 3/1 Undead with Lifesteal that attack it.
			cards.Add("JAIL_454", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_ENEMY_TARGET, 0 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is Minion target) Generic.SummonThatAttack(c, s, Cards.FromId("JAIL_454t"), V("JAIL_454", "undead", 4), target);
				})
			}));

			// ---------------------------------------------------------------- Demon Hunter
			// [JAIL_206] Dark Bribe (2) — Draw 3 cards. Pick one to give to your opponent.
			cards.Add("JAIL_206", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var drawn = new List<IPlayable>();
					for (int i = 0; i < V("JAIL_206", "draws", 3); i++)
						if (Generic.Draw(c) is IPlayable p && p.Zone?.Type == Zone.HAND) drawn.Add(p);
					if (drawn.Count > 0) Generic.DiscoverFromCards(c, s, drawn, (g2, c2, s2, card) => { if (card.Zone?.Type == Zone.HAND) Generic.TakeIntoHand(c2.Opponent, card); });
				})
			}));
			// [JAIL_719] Irida Sinseeker (4) — Lifesteal. Battlecry: Send your deck to the Void, except 1 card. At the start of your turns, get two
			// cards from the Void.
			cards.Add("JAIL_719", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Generic.SendDeckToVoid(c, V("JAIL_719", "keep", 1));
					Generic.AddEnchantmentBlock(g, Cards.FromId("JAIL_719e2"), (IPlayable)s, c, 0, 0, 0);
				})
			}));
			cards.Add("JAIL_719e2", new CardDef(new Power
			{
				Enchant = new Enchant(),
				Trigger = new Trigger(TriggerType.TURN_START) { SingleTask = new CustomTask((g, c, s, t, stack) => Generic.TakeFromVoid(c, V("JAIL_719", "cards", 2))) }
			}));
			// [JAIL_721] Tras'tath, Soul Parasite (6) — Prepare, Rush. After you summon a Demon, gain its stats.
			cards.Add("JAIL_721", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_SUMMON)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p is Minion m && m.IsRace(Race.DEMON)),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if ((t ?? g.CurrentEventData?.EventSource as IPlayable) is Minion demon && demon.Id != s.Id && s is Minion me && me.Zone?.Type == Zone.PLAY)
							Generic.AddEnchantmentBlock(g, Cards.FromId("JAIL_721e"), me, me, demon.AttackDamage, Math.Max(0, demon.Health), 0);
					})
				}
			}));
			cards.Add("JAIL_721e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(0), Effects.Health_N(0)) { UseScriptTag = true } }));
			// [JAIL_730] Stardust Scythe (4, weapon) — After your hero attacks, get a Void Soul.
			cards.Add("JAIL_730", new CardDef(new Power { Trigger = new Trigger(TriggerType.AFTER_ATTACK) { TriggerSource = TriggerSource.HERO, SingleTask = GetVoidSoul } }));
			// [JAIL_732] Void Soul (1) — Summon a random @-Cost Demon. Improve your future Void Souls. (client player 4662)
			Func<IPlayable, int> voidSoulCost = p => V("JAIL_732", "cost", 1) + p.Controller[VOID_SOUL_IMPROVEMENTS];
			cards.Add("JAIL_732", new CardDef(new Power
			{
				HandCounter = voidSoulCost,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int cost = voidSoulCost((IPlayable)s);
					Summon(g, c, RandomCard(g, x => x.Type == CardType.MINION && x.IsRace(Race.DEMON) && x.Cost == cost && !x.NeverRandomlyOnBoard), s);
					c[VOID_SOUL_IMPROVEMENTS] = c[VOID_SOUL_IMPROVEMENTS] + 1;
				})
			}));
			// [JAIL_733] Vicious Voidscale (3) — Taunt. Deathrattle: Get a Void Soul.
			cards.Add("JAIL_733", new CardDef(new Power { DeathrattleTask = GetVoidSoul }));
			// [JAIL_734] Hellraiser (3) — Taunt. Battlecry: Discover a card in your deck. If it's empty, gain +4/+4 instead.
			cards.Add("JAIL_734", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c.DeckZone.Count == 0, new CustomTask((g, c, s, t, stack) => { if (s is Minion me) Give(g, s, me, "JAIL_734e"); }),
					new CustomTask((g, c, s, t, stack) => Generic.DiscoverFromDeck(c, s, Generic.PickFromDeck(c, p => true))))
			}));
			cards.Add("JAIL_734e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("JAIL_734", "stats", 4))) }));
			// [JAIL_890] Captive Nathrezim (10) — Prepare, Taunt. ALL minions cost (2) more. (both hands)
			cards.Add("JAIL_890", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HANDS, Effects.AddCost(V("JAIL_890", "more", 2))) { Condition = SelfCondition.IsMinion, ShowAs = "JAIL_890e" }
			}));
			// [JAIL_891] Void Blast (2) — Deal $3 damage to a minion. If it dies, get a Void Soul.
			cards.Add("JAIL_891", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					Generic.DamageCharFunc((IPlayable)s, m, V("JAIL_891", "damage", 3), true);
					if (m.IsDead || m.ToBeDestroyed || m.Health <= 0) GetCard(c, Cards.FromId("JAIL_732"), s);
				})
			}));
			// [JAIL_892] Cosmic Manifestations (2) — Deal $2 damage. Shuffle a random Demon Hunter spell into your deck. Outcast: Do it again.
			cards.Add("JAIL_892", new CardDef(TargetAny, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int round = 0; round < (c.OutcastPlayId == s.Id ? 2 : 1); round++)
					{
						if (t is ICharacter ch && !ch.IsDead && ch.Health > 0) Generic.DamageCharFunc((IPlayable)s, ch, V("JAIL_892", "damage", 2), true);
						if (RandomCard(g, x => x.Type == CardType.SPELL && x.HasClass(CardClass.DEMONHUNTER)) is Card spell)
							Generic.ShuffleIntoDeck.Invoke(c, s, Entity.FromCard(c, spell, new EntityData { { GameTag.CREATOR, s.Id } }));
					}
				})
			}));
		}
	}
}
