// HsServer addition (not upstream SabberStone): March of the Lich King (2022) and Return to Naxxramas — the neutral cards
// and the set's shared pieces (Manathirst, "a friendly Undead died after your last turn", Concoctions). Class cards:
// LichKingCardsGenHs2.cs. Every number goes through CardValues (V).
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
		// ---------------------------------------------------------------- shared rules of the set

		/// <summary>Manathirst (Generic.Manathirst): <paramref name="thirsty"/> when the player has the Mana Crystals, else
		/// <paramref name="normal"/> (null: nothing). <paramref name="threshold"/>: a second Manathirst on the card.</summary>
		private static ISimpleTask ManathirstOr(ISimpleTask normal, ISimpleTask thirsty, int threshold = 0) => new CustomTask((g, c, s, t, stack) =>
			(Generic.Manathirst(s as IPlayable, threshold) ? thirsty : normal)?.Process(g, c, s, t, stack));

		/// <summary>"If a friendly Undead died after your last turn, …" (Generic.UndeadDiedAfterYourLastTurn; the check first,
		/// so the card glows: PoweredUp).</summary>
		private static ISimpleTask IfUndeadDied(ISimpleTask then) => ComplexTask.If((g, c, s) => Generic.UndeadDiedAfterYourLastTurn(c), then);

		private static bool IsUndead(IPlayable p) => IsRaceCard(p, Race.UNDEAD);

		/// <summary>A random friendly minion (alive, on the board) matching <paramref name="where"/>, or null.</summary>
		private static Minion RandomFriendly(Game g, Controller c, System.Func<Minion, bool> where = null, IEntity except = null)
		{
			var list = c.BoardZone.Where(m => !m.IsDead && !m.ToBeDestroyed && m != except && (where == null || where(m))).ToList();
			return list.Count == 0 ? null : list[g.Random.Next(list.Count)];
		}

		/// <summary>The play requirement of "Battlecry: If a friendly Undead died after your last turn, deal N damage".</summary>
		private static readonly Dictionary<PlayReq, int> TargetIfUndeadDied =
			new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_IF_AVAILABLE_AND_UNDEAD_DIED_AFTER_LAST_TURN, 0 } };

		private static readonly Dictionary<PlayReq, int> TargetEnemyMinion =
			new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_ENEMY_TARGET, 0 }, { PlayReq.REQ_MINION_TARGET, 0 } };


		// ---------------------------------------------------------------- Concoctions
		// Sources (HearthstoneTopDecks, Vicious Syndicate): "When you add a second concoction to your hand, they are mixed
		// together into one card with their effects combined (but mana cost remains at 3)"; two of the same mix too. Client
		// data: tag 2500 marks the Concoctions; an unmixed one says "Add another Concoction to your hand to mix together!";
		// each Mixed Concoction's text is its two parts' effects (RLK_570t1..t5 → RLK_570t1t1 … RLK_570tt5).

		private const GameTag CONCOCTION = (GameTag)2500;

		private static bool IsUnmixedConcoction(Card card) => card[CONCOCTION] > 0 && (card.Text ?? "").Contains("mix together");

		private static string ConcoctionEffect(Card card) =>
			System.Text.RegularExpressions.Regex.Replace(card.Text ?? "", "<i>.*?</i>|<[^>]+>|\\[x\\]|\\$", "",
				System.Text.RegularExpressions.RegexOptions.Singleline).Replace("\n", " ").Trim(); // (the italic mix hint spans lines)

		private static List<Card> _concoctions;

		/// <summary>The five unmixed Concoctions (client data).</summary>
		private static List<Card> Concoctions => _concoctions ??= Cards.All.Where(IsUnmixedConcoction).OrderBy(x => x.Id).ToList();

		/// <summary>The verbs that open an effect's sentences ("Summon", "Destroy", "Deal", "Add", "Draw"; not "It" / "They").</summary>
		private static HashSet<string> EffectVerbs(Card card) =>
			new HashSet<string>(ConcoctionEffect(card).Split('.').Select(x => x.Trim()).Where(x => x.Length > 0)
				.Select(x => x.Split(' ')[0]).Where(w => w != "It" && w != "They"));

		/// <summary>The Mixed Concoction of <paramref name="a"/> and <paramref name="b"/>: its sentences open with both parts'
		/// verbs (a doubled pair: the one verb alone — "Draw 4 cards", "Deal 3 damage, twice"). Client text only.</summary>
		private static Card MixedConcoction(Card a, Card b)
		{
			var want = new HashSet<string>(EffectVerbs(a).Concat(EffectVerbs(b)));
			return Cards.All.Where(x => x[CONCOCTION] > 0 && !IsUnmixedConcoction(x) && x.Type == CardType.SPELL && EffectVerbs(x).SetEquals(want))
				.OrderBy(x => x.Id).FirstOrDefault();
		}

		/// <summary>
		/// A Concoction to the hand: with another unmixed one already there they become one Mixed Concoction (the one in hand
		/// changes into it, as a transform in hand: Generic.ChangeEntityBlock); else it is added. Two of the same: the doubled
		/// mix (two Gleaming: "Draw 4 cards"). The mixed one keeps the card already in hand (its cost enchantments too: the
		/// Concoction's Cost stays 3).
		/// </summary>
		private static void AddConcoction(Game g, Controller c, IEntity source, Card concoction)
		{
			if (concoction == null) return;
			IPlayable waiting = c.HandZone.FirstOrDefault(p => IsUnmixedConcoction(p.Card));
			Card mixed = waiting != null ? MixedConcoction(waiting.Card, concoction) ?? FallbackMix(waiting.Card, concoction) : null;
			if (waiting != null && mixed != null)
			{
				// (2026-10-08: live heist 11032 — the new Concoction enters the hand, then both become a NEW Mixed Concoction:
				// Generic.CombineInHand)
				IPlayable arriving = Entity.FromCard(c, concoction);
				if (!c.HandZone.IsFull) Generic.AddHandPhase.Invoke(c, arriving);
				else c.SetasideZone.Add(arriving);
				if (waiting.Zone?.Type == Zone.HAND) Generic.CombineInHand(c, waiting, arriving, mixed);
				return;
			}
			AddToHand(c, concoction);
		}

		private static Card FallbackMix(Card a, Card b) => null;

		private static Card RandomConcoction(Game g) => Concoctions.Count == 0 ? null : Concoctions[g.Random.Next(Concoctions.Count)];

		/// <summary>The effect of one unmixed Concoction (its part of a Mixed one).</summary>
		private static void ConcoctionPart(Game g, Controller c, IEntity s, Card part)
		{
			string id = part.Id;
			if (id == "RLK_570t1") Summon(g, c, RandomMinionOfCost(g, V("RLK_570t1", "cost", 3)), s);
			else if (id == "RLK_570t2")
			{
				var enemies = c.Opponent.BoardZone.Where(m => !m.IsDead && !m.ToBeDestroyed).ToList();
				if (enemies.Count > 0) Generic.Destroy(enemies[g.Random.Next(enemies.Count)], s);
			}
			else if (id == "RLK_570t3")
			{
				var enemies = Enemies(c);
				if (enemies.Count > 0) Generic.DamageCharFunc((IPlayable)s, enemies[g.Random.Next(enemies.Count)], V("RLK_570t3", "damage", 3), true);
			}
			else if (id == "RLK_570t4")
			{
				if (RandomCard(g, x => !x.HasClass(c.HeroClass) && !x.HasClass(CardClass.NEUTRAL) && !x.HasClass(CardClass.INVALID)) is Card other && !c.HandZone.IsFull)
				{
					IPlayable p = Entity.FromCard(c, other);
					if (Generic.AddHandPhase.Invoke(c, p)) Give(g, s, p, "RLK_570e3");
				}
			}
			else if (id == "RLK_570t5")
				for (int i = 0; i < V("RLK_570t5", "draw", 2); i++) Generic.Draw(c);
		}

		private static void LichKing(IDictionary<string, CardDef> cards)
		{
			LichKingClasses1(cards);
			LichKingClasses2(cards);
			// ---------------------------------------------------------------- Concoction cards
			foreach (Card part in Concoctions)
			{
				Card p2 = part;
				cards.Add(part.Id, new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => ConcoctionPart(g, c, s, p2)) }));
			}
			foreach (Card a in Concoctions)
				foreach (Card b in Concoctions.Where(x => string.CompareOrdinal(x.Id, a.Id) >= 0))
				{
					Card mixed = MixedConcoction(a, b) ?? FallbackMix(a, b);
					if (mixed == null || cards.ContainsKey(mixed.Id)) continue;
					Card pa = a, pb = b;
					cards.Add(mixed.Id, new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) =>
					{
						ConcoctionPart(g, c, s, pa);
						ConcoctionPart(g, c, s, pb);
					}) }));
				}
			cards.Add("RLK_570e3", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("RLK_570t4", "less", 3))) }));

			// ---------------------------------------------------------------- Neutral
			// [NX2_050] Mistake (1) — This has all minion types. (Card data: CARDRACE ALL; nothing to run.)
			cards.Add("NX2_050", new CardDef(new Power { NoScript = true }));
			// [NX2_032] Lost Exarch (4) — Deathrattle: Spend all your Mana. Summon that many 2/2 Zombies with Rush.
			cards.Add("NX2_032", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					int mana = System.Math.Max(0, c.RemainingMana);
					c.UsedMana += mana;
					for (int i = 0; i < mana && !c.BoardZone.IsFull; i++) Summon(g, c, Cards.FromId("NX2_032t"), s);
				})
			}));
			// [NX2_033] Thaddius, Monstrosity (10) — Taunt. Your odd-Cost cards cost (2) less. (Swaps polarity each turn!) The
			// polarity is its alternate text (USE_ALTERNATE_CARD_TEXT, the client's "@" variant: even-Cost); it swaps at the
			// start of each turn.
			cards.Add("NX2_033", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HAND, "NX2_033e")
				{
					// odd while its text is the first one, even while the alternate (the polarity of the Thaddius it is)
					Condition = new SelfCondition(p => p.Controller.BoardZone.FirstOrDefault(m => m.Card.Id == "NX2_033") is Minion th &&
					                                   p.Card.Cost % 2 == (th[GameTag.USE_ALTERNATE_CARD_TEXT] == 0 ? 1 : 0)),
					Restless = true,
				},
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					TriggerSource = TriggerSource.ALL,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is Minion thaddius) thaddius[GameTag.USE_ALTERNATE_CARD_TEXT] = thaddius[GameTag.USE_ALTERNATE_CARD_TEXT] == 0 ? 1 : 0;
					})
				}
			}));
			cards.Add("NX2_033e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("NX2_033", "less", 2))) }));
			cards.Add("NX2_033e2", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("NX2_033", "less", 2))) }));
			// [NX2_034] Rivendare, Warrider (6) — Deathrattle: Shuffle the other 3 Horsemen into your deck. The Horsemen:
			// "Deathrattle: If you had all four Horsemen die this game, destroy the enemy hero."
			string[] horsemen = { "NX2_034", "NX2_034t", "NX2_034t1", "NX2_034t2" };
			cards.Add("NX2_034", new CardDef(new Power
			{
				DeathrattleTask = ComplexTask.Create(
					new CustomTask((g, c, s, t, stack) =>
					{
						foreach (string id in horsemen.Skip(1)) Generic.ShuffleIntoDeck.Invoke(c, s, Entity.FromCard(c, Cards.FromId(id)));
					}),
					HorsemenWin(horsemen))
			}));
			foreach (string id in horsemen.Skip(1))
				cards.Add(id, new CardDef(new Power { DeathrattleTask = HorsemenWin(horsemen) }));
			// [NX2_051] Darkfallen Shadow (3) — Rush. Manathirst (@): Gain Reborn.
			cards.Add("NX2_051", new CardDef(new Power
			{
				PowerTask = ManathirstOr(null, new CustomTask((g, c, s, t, stack) => { if (s is Minion m) m[GameTag.REBORN] = 1; }))
			}));
			// [RLK_029] Shatterskin Gargoyle (6) — Taunt. Deathrattle: Deal 4 damage to a random enemy.
			cards.Add("RLK_029", new CardDef(new Power
			{
				DeathrattleTask = ComplexTask.Create(new RandomTask(1, EntityType.ENEMIES, harmful: true), new DamageTask(V("RLK_029", "damage", 4), EntityType.STACK))
			}));
			// [RLK_070] Infected Peasant (2) — Deathrattle: Summon a 2/2 Undead Peasant.
			cards.Add("RLK_070", new CardDef(new Power { DeathrattleTask = new SummonTask("RLK_070t", SummonSide.DEATHRATTLE) }));
			// [RLK_104] Street Sweeper (4) — Battlecry: Deal 2 damage to all other minions.
			cards.Add("RLK_104", new CardDef(new Power { PowerTask = new DamageTask(V("RLK_104", "damage", 2), EntityType.ALLMINIONS_NOSOURCE) }));
			// [RLK_113] Brittleskin Zombie (3) — Deathrattle: If it's your opponent's turn, deal 3 damage to them.
			cards.Add("RLK_113", new CardDef(new Power
			{
				DeathrattleTask = ComplexTask.If((g, c, s) => g.CurrentPlayer == c.Opponent, new DamageTask(V("RLK_113", "damage", 3), EntityType.OP_HERO))
			}));
			// [RLK_117] Incorporeal Corporal (2) — After this minion attacks, destroy it.
			cards.Add("RLK_117", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK) { TriggerSource = TriggerSource.SELF, SingleTask = new DestroyTask(EntityType.SOURCE) }
			}));
			// [RLK_119] Drakkari Embalmer (3) — Battlecry: Give a friendly Undead Reborn.
			cards.Add("RLK_119", new CardDef(new Dictionary<PlayReq, int>
				{ { PlayReq.REQ_TARGET_IF_AVAILABLE, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_TARGET_WITH_RACE, (int)Race.UNDEAD } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (t is Minion m) m[GameTag.REBORN] = 1; })
			}));
			// [RLK_123] Bone Flinger (2) — Battlecry: If a friendly Undead died after your last turn, deal 2 damage.
			cards.Add("RLK_123", new CardDef(TargetIfUndeadDied, new Power { PowerTask = IfUndeadDied(new DamageTask(V("RLK_123", "damage", 2), EntityType.TARGET)) }));
			// [RLK_218] Silvermoon Arcanist (3) — Spell Damage +2. Battlecry: Your spells can't target heroes this turn.
			// (Spell.TargetingRequirements reads the player's turn stamp; RLK_218e3 shows it on the player.)
			cards.Add("RLK_218", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					c[HsTag.SPELLS_CANT_TARGET_HEROES_TURN] = g.Turn;
					Generic.AddEnchantmentBlock(g, Cards.FromId("RLK_218e3"), (IPlayable)s, c);
				})
			}));
			cards.Add("RLK_218e3", new CardDef(new Power { Enchant = new Enchant(new Effect(HsTag.SPELLS_CANT_TARGET_HEROES_SHOWN, EffectOperator.SET, 1)) { IsOneTurnEffect = true } }));
			// [RLK_219] Sunfury Clergy (3) — Battlecry: Restore 3 Health to all friendly characters. Manathirst (6): Restore 6 instead.
			cards.Add("RLK_219", new CardDef(new Power
			{
				PowerTask = ManathirstOr(new HealTask(V("RLK_219", "heal", 3), EntityType.FRIENDS), new HealTask(V("RLK_219", "manathirst heal", 6), EntityType.FRIENDS))
			}));
			// [RLK_220] Tenacious San'layn (5) — Lifesteal. Whenever this attacks, deal 2 damage to the enemy hero.
			cards.Add("RLK_220", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ATTACK) { TriggerSource = TriggerSource.SELF, SingleTask = new DamageTask(V("RLK_220", "damage", 2), EntityType.OP_HERO) }
			}));
			// [RLK_221] Crystal Broker (3) — Manathirst (5): Summon a random 3-Cost minion. Manathirst (10): Summon an 8-Cost minion instead.
			cards.Add("RLK_221", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var me = (IPlayable)s;
					if (Generic.Manathirst(me, V("RLK_221", "manathirst 2", 10))) Summon(g, c, RandomMinionOfCost(g, V("RLK_221", "cost 2", 8)), s);
					else if (Generic.Manathirst(me)) Summon(g, c, RandomMinionOfCost(g, V("RLK_221", "cost", 3)), s);
				})
			}));
			// [RLK_222] Astalor Bloodsworn (2) — Battlecry: Add Astalor, the Protector to your hand. Manathirst (@): Deal 2 damage.
			cards.Add("RLK_222", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_IF_AVAILABLE_AND_MANATHIRST, 0 } }, new Power
			{
				PowerTask = ComplexTask.Create(
					new CustomTask((g, c, s, t, stack) => AddToHand(c, Cards.FromId("RLK_222t1"))),
					ManathirstOr(null, new DamageTask(V("RLK_222", "damage", 2), EntityType.TARGET)))
			}));
			// [RLK_222t1] Astalor, the Protector (5) — Battlecry: Add Astalor, the Flamebringer to your hand. Manathirst (@): Gain 5 Armor.
			cards.Add("RLK_222t1", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					new CustomTask((g, c, s, t, stack) => AddToHand(c, Cards.FromId("RLK_222t2"))),
					ManathirstOr(null, new ArmorTask(V("RLK_222t1", "armor", 5))))
			}));
			// [RLK_222t2] Astalor, the Flamebringer (8) — Battlecry: Deal 7 damage randomly split between all enemies. Manathirst (10): 14 instead.
			cards.Add("RLK_222t2", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int shots = Generic.Manathirst((IPlayable)s) ? V("RLK_222t2", "manathirst damage", 14) : V("RLK_222t2", "damage", 7);
					for (int i = 0; i < shots; i++)
					{
						var enemies = Enemies(c).Where(x => !x.IsDead).ToList();
						if (enemies.Count == 0) break;
						g.PowerHistory?.NewTargetRound();
						Generic.DamageCharFunc((IPlayable)s, enemies[g.Random.Next(enemies.Count)], 1, false);
					}
				})
			}));
			// [RLK_518] Silvermoon Sentinel (3) — Taunt. Manathirst (@): Gain +2/+2 and Divine Shield.
			cards.Add("RLK_518", new CardDef(new Power
			{
				PowerTask = ManathirstOr(null, ComplexTask.Create(new AddEnchantmentTask("RLK_518e", EntityType.SOURCE),
					new CustomTask((g, c, s, t, stack) => { if (s is Minion m) m.HasDivineShield = true; })))
			}));
			cards.Add("RLK_518e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("RLK_518", "stats", 2))) }));
			// [RLK_590] The Sunwell (9) — Fill your hand with random spells. Costs (1) less for each other card in your hand.
			cards.Add("RLK_590", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => p.Controller.HandZone.Count(x => x != p)),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					while (!c.HandZone.IsFull && RandomCard(g, x => x.Type == CardType.SPELL) is Card spell) AddToHand(c, spell);
				})
			}));
			// [RLK_591] Bonelord Frostwhisper (6) — Deathrattle: For the rest of the game, your first card each turn costs (0). You
			// die in 3 turns. (NamuWiki: one flame turns at the end of each of your turns, the hero dies when the third does; a
			// second Bonelord doesn't reset or extend it.)
			cards.Add("RLK_591", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c.AppliedEnchantments?.Any(e => e.Card.Id == "RLK_591e") == true) return;
					Generic.AddEnchantmentBlock(g, Cards.FromId("RLK_591e"), (IPlayable)s, c, V("RLK_591", "turns", 3), 0, 0);
				})
			}));
			cards.Add("RLK_591e", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HAND, "RLK_591e2") { Condition = new SelfCondition(p => p.Controller.NumCardsPlayedThisTurn == 0), Restless = true },
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Enchantment counter)) return;
						int left = counter[GameTag.TAG_SCRIPT_DATA_NUM_1] - 1;
						counter[GameTag.TAG_SCRIPT_DATA_NUM_1] = left;
						if (left <= 0) c.Hero.Destroy();
					})
				}
			}));
			cards.Add("RLK_591e2", new CardDef(new Power { Enchant = new Enchant(Effects.SetCost(0)) }));
			// [RLK_592] Invincible (8) — Reborn. Battlecry and Deathrattle: Give another random friendly Undead +5/+5 and Taunt.
			ISimpleTask invincible = new CustomTask((g, c, s, t, stack) =>
			{
				if (RandomFriendly(g, c, m => m.IsRace(Race.UNDEAD), s) is Minion undead)
				{
					Give(g, s, undead, "RLK_592e");
					undead.HasTaunt = true;
				}
			});
			cards.Add("RLK_592", new CardDef(new Power { PowerTask = invincible, DeathrattleTask = invincible }));
			cards.Add("RLK_592e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("RLK_592", "stats", 5))) }));
			// [RLK_593] Lor'themar Theron (7) — Battlecry: Double the stats of all minions in your deck.
			cards.Add("RLK_593", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable p in c.DeckZone.Where(x => Generic.IsMinionCard(x)).ToList())
						Generic.AddEnchantmentBlock(g, Cards.FromId("RLK_593e"), (IPlayable)s, p, p[GameTag.ATK], ((Minion)p).BaseHealth, 0);
				})
			}));
			cards.Add("RLK_593e", new CardDef(new Power { Enchant = Enchants.Enchants.AddAttackHealthScriptTag }));
			// [RLK_653] Infectious Ghoul (5) — Deathrattle: Give a random friendly minion "Deathrattle: Summon an Infectious Ghoul."
			cards.Add("RLK_653", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => { if (RandomFriendly(g, c, null, s) is Minion m) Give(g, s, m, "RLK_653e"); })
			}));
			cards.Add("RLK_653e", new CardDef(new Power { DeathrattleTask = new SummonTask("RLK_653", SummonSide.DEATHRATTLE) }));
			// [RLK_677] Sanctum Spellbender (4) — Whenever your opponent targets another minion with a spell, redirect it to this.
			cards.Add("RLK_677", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TARGET)
				{
					TriggerSource = TriggerSource.ENEMY,
					Condition = new SelfCondition(p => p is Spell),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						Spell spell = t as Spell ?? g.CurrentEventData?.EventSource as Spell;
						if (spell == null || !(s is Minion me) || me.IsDead || me.Zone?.Type != Zone.PLAY) return;
						if (g.IdEntityDic.TryGetValue(spell.CardTarget, out IPlayable aimed) && aimed is Minion && aimed != me)
							spell.CardTarget = me.Id;
					})
				}
			}));
			// [RLK_824] Arms Dealer (1) — After you summon an Undead, give it +1 Attack.
			cards.Add("RLK_824", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_SUMMON)
				{
					TriggerSource = TriggerSource.MINIONS_EXCEPT_SELF,
					Condition = new SelfCondition(p => IsUndead(p)),
					SingleTask = new AddEnchantmentTask("RLK_824e", EntityType.TARGET)
				}
			}));
			cards.Add("RLK_824e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("RLK_824", "attack", 1))) }));
			// [RLK_830] Flesh Behemoth (9) — Taunt. Deathrattle: Draw another Undead and summon a copy of it.
			cards.Add("RLK_830", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					var undead = c.DeckZone.Where(p => Generic.IsMinionCard(p) && IsUndead(p) && p.Card.Id != ((IPlayable)s).Card.Id).ToList();
					if (undead.Count == 0) return;
					IPlayable drawn = undead[g.Random.Next(undead.Count)];
					Generic.Draw(c, drawn);
					Summon(g, c, drawn.Card, s);
				})
			}));
			// [RLK_831] Plaguespreader (4) — Deathrattle: Transform a random minion in your opponent's hand into a Plaguespreader.
			cards.Add("RLK_831", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					var minions = c.Opponent.HandZone.Where(p => Generic.IsMinionCard(p)).ToList();
					if (minions.Count > 0) Generic.ChangeEntityBlock.Invoke(c.Opponent, minions[g.Random.Next(minions.Count)], Cards.FromId("RLK_831"), true);
				})
			}));
			// [RLK_833] Foul Egg (1) — Deathrattle: Summon a 3/3 Undead Chicken.
			cards.Add("RLK_833", new CardDef(new Power { DeathrattleTask = new SummonTask("RLK_833t", SummonSide.DEATHRATTLE) }));
			// [RLK_834] Nerubian Vizier (3) — Battlecry: Discover a spell. If a friendly Undead died after your last turn, it costs (2) less.
			cards.Add("RLK_834", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					DiscoverFromTask.ClassOrNeutral((c2, x) => x.Type == CardType.SPELL,
						Generic.UndeadDiedAfterYourLastTurn(c) ? "RLK_834e" : null).Process(g, c, s, null, stack))
			}));
			cards.Add("RLK_834e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("RLK_834", "less", 2))) }));
			// [RLK_867] Vrykul Necrolyte (2) — Battlecry: Give a friendly minion "Deathrattle: Summon a 2/2 Zombie with Rush."
			cards.Add("RLK_867", new CardDef(BattlecryFriendlyMinion, new Power { PowerTask = new AddEnchantmentTask("RLK_867e", EntityType.TARGET) }));
			cards.Add("RLK_867e", new CardDef(new Power { DeathrattleTask = new SummonTask("RLK_018t", SummonSide.DEATHRATTLE) }));
			// [RLK_900] Scourge Rager (3) — Reborn. Battlecry: Die.
			cards.Add("RLK_900", new CardDef(new Power { PowerTask = new DestroyTask(EntityType.SOURCE) }));
			// [RLK_914] Umbral Geist (2) — Deathrattle: Add a random Shadow spell to your hand.
			cards.Add("RLK_914", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (RandomCard(g, x => x.Type == CardType.SPELL && x[SPELL_SCHOOL] == SHADOW) is Card spell) AddToHand(c, spell);
				})
			}));
			// [RLK_915] Amber Whelp (3) — Battlecry: If you're holding a Dragon, deal 3 damage.
			cards.Add("RLK_915", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_IF_AVAILABLE_AND_DRAGON_IN_HAND, 0 } }, new Power
			{
				PowerTask = IfHoldingDragon(new DamageTask(V("RLK_915", "damage", 3), EntityType.TARGET))
			}));
			// [RLK_926] Bloodied Knight (3) — At the end of your turn, deal 2 damage to your hero.
			cards.Add("RLK_926", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = new DamageTask(V("RLK_926", "damage", 2), EntityType.HERO) }
			}));
			// [RLK_950] Translocation Instructor (5) — Battlecry: Choose an enemy minion. Swap it with a random one in their deck.
			cards.Add("RLK_950", new CardDef(BattlecryEnemyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion chosen) || chosen.Zone?.Type != Zone.PLAY) return;
					Controller owner = chosen.Controller;
					var deckMinions = owner.DeckZone.Where(p => Generic.IsMinionCard(p)).ToList();
					if (deckMinions.Count == 0) return;
					var other = (Minion)deckMinions[g.Random.Next(deckMinions.Count)];
					int pos = chosen.ZonePosition;
					owner.BoardZone.Remove(chosen);
					Generic.ShuffleIntoDeck.Invoke(owner, s, chosen);
					owner.DeckZone.Remove(other);
					Generic.SummonBlock.Invoke(g, other, pos, s);
				})
			}));
			// [RLK_951] Coroner (2) — Battlecry: Freeze an enemy minion. Manathirst (6): Silence it first.
			cards.Add("RLK_951", new CardDef(BattlecryEnemyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					if (Generic.Manathirst((IPlayable)s)) m.Silence();
					FreezeAll(new ICharacter[] { m });
				})
			}));
			// [RLK_952] Enchanter (3) — Enemy minions take double damage during your turn. (Generic.DamageCharFunc)
			cards.Add("RLK_952", new CardDef(new Power
			{
				Aura = new Aura(AuraType.CONTROLLER, new Effect(HsTag.ENEMY_MINIONS_DOUBLE_DAMAGE_ON_MY_TURN, EffectOperator.ADD, 1))
			}));
			// [RLK_955] Silvermoon Armorer (4) — Rush. Manathirst (@): Gain +2/+2.
			cards.Add("RLK_955", new CardDef(new Power { PowerTask = ManathirstOr(null, new AddEnchantmentTask("RLK_955e", EntityType.SOURCE)) }));
			cards.Add("RLK_955e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("RLK_955", "stats", 2))) }));
			// [RLK_957] Wailing Banshee (1) — Deathrattle: Give a random friendly Undead +2/+1.
			cards.Add("RLK_957", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => { if (RandomFriendly(g, c, m => m.IsRace(Race.UNDEAD), s) is Minion m2) Give(g, s, m2, "RLK_957e"); })
			}));
			cards.Add("RLK_957e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("RLK_957", "attack", 2)), Effects.Health_N(V("RLK_957", "health", 1))) }));
			// [RLK_970] Hawkstrider Rancher (3) — Whenever you play a minion, give it +1/+1 and "Deathrattle: Summon a 1/1 Hawkstrider."
			cards.Add("RLK_970", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.PLAY_MINION) { TriggerSource = TriggerSource.FRIENDLY, SingleTask = new AddEnchantmentTask("RLK_970e", EntityType.TARGET) }
			}));
			cards.Add("RLK_970e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.AttackHealth_N(V("RLK_970", "stats", 1))),
				DeathrattleTask = new SummonTask("RLK_970t", SummonSide.DEATHRATTLE)
			}));
		}

		/// <summary>The Four Horsemen's Deathrattle: all four died this game → destroy the enemy hero.</summary>
		private static ISimpleTask HorsemenWin(string[] horsemen) => new CustomTask((g, c, s, t, stack) =>
		{
			var died = new HashSet<string>(c.DiedThisGameCards.Select(p => p.Card.Id));
			if (s is IPlayable self) died.Add(self.Card.Id);
			if (horsemen.All(died.Contains)) c.Opponent.Hero.Destroy();
		});
	}
}
