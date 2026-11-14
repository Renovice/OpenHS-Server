// HsServer addition (not upstream SabberStone): Across the Timeways — chunk 3: Paladin, Priest and Rogue (spec: research spec_chunk3).
// Shared pieces: Generic.DiscoverCopyOf / PutDurationSpellIntoPlay / ControlsAura / FabledFamily (Actions/TimeTravel.cs), Copy's
// keepEnchantments, the Hero Power heal doubler (Character.TakeHeal), Generic.ShiftInHand (Shapeshifter), SetAsideHand (The Fins
// Beyond Time), Power.TargetIf with the client's PlayReq 159 (Eternus). Rewind: see TimeTravelCardsGenHs.cs. Every number goes
// through CardValues (V).
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
		/// <summary>Client PlayReq REQ_TARGET_HEALTH_LESS_THAN_EQUAL_MY_HEALTH (acs PlayErrors: 159) — Eternus.</summary>
		private const PlayReq REQ_TARGET_HEALTH_LESS_THAN_EQUAL_MY_HEALTH = (PlayReq)159;

		/// <summary>The minions on both sides but <paramref name="except"/> (not Locations, not Dormant / untouchable).</summary>
		private static List<Minion> AllOtherMinions(Controller c, IEntity except) =>
			c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll())
				.Where(m => m.Id != except?.Id && !(m is Location) && !m.Untouchable && !m.IsDead && !m.ToBeDestroyed).ToList();

		private static void TimeTravelChunk3(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Paladin
			// [END_012] Hand of Infinity (3, weapon) — Can't attack heroes. Battlecry: Set this weapon's Attack to INFINITY this turn!
			// (Can't attack heroes: ReprintCards' text rule. INFINITY: Generic.Infinity; "this turn" = a one-turn ADD, which reverts)
			cards.Add("END_012", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (s is Weapon w && !w.ToBeDestroyed) Give(g, s, w, "END_012e", Math.Max(0, Generic.Infinity - w.AttackDamage));
				})
			}));
			cards.Add("END_012e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(0)) { UseScriptTag = true, IsOneTurnEffect = true } }));
			// [TIME_009] Gelbin of Tomorrow (8) — Fabled. Battlecry: Put one of each Aura from your deck into the battlefield. (an Aura already
			// up may come again: wiki patch 34.0.0)
			cards.Add("TIME_009", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var auras = c.DeckZone.Where(p => p is Spell && p.Card[HsTag.PALADIN_AURA] > 0).GroupBy(p => p.Card.Id)
						.Select(x => x.ElementAt(g.Random.Next(x.Count()))).Cast<Spell>().ToList();
					foreach (Spell aura in auras)
						if (!Generic.PutDurationSpellIntoPlay(c, aura)) break;
				})
			}));
			// [TIME_009t1] Gnomish Aura (4) — Tradeable. At the end of your turn, restore #4 Health to all your characters. Lasts @ turns.
			cards.Add("TIME_009t1", new CardDef(PaladinAura(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = new HealTask(V("TIME_009t1", "heal", 4), EntityType.FRIENDS) }
			})));
			// [TIME_009t2] Mekkatorque's Aura (5) — Tradeable. At the end of your turn, give a random friendly minion +4/+4 and Divine Shield.
			cards.Add("TIME_009t2", new CardDef(PaladinAura(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(RandomFriendly(g, c) is Minion m)) return;
						Give(g, s, m, "TIME_009t2e");
						m.HasDivineShield = true;
					})
				}
			})));
			cards.Add("TIME_009t2e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TIME_009t2", "stats", 4))) }));
			// [TIME_015] Hardlight Protector (2) — Divine Shield. Battlecry: Restore #3 Health to your hero and give them Divine Shield.
			cards.Add("TIME_015", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					c.Hero.TakeHeal((IPlayable)s, V("TIME_015", "heal", 3));
					c.Hero[GameTag.DIVINE_SHIELD] = 1; // (a hero's shield absorbs: Character.TakeDamage)
				})
			}));
			// [TIME_016] Neon Innovation (3) — Discover a Paladin Mech from the past. Give it +5/+5.
			cards.Add("TIME_016", new CardDef(new Power
			{
				PowerTask = new DiscoverFromTask((g, c, s) => PastCards(g, c, x => x.Type == CardType.MINION && x.IsRace(Race.MECHANICAL) && x.HasClass(CardClass.PALADIN)), "TIME_016e")
			}));
			cards.Add("TIME_016e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TIME_016", "stats", 5))) }));
			// [TIME_017] Tankgineer (4) — Divine Shield. Deathrattle: Summon a 7/7 Tank with Divine Shield.
			cards.Add("TIME_017", new CardDef(new Power { DeathrattleTask = new SummonTask("GVG_079", SummonSide.DEATHRATTLE) }));
			// [TIME_018] Mend the Timeline (3) — Rewind. Get 2 random Holy spells. Restore Health to your hero equal to their Costs. (any class:
			// live 188326 — a Priest spell for a Paladin; the heal is their Costs' sum, burnt ones too — doubt)
			cards.Add("TIME_018", new CardDef(new Power
			{
				PowerTask = RewindPower((g, c, s, t) =>
				{
					int costs = 0;
					for (int i = 0; i < V("TIME_018", "spells", 2); i++)
						if (RandomCard(g, x => x.Type == CardType.SPELL && x[SPELL_SCHOOL] == HOLY) is Card spell)
						{
							costs += spell.Cost;
							AddToHand(c, spell);
						}
					c.Hero.TakeHeal((IPlayable)s, costs);
				})
			}));
			// [TIME_019] Manifested Timeways (4) — Battlecry: If you control an Aura, deal 3 damage to all enemies.
			cards.Add("TIME_019", new CardDef(new Power
			{
				GlowWhile = p => Generic.ControlsAura(p.Controller),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (Generic.ControlsAura(c)) DamageAll(s, Enemies(c), V("TIME_019", "damage", 3), false);
				})
			}));
			// [TIME_043] PMM Infinitizer (6) — Battlecry: Set a friendly minion's Attack and Health to 8. It can't attack heroes this turn.
			cards.Add("TIME_043", new CardDef(BattlecryFriendlyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					Give(g, s, m, "TIME_043e");
					Give(g, s, m, "TIME_043e2");
				})
			}));
			cards.Add("TIME_043e", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttack(V("TIME_043", "attack", 8)), Effects.SetMaxHealth(V("TIME_043", "health", 8))) }));
			cards.Add("TIME_043e2", OneTurn(new Effect(GameTag.CANNOT_ATTACK_HEROES, EffectOperator.SET, 1)));
			// [TIME_044] Past / Present / Future Gnomeregan (2, Location) — Give a minion +2/+1 (Present: and "Deathrattle: Deal 2 damage to
			// the enemy hero."; Future: also Divine Shield). Advance to the present / future (LocationActions).
			void Gnomeregan(Game g, IEntity s, IPlayable t, bool deathrattle, bool shield)
			{
				if (!(t is Minion m)) return;
				Give(g, s, m, "TIME_044e");
				if (deathrattle) Give(g, s, m, "TIME_044t1e");
				if (shield) m.HasDivineShield = true;
			}
			cards.Add("TIME_044", new CardDef(TargetMinion, new Power { PowerTask = new CustomTask((g, c, s, t, stack) => Gnomeregan(g, s, t, false, false)) }));
			cards.Add("TIME_044t1", new CardDef(TargetMinion, new Power { PowerTask = new CustomTask((g, c, s, t, stack) => Gnomeregan(g, s, t, true, false)) }));
			cards.Add("TIME_044t2", new CardDef(TargetMinion, new Power { PowerTask = new CustomTask((g, c, s, t, stack) => Gnomeregan(g, s, t, true, true)) }));
			cards.Add("TIME_044e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("TIME_044", "attack", 2)), Effects.Health_N(V("TIME_044", "health", 1))) }));
			cards.Add("TIME_044t1e", new CardDef(new Power { DeathrattleTask = new DamageTask(V("TIME_044t1", "damage", 2), EntityType.OP_HERO) }));
			// [TIME_700] Chronological Aura (5) — At the end of your turn, summon a 3/5 Dragon with Taunt. Lasts @ turns.
			cards.Add("TIME_700", new CardDef(PaladinAura(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = new SummonTask("TIME_700t", SummonSide.SPELL) }
			})));
			// [TIME_706] The Fins Beyond Time (2) — Battlecry: Replace your hand with your starting hand. Swap back at the end of your turn.
			// (wiki: like Secret Passage — the hand is set aside; new copies of the starting hand (TIME_706e2); at the end of the turn those
			// still in hand vanish, the hand comes back after cards gained meanwhile; the marker leaves after one run: twice-triggered turn
			// ends are safe, wiki patch 34.4.0)
			cards.Add("TIME_706", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					List<Card> start = Generic.StartingHand(c);
					SetAsideHand(g, c, s, "TIME_706e3");
					Enchantment marker = c.AppliedEnchantments?.LastOrDefault(e => e.Card.Id == "TIME_706e3");
					foreach (Card card in start)
					{
						if (c.HandZone.IsFull) break;
						IPlayable p = Entity.FromCard(c, card, new EntityData { { GameTag.CREATOR, s.Id }, { GameTag.DISPLAYED_CREATOR, s.Id } });
						if (!Generic.AddHandPhase.Invoke(c, p)) continue;
						if (marker != null) p[HsTag.HAND_UNTIL_TURN_END] = marker.Id;
						Give(g, s, p, "TIME_706e2");
					}
				})
			}));
			cards.Add("TIME_706e3", SetAsideHandReturns(tempCardsToDeck: false));
			cards.Add("TIME_706e2", new CardDef(new Power { Enchant = new Enchant() }));

			// ---------------------------------------------------------------- Priest
			// [END_027] Wings of Eternity (1) — Discover a Dragon from the past with a Dark Gift.
			cards.Add("END_027", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => DiscoverWithDarkGift(g, c, s, PastCards(g, c, x => x.Type == CardType.MINION &&
					x.IsRace(Race.DRAGON) && (x.HasClass(c.HeroClass) || x.HasClass(CardClass.NEUTRAL)))))
			}));
			// [TIME_037] Disciple of the Dove (3) — Battlecry: Draw a minion. Give minions in your hand +2 Health.
			cards.Add("TIME_037", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					DrawOne(g, c, p => p is Minion);
					GiveAll(g, s, c.HandZone.Where(p => Generic.IsMinionCard(p)), "TIME_037e");
				})
			}));
			cards.Add("TIME_037e", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(V("TIME_037", "health", 2))) }));
			// [TIME_427] Cleansing Lightspawn (4) — Lifesteal. Battlecry: Deal damage to an enemy minion equal to this minion's Health.
			cards.Add("TIME_427", new CardDef(BattlecryEnemyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is Minion m && s is Minion me) Generic.DamageCharFunc(me, m, Math.Max(0, me.Health), false);
				})
			}));
			// [TIME_429] Divine Augur (4) — Battlecry: Set the Attack and Health of every minion in your hand to the higher of the two stats.
			cards.Add("TIME_429", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in c.HandZone.OfType<Minion>().Where(m => Generic.IsMinionCard(m)).ToList())
					{
						int high = Math.Max(m.AttackDamage, m.Health);
						if (m.AttackDamage < high) Give(g, s, m, "TIME_429e1", high - m.AttackDamage);
						if (m.Health < high) Give(g, s, m, "TIME_429e2", high - m.Health);
					}
				})
			}));
			cards.Add("TIME_429e1", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(0)) { UseScriptTag = true } }));
			cards.Add("TIME_429e2", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(0)) { UseScriptTag = true } }));
			// [TIME_431] Amber Priestess (2) — Taunt. Battlecry: Restore Health to a character equal to this minion's Health.
			cards.Add("TIME_431", new CardDef(BattlecryAny, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is ICharacter ch && s is Minion me && me.Health > 0) ch.TakeHeal(me, me.Health);
				})
			}));
			// [TIME_432] Intertwined Fate (3) — Discover a copy of a card from your deck and one from your opponent's. (live 190813: yours,
			// then — after that pick — theirs; an empty deck skips its Discover)
			cards.Add("TIME_432", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					void Theirs(Game g2, Controller c2, IEntity s2) => Generic.DiscoverCopyOf(c2, s2, c2.Opponent.DeckZone.GetAll());
					if (!Generic.DiscoverCopyOf(c, s, c.DeckZone.GetAll(), (g2, c2, s2, pick) => Theirs(g2, c2, s2))) Theirs(g, c, s);
				})
			}));
			// [TIME_433] Cease to Exist (3) — Rewind. Silence and destroy a random enemy minion.
			cards.Add("TIME_433", new CardDef(new Power
			{
				PowerTask = RewindPower((g, c, s, t) =>
				{
					var enemies = c.Opponent.BoardZone.GetAll().Where(m => !(m is Location) && !m.Untouchable && !m.IsDead && !m.ToBeDestroyed).ToList();
					if (enemies.Count == 0) return;
					Minion m = enemies[g.Random.Next(enemies.Count)];
					new SilenceTask(EntityType.TARGET).Process(g, c, s, m);
					Generic.Destroy(m, s);
				})
			}));
			// [TIME_435] Eternus (6) — Battlecry: Take control of an enemy minion with this minion's Health or less. (its current Health:
			// buffed in hand by Disciple of the Dove / Power Word: Barrier; the client's PlayReq 159)
			cards.Add("TIME_435", new CardDef(BattlecryEnemyMinion, new Power
			{
				TargetIf = (REQ_TARGET_HEALTH_LESS_THAN_EQUAL_MY_HEALTH, (src, target) => target is Minion m && m.Health <= (src is Minion me ? me.Health : src.Card.Health)),
				PowerTask = new ControlTask(EntityType.TARGET)
			}));
			// [TIME_436] Past / Present / Future Conflux (7, Location) — Summon a random Dragon that costs (5) or more (Present: Discover one
			// and summon it; Future: also get a copy of it). Advance to the present / future (LocationActions).
			bool BigDragon(Card x) => x.Type == CardType.MINION && x.IsRace(Race.DRAGON) && x.Cost >= V("TIME_436", "cost", 5) && !x.NeverRandomlyOnBoard;
			var slot = new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } };
			cards.Add("TIME_436", new CardDef(slot, new Power { PowerTask = new CustomTask((g, c, s, t, stack) => Summon(g, c, RandomBoardMinion(g, BigDragon), s)) }));
			cards.Add("TIME_436t1", new CardDef(slot, new Power { PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => BigDragon(x), action: ChoiceAction.SUMMON) }));
			cards.Add("TIME_436t2", new CardDef(slot, new Power
			{
				PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => BigDragon(x), action: ChoiceAction.SUMMON,
					afterPick: new CustomTask((g, c, s, t, stack) => { if (t != null) AddToHand(c, t.Card); }))
			}));
			// [TIME_447] Power Word: Barrier (1) — Give a character Divine Shield. Give minions in your hand +2 Health.
			cards.Add("TIME_447", new CardDef(TargetAny, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is Minion m) m.HasDivineShield = true;
					else if (t is Hero h) h[GameTag.DIVINE_SHIELD] = 1;
					GiveAll(g, s, c.HandZone.Where(p => Generic.IsMinionCard(p)), "TIME_447e2");
				})
			}));
			cards.Add("TIME_447e2", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(V("TIME_447", "health", 2))) }));
			// [TIME_890] Medivh the Hallowed (10) — Fabled. Costs (0) if you control Karazhan. Battlecry: Silence and destroy all other minions.
			// (Karazhan / Atiesh / Medivh: the Fabled family's location / weapon / minion — Generic.FabledRelative)
			bool Controls(IPlayable p, CardType type) => Generic.FabledRelative(p.Card, type) is Card other &&
				(type == CardType.WEAPON ? p.Controller.Hero.Weapon?.Card == other : p.Controller.BoardZone.Any(x => x.Card == other));
			cards.Add("TIME_890", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => 0, EffectOperator.SET, new SelfCondition(p => Controls(p, CardType.LOCATION))),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					List<Minion> others = AllOtherMinions(c, s);
					foreach (Minion m in others) new SilenceTask(EntityType.TARGET).Process(g, c, s, m);
					foreach (Minion m in others) Generic.Destroy(m, s, many: true);
				})
			}));
			// [TIME_890t] Atiesh the Greatstaff (10, weapon) — Costs (0) if you control Medivh. Double the damage and healing of your spells.
			cards.Add("TIME_890t", new CardDef(new Power
			{
				Aura = new MultiAura(
					new AdaptiveCostEffect(p => 0, EffectOperator.SET, new SelfCondition(p => Controls(p, CardType.MINION))),
					new Aura(AuraType.CONTROLLER, new Effect(GameTag.SPELLPOWER_DOUBLE, EffectOperator.ADD, 1)))
			}));
			// [TIME_890t2] Karazhan the Sanctum (10, Location) — Costs (0) if you're wielding Atiesh. Summon two random 8-Cost minions.
			cards.Add("TIME_890t2", new CardDef(slot, new Power
			{
				Aura = new AdaptiveCostEffect(p => 0, EffectOperator.SET, new SelfCondition(p => Controls(p, CardType.WEAPON))),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("TIME_890t2", "minions", 2); i++) Summon(g, c, RandomMinionOfCost(g, V("TIME_890t2", "cost", 8)), s);
				})
			}));

			// ---------------------------------------------------------------- Rogue
			// [END_000] Eventuality (2) — Deal $2 damage. Imbue your Hero Power.
			cards.Add("END_000", new CardDef(TargetAny, new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("END_000", "damage", 2), EntityType.TARGET, true),
					new CustomTask((g, c, s, t, stack) => Generic.Imbue(c, s)))
			}));
			// [TIME_001] Chrono Daggers (3) — Rewind. Throw 3 knives at random enemies that deal $2 damage each. (each knife picks again among
			// the enemies still standing)
			cards.Add("TIME_001", new CardDef(new Power
			{
				PowerTask = RewindPower((g, c, s, t) =>
				{
					for (int i = 0; i < V("TIME_001", "knives", 3); i++)
					{
						var enemies = Enemies(c).Where(x => !x.IsDead && !x.ToBeDestroyed && x.Health > 0).ToList();
						if (enemies.Count == 0) break;
						Generic.DamageCharFunc((IPlayable)s, enemies[g.Random.Next(enemies.Count)], V("TIME_001", "damage", 2), true);
					}
				})
			}));
			// [TIME_036] Royal Informant (3) — Battlecry: Look at the right-most card in your opponent's hand. Either get a copy of it or
			// increase its Cost by (2). (the player picks: a copy — hand to hand, its enchantments kept — or Mitigate Threat, TIME_036t)
			cards.Add("TIME_036", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					IPlayable last = c.Opponent.HandZone.GetAll().OrderBy(p => p.ZonePosition).LastOrDefault();
					if (last == null) return;
					int lastId = last.Id;
					IPlayable copy = Generic.Copy(c, s, last, Zone.SETASIDE, keepEnchantments: true);
					copy[GameTag.CREATOR] = s.Id;
					copy[GameTag.LINKED_ENTITY] = lastId;
					IPlayable mitigate = Entity.FromCard(c, Cards.FromId("TIME_036t"), new EntityData { { GameTag.CREATOR, s.Id } }, c.SetasideZone);
					int copyId = copy.Id;
					Generic.CreateChoiceEntities(c, s, new List<IPlayable> { copy, mitigate }, ChoiceAction.STACK, new CustomTask((g2, c2, s2, pick, st) =>
					{
						if (pick == null) return;
						if (pick.Id == copyId)
						{
							if (pick.Zone?.Type == Zone.SETASIDE) Generic.AddHandPhase.Invoke(c2, pick.Zone.Remove(pick));
						}
						else if (g2.IdEntityDic.TryGetValue(lastId, out IPlayable target) && target.Zone?.Type == Zone.HAND && target.Controller != c2)
							Give(g2, s2, target, "TIME_036e");
					}));
				})
			}));
			cards.Add("TIME_036t", EngineOnly); // a choice Royal Informant resolves (never cast)
			cards.Add("TIME_036e", new CardDef(new Power { Enchant = new Enchant(Effects.AddCost(V("TIME_036", "cost", 2))) }));
			// [TIME_039] Deja Vu (1) — Discover a copy of a card in your opponent's hand.
			cards.Add("TIME_039", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.DiscoverCopyOf(c, s, c.Opponent.HandZone.GetAll()))
			}));
			// [TIME_710] Troubled Double (4) — Stealth. Combo: Summon a copy of this.
			cards.Add("TIME_710", new CardDef(new Power
			{
				ComboTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (s is Minion me && me.Zone?.Type == Zone.PLAY && !c.BoardZone.IsFull) Generic.Copy(c, s, me, Zone.PLAY);
				})
			}));
			// [TIME_711] Flashback (2) — Summon two random 1-Cost minions from the past. Combo: With +1 Attack.
			ISimpleTask Flashback(bool combo) => new CustomTask((g, c, s, t, stack) =>
			{
				for (int i = 0; i < V("TIME_711", "minions", 2); i++)
				{
					Card card = Pick(g, PastCards(g, c, x => x.Type == CardType.MINION && x.Cost == V("TIME_711", "cost", 1) && !x.NeverRandomlyOnBoard), 1).FirstOrDefault();
					if (SummonGet(g, c, card, s) is Minion m && combo) Give(g, s, m, "TIME_711e");
				}
			});
			cards.Add("TIME_711", new CardDef(new Power { PowerTask = Flashback(false), ComboTask = Flashback(true) }));
			cards.Add("TIME_711e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("TIME_711", "attack", 1))) }));
			// [TIME_712] Dethrone (7) — Destroy a minion. Combo: Summon a random 8-Cost minion. (live 204312: the summon before the deaths)
			ISimpleTask Dethrone(bool combo) => new CustomTask((g, c, s, t, stack) =>
			{
				if (t is Minion m) Generic.Destroy(m, s);
				if (combo) Summon(g, c, RandomMinionOfCost(g, V("TIME_712", "cost", 8)), s);
			});
			cards.Add("TIME_712", new CardDef(TargetMinion, new Power { PowerTask = Dethrone(false), ComboTask = Dethrone(true) }));
			// [TIME_713] Time Adm'ral Hooktail (5) — Battlecry: Summon a 0/8 Chest for your opponent. It's FULL of Coins!
			cards.Add("TIME_713", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => Summon(g, c.Opponent, Cards.FromId("TIME_713t"), s)) }));
			// [TIME_713t] Timeless Chest — Deathrattle: Fill your opponent's hand with Coins. (live 130515: Hooktail's owner gets them)
			cards.Add("TIME_713t", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int guard = 0; guard < 10 && !c.Opponent.HandZone.IsFull; guard++) AddToHand(c.Opponent, Cards.FromId("GAME_005"));
				})
			}));
			// [TIME_770] Fast Forward (4) — Draw 2 cards. Pick one to have its Cost reduced by (2). (the player picks among the drawn cards
			// that reached the hand; only one: it gets it)
			cards.Add("TIME_770", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var drawn = new List<IPlayable>();
					for (int i = 0; i < V("TIME_770", "draws", 2); i++)
						if (Generic.Draw(c) is IPlayable p && p.Zone?.Type == Zone.HAND && p.Controller == c) drawn.Add(p);
					string less = GenericCostLess(V("TIME_770", "less", 2)) ?? "GBL_002e";
					if (drawn.Count == 1) Give(g, s, drawn[0], less);
					else if (drawn.Count > 1)
						Generic.DiscoverFromCards(c, s, drawn, (g2, c2, s2, card) => { if (card.Zone?.Type == Zone.HAND) Give(g2, s2, card, less); });
				})
			}));
			// [TIME_875] Garona Halforcen (4) — Fabled. Battlecry: If your opponent is holding King Llane, destroy him and cut their Health in
			// half. (wiki: half of their current Health, the subtraction rounded up; no damage, Armor and maximum untouched; two Llanes: both
			// destroyed, halved once — doubt)
			cards.Add("TIME_875", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Card llane = Generic.FabledRelative(((IPlayable)s).Card, CardType.MINION);
					var held = c.Opponent.HandZone.Where(p => p.Card == llane).ToList();
					if (llane == null || held.Count == 0) return;
					foreach (IPlayable p in held) Generic.Destroy(p, s);
					Hero enemy = c.Opponent.Hero;
					enemy.Damage += (Math.Max(0, enemy.Health) + 1) / 2;
				})
			}));
			// [TIME_875t] King Llane (3) — Start of Game: Hide from Garona in the enemy's deck. Battlecry: Draw a card. Shuffle this back into
			// your deck. (live 130515 1464: a NEW Llane in the enemy deck — CREATOR / COPIED_FROM_ENTITY_ID = this; this one set aside)
			cards.Add("TIME_875t", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.GAME_START)
				{
					TriggerActivation = TriggerActivation.DECK,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is IPlayable me) || !Generic.StartOfGameHolds(me) || me[HsTag.COPIED_FROM_ENTITY_ID] > 0) return;
						if (g.History)
							g.PowerHistory.Add(new SabberStoneCore.Kettle.PowerHistoryMetaData { Type = MetaDataType.TARGET, Data = 0, Info = new List<int> { me.Id } });
						IPlayable hidden = Entity.FromCard(c.Opponent, me.Card, new EntityData
						{
							{ GameTag.CREATOR, me.Id }, { HsTag.COPIED_FROM_ENTITY_ID, me.Id }, { GameTag.CREATOR_DBID, me.Card.AssetId }, { (GameTag)1037, 2 }
						});
						Generic.ShuffleIntoDeck.Invoke(c.Opponent, me, hidden);
						me.Zone.Remove(me); // (its deck, or its opening hand)
						c.SetasideZone.Add(me);
					})
				},
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Generic.Draw(c);
					if (s is IPlayable me && me.Zone?.Type == Zone.PLAY) Generic.MoveToDeck(c, s, me);
				})
			}));
			// [TIME_875t1] The Kingslayers (2, weapon) — After your hero attacks, both players draw a Legendary card. (live 130515: you, then
			// your opponent)
			cards.Add("TIME_875t1", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						DrawOne(g, c, p => p.Card.Rarity == Rarity.LEGENDARY);
						DrawOne(g, c.Opponent, p => p.Card.Rarity == Rarity.LEGENDARY);
					})
				}
			}));
			// [TIME_876] Shapeshifter (1) — Each turn this is in your hand, transform into a random minion in your opponent's hand. (Shifter
			// Zerus' rule: Generic.ShiftInHand; no minion there: it stays)
			ISimpleTask shapeshift = new CustomTask((g, c, s, t, stack) => Generic.ShiftInHand(s, me =>
			{
				var minions = me.Controller.Opponent.HandZone.Where(p => Generic.IsMinionCard(p)).ToList();
				return minions.Count == 0 ? null : minions[g.Random.Next(minions.Count)].Card;
			}, Cards.FromId("TIME_876e")));
			cards.Add("TIME_876", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START) { TriggerActivation = TriggerActivation.HAND, SingleTask = shapeshift }
			}));
			cards.Add("TIME_876e", new CardDef(new Power
			{
				Enchant = new Enchant() { RemoveWhenPlayed = true },
				Trigger = new Trigger(TriggerType.TURN_START) { SingleTask = shapeshift }
			}));
		}
	}
}
