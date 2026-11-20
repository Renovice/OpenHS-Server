// HsServer addition (not upstream SabberStone): Voyage to the Sunken City (2022, TSC_ / TID_). Every number goes through
// CardValues (V).
// Shared pieces this set uses: Dredge (Generic.Dredge, live), "Put … on the bottom of your deck" (Generic.PutOnBottom),
// Colossal (Generic.SummonAppendages: every summon / play / transform), "… while holding this" (a hand trigger counting into
// the card's TAG_SCRIPT_DATA_NUM_1; Generic.ThresholdMet, the yellow glow), Abyssal Curses.
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
		private const Race NAGA = (Race)92; // client TAG_RACE.NAGA
		private const int ARCANE = 1;

		private static bool IsNaga(IPlayable p) => IsRaceCard(p, NAGA);
		private static bool IsMurloc(IPlayable p) => IsRaceCard(p, Race.MURLOC);
		private static bool IsMech(IPlayable p) => IsRaceCard(p, Race.MECHANICAL);
		private static bool IsSchoolSpell(IPlayable p, int school) => p is Spell && p[SPELL_SCHOOL] == school;

		/// <summary>
		/// "If you've … while holding this" (the Naga): a trigger in hand counting each matching card (the event's source) into
		/// the card's TAG_SCRIPT_DATA_NUM_1 — what the client's "({0} left!)" reads; its effect checks Generic.ThresholdMet
		/// (the card data's threshold, else Power.ThresholdValue). <paramref name="type"/>: CAST_SPELL for "cast", PLAY_CARD for
		/// "played".
		/// </summary>
		/// (VIOLET_HOLD: <paramref name="source"/> ALL for "If a Stealthed minion attacked while holding this": either side's.)
		private static Trigger WhileHolding(TriggerType type, System.Func<IPlayable, bool> counts, TriggerSource source = TriggerSource.FRIENDLY) => new Trigger(type)
		{
			TriggerActivation = TriggerActivation.HAND,
			TriggerSource = source,
			CountsWhileHeld = true, // starts over when it enters the hand again
			Condition = new SelfCondition(p => counts(p)),
			SingleTask = new CustomTask((g, c, s, t, stack) =>
			{
				if (!(s is IPlayable me) || me.Zone?.Type != Zone.HAND || !(g.CurrentEventData?.EventSource is IPlayable counted) || counted.Id == me.Id) return;
				// it stops at its threshold (user 2026-10-06: Sivara remembers the first three, then says Ready)
				if (Generic.ThresholdMet(me)) return;
				me[GameTag.TAG_SCRIPT_DATA_NUM_1] = me[GameTag.TAG_SCRIPT_DATA_NUM_1] + 1;
			})
		};

		/// <summary>The common "If you've cast a … spell while holding this" card: a hand counter and a condition of 1.</summary>
		private static Power HeldSpell(System.Func<IPlayable, bool> spell, ISimpleTask effect) => new Power
		{
			Trigger = WhileHolding(TriggerType.CAST_SPELL, spell),
			ThresholdValue = 1,
			PowerTask = new CustomTask((g, c, s, t, stack) => { if (Generic.ThresholdMet((IPlayable)s)) effect.Process(g, c, s, t, stack); }),
			// (Assemble: "Battlecry: If you've cast a … while holding this," is the first half, the effect the second)
			AssembleWhen = task => HeldSpell(spell, task),
			AssembleWhat = effect
		};

		/// <summary>"If you played a Naga while holding this" (a spell's condition: counted in hand like the Naga's).</summary>
		private static Trigger NagaWhileHolding => WhileHolding(TriggerType.PLAY_CARD, p => p is Minion && IsNaga(p));

		/// <summary>Did this card see its "while holding this" count (the spell variants: the condition is checked as it resolves).</summary>
		private static bool Held(IEntity s) => s is IPlayable p && Generic.ThresholdMet(p);

		/// <summary>
		/// Abyssal Curse (TSC_955t): "At the start of your turn, take {0} damage. Each Curse is worse than the last. ({1} turns
		/// remaining)". Its data: TAG_SCRIPT_DATA_NUM_1 = 1 (damage), _NUM_2 = 2 (turns). Each Curse a player gives deals 1 more
		/// than their last one (HsTag.CURSES_GIVEN).
		/// </summary>
		private static void GiveAbyssalCurse(Game g, Controller giver, IEntity source)
		{
			Controller victim = giver.Opponent;
			if (victim.HandZone.IsFull) return;
			int n = giver[HsTag.CURSES_GIVEN] + 1;
			giver[HsTag.CURSES_GIVEN] = n;
			IPlayable curse = Entity.FromCard(victim, Cards.FromId("TSC_955t"), new EntityData
			{
				{ GameTag.TAG_SCRIPT_DATA_NUM_1, n },
				{ GameTag.TAG_SCRIPT_DATA_NUM_2, Cards.FromId("TSC_955t")[GameTag.TAG_SCRIPT_DATA_NUM_2] },
				{ GameTag.CREATOR, source.Id },
				{ HsTag.CURSE_GIVER, giver.PlayerId },
			});
			Generic.AddHandPhase.Invoke(victim, curse);
		}

		private static void SunkenCity(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Abyssal Curse
			// (user 2026-10-06: at the start of the holder's turn the damage first, then its "turns remaining" timer goes down; at 0
			// it leaves right away)
			cards.Add("TSC_955t", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					TriggerActivation = TriggerActivation.HAND,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is IPlayable curse) || curse.Zone?.Type != Zone.HAND) return;
						int damage = curse[GameTag.TAG_SCRIPT_DATA_NUM_1];
						Generic.DamageCharFunc(curse, c.Hero, damage, false);
						// Za'qul: "Your Abyssal Curses heal you for the damage they deal" (the giver's TSC_959e)
						Controller giver = c.Opponent;
						if (giver.PlayerId == curse[HsTag.CURSE_GIVER] && giver.AppliedEnchantments?.Any(e => e.Card.Id == "TSC_959e") == true)
							giver.Hero.TakeHeal(curse, damage);
						int left = curse[GameTag.TAG_SCRIPT_DATA_NUM_2] - 1;
						curse[GameTag.TAG_SCRIPT_DATA_NUM_2] = left;
						if (left <= 0) Generic.RemoveFromHand(c, curse);
					})
				}
			}));
			// [TSC_955] Sira'kess Cultist (3) — Battlecry: Give your opponent an Abyssal Curse.
			cards.Add("TSC_955", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => GiveAbyssalCurse(g, c, s)) }));
			// [TSC_956] Dragged Below (3) — Deal $4 damage to a minion. Give your opponent an Abyssal Curse.
			cards.Add("TSC_956", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("TSC_956", "damage", 4), EntityType.TARGET, true), new CustomTask((g, c, s, t, stack) => GiveAbyssalCurse(g, c, s)))
			}));
			// [TSC_924] Abyssal Wave (6) — Deal $4 damage to all minions. Give your opponent an Abyssal Curse.
			cards.Add("TSC_924", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("TSC_924", "damage", 4), EntityType.ALLMINIONS, true), new CustomTask((g, c, s, t, stack) => GiveAbyssalCurse(g, c, s)))
			}));
			// [TSC_959] Za'qul (5) — Your Abyssal Curses heal you for the damage they deal. Battlecry: Give your opponent an
			// Abyssal Curse. (TSC_959e on the player.)
			cards.Add("TSC_959", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new AddEnchantmentTask("TSC_959e", EntityType.CONTROLLER), new CustomTask((g, c, s, t, stack) => GiveAbyssalCurse(g, c, s)))
			}));
			cards.Add("TSC_959e", new CardDef(new Power()));

			// ---------------------------------------------------------------- Dredge
			// [TSC_909] Tuskarrrr Trawler (2) — Battlecry: Dredge.
			cards.Add("TSC_909", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => Generic.Dredge(c, c, s)) }));
			// [TSC_911] Excavation Specialist (4) — Battlecry: Dredge. Reduce its Cost by (1).
			cards.Add("TSC_911", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.Dredge(c, c, s, (g2, c2, top) => Give(g2, s, top, "GBL_003e")))
			}));
			// [TSC_915] Bone Glaive (5, weapon) — Battlecry: Dredge.
			cards.Add("TSC_915", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => Generic.Dredge(c, c, s)) }));
			// [TSC_916] Gone Fishin' (1) — Dredge. Combo: Draw a card.
			cards.Add("TSC_916", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.Dredge(c, c, s)),
				ComboTask = new CustomTask((g, c, s, t, stack) => Generic.Dredge(c, c, s, (g2, c2, top) => Generic.Draw(c2)))
			}));
			// [TSC_637] Scalding Geyser (1) — Deal $2 damage. Dredge.
			cards.Add("TSC_637", new CardDef(TargetAny, new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("TSC_637", "damage", 2), EntityType.TARGET, true), new CustomTask((g, c, s, t, stack) => Generic.Dredge(c, c, s)))
			}));
			// [TSC_210] Illuminate (0) — Dredge. If it's a spell, reduce its Cost by (3).
			cards.Add("TSC_210", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.Dredge(c, c, s, (g2, c2, top) => { if (top is Spell) Give(g2, s, top, "GBL_004e"); }))
			}));
			// [TSC_654] Aquatic Form (0) — Dredge. If you have the Mana to play the card this turn, draw it.
			cards.Add("TSC_654", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.Dredge(c, c, s, (g2, c2, top) => { if (c2.RemainingMana >= top.Cost) Generic.Draw(c2, top); }))
			}));
			// [TSC_642] Trench Surveyor (1) — Battlecry: Dredge. If it's a Mech, draw it.
			cards.Add("TSC_642", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.Dredge(c, c, s, (g2, c2, top) => { if (IsMech(top)) Generic.Draw(c2, top); }))
			}));
			// [TSC_083] Seafloor Savior (2) — Battlecry: Dredge. If it's a minion, give it this minion's Attack and Health.
			cards.Add("TSC_083", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.Dredge(c, c, s, (g2, c2, top) =>
				{
					if (top is Minion && s is Minion me) Generic.AddEnchantmentBlock(g2, Cards.FromId("TSC_083e"), me, top, me.AttackDamage, me.Health, 0);
				}))
			}));
			cards.Add("TSC_083e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(0), Effects.Health_N(0)) { UseScriptTag = true } }));
			// [TSC_086] Swordfish (3, weapon) — Battlecry: Dredge. If it's a Pirate, give this weapon and the Pirate +2 Attack.
			cards.Add("TSC_086", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.Dredge(c, c, s, (g2, c2, top) =>
				{
					if (!IsPirate(top)) return;
					Give(g2, s, top, "TSC_086e");
					if (c2.Hero.Weapon?.Id == s.Id) Give(g2, s, c2.Hero.Weapon, "TSC_086e");
				}))
			}));
			cards.Add("TSC_086e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("TSC_086", "attack", 2))) }));
			// [TSC_942] Obsidiansmith (2) — Battlecry: Dredge. If it's a minion or a weapon, give it +1/+1.
			cards.Add("TSC_942", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.Dredge(c, c, s, (g2, c2, top) => { if (top is Minion || top is Weapon) Give(g2, s, top, "TSC_942e"); }))
			}));
			cards.Add("TSC_942e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TSC_942", "stats", 1))) }));
			// [TID_714] Igneous Lavagorger (4) — Taunt. Battlecry: Dredge. Gain Armor equal to its Cost.
			cards.Add("TID_714", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.Dredge(c, c, s, (g2, c2, top) => { if (top.Cost > 0) c2.Hero.GainArmor((IPlayable)s, top.Cost); }))
			}));
			// [TID_003] Tidelost Burrower (4) — Battlecry: Dredge. If it's a Murloc, summon a 2/2 copy of it.
			cards.Add("TID_003", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.Dredge(c, c, s, (g2, c2, top) =>
				{
					if (!IsMurloc(top) || c2.BoardZone.IsFull) return;
					var copy = (Minion)Entity.FromCard(c2, top.Card);
					Generic.SummonBlock.Invoke(g2, copy, -1, s);
					if (copy.Zone?.Type == Zone.PLAY) Give(g2, s, copy, "TID_003e2");
				}))
			}));
			cards.Add("TID_003e2", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttackHealth(V("TID_003", "stats", 2))) }));
			// [TID_099] K9-0tron (2) — Battlecry: Dredge. If it's a 1-Cost minion, summon it.
			cards.Add("TID_099", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.Dredge(c, c, s, (g2, c2, top) =>
				{
					if (top is Minion m && m.Cost == V("TID_099", "cost", 1) && !c2.BoardZone.IsFull)
					{
						Generic.RemoveFromZone.Invoke(c2, m);
						Generic.SummonBlock.Invoke(g2, m, -1, s);
					}
				}))
			}));
			// [TID_700] Disarming Elemental (4) — Battlecry: Dredge for your opponent. Set its Cost to (6).
			cards.Add("TID_700", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.Dredge(c, c.Opponent, s, (g2, c2, top) => Give(g2, s, top, "TID_700e")))
			}));
			cards.Add("TID_700e", new CardDef(new Power { Enchant = new Enchant(Effects.SetCost(V("TID_700", "cost", 6))) }));
			// [TID_703] Topple the Idol (4) — Dredge. Reveal it and deal damage equal to its Cost to all minions.
			cards.Add("TID_703", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.Dredge(c, c, s, (g2, c2, top) =>
				{
					int damage = top.Cost;
					foreach (Minion m in c2.BoardZone.Concat(c2.Opponent.BoardZone).Where(x => !x.IsDead).ToList())
						Generic.DamageCharFunc((IPlayable)s, m, damage, true);
				}))
			}));
			// [TSC_070] Harpoon Gun (3, weapon) — After your hero attacks, Dredge. If it's a Beast, reduce its Cost by (2).
			cards.Add("TSC_070", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) => Generic.Dredge(c, c, s, (g2, c2, top) => { if (IsRaceCard(top, Race.BEAST)) Give(g2, s, top, "GBL_002e"); }))
				}
			}));
			// [TSC_753] Bloodscent Vilefin (3) — Battlecry: Dredge. If it's a Murloc, change its Cost to Health instead of Mana.
			cards.Add("TSC_753", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.Dredge(c, c, s, (g2, c2, top) => { if (IsMurloc(top)) Give(g2, s, top, "TSC_753e"); }))
			}));
			cards.Add("TSC_753e", new CardDef(new Power { Enchant = new Enchant(new Effect(GameTag.CARD_COSTS_HEALTH, EffectOperator.SET, 1)) }));
			// [TSC_925] Rock Bottom (1) — Summon a 1/1 Murloc, then Dredge. If it's also a Murloc, summon one more.
			cards.Add("TSC_925", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new SummonTask("TSC_925t", SummonSide.SPELL), new CustomTask((g, c, s, t, stack) =>
					Generic.Dredge(c, c, s, (g2, c2, top) => { if (IsMurloc(top)) Summon(g2, c2, Cards.FromId("TSC_925t"), s); })))
			}));
			// [TSC_940] From the Depths (3) — Reduce the Cost of the bottom five cards in your deck by (3), then Dredge.
			cards.Add("TSC_940", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					GiveAll(g, s, c.DeckZone.Take(V("TSC_940", "cards", 5)).ToList(), "TSC_940e2");
					Generic.Dredge(c, c, s);
				})
			}));
			cards.Add("TSC_940e2", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("TSC_940", "discount", 3))) }));

			// ---------------------------------------------------------------- "… while holding this"
			// [TSC_017] Baba Naga (4) — Battlecry: If you've cast a spell while holding this, deal 3 damage.
			cards.Add("TSC_017", new CardDef(BattlecryAny, HeldSpell(p => p is Spell, new DamageTask(V("TSC_017", "damage", 3), EntityType.TARGET))));
			// [TSC_826] Crushclaw Enforcer (3) — Battlecry: If you've cast a spell while holding this, draw a Naga.
			cards.Add("TSC_826", new CardDef(HeldSpell(p => p is Spell, new CustomTask((g, c, s, t, stack) => DrawWhere(g, c, IsNaga)))));
			// [TSC_643] Spellcoiler (2) — Battlecry: If you've cast a spell while holding this, Discover a spell.
			cards.Add("TSC_643", new CardDef(HeldSpell(p => p is Spell, new DiscoverTask(CardType.SPELL))));
			// [TID_002] Herald of Nature (3) — Battlecry: If you've cast a Nature spell while holding this, give your other minions
			// +1/+1.
			cards.Add("TID_002", new CardDef(HeldSpell(p => IsSchoolSpell(p, NATURE), new CustomTask((g, c, s, t, stack) =>
				GiveAll(g, s, c.BoardZone.Where(m => m.Id != s.Id && !m.IsDead), "TID_002e")))));
			cards.Add("TID_002e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TID_002", "stats", 1))) }));
			// [TID_085] Herald of Light (3) — Battlecry: If you've cast a Holy spell while holding this, restore 6 Health to all
			// friendly characters.
			cards.Add("TID_085", new CardDef(HeldSpell(p => IsSchoolSpell(p, HOLY), new CustomTask((g, c, s, t, stack) =>
			{
				var all = new List<ICharacter> { c.Hero };
				all.AddRange(c.BoardZone.Where(m => !m.IsDead));
				foreach (ICharacter ch in all) ch.TakeHeal((IPlayable)s, V("TID_085", "heal", 6));
			}))));
			// [TID_706] Herald of Chaos (3) — Lifesteal. Battlecry: If you've cast a Fel spell while holding this, gain Rush.
			cards.Add("TID_706", new CardDef(HeldSpell(p => IsSchoolSpell(p, FEL), new AddEnchantmentTask("TID_706e", EntityType.SOURCE))));
			cards.Add("TID_706e", new CardDef(new Power { Enchant = new Enchant(Effects.Rush) }));
			// [TID_717] Herald of Shadows (3) — Battlecry: If you've cast a Shadow spell while holding this, steal 2 Health from a
			// minion.
			cards.Add("TID_717", new CardDef(BattlecryMinion, HeldSpell(p => IsSchoolSpell(p, SHADOW), new CustomTask((g, c, s, t, stack) =>
			{
				if (!(t is Minion victim) || !(s is Minion me)) return;
				Give(g, s, victim, victim.Controller == c ? "TID_717eb" : "TID_717e");
				Give(g, s, me, victim.Controller == c ? "TID_717e2b" : "TID_717e2");
			}))));
			foreach (string id in new[] { "TID_717e", "TID_717eb" })
				cards.Add(id, new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(-V("TID_717", "health", 2))) }));
			foreach (string id in new[] { "TID_717e2", "TID_717e2b" })
				cards.Add(id, new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(V("TID_717", "health", 2))) }));
			// Three spells while holding this (the card data's threshold 3, "({0} left!)")
			// [TID_074] Ancient Krakenbane (3) — Battlecry: … deal 5 damage.
			cards.Add("TID_074", new CardDef(BattlecryAny, new Power
			{
				Trigger = WhileHolding(TriggerType.CAST_SPELL, p => p is Spell),
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (Held(s) && t is ICharacter target) Generic.DamageCharFunc((IPlayable)s, target, V("TID_074", "damage", 5), false); })
			}));
			// [TSC_064] Slithering Deathscale (7) — Battlecry: … deal 3 damage to all enemies.
			cards.Add("TSC_064", new CardDef(new Power
			{
				Trigger = WhileHolding(TriggerType.CAST_SPELL, p => p is Spell),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!Held(s)) return;
					var enemies = new List<ICharacter> { c.Opponent.Hero };
					enemies.AddRange(c.Opponent.BoardZone.Where(m => !m.IsDead));
					foreach (ICharacter e in enemies) Generic.DamageCharFunc((IPlayable)s, e, V("TSC_064", "damage", 3), false);
				})
			}));
			// [TSC_212] Handmaiden (3) — Battlecry: … draw 3 cards.
			cards.Add("TSC_212", new CardDef(new Power
			{
				Trigger = WhileHolding(TriggerType.CAST_SPELL, p => p is Spell),
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (Held(s)) for (int i = 0; i < V("TSC_212", "cards", 3); i++) Generic.Draw(c); })
			}));
			// [TSC_609] Coilskar Commander (6) — Taunt. Battlecry: … summon two copies of this.
			cards.Add("TSC_609", new CardDef(new Power
			{
				Trigger = WhileHolding(TriggerType.CAST_SPELL, p => p is Spell),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!Held(s) || !(s is Minion me)) return;
					for (int i = 0; i < V("TSC_609", "copies", 2); i++) new SummonCopyTask(EntityType.SOURCE).Process(g, c, s, null);
				})
			}));
			// Spells: "If you played a Naga while holding this"
			// [TSC_023] Barbed Nets (1) — Deal $2 damage to an enemy. If you played a Naga while holding this, choose a second target.
			cards.Add("TSC_023", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_ENEMY_TARGET, 0 } }, new Power
			{
				Trigger = NagaWhileHolding,
				ThresholdValue = 1,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is ICharacter first) Generic.DamageCharFunc((IPlayable)s, first, V("TSC_023", "damage", 2), true);
					if (!Held(s)) return;
					Generic.PickTargets(c, s, 1, ch => ch.Controller != c && ch.Id != t?.Id,
						(g2, c2, s2, second) => Generic.DamageCharFunc((IPlayable)s2, second, V("TSC_023", "damage", 2), true));
				})
			}));
			// [TSC_058] Predation (2) — Deal $2 damage. Costs (0) if you played a Naga while holding this.
			cards.Add("TSC_058", new CardDef(TargetAny, new Power
			{
				Trigger = NagaWhileHolding,
				ThresholdValue = 1,
				Aura = new AdaptiveCostEffect(p => 0, EffectOperator.SET, new SelfCondition(p => Generic.ThresholdMet(p))),
				PowerTask = new DamageTask(V("TSC_058", "damage", 2), EntityType.TARGET, true)
			}));
			// [TSC_215] Serpent Wig (1) — Give a minion +1/+2. If you played a Naga while holding this, add a Serpent Wig to your hand.
			cards.Add("TSC_215", new CardDef(TargetMinion, new Power
			{
				Trigger = NagaWhileHolding,
				ThresholdValue = 1,
				PowerTask = ComplexTask.Create(new AddEnchantmentTask("TSC_215e", EntityType.TARGET),
					new CustomTask((g, c, s, t, stack) => { if (Held(s)) AddToHand(c, Cards.FromId("TSC_215")); }))
			}));
			cards.Add("TSC_215e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("TSC_215", "attack", 1)), Effects.Health_N(V("TSC_215", "health", 2))) }));
			// [TSC_651] Seaweed Strike (3) — Deal $4 damage to a minion. If you played a Naga while holding this, also give your
			// hero +4 Attack this turn.
			cards.Add("TSC_651", new CardDef(TargetMinion, new Power
			{
				Trigger = NagaWhileHolding,
				ThresholdValue = 1,
				PowerTask = ComplexTask.Create(new DamageTask(V("TSC_651", "damage", 4), EntityType.TARGET, true),
					new CustomTask((g, c, s, t, stack) => { if (Held(s)) Give(g, s, c.Hero, "TSC_651e"); }))
			}));
			cards.Add("TSC_651e", OneTurn(Effects.Attack_N(V("TSC_651", "attack", 4))));
			// [TSC_947] Naga's Pride (3) — Summon two 2/2 Lionfish. If you played a Naga while holding this, give them +1/+1.
			cards.Add("TSC_947", new CardDef(new Power
			{
				Trigger = NagaWhileHolding,
				ThresholdValue = 1,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					bool fed = Held(s);
					for (int i = 0; i < V("TSC_947", "lionfish", 2) && !c.BoardZone.IsFull; i++)
					{
						var fish = (Minion)Entity.FromCard(c, Cards.FromId("TSC_947t"));
						Generic.SummonBlock.Invoke(g, fish, -1, s);
						if (fed && fish.Zone?.Type == Zone.PLAY) Give(g, s, fish, "TSC_947e");
					}
				})
			}));
			cards.Add("TSC_947e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TSC_947", "stats", 1))) }));
			// [TSC_948] Gifts of Azshara (2) — Draw a card. If you played a Naga while holding this, do it again.
			cards.Add("TSC_948", new CardDef(new Power
			{
				Trigger = NagaWhileHolding,
				ThresholdValue = 1,
				PowerTask = new CustomTask((g, c, s, t, stack) => { Generic.Draw(c); if (Held(s)) Generic.Draw(c); })
			}));
			// [TSC_071] Twinbow Terrorcoil (4) — Battlecry: If you've cast a spell while holding this, your next spell casts twice.
			cards.Add("TSC_071", new CardDef(HeldSpell(p => p is Spell, new AddEnchantmentTask("TSC_071e", EntityType.CONTROLLER))));
			cards.Add("TSC_071e", new CardDef(new Power
			{
				Enchant = new Enchant(new Effect(HsTag.SPELLS_CAST_TWICE, EffectOperator.ADD, 1)),
				Trigger = new Trigger(TriggerType.AFTER_CAST) { TriggerSource = TriggerSource.FRIENDLY, SingleTask = RemoveEnchantmentTask.Task }
			}));

			// ---------------------------------------------------------------- "Put … on the bottom of your deck" (Azsharan → Sunken)
			// [TSC_039] Azsharan Scavenger (2) — Battlecry: Put a 'Sunken Scavenger' on the bottom of your deck.
			cards.Add("TSC_039", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => Generic.PutOnBottom(c, s, Cards.FromId("TSC_039t"))) }));
			// [TSC_039t] Sunken Scavenger (2) — Battlecry: Give your other Murlocs +1/+1 (wherever they are).
			cards.Add("TSC_039t", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					GiveAll(g, s, c.BoardZone.Cast<IPlayable>().Concat(c.HandZone).Concat(c.DeckZone).Where(p => p.Id != s.Id && IsMurloc(p)), "TSC_039te"))
			}));
			cards.Add("TSC_039te", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TSC_039t", "stats", 1))) }));
			// [TSC_057] Azsharan Defector (4) — Rush. Deathrattle: Put a 'Sunken Defector' on the bottom of your deck.
			cards.Add("TSC_057", new CardDef(new Power { DeathrattleTask = new CustomTask((g, c, s, t, stack) => Generic.PutOnBottom(c, s, Cards.FromId("TSC_057t"))) }));
			// [TSC_057t] Sunken Defector (4) — Charge. After this attacks, deal 5 damage to a random enemy minion.
			cards.Add("TSC_057t", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						var enemies = c.Opponent.BoardZone.Where(m => !m.IsDead && !m.ToBeDestroyed && !m.Untouchable).ToList();
						if (enemies.Count > 0) Generic.DamageCharFunc((IPlayable)s, enemies[g.Random.Next(enemies.Count)], V("TSC_057t", "damage", 5), false);
					})
				}
			}));
			// [TSC_644] Azsharan Mooncatcher (3) — Divine Shield. Battlecry: Put a 'Sunken Mooncatcher' on the bottom of your deck.
			cards.Add("TSC_644", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => Generic.PutOnBottom(c, s, Cards.FromId("TSC_644t"))) }));
			cards.Add("TSC_644t", new CardDef(new Power { PowerTask = new SummonCopyTask(EntityType.SOURCE) }));
			// [TSC_772] Azsharan Scroll (1) — Discover a Fire, Frost or Nature spell. Put a 'Sunken Scroll' on the bottom of your deck.
			cards.Add("TSC_772", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.SPELL && (x[SPELL_SCHOOL] == FIRE || x[SPELL_SCHOOL] == FROST || x[SPELL_SCHOOL] == NATURE)),
					new CustomTask((g, c, s, t, stack) => Generic.PutOnBottom(c, s, Cards.FromId("TSC_772t"))))
			}));
			// [TSC_772t] Sunken Scroll (1) — Add a Fire, Frost, and Nature spell from your class to your hand.
			cards.Add("TSC_772t", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (int school in new[] { FIRE, FROST, NATURE })
						if (RandomCard(g, x => x.Type == CardType.SPELL && x.HasClass(c.HeroClass) && x[SPELL_SCHOOL] == school) is Card spell) AddToHand(c, spell);
				})
			}));
			// [TSC_775] Azsharan Ritual (4) — Silence a minion and summon a copy of it. Put a 'Sunken Ritual' on the bottom of your deck.
			cards.Add("TSC_775", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(new SilenceTask(EntityType.TARGET), new SummonCopyTask(EntityType.TARGET),
					new CustomTask((g, c, s, t, stack) => Generic.PutOnBottom(c, s, Cards.FromId("TSC_775t"))))
			}));
			// [TSC_775t] Sunken Ritual (4) — Silence a minion and summon 2 copies of it.
			cards.Add("TSC_775t", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(new SilenceTask(EntityType.TARGET), new SummonCopyTask(EntityType.TARGET), new SummonCopyTask(EntityType.TARGET))
			}));
			// [TSC_776] Azsharan Sweeper (3) — Battlecry: Put a 'Sunken Sweeper' on the bottom of your deck.
			cards.Add("TSC_776", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => Generic.PutOnBottom(c, s, Cards.FromId("TSC_776t"))) }));
			// [TSC_776t] Sunken Sweeper (3) — Battlecry: Add 3 random Mechs to your hand. They cost (1) less.
			cards.Add("TSC_776t", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("TSC_776t", "mechs", 3) && !c.HandZone.IsFull; i++)
					{
						if (!(RandomCard(g, x => x.Type == CardType.MINION && x.IsRace(Race.MECHANICAL)) is Card mech)) break;
						IPlayable p = Entity.FromCard(c, mech);
						if (Generic.AddHandPhase.Invoke(c, p)) Give(g, s, p, "GBL_003e");
					}
				})
			}));
			// [TSC_912] Azsharan Vessel (5) — Summon two 3/3 Pirates with Stealth. Put a 'Sunken Vessel' on the bottom of your deck.
			cards.Add("TSC_912", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(ComplexTask.Repeat(new SummonTask("TSC_912t3", SummonSide.SPELL), V("TSC_912", "pirates", 2)),
					new CustomTask((g, c, s, t, stack) => Generic.PutOnBottom(c, s, Cards.FromId("TSC_912t"))))
			}));
			// [TSC_912t] Sunken Vessel (5) — Casts When Drawn. Summon two 3/3 Pirates with Stealth.
			cards.Add("TSC_912t", new CardDef(new Power { PowerTask = ComplexTask.Repeat(new SummonTask("TSC_912t2", SummonSide.SPELL), V("TSC_912t", "pirates", 2)) }));
			// [TSC_913] Azsharan Trident (3, weapon) — Deathrattle: Put a 'Sunken Trident' on the bottom of your deck.
			cards.Add("TSC_913", new CardDef(new Power { DeathrattleTask = new CustomTask((g, c, s, t, stack) => Generic.PutOnBottom(c, s, Cards.FromId("TSC_913t"))) }));
			// [TSC_913t] Sunken Trident (3, weapon) — After your hero attacks, deal 2 damage to all enemy minions.
			cards.Add("TSC_913t", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						foreach (Minion m in c.Opponent.BoardZone.Where(x => !x.IsDead).ToList()) Generic.DamageCharFunc((IPlayable)s, m, V("TSC_913t", "damage", 2), false);
					})
				}
			}));
			// [TSC_919] Azsharan Sentinel (5) — Taunt. Deathrattle: Put a 'Sunken Sentinel' on the bottom of your deck.
			cards.Add("TSC_919", new CardDef(new Power { DeathrattleTask = new CustomTask((g, c, s, t, stack) => Generic.PutOnBottom(c, s, Cards.FromId("TSC_919t"))) }));
			// [TSC_927] Azsharan Gardens (1) — Give all minions in your hand +1/+1. Put a 'Sunken Gardens' on the bottom of your deck.
			cards.Add("TSC_927", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					GiveAll(g, s, c.HandZone.Where(p => Generic.IsMinionCard(p)), "TSC_927e");
					Generic.PutOnBottom(c, s, Cards.FromId("TSC_927t"));
				})
			}));
			cards.Add("TSC_927e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TSC_927", "stats", 1))) }));
			// [TSC_927t] Sunken Gardens (1) — Give +1/+1 to all minions in your hand, deck, and battlefield.
			cards.Add("TSC_927t", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					GiveAll(g, s, c.HandZone.Concat(c.DeckZone).Concat(c.BoardZone.Where(m => !m.IsDead)).Where(p => Generic.IsMinionCard(p)), "TSC_927e"))
			}));
			// [TSC_945] Azsharan Saber (4) — Rush. Deathrattle: Put a 'Sunken Saber' on the bottom of your deck.
			cards.Add("TSC_945", new CardDef(new Power { DeathrattleTask = new CustomTask((g, c, s, t, stack) => Generic.PutOnBottom(c, s, Cards.FromId("TSC_945t"))) }));
			// [TSC_945t] Sunken Saber (4) — Rush. Deathrattle: Summon a Beast from your deck.
			cards.Add("TSC_945t", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => SummonFromDeck(g, c, s, p => IsRaceCard(p, Race.BEAST)))
			}));
			// [TSC_653] Bottomfeeder (1) — Deathrattle: Add a Bottomfeeder to the bottom of your deck with permanent +2/+2.
			cards.Add("TSC_653", new CardDef(new Power
			{
				// "permanent +2/+2" (no client enchantment): the new card's own stats, on top of the dead one's permanent ones
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Minion dead) || c.DeckZone.IsFull) return;
					var data = ((Entity)dead)._data;
					int atk = (data.TryGetValue(GameTag.ATK, out int a) ? a : dead.Card.ATK) + V("TSC_653", "stats", 2);
					int hp = (data.TryGetValue(GameTag.HEALTH, out int h) ? h : dead.Card.Health) + V("TSC_653", "stats", 2);
					IPlayable fresh = Entity.FromCard(c, dead.Card, new EntityData { { GameTag.CREATOR, s.Id }, { GameTag.ATK, atk }, { GameTag.HEALTH, hp } });
					c.DeckZone.Add(fresh, 0);
					if (g.History) g.PowerHistory.Add(Kettle.PowerHistoryBuilder.HideEntity(fresh));
				})
			}));

			SunkenCityBatch1b(cards);
		}
	}
}
