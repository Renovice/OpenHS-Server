// HsServer addition (not upstream SabberStone): The Dalaran Heist — chunk 3: the Hero Powers (Normal "p", Heroic "px") of bosses
// DALA_BOSS_51..75 with the tokens and enchantments they make, and the boss-only deck cards of other sets the Heist bosses play
// (Dark Iron Bouncer, Infuriated Banker, Kirin Tor Guard / Elemental, Moragg's Beams, Gluttonous Trogg, Archmage's Apprentice,
// Commander Bolan's ICCA01 minions). Shared rules: Actions/DalaranHeistGroup3.cs (extra spell casts, Brawl survivor, Next...'s
// swap, a minion from the opponent's deck, a card shown as it enters a hidden hand). Every number goes through CardValues (V).
// Live shapes: scratchpad boss-games (Power.log text of the recorded Heist games), cited per card.
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
		/// <summary>The minion an enchantment's effect belongs to (a granted Deathrattle runs with the enchantment or the minion as
		/// its source).</summary>
		private static Minion HeistC3Bearer(IEntity s) => s is Enchantment e ? e.Target as Minion : s as Minion;

		/// <summary>"Battlecry: Deal N damage to an enemy" (Fruit Slasher).</summary>
		private static readonly Dictionary<PlayReq, int> HeistC3EnemyBattlecry =
			new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_IF_AVAILABLE, 0 }, { PlayReq.REQ_ENEMY_TARGET, 0 } };

		static partial void HeistBossPowers3(IDictionary<string, CardDef> cards)
		{
			// ================================================================ 51 Millificent Manastorm
			// [DALA_BOSS_51p] Tinker (2) — Hero Power: Summon a 0/4 Squirrel Bomb.
			cards.Add("DALA_BOSS_51p", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Summon(g, c, Cards.FromId("DALA_BOSS_51t"), s))
			}));
			// [DALA_BOSS_51t] Elementium Squirrel Bomb (0/4) — At the start of your turn, destroy this and deal 2 damage to all enemies.
			// (in the text's order: destroyed, then the damage — from the bomb)
			cards.Add("DALA_BOSS_51t", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Minion bomb) || bomb.ToBeDestroyed) return;
						Generic.Destroy(bomb, bomb);
						new DamageTask(V("DALA_BOSS_51t", "damage", 2), EntityType.ENEMIES).Process(g, c, s, null);
					})
				}
			}));
			// [DALA_BOSS_51t2] Thorium Chicken (5/1) — After your opponent plays a card, deal 3 damage to their hero.
			cards.Add("DALA_BOSS_51t2", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_CARD)
				{
					TriggerSource = TriggerSource.ENEMY,
					SingleTask = new DamageTask(V("DALA_BOSS_51t2", "damage", 3), EntityType.OP_HERO)
				}
			}));

			// ================================================================ 52 Lieutenant Sinclari
			// [DALA_BOSS_52p] / [DALA_BOSS_52px] Hold the Gates! (2) — Give a minion +2 (+4) Health and Taunt.
			cards.Add("DALA_BOSS_52p", new CardDef(TargetMinion, new Power { PowerTask = new AddEnchantmentTask("DALA_BOSS_52e", EntityType.TARGET) }));
			cards.Add("DALA_BOSS_52px", new CardDef(TargetMinion, new Power { PowerTask = new AddEnchantmentTask("DALA_BOSS_52ex", EntityType.TARGET) }));
			cards.Add("DALA_BOSS_52e", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(V("DALA_BOSS_52p", "health", 2)), Effects.TauntEff) }));
			cards.Add("DALA_BOSS_52ex", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(V("DALA_BOSS_52px", "health", 4)), Effects.TauntEff) }));

			// ================================================================ 53 Dancin' Deryl
			// [DALA_BOSS_53p] / [DALA_BOSS_53px] Boogie Woogie (2) — Give a friendly minion "Deathrattle: Resummon this minion."
			// (the text says friendly: the client's play requirement lists only a minion)
			cards.Add("DALA_BOSS_53p", new CardDef(TargetFriendlyMinionToPlay, new Power { PowerTask = new AddEnchantmentTask("DALA_BOSS_53e", EntityType.TARGET) }));
			cards.Add("DALA_BOSS_53px", new CardDef(TargetFriendlyMinionToPlay, new Power { PowerTask = new AddEnchantmentTask("DALA_BOSS_53e", EntityType.TARGET) }));
			// [DALA_BOSS_53e] Boogie Beyond — Granted "Deathrattle: Resummon this minion." (a new copy of its card in its slot, as Wax Rager)
			cards.Add("DALA_BOSS_53e", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (HeistC3Bearer(s) is Minion me) Summon(g, me.Controller, me.Card, me, me.LastBoardPosition);
				})
			}));

			// ================================================================ 54 Locksmith Zibb
			// [DALA_BOSS_54p] Spell Lock — Passive: Spells Overload for their Cost. (Every spell, either player's — the text names no
			// side; outof.games/hearthstone Locksmith Zibb: "all spells Overload for their price". The Cost it was played for; its
			// player's Overload through Generic.AddOverload. DOUBT: unrecorded — when the Overload lands and its block.)
			cards.Add("DALA_BOSS_54p", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.ALL,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(t is Spell spell) || spell.Controller == null) return;
						PlayHistoryEntry played = spell.Controller.PlayHistory.LastOrDefault(h => h.SourceId == spell.Id);
						int cost = played.SourceCard != null ? played.Cost : spell.Cost;
						int amount = Generic.AddOverload(spell.Controller, cost);
						if (amount > 0) g.RaiseEvent(() => g.TriggerManager.OnOverloadTrigger(spell, amount));
					})
				}
			}));

			// ================================================================ 55 Captain Hannigan
			// [DALA_BOSS_55p] / [DALA_BOSS_55px] Raise the Alarm (2) — Summon a 1/4 Kirin Tor Guard.
			foreach (string id in new[] { "DALA_BOSS_55p", "DALA_BOSS_55px" })
				cards.Add(id, new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
				{
					PowerTask = new CustomTask((g, c, s, t, stack) => Summon(g, c, Cards.FromId("DALA_503"), s))
				}));
			// [DALA_503] Kirin Tor Guard (1/4) — Has +1 Attack for each other friendly Kirin Tor Guard.
			cards.Add("DALA_503", new CardDef(new Power
			{
				Aura = new AdaptiveEffect(GameTag.ATK, EffectOperator.ADD, p =>
					V("DALA_503", "attack", 1) * p.Controller.BoardZone.Count(m => m != p && m.Card.Id == p.Card.Id))
			}));

			// ================================================================ 56 Queen Wagtoggle
			// [DALA_BOSS_56p] Bribery (2) — Summon a minion from your opponent's deck. When it dies, give it back. (the same entity
			// changes side: Generic.SummonFromOpponentDeck; it carries "Bribed" DALA_BOSS_56e)
			cards.Add("DALA_BOSS_56p", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (Generic.SummonFromOpponentDeck(c, s) is Minion m) Give(g, s, m, "DALA_BOSS_56e");
				})
			}));
			// [DALA_BOSS_56e] Bribed — Deathrattle: Return this to your opponent's hand. (from the graveyard: a new card, user rule
			// "generated cards are new entities"; Generic.ReturnToHand makes it)
			cards.Add("DALA_BOSS_56e", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (HeistC3Bearer(s) is Minion me) Generic.ReturnToHand(me.Controller.Opponent, me);
				})
			}));

			// ================================================================ 57 Trade Prince Gallywix
			// [DALA_BOSS_57p] Greed is Good — Passive: Buy a copy of the first card your opponent plays each turn for a Coin. Live
			// (live-20260929-183021 89591, 90262, 92329): inside the played card's PLAY block, a TRIGGER block (TRIGGER_VISUAL,
			// ShowInHistory): the copy into the boss's hand shown (SHOW_ENTITY, TARGET, DISPLAYED_CREATOR), "Gallywix's Coin"
			// GVG_028t into the player's hand, the copy hidden again, then TAG_SCRIPT_DATA_NUM_1 = 1 on the Hero Power (back to 0 at
			// the next turn start: the "first card each turn" mark).
			cards.Add("DALA_BOSS_57p", new CardDef(new Power
			{
				Trigger = new MultiTrigger(
					new Trigger(TriggerType.PLAY_CARD)
					{
						TriggerSource = TriggerSource.ENEMY,
						Condition = new SelfCondition(p => p.Game.CurrentPlayer == p.Controller),
						SingleTask = new CustomTask((g, c, s, t, stack) =>
						{
							// (Gallywix's Coin "Won't trigger Gallywix": not bought, not the first card — live 140614 74794-75011)
							if (!(s is IPlayable hp) || hp[GameTag.TAG_SCRIPT_DATA_NUM_1] > 0 || t == null || !Generic.TriggersGallywix(t)) return;
							IPlayable copy = null;
							if (!c.HandZone.IsFull)
							{
								copy = Entity.FromCard(c, t.Card, new EntityData { [GameTag.CREATOR] = s.Id, [GameTag.DISPLAYED_CREATOR] = s.Id });
								if (Generic.AddHandPhase.Invoke(c, copy)) Generic.ShowCardInHand(copy);
								else copy = null;
							}
							AddToHand(c.Opponent, Cards.FromId("GVG_028t"));
							Generic.HideShownCard(copy);
							hp[GameTag.TAG_SCRIPT_DATA_NUM_1] = 1;
						})
					},
					new Trigger(TriggerType.TURN_START)
					{
						EitherTurn = true,
						SingleTask = new CustomTask((g, c, s, t, stack) =>
						{
							if (s is IPlayable hp && hp[GameTag.TAG_SCRIPT_DATA_NUM_1] != 0) hp[GameTag.TAG_SCRIPT_DATA_NUM_1] = 0;
						})
					})
			}));

			// ================================================================ 58 Boommaster Flark
			// [DALA_BOSS_58p] Blast Powder (2) — Coat a minion in Blast Powder. When it takes damage, destroy it.
			cards.Add("DALA_BOSS_58p", new CardDef(TargetMinion, new Power { PowerTask = new AddEnchantmentTask("DALA_BOSS_58e", EntityType.TARGET) }));
			// [DALA_BOSS_58e] Blast Powder — If this minion takes damage, it will be destroyed.
			cards.Add("DALA_BOSS_58e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TAKE_DAMAGE)
				{
					TriggerSource = TriggerSource.ENCHANTMENT_TARGET,
					EitherTurn = true,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is Enchantment e && e.Target is Minion m && !m.IsDead && !m.ToBeDestroyed) Generic.Destroy(m, e);
					})
				}
			}));

			// ================================================================ 59 Madam Goya
			// [DALA_BOSS_59p] / [DALA_BOSS_59px] Blackmail (2) — Replace a random card in your opponent's hand with an 'Extortion.'
			// The card is kept aside (wiki Madam Goya: "The card replaced by Extortion retains all increment attribute buffs after
			// being given back to the player": the same entity) in the Extortion's slot; the Extortion's TAG_SCRIPT_DATA_NUM_1 names
			// it (its text "Return your '{0}'": CardTextBuilderType REFERENCE_SCRIPT_DATA_NUM_1_ENTITY).
			foreach (string id in new[] { "DALA_BOSS_59p", "DALA_BOSS_59px" })
				cards.Add(id, new CardDef(new Power
				{
					PowerTask = new CustomTask((g, c, s, t, stack) =>
					{
						Controller op = c.Opponent;
						var hand = op.HandZone.GetAll().Where(p => p.Card.Id != "DALA_BOSS_59t3").ToList();
						if (hand.Count == 0) return;
						IPlayable taken = hand[g.Random.Next(hand.Count)];
						if (hand.Count > 1) g.OnRandomHappened(true);
						int slot = taken.ZonePosition;
						Generic.RemoveFromHand(op, taken);
						IPlayable extortion = Entity.FromCard(op, Cards.FromId("DALA_BOSS_59t3"), new EntityData
						{
							[GameTag.CREATOR] = s.Id,
							[GameTag.TAG_SCRIPT_DATA_NUM_1] = taken.Id,
						});
						Generic.AddHandPhaseAt(op, extortion, slot);
					})
				}));
			// [DALA_BOSS_59t3] Extortion (2) — Return your '{0}' to your hand. (the card it replaced, from aside; a full hand: it burns)
			cards.Add("DALA_BOSS_59t3", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is IPlayable me) || !g.IdEntityDic.TryGetValue(me[GameTag.TAG_SCRIPT_DATA_NUM_1], out IPlayable kept)) return;
					if (kept.Zone?.Type != Zone.SETASIDE || !Generic.RemoveFromZone.Invoke(kept.Controller, kept)) return;
					if (kept.Controller != c) Generic.GiveTo(kept, c);
					Generic.AddHandPhase.Invoke(c, kept);
				})
			}));
			// [DALA_BOSS_59t] Lotus Bruiser (2/3) — Whenever an 'Extortion' is played, gain +2/+2.
			cards.Add("DALA_BOSS_59t", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.PLAY_CARD)
				{
					TriggerSource = TriggerSource.ALL,
					EitherTurn = true,
					Condition = new SelfCondition(p => p.Card.Id == "DALA_BOSS_59t3"),
					SingleTask = new AddEnchantmentTask("DALA_BOSS_59e", EntityType.SOURCE)
				}
			}));
			cards.Add("DALA_BOSS_59e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("DALA_BOSS_59t", "stats", 2))) }));
			// [DALA_BOSS_59t2] Fruit Slasher (4/2) — Battlecry: Deal 4 damage to an enemy. Combo: And 1 damage to all other enemies.
			cards.Add("DALA_BOSS_59t2", new CardDef(HeistC3EnemyBattlecry, new Power
			{
				PowerTask = new DamageTask(V("DALA_BOSS_59t2", "damage", 4), EntityType.TARGET),
				ComboTask = ComplexTask.Create(
					new DamageTask(V("DALA_BOSS_59t2", "damage", 4), EntityType.TARGET),
					new DamageTask(V("DALA_BOSS_59t2", "splash", 1), EntityType.ENEMIES_NOTARGET))
			}));

			// ================================================================ 60 Archmage Khadgar
			// [DALA_BOSS_60p] / [DALA_BOSS_60px] Summon Elemental (2) — Summon a 2/3 Elemental. It copies your spells. (the Kirin Tor
			// Elemental DALA_504; live-20261002-204752 146452: POWER block, TARGET on the Hero Power, the summon)
			foreach (string id in new[] { "DALA_BOSS_60p", "DALA_BOSS_60px" })
				cards.Add(id, new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
				{
					PowerTask = new CustomTask((g, c, s, t, stack) => Summon(g, c, Cards.FromId("DALA_504"), s))
				}));
			// [DALA_504] Kirin Tor Elemental (2/3) — Your spells cast an additional time. Live (148363): each Elemental's empty TRIGGER
			// block when the spell is played, then the spell casts once more per Elemental (Generic.EXTRA_SPELL_CASTS, CastSpell).
			cards.Add("DALA_504", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.CAST_SPELL)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (t is Spell spell) spell[Generic.EXTRA_SPELL_CASTS] = spell[Generic.EXTRA_SPELL_CASTS] + 1;
					})
				}
			}));
			// [KARA_00_08] Archmage's Apprentice (2/4) — Whenever you cast a spell, shuffle a copy of it into your deck.
			cards.Add("KARA_00_08", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.CAST_SPELL)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (t is Spell spell && !c.DeckZone.IsFull)
							Generic.ShuffleIntoDeck.Invoke(c, s, Entity.FromCard(c, spell.Card, new EntityData { [GameTag.CREATOR] = s.Id }));
					})
				}
			}));

			// ================================================================ 75 Commander Bolan
			// Live (session-20261007-113244 136833-142106; wiki Commander_Bolan): Immune from the start, no deck; "A Quest!" (DALA_BOSS_75p)
			// gives the player one of five quests — an enchantment on his hero (NUM_2 the goal, NUM_1 the progress) whose card also marks each
			// quest target. At the start of his turns each quest grows (its TRIGGER block on the hero); a marked target dying counts. Any quest
			// completed ends the battle (wiki: "Upon completion of any event, the reward choice is given and the battle ends": his hero
			// dies — the run's treasure pick follows). Recorded: Bear Liver (start 2, kill 3, turn 4, goal 3), Turtle Escort (start 8, turn 9,
			// goal 5 turns; fails if the turtle dies), Herbalist (start 12, kill 13, turn 14, goal 6), Hogger (start 15, Hogger 17, goal 1).
			// The Murloc quest (kill Megafin) isn't recorded: its spawns and indexes are DOUBT (RESEARCH/live-checks.md).
			const string BolanBear = "DALA_BOSS_75e1", BolanMurloc = "DALA_BOSS_75e2", BolanTurtle = "DALA_BOSS_75e3", BolanHerb = "DALA_BOSS_75e4", BolanHogger = "DALA_BOSS_75e5";
			Enchantment BolanQuest(Controller bolan, string id) => bolan.Hero.AppliedEnchantments?.FirstOrDefault(e => e.Card.Id == id);
			bool Marked(IPlayable p, string id) => p.AppliedEnchantments?.Any(e => e.Card.Id == id) == true;
			Minion BolanSpawn(Game g, Controller c, IEntity s, string cardId, string mark)
			{
				if (c.BoardZone.IsFull || Cards.TryFromId(cardId) is not Card card) return null;
				var m = (Minion)Entity.FromCard(c, card, new EntityData { [GameTag.CREATOR] = s.Id });
				Generic.SummonBlock.Invoke(g, m, -1, s);
				if (mark != null && m.Zone?.Type == Zone.PLAY) Give(g, s, m, mark);
				return m;
			}
			void BolanDefeated(Game g, Controller bolan)
			{
				bolan.Hero[GameTag.IMMUNE] = 0;
				bolan.Hero.ToBeDestroyed = true;
				g.DeathProcessingAndAuraUpdate();
			}
			// (each quest's goal from here, not from its NUM_2: a rebuilt position may not carry it — livecheck 20260929-122856 47703)
			int BolanGoal(string id) => id == BolanBear ? 3 : id == BolanTurtle ? 5 : id == BolanHerb ? 6 : 1;
			void BolanProgress(Game g, Controller bolan, string id)
			{
				if (!(BolanQuest(bolan, id) is Enchantment q)) return;
				q[GameTag.TAG_SCRIPT_DATA_NUM_1] = q[GameTag.TAG_SCRIPT_DATA_NUM_1] + 1;
				if (q[GameTag.TAG_SCRIPT_DATA_NUM_1] >= BolanGoal(id)) BolanDefeated(g, bolan);
			}
			string[] herbs = { "UNG_058", "UNG_844", "UNG_843" };
			string[][] turtleHunters = { new[] { "KAR_300", "UNG_807" }, new[] { "ICC_419", "CS2_172" }, new[] { "UNG_086", "EX1_534t" } };
			var bolanStarts = new (string Quest, int Goal, System.Action<Game, Controller, IEntity> Start)[]
			{
				(BolanBear, BolanGoal(BolanBear), (g, c, s) => BolanSpawn(g, c, s, "CS2_125", BolanBear)),
				(BolanMurloc, BolanGoal(BolanMurloc), (g, c, s) =>
				{
					for (int i = 0; i < 2; i++)
						if (RandomCard(g, x => x.Type == CardType.MINION && x.IsRace(Race.MURLOC) && x.Cost <= 2) is Card mur) BolanSpawn(g, c, s, mur.Id, BolanMurloc);
				}),
				(BolanTurtle, BolanGoal(BolanTurtle), (g, c, s) =>
				{
					BolanSpawn(g, c.Opponent, s, "CS2_119", BolanTurtle); // (the player's turtle to escort)
					foreach (string h in turtleHunters[0]) BolanSpawn(g, c, s, h, null);
				}),
				(BolanHerb, BolanGoal(BolanHerb), (g, c, s) => BolanSpawn(g, c, s, herbs[0], BolanHerb)),
				(BolanHogger, BolanGoal(BolanHogger), (g, c, s) => BolanSpawn(g, c, s, "NEW1_040t", null)),
			};
			cards.Add("DALA_BOSS_75p", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					// (any of the five, one already given included: live 20260929-122856 turn 6 gave Bear Liver again, its progress back to 0,
					// and turn 10 of 20261007-113244 gave Hogger again — its Gnoll)
					var pick = bolanStarts[g.Random.Next(bolanStarts.Length)];
					if (BolanQuest(c, pick.Quest) is Enchantment again) again[GameTag.TAG_SCRIPT_DATA_NUM_1] = 0;
					else Generic.AddEnchantmentBlock(g, Cards.FromId(pick.Quest), c.Hero, c.Hero);
					if (BolanQuest(c, pick.Quest) is Enchantment quest) quest[GameTag.TAG_SCRIPT_DATA_NUM_2] = pick.Goal; // (live: the goal set right after, 137595)
					pick.Start(g, c, c.Hero);
				})
			}));
			foreach (string q in new[] { BolanBear, BolanMurloc, BolanTurtle, BolanHerb, BolanHogger })
				cards.Add(q, new CardDef(new Power { NoScript = true })); // (a quest on his hero / its mark on a target: his hero's triggers read them)
			Trigger BolanTurn(string quest, int index, System.Action<Game, Controller, IEntity, Enchantment> grow) => new Trigger(TriggerType.TURN_START)
			{
				EffectIndex = index, InHistory = true,
				Condition = new SelfCondition(p => BolanQuest(p.Controller, quest) != null),
				SingleTask = new CustomTask((g, c, s, t, stack) => { if (BolanQuest(c, quest) is Enchantment q) grow(g, c, s, q); })
			};
			Trigger BolanKill(string mark, int index, System.Action<Game, Controller, IPlayable> killed) => new Trigger(TriggerType.DEATH)
			{
				TriggerSource = TriggerSource.ALL_MINIONS, EffectIndex = index, InHistory = true,
				Condition = new SelfCondition(p => Marked(p, mark)),
				SingleTask = new CustomTask((g, c, s, t, stack) =>
				{
					IPlayable dead = t ?? g.CurrentEventData?.EventSource as IPlayable;
					if (dead != null) killed(g, c, dead);
				})
			};
			cards.Add("DALA_BOSS_75h", new CardDef(new Power
			{
				SkipsTurnDraw = true, // (no deck: he never draws — live)
				Trigger = new MultiTrigger(
					new Trigger(TriggerType.GAME_START) { SingleTask = new CustomTask((g, c, s, t, stack) => c.Hero[GameTag.IMMUNE] = 1) },
					// Bear Liver: kill the bears — a new bear (Bearshark or Amani War Bear) when none of its bears is left (live: 20260929-122856
					// turns 4 / 6 after a kill, 20261007-113244 turns 10 / 12; turn 8's block empty with his board full)
					BolanKill(BolanBear, 3, (g, c, dead) => BolanProgress(g, c, BolanBear)),
					BolanTurn(BolanBear, 4, (g, c, s, q) =>
					{
						if (c.BoardZone.GetAll().Any(m => Marked(m, BolanBear))) return;
						BolanSpawn(g, c, s, g.Random.Next(2) == 0 ? "ICC_419" : "TRL_550", BolanBear);
					}),
					// Murloc Infestation (DOUBT): a murloc each turn, Megafin on the quest's second turn; Megafin dead: done
					BolanKill(BolanMurloc, 6, (g, c, dead) => { if (dead.Card.Id == "UNG_942t") BolanDefeated(g, c); }),
					BolanTurn(BolanMurloc, 7, (g, c, s, q) =>
					{
						q[HsTag.QUEST_GROWN] = q[HsTag.QUEST_GROWN] + 1;
						if (q[HsTag.QUEST_GROWN] == 2) BolanSpawn(g, c, s, "UNG_942t", BolanMurloc);
						else if (RandomCard(g, x => x.Type == CardType.MINION && x.IsRace(Race.MURLOC) && x.Cost <= 3) is Card mur) BolanSpawn(g, c, s, mur.Id, BolanMurloc);
					}),
					// Turtle Escort: each of his turns it lasts counts; hunters come on its first two (live: none on the 3rd and 4th); the turtle
					// dead: the player loses the battle (user 2026-11-20: "if the turtle dies u lose the fight and the run ends")
					BolanTurn(BolanTurtle, 9, (g, c, s, q) =>
					{
						int n = q[GameTag.TAG_SCRIPT_DATA_NUM_1] + 1;
						q[GameTag.TAG_SCRIPT_DATA_NUM_1] = n;
						if (n >= BolanGoal(BolanTurtle)) { BolanDefeated(g, c); return; }
						if (n < turtleHunters.Length) foreach (string h in turtleHunters[n]) BolanSpawn(g, c, s, h, null);
					}),
					BolanKill(BolanTurtle, 10, (g, c, dead) =>
					{
						BolanQuest(c, BolanTurtle)?.Remove();
						c.Opponent.Hero[GameTag.IMMUNE] = 0;
						c.Opponent.Hero.ToBeDestroyed = true;
						g.DeathProcessingAndAuraUpdate();
					}),
					// Herbalist: pick the flowers (another each turn: Razorpetal Lasher, Humongous Razorleaf, The Voraxx)
					BolanKill(BolanHerb, 13, (g, c, dead) => BolanProgress(g, c, BolanHerb)),
					BolanTurn(BolanHerb, 14, (g, c, s, q) =>
					{
						q[HsTag.QUEST_GROWN] = q[HsTag.QUEST_GROWN] + 1;
						BolanSpawn(g, c, s, herbs[System.Math.Min(q[HsTag.QUEST_GROWN] - 1, herbs.Length - 1)], BolanHerb);
					}),
					// Hogger: his Gnoll when given, Hogger himself on the quest's next turn (index 17, once); Hogger dead: done. (Live's Hogger was
					// 4/10 — DOUBT, RESEARCH/live-checks.md)
					BolanKill(BolanHogger, 16, (g, c, dead) => BolanDefeated(g, c)),
					BolanTurn(BolanHogger, 17, (g, c, s, q) =>
					{
						if (q[HsTag.QUEST_GROWN] > 0) return;
						q[HsTag.QUEST_GROWN] = 1;
						BolanSpawn(g, c, s, "NEW1_040", BolanHogger);
					}))
			}));

			// [DALA_BOSS_58t] The Wire / [DALA_BOSS_58t2] The Other Wire (Boommaster Flark) — While wires are touching, take 5 damage at the
			// start of your turn. ("touching": next to each other on the board. Both carry the text; the pair hurts once — The Wire's
			// trigger — DOUBT, not recorded)
			cards.Add("DALA_BOSS_58t", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is Minion wire && wire.GetAdjacentMinions().Any(m => m.Card.Id == "DALA_BOSS_58t2"))
							Generic.DamageCharFunc(wire, c.Hero, V("DALA_BOSS_58t", "damage", 5), false);
					})
				}
			}));
			cards.Add("DALA_BOSS_58t2", new CardDef(new Power { NoScript = true })); // (its partner's trigger does the damage)

			// [DALA_BOSS_61t] Phase Spider (Sael'orn) — Immune. Can't Attack while a minion is opposite of this. ("opposite": an enemy
			// minion in the same board slot; Immune from its card data)
			cards.Add("DALA_BOSS_61t", new CardDef(new Power
			{
				Aura = new AdaptiveEffect(GameTag.CANT_ATTACK, EffectOperator.SET, p =>
					p is Minion me && me.Zone?.Type == Zone.PLAY && me.ZonePosition < p.Controller.Opponent.BoardZone.Count &&
					!(p.Controller.Opponent.BoardZone[me.ZonePosition] is Location) ? 1 : 0)
			}));

			// [DALA_501] Cheerful Spirit — Whenever you draw a spell, add a copy of it to your deck. (shuffled in)
			cards.Add("DALA_501", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DRAW)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = SelfCondition.IsSpell,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (t is IPlayable drawn && !c.DeckZone.IsFull)
							Generic.ShuffleIntoDeck.Invoke(c, s, Entity.FromCard(c, drawn.Card, new EntityData { [GameTag.CREATOR] = s.Id }));
					})
				}
			}));

			// ================================================================ 61 Sael'orn
			// [DALA_BOSS_61p] Web Grab (2) — Your opponent summons a minion from their hand. (a random one: the opponent's hidden zone;
			// the same entity, Generic.SummonFromHand)
			cards.Add("DALA_BOSS_61p", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Controller op = c.Opponent;
					var minions = op.HandZone.GetAll().OfType<Minion>().Where(m => Generic.IsMinionCard(m)).ToList();
					if (minions.Count == 0 || op.BoardZone.IsFull) return;
					if (minions.Count > 1) g.OnRandomHappened(true);
					Generic.SummonFromHand(op, minions[g.Random.Next(minions.Count)], s);
				})
			}));

			// ================================================================ 62 Haro Setting-Sun
			// [DALA_BOSS_62p] Darken (2) — Give all minions -2 Attack until your next turn.
			cards.Add("DALA_BOSS_62p", new CardDef(new Power { PowerTask = new AddEnchantmentTask("DALA_BOSS_62e", EntityType.ALLMINIONS) }));
			// [DALA_BOSS_62e] Darkened — -2 Attack until next turn (its creator's next turn: gone at the start of the Hero Power owner's
			// turn, also on the enemy minions — an ADD, removable)
			cards.Add("DALA_BOSS_62e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.Attack_N(-V("DALA_BOSS_62p", "attack", 2))),
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					EitherTurn = true,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is Enchantment e && g.CurrentPlayer == (e.Creator?.Controller ?? e.Controller)) RemoveEnchantmentTask.Task.Process(g, c, s, t);
					})
				}
			}));
			// [DALA_BOSS_62px] Darken — Passive: Your opponent's minions have -1 Attack. (live: DALA_BOSS_62ex on each enemy minion,
			// CREATOR the Hero Power)
			// (2026-11-16) an enchantment given to each enemy minion as it is summoned, not an aura: live, Confuse on a Darkened 1/4 with 2
			// damage made it 2/1 — a "set" wipes an earlier enchantment, while an aura (rulebook: processed after) would leave 1/1
			// (livecheck session-20261008-012951 :141368)
			cards.Add("DALA_BOSS_62px", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.SUMMON)
				{
					TriggerSource = TriggerSource.OP_MINIONS,
					SingleTask = new AddEnchantmentTask("DALA_BOSS_62ex", EntityType.TARGET)
				}
			}));
			cards.Add("DALA_BOSS_62ex", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(-V("DALA_BOSS_62px", "attack", 1))) }));

			// ================================================================ 63 Dagg Cruelmight
			// [DALA_BOSS_63p] / [DALA_BOSS_63px] Bully (2) — Deal $2 ($4) damage to a damaged minion. (live-20261004-224614 117746)
			var bully = new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_DAMAGED_TARGET, 0 }, { PlayReq.REQ_MINION_TARGET, 0 } };
			cards.Add("DALA_BOSS_63p", new CardDef(bully, new Power { PowerTask = new DamageTask(V("DALA_BOSS_63p", "damage", 2), EntityType.TARGET, true) }));
			cards.Add("DALA_BOSS_63px", new CardDef(bully, new Power { PowerTask = new DamageTask(V("DALA_BOSS_63px", "damage", 4), EntityType.TARGET, true) }));

			// ================================================================ 64 Banker Biggs
			// [DALA_BOSS_64p] Invest! (2) — Return a friendly minion to your hand. Give it +4/+4.
			ISimpleTask invest = new CustomTask((g, c, s, t, stack) =>
			{
				if (t is Minion m && Generic.ReturnToHand(c, m) is IPlayable back) Give(g, s, back, "DALA_BOSS_64e");
			});
			cards.Add("DALA_BOSS_64p", new CardDef(TargetFriendlyMinionToPlay, new Power { PowerTask = invest }));
			// [DALA_BOSS_64px] Invest! (2) — Return a friendly minion to your hand, then give it +4/+4 and copy it. Live
			// (live-20261005-140614 47176, 53300): POWER 0 (the return and the buff), SLUSH_TIME 3000, POWER 1 (a copy into the hand,
			// its enchantment copied) — Power.SecondPowerTask (Generic.SecondPowerBlock, also for Hero Powers now).
			cards.Add("DALA_BOSS_64px", new CardDef(TargetFriendlyMinionToPlay, new Power
			{
				PowerTask = invest,
				SecondPowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is IPlayable back && back.Zone?.Type == Zone.HAND && back.Controller == c && !c.HandZone.IsFull)
						Generic.Copy(c, s, back, Zone.HAND);
				})
			}));
			cards.Add("DALA_BOSS_64e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("DALA_BOSS_64p", "stats", 4))) }));

			// ================================================================ 65 Valdera Highborne
			// [DALA_BOSS_65p] Impervious — Passive: Your hero can only take up to 3 damage at a time. (live-20260929-183021 58427: a
			// TRIGGER block, TRIGGER_VISUAL, lowering PREDAMAGE to 3 — only for a bigger hit; the shared damage cap, rule book DH7c)
			cards.Add("DALA_BOSS_65p", new CardDef(new Power
			{
				Trigger = Generic.PreDamageCapTrigger(TriggerSource.HERO, V("DALA_BOSS_65p", "cap", 3))
			}));

			// ================================================================ 66 Marei Loom
			// [DALA_BOSS_66p] Next... (2) — Swap the left-most card in each player's hand with one in their deck. (its user first, live
			// live-20260929-183021 4300: Generic.SwapLeftmostWithDeck)
			cards.Add("DALA_BOSS_66p", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Generic.SwapLeftmostWithDeck(c, s);
					Generic.SwapLeftmostWithDeck(c.Opponent, s);
				})
			}));

			// ================================================================ 67 Archmage Vargoth
			// [DALA_BOSS_67p] Arcane Runes (2) — Add a random Arcane spell to your hand. (the card's entourage: Arcane Blast, Greater
			// Arcane Missiles, Arcane Missiles, Arcane Intellect, Arcane Explosion — wiki Archmage Vargoth (boss) "Generated cards"
			// lists those five; live-20261002-204752 105424: one hidden card into the boss's hand, DISPLAYED_CREATOR the Hero Power)
			string[] runes = { "AT_004", "CFM_623", "EX1_277", "CS2_023", "CS2_025" };
			cards.Add("DALA_BOSS_67p", new CardDef(runes, new Dictionary<PlayReq, int> { { PlayReq.REQ_HAND_NOT_FULL, 0 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c.HandZone.IsFull) return;
					g.OnRandomHappened(true);
					Generic.AddHandPhase.Invoke(c, Entity.FromCard(c, Cards.FromId(runes[g.Random.Next(runes.Length)]),
						new EntityData { [GameTag.CREATOR] = s.Id, [GameTag.DISPLAYED_CREATOR] = s.Id }));
				})
			}));

			// ================================================================ 68 Lilayell Suntear
			// [DALA_BOSS_68p] Mini Portal — Passive: After you cast a spell, summon a random 1-Cost minion.
			// [DALA_BOSS_68px] Mega Portal — Passive: After you cast a spell, summon a random minion of the same Cost. (live
			// live-20261002-204752 60184: a TRIGGER block inside the spell's PLAY block, the minion with DISPLAYED_CREATOR the Hero
			// Power; the Cost it was played for)
			cards.Add("DALA_BOSS_68p", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) => HeistC3PortalSummon(g, c, s, V("DALA_BOSS_68p", "cost", 1)))
				}
			}));
			cards.Add("DALA_BOSS_68px", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(t is Spell spell)) return;
						PlayHistoryEntry played = c.PlayHistory.LastOrDefault(h => h.SourceId == spell.Id);
						HeistC3PortalSummon(g, c, s, played.SourceCard != null ? played.Cost : spell.Cost);
					})
				}
			}));

			// ================================================================ 69 Archmage Kalec
			// [DALA_BOSS_69p] / [DALA_BOSS_69px] Dragonwrath (2) — Give your hero Spell Damage +1. (+@ currently) Live
			// (live-20261002-204752 125611): the Hero Power's TAG_SCRIPT_DATA_NUM_1 counts up (its "(+@ currently)"), the hero's own
			// SPELLPOWER rises by 1 (no enchantment; Controller.CurrentSpellPower reads it).
			foreach (string id in new[] { "DALA_BOSS_69p", "DALA_BOSS_69px" })
				cards.Add(id, new CardDef(new Power
				{
					PowerTask = new CustomTask((g, c, s, t, stack) =>
					{
						int n = V(id, "spell damage", 1);
						if (s is IPlayable hp) hp[GameTag.TAG_SCRIPT_DATA_NUM_1] = hp[GameTag.TAG_SCRIPT_DATA_NUM_1] + n;
						c.Hero[GameTag.SPELLPOWER] = c.Hero[GameTag.SPELLPOWER] + n;
					})
				}));

			// ================================================================ 70 Sky Captain Smiggs
			// [DALA_BOSS_70p] Bombardment (2) — Deal $2 damage to all minions. Takes 2 turns to reload.
			// [DALA_BOSS_70px] Bombardment (2) — Deal $3 damage to all enemy minions. Takes 2 turns to reload. Live
			// (live-20261003-185040 68657-71400): the damage, then "Bombardment Recharge Player Enchantment" DALA_BOSS_70e on the player
			// (HERO_POWER_DISABLED 1); at the end of each of the player's turns its TAG_SCRIPT_DATA_NUM_1 counts 1, 2, 3 and at 3 it goes
			// (HERO_POWER_DISABLED 0): used on turn 4, usable again on turn 10.
			cards.Add("DALA_BOSS_70p", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					new DamageTask(V("DALA_BOSS_70p", "damage", 2), EntityType.ALLMINIONS, true),
					new AddEnchantmentTask("DALA_BOSS_70e", EntityType.CONTROLLER))
			}));
			cards.Add("DALA_BOSS_70px", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					new DamageTask(V("DALA_BOSS_70px", "damage", 3), EntityType.OP_MINIONS, true),
					new AddEnchantmentTask("DALA_BOSS_70e", EntityType.CONTROLLER))
			}));
			cards.Add("DALA_BOSS_70e", new CardDef(new Power
			{
				Aura = new Aura(AuraType.CONTROLLER, new Effect(GameTag.HERO_POWER_DISABLED, EffectOperator.ADD, 1)),
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Enchantment e)) return;
						e[GameTag.TAG_SCRIPT_DATA_NUM_1] = e[GameTag.TAG_SCRIPT_DATA_NUM_1] + 1;
						if (e[GameTag.TAG_SCRIPT_DATA_NUM_1] >= V("DALA_BOSS_70p", "reload ends", 3)) RemoveEnchantmentTask.Task.Process(g, c, s, t);
					})
				}
			}));

			// ================================================================ 71 Anarii Duskgrove
			// [DALA_BOSS_71p] / [DALA_BOSS_71px] Summon Protectors (2) — Summon a (two) 2/2 Treant(s) with Taunt. (the Taunt Treant
			// EX1_573t: wiki Anarii Duskgrove "Summoned minions" dbfId 678)
			cards.Add("DALA_BOSS_71p", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("DALA_BOSS_71p", "treants", 1); i++) Summon(g, c, Cards.FromId("EX1_573t"), s);
				})
			}));
			cards.Add("DALA_BOSS_71px", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = V("DALA_BOSS_71px", "treants", 2);
					Generic.BeginSummonGroup(g, s, n);
					for (int i = 0; i < n; i++) Summon(g, c, Cards.FromId("EX1_573t"), s);
				})
			}));

			// ================================================================ 72 Kazamon Steelskin
			// [DALA_BOSS_72p] Steelskin — Passive: You can't damage yourself. Live (live-20261004-224614 104643, live-20261008-012951
			// 1165): inside the damaging card's POWER block a TRIGGER block (TRIGGER_VISUAL, ShowInHistory): PREDAMAGE 0 on the
			// hero, then the trigger tile naming the Hero Power with HISTORY_TARGET = the card that dealt it.
			cards.Add("DALA_BOSS_72p", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.PREDAMAGE)
				{
					TriggerSource = TriggerSource.HERO,
					FastExecution = true,
					InHistory = true,
					Condition = new SelfCondition(p => p.Game.CurrentEventData?.EventNumber > 0 &&
						p.Game.CurrentEventData.EventSource is IPlayable src && src.Controller == p.Controller),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						IPlayable src = g.CurrentEventData?.EventSource as IPlayable;
						Generic.PreventDamage.Process(g, c, s, t, stack);
						if (s is IPlayable hp && src != null) Generic.SelfTriggerTile(hp, new[] { src.Id });
					})
				}
			}));

			// ================================================================ 73 Hesutu Stonewind
			// [DALA_BOSS_73p] / [DALA_BOSS_73px] Earthmother's Rage (2) — Give a friendly minion Windfury (Mega-Windfury).
			// (live-20261004-222041 36611: WINDFURY 1 then DALA_BOSS_73e)
			cards.Add("DALA_BOSS_73p", new CardDef(TargetFriendlyMinionToPlay, new Power { PowerTask = new AddEnchantmentTask("DALA_BOSS_73e", EntityType.TARGET) }));
			cards.Add("DALA_BOSS_73px", new CardDef(TargetFriendlyMinionToPlay, new Power { PowerTask = new AddEnchantmentTask("DALA_BOSS_73ex", EntityType.TARGET) }));
			cards.Add("DALA_BOSS_73e", new CardDef(new Power { Enchant = new Enchant(Effects.Windfury) }));
			cards.Add("DALA_BOSS_73ex", new CardDef(new Power { Enchant = new Enchant(Effects.Windfury, new Effect(GameTag.MEGA_WINDFURY, EffectOperator.SET, 1)) }));

			// ================================================================ 74 Kara Stamper
			// [DALA_BOSS_74p] / [DALA_BOSS_74px] Soul Weave (2) — Discard a card. Summon a random minion of the same Cost. ("Discard a
			// card" = a random one; its Cost in hand; none of that Cost: nothing summoned)
			foreach (string id in new[] { "DALA_BOSS_74p", "DALA_BOSS_74px" })
				cards.Add(id, new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
				{
					PowerTask = new CustomTask((g, c, s, t, stack) =>
					{
						var hand = c.HandZone.GetAll().ToList();
						if (hand.Count == 0) return;
						IPlayable gone = hand[g.Random.Next(hand.Count)];
						if (hand.Count > 1) g.OnRandomHappened(true);
						int cost = gone.Cost;
						Generic.DiscardBlock.Invoke(c, gone);
						Summon(g, c, RandomMinionOfCost(g, cost), s);
					})
				}));

			// ================================================================ boss-deck cards of other sets
			// [BRMA01_3] Dark Iron Bouncer (4/8) — Always wins Brawls. (Generic.ALWAYS_WINS_BRAWLS on itself; Brawl and Badlands Brawler
			// pick their survivor through Generic.BrawlSurvivor)
			cards.Add("BRMA01_3", new CardDef(new Power
			{
				Aura = new Aura(AuraType.SELF, new Effect(Generic.ALWAYS_WINS_BRAWLS, EffectOperator.SET, 1))
			}));
			// [DALA_502] Infuriated Banker (1/1) — Whenever a Coin is played, gain +1/+1. (either player's; a Coin = client COIN_CARD,
			// Generic.IsCoin; live-20260929-183021 41250: its empty POWER block when played)
			cards.Add("DALA_502", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.PLAY_CARD)
				{
					TriggerSource = TriggerSource.ALL,
					EitherTurn = true,
					Condition = new SelfCondition(p => Generic.IsCoin(p)),
					SingleTask = new AddEnchantmentTask("DALA_502e", EntityType.SOURCE)
				}
			}));
			cards.Add("DALA_502e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("DALA_502", "stats", 1))) }));
			// [LOOTA_BOSS_26p3] Beam of Death (3) — Destroy all damaged enemy minions.
			cards.Add("LOOTA_BOSS_26p3", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var doomed = c.Opponent.BoardZone.GetAll().Where(m => m.Damage > 0 && !m.ToBeDestroyed).ToList();
					foreach (Minion m in doomed) Generic.Destroy(m, s, doomed.Count > 1);
				})
			}));
			// [LOOTA_BOSS_26p5] Beam of Fear (3) — Shuffle a random enemy minion into your opponent's deck.
			cards.Add("LOOTA_BOSS_26p5", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var enemies = c.Opponent.BoardZone.GetAll().Where(m => !m.ToBeDestroyed && !m.IsDead).ToList();
					if (enemies.Count == 0) return;
					if (enemies.Count > 1) g.OnRandomHappened(true);
					Generic.MoveToDeck(c.Opponent, s, enemies[g.Random.Next(enemies.Count)]);
				})
			}));
			// [LOOTA_109] Gluttonous Trogg (3/3) — Whenever your opponent casts a spell, gain +1/+1.
			cards.Add("LOOTA_109", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.CAST_SPELL)
				{
					TriggerSource = TriggerSource.ENEMY,
					SingleTask = new AddEnchantmentTask("LOOTA_109e", EntityType.SOURCE)
				}
			}));
			cards.Add("LOOTA_109e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("LOOTA_109", "stats", 1))) }));
			// [ICCA01_007] Eager Rogue (3/2) — Deals no damage while attacking. (the Prevent-the-Damage rule on what it hits when it
			// is the attacker: rule book DH7d through Generic.PreventDamage)
			cards.Add("ICCA01_007", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.PREDAMAGE)
				{
					TriggerSource = TriggerSource.ALL,
					EitherTurn = true,
					FastExecution = true,
					Condition = new SelfCondition(p => p.Game.CurrentEventData?.EventNumber > 0),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is Minion me && g.CurrentEventData?.EventSource?.Id == me.Id && me.IsAttacking)
							Generic.PreventDamage.Process(g, c, s, t, stack);
					})
				}
			}));
			// [ICCA01_009] Needy Hunter (3/3) — Whenever this minion attacks, add a Coin to your hand.
			cards.Add("ICCA01_009", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ATTACK)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) => AddToHand(c, Cards.FromId("GAME_005")))
				}
			}));
			// [ICCA01_010] A. F. Kay (5/2) — Can't attack.
			cards.Add("ICCA01_010", new CardDef(new Power
			{
				Aura = new Aura(AuraType.SELF, new Effect(GameTag.CANT_ATTACK, EffectOperator.SET, 1))
			}));
			// [ICCA01_011] Warlock on Fire (7/1) — At the end of your turn, deal 1 damage to this minion.
			cards.Add("ICCA01_011", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = new DamageTask(V("ICCA01_011", "damage", 1), EntityType.SOURCE) }
			}));
		}

		/// <summary>Lilayell's portals: a random minion of <paramref name="cost"/> summoned by the Hero Power (none of that Cost:
		/// nothing).</summary>
		private static void HeistC3PortalSummon(Game g, Controller c, IEntity s, int cost)
		{
			if (c.BoardZone.IsFull) return;
			Card card = RandomMinionOfCost(g, cost);
			if (card == null) return;
			var m = (Minion)Entity.FromCard(c, card, new EntityData { [GameTag.CREATOR] = s.Id, [GameTag.DISPLAYED_CREATOR] = s.Id });
			Generic.SummonBlock.Invoke(g, m, -1, s);
		}
	}
}
