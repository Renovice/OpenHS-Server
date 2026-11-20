// HsServer addition (not upstream SabberStone): cards of the random/Discover pool (games run as Wild) that upstream
// SabberStone left unscripted. They stay in every pool; this file gives them their effect (`HsServer cards --pool`
// lists what's left). Every number goes through CardValues (V).
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
		// ------------------------------------------------------------------ shared rules

		/// <summary>Redirects the attack in progress to <paramref name="newDefender"/> (Noble Sacrifice's mechanism).</summary>
		private static void Redirect(Game g, ICharacter newDefender)
		{
			g.ProposedDefender = newDefender.Id;
			// HsServer: a redirect is a new Proposed Attack Event, not an edit of the one already queued against (rule book l.521: the
			// triggers "queue based on the defender at that moment, and will remain in the queue even if the defender changes";
			// l.578 "Misdirection and Noble Sacrifice, regardless of order of play, both go off when your hero is attacked")
			if (g.CurrentEventData != null) g.CurrentEventData = new EventMetaData(g.CurrentEventData.EventSource, newDefender as IPlayable, g.CurrentEventData.EventNumber);
		}

		/// <summary>The characters an attacker could hit instead of its defender: any other enemy character, Taunt ignored — wiki
		/// Forgetful notes: "the new target will be randomly selected from among all other enemy characters with even chances,
		/// including both heroes and minions"; it "can allow the character to hit minions with Stealth, or even Immune targets"
		/// (HsServer WILD_WEST 2026-10-07: Stealth / Immune no longer left out). Never an untouchable (Dormant) one.</summary>
		private static List<ICharacter> OtherEnemies(ICharacter attacker, ICharacter defender)
		{
			Controller enemy = attacker.Controller.Opponent;
			var list = enemy.BoardZone.GetAll().Where(m => m.Id != defender.Id && !m.Untouchable && !m.IsDead && !m.ToBeDestroyed)
				.Cast<ICharacter>().ToList();
			if (enemy.Hero.Id != defender.Id) list.Add(enemy.Hero);
			return list;
		}

		/// <summary>"50% chance to attack the wrong enemy" when this character attacks. Live (live-20261007-vfx-decks 36194-36240,
		/// Ogre-Gang Outlaw): TRIGGER block TriggerKeyword=FORGETFUL ShowInHistory.</summary>
		private static Trigger Forgetful(string cardId) => new Trigger(TriggerType.ATTACK)
		{
			TriggerSource = TriggerSource.SELF,
			KeywordOverride = GameTag.FORGETFUL,
			ProposedAttack = true, // it can change the defender (rule book, Proposed Attack Event: Forgetful)
			SingleTask = new CustomTask((g, c, s, t, stack) => ForgetfulAttack(g, s as ICharacter, V(cardId, "chance %", 50)))
		};

		private static void ForgetfulAttack(Game g, ICharacter attacker, int percent)
		{
			if (attacker == null || !(g.CurrentEventData?.EventTarget is ICharacter defender)) return;
			var others = OtherEnemies(attacker, defender);
			if (others.Count == 0 || g.Random.Next(100) >= percent) return;
			Redirect(g, others[g.Random.Next(others.Count)]);
		}

		/// <summary>
		/// "Also damages the minions next to whomever this attacks" (Foe Reaper 4000, Magnataur Alpha, Lake Thresher, Gnomelia…);
		/// <paramref name="attacker"/> HERO: "… whomever your hero attacks" on a weapon or a hero enchantment (Flamereaper, Reaper's
		/// Scythe, Ignis' Flame of Odyn). Rule book (Advanced_rulebook.txt:560-569): the After Attack Event — "Foe Reaper
		/// 4000/Magnataur Alpha has a condition of 'attacker is this minion'"; "An After Attack Event is resolved as long as the
		/// attack was successful" (a cancelled attack cleaves nothing); it "is resolved in the same Phase … as the combat damage,
		/// meaning death processing is not done in-between" (the defender's neighbours are still beside it). The attacker's
		/// Attack now, from the attacker.
		/// </summary>
		private static Trigger Cleave(TriggerSource attacker = TriggerSource.SELF) => new Trigger(TriggerType.AFTER_ATTACK)
		{
			TriggerSource = attacker,
			SingleTask = new CustomTask((g, c, s, t, stack) =>
			{
				ICharacter me = attacker == TriggerSource.HERO ? c.Hero : s as ICharacter;
				if (!(g.CurrentEventData?.EventTarget is Minion defender) || me == null) return;
				var adjacent = defender.GetAdjacentMinions().ToList();
				if (adjacent.Count == 0) return;
				// the card's own burst at the minions it hits, when it has one (Gnomelia: TOYFX_..._Trigger_AdjacentBurst)
				Generic.NamedFx((IPlayable)s, "Adjacent", adjacent.Select(m => m.Id), () =>
				{
					foreach (Minion next in adjacent)
						if (!next.IsDead) next.TakeDamage((IPlayable)me, me.AttackDamage);
				});
			})
		};

		/// <summary>A Secret put straight into play: the shared Generic.PutSecretIntoPlay.</summary>
		private static bool PutSecretIntoPlay(Controller c, Spell secret) => Generic.PutSecretIntoPlay(c, secret);

		private static void Give(Game g, IEntity source, IPlayable target, string enchantment, int num1 = 0) =>
			Generic.AddEnchantmentBlock(g, Cards.FromId(enchantment), (IPlayable)source, target, num1, 0, 0);

		private static void AddToHand(Controller c, Card card)
		{
			if (!c.HandZone.IsFull) Generic.AddHandPhase.Invoke(c, Entity.FromCard(c, card));
		}

		private static void WildPool(IDictionary<string, CardDef> cards)
		{

			// ---------------------------------------------------------------- Rastakhan's Rumble

			// [TRL_343] Wardruid Loti (3) — Choose One - Transform into one of Loti's four dinosaur forms. Its options are
			// the client's related cards (Cards.ChooseOneOptions); each turns Loti into its form (<x>t1 -> <x>t2), like
			// Druid of the Claw. Its own task is the "choose both" version (Fandral): the form with all four keywords.
			cards.Add("TRL_343", new CardDef(new Power { PowerTask = new TransformTask("TRL_343et1", EntityType.SOURCE) }));
			foreach (string option in new[] { "TRL_343at1", "TRL_343bt1", "TRL_343ct1", "TRL_343dt1" })
				cards.Add(option, new CardDef(new Power { PowerTask = new TransformTask(option[..^1] + "2", EntityType.SOURCE) }));

			// ---------------------------------------------------------------- Fractured in Alterac Valley

			// [AV_258] Bru'kan of the Elements (8, Shaman hero) — Battlecry: Call upon the power of two Elements! Two picks of
			// the four Invocations (the second offers the three left, like Siamat); each is cast as it's picked. Its Hero
			// Power (AV_258pt7, "a different Element every turn") is an Invocation Hero Power from the moment he's played,
			// and swaps to a random different one at the end of each of your turns (live session-20260930-071519 23841…: the
			// Hero Power's TRIGGER block at MAIN_END, 14 swaps, each to one of the other three — livecheck 2026-10-10; was at the start
			// of your turns).
			string[] invocations = { "AV_258t", "AV_258t2", "AV_258t3", "AV_258t4" };
			string[] invocationPowers = { "AV_258pt", "AV_258p2", "AV_258pt3", "AV_258pt4" };
			void SwapInvocation(Game g, Controller c)
			{
				var others = invocationPowers.Where(id => id != c.Hero.HeroPower?.Card.Id).ToList();
				new ReplaceHeroPower(Cards.FromId(others[g.Random.Next(others.Count)])).Process(g, c, c.Hero, null);
			}
			cards.Add("AV_258", new CardDef(new Power
			{
				// (2026-10-08: his Hero Power arrives after the Battlecry — rulebook :403 — so the first swap runs then)
				AfterHeroPowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c.Hero.HeroPower?.Card.Id is "AV_258pt7" or "AV_258p")
						SwapInvocation(g, c);
				}),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var self = new List<IPlayable> { (IPlayable)s };
					var second = new CustomTask((g2, c2, s2, chosen, stack2) =>
						Generic.CreateChoiceCards.Invoke(c2, s2, stack2?.Playables ?? self, ChoiceType.GENERAL, ChoiceAction.ADAPT,
							invocations.Where(id => id != chosen.Card.Id).Select(Cards.FromId).ToArray(), null));
					Generic.CreateChoiceCards.Invoke(c, s, self, ChoiceType.GENERAL, ChoiceAction.ADAPT,
						invocations.Select(Cards.FromId).ToArray(), second);
				})
			}));
			// the Invocations, as Battlecry picks (spells) and as Hero Powers (which swap each turn)
			ISimpleTask Earth(string id) => new SummonTask("AV_258t6", V(id, "elementals", 2), SummonSide.SPELL);
			ISimpleTask Water(string id) => new HealTask(V(id, "heal", 6), EntityType.FRIENDS);
			ISimpleTask Fire(string id) => new DamageTask(V(id, "damage", 6), EntityType.OP_HERO);
			ISimpleTask Lightning(string id) => new DamageTask(V(id, "damage", 2), EntityType.OP_MINIONS);
			cards.Add("AV_258t", new CardDef(new Power { PowerTask = Earth("AV_258t") }));
			cards.Add("AV_258t2", new CardDef(new Power { PowerTask = Water("AV_258t2") }));
			cards.Add("AV_258t3", new CardDef(new Power { PowerTask = Fire("AV_258t3") }));
			cards.Add("AV_258t4", new CardDef(new Power { PowerTask = Lightning("AV_258t4") }));
			Trigger Swaps() => new Trigger(TriggerType.TURN_END) { SingleTask = new CustomTask((g, c, s, t, stack) => SwapInvocation(g, c)) };
			cards.Add("AV_258pt", new CardDef(new Power { PowerTask = Earth("AV_258pt"), Trigger = Swaps() }));
			cards.Add("AV_258p2", new CardDef(new Power { PowerTask = Water("AV_258p2"), Trigger = Swaps() }));
			cards.Add("AV_258pt3", new CardDef(new Power { PowerTask = Fire("AV_258pt3"), Trigger = Swaps() }));
			cards.Add("AV_258pt4", new CardDef(new Power { PowerTask = Lightning("AV_258pt4"), Trigger = Swaps() }));
			cards.Add("AV_258pt7", new CardDef(new Power { Trigger = Swaps() }));
			cards.Add("AV_258p", new CardDef(new Power { Trigger = Swaps() }));

			// ---------------------------------------------------------------- United in Stormwind

			// (the Questlines: QuestlinesCardsGenHs.cs)
			// [SW_031t7] Stormcaller Bru'kan (5) — Battlecry: For the rest of the game, your spells cast twice. Live
			// (captures/live-20261001-questline): an enchantment SW_031t7e on the player; Generic.CastSpell repeats the effect.
			cards.Add("SW_031t7", new CardDef(new Power { PowerTask = new AddEnchantmentTask("SW_031t7e", EntityType.CONTROLLER) }));
			cards.Add("SW_031t7e", new CardDef(new Power { Enchant = new Enchant(new Effect(HsTag.SPELLS_CAST_TWICE, EffectOperator.SET, 1)) }));

			// ---------------------------------------------------------------- Galakrond's Awakening

			// [YOD_001] Rising Winds (2) — Twinspell. Choose One - Draw a card; or Summon a 3/2 Eagle. Its options are
			// YOD_001b / YOD_001c (Cards.ChooseOneOptions); the Twinspell copy YOD_001ts uses the same ones.
			cards.Add("YOD_001b", new CardDef(new Power
			{
				PowerTask = new DrawTask(V("YOD_001b", "cards", 1))
			}));
			cards.Add("YOD_001c", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new SummonTask("YOD_001t", SummonSide.SPELL)
			}));
			// Twinspell cards: the spell and its copy (<id>ts, the client's on-play replacement) share one script.
			void Twinspell(string id, CardDef def) { cards.Add(id, def); cards.Add(id + "ts", def); }

			// [YOD_004] Chopshop Copter (3) — After a friendly Mech dies, add a random Mech to your hand.
			cards.Add("YOD_004", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DEATH)
				{
					TriggerSource = TriggerSource.MINIONS_EXCEPT_SELF,
					Condition = SelfCondition.IsRace(Race.MECHANICAL),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (RandomCard(g, x => x.Type == CardType.MINION && x.IsRace(Race.MECHANICAL)) is Card mech) AddToHand(c, mech);
					})
				}
			}));
			// [YOD_005] Fresh Scent (2) — Twinspell. Give a Beast +2/+2.
			Twinspell("YOD_005", new CardDef(new Dictionary<PlayReq, int>
				{ { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_TARGET_WITH_RACE, (int)Race.BEAST } },
				new Power { PowerTask = new AddEnchantmentTask("YOD_005e", EntityType.TARGET) }));
			cards.Add("YOD_005e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("YOD_005", "stats", 2))) }));
			// [YOD_007] Animated Avalanche (7) — Battlecry: If you played an Elemental last turn, summon a copy of this.
			cards.Add("YOD_007", new CardDef(new Power
			{
				PowerTask = ComplexTask.Conditional(SelfCondition.ElementalPlayedLastTurn, new SummonCopyTask(EntityType.SOURCE)) // the shared glow condition
			}));
			// [YOD_008] Arcane Amplifier (3) — Your Hero Power deals 2 extra damage.
			cards.Add("YOD_008", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HERO, new Effect(GameTag.HEROPOWER_DAMAGE, EffectOperator.ADD, V("YOD_008", "extra damage", 2)))
			}));
			// [YOD_012] Air Raid (2) — Twinspell. Summon two 1/1 Silver Hand Recruits with Taunt.
			Twinspell("YOD_012", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("YOD_012", "recruits", 2) && !c.BoardZone.IsFull; i++)
					{
						var recruit = (Minion)Entity.FromCard(c, Cards.FromId("CS2_101t"));
						Generic.SummonBlock.Invoke(g, recruit, -1, s);
						recruit[GameTag.TAUNT] = 1;
					}
				})
			}));
			// [YOD_013] Cleric of Scales (1) — Battlecry: If you're holding a Dragon, Discover a spell from your deck.
			cards.Add("YOD_013", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => HoldingDragon(c, s), new CustomTask((g, c, s, t, stack) =>
					Generic.DiscoverFromDeck(c, s, Generic.PickFromDeck(c, p => p is Spell))))
			}));
			// [YOD_014] Aeon Reaver (6) — Battlecry: Deal damage to a minion equal to its Attack.
			cards.Add("YOD_014", new CardDef(BattlecryMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is Minion m) Generic.DamageCharFunc((IPlayable)s, m, m.AttackDamage, false);
				})
			}));
			// [YOD_015] Dark Prophecy (3) — Discover a 2-Cost minion. Summon it and give it +3 Health.
			cards.Add("YOD_015", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.MINION && x.Cost == V("YOD_015", "cost", 2),
					"YOD_015e", action: ChoiceAction.SUMMON)
			}));
			cards.Add("YOD_015e", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(V("YOD_015", "health", 3))) }));
			// [YOD_016] Skyvateer (2) — Stealth. Deathrattle: Draw a card.
			cards.Add("YOD_016", new CardDef(new Power { DeathrattleTask = new DrawTask() }));
			// [YOD_017] Shadow Sculptor (5) — Combo: Draw a card for each card you've played this turn. (itself not counted:
			// a card isn't played until its effect is done, Generic.CardsPlayedBefore)
			cards.Add("YOD_017", new CardDef(new Power
			{
				ComboTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < Generic.CardsPlayedBefore(c, s); i++) Generic.Draw(c);
				})
			}));
			// [YOD_018] Waxmancy (2) — Discover a Battlecry minion. Reduce its Cost by (2).
			cards.Add("YOD_018", new CardDef(new Power
			{
				PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.MINION && x[GameTag.BATTLECRY] > 0, "YOD_018e")
			}));
			cards.Add("YOD_018e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("YOD_018", "discount", 2))) }));
			// [YOD_020] Explosive Evolution (2) — Transform a minion into a random one that costs (3) more.
			cards.Add("YOD_020", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is Minion m && CostShifted(g, m, V("YOD_020", "cost", 3)) is Card card) Generic.TransformBlock.Invoke(m.Controller, card, m);
				})
			}));
			// [YOD_022] Risky Skipper (1) — After you play a minion, deal 1 damage to all minions.
			var riskySkipper = new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_MINION)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new DamageTask(V("YOD_022", "damage", 1), EntityType.ALLMINIONS)
				}
			});
			cards.Add("YOD_022", riskySkipper);
			cards.Add("YOD_022_COPY", riskySkipper);
			// [YOD_023] Boom Squad (1) — Discover a Lackey, Mech, or Dragon: one option of each.
			cards.Add("YOD_023", new CardDef(new Power
			{
				PowerTask = new DiscoverFromTask((g, c, s) =>
				{
					bool Discoverable(Card x) => x.Type == CardType.MINION && (x.HasClass(c.HeroClass) || x.HasClass(CardClass.NEUTRAL));
					Card lackey = AddLackeyTask.Lackeys.Where(x => x.Type == CardType.MINION).ToList() is { Count: > 0 } lackeys
						? lackeys[g.Random.Next(lackeys.Count)] : null;
					return new[]
					{
						lackey,
						RandomCard(g, x => Discoverable(x) && x.IsRace(Race.MECHANICAL)),
						RandomCard(g, x => Discoverable(x) && x.IsRace(Race.DRAGON))
					}.Where(x => x != null);
				})
			}));
			// [YOD_024] Bomb Wrangler (3) — Whenever this minion takes damage, summon a 1/1 Boom Bot. (Imp Gang Boss's way)
			cards.Add("YOD_024", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TAKE_DAMAGE)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = new SummonTask("GVG_110t", SummonSide.RIGHT)
				}
			}));
			// [YOD_025] Twisted Knowledge (2) — Discover 2 Warlock cards (two Discovers).
			cards.Add("YOD_025", new CardDef(new Power
			{
				PowerTask = ComplexTask.Repeat(DiscoverFromTask.Where((c, x) => x.HasClass(CardClass.WARLOCK)), V("YOD_025", "cards", 2))
			}));
			// [YOD_027] Chaos Gazer (3) — Battlecry: Curse a playable card in your opponent's hand. They have 1 turn to play it!
			// wiki: "a random card in your opponent's hand that they will have enough mana to play next turn"; "The effect does
			// not check if the card fulfills any requirements to be played".
			cards.Add("YOD_027", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Controller op = c.Opponent;
					int mana = System.Math.Min(op.BaseMana + 1, 10) - op.OverloadOwed;
					var playable = op.HandZone.Where(p => p.Cost <= mana).ToList();
					if (playable.Count > 0) Give(g, s, playable[g.Random.Next(playable.Count)], "YOD_027e");
				})
			}));
			// [YOD_027e] Withering Away — Discard this at the end of your turn (its owner's turn: the Gazer's opponent).
			cards.Add("YOD_027e", new CardDef(new Power
			{
				Enchant = new Enchant { RemoveWhenPlayed = true },
				// the curse is the Gazer's player's enchantment: it fires at the other player's (the card owner's) turn end
				Trigger = new Trigger(TriggerType.TURN_END) { EitherTurn = true, TriggerSource = TriggerSource.ENEMY, SingleTask = new DiscardTask(EntityType.TARGET) }
			}));
			// [YOD_028] Skydiving Instructor (3) — Battlecry: Summon a 1-Cost minion from your deck.
			cards.Add("YOD_028", new CardDef(new Power { PowerTask = new RecruitTask(1, SelfCondition.IsCost(V("YOD_028", "cost", 1))) }));
			// [YOD_029] Hailbringer (5) — Battlecry: Summon two 1/1 Ice Shards that Freeze. (their Freeze: the card's FREEZE tag)
			cards.Add("YOD_029", new CardDef(new Power { PowerTask = new SummonTask("YOD_029t", V("YOD_029", "shards", 2)) }));
			// [YOD_030] Licensed Adventurer (2) — Battlecry: If you control a Quest, add a Coin to your hand.
			cards.Add("YOD_030", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c.SecretZone.Quest != null, new AddCardTo("GAME_005", EntityType.HAND))
			}));
			// [YOD_032] Frenzied Felwing (4) — Costs (1) less for each damage dealt to your opponent this turn.
			cards.Add("YOD_032", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => V("YOD_032", "discount per damage", 1) * p.Controller.Opponent.Hero.DamageTakenThisTurn)
			}));
			// [YOD_033] Boompistol Bully (5) — Battlecry: Enemy Battlecry cards cost (5) more next turn. (Loatheb's way)
			cards.Add("YOD_033", new CardDef(new Power { PowerTask = new AddEnchantmentTask("YOD_033e", EntityType.OP_CONTROLLER) }));
			cards.Add("YOD_033e", new CardDef(new Power
			{
				Aura = new Aura(AuraType.OP_HAND, Effects.AddCost(V("YOD_033", "cost", 5)))
				{
					Condition = new SelfCondition(p => p.Card[GameTag.BATTLECRY] > 0),
					RemoveTrigger = (TriggerType.TURN_END, SelfCondition.IsOpTurn),
				}
			}));
			// [YOD_035] Grand Lackey Erkh (4) — After you play a Lackey, add a Lackey to your hand.
			cards.Add("YOD_035", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_MINION)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p.Card[GameTag.MARK_OF_EVIL] > 0),
					SingleTask = new AddLackeyTask(V("YOD_035", "lackeys", 1))
				}
			}));
			// [YOD_036] Rotnest Drake (5) — Battlecry: If you're holding a Dragon, destroy a random enemy minion.
			cards.Add("YOD_036", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => HoldingDragon(c, s),
					ComplexTask.Create(new RandomTask(1, EntityType.OP_MINIONS, harmful: true), new DestroyTask(EntityType.STACK)))
			}));
			// [YOD_038] Sky Gen'ral Kragg (4) — Taunt. Battlecry: If you've played a Quest this game, summon a 4/2 Parrot with Rush.
			cards.Add("YOD_038", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c.PlayHistory.Any(e => e.SourceController == c.PlayerId && e.SourceCard.IsQuest),
					new SummonTask("YOD_038t"))
			}));
			// [YOD_040] Steel Beetle (2) — Battlecry: If you're holding a spell that costs (5) or more, gain 5 Armor.
			cards.Add("YOD_040", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c.HandZone.Any(p => p is Spell && p.Cost >= V("YOD_040", "spell cost", 5)),
					new ArmorTask(V("YOD_040", "armor", 5)))
			}));
			// [YOD_041] Eye of the Storm (10) — Summon three 5/6 Elementals with Taunt. Overload: (3) (the card's tag)
			cards.Add("YOD_041", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new SummonTask("YOD_041t", V("YOD_041", "elementals", 3), SummonSide.SPELL)
			}));
			// [YOD_042] The Fist of Ra-den (4) — After you cast a spell, summon a Legendary minion of that Cost. Lose 1 Durability.
			// (wiki: "see Medivh, the Guardian": Atiesh's way; user ruling: no Legendary of that Cost, no trigger at all)
			cards.Add("YOD_042", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = SelfCondition.MinionOfItsCostExists(x => x.Rarity == Rarity.LEGENDARY),
					SingleTask = ComplexTask.Create(
						new GetGameTagTask(GameTag.TAG_LAST_KNOWN_COST_IN_HAND, EntityType.TARGET),
						new RandomMinionNumberTask(GameTag.COST) { Filter = x => x.Rarity == Rarity.LEGENDARY },
						new SummonTask(),
						new DamageWeaponTask(false))
				}
			}));
			// [YOD_043] Scalelord (5) — Battlecry: Give your Murlocs Divine Shield.
			cards.Add("YOD_043", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new IncludeTask(EntityType.MINIONS_NOSOURCE), new FilterStackTask(SelfCondition.IsRace(Race.MURLOC)),
					new SetGameTagTask(GameTag.DIVINE_SHIELD, 1, EntityType.STACK))
			}));

			// ---------------------------------------------------------------- Goblins vs Gnomes

			// [GVG_021] Mal'Ganis (9) — Your other Demons have +2/+2. Your hero is Immune.
			var malganis = new CardDef(new Power
			{
				Aura = new MultiAura(
					new Aura(AuraType.BOARD_EXCEPT_SOURCE, "GVG_021e") { Condition = SelfCondition.IsRace(Race.DEMON) },
					new Aura(AuraType.HERO, new Effect(GameTag.IMMUNE, EffectOperator.SET, 1)))
			});
			cards.Add("GVG_021", malganis);
			cards.Add("CORE_GVG_021", malganis);

			// [GVG_014] Vol'jin (5) — Battlecry: Swap Health with another minion.
			cards.Add("GVG_014", new CardDef(BattlecryMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Minion me) || !(t is Minion other) || other.Id == me.Id) return;
					int mine = me.Health, theirs = other.Health;
					me.Damage = 0; me.BaseHealth = theirs;
					other.Damage = 0; other.BaseHealth = mine;
				})
			}));

			// [TTN_076] Mechagnome Guide (5) — Battlecry: Discover a spell. Forge: It costs (3) less. (Forging: Generic.Forge,
			// the client's FORGES_INTO = TTN_076t.)
			bool Spell(Controller c, Card x) => x.Type == CardType.SPELL;
			cards.Add("TTN_076", new CardDef(new Power { PowerTask = DiscoverFromTask.ClassOrNeutral(Spell) }));
			// [TTN_076t] Mechagnome Guide (Forged) — Battlecry: Discover a spell. It costs (3) less.
			cards.Add("TTN_076t", new CardDef(new Power { PowerTask = DiscoverFromTask.ClassOrNeutral(Spell, "TTN_076e") }));
			cards.Add("TTN_076e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("TTN_076t", "discount", 3))) }));

			// Drink spells (Perils in Paradise): each drink of a chain does the same; the next drink is the client's on-play
			// replacement (ReplacementsWhenPlayed → Generic.OnPlayReplacement), not part of the script.
			void Drink(CardDef def, params string[] chain) { def.Power.Drink = true; foreach (string id in chain) cards.Add(id, def); }
			// [VAC_520] Seabreeze Chalice (1) — Deal 2 damage randomly split among all enemy minions.
			Drink(new CardDef(new Power { PowerTask = new EnqueueTask(V("VAC_520", "damage", 2), ComplexTask.DamageRandomTargets(1, EntityType.OP_MINIONS, 1), true) }),
				"VAC_520", "VAC_520t", "VAC_520t2");
			// [VAC_916] Divine Brew (1) — Give a character Divine Shield.
			Drink(new CardDef(TargetAny, new Power { PowerTask = new SetGameTagTask(GameTag.DIVINE_SHIELD, 1, EntityType.TARGET) }),
				"VAC_916", "VAC_916t2", "VAC_916t3");
			// [VAC_404] Nightshade Tea (1) — Deal 2 damage to a minion. Deal 2 damage to your hero.
			Drink(new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("VAC_404", "damage", 2), EntityType.TARGET, true),
					new DamageTask(V("VAC_404", "self", 2), EntityType.HERO, true))
			}), "VAC_404", "VAC_404t1", "VAC_404t2");
			// [VAC_323] Malted Magma (1) — Deal 1 damage to all enemies.
			Drink(new CardDef(new Power { PowerTask = new DamageTask(V("VAC_323", "damage", 1), EntityType.ENEMIES, true) }),
				"VAC_323", "VAC_323t", "VAC_323t2");
			// [VAC_338] Cup o' Muscle (1) — Give a minion in your hand +2/+1. (live: a random one, no choice; no hand-target tag)
			Drink(new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var minions = c.HandZone.Where(p => Generic.IsMinionCard(p)).ToList();
					if (minions.Count > 0) Give(g, s, minions[g.Random.Next(minions.Count)], "VAC_338e");
				})
			}), "VAC_338", "VAC_338t", "VAC_338t2");
			cards.Add("VAC_338e", new CardDef(new Power { Enchant = Enchants.Enchants.GetAutoEnchantFromText("VAC_338e") }));
			// [VAC_951] "Health" Drink (1) — Lifesteal. Deal 3 damage to a minion. (Lifesteal: the card's tag)
			Drink(new CardDef(TargetMinion, new Power { PowerTask = new DamageTask(V("VAC_951", "damage", 3), EntityType.TARGET, true) }),
				"VAC_951", "VAC_951t", "VAC_951t2");

			// [WW_334] Reinforced Plating (2) — Gain 6 Armor. Excavate a treasure. (live order: the Armor, then the Excavate)
			cards.Add("WW_334", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new ArmorTask(V("WW_334", "armor", 6)), new ExcavateTask())
			}));

			// [GVG_016] Fel Reaver (5) — Whenever your opponent plays a card, remove the top 3 cards of your deck.
			cards.Add("GVG_016", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.PLAY_CARD)
				{
					TriggerSource = TriggerSource.ENEMY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						int n = V("GVG_016", "cards", 3);
						for (int i = 0; i < n && c.DeckZone.Count > 0; i++)
							Generic.DeckCardToGraveyard(c.DeckZone.TopCard, s?.Id ?? 0, n > 1);   // rulebook: a deck card goes to the graveyard
					})
				}
			}));

			// [GVG_017] Call Pet (2) — Draw a card. If it's a Beast, it costs (4) less.
			cards.Add("GVG_017", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					IPlayable drawn = Generic.Draw(c);
					if (drawn is Minion m && m.IsRace(Race.BEAST)) // no enchantment in the client DB: the cost changes directly
						drawn.Cost = System.Math.Max(0, drawn.Cost - V("GVG_017", "discount", 4));
				})
			}));

			// [GVG_025] One-eyed Cheat (2) — Whenever you summon a Pirate, gain Stealth.
			cards.Add("GVG_025", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.SUMMON)
				{
					TriggerSource = TriggerSource.MINIONS_EXCEPT_SELF,
					Condition = SelfCondition.IsRace(Race.PIRATE),
					SingleTask = new SetGameTagTask(GameTag.STEALTH, 1, EntityType.SOURCE)
				}
			}));

			// [GVG_046] King of Beasts (3) — Taunt. Has +1 Attack for each other Beast you control. (Patch 27.2.0; the aura is
			// WondersC2KingOfBeasts — live-20261007-zilliax-etc-remix 106367: a plain ATK change, no enchantment.)
			cards.Add("GVG_046", new CardDef(WondersC2KingOfBeasts));
			cards.Add("GVG_046e", new CardDef(new Power { Enchant = new Enchant(GameTag.ATK, EffectOperator.ADD) }));

			// [GVG_049] Gahz'rilla (7) — Whenever this minion takes damage, double its Attack.
			cards.Add("GVG_049", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TAKE_DAMAGE)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is Minion me && me.AttackDamage > 0) Give(g, s, me, "GVG_049e", me.AttackDamage);
					})
				}
			}));
			cards.Add("GVG_049e", new CardDef(new Power { Enchant = new Enchant(GameTag.ATK, EffectOperator.ADD) }));

			// [GVG_050] Bouncing Blade (3) — Deal 1 damage to a random minion. Repeat until a minion dies.
			cards.Add("GVG_050", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_MINIMUM_TOTAL_MINIONS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int bounce = 0; bounce < 80; bounce++) // every target Immune or Divine Shield-ed forever is impossible in practice
					{
						var targets = c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll())
							.Where(m => !m.IsDead && !m.ToBeDestroyed && !m.IsImmune).ToList();
						if (targets.Count == 0) break;
						Minion hit = targets[g.Random.Next(targets.Count)];
						Generic.DamageCharFunc((IPlayable)s, hit, V("GVG_050", "damage", 1), true);
						if (hit.IsDead || hit.ToBeDestroyed) break;
					}
				})
			}));

			// [GVG_054] Ogre Warmaul, [GVG_065] Ogre Brute, [GVG_066] Dunemaul Shaman, [GVG_088] Ogre Ninja,
			// [AT_088] Mogor's Champion — 50% chance to attack the wrong enemy.
			foreach (string ogre in new[] { "GVG_054", "GVG_065", "GVG_066", "GVG_088", "AT_088" })
				cards.Add(ogre, new CardDef(new Power { Trigger = Forgetful(ogre) }));

			// [GVG_112] Mogor the Ogre (6) — All minions have a 50% chance to attack the wrong enemy.
			cards.Add("GVG_112", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ATTACK)
				{
					TriggerSource = TriggerSource.ALL_MINIONS,
					ProposedAttack = true, // rule book: Mogor "has a queuing condition of 'attacker is a minion'" (Proposed Attack Event)
					SingleTask = new CustomTask((g, c, s, t, stack) =>
						ForgetfulAttack(g, g.CurrentEventData?.EventSource as ICharacter, V("GVG_112", "chance %", 50)))
				}
			}));

			// [OG_254] Eater of Secrets (4) — Battlecry: Destroy all enemy Secrets. Gain +1/+1 for each.
			// Live (live-20261006-secrets-generators, its POWER block): Secret by Secret — its TARGET and its reveal into the
			// graveyard (Generic.DestroySecret), then the +1/+1: the first makes the OG_254e enchantment, each next one grows it.
			cards.Add("OG_254", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int per = V("OG_254", "stats", 1);
					Enchantment gained = null;
					foreach (Spell secret in c.Opponent.SecretZone.OfType<Spell>().Where(sp => sp.IsSecret).ToList())
					{
						Generic.DestroySecret(secret, reveal: false, s);
						if (!(s is Minion eater) || eater.Zone?.Type != Zone.PLAY) continue;
						if (gained != null)
							Generic.GrowEnchantment(gained, per, per);
						else
						{
							Generic.AddEnchantmentBlock(g, Cards.FromId("OG_254e"), eater, eater, per, per);
							gained = eater.AppliedEnchantments?.LastOrDefault(e => e.Card.Id == "OG_254e" && e.Creator == eater);
						}
					}
				})
			}));

			// [OG_123] Shifter Zerus (1) — Each turn this is in your hand, transform it into a random minion. (WONDERS: the shared in-hand
			// shift, Generic.ShiftInHand — Advanced_rulebook.txt:1554-1559; once a turn, TRANSFORMED_FROM_CARD = Zerus (OG_123e's
			// DEFINING_ENCHANTMENT); live NIGHT 29402: the Shifting card shows SHIFTING, not SHIFTING_MINION. Was upstream's
			// ChangeEntityTask.)
			ISimpleTask zerus = new CustomTask((g, c, s, t, stack) =>
				Generic.ShiftInHand(s, me => RandomCard(g, x => x.Type == CardType.MINION), Cards.FromId("OG_123e")));
			cards.Add("OG_123", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START) { TriggerActivation = TriggerActivation.HAND, SingleTask = zerus }
			}));
			cards.Add("OG_123e", new CardDef(new Power
			{
				Enchant = new Enchant(GameTag.SHIFTING, EffectOperator.SET, 1) { RemoveWhenPlayed = true },
				Trigger = new Trigger(TriggerType.TURN_START) { SingleTask = zerus }
			}));

			// [GVG_043e] Glaivezooka (+1 Attack). The client's enchantment text still says "+2 Attack" (patch 27.2.0's accidental buff,
			// reverted in 27.2.2: wiki Glaivezooka "Now reads: '... +1 Attack.' (previously: '... +2 Attack.')"), so not from its text.
			cards.Add("GVG_043e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("GVG_043", "attack", 1))) }));

			// [GVG_074] Kezan Mystic (4) — Battlecry: Take control of a random enemy Secret.
			cards.Add("GVG_074", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					// Advanced rulebook Z3: steal a random Secret that can be stolen (the same entity changes owner); if none
					// can (zone full, or a copy of each is already up) a random one is destroyed and revealed instead
					var secrets = c.Opponent.SecretZone.Where(p => p is Spell sp && sp.IsSecret).Cast<Spell>().ToList();
					if (secrets.Count == 0) return;
					var stealable = secrets.Where(sp => Generic.CanHoldSecret(c, sp.Card)).ToList();
					if (stealable.Count == 0)
					{
						Generic.DestroySecret(secrets[g.Random.Next(secrets.Count)], reveal: true, s);
						return;
					}
					Spell taken = stealable[g.Random.Next(stealable.Count)];
					taken.ActivatedTrigger?.Remove();
					Generic.PutSecretIntoPlay(c, (Spell)Generic.GiveTo(taken, c));
				})
			}));

			// [GVG_077] Anima Golem (6) — At the end of each turn, destroy this minion if it's your only one.
			cards.Add("GVG_077", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					EitherTurn = true,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is Minion me && c.BoardZone.GetAll().All(m => m.Id == me.Id)) me.Destroy();
					})
				}
			}));

			// [GVG_092] Gnomish Experimenter (3) — Battlecry: Draw a card. If it's a minion, transform it into a Chicken.
			cards.Add("GVG_092", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (Generic.Draw(c) is Minion drawn && drawn.Zone?.Type == SabberStoneCore.Enums.Zone.HAND)
						Generic.ChangeEntityBlock(c, drawn, Cards.FromId("GVG_092t"), true);
				})
			}));

			// [GVG_097] Lil' Exorcist (3) — Taunt. Battlecry: Gain +1/+1 for each enemy Deathrattle minion.
			cards.Add("GVG_097", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = c.Opponent.BoardZone.GetAll().Count(m => m.HasDeathrattle);
					if (n == 0 || !(s is Minion me)) return; // no enchantment in the client DB: stats change directly
					int per = V("GVG_097", "stats per minion", 1);
					me.AttackDamage += n * per;
					me.BaseHealth += n * per;
				})
			}));

			// [GVG_107] Enhance-o Mechano (4) — Battlecry: Give your other minions Windfury, Taunt, or Divine Shield (at random).
			cards.Add("GVG_107", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					GameTag[] gifts = { GameTag.WINDFURY, GameTag.TAUNT, GameTag.DIVINE_SHIELD };
					foreach (Minion m in c.BoardZone.GetAll().Where(m => m.Id != s.Id))
						m[gifts[g.Random.Next(gifts.Length)]] = 1;
				})
			}));

			// [GVG_108] Recombobulator (2) — Battlecry: Transform a friendly minion into a random minion with the same Cost.
			cards.Add("GVG_108", new CardDef(BattlecryFriendlyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is Minion m && RandomMinionOfCost(g, m.Cost) is Card card)
						Generic.TransformBlock.Invoke(c, card, m);
				})
			}));

			// [GVG_111] Mimiron's Head (5) — At the start of your turn, if you have at least 3 Mechs, destroy them all and form
			// V-07-TR-0N.
			cards.Add("GVG_111", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						var mechs = c.BoardZone.GetAll().Where(m => m.IsRace(Race.MECHANICAL)).ToList();
						if (mechs.Count < V("GVG_111", "mechs", 3)) return;
						foreach (Minion m in mechs) m.Destroy();
						g.ForcedDeathPhase(); // rules A9
						if (!c.BoardZone.IsFull)
							Generic.SummonBlock.Invoke(g, (Minion)Entity.FromCard(c, Cards.FromId("GVG_111t")), -1, s);
					})
				}
			}));

			// [GVG_113] Foe Reaper 4000 (8), [AT_067] Magnataur Alpha (4) — Also damages the minions next to whomever it attacks.
			cards.Add("GVG_113", new CardDef(new Power { Trigger = Cleave() }));
			cards.Add("AT_067", new CardDef(new Power { Trigger = Cleave() }));

			// [GVG_119] Blingtron 3000 (5) — Battlecry: Equip a random weapon for each player.
			cards.Add("GVG_119", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var weapons = Cards.GenerationPool(g, c).Where(card => card.Type == CardType.WEAPON).ToList();
					if (weapons.Count == 0) return;
					foreach (Controller p in new[] { c, c.Opponent })
					{
						var weapon = (Weapon)Entity.FromCard(p, weapons[g.Random.Next(weapons.Count)]);
						weapon.Card.Power?.Aura?.Activate(weapon);
						weapon.Card.Power?.Trigger?.Activate(weapon);
						p.Hero.AddWeapon(weapon);
					}
				})
			}));

			// [GVG_122] Wee Spellstopper (4) — Adjacent minions can't be targeted by spells or Hero Powers.
			cards.Add("GVG_122", new CardDef(new Power
			{
				Aura = new AdjacentAura(
					new Effect(GameTag.CANT_BE_TARGETED_BY_SPELLS, EffectOperator.SET, 1),
					new Effect(GameTag.CANT_BE_TARGETED_BY_HERO_POWERS, EffectOperator.SET, 1))
			}));

			// [GVG_087] Steamwheedle Sniper (2) — Your Hero Power can target minions. (Controller.Options offers the targets; the
			// Steady Shots below hit the chosen one.)
			cards.Add("GVG_087", new CardDef(new Power { Aura = new Aura(AuraType.CONTROLLER, new Effect(GameTag.STEADY_SHOT_CAN_TARGET, EffectOperator.ADD, 1)) }));
			// [DS1h_292] Steady Shot / [AT_132_HUNTER] Ballista Shot — Deal 2 / 3 damage to the enemy hero (or the chosen target
			// while a Steamwheedle Sniper is out).
			foreach (var (id, damage) in new[] { ("DS1h_292", 2), ("AT_132_HUNTER", 3) })
				cards.Add(id, new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_STEADY_SHOT, 0 }, { PlayReq.REQ_MINION_OR_ENEMY_HERO, 0 } },
					new Power
					{
						PowerTask = new CustomTask((g, c, s, t, stack) =>
							Generic.DamageCharFunc((IPlayable)s, t as ICharacter ?? c.Opponent.Hero, V(id, "damage", damage), false))
					}));

			// [ICC_832p] Plague Lord (Malfurion the Pestilent) — Choose One: +3 Attack this turn; or Gain 3 Armor. Upstream gave it
			// a friendly-minion target and no power; its options ICC_832pa/pb are scripted.
			cards.Add("ICC_832p", new CardDef(new Power()));

			// ---------------------------------------------------------------- The Grand Tournament

			// [AT_005] Polymorph: Boar (3) — Transform a minion into a 4/2 Boar with Charge.
			cards.Add("AT_005", new CardDef(TargetMinion, new Power { PowerTask = new TransformTask("AT_005t", EntityType.TARGET) }));

			// [AT_027] Wilfred Fizzlebang (6) — Cards you draw from your Hero Power cost (0). (A DRAW trigger while the Hero Power resolves.)
			cards.Add("AT_027", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DRAW) // cards drawn by your Hero Power cost (0)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p.Controller.ResolvingHeroPower),
					SingleTask = new AddEnchantmentTask("AT_027e", EntityType.TARGET)
				}
			}));
			cards.Add("AT_027e", new CardDef(new Power { Enchant = new Enchant(Effects.SetCost(V("AT_027", "cost", 0))) }));

			// [AT_034] Poisoned Blade (2) — Your Hero Power gives this +1 Attack instead of replacing it. (HsServer, WONDERS: the shared
			// rule — Generic.HeroPowerBuffsWeapon: any weapon-equipping Hero Power's equip is replaced, the +1 comes in the Inspire
			// Phase as the weapon's TRIGGER (index 0, PoisonedBlade_Throw_FX); WON_318 plays it (tag 858). Was: Dagger Mastery checked
			// for the card id "AT_034".)
			cards.Add("AT_034", new CardDef(Generic.HeroPowerBuffsWeapon("AT_034e")));
			cards.Add("AT_034e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("AT_034", "attack", 1))) }));
			cards.Add("CS2_083b", new CardDef(new Power { PowerTask = new WeaponTask("CS2_082") }));

			// [AT_079] Mysterious Challenger (6) — Battlecry: Put one of each Secret from your deck into the battlefield.
			cards.Add("AT_079", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (var group in c.DeckZone.Where(p => p is Spell sp && sp.IsSecret).GroupBy(Generic.UniqueCardKey).ToList())
					{
						var secret = (Spell)group.First();
						if (c.SecretZone.IsFull) break;
						if (c.SecretZone.Any(p => p.Card.Id == secret.Card.Id)) continue;
						c.DeckZone.Remove(secret);
						PutSecretIntoPlay(c, secret);
					}
				})
			}));

			// [AT_081] Eadric the Pure (7) — Battlecry: Change all enemy minions' Attack to 1.
			cards.Add("AT_081", new CardDef(new Power { PowerTask = new AddEnchantmentTask("AT_081e", EntityType.OP_MINIONS) }));
			cards.Add("AT_081e", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttack(V("AT_081", "attack", 1))) }));

			// [AT_115] Fencing Coach (3) — Battlecry: The next time you use your Hero Power, it costs (2) less.
			cards.Add("AT_115", new CardDef(new Power { PowerTask = new AddEnchantmentTask("AT_115e", EntityType.CONTROLLER) }));
			cards.Add("AT_115e", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HEROPOWER, Effects.ReduceCost(V("AT_115", "discount", 2))),
				Trigger = new Trigger(TriggerType.INSPIRE)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = RemoveEnchantmentTask.Task
				}
			}));

			// ---------------------------------------------------------------- League of Explorers

			// [LOE_009] Obsidian Destroyer (7) — At the end of your turn, summon a 1/1 Scarab with Taunt.
			cards.Add("LOE_009", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = new SummonTask("LOE_009t", SummonSide.RIGHT) }
			}));

			// [LOE_023] Dark Peddler (2), [LOE_029] Jeweled Scarab (2) — Battlecry: Discover a 1- / 3-Cost card.
			cards.Add("LOE_023", new CardDef(new Power { PowerTask = new DiscoverTask(CardType.INVALID, CardClass.INVALID, (GameTag.COST, RelaSign.EQ, V("LOE_023", "cost", 1))) }));
			cards.Add("LOE_029", new CardDef(new Power { PowerTask = new DiscoverTask(CardType.INVALID, CardClass.INVALID, (GameTag.COST, RelaSign.EQ, V("LOE_029", "cost", 3))) }));

			// [LOE_046] Huge Toad (2) — Deathrattle: Deal 1 damage to a random enemy.
			cards.Add("LOE_046", new CardDef(new Power { DeathrattleTask = ComplexTask.DamageRandomTargets(1, EntityType.ENEMIES, V("LOE_046", "damage", 1)) }));

			// [LOE_047] Tomb Spider (4) — Battlecry: Discover a Beast.
			cards.Add("LOE_047", new CardDef(new Power { PowerTask = new DiscoverTask(DiscoverType.BEAST) }));

			// [LOE_053] Djinni of Zephyrs (5) — After you cast a spell on another friendly minion, cast a copy of it on this one.
			cards.Add("LOE_053", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY_SPELL_CASTED_ON_OWN_MINIONS,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Minion djinni) || !(t is Spell spell) || djinni.IsDead || djinni.ToBeDestroyed) return;
						if (spell.CardTarget == 0 || spell.CardTarget == djinni.Id || djinni.Zone?.Type != SabberStoneCore.Enums.Zone.PLAY) return;
						if (!(g.IdEntityDic[spell.CardTarget] is Minion first) || first.Controller != c) return;
						var copy = (Spell)Entity.FromCard(c, spell.Card);
						Generic.CastSpell.Invoke(c, g, copy, djinni, 0);
					})
				}
			}));

			// [LOE_061] Anubisath Sentinel (5) — Deathrattle: Give a random friendly minion +3/+3.
			cards.Add("LOE_061", new CardDef(new Power
			{
				DeathrattleTask = ComplexTask.Create(
					new IncludeTask(EntityType.MINIONS),
					new FilterStackTask(SelfCondition.IsNotDead),
					new RandomTask(1, EntityType.STACK),
					new AddEnchantmentTask("LOE_061e", EntityType.STACK))
			}));
			cards.Add("LOE_061e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("LOE_061", "stats", 3))) }));

			// [LOE_073] Fossilized Devilsaur (8) — Battlecry: If you control a Beast, gain Taunt.
			cards.Add("LOE_073", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c.BoardZone.GetAll().Any(m => m.Id != s.Id && m.IsRace(Race.BEAST)),
					new CustomTask((g, c, s, t, stack) => ((Minion)s).HasTaunt = true))
			}));

			// [LOE_092] Arch-Thief Rafaam (9) — Battlecry: Discover a powerful Artifact.
			cards.Add("LOE_092", new CardDef(new Power { PowerTask = new DiscoverTask(DiscoverType.ARTIFACT) }));

			// [LOE_107] Eerie Statue (4) — Can't attack unless it's the only minion on the battlefield.
			cards.Add("LOE_107", new CardDef(new Power // "Can't attack unless it's the only minion in the battlefield"
			{
				Aura = new AdaptiveEffect(new SelfCondition(me =>
					me.Controller.BoardZone.GetAll().Any(m => m.Id != me.Id) || me.Controller.Opponent.BoardZone.GetAll().Length > 0), GameTag.CANT_ATTACK)
			}));

			// [LOE_118] Cursed Blade (1) — Double all damage dealt to your hero.
			cards.Add("LOE_118", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.PREDAMAGE)
				{
					TriggerSource = TriggerSource.HERO,
					FastExecution = true,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (g.CurrentEventData != null) g.CurrentEventData.EventNumber *= 2;
					})
				}
			}));

			// [LOE_119] Animated Armor (4) — Your hero can only take 1 damage at a time.
			cards.Add("LOE_119", new CardDef(new Power { Trigger = Generic.PreDamageCapTrigger(TriggerSource.HERO, 1) })); // (only a hit above 1)

			// ---------------------------------------------------------------- Witchwood, Boomsday, Rastakhan, Rise of Shadows

			// [GIL_655] Festeroot Hulk (5) — After a friendly minion attacks, gain +1 Attack.
			cards.Add("GIL_655", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.MINIONS,
					SingleTask = new AddEnchantmentTask("GIL_655e", EntityType.SOURCE)
				}
			}));
			cards.Add("GIL_655e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("GIL_655", "attack", 1))) }));

			// [BOT_436] Prismatic Lens (4) — Draw a minion and a spell from your deck. Swap their Costs.
			cards.Add("BOT_436", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var minions = c.DeckZone.Where(p => Generic.IsMinionCard(p)).ToList();
					var spells = c.DeckZone.Where(p => p is Spell).ToList();
					IPlayable minion = minions.Count > 0 ? Generic.Draw(c, minions[g.Random.Next(minions.Count)]) : null;
					IPlayable spell = spells.Count > 0 ? Generic.Draw(c, spells[g.Random.Next(spells.Count)]) : null;
					if (minion == null || spell == null) return;
					int a = minion.Cost, b = spell.Cost;
					minion.Cost = b;
					spell.Cost = a;
				})
			}));

			// [BOT_914] Whizbang the Wonderful, [DAL_800] Zayle, Shadow Cloak — "You start the game with one of ... Decks!": a
			// deck-building rule (the server picks the deck); in play they're plain minions.
			cards.Add("BOT_914", new CardDef(new Power { PowerTask = Nothing }));
			cards.Add("DAL_800", new CardDef(new Power { PowerTask = Nothing }));

			// [TRL_318] Hex Lord Malacrass (8) — Battlecry: Add a copy of your opening hand to your hand (except this card).
			cards.Add("TRL_318", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c.OpeningHand == null) return;
					bool skippedSelf = false;
					foreach (Card card in c.OpeningHand)
					{
						if (!skippedSelf && card.Id == s.Card.Id) { skippedSelf = true; continue; }
						AddToHand(c, card);
					}
				})
			}));

			// [TRL_522] Wartbringer (1) — Battlecry: If you played 2 spells this turn, deal 2 damage.
			cards.Add("TRL_522", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_IF_AVAILABLE, 0 } }, new Power
			{
				PowerTask = ComplexTask.Create(
					new CustomTask((g, c, s, t, stack) =>
						stack.Flag = t != null && c.CardsPlayedThisTurn.Count(card => card.Type == CardType.SPELL) >= V("TRL_522", "spells", 2)),
					new FlagTask(true, new DamageTask(V("TRL_522", "damage", 2), EntityType.TARGET)))
			}));

			// [TRL_532] Mosh'Ogg Announcer (5) — Enemies attacking this have a 50% chance to attack someone else.
			cards.Add("TRL_532", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ATTACK)
				{
					TriggerSource = TriggerSource.ENEMY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (g.CurrentEventData?.EventTarget?.Id != s.Id) return;
						ForgetfulAttack(g, g.CurrentEventData.EventSource as ICharacter, V("TRL_532", "chance %", 50));
					})
				}
			}));

			// [TRL_535] Snapjaw Shellfighter (5) — Whenever an adjacent minion takes damage, this minion takes it instead.
			cards.Add("TRL_535", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.PREDAMAGE)
				{
					TriggerSource = TriggerSource.MINIONS_EXCEPT_SELF,
					FastExecution = true,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Minion me) || !(t is Minion hurt) || g.CurrentEventData == null) return;
						if (me.IsDead || me.ToBeDestroyed || hurt.Zone != me.Zone || System.Math.Abs(hurt.ZonePosition - me.ZonePosition) != 1) return;
						if (hurt[HsTag.TAKING_REDIRECTED_DAMAGE] > 0) return; // two Shellfighters don't pass it back and forth
						int amount = g.CurrentEventData.EventNumber;
						if (amount <= 0) return;
						g.CurrentEventData.EventNumber = 0;
						me[HsTag.TAKING_REDIRECTED_DAMAGE] = 1;
						me.TakeDamage(g.CurrentEventData.EventSource, amount);
						me[HsTag.TAKING_REDIRECTED_DAMAGE] = 0;
					})
				}
			}));

			// [DAL_729] Madame Lazul (3) — Battlecry: Discover a copy of a card in your opponent's hand.
			var lazul = new CardDef(new Power
			{
				PowerTask = new DiscoverFromTask((g, c, s) => c.Opponent.HandZone.Select(p => p.Card))
			});
			cards.Add("DAL_729", lazul);
			cards.Add("CORE_DAL_729", lazul);

			// [DAL_377] Nine Lives (3) — Discover a friendly Deathrattle minion that died this game. Also trigger its Deathrattle.
			cards.Add("DAL_377", new CardDef(new Power
			{
				PowerTask = new DiscoverFromTask((g, c, s) => c.DiedThisGameCards.Where(p => p is Minion && p.Card.Deathrattle).Select(p => p.Card),
					afterPick: new ActivateDeathrattleTask(EntityType.TARGET))
			}));

			// [DAL_431] Swampqueen Hagatha (7) — Battlecry: Add a 5/5 Horror to your hand. Teach it two Shaman spells.
			// Two Discovers of Shaman spells (the second without the first, and untargeted if the first targets); the picks are
			// remembered on the Horror (TAG_SCRIPT_DATA_ENT_1/2, which its text shows), which casts them when played.
			cards.Add("DAL_431", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c.HandZone.IsFull) return;
					var horror = Entity.FromCard(c, Cards.FromId("DAL_431t"));
					Generic.AddHandPhase.Invoke(c, horror);
					var spells = Cards.GenerationPool(g, c)
						.Where(card => card.Type == CardType.SPELL && card.HasClass(CardClass.SHAMAN)).ToList();
					Card[] Offer(IEnumerable<Card> pool)
					{
						var list = pool.ToList();
						var offer = new List<Card>();
						while (offer.Count < 3 && list.Count > 0)
						{
							Card pick = list[g.Random.Next(list.Count)];
							list.Remove(pick);
							offer.Add(pick);
						}
						return offer.ToArray();
					}
					var second = new CustomTask((g2, c2, s2, chosen, stack2) =>
					{
						if (chosen != null) Generic.CarrySpell(horror, chosen, 1);
					});
					var first = new CustomTask((g2, c2, s2, chosen, stack2) =>
					{
						if (chosen == null) return;
						Generic.CarrySpell(horror, chosen, 0);
						var rest = spells.Where(card => card.Id != chosen.Card.Id && (!chosen.Card.MustHaveTargetToPlay || !card.MustHaveTargetToPlay));
						Card[] offer2 = Offer(rest);
						if (offer2.Length > 0)
							Generic.CreateChoiceCards.Invoke(c2, s2, null, ChoiceType.GENERAL, ChoiceAction.STACK, offer2, second);
					});
					Card[] offer1 = Offer(spells);
					if (offer1.Length > 0)
						Generic.CreateChoiceCards.Invoke(c, s, null, ChoiceType.GENERAL, ChoiceAction.STACK, offer1, first);
				})
			}));
			// [DAL_431t] Drustvar Horror (5) — Battlecry: Cast {0} and {1}. (The targeted one at the Horror's target: wiki "you choose the
			// target when you play the minion, just like any other targeted battlecry" — Generic.CastCarriedSpells.)
			cards.Add("DAL_431t", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => Generic.CastCarriedSpells(c, (IPlayable)s, t)) }));

			// [DAL_573] Commander Rhyssa (3) — Your Secrets trigger twice. (Its aura sets HsTag.SECRETS_TRIGGER_TWICE.)
			cards.Add("DAL_573", new CardDef(new Power { Aura = new Aura(AuraType.CONTROLLER, new Effect(HsTag.SECRETS_TRIGGER_TWICE, EffectOperator.ADD, 1)) }));

			// [DAL_731] Duel! (5) — Summon a minion from each player's deck. They fight!
			cards.Add("DAL_731", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Minion Pull(Controller p)
					{
						var minions = p.DeckZone.Where(x => Generic.IsMinionCard(x)).ToList();
						if (minions.Count == 0 || p.BoardZone.IsFull) return null;
						var m = (Minion)minions[g.Random.Next(minions.Count)];
						p.DeckZone.Remove(m);
						Generic.SummonBlock.Invoke(g, m, -1, s);
						return m;
					}
					Minion mine = Pull(c), theirs = Pull(c.Opponent);
					if (mine == null || theirs == null || mine.IsDead || theirs.IsDead) return;
					EventMetaData temp = g.CurrentEventData;
					Generic.AttackBlock.Invoke(c, mine, theirs, true, true);
					g.DeathProcessingAndAuraUpdate();
					g.CurrentEventData = temp;
				})
			}));

			// [DAL_742] Whirlwind Tempest (8) — Your minions with Windfury have Mega-Windfury. (Generic.AttacksPerTurn checks it.)
			cards.Add("DAL_742", new CardDef(new Power // "Your Windfury minions have Mega-Windfury"
			{
				Aura = new Aura(AuraType.BOARD, new Effect(GameTag.MEGA_WINDFURY, EffectOperator.SET, 1)) { Condition = new SelfCondition(p => p[GameTag.WINDFURY] > 0) }
			}));

			// ---------------------------------------------------------------- Hall of Fame

			// [EX1_112] Gelbin Mekkatorque (6) — Battlecry: Summon an AWESOME invention.
			var gelbin = new CardDef(new Power { PowerTask = ComplexTask.Create(new RandomEntourageTask(), new SummonTask(SummonSide.RIGHT)) });
			cards.Add("EX1_112", gelbin);

			// [NEW1_016] Captain's Parrot (2) — Battlecry: Draw a Pirate from your deck.
			var parrot = new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var pirates = c.DeckZone.Where(p => p is Minion m && m.IsRace(Race.PIRATE)).ToList();
					if (pirates.Count > 0) Generic.Draw(c, pirates[g.Random.Next(pirates.Count)]);
				})
			});
			cards.Add("NEW1_016", parrot);

			// [PRO_001] Elite Tauren Chieftain (5) — Battlecry: Give both players the power to ROCK! (with a Power Chord card)
			var etc = new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Card[] chords = s.Card.Entourage.Select(Cards.FromId).ToArray();
					if (chords.Length == 0) return;
					foreach (Controller p in new[] { c, c.Opponent })
						AddToHand(p, chords[g.Random.Next(chords.Length)]);
				})
			});
			cards.Add("PRO_001", etc);

			// The Power Chords (PRO_001's entourage; were unscripted placeholders: "ETC does nothing", user 2026-10-08).
			// [PRO_001a] I Am Murloc (4) — Summon three, four, or five 1/1 Murlocs. (No one picks the number: the game does.)
			cards.Add("PRO_001a", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = g.Random.Next(V("PRO_001a", "min", 3), V("PRO_001a", "max", 5) + 1);
					new SummonTask("PRO_001at", n, SummonSide.SPELL).Process(g, c, s, t, stack);
				})
			}));
			// [PRO_001b] Rogues Do It... (4) — Deal $4 damage. Draw a card.
			cards.Add("PRO_001b", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("PRO_001b", "damage", 4), EntityType.TARGET, true), new DrawTask())
			}));
			// [PRO_001c] Power of the Horde (4) — Summon a random Horde Warrior. (Its entourage: the six Horde Warriors.)
			cards.Add("PRO_001c", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new RandomEntourageTask(), new SummonTask(SummonSide.SPELL))
			}));
			// ---------------------------------------------------------------- Summoned tokens that didn't match their text (livecheck 2026-10-10;
			// a static audit of every SummonTask / FromId id against its card's "summon a N/M": RESEARCH/engine-rules.md)

			// [BRM_022] Dragon Egg (1) — Whenever this minion takes damage, summon a 2/1 Whelp. Upstream summoned Nefarian's 1/1 BRM_004t;
			// the 2/1 is Black Whelp BRM_022t (live session-20261005-140614 2833: BRM_022t 2/1).
			cards.Add("BRM_022", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TAKE_DAMAGE) { TriggerSource = TriggerSource.SELF, SingleTask = new SummonTask("BRM_022t") }
			}));
			// [DREAM_02] Ysera Awakens (2) — Deal $5 damage to all characters except Ysera. Every Ysera (client card tag 4392: Generic.YSERA),
			// not only cards named "Ysera" (live session-20260930-125547 13769: Ysera the Dreamer CS3_033 undamaged; ours hit it for 5).
			cards.Add("DREAM_02", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					new IncludeTask(EntityType.ALL),
					new FilterStackTask(new SelfCondition(p => p.Card[Generic.YSERA] == 0)),
					new DamageTask(V("DREAM_02", "damage", 5), EntityType.STACK, true))
			}));
			// [UNG_208] Stone Sentinel (7) — Battlecry: If you played an Elemental last turn, summon two 2/3 Elementals with Taunt. Upstream
			// summoned two 1/2 Flame Elementals (UNG_809t1) and took another card's play requirements (a friendly Deathrattle target); the
			// 2/3 Taunt Elemental is Rock Elemental UNG_208t.
			cards.Add("UNG_208", new CardDef(new Power
			{
				// (the shared condition: it drives the ELEMENTAL_POWERED_UP glow)
				PowerTask = ComplexTask.Create(
					new ConditionTask(EntityType.SOURCE, SelfCondition.ElementalPlayedLastTurn),
					new FlagTask(true, new SummonTask("UNG_208t", V("UNG_208", "elementals", 2))))
			}));
		}
	}
}
