// HsServer addition (not upstream SabberStone): Forged in the Barrens (2021) and its Wailing Caverns miniset (WC_). Upstream
// had no script for the set. Every number goes through CardValues (V).
// Shared rules this set added: Frenzy (Frenzy helper: live — the TRIGGER block after the damage, FRENZY = 0 at its end),
// ranked spells (Generic.RankUp: the engine upgrades them; each rank here is its own card), Sigils (a player enchantment
// BAR_705e that casts the Sigil's effect at the start of its owner's next turn).
using System.Collections.Generic;
using System.Linq;
using SabberStoneCore.Actions;
using SabberStoneCore.Auras;
using SabberStoneCore.Conditions;
using SabberStoneCore.Enchants;
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;
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
		/// <summary>Frenzy (wiki: "A one-time effect after this survives damage"). Live (live-20261004-night, Blademaster
		/// Samuro): its TRIGGER block (TriggerKeyword FRENZY) follows the damage, before deaths; FRENZY goes to 0 at the end
		/// of the block. The damage the minion took is kept for the effect (Gold Road Grunt).</summary>
		private static Trigger Frenzy(ISimpleTask effect) => new Trigger(TriggerType.TAKE_DAMAGE)
		{
			TriggerSource = TriggerSource.SELF,
			RemoveAfterTriggered = true,
			Condition = new SelfCondition(p =>
			{
				if (!(p is Minion m) || m.Health <= 0 || m.ToBeDestroyed || m.Zone?.Type != SabberStoneCore.Enums.Zone.PLAY) return false;
				if (m.Game.CurrentEventData?.EventTarget == m && m.Game.CurrentEventData.EventNumber > 0)
					m[FRENZY_DAMAGE] = m.Game.CurrentEventData.EventNumber;
				return true;
			}),
			SingleTask = ComplexTask.Create(effect, new SetGameTagTask(HsTag.FRENZY, 0, EntityType.SOURCE))
		};
		private const GameTag FRENZY_DAMAGE = GameTag.TAG_SCRIPT_DATA_NUM_2;

		/// <summary>Sigils (client SIGIL): the spell sits in its owner's Secret zone, visible, and at the start of their next turn
		/// runs its effect and goes to the graveyard (Generic.SigilPower; live-20261007-vfx-decks 263109 / 266351; user 2026-10-07:
		/// "the same slots as secrets and/or auras … like Wildpaw Cavern"). Every Sigil script uses this; <paramref name="id"/>
		/// names the card for the reader. (Was: a BAR_705e player enchantment, which isn't what live does.)</summary>
		private static readonly Dictionary<string, ISimpleTask> SigilEffects = new Dictionary<string, ISimpleTask>();
		private static CardDef Sigil(string id, ISimpleTask effect) => new CardDef(Generic.SigilPower(effect));

		/// <summary>"A 2/2 Adventurer with a random bonus effect": one of WC_034t..t8 (the client's related cards).</summary>
		private static Card RandomAdventurer(Game g)
		{
			string[] ids = { "WC_034t", "WC_034t2", "WC_034t3", "WC_034t4", "WC_034t5", "WC_034t6", "WC_034t7", "WC_034t8" };
			return Cards.FromId(ids[g.Random.Next(ids.Length)]);
		}

		private static bool IsRaceCard(IPlayable p, Race race) => p is Minion m ? m.IsRace(race) : p?.Card.IsRace(race) == true;
		private static bool IsPoison(Card card) => card.Type == CardType.SPELL && card.Name.Contains("Poison");
		private static readonly SelfCondition IsNatureSpell = new SelfCondition(p => p is Spell && p[SPELL_SCHOOL] == NATURE);

		private static void Barrens(IDictionary<string, CardDef> cards)
		{
			cards.Add("BAR_705e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is Enchantment e && e.CapturedCard != null && SigilEffects.TryGetValue(e.CapturedCard.Id, out ISimpleTask effect))
							effect.Process(g, c, e.Creator ?? (IPlayable)s, null);
						RemoveEnchantmentTask.Task.Process(g, c, s, null);
					})
				}
			}));

			// ---------------------------------------------------------------- Ranked spells (one card per rank)
			foreach ((string id, int n) in new[] { ("BAR_034", 0), ("BAR_034t", 1), ("BAR_034t2", 2) })
			{
				// Tame Beast — Summon a 2/2 / 4/4 / 6/6 Beast with Rush.
				string beast = new[] { "BAR_034t3", "BAR_034t4", "BAR_034t5" }[n];
				cards.Add(id, new CardDef(new Power { PowerTask = new SummonTask(beast) }));
			}
			foreach ((string id, int dmg) in new[] { ("BAR_044", 2), ("BAR_044t", 3), ("BAR_044t2", 4) })
				// Chain Lightning — Deal $N damage to a minion and a random adjacent one.
				cards.Add(id, new CardDef(TargetMinion, new Power
				{
					PowerTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(t is Minion m)) return;
						var next = new List<Minion>();
						if (m.Zone is SabberStoneCore.Model.Zones.BoardZone board)
						{
							if (m.ZonePosition > 0) next.Add(board[m.ZonePosition - 1]);
							if (m.ZonePosition < board.Count - 1) next.Add(board[m.ZonePosition + 1]);
						}
						int amount = V(id, "damage", dmg);
						Generic.DamageCharFunc((IPlayable)s, m, amount, true);
						if (next.Count > 0) Generic.DamageCharFunc((IPlayable)s, next[g.Random.Next(next.Count)], amount, true);
					})
				}));
			foreach ((string id, int n) in new[] { ("BAR_305", 1), ("BAR_305t", 2), ("BAR_305t2", 3) })
				// Flurry — Freeze N random enemy minions.
				cards.Add(id, new CardDef(new Power
				{
					PowerTask = new CustomTask((g, c, s, t, stack) =>
					{
						// unfrozen ones first, then any
						int count = V(id, "minions", n);
						var picked = PickMinions(g, c.Opponent.BoardZone.Where(x => !x.IsFrozen), count);
						picked.AddRange(PickMinions(g, c.Opponent.BoardZone.Where(x => x.IsFrozen), count - picked.Count));
						foreach (Minion m in picked) m.IsFrozen = true;
					})
				}));
			foreach ((string id, int dmg) in new[] { ("BAR_314", 1), ("BAR_314t", 2), ("BAR_314t2", 3) })
				// Condemn — Deal $N damage to all enemy minions.
				cards.Add(id, new CardDef(new Power { PowerTask = new DamageTask(V(id, "damage", dmg), EntityType.OP_MINIONS, true) }));
			foreach ((string id, int dmg) in new[] { ("BAR_319", 2), ("BAR_319t", 4), ("BAR_319t2", 6) })
				// Wicked Stab — Deal $N damage.
				cards.Add(id, new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 } },
					new Power { PowerTask = new DamageTask(V(id, "damage", dmg), EntityType.TARGET, true) }));
			foreach ((string id, string ench) in new[] { ("BAR_536", "BAR_536e"), ("BAR_536t", "BAR_536te"), ("BAR_536t2", "BAR_536t2e") })
				// Living Seed — Draw a Beast. Reduce its Cost by (N).
				cards.Add(id, new CardDef(new Power
				{
					PowerTask = new CustomTask((g, c, s, t, stack) => { if (DrawOne(g, c, p => IsRaceCard(p, Race.BEAST)) is IPlayable beast) Give(g, s, beast, ench); })
				}));
			cards.Add("BAR_536e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("BAR_536", "cost", 1))) }));
			cards.Add("BAR_536te", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("BAR_536t", "cost", 2))) }));
			cards.Add("BAR_536t2e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("BAR_536t2", "cost", 3))) }));
			foreach ((string id, string ench, int n) in new[] { ("BAR_842", "BAR_842e", 1), ("BAR_842t", "BAR_842e2", 2), ("BAR_842t2", "BAR_842e3", 3) })
			{
				// Conditioning — Give minions in your hand +N/+N.
				cards.Add(id, new CardDef(new Power
				{
					PowerTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.HandZone.Where(p => Generic.IsMinionCard(p)).ToList(), ench))
				}));
				cards.Add(ench, new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V(id, "stats", n))) }));
			}
			foreach ((string id, int n) in new[] { ("BAR_880", 1), ("BAR_880t", 2), ("BAR_880t2", 3) })
				// Conviction — Give N random friendly minions +3 Attack.
				cards.Add(id, new CardDef(new Power
				{
					PowerTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, PickMinions(g, c.BoardZone, V(id, "minions", n)), "BAR_880e"))
				}));
			cards.Add("BAR_880e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("BAR_880", "attack", 3))) }));
			foreach ((string id, string ench, int n) in new[] { ("BAR_891", "BAR_891e", 2), ("BAR_891t", "BAR_891e2", 3), ("BAR_891t2", "BAR_891e3", 4) })
			{
				// Fury — Give your hero +N Attack this turn.
				cards.Add(id, new CardDef(new Power { PowerTask = new AddEnchantmentTask(ench, EntityType.HERO) }));
				cards.Add(ench, OneTurn(Effects.Attack_N(V(id, "attack", n))));
			}
			foreach ((string id, int n) in new[] { ("BAR_914", 1), ("BAR_914t", 2), ("BAR_914t2", 3) })
				// Imp Swarm — Summon N 3/2 Imps.
				cards.Add(id, new CardDef(new Power { PowerTask = ComplexTask.Repeat(new SummonTask("BAR_914t3"), V(id, "imps", n)) }));

			// ---------------------------------------------------------------- Frenzy
			// [BAR_020] Razormane Raider (5) — Frenzy: Attack a random enemy.
			cards.Add("BAR_020", new CardDef(new Power
			{
				Trigger = Frenzy(new CustomTask((g, c, s, t, stack) =>
				{
					var enemies = AttackableEnemies(c);
					if (s is Minion me && enemies.Count > 0) EffectAttack(g, me, enemies[g.Random.Next(enemies.Count)]);
				}))
			}));
			// [BAR_021] Gold Road Grunt (5) — Taunt. Frenzy: Gain Armor equal to the damage taken.
			cards.Add("BAR_021", new CardDef(new Power
			{
				Trigger = Frenzy(new CustomTask((g, c, s, t, stack) => { if (s is Minion me && me[FRENZY_DAMAGE] > 0) c.Hero.GainArmor((IPlayable)s, me[FRENZY_DAMAGE]); }))
			}));
			// [BAR_022] Peon (2) — Frenzy: Add a random spell from your class to your hand.
			cards.Add("BAR_022", new CardDef(new Power
			{
				Trigger = Frenzy(new CustomTask((g, c, s, t, stack) => { if (RandomCard(g, x => x.Type == CardType.SPELL && x.HasClass(c.HeroClass)) is Card card) AddToHand(c, card); }))
			}));
			// [BAR_024] Oasis Thrasher (2) — Frenzy: Deal 3 damage to the enemy hero.
			cards.Add("BAR_024", new CardDef(new Power { Trigger = Frenzy(new DamageTask(V("BAR_024", "damage", 3), EntityType.OP_HERO)) }));
			// [BAR_025] Sunwell Initiate (3) — Frenzy: Gain Divine Shield.
			cards.Add("BAR_025", new CardDef(new Power { Trigger = Frenzy(new SetGameTagTask(GameTag.DIVINE_SHIELD, 1, EntityType.SOURCE)) }));
			// [BAR_031] Sunscale Raptor (1) — Frenzy: Shuffle a Sunscale Raptor into your deck with permanent +2/+1. (The new
			// one carries this one's bonus plus +2/+1; no client enchantment for it — its stats.)
			cards.Add("BAR_031", new CardDef(new Power
			{
				Trigger = Frenzy(new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Minion me)) return;
					IPlayable raptor = Entity.FromCard(c, Cards.FromId("BAR_031"));
					raptor[GameTag.ATK] = me.Card.ATK + (me[RAPTOR_BONUS] + 1) * V("BAR_031", "attack", 2);
					raptor[GameTag.HEALTH] = me.Card.Health + (me[RAPTOR_BONUS] + 1) * V("BAR_031", "health", 1);
					raptor[RAPTOR_BONUS] = me[RAPTOR_BONUS] + 1;
					Generic.ShuffleIntoDeck.Invoke(c, s, raptor);
				}))
			}));
			// [BAR_070] Gruntled Patron (4) — Frenzy: Summon another Gruntled Patron.
			cards.Add("BAR_070", new CardDef(new Power { Trigger = Frenzy(new CustomTask((g, c, s, t, stack) => Summon(g, c, Cards.FromId("BAR_070"), s))) }));
			// [BAR_071] Taurajo Brave (6) — Frenzy: Destroy a random enemy minion.
			cards.Add("BAR_071", new CardDef(new Power
			{
				Trigger = Frenzy(new CustomTask((g, c, s, t, stack) =>
				{
					var enemies = c.Opponent.BoardZone.Where(m => !m.Untouchable && !m.ToBeDestroyed).ToList();
					if (enemies.Count > 0) enemies[g.Random.Next(enemies.Count)].Destroy();
				}))
			}));
			// [BAR_073] Barrens Blacksmith (5) — Frenzy: Give your other minions +2/+2.
			cards.Add("BAR_073", new CardDef(new Power
			{
				Trigger = Frenzy(new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.BoardZone.Where(m => m.Id != s.Id).ToList(), "BAR_073e")))
			}));
			cards.Add("BAR_073e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("BAR_073", "stats", 2))) }));
			// [BAR_078] Blademaster Samuro (4) — Rush. Frenzy: Deal damage equal to this minion's Attack to all enemy minions.
			cards.Add("BAR_078", new CardDef(new Power
			{
				Trigger = Frenzy(new CustomTask((g, c, s, t, stack) =>
				{
					if (s is Minion me)
						foreach (Minion m in c.Opponent.BoardZone.ToList()) Generic.DamageCharFunc(me, m, me.AttackDamage, false);
				}))
			}));
			// [BAR_320] Efficient Octo-bot (3) — Frenzy: Reduce the Cost of cards in your hand by (1).
			cards.Add("BAR_320", new CardDef(new Power { Trigger = Frenzy(new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.HandZone.ToList(), "BAR_320e"))) }));
			cards.Add("BAR_320e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("BAR_320", "cost", 1))) }));
			// [BAR_538] Druid of the Plains (7) — Rush. Frenzy: Transform into a 6/7 Kodo with Taunt.
			cards.Add("BAR_538", new CardDef(new Power { Trigger = Frenzy(new TransformTask("BAR_538t", EntityType.SOURCE)) }));
			// [BAR_840] Whirling Combatant (4) — Battlecry and Frenzy: Deal 1 damage to all other minions.
			cards.Add("BAR_840", new CardDef(new Power
			{
				PowerTask = new DamageTask(V("BAR_840", "damage", 1), EntityType.ALLMINIONS_NOSOURCE),
				Trigger = Frenzy(new DamageTask(V("BAR_840", "damage", 1), EntityType.ALLMINIONS_NOSOURCE))
			}));
			// [BAR_843] Warsong Envoy (1) — Frenzy: Gain +1 Attack for each damaged character. (No client enchantment: BAR_896e
			// "Increased Attack" is the set's own, used here.)
			cards.Add("BAR_843", new CardDef(new Power
			{
				Trigger = Frenzy(new CustomTask((g, c, s, t, stack) =>
				{
					int damaged = c.BoardZone.Concat(c.Opponent.BoardZone).Count(m => m.Damage > 0 && m.Health > 0) +
					              (c.Hero.Damage > 0 ? 1 : 0) + (c.Opponent.Hero.Damage > 0 ? 1 : 0);
					if (damaged > 0 && s is Minion me) Generic.AddEnchantmentBlock(g, Cards.FromId("BAR_896e"), me, me, damaged * V("BAR_843", "attack", 1), 0, 0);
				}))
			}));
			cards.Add("BAR_896e", new CardDef(new Power { Enchant = new Enchant(new Effect(GameTag.ATK, EffectOperator.ADD, 0)) { UseScriptTag = true } }));
			// [BAR_896] Stonemaul Anchorman (5) — Rush. Frenzy: Draw a card.
			cards.Add("BAR_896", new CardDef(new Power { Trigger = Frenzy(new DrawTask()) }));
			// [WC_008] Sin'dorei Scentfinder (4) — Frenzy: Summon four 1/1 Hyenas with Rush.
			cards.Add("WC_008", new CardDef(new Power { Trigger = Frenzy(ComplexTask.Repeat(new SummonTask("BAR_035t"), V("WC_008", "hyenas", 4))) }));
			// [WC_026] Kresh, Lord of Turtling (6) — Frenzy: Gain 8 Armor. Deathrattle: Equip a 2/5 Turtle Spike.
			cards.Add("WC_026", new CardDef(new Power
			{
				Trigger = Frenzy(new ArmorTask(V("WC_026", "armor", 8))),
				DeathrattleTask = new WeaponTask("WC_026t")
			}));

			// ---------------------------------------------------------------- Neutral
			// [BAR_026] Death's Head Cultist (3) — Taunt. Deathrattle: Restore 4 Health to your hero.
			cards.Add("BAR_026", new CardDef(new Power { DeathrattleTask = new HealTask(V("BAR_026", "heal", 4), EntityType.HERO) }));
			// [BAR_027] Darkspear Berserker (4) — Deathrattle: Deal 5 damage to your hero.
			cards.Add("BAR_027", new CardDef(new Power { DeathrattleTask = new DamageTask(V("BAR_027", "damage", 5), EntityType.HERO) }));
			// [BAR_042] Primordial Protector (8) — Battlecry: Draw your highest Cost spell. Summon a random minion with the same Cost.
			cards.Add("BAR_042", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var spells = c.DeckZone.Where(p => p is Spell).ToList();
					if (spells.Count == 0) return;
					int top = spells.Max(p => p.Cost);
					var highest = spells.Where(p => p.Cost == top).ToList();
					IPlayable drawn = Generic.Draw(c, highest[g.Random.Next(highest.Count)]);
					int cost = drawn?.Cost ?? top;
					Summon(g, c, RandomBoardMinion(g, x => x.Cost == cost), s);
				})
			}));
			// [BAR_060] Hog Rancher (3) — Battlecry: Summon a 2/1 Hog with Rush.
			cards.Add("BAR_060", new CardDef(new Power { PowerTask = new SummonTask("BAR_060t", SummonSide.RIGHT) }));
			// [BAR_061] Ratchet Privateer (3) — Battlecry: Give your weapon +1 Attack.
			cards.Add("BAR_061", new CardDef(new Power { PowerTask = new AddEnchantmentTask("BAR_061e", EntityType.WEAPON) }));
			cards.Add("BAR_061e", new CardDef(new Power { Enchant = new Enchant(new Effect(GameTag.ATK, EffectOperator.ADD, V("BAR_061", "attack", 1))) }));
			// [BAR_062] Lushwater Murcenary (2) — Battlecry: If you control a Murloc, gain +1/+1.
			cards.Add("BAR_062", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c.BoardZone.Any(m => m.Id != s.Id && m.IsRace(Race.MURLOC)), new AddEnchantmentTask("BAR_062e", EntityType.SOURCE))
			}));
			cards.Add("BAR_062e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("BAR_062", "stats", 1))) }));
			// [BAR_063] Lushwater Scout (2) — After you summon a Murloc, give it +1 Attack and Rush.
			cards.Add("BAR_063", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_SUMMON)
				{
					TriggerSource = TriggerSource.MINIONS_EXCEPT_SELF,
					Condition = new SelfCondition(p => p is Minion m && m.IsRace(Race.MURLOC)),
					SingleTask = new AddEnchantmentTask("BAR_063e", EntityType.TARGET)
				}
			}));
			cards.Add("BAR_063e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("BAR_063", "attack", 1)), new Effect(GameTag.RUSH, EffectOperator.SET, 1)) }));
			// [BAR_065] Venomous Scorpid (3) — Poisonous. Battlecry: Discover a spell.
			cards.Add("BAR_065", new CardDef(new Power { PowerTask = new DiscoverTask(CardType.SPELL) }));
			// [BAR_069] Injured Marauder (4) — Taunt. Battlecry: Deal 6 damage to this minion.
			cards.Add("BAR_069", new CardDef(new Power { PowerTask = new DamageTask(V("BAR_069", "damage", 6), EntityType.SOURCE) }));
			// [BAR_072] Burning Blade Acolyte (5) — Deathrattle: Summon a 5/8 Demonspawn with Taunt.
			cards.Add("BAR_072", new CardDef(new Power { DeathrattleTask = new SummonTask("BAR_072t") }));
			// [BAR_074] Far Watch Post (2) — Can't attack. After your opponent draws a card, it costs (1) more (up to 10).
			cards.Add("BAR_074", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DRAW)
				{
					TriggerSource = TriggerSource.ENEMY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						IPlayable drawn = t ?? g.CurrentEventData?.EventSource as IPlayable;
						if (drawn?.Zone?.Type == SabberStoneCore.Enums.Zone.HAND && drawn.Cost < V("BAR_074", "max", 10)) Give(g, s, drawn, "BAR_074e");
					})
				}
			}));
			cards.Add("BAR_074e", new CardDef(new Power { Enchant = new Enchant(Effects.AddCost(V("BAR_074", "cost", 1))) }));
			// [BAR_075] Crossroads Watch Post (4) — Can't attack. Whenever your opponent casts a spell, give your minions +1/+1.
			cards.Add("BAR_075", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.CAST_SPELL)
				{
					TriggerSource = TriggerSource.ENEMY,
					SingleTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.BoardZone.ToList(), "BAR_075e"))
				}
			}));
			cards.Add("BAR_075e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("BAR_075", "stats", 1))) }));
			// [BAR_076] Mor'shan Watch Post (3) — Can't attack. After your opponent plays a minion, summon a 2/2 Grunt.
			cards.Add("BAR_076", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_MINION) { TriggerSource = TriggerSource.ENEMY, SingleTask = new SummonTask("BAR_076t", SummonSide.RIGHT) }
			}));
			// [BAR_082] Barrens Trapper (3) — Your Deathrattle cards cost (1) less.
			cards.Add("BAR_082", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HAND, Effects.ReduceCost(V("BAR_082", "cost", 1))) { Condition = new SelfCondition(p => p.Card[GameTag.DEATHRATTLE] > 0), ShowAs = "BAR_082e" }
			}));
			// [BAR_430] Horde Operative (3) — Battlecry: Copy your opponent's Secrets and put them into play.
			cards.Add("BAR_430", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Spell secret in Generic.SecretsOf(c.Opponent))
						if (!c.SecretZone.IsFull && !c.SecretZone.Any(x => x.Card.Id == secret.Card.Id))
							PutSecretIntoPlay(c, (Spell)Entity.FromCard(c, secret.Card));
				})
			}));
			// [BAR_743] Toad of the Wilds (2) — Taunt. Battlecry: If you're holding a Nature spell, gain +2 Health.
			cards.Add("BAR_743", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c.HandZone.Any(p => IsNatureSpell.Eval(p)), new AddEnchantmentTask("BAR_743e", EntityType.SOURCE))
			}));
			cards.Add("BAR_743e", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(V("BAR_743", "health", 2))) }));
			// [BAR_744] Spirit Healer (4) — After you cast a Holy spell, give a random friendly minion +2 Health.
			cards.Add("BAR_744", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = IsSchool(HOLY),
					SingleTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, PickMinions(g, c.BoardZone, 1), "BAR_744e"))
				}
			}));
			cards.Add("BAR_744e", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(V("BAR_744", "health", 2))) }));
			// [BAR_745] Hecklefang Hyena (2) — Battlecry: Deal 3 damage to your hero.
			cards.Add("BAR_745", new CardDef(new Power { PowerTask = new DamageTask(V("BAR_745", "damage", 3), EntityType.HERO) }));
			// [BAR_854] Kindling Elemental (1) — Battlecry: The next Elemental you play costs (1) less.
			cards.Add("BAR_854", new CardDef(new Power { PowerTask = new AddEnchantmentTask("BAR_854e", EntityType.CONTROLLER) }));
			cards.Add("BAR_854e", NextCardEffect(Effects.ReduceCost(V("BAR_854", "cost", 1)), new SelfCondition(p => IsRaceCard(p, Race.ELEMENTAL)), false));
			// [WC_027] Devouring Ectoplasm (3) — Deathrattle: Summon a 2/2 Adventurer with a random bonus effect.
			cards.Add("WC_027", new CardDef(new Power { DeathrattleTask = new CustomTask((g, c, s, t, stack) => Summon(g, c, RandomAdventurer(g), s)) }));
			// [WC_028] Meeting Stone (1) — At the end of your turn, add a 2/2 Adventurer with a random bonus effect to your hand.
			cards.Add("WC_028", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = new CustomTask((g, c, s, t, stack) => AddToHand(c, RandomAdventurer(g))) }
			}));
			// [WC_029] Selfless Sidekick (7) — Battlecry: Equip a random weapon from your deck.
			cards.Add("WC_029", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.EquipFromDeck(c))
			}));

			// ---------------------------------------------------------------- Demon Hunter
			// [BAR_306] Sigil of Flame (2) — At the start of your next turn, deal $3 damage to all enemy minions.
			cards.Add("BAR_306", Sigil("BAR_306", new DamageTask(V("BAR_306", "damage", 3), EntityType.OP_MINIONS, true)));
			// [BAR_705] Sigil of Silence (0) — At the start of your next turn, Silence all enemy minions.
			cards.Add("BAR_705", Sigil("BAR_705", new SilenceTask(EntityType.OP_MINIONS)));
			// [WC_003] Sigil of Summoning (2) — At the start of your next turn, summon two 2/2 Demons with Taunt.
			cards.Add("WC_003", Sigil("WC_003", ComplexTask.Repeat(new SummonTask("WC_003t"), V("WC_003", "demons", 2))));
			// [BAR_325] Razorboar (2) — Deathrattle: Summon a Deathrattle minion that costs (3) or less from your hand.
			// [BAR_326] Razorfen Beastmaster (3) — the same, (4) or less.
			foreach ((string id, int max) in new[] { ("BAR_325", 3), ("BAR_326", 4) })
				cards.Add(id, new CardDef(new Power
				{
					DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
					{
						var pool = c.HandZone.OfType<Minion>().Where(m => Generic.IsMinionCard(m)).Where(m => m.Card[GameTag.DEATHRATTLE] > 0 && m.Cost <= V(id, "cost", max)).ToList();
						if (pool.Count > 0) Generic.SummonFromHand(c, pool[g.Random.Next(pool.Count)], s);
					})
				}));
			// [BAR_327] Vile Call (3) — Summon two 2/2 Demons with Lifesteal.
			cards.Add("BAR_327", new CardDef(new Power { PowerTask = ComplexTask.Repeat(new SummonTask("BAR_327t"), V("BAR_327", "demons", 2)) }));
			// [BAR_328] Vengeful Spirit (4) — Outcast: Draw 2 Deathrattle minions.
			cards.Add("BAR_328", new CardDef(new Power
			{
				PowerTask = ComplexTask.Conditional(PlayedAsOutcast, new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("BAR_328", "draw", 2); i++) DrawOne(g, c, p => p is Minion && p.Card[GameTag.DEATHRATTLE] > 0);
				}))
			}));
			// [BAR_329] Death Speaker Blackthorn (7) — Battlecry: Summon 3 Deathrattle minions that cost (5) or less from your deck.
			cards.Add("BAR_329", new CardDef(new Power
			{
				PowerTask = new RecruitTask(V("BAR_329", "minions", 3), new SelfCondition(p => p.Card[GameTag.DEATHRATTLE] > 0 && p.Cost <= V("BAR_329", "cost", 5)))
			}));
			// [BAR_333] Kurtrus Ashfallen (4) — Battlecry: Attack the left and right-most enemy minions. Outcast: Immune this turn.
			cards.Add("BAR_333", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Minion me)) return;
					if (PlayedAsOutcast.Eval(me)) Give(g, me, me, "BAR_333e");
					var enemies = c.Opponent.BoardZone.Where(m => !m.Untouchable).ToList();
					if (enemies.Count == 0) return;
					Minion left = enemies[0], right = enemies[enemies.Count - 1];
					EffectAttack(g, me, left);
					if (right != left && right.Zone?.Type == SabberStoneCore.Enums.Zone.PLAY && me.Zone?.Type == SabberStoneCore.Enums.Zone.PLAY && !me.ToBeDestroyed)
						EffectAttack(g, me, right);
				})
			}));
			cards.Add("BAR_333e", OneTurn(Effects.Immune));
			// [WC_040] Taintheart Tormenter (8) — Taunt. Your opponent's spells cost (2) more.
			cards.Add("WC_040", new CardDef(new Power
			{
				Aura = new Aura(AuraType.OP_HAND, Effects.AddCost(V("WC_040", "cost", 2))) { Condition = SelfCondition.IsSpell, ShowAs = "WC_040e" }
			}));

			// ---------------------------------------------------------------- Druid
			// [BAR_533] Thorngrowth Sentries (2) — Summon two 1/2 Turtles with Taunt.
			cards.Add("BAR_533", new CardDef(new Power { PowerTask = ComplexTask.Repeat(new SummonTask("BAR_533t"), V("BAR_533", "turtles", 2)) }));
			// [BAR_534] Pride's Fury (4) — Give your minions +1/+3.
			cards.Add("BAR_534", new CardDef(new Power { PowerTask = new AddEnchantmentTask("BAR_534e", EntityType.MINIONS) }));
			cards.Add("BAR_534e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("BAR_534", "attack", 1)), Effects.Health_N(V("BAR_534", "health", 3))) }));
			// [BAR_540] Plaguemaw the Rotting (4) — After a friendly minion with Taunt dies, summon a new copy of it without Taunt.
			cards.Add("BAR_540", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DEATH)
				{
					TriggerSource = TriggerSource.MINIONS_EXCEPT_SELF,
					Condition = new SelfCondition(p => p is Minion m && m.HasTaunt),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						IPlayable dead = t ?? g.CurrentEventData?.EventSource as IPlayable;
						if (dead == null || c.BoardZone.IsFull) return;
						var copy = (Minion)Entity.FromCard(c, dead.Card);
						Generic.SummonBlock.Invoke(g, copy, -1, s);
						if (copy.Zone?.Type == SabberStoneCore.Enums.Zone.PLAY) copy.HasTaunt = false;
					})
				}
			}));
			// [BAR_549] Mark of the Spikeshell (2) — Give a minion +2/+2. If it has Taunt, add a copy of it to your hand.
			cards.Add("BAR_549", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					Give(g, s, m, "BAR_549e");
					if (m.HasTaunt) AddToHand(c, m.Card);
				})
			}));
			cards.Add("BAR_549e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("BAR_549", "stats", 2))) }));
			// [BAR_720] Guff Runetotem (3) — After you cast a Nature spell, give another friendly minion +2/+2.
			cards.Add("BAR_720", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = IsSchool(NATURE),
					SingleTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, PickMinions(g, c.BoardZone.Where(m => m.Id != s.Id), 1), "BAR_720e"))
				}
			}));
			cards.Add("BAR_720e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("BAR_720", "stats", 2))) }));
			// [WC_004] Fangbound Druid (3) — Taunt. Deathrattle: Reduce the Cost of a Beast in your hand by (2).
			cards.Add("WC_004", new CardDef(new Power
			{
				DeathrattleTask = BuffRandomInHand(p => IsRaceCard(p, Race.BEAST), "WC_004t")
			}));
			cards.Add("WC_004t", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("WC_004", "cost", 2))) }));
			// [WC_006] Lady Anacondra (6) — Your Nature spells cost (2) less.
			cards.Add("WC_006", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HAND, Effects.ReduceCost(V("WC_006", "cost", 2))) { Condition = IsNatureSpell, ShowAs = "WC_006e" }
			}));
			// [WC_036] Deviate Dreadfang (8) — After you cast a Nature spell, summon a 4/2 Viper with Rush.
			cards.Add("WC_036", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST) { TriggerSource = TriggerSource.FRIENDLY, Condition = IsSchool(NATURE), SingleTask = new SummonTask("WC_036t1", SummonSide.RIGHT) }
			}));

			// ---------------------------------------------------------------- Hunter
			// [BAR_030] Pack Kodo (3) — Battlecry: Discover a Beast, Secret, or weapon.
			cards.Add("BAR_030", new CardDef(new Power
			{
				PowerTask = DiscoverFromTask.Where((c, x) => x.Type == CardType.WEAPON || x.IsSecret || x.Type == CardType.MINION && x.IsRace(Race.BEAST))
			}));
			// [BAR_032] Piercing Shot (4) — Deal $6 damage to a minion. Excess damage hits the enemy hero.
			cards.Add("BAR_032", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					// (WHIZBANG: the shared excess hit — capped at its Health, live vfx 276716; worked out before the hit, forum t/70422)
					int excess = Generic.DamageWithExcess((IPlayable)s, m, V("BAR_032", "damage", 6), true);
					if (excess > 0) Generic.DamageCharFunc((IPlayable)s, c.Opponent.Hero, excess, false);
				})
			}));
			// [BAR_033] Prospector's Caravan (2) — At the start of your turn, give all minions in your hand +1/+1.
			cards.Add("BAR_033", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START) { SingleTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.HandZone.Where(p => Generic.IsMinionCard(p)).ToList(), "BAR_033e")) }
			}));
			cards.Add("BAR_033e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("BAR_033", "stats", 1))) }));
			// [BAR_035] Kolkar Pack Runner (2) — After you cast a spell, summon a 1/1 Hyena with Rush.
			cards.Add("BAR_035", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST) { TriggerSource = TriggerSource.FRIENDLY, SingleTask = new SummonTask("BAR_035t", SummonSide.RIGHT) }
			}));
			// [WC_007] Serpentbloom (0) — Give a friendly Beast Poisonous.
			cards.Add("WC_007", new CardDef(new Dictionary<PlayReq, int>
				{ { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 }, { PlayReq.REQ_TARGET_WITH_RACE, (int)Race.BEAST } },
				new Power { PowerTask = new AddEnchantmentTask("WC_007e", EntityType.TARGET) }));
			cards.Add("WC_007e", new CardDef(new Power { Enchant = new Enchant(new Effect(GameTag.POISONOUS, EffectOperator.SET, 1)) }));

			// ---------------------------------------------------------------- Mage
			// [BAR_542] Refreshing Spring Water (5) — Draw 2 cards. Refresh 2 Mana Crystals for each spell drawn.
			cards.Add("BAR_542", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("BAR_542", "draw", 2); i++)
						if (Generic.Draw(c) is Spell)
							c.UsedMana = System.Math.Max(0, c.UsedMana - V("BAR_542", "mana", 2));
				})
			}));
			// [BAR_748] Varden Dawngrasp (4) — Battlecry: Freeze all enemy minions. If any are already Frozen, deal 4 damage to them instead.
			cards.Add("BAR_748", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in c.Opponent.BoardZone.Where(x => !x.Untouchable).ToList())
						if (m.IsFrozen) Generic.DamageCharFunc((IPlayable)s, m, V("BAR_748", "damage", 4), false);
						else m.IsFrozen = true;
				})
			}));
			// [BAR_888] Rimetongue (3) — After you cast a Frost spell, summon a 1/1 Elemental that Freezes.
			cards.Add("BAR_888", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST) { TriggerSource = TriggerSource.FRIENDLY, Condition = IsSchool(FROST), SingleTask = new SummonTask("BAR_888t", SummonSide.RIGHT) }
			}));
			// [WC_041] Shattering Blast (3) — Destroy all Frozen minions.
			cards.Add("WC_041", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in c.BoardZone.Concat(c.Opponent.BoardZone).Where(m => m.IsFrozen && !m.Untouchable).ToList()) m.Destroy();
				})
			}));
			// [WC_805] Frostweave Dungeoneer (3) — Battlecry: Draw a spell. If it's a Frost spell, summon two 1/1 Elementals that Freeze.
			cards.Add("WC_805", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (DrawOne(g, c, p => p is Spell) is IPlayable spell && spell[SPELL_SCHOOL] == FROST)
						for (int i = 0; i < V("WC_805", "elementals", 2); i++) Summon(g, c, Cards.FromId("BAR_888t"), s);
				})
			}));
			// [WC_806] Floecaster (6) — Costs (2) less for each Frozen enemy.
			cards.Add("WC_806", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => V("WC_806", "cost", 2) * (p.Controller.Opponent.BoardZone.Count(m => m.IsFrozen) + (p.Controller.Opponent.Hero.IsFrozen ? 1 : 0)))
			}));

			// ---------------------------------------------------------------- Paladin
			// [BAR_550] Galloping Savior (1) — Secret: After your opponent plays three cards in a turn, summon a 3/4 Steed with Taunt.
			cards.Add("BAR_550", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_CARD)
				{
					TriggerSource = TriggerSource.ENEMY,
					Condition = new SelfCondition(p => p.Controller.NumCardsPlayedThisTurn == V("BAR_550", "cards", 3)),
					SingleTask = ComplexTask.Secret(new SummonTask("BAR_550t"))
				}
			}));
			// [BAR_871] Soldier's Caravan (2) — At the start of your turn, summon two 1/1 Silver Hand Recruits.
			cards.Add("BAR_871", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START) { SingleTask = ComplexTask.Repeat(new SummonTask("CS2_101t", SummonSide.RIGHT), V("BAR_871", "recruits", 2)) }
			}));
			// [BAR_873] Knight of Anointment (1) — Battlecry: Draw a Holy spell.
			cards.Add("BAR_873", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => DrawOne(g, c, p => p is Spell && p[SPELL_SCHOOL] == HOLY)) }));
			// [BAR_875] Sword of the Fallen (2) — After your hero attacks, cast a Secret from your deck.
			cards.Add("BAR_875", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						var secrets = c.DeckZone.OfType<Spell>().Where(x => x.IsSecret && !c.SecretZone.Any(z => z.Card.Id == x.Card.Id)).ToList();
						if (secrets.Count > 0 && !c.SecretZone.IsFull) Generic.CastFromDeck(c, s, secrets[g.Random.Next(secrets.Count)]);
					})
				}
			}));
			// [BAR_876] Northwatch Commander (3) — Battlecry: If you control a Secret, draw a minion.
			cards.Add("BAR_876", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => Generic.ControlsSecret(c), new CustomTask((g, c, s, t, stack) => DrawOne(g, c, p => p is Minion)))
			}));
			// [BAR_881] Invigorating Sermon (4) — Give +1/+1 to all minions in your hand, deck, and battlefield.
			cards.Add("BAR_881", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					GiveAll(g, s, c.HandZone.Concat(c.DeckZone).Concat(c.BoardZone).Where(p => Generic.IsMinionCard(p)).ToList(), "BAR_881e"))
			}));
			cards.Add("BAR_881e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("BAR_881", "stats", 1))) }));
			// [BAR_890] Crossroads Gossiper (3) — After a friendly Secret is revealed, gain +2/+2.
			cards.Add("BAR_890", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.SECRET_REVEALED) { TriggerSource = TriggerSource.FRIENDLY, SingleTask = new AddEnchantmentTask("BAR_890e", EntityType.SOURCE) }
			}));
			cards.Add("BAR_890e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("BAR_890", "stats", 2))) }));
			// [BAR_902] Cariel Roame (4) — Rush, Divine Shield. Whenever this attacks, reduce the Cost of Holy spells in your hand by (1).
			cards.Add("BAR_902", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ATTACK)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.HandZone.Where(p => p is Spell && p[SPELL_SCHOOL] == HOLY).ToList(), "BAR_902e"))
				}
			}));
			cards.Add("BAR_902e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("BAR_902", "cost", 1))) }));
			// [WC_032] Seedcloud Buckler (3) — Deathrattle: Give your minions Divine Shield.
			cards.Add("WC_032", new CardDef(new Power { DeathrattleTask = new SetGameTagTask(GameTag.DIVINE_SHIELD, 1, EntityType.MINIONS) }));
			// [WC_033] Judgment of Justice (1) — Secret: When an enemy minion attacks, set its Attack and Health to 1.
			cards.Add("WC_033", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ATTACK)
				{
					TriggerSource = TriggerSource.OP_MINIONS,
					SingleTask = ComplexTask.Secret(new CustomTask((g, c, s, t, stack) =>
					{
						if (g.CurrentEventData?.EventSource is Minion attacker) Give(g, s, attacker, "WC_033e");
					}))
				}
			}));
			cards.Add("WC_033e", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttackHealth(V("WC_033", "stats", 1))) }));
			// [WC_034] Party Up! (7) — Summon five 2/2 Adventurers with random bonus effects.
			cards.Add("WC_034", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { for (int i = 0; i < V("WC_034", "adventurers", 5); i++) Summon(g, c, RandomAdventurer(g), s); })
			}));

			// ---------------------------------------------------------------- Priest
			// [BAR_307] Void Flayer (4) — Battlecry: For each spell in your hand, deal 1 damage to a random enemy minion.
			cards.Add("BAR_307", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = c.HandZone.Count(p => p is Spell);
					for (int i = 0; i < n; i++)
					{
						var enemies = c.Opponent.BoardZone.Where(m => !m.Untouchable && m.Health > 0).ToList();
						if (enemies.Count == 0) break;
						Generic.DamageCharFunc((IPlayable)s, enemies[g.Random.Next(enemies.Count)], V("BAR_307", "damage", 1), false);
					}
				})
			}));
			// [BAR_308] Power Word: Fortitude (8) — Give a minion +3/+5. Costs (1) less for each spell in your hand.
			cards.Add("BAR_308", new CardDef(TargetMinion, new Power
			{
				PowerTask = new AddEnchantmentTask("BAR_308e", EntityType.TARGET),
				Aura = new AdaptiveCostEffect(p => p.Controller.HandZone.Count(x => x is Spell && x.Id != p.Id))
			}));
			cards.Add("BAR_308e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("BAR_308", "attack", 3)), Effects.Health_N(V("BAR_308", "health", 5))) }));
			// [BAR_309] Desperate Prayer (0) — Restore 5 Health to each hero.
			cards.Add("BAR_309", new CardDef(new Power { PowerTask = new HealTask(V("BAR_309", "heal", 5), EntityType.HEROES) }));
			// [BAR_312] Soothsayer's Caravan (2) — At the start of your turn, copy a spell from your opponent's deck to your hand.
			cards.Add("BAR_312", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						var spells = c.Opponent.DeckZone.Where(p => p is Spell).ToList();
						if (spells.Count > 0) AddToHand(c, spells[g.Random.Next(spells.Count)].Card);
					})
				}
			}));
			// [WC_013] Devout Dungeoneer (3) — Battlecry: Draw a spell. If it's a Holy spell, reduce its Cost by (2).
			cards.Add("WC_013", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (DrawOne(g, c, p => p is Spell) is IPlayable spell && spell[SPELL_SCHOOL] == HOLY) Give(g, s, spell, "WC_013e"); })
			}));
			cards.Add("WC_013e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("WC_013", "cost", 2))) }));
			// [WC_014] Against All Odds (5) — Destroy ALL odd-Attack minions.
			cards.Add("WC_014", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in c.BoardZone.Concat(c.Opponent.BoardZone).Where(m => m.AttackDamage % 2 == 1 && !m.Untouchable).ToList()) m.Destroy();
				})
			}));

			// ---------------------------------------------------------------- Rogue
			// [BAR_316] Oil Rig Ambusher (4) — Battlecry: Deal 2 damage. If this entered your hand this turn, deal 4 instead.
			cards.Add("BAR_316", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_IF_AVAILABLE, 0 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is ICharacter target)
						Generic.DamageCharFunc((IPlayable)s, target, s[HsTag.QUICKDRAW_PLAY] > 0 ? V("BAR_316", "fresh", 4) : V("BAR_316", "damage", 2), false);
				})
			}));
			// [BAR_317] Field Contact (3) — After you play a Battlecry or Combo card, draw a card.
			cards.Add("BAR_317", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_CARD)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p.Card[GameTag.BATTLECRY] > 0 || p.Card[GameTag.COMBO] > 0),
					SingleTask = new DrawTask()
				}
			}));
			// [BAR_318] Silverleaf Poison (1) — Give your weapon "After your hero attacks, draw a card."
			cards.Add("BAR_318", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_WEAPON_EQUIPPED, 0 } }, new Power { PowerTask = new AddEnchantmentTask("BAR_318e", EntityType.WEAPON) }));
			cards.Add("BAR_318e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK) { TriggerSource = TriggerSource.HERO, SingleTask = new DrawTask() }
			}));
			// [BAR_321] Paralytic Poison (1) — Give your weapon +1 Attack and "Your hero is Immune while attacking."
			cards.Add("BAR_321", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_WEAPON_EQUIPPED, 0 } }, new Power { PowerTask = new AddEnchantmentTask("BAR_321e", EntityType.WEAPON) }));
			cards.Add("BAR_321e", new CardDef(new Power
			{
				Enchant = new Enchant(new Effect(GameTag.ATK, EffectOperator.ADD, V("BAR_321", "attack", 1)), new Effect(GameTag.IMMUNE_WHILE_ATTACKING, EffectOperator.SET, 1))
			}));
			// [BAR_322] Swinetusk Shank (3) — After you play a Poison, gain +1 Durability.
			cards.Add("BAR_322", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_CARD)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => IsPoison(p.Card)),
					SingleTask = new AddEnchantmentTask("BAR_322e", EntityType.SOURCE)
				}
			}));
			cards.Add("BAR_322e", new CardDef(new Power { Enchant = new Enchant(new Effect(GameTag.DURABILITY, EffectOperator.ADD, V("BAR_322", "durability", 1))) }));
			// [BAR_324] Apothecary Helbrim (4) — Battlecry and Deathrattle: Add a random Poison to your hand.
			ISimpleTask addPoison = new CustomTask((g, c, s, t, stack) => { if (RandomCard(g, IsPoison) is Card poison) AddToHand(c, poison); });
			cards.Add("BAR_324", new CardDef(new Power { PowerTask = addPoison, DeathrattleTask = addPoison }));
			// [WC_015] Water Moccasin (3) — Stealth. Has Poisonous while you have no other minions.
			cards.Add("WC_015", new CardDef(new Power
			{
				Aura = new Aura(AuraType.SELF, new Effect(GameTag.POISONOUS, EffectOperator.SET, 1))
				{
					Condition = new SelfCondition(p => p.Controller.BoardZone.Count == 1), Restless = true
				}
			}));

			// ---------------------------------------------------------------- Shaman
			// [BAR_040] South Coast Chieftain (2) — Battlecry: If you control another Murloc, deal 2 damage.
			// (its target only while another Murloc is in play: REQ_TARGET_IF_AVAILABLE_AND_FRIENDLY_RACE, the shared rule — the card
			// glows then; TITANS 2026-10-07)
			cards.Add("BAR_040", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_IF_AVAILABLE_AND_FRIENDLY_RACE, (int)Race.MURLOC } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is ICharacter target && c.BoardZone.Any(m => m.Id != s.Id && m.IsRace(Race.MURLOC)))
						Generic.DamageCharFunc((IPlayable)s, target, V("BAR_040", "damage", 2), false);
				})
			}));
			// [BAR_041] Nofin Can Stop Us (3) — Give your minions +1/+1. Give your Murlocs an extra +1/+1.
			cards.Add("BAR_041", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in c.BoardZone.ToList()) Give(g, s, m, m.IsRace(Race.MURLOC) ? "BAR_041e2" : "BAR_041e");
				})
			}));
			cards.Add("BAR_041e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("BAR_041", "stats", 1))) }));
			cards.Add("BAR_041e2", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("BAR_041", "murlocs", 2))) }));
			// [BAR_043] Tinyfin's Caravan (2) — At the start of your turn, draw a Murloc.
			cards.Add("BAR_043", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START) { SingleTask = new CustomTask((g, c, s, t, stack) => DrawOne(g, c, p => IsRaceCard(p, Race.MURLOC))) }
			}));
			// [BAR_045] Arid Stormer (3) — Battlecry: If you played an Elemental last turn, gain Rush and Windfury.
			cards.Add("BAR_045", new CardDef(new Power
			{
				PowerTask = ComplexTask.Conditional(SelfCondition.ElementalPlayedLastTurn, new AddEnchantmentTask("BAR_045e", EntityType.SOURCE))
			}));
			cards.Add("BAR_045e", new CardDef(new Power { Enchant = new Enchant(new Effect(GameTag.RUSH, EffectOperator.SET, 1), Effects.Windfury) }));
			// [BAR_750] Earth Revenant (4) — Taunt. Battlecry: Deal 1 damage to all enemy minions.
			cards.Add("BAR_750", new CardDef(new Power { PowerTask = new DamageTask(V("BAR_750", "damage", 1), EntityType.OP_MINIONS) }));
			// [BAR_751] Spawnpool Forager (1) — Deathrattle: Summon a 1/1 Tinyfin.
			cards.Add("BAR_751", new CardDef(new Power { DeathrattleTask = new SummonTask("BAR_751t") }));
			// [BAR_848] Lilypad Lurker (5) — Battlecry: If you played an Elemental last turn, transform an enemy minion into a 0/1 Frog with Taunt.
			cards.Add("BAR_848", new CardDef(new Dictionary<PlayReq, int>
				{ { PlayReq.REQ_TARGET_IF_AVAILABE_AND_ELEMENTAL_PLAYED_LAST_TURN, 0 }, { PlayReq.REQ_ENEMY_TARGET, 0 }, { PlayReq.REQ_MINION_TARGET, 0 } }, new Power
			{
				PowerTask = ComplexTask.Conditional(SelfCondition.ElementalPlayedLastTurn, new TransformTask("hexfrog", EntityType.TARGET))
			}));
			// [BAR_860] Firemancer Flurgl (4) — After you play a Murloc, deal 1 damage to all enemies.
			cards.Add("BAR_860", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_MINION)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p is Minion m && m.IsRace(Race.MURLOC)),
					SingleTask = new DamageTask(V("BAR_860", "damage", 1), EntityType.ENEMIES)
				}
			}));
			// [WC_005] Primal Dungeoneer (3) — Battlecry: Draw a spell. If it's a Nature spell, also draw an Elemental.
			cards.Add("WC_005", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (DrawOne(g, c, p => p is Spell) is IPlayable spell && spell[SPELL_SCHOOL] == NATURE) DrawOne(g, c, p => IsRaceCard(p, Race.ELEMENTAL));
				})
			}));
			// [WC_020] Perpetual Flame (2) — Deal $3 damage to a random enemy minion. If it dies, recast this. Overload: (1)
			cards.Add("WC_020", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int guard = 0; guard < 30; guard++)
					{
						var enemies = c.Opponent.BoardZone.Where(m => !m.Untouchable && m.Health > 0 && !m.ToBeDestroyed).ToList();
						if (enemies.Count == 0) return;
						Minion m = enemies[g.Random.Next(enemies.Count)];
						Generic.DamageCharFunc((IPlayable)s, m, V("WC_020", "damage", 3), true);
						if (m.Health > 0 && !m.ToBeDestroyed) return;
						g.DeathProcessingAndAuraUpdate();
					}
				})
			}));

			// ---------------------------------------------------------------- Warlock
			// [BAR_910] Grimoire of Sacrifice (1) — Destroy a friendly minion. Deal $2 damage to all enemy minions.
			cards.Add("BAR_910", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 } }, new Power
			{
				PowerTask = ComplexTask.Create(new DestroyTask(EntityType.TARGET), new DamageTask(V("BAR_910", "damage", 2), EntityType.OP_MINIONS, true))
			}));
			// [BAR_911] Soul Rend (4) — Deal $5 damage to all minions. Destroy a card in your deck for each killed.
			cards.Add("BAR_911", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var all = c.BoardZone.Concat(c.Opponent.BoardZone).ToList();
					foreach (Minion m in all) Generic.DamageCharFunc((IPlayable)s, m, V("BAR_911", "damage", 5), true);
					int killed = all.Count(m => m.Health <= 0 || m.ToBeDestroyed);
					for (int i = 0; i < killed && c.DeckZone.Count > 0; i++) Generic.Destroy(c.DeckZone.TopCard, s, many: true);
				})
			}));
			// [BAR_912] Apothecary's Caravan (2) — At the start of your turn, summon a 1-Cost minion from your deck.
			cards.Add("BAR_912", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START) { SingleTask = new RecruitTask(1, new SelfCondition(p => p.Cost == V("BAR_912", "cost", 1))) }
			}));
			// [BAR_913] Altar of Fire (1) — Destroy the top 3 cards of each deck.
			cards.Add("BAR_913", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Controller p in new[] { c, c.Opponent })
						for (int i = 0; i < V("BAR_913", "cards", 3) && p.DeckZone.Count > 0; i++) Generic.Destroy(p.DeckZone.TopCard, s, many: true);
				})
			}));
			// [BAR_915] Kabal Outfitter (3) — Battlecry and Deathrattle: Give another random friendly minion +1/+1.
			ISimpleTask outfit = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, PickMinions(g, c.BoardZone.Where(m => m.Id != s.Id), 1), "BAR_915e"));
			cards.Add("BAR_915", new CardDef(new Power { PowerTask = outfit, DeathrattleTask = outfit }));
			cards.Add("BAR_915e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("BAR_915", "stats", 1))) }));
			// [BAR_916] Blood Shard Bristleback (3) — Lifesteal. Battlecry: If your deck contains 10 or fewer cards, deal 6 damage to a minion.
			cards.Add("BAR_916", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_IF_AVAILABLE, 0 }, { PlayReq.REQ_MINION_TARGET, 0 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is Minion m && c.DeckZone.Count <= V("BAR_916", "deck", 10)) Generic.DamageCharFunc((IPlayable)s, m, V("BAR_916", "damage", 6), false);
				})
			}));
			// [BAR_917] Barrens Scavenger (6) — Taunt. Costs (1) while your deck has 10 or fewer cards.
			cards.Add("BAR_917", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => p.Controller.DeckZone.Count <= V("BAR_917", "deck", 10) ? System.Math.Max(0, p.Card.Cost - V("BAR_917", "cost", 1)) : 0)
			}));
			// [BAR_918] Tamsin Roame (3) — Whenever you cast a Shadow spell that costs (1) or more, add a copy to your hand that costs (0).
			cards.Add("BAR_918", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.CAST_SPELL)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p[SPELL_SCHOOL] == SHADOW && PlayedCost(p) >= 1),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						IPlayable cast = t ?? g.CurrentEventData?.EventSource as IPlayable;
						if (cast == null || c.HandZone.IsFull) return;
						IPlayable copy = Entity.FromCard(c, cast.Card);
						Generic.AddHandPhase.Invoke(c, copy);
						if (copy.Zone?.Type == SabberStoneCore.Enums.Zone.HAND) Give(g, s, copy, "BAR_918e");
					})
				}
			}));
			cards.Add("BAR_918e", new CardDef(new Power { Enchant = new Enchant(Effects.SetCost(V("BAR_918", "cost", 0))) }));
			// [WC_021] Unstable Shadow Blast (2) — Deal $6 damage to a minion. Excess damage hits your hero.
			cards.Add("WC_021", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					int excess = Generic.DamageWithExcess((IPlayable)s, m, V("WC_021", "damage", 6), true); // (the shared excess hit)
					if (excess > 0) Generic.DamageCharFunc((IPlayable)s, c.Hero, excess, false);
				})
			}));
			// [WC_022] Final Gasp (1) — Deal $1 damage to a minion. If it dies, summon a 2/2 Adventurer with a random bonus effect.
			cards.Add("WC_022", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					Generic.DamageCharFunc((IPlayable)s, m, V("WC_022", "damage", 1), true);
					if (m.Health <= 0 || m.ToBeDestroyed)
					{
						g.DeathProcessingAndAuraUpdate();
						Summon(g, c, RandomAdventurer(g), s);
					}
				})
			}));

			// ---------------------------------------------------------------- Warrior
			// [BAR_841] Bulk Up (2) — Give a random Taunt minion in your hand +1/+1 and copy it.
			cards.Add("BAR_841", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var taunts = c.HandZone.Where(p => Generic.IsMinionCard(p) && p.Card[GameTag.TAUNT] > 0).ToList();
					if (taunts.Count == 0) return;
					IPlayable picked = taunts[g.Random.Next(taunts.Count)];
					Give(g, s, picked, "BAR_841e");
					if (Generic.Copy(c, s, picked, SabberStoneCore.Enums.Zone.HAND) is IPlayable copy && copy.Zone?.Type != SabberStoneCore.Enums.Zone.HAND && !c.HandZone.IsFull)
						Generic.AddHandPhase.Invoke(c, copy);
				})
			}));
			cards.Add("BAR_841e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("BAR_841", "stats", 1))) }));
			// [BAR_844] Outrider's Axe (4) — After your hero attacks and kills a minion, draw a card.
			cards.Add("BAR_844", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK) { TriggerSource = TriggerSource.HERO, Condition = new SelfCondition(p => KilledItsDefender(p.Game)), SingleTask = new DrawTask() }
			}));
			// [BAR_845] Rancor (4) — Deal $2 damage to all minions. Gain 2 Armor for each destroyed.
			cards.Add("BAR_845", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var all = c.BoardZone.Concat(c.Opponent.BoardZone).ToList();
					foreach (Minion m in all) Generic.DamageCharFunc((IPlayable)s, m, V("BAR_845", "damage", 2), true);
					int killed = all.Count(m => m.Health <= 0 || m.ToBeDestroyed);
					if (killed > 0) c.Hero.GainArmor((IPlayable)s, killed * V("BAR_845", "armor", 2));
				})
			}));
			// [BAR_846] Mor'shan Elite (5) — Taunt. Battlecry: If your hero attacked this turn, summon a copy of this.
			cards.Add("BAR_846", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c.Hero.NumAttacksThisTurn > 0, new CustomTask((g, c, s, t, stack) =>
				{
					if (s is Minion me && !c.BoardZone.IsFull && Generic.Copy(c, s, me, SabberStoneCore.Enums.Zone.PLAY) is Minion copy && copy.Zone?.Type != SabberStoneCore.Enums.Zone.PLAY)
						Generic.SummonBlock.Invoke(g, copy, me.ZonePosition + 1, s);
				}))
			}));
			// [BAR_847] Rokara (3) — Rush. After a friendly minion attacks and survives, give it +1/+1.
			cards.Add("BAR_847", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.MINIONS,
					Condition = new SelfCondition(p => p is Minion m && m.Health > 0 && !m.ToBeDestroyed && m.Zone?.Type == SabberStoneCore.Enums.Zone.PLAY),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (g.CurrentEventData?.EventSource is Minion attacker) Give(g, s, attacker, "BAR_847e");
					})
				}
			}));
			cards.Add("BAR_847e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("BAR_847", "stats", 1))) }));
			// [WC_024] Man-at-Arms (2) — Battlecry: If you have a weapon equipped, gain +1/+1.
			cards.Add("WC_024", new CardDef(new Power { PowerTask = ComplexTask.If((g, c, s) => c.Hero.Weapon != null, new AddEnchantmentTask("WC_024e", EntityType.SOURCE)) }));
			cards.Add("WC_024e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("WC_024", "stats", 1))) }));
			// [WC_025] Whetstone Hatchet (1) — After your hero attacks, give a minion in your hand +1 Attack.
			cards.Add("WC_025", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK) { TriggerSource = TriggerSource.HERO, SingleTask = BuffRandomInHand(p => p is Minion, "WC_025e") }
			}));
			cards.Add("WC_025e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("WC_025", "attack", 1))) }));
			BarrensBatch2(cards);
		}

		/// <summary>Kazakus, Golem Shaper's herbs: the keyword herbs (Swifthistle … Grave Moss) give the golem their keyword;
		/// the battlecry herbs come in three strengths, the Lesser / Greater / Superior golem's (base, "b", "c" cards).</summary>
		private static readonly (string Herb, GameTag Keyword)[] KeywordHerbs =
		{
			("BAR_079t4", GameTag.RUSH), ("BAR_079t5", GameTag.TAUNT), ("BAR_079t6", GameTag.DIVINE_SHIELD),
			("BAR_079t7", GameTag.LIFESTEAL), ("BAR_079t8", GameTag.STEALTH), ("BAR_079t9", GameTag.POISONOUS),
		};
		private static readonly string[][] BattlecryHerbs =
		{
			new[] { "BAR_079t10", "BAR_079t10b", "bar_079t10c" }, // Wildvine: other minions +1/+1, +2/+2, +4/+4 (the client's id is lower case)
			new[] { "BAR_079t11", "BAR_079t11", "BAR_079t11" },   // Gromsblood: summon a copy of this
			new[] { "BAR_079t12", "BAR_079t12b", "BAR_079t12c" }, // Icecap: Freeze 1 / 2 / all random enemy minions
			new[] { "BAR_079t13", "BAR_079t13b", "BAR_079t13c" }, // Firebloom: 3 damage to 1 / 2 / all random enemy minions
			new[] { "BAR_079t14", "BAR_079t14b", "BAR_079t14c" }, // Mageroyal: Spell Damage +1 / +2 / +4
			new[] { "BAR_079t15", "BAR_079t15b", "BAR_079t15c" }, // Kingsblood: draw 1 / 2 / 4
		};
		private static readonly string[] Golems = { "BAR_079_m1", "BAR_079_m2", "BAR_079_m3" };

		private static List<IPlayable> OfferCards(Controller c, IEntity source, IEnumerable<string> ids)
		{
			c.Game.FlushPendingFx(source.Id); // (a held-back buff effect opens before the options: Generic.CreateChoiceCards)
			return ids.Select(id => Entity.FromCard(c, Cards.FromId(id), new EntityData { { GameTag.CREATOR, source.Id }, { GameTag.DISPLAYED_CREATOR, source.Id } }, c.SetasideZone)).ToList();
		}

		/// <summary>The golem's battlecry: its herb (MODULAR_ENTITY_PART_2) at its size's strength.</summary>
		private static readonly ISimpleTask GolemBattlecry = new CustomTask((g, c, s, t, stack) =>
		{
			if (!(s is Minion golem) || !(Cards.FromAssetId(golem[GameTag.MODULAR_ENTITY_PART_2]) is Card herb)) return;
			int tier = System.Array.IndexOf(Golems, golem.Card.Id);
			int family = System.Array.FindIndex(BattlecryHerbs, f => f.Contains(herb.Id));
			if (tier < 0 || family < 0) return;
			int[] counts = { 1, 2, 7 };
			switch (family)
			{
				case 0:
					GiveAll(g, golem, c.BoardZone.Where(m => m.Id != golem.Id).ToList(), new[] { "BAR_079t10e", "BAR_079t10be", "BAR_079t10ce" }[tier]);
					break;
				case 1:
					if (!c.BoardZone.IsFull && Generic.Copy(c, golem, golem, SabberStoneCore.Enums.Zone.PLAY) is Minion copy && copy.Zone?.Type != SabberStoneCore.Enums.Zone.PLAY)
						Generic.SummonBlock.Invoke(g, copy, golem.ZonePosition + 1, golem);
					break;
				case 2:
					foreach (Minion m in PickMinions(g, c.Opponent.BoardZone.Where(x => !x.IsFrozen), counts[tier])) m.IsFrozen = true;
					break;
				case 3:
					foreach (Minion m in PickMinions(g, c.Opponent.BoardZone, counts[tier])) Generic.DamageCharFunc(golem, m, V("BAR_079t13", "damage", 3), false);
					break;
				case 5:
					for (int i = 0; i < new[] { 1, 2, 4 }[tier]; i++) Generic.Draw(c);
					break;
			}
		});

		/// <summary>Silas-style cards' "a minion swaps with a random one in its owner's hand" (Shadow Hunter Vol'jin).</summary>
		private static void SwapWithHand(Game g, Minion onBoard, IEntity source)
		{
			Controller owner = onBoard.Controller;
			var hand = owner.HandZone.OfType<Minion>().Where(m => Generic.IsMinionCard(m)).ToList();
			if (hand.Count == 0) return;
			Minion fromHand = hand[g.Random.Next(hand.Count)];
			int position = onBoard.ZonePosition;
			owner.HandZone.Remove(fromHand);
			Generic.ReturnToHand(owner, onBoard);
			if (!owner.BoardZone.IsFull) Generic.SummonBlock.Invoke(g, fromHand, position, source);
		}

		private static void BarrensBatch2(IDictionary<string, CardDef> cards)
		{
			// [BAR_048] Bru'kan (4) — Nature Spell Damage +3. (The client's SPELLPOWER_NATURE: Controller school Spell Damage.)
			cards.Add("BAR_048", EngineOnly);
			// Kazakus' herbs are choices only (their effects are the golem's)
			foreach (string herb in KeywordHerbs.Select(h => h.Herb).Concat(BattlecryHerbs.SelectMany(f => f)).Distinct())
				cards.Add(herb, EngineOnly);
			cards.Add("BAR_079t10e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("BAR_079t10", "stats", 1))) }));
			cards.Add("BAR_079t10be", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("BAR_079t10b", "stats", 2))) }));
			cards.Add("BAR_079t10ce", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("BAR_079t10c", "stats", 4))) }));
			foreach (string golem in Golems)
				cards.Add(golem, new CardDef(new Power { PowerTask = GolemBattlecry }));

			// [BAR_079] Kazakus, Golem Shaper (4) — Battlecry: If your deck has no 4-Cost cards, build a custom Golem. (User
			// 2026-10-06: pick the golem, then a keyword herb and a battlecry herb of its strength, three offered each; the golem
			// shows them through MODULAR_ENTITY_PART_1 / _2, the client's text builder for it.)
			cards.Add("BAR_079", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c.DeckZone.Any(p => p.Cost == V("BAR_079", "cost", 4))) return;
					int kazakus = s.Id;
					// live (live-20261006-dredge): the picked size makes a new golem in SETASIDE right away (CREATOR /
					// DISPLAYED_CREATOR = Kazakus, HISTORY_TARGET_DONT_DUPLICATE_UNTIL_END); each herb pick puts its tag on it (TAUNT …,
					// then BATTLECRY and MODULAR_ENTITY_PART_1 / _2); then it moves into the hand (LAST_AFFECTED_BY = Kazakus).
					Generic.CreateChoiceEntities(c, s, OfferCards(c, s, Golems), ChoiceAction.STACK, new CustomTask((g2, c2, s2, golemPick, st2) =>
					{
						if (golemPick == null) return;
						int tier = System.Array.IndexOf(Golems, golemPick.Card.Id);
						IPlayable golem = Entity.FromCard(c2, Cards.FromId(Golems[tier]), new EntityData
						{
							{ GameTag.CREATOR, kazakus }, { GameTag.DISPLAYED_CREATOR, kazakus }
						}, c2.SetasideZone);
						if (g2.History)
							g2.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.HISTORY_TARGET_DONT_DUPLICATE_UNTIL_END, Data = 0, Info = new List<int> { golem.Id } });
						var keywordOffer = OfferCards(c2, s2, Pick(g2, KeywordHerbs.Select(h => Cards.FromId(h.Herb)), 3).Select(x => x.Id));
						Generic.CreateChoiceEntities(c2, s2, keywordOffer, ChoiceAction.STACK, new CustomTask((g3, c3, s3, keywordPick, st3) =>
						{
							if (keywordPick == null) return;
							golem[KeywordHerbs.First(h => h.Herb == keywordPick.Card.Id).Keyword] = 1;
							var battleOffer = OfferCards(c3, s3, Pick(g3, BattlecryHerbs.Select(f => Cards.FromId(f[tier])), 3).Select(x => x.Id));
							Generic.CreateChoiceEntities(c3, s3, battleOffer, ChoiceAction.STACK, new CustomTask((g4, c4, s4, battlePick, st4) =>
							{
								if (battlePick == null) return;
								int family = System.Array.FindIndex(BattlecryHerbs, f => f.Contains(battlePick.Card.Id));
								if (family == 4) golem[GameTag.SPELLPOWER] = new[] { 1, 2, 4 }[tier]; // Mageroyal: Spell Damage
								else golem[GameTag.BATTLECRY] = 1;
								golem[GameTag.MODULAR_ENTITY_PART_1] = keywordPick.Card.AssetId;
								golem[GameTag.MODULAR_ENTITY_PART_2] = battlePick.Card.AssetId;
								Generic.NoteAffected(golem, kazakus);
								if (golem.Zone?.Type == SabberStoneCore.Enums.Zone.SETASIDE)
									Generic.AddHandPhase.Invoke(c4, golem.Zone.Remove(golem));
							}));
						}));
					}));
				})
			}));

			// [BAR_064] Talented Arcanist (2) — Battlecry: Your next spell this turn has Spell Damage +2.
			cards.Add("BAR_064", new CardDef(new Power { PowerTask = new AddEnchantmentTask("BAR_064e", EntityType.CONTROLLER) }));
			cards.Add("BAR_064e", new CardDef(new Power
			{
				Enchant = new Enchant(new Effect(GameTag.SPELLPOWER, EffectOperator.ADD, V("BAR_064", "spell damage", 2))) { IsOneTurnEffect = true },
				Trigger = new Trigger(TriggerType.AFTER_CAST) { TriggerSource = TriggerSource.FRIENDLY, SingleTask = RemoveEnchantmentTask.Task }
			}));
			// [BAR_077] Kargal Battlescar (7) — Battlecry: Summon a 5/5 Lookout for each Watch Post you've summoned this game.
			cards.Add("BAR_077", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int posts = c.SummonedThisGame.Count(card => card.Name.Contains("Watch Post"));
					for (int i = 0; i < posts && !c.BoardZone.IsFull; i++) Summon(g, c, Cards.FromId("BAR_077t"), s);
				})
			}));
			// [BAR_080] Shadow Hunter Vol'jin (5) — Battlecry: Choose a minion. Swap it with a random one in its owner's hand.
			cards.Add("BAR_080", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_IF_AVAILABLE, 0 }, { PlayReq.REQ_MINION_TARGET, 0 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (t is Minion m && m.Id != s.Id && m.Zone?.Type == SabberStoneCore.Enums.Zone.PLAY) SwapWithHand(g, m, s); })
			}));
			// [BAR_081] Southsea Scoundrel (4) — Battlecry: Discover a card in your opponent's deck. They draw theirs as well.
			cards.Add("BAR_081", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					Generic.DiscoverFromCards(c, s, Generic.PickFromDeck(c.Opponent, p => true), (g2, c2, s2, real) =>
					{
						AddToHand(c2, real.Card);
						if (real.Zone?.Type == SabberStoneCore.Enums.Zone.DECK) Generic.Draw(real.Controller, real);
					}))
			}));
			// [BAR_721] Mankrik (3) — Battlecry: Help Mankrik find his wife! She was last seen somewhere in your deck.
			cards.Add("BAR_721", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.ShuffleIntoDeck.Invoke(c, s, Entity.FromCard(c, Cards.FromId("BAR_721t"))))
			}));
			// [BAR_721t] Olgra, Mankrik's Wife — Casts When Drawn. Summon a 3/7 Mankrik, who immediately attacks the enemy hero.
			cards.Add("BAR_721t", new CardDef(new Power
			{
				TopdeckTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c.BoardZone.IsFull) return;
					var mankrik = (Minion)Entity.FromCard(c, Cards.FromId("BAR_721t2"));
					Generic.SummonBlock.Invoke(g, mankrik, -1, s);
					if (mankrik.Zone?.Type == SabberStoneCore.Enums.Zone.PLAY && !mankrik.ToBeDestroyed) EffectAttack(g, mankrik, c.Opponent.Hero);
				})
			}));
			// [WC_030] Mutanus the Devourer (7) — Battlecry: Eat a minion in your opponent's hand. Gain its stats.
			cards.Add("WC_030", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var hand = c.Opponent.HandZone.OfType<Minion>().Where(m => Generic.IsMinionCard(m)).ToList();
					if (hand.Count == 0 || !(s is Minion me)) return;
					Minion eaten = hand[g.Random.Next(hand.Count)];
					int atk = eaten.AttackDamage, hp = eaten.Health;
					Generic.Destroy(eaten, s);
					if (me.Zone?.Type == SabberStoneCore.Enums.Zone.PLAY) Generic.AddEnchantmentBlock(g, Cards.FromId("WC_030e"), me, me, atk, hp, 0);
				})
			}));
			cards.Add("WC_030e", new CardDef(new Power
			{
				Enchant = new Enchant(new Effect(GameTag.ATK, EffectOperator.ADD, 0), new Effect(GameTag.HEALTH, EffectOperator.ADD, 0)) { UseScriptTag = true }
			}));
			// [WC_035] Archdruid Naralex (3) — Dormant for 2 turns. While Dormant, add a Dream card to your hand at the end of your turn.
			cards.Add("WC_035", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					WhileDormant = true,
					Condition = new SelfCondition(p => p[GameTag.DORMANT] > 0),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						string[] dreams = { "DREAM_01", "DREAM_02", "DREAM_03", "DREAM_04", "DREAM_05" };
						AddToHand(c, Cards.FromId(dreams[g.Random.Next(dreams.Length)]));
					})
				}
			}));

			// ---------------------------------------------------------------- Demon Hunter / Druid / Hunter
			// [BAR_537] Razormane Battleguard (2) — The first Taunt minion you play each turn costs (2) less.
			cards.Add("BAR_537", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HAND, Effects.ReduceCost(V("BAR_537", "cost", 2)))
				{
					Condition = new SelfCondition(p => p is Minion && p.Card[GameTag.TAUNT] > 0 &&
						!p.Controller.PlayHistory.Any(h => h.Turn == p.Game.Turn && h.SourceCard.Type == CardType.MINION && h.SourceCard[GameTag.TAUNT] > 0)),
					Restless = true
				}
			}));
			// [BAR_539] Celestial Alignment (8) — Set your Mana Crystals to 0. Set the Cost of cards in your hand and deck to (1).
			cards.Add("BAR_539", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					c.BaseMana = 0;
					c.UsedMana = 0;
					c.TemporaryMana = 0;
					GiveAll(g, s, c.HandZone.Concat(c.DeckZone).ToList(), "BAR_539e");
				})
			}));
			cards.Add("BAR_539e", new CardDef(new Power { Enchant = new Enchant(Effects.SetCost(V("BAR_539", "cost", 1))) }));
			// [BAR_037] Warsong Wrangler (4) — Battlecry: Discover a Beast from your deck. Give all copies of it +2/+1 (wherever they are).
			cards.Add("BAR_037", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					Generic.DiscoverFromDeck(c, s, Generic.PickFromDeck(c, p => IsRaceCard(p, Race.BEAST)), (g2, c2, s2, beast) =>
					{
						string id = beast.Card.Id;
						if (beast.Zone?.Type == SabberStoneCore.Enums.Zone.DECK) Generic.Draw(c2, beast);
						GiveAll(g2, s2, c2.HandZone.Concat(c2.DeckZone).Concat(c2.BoardZone).Where(p => p.Card.Id == id).ToList(), "BAR_037e");
					}))
			}));
			cards.Add("BAR_037e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("BAR_037", "attack", 2)), Effects.Health_N(V("BAR_037", "health", 1))) }));
			// [BAR_038] Tavish Stormpike (3) — After a friendly Beast attacks, summon a Beast from your deck that costs (1) less.
			cards.Add("BAR_038", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.MINIONS,
					Condition = new SelfCondition(p => IsRaceCard(p, Race.BEAST)),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(g.CurrentEventData?.EventSource is IPlayable attacker)) return;
						int cost = attacker.Cost - V("BAR_038", "less", 1);
						new RecruitTask(1, new SelfCondition(p => IsRaceCard(p, Race.BEAST) && p.Cost == cost)).Process(g, c, s, null);
					})
				}
			}));

			// ---------------------------------------------------------------- Mage
			// [BAR_544] Reckless Apprentice (4) — Battlecry: Fire your Hero Power at all enemies. (A targeted Hero Power hits each
			// enemy; any other is used once.)
			cards.Add("BAR_544", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					HeroPower hp = c.Hero.HeroPower;
					ISimpleTask effect = hp?.Card.Power?.PowerTask;
					if (effect == null) return;
					if (hp.Card.RequiresTarget || hp.Card.PlayRequirements?.ContainsKey(PlayReq.REQ_TARGET_IF_AVAILABLE) == true)
						foreach (ICharacter enemy in new ICharacter[] { c.Opponent.Hero }.Concat(c.Opponent.BoardZone.Where(m => !m.Untouchable)).ToList())
							effect.Process(g, c, hp, (IPlayable)enemy);
					else
						effect.Process(g, c, hp, null);
				})
			}));
			// [BAR_545] Arcane Luminary (3) — Cards that didn't start in your deck cost (2) less (but not less than 1).
			cards.Add("BAR_545", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HAND, Effects.ReduceCostNotBelow1(V("BAR_545", "cost", 2)))
				{
					Condition = new SelfCondition(p => p.Id >= p.Game.FirstGeneratedEntityId), ShowAs = "BAR_545e"
				}
			}));
			// [BAR_546] Wildfire (1) — Your Hero Power deals 1 more damage this game.
			cards.Add("BAR_546", new CardDef(new Power { PowerTask = new AddEnchantmentTask("BAR_546e", EntityType.HERO) }));
			cards.Add("BAR_546e", new CardDef(new Power { Enchant = new Enchant(GameTag.HEROPOWER_DAMAGE, EffectOperator.ADD, V("BAR_546", "damage", 1)) }));
			// [BAR_547] Mordresh Fire Eye (8) — Battlecry: If you've dealt 10 damage with your Hero Power this game, deal 10 damage to all enemies.
			cards.Add("BAR_547", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c.NumHeroPowerDamageThisGame >= V("BAR_547", "needed", 10),
					new DamageTask(V("BAR_547", "damage", 10), EntityType.ENEMIES))
			}));

			// ---------------------------------------------------------------- Paladin / Priest
			// [BAR_879] Cannonmaster Smythe (5) — Battlecry: Transform your Secrets into 3/3 Soldiers. They transform back when they die.
			// (Each Soldier carries BAR_879e "Secrecy" holding its Secret; the enchantment's Deathrattle puts it back.)
			cards.Add("BAR_879", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Spell secret in c.SecretZone.Where(x => x.IsSecret).ToList())
					{
						if (c.BoardZone.IsFull) break;
						secret.ActivatedTrigger?.Remove();
						c.SecretZone.Remove(secret);
						c.SetasideZone.Add(secret);
						var soldier = (Minion)Entity.FromCard(c, Cards.FromId("BAR_879t"));
						Generic.SummonBlock.Invoke(g, soldier, -1, s);
						if (soldier.Zone?.Type == SabberStoneCore.Enums.Zone.PLAY)
							Generic.AddEnchantmentBlock(g, Cards.FromId("BAR_879e"), (IPlayable)s, soldier, 0, 0, secret.Id);
					}
				})
			}));
			cards.Add("BAR_879e", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					// (an enchantment's Deathrattle: source = the dead minion, target = the enchantment)
					if (t is Enchantment e && e.CapturedCard != null && !c.SecretZone.IsFull && !c.SecretZone.Any(x => x.Card.Id == e.CapturedCard.Id))
						PutSecretIntoPlay(c, (Spell)Entity.FromCard(c, e.CapturedCard));
				})
			}));
			cards.Add("BAR_879t", EngineOnly); // its Deathrattle is its BAR_879e's
			// [BAR_315] Serena Bloodfeather (2) — Battlecry: Choose an enemy minion. Steal Attack and Health from it until this has more.
			// (User 2026-10-06: one point at a time until hers is higher; it can take the last Health — a 1/1 dies.)
			cards.Add("BAR_315", new CardDef(new Dictionary<PlayReq, int>
				{ { PlayReq.REQ_TARGET_IF_AVAILABLE, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_ENEMY_TARGET, 0 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion target) || !(s is Minion me)) return;
					int attack = 0, health = 0;
					while (me.AttackDamage + attack <= target.AttackDamage - attack && target.AttackDamage - attack > 0) attack++;
					while (me.Health + health <= target.Health - health && target.Health - health > 0) health++;
					if (attack > 0)
					{
						Generic.AddEnchantmentBlock(g, Cards.FromId("BAR_315e1"), me, me, attack, 0, 0);
						Generic.AddEnchantmentBlock(g, Cards.FromId("BAR_315e3"), me, target, -attack, 0, 0);
					}
					if (health > 0)
					{
						Generic.AddEnchantmentBlock(g, Cards.FromId("BAR_315e2"), me, me, health, 0, 0);
						Generic.AddEnchantmentBlock(g, Cards.FromId("BAR_315e4"), me, target, -health, 0, 0);
						if (target.Health <= 0) target.ToBeDestroyed = true;
					}
				})
			}));
			foreach ((string id, GameTag stat) in new[] { ("BAR_315e1", GameTag.ATK), ("BAR_315e3", GameTag.ATK), ("BAR_315e2", GameTag.HEALTH), ("BAR_315e4", GameTag.HEALTH) })
				cards.Add(id, new CardDef(new Power { Enchant = new Enchant(new Effect(stat, EffectOperator.ADD, 0)) { UseScriptTag = true } }));
			// [BAR_735] Xyrella (4) — Battlecry: If you've restored Health this turn, deal that much damage to all enemy minions.
			cards.Add("BAR_735", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int healed = c.AmountHealedThisTurn;
					if (healed <= 0) return;
					foreach (Minion m in c.Opponent.BoardZone.ToList()) Generic.DamageCharFunc((IPlayable)s, m, healed, false);
				})
			}));
			// [WC_803] Cleric of An'she (1) — Battlecry: If you've restored Health this turn, Discover a spell from your deck.
			cards.Add("WC_803", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c.AmountHealedThisTurn > 0) Generic.DiscoverFromDeck(c, s, Generic.PickFromDeck(c, p => p is Spell));
				})
			}));

			// ---------------------------------------------------------------- Rogue
			// [BAR_323] Yoink! (1) — Discover a Hero Power and set its Cost to (0). Swap back after 2 uses. (User 2026-10-06:
			// another class's base Hero Power; after 2 uses yours comes back. BAR_323e on it holds yours.)
			cards.Add("BAR_323", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var pool = Cards.HeroClasses.Where(cls => cls != c.HeroClass)
						.Select(cls => { try { return Cards.HeroCard(cls); } catch { return null; } })
						.Where(h => h != null && h[GameTag.HERO_POWER] > 0)
						.Select(h => Cards.FromAssetId(h[GameTag.HERO_POWER])).Where(x => x != null).ToList();
					var offered = OfferCards(c, s, Pick(g, pool, 3).Select(x => x.Id));
					if (offered.Count == 0) return;
					Generic.CreateChoiceEntities(c, s, offered, ChoiceAction.STACK, new CustomTask((g2, c2, s2, chosen, st2) =>
					{
						if (chosen == null || c2.Hero.HeroPower == null) return;
						int yours = c2.Hero.HeroPower.Id;
						// (2026-10-08, live zilliax 113244 123490: the PICKED option itself becomes the Hero Power — not a new copy; BAR_323e
						// on it keeps yours: NUM_2 = its entity)
						if (chosen is HeroPower picked)
						{
							picked.Zone?.Remove(picked);
							new ReplaceHeroPower().Process(g2, c2, s2, null, new TaskStack { Playables = new List<IPlayable> { picked } });
						}
						else new ReplaceHeroPower(chosen.Card).Process(g2, c2, s2, null);
						Generic.AddEnchantmentBlock(g2, Cards.FromId("BAR_323e"), (IPlayable)s2, c2.Hero.HeroPower, 0, 0, yours);
						if (c2.Hero.HeroPower.AppliedEnchantments?.LastOrDefault(e => e.Card.Id == "BAR_323e") is Enchantment held)
							held[GameTag.TAG_SCRIPT_DATA_NUM_2] = yours;
					}));
				})
			}));
			cards.Add("BAR_323e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.SetCost(V("BAR_323", "cost", 0))),
				Trigger = new Trigger(TriggerType.INSPIRE)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Enchantment e)) return;
						e[GameTag.TAG_SCRIPT_DATA_NUM_1] = e[GameTag.TAG_SCRIPT_DATA_NUM_1] + 1;
						if (e[GameTag.TAG_SCRIPT_DATA_NUM_1] < V("BAR_323", "uses", 2) || e.CapturedCard == null) return;
						// (2026-10-08, live 129381: the count's block, then a second block at index 1: a NEW copy of your Hero Power —
						// CREATOR = this, COPIED_FROM_ENTITY_ID = yours — the yoinked one set aside at its own Cost again)
						g.TaskQueue.EnqueueTriggerBlock(new CustomTask((g2, c2, s2, t2, st2) =>
						{
							if (!(s2 is Enchantment held) || held.CapturedCard == null) return;
							IPlayable yoinked = held.Target as IPlayable;
							new ReplaceHeroPower(held.CapturedCard).Process(g2, c2, held, null);
							if (held[GameTag.TAG_SCRIPT_DATA_NUM_2] > 0) c2.Hero.HeroPower[HsTag.COPIED_FROM_ENTITY_ID] = held[GameTag.TAG_SCRIPT_DATA_NUM_2];
							held.Remove();
						}), c, e, 1);
					})
				}
			}));
			// [BAR_552] Scabbs Cutterbutter (4) — Combo: The next two cards you play this turn cost (2) less.
			cards.Add("BAR_552", new CardDef(new Power { ComboTask = new AddEnchantmentTask("BAR_552o", EntityType.CONTROLLER) }));
			cards.Add("BAR_552o", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HAND, Effects.ReduceCost(V("BAR_552", "cost", 2))) { RemoveTrigger = (TriggerType.TURN_END, null), ShowAs = "BAR_552e" },
				Trigger = new Trigger(TriggerType.PLAY_CARD)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Enchantment e)) return;
						e[GameTag.TAG_SCRIPT_DATA_NUM_1] = e[GameTag.TAG_SCRIPT_DATA_NUM_1] + 1;
						if (e[GameTag.TAG_SCRIPT_DATA_NUM_1] >= V("BAR_552", "cards", 2)) RemoveEnchantmentTask.Task.Process(g, c, s, null);
					})
				}
			}));
			// [WC_016] Shroud of Concealment (3) — Draw 2 minions. Any played this turn gain Stealth for 1 turn.
			cards.Add("WC_016", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("WC_016", "draw", 2); i++)
						if (DrawOne(g, c, p => p is Minion) is IPlayable drawn) Give(g, s, drawn, "WC_016e");
				})
			}));
			cards.Add("WC_016e", new CardDef(new Power
			{
				Enchant = new Enchant { IsOneTurnEffect = true },
				Trigger = new Trigger(TriggerType.AFTER_PLAY_MINION)
				{
					TriggerActivation = TriggerActivation.HAND_OR_PLAY,
					TriggerSource = TriggerSource.ENCHANTMENT_TARGET,
					SingleTask = new CustomTask((g, c, s, t, stack) => { if (s is Enchantment e && e.Target is Minion m) Give(g, s, m, "WC_016e2"); })
				}
			}));
			cards.Add("WC_016e2", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.StealthEff),
				Trigger = new Trigger(TriggerType.TURN_START) { SingleTask = RemoveEnchantmentTask.Task }
			}));
			// [WC_017] Savory Deviate Delight (1) — Transform a minion in both players' hands into a Pirate or Stealth minion.
			// (User 2026-10-06: any cost, random.)
			cards.Add("WC_017", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Controller p in new[] { c, c.Opponent })
					{
						var minions = p.HandZone.OfType<Minion>().Where(m => Generic.IsMinionCard(m)).ToList();
						if (minions.Count == 0) continue;
						Card into = RandomCard(g, x => x.Type == CardType.MINION && (x.IsRace(Race.PIRATE) || x[GameTag.STEALTH] > 0));
						if (into != null) Generic.ChangeEntityBlock(p, minions[g.Random.Next(minions.Count)], into, true);
					}
				})
			}));

			// ---------------------------------------------------------------- Warlock / Warrior
			// [BAR_919] Neeru Fireblade (5) — Battlecry: If your deck is empty, open a portal that fills your board with 3/2 Imps each turn.
			cards.Add("BAR_919", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c.DeckZone.Count == 0, new CustomTask((g, c, s, t, stack) => Summon(g, c, Cards.FromId("BAR_919t"), s)))
			}));
			// [BAR_919t] Burning Blade Portal — At the end of your turn, fill your board with 3/2 Imps.
			cards.Add("BAR_919t", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						for (int guard = 0; !c.BoardZone.IsFull && guard < 7; guard++) Summon(g, c, Cards.FromId("BAR_914t3"), s);
					})
				}
			}));
			// [WC_023] Stealer of Souls (4) — The first card you draw each turn costs Health instead of Mana.
			cards.Add("WC_023", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DRAW)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p.Controller.NumCardsDrawnThisTurn == 1),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						IPlayable drawn = t ?? g.CurrentEventData?.EventSource as IPlayable;
						if (drawn?.Zone?.Type == SabberStoneCore.Enums.Zone.HAND) Give(g, s, drawn, "WC_023e");
					})
				}
			}));
			cards.Add("WC_023e", new CardDef(new Power { Enchant = new Enchant(new Effect(GameTag.CARD_COSTS_HEALTH, EffectOperator.SET, 1)) }));
			// [BAR_334] Overlord Saurfang (7) — Battlecry: Resurrect 2 friendly Frenzy minions. Deal 1 damage to all other minions.
			cards.Add("BAR_334", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var dead = c.DiedThisGameCards.Where(p => p is Minion && p.Card[HsTag.FRENZY] > 0).ToList();
					for (int i = 0; i < V("BAR_334", "minions", 2) && dead.Count > 0; i++)
					{
						IPlayable pick = dead[g.Random.Next(dead.Count)];
						dead.Remove(pick);
						Summon(g, c, pick.Card, s);
					}
					foreach (Minion m in c.BoardZone.Concat(c.Opponent.BoardZone).Where(m => m.Id != s.Id).ToList())
						Generic.DamageCharFunc((IPlayable)s, m, V("BAR_334", "damage", 1), false);
				})
			}));
		}

		private const GameTag RAPTOR_BONUS = GameTag.TAG_SCRIPT_DATA_NUM_1;

		/// <summary>Up to <paramref name="n"/> different random minions of <paramref name="from"/>.</summary>
		private static List<Minion> PickMinions(Game g, IEnumerable<Minion> from, int n)
		{
			var pool = from.Where(m => !m.Untouchable).ToList();
			var picked = new List<Minion>();
			while (picked.Count < n && pool.Count > 0)
			{
				Minion m = pool[g.Random.Next(pool.Count)];
				pool.Remove(m);
				picked.Add(m);
			}
			return picked;
		}
	}
}
