// HsServer addition (not upstream SabberStone): Perils in Paradise — chunk 4: Shaman, Warrior and Warlock cards and the Travel
// Agency's (spec: research spec_chunk4; shared pieces: IslandCardsGenHs.cs, Actions/Paradise.cs). Every number goes through
// CardValues (V).
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
		/// <summary>Carress' seven school bonuses: the sentence each form's text holds for it (wiki Carress, Cabaret Star) and what it
		/// does (client SPELL_SCHOOL values: Arcane 1, Fire 2, Frost 3, Nature 4, Holy 5, Shadow 6, Fel 7).</summary>
		private static readonly (int School, string Marker)[] CarressSchools =
		{
			(1, "Draw 2 cards"), (2, "enemy hero"), (3, "Freeze"), (4, "Taunt"), (5, "Restore"), (6, "Destroy"), (7, "all enemy minions"),
		};

		private static void CarressBonus(Game g, Controller c, IEntity s, int school)
		{
			switch (school)
			{
				case 1: for (int i = 0; i < V("VAC_449", "cards", 2); i++) Generic.Draw(c); break;
				case 2: Generic.DamageCharFunc((IPlayable)s, c.Opponent.Hero, V("VAC_449", "hero damage", 6), false); break;
				case 3:
					foreach (Minion m in RandomOf(g, c.Opponent.BoardZone.GetAll().Where(m => !m.IsDead && !m.Untouchable), V("VAC_449", "frozen", 3)))
						IslandFreeze(g, c, s, m);
					break;
				case 4:
					if (s is Minion me && me.Zone?.Type == Zone.PLAY) { Give(g, s, me, "VAC_449e1"); me[GameTag.TAUNT] = 1; }
					break;
				case 5: new HealTask(V("VAC_449", "heal", 6), EntityType.HERO).Process(g, c, s, null); break;
				case 6:
					foreach (Minion m in RandomOf(g, c.Opponent.BoardZone.GetAll().Where(m => !m.IsDead && !m.ToBeDestroyed && !m.Untouchable), V("VAC_449", "destroyed", 2)))
						Generic.Destroy(m, s);
					break;
				case 7: DamageAll(s, c.Opponent.BoardZone.GetAll().Where(m => !m.IsDead).Cast<ICharacter>().ToList(), V("VAC_449", "damage", 2), false); break;
			}
		}

		/// <summary>The schools a Carress form's text names, in text order.</summary>
		private static List<int> CarressSchoolsOf(Card form) =>
			CarressSchools.Select(x => (x.School, At: form.Text?.IndexOf(x.Marker, StringComparison.Ordinal) ?? -1))
				.Where(x => x.At >= 0).OrderBy(x => x.At).Select(x => x.School).ToList();

		/// <summary>The Ryecleaver: when two Slices of Bread are in the hand, the minions between them (hand order) go into a Minion
		/// Sandwich (the same entities, set aside: wiki "stuff all minions in between"), the Slices leave, the Sandwich comes in.</summary>
		private static void IslandMakeSandwich(Game g, Controller c, IEntity source)
		{
			Card slice = Cards.FromId("VAC_525t1");
			var hand = c.HandZone.GetAll().ToList();
			var slices = hand.Where(p => p.Card == slice).Take(2).ToList();
			if (slices.Count < 2) return;
			int from = hand.IndexOf(slices[0]), to = hand.IndexOf(slices[1]);
			var stuffed = hand.Skip(from + 1).Take(to - from - 1).Where(p => p is Minion).Take(10).ToList();
			foreach (IPlayable p in slices.Concat(stuffed)) Generic.RemoveFromHand(c, p);
			if (!(IslandToHand(c, source, Cards.FromId("VAC_525t2")) is IPlayable sandwich)) return;
			for (int i = 0; i < stuffed.Count; i++) sandwich[HsTag.STUFFED_BASE + i] = stuffed[i].Id;
			sandwich[GameTag.TAG_SCRIPT_DATA_NUM_1] = stuffed.Count;
			if (stuffed.Count > 0) sandwich[GameTag.USE_ALTERNATE_CARD_TEXT] = 1;
		}

		private static void IslandChunk4(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Shaman
			// [VAC_301] Razzle-Dazzler (7) — Battlecry: Summon a random 5-Cost minion. Repeat for each spell school you've cast this
			// game. (@)
			cards.Add("VAC_301", new CardDef(new Power
			{
				HandCounter = p => Generic.SpellSchoolsCast(p.Controller).Count,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = 1 + Generic.SpellSchoolsCast(c).Count;
					for (int i = 0; i < n && !c.BoardZone.IsFull; i++) Summon(g, c, RandomMinionOfCost(g, V("VAC_301", "cost", 5)), s);
				})
			}));
			// [VAC_305] Frosty Décor (5) — Summon two 2/4 Elementals with Taunt and "Deathrattle: Gain 4 Armor".
			cards.Add("VAC_305", new CardDef(new Power { PowerTask = new SummonTask("VAC_305t", V("VAC_305", "elementals", 2), SummonSide.SPELL) }));
			cards.Add("VAC_305t", new CardDef(new Power { DeathrattleTask = new ArmorTask(V("VAC_305t", "armor", 4)) }));
			// [VAC_308] Siren Song (2) — Get two random spells from spell schools you haven't cast this game. (Live heist 122856: any
			// class, two schools.)
			cards.Add("VAC_308", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Card spell in TitansC2SchoolOffer(g, c, V("VAC_308", "spells", 2))) AddToHand(c, spell);
				})
			}));
			// [VAC_324] Matching Outfits (4) — Transform a minion into a random one that costs (1) more, then summon a copy of it. (Forum
			// t/131677: the copy is the caster's.)
			cards.Add("VAC_324", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m) || !(RandomMinionOfCost(g, m.Cost + 1) is Card into)) return;
					Controller owner = m.Controller;
					int pos = m.ZonePosition;
					if (!Generic.TransformBlock.Invoke(owner, into, m)) return;
					if (pos < owner.BoardZone.Count && owner.BoardZone[pos] is Minion now && !c.BoardZone.IsFull) Generic.Copy(c, s, now, Zone.PLAY);
				})
			}));
			// [VAC_328] Meltemental (3) — Taunt. This is permanently Frozen. (Its own text: wiki 30.6.0 — copies keep it.)
			cards.Add("VAC_328", new CardDef(new Power
			{
				Aura = new Aura(AuraType.SELF, new Effect(GameTag.FROZEN, EffectOperator.SET, 1), new Effect(HsTag.STAYS_FROZEN, EffectOperator.SET, 1))
			}));
			// [VAC_329] Natural Talent (3) — Get a random Naga and a random spell. They cost (2) less.
			cards.Add("VAC_329", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Card got in new[] { RandomCard(g, x => x.Type == CardType.MINION && x.IsRace(NAGA)), RandomCard(g, x => x.Type == CardType.SPELL) })
						if (IslandToHand(c, s, got) is IPlayable p) GiveCostLess(g, s, p, V("VAC_329", "discount", 2));
				})
			}));
			// [VAC_449] Carress, Cabaret Star (3) — While in your hand, play two different spell schools to transform. (Firestone: the
			// first spell of each school played while holding her.)
			cards.Add("VAC_449", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerActivation = TriggerActivation.HAND,
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p is Spell && p[SPELL_SCHOOL] > 0),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is IPlayable me) || me.Zone?.Type != Zone.HAND || !(g.CurrentEventData?.EventSource is Spell spell)) return;
						int bit = 1 << spell[SPELL_SCHOOL];
						if ((me[HsTag.SCHOOLS_SEEN] & bit) != 0) return;
						me[HsTag.SCHOOLS_SEEN] = me[HsTag.SCHOOLS_SEEN] | bit;
						var seen = Enumerable.Range(1, 15).Where(x => (me[HsTag.SCHOOLS_SEEN] & (1 << x)) != 0).ToList();
						me[GameTag.TAG_SCRIPT_DATA_NUM_1] = Math.Min(seen.Count, 2);
						if (seen.Count < V("VAC_449", "schools", 2)) return;
						var pair = seen.Take(2).ToHashSet();
						Card form = new[] { "VAC_449t" }.Concat(Enumerable.Range(1, 20).Select(i => "VAC_449t" + i)).Select(Cards.TryFromId)
							.FirstOrDefault(x => x != null && CarressSchoolsOf(x).ToHashSet().SetEquals(pair));
						if (form != null) Generic.ChangeInHand(c, me, form);
					})
				}
			}));
			foreach (string formId in new[] { "VAC_449t" }.Concat(Enumerable.Range(1, 20).Select(i => "VAC_449t" + i)))
			{
				Card form = Cards.TryFromId(formId);
				if (form == null) continue;
				var schools = CarressSchoolsOf(form);
				cards.Add(formId, new CardDef(new Power
				{
					PowerTask = new CustomTask((g, c, s, t, stack) => { foreach (int school in schools) CarressBonus(g, c, s, school); })
				}));
			}
			cards.Add("VAC_449e1", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("VAC_449", "stats", 2))) }));
			// [VAC_450] Carefree Cookie (3) — Demon Hunter Tourist. After a friendly minion dies, summon a random minion that costs (1)
			// more.
			cards.Add("VAC_450", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DEATH)
				{
					TriggerSource = TriggerSource.MINIONS_EXCEPT_SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (g.CurrentEventData?.EventSource is Minion dead) Summon(g, c, RandomMinionOfCost(g, dead.Cost + V("VAC_450", "more", 1)), s);
					})
				}
			}));
			// [VAC_954] Cabaret Headliner (4) — Battlecry: Reduce the Cost of a spell of each school in your hand by (2). (Forum
			// t/132145 + 30.2.0: the game picks, a spell that costs more than 0 first.)
			cards.Add("VAC_954", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (var school in c.HandZone.GetAll().Where(p => p is Spell && p[SPELL_SCHOOL] > 0).GroupBy(p => p[SPELL_SCHOOL]).ToList())
					{
						var options = school.Where(p => p.Cost > 0).ToList();
						if (options.Count == 0) options = school.ToList();
						Give(g, s, options[g.Random.Next(options.Count)], "VAC_954e1");
					}
				})
			}));
			cards.Add("VAC_954e1", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("VAC_954", "discount", 2))) }));
			// [WORK_011] Hydration Totem (2) — At the end of your turn, give adjacent minions +1/+1.
			cards.Add("WORK_011", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) => { if (s is Minion me) GiveAll(g, s, Neighbors(me).Where(m => !(m is Location)).ToList(), "WORK_011e"); })
				}
			}));
			cards.Add("WORK_011e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("WORK_011", "stats", 1))) }));
			// [WORK_013] Turbulus (3) — Hunter Tourist. Battlecry: Give +1/+1 to all other Battlecry minions in your hand, deck, and
			// battlefield. (Live dungar 12675: minions only, not a hero card.)
			cards.Add("WORK_013", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					GiveAll(g, s, c.HandZone.GetAll().Concat(c.DeckZone.GetAll()).Concat(c.BoardZone.GetAll())
						.Where(p => p != s && p is Minion && !(p is Location) && p.Card[GameTag.BATTLECRY] > 0).ToList(), "WORK_013e"))
			}));
			cards.Add("WORK_013e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("WORK_013", "stats", 1))) }));
			// [WORK_030] Icecrown Brochure (2) — Deal $3 damage to a minion and Freeze its neighbors. (Flips each turn.) / [WORK_030t]
			// Blackrock Brochure — Deal $3 damage to a minion and $1 to its neighbors.
			cards.Add("WORK_030", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					var next = Neighbors(m).Where(x => !(x is Location)).ToList();
					Generic.DamageCharFunc((IPlayable)s, m, V("WORK_030", "damage", 3), true);
					foreach (Minion n in next) IslandFreeze(g, c, s, n);
				})
			}));
			cards.Add("WORK_030t", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					var next = Neighbors(m).Where(x => !(x is Location)).ToList();
					Generic.DamageCharFunc((IPlayable)s, m, V("WORK_030t", "damage", 3), true);
					foreach (Minion n in next) Generic.DamageCharFunc((IPlayable)s, n, V("WORK_030t", "splash", 1), true);
				})
			}));

			// ---------------------------------------------------------------- Warrior
			// [VAC_337] Line Cook (3) — Tradeable. Taunt. When you draw this, get a copy of it. (Live vfx-decks 248294, 6 of 6.)
			cards.Add("VAC_337", new CardDef(new Power
			{
				TopdeckTask = new CustomTask((g, c, s, t, stack) => { if (!c.HandZone.IsFull) Generic.Copy(c, s, (IPlayable)s, Zone.HAND); })
			}));
			// [VAC_339] Muensterosity (9) — Taunt. At the end of your turn, summon an Elemental with stats equal to this minion's.
			cards.Add("VAC_339", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is Minion me) SummonWithStats(g, c, Cards.FromId("VAC_339t"), me.AttackDamage, me.Health, s);
					})
				}
			}));
			// [VAC_340] Hamm, the Hungry (6) — Druid Tourist. Taunt. At the end of your turn, eat a minion in the enemy's deck to gain
			// +2/+2. (Live night 224614 48701: the eaten card to the graveyard, VAC_340e 2/2.)
			cards.Add("VAC_340", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						var minions = c.Opponent.DeckZone.GetAll().Where(p => Generic.IsMinionCard(p)).ToList();
						if (minions.Count == 0 || !(s is Minion hamm)) return;
						IPlayable eaten = minions[g.Random.Next(minions.Count)];
						Generic.NoteAffected(eaten, s.Id);
						c.Opponent.GraveyardZone.Add(c.Opponent.DeckZone.Remove(eaten));
						int stats = V("VAC_340", "stats", 2);
						Generic.AddEnchantmentBlock(g, Cards.FromId("VAC_340e"), hamm, hamm, stats, stats, 0);
					})
				}
			}));
			cards.Add("VAC_340e", new CardDef(new Power { Enchant = Enchants.Enchants.AddAttackHealthScriptTag }));
			// [VAC_341] Undercooked Calamari (4) — Battlecry: Destroy an enemy minion with Attack less than or equal to this minion's.
			cards.Add("VAC_341", new CardDef(BattlecryEnemyMinion, new Power
			{
				TargetIf = (PlayReq.REQ_TARGET_MAX_ATTACK, (src, target) => target is Minion m && m.AttackDamage <= (src is Minion me ? me.AttackDamage : src.Card.ATK)),
				PowerTask = new DestroyTask(EntityType.TARGET)
			}));
			// [VAC_525] The Ryecleaver (5, weapon) — Battlecry and Deathrattle: Get a Slice of Bread. (Get 2 to Sandwich any minions in
			// between!)
			ISimpleTask slice = new CustomTask((g, c, s, t, stack) =>
			{
				IslandToHand(c, s, Cards.FromId("VAC_525t1"));
				IslandMakeSandwich(g, c, s);
			});
			cards.Add("VAC_525", new CardDef(new Power { PowerTask = slice, DeathrattleTask = slice }));
			cards.Add("VAC_525t1", new CardDef(new Power { PlayableIf = (PlayReq.REQ_LITERALLY_UNPLAYABLE, ctl => false) }));
			// [VAC_525t2] Minion Sandwich (2) — Summon the minions stuffed in this Sandwich. (Summoned, not played: no Battlecries.)
			cards.Add("VAC_525t2", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < 10; i++)
					{
						int id = s[HsTag.STUFFED_BASE + i];
						if (id <= 0 || c.BoardZone.IsFull) continue;
						if (!g.IdEntityDic.TryGetValue(id, out IPlayable p) || !(p is Minion m) || m.Zone?.Type != Zone.SETASIDE) continue;
						c.SetasideZone.Remove(m);
						Generic.SummonBlock.Invoke(g, m, -1, s);
					}
				})
			}));
			// [VAC_526] Char (4) — Deal $7 damage to a minion. Give a random minion in your hand stats equal to the excess damage.
			cards.Add("VAC_526", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					int excess = Generic.DamageWithExcess((IPlayable)s, m, V("VAC_526", "damage", 7), true); // (the shared capped excess hit)
					var minions = c.HandZone.GetAll().Where(p => Generic.IsMinionCard(p)).ToList();
					if (excess > 0 && minions.Count > 0)
						Generic.AddEnchantmentBlock(g, Cards.FromId("VAC_526e"), (IPlayable)s, minions[g.Random.Next(minions.Count)], excess, excess, 0);
				})
			}));
			cards.Add("VAC_526e", new CardDef(new Power { Enchant = Enchants.Enchants.AddAttackHealthScriptTag }));
			// [VAC_527] Draconic Delicacy (8) — Rush, Elusive. Can only take 1 damage at a time.
			cards.Add("VAC_527", new CardDef(new Power { Trigger = Generic.PreDamageCapTrigger(TriggerSource.SELF, V("VAC_527", "cap", 1)) }));
			// [VAC_528] All You Can Eat (3) — Draw three minions of different minion types.
			cards.Add("VAC_528", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable p in OfDifferentTypes(g, c.DeckZone.GetAll().Where(x => Generic.IsMinionCard(x)).ToList(), x => x.Card).Take(V("VAC_528", "minions", 3)))
						if (p.Zone?.Type == Zone.DECK) Generic.Draw(c, p);
				})
			}));
			// [VAC_533] Food Fight (3) — Summon a 0/4 Entrée for your opponent. When it dies, summon a minion from your deck.
			cards.Add("VAC_533", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => Summon(g, c.Opponent, Cards.FromId("VAC_533t"), s)) }));
			cards.Add("VAC_533t", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => SummonFromDeck(g, c.Opponent, s, p => p is Minion))
			}));
			// [WORK_021] Reserved Spot (3) — Give a random minion in your hand +4/+4. If it's the only one there, reduce its Cost by (2).
			// (Live 222041 15557: glow while one minion is in hand; WORK_021e then GBL_002e.)
			ISimpleTask reserve(bool alone) => new CustomTask((g, c, s, t, stack) =>
			{
				var minions = c.HandZone.GetAll().Where(p => Generic.IsMinionCard(p)).ToList();
				if (minions.Count == 0) return;
				IPlayable m = minions[g.Random.Next(minions.Count)];
				Give(g, s, m, "WORK_021e");
				if (alone) GiveCostLess(g, s, m, V("WORK_021", "discount", 2));
			});
			cards.Add("WORK_021", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new CustomTask((g, c, s, t, stack) => stack.Flag = c.HandZone.Count(p => Generic.IsMinionCard(p)) == 1),
					new FlagTask(true, reserve(true)), new FlagTask(false, reserve(false)))
			}));
			cards.Add("WORK_021e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("WORK_021", "stats", 4))) }));
			// [WORK_022] Punch Card (3) — Give your hero +3 Attack and "Also damages adjacent minions" this turn. (Reaper's Scythe's
			// shape: a one-turn enchantment with the Cleave.)
			cards.Add("WORK_022", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => Give(g, s, c.Hero, "WORK_022e")) }));
			cards.Add("WORK_022e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.Attack_N(V("WORK_022", "attack", 3))) { IsOneTurnEffect = true },
				Trigger = Cleave(TriggerSource.HERO)
			}));
			// [WORK_023] Alloy Advisor (4) — Taunt. Whenever this takes damage, gain 3 Armor.
			cards.Add("WORK_023", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TAKE_DAMAGE) { TriggerSource = TriggerSource.SELF, SingleTask = new ArmorTask(V("WORK_023", "armor", 3)) }
			}));

			// ---------------------------------------------------------------- Warlock
			// [VAC_503] Summoner Darkmarrow (4) — Death Knight Tourist. Your Deathrattles trigger twice. After you play a Deathrattle
			// minion, destroy it.
			cards.Add("VAC_503", new CardDef(new Power
			{
				Aura = new Aura(AuraType.CONTROLLER, new Effect(GameTag.EXTRA_DEATHRATTLES_BASE, EffectOperator.SET, 1)) { ShowAs = "VAC_503e" },
				Trigger = new Trigger(TriggerType.AFTER_PLAY_MINION)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p is Minion m && m.HasDeathrattle),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if ((t ?? g.CurrentEventData?.EventSource as IPlayable) is Minion m && m.Zone?.Type == Zone.PLAY && !m.ToBeDestroyed) Generic.Destroy(m, s);
					})
				}
			}));
			// [VAC_939] Eat! The! Imp! (2) — Destroy a friendly minion to draw 3 cards.
			cards.Add("VAC_939", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 } }, new Power
			{
				PowerTask = ComplexTask.Create(new DestroyTask(EntityType.TARGET), ComplexTask.Repeat(new DrawTask(), V("VAC_939", "cards", 3)))
			}));
			// [VAC_940] Party Fiend (1) — Battlecry: Summon two 1/1 Felbeasts. Deal 3 damage to your hero.
			cards.Add("VAC_940", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("VAC_940", "felbeasts", 2); i++) Summon(g, c, Cards.FromId("VAC_940t"), s);
				}), new DamageTask(V("VAC_940", "damage", 3), EntityType.HERO))
			}));
			// [VAC_941] Announce Darkness (1) — Replace your Hero Power and non-Warlock cards with Warlock ones. They cost (1) less.
			// (Wiki: hand and deck, Life Tap; random Warlock cards of any type.)
			cards.Add("VAC_941", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					new ReplaceHeroPower(Cards.FromId("HERO_07bp")).Process(g, c, s, null);
					if (c.Hero.HeroPower is HeroPower hp) { hp[GameTag.HEROPOWER_ACTIVATIONS_THIS_TURN] = 0; hp.IsExhausted = false; Give(g, s, hp, "VAC_941e"); }
					var pool = Cards.GenerationPool(g, c).Where(x => x.Class == CardClass.WARLOCK).ToList();
					if (pool.Count == 0) return;
					foreach (IPlayable p in c.HandZone.GetAll().Where(p => !p.Card.HasClass(CardClass.WARLOCK)).ToList())
						if (Generic.ChangeEntityBlock(c, p, pool[g.Random.Next(pool.Count)], true) is IPlayable now) Give(g, s, now, "VAC_941e");
					foreach (IPlayable p in c.DeckZone.GetAll().Where(p => !p.Card.HasClass(CardClass.WARLOCK)).ToList())
					{
						g.SilentChanges = true;
						IPlayable now;
						try { now = Generic.ChangeEntityBlock(c, p, pool[g.Random.Next(pool.Count)], true); }
						finally { g.SilentChanges = false; }
						if (now != null) Give(g, s, now, "VAC_941e");
					}
				})
			}));
			cards.Add("VAC_941e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("VAC_941", "discount", 1))) }));
			// [VAC_942] Fearless Flamejuggler (2) — Battlecry: Gain stats equal to the damage your hero has taken this turn. (@)
			cards.Add("VAC_942", new CardDef(new Power
			{
				HandCounter = p => p.Controller[HsTag.HERO_DAMAGE_THIS_TURN],
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = c[HsTag.HERO_DAMAGE_THIS_TURN];
					if (n > 0 && s is Minion me) Generic.AddEnchantmentBlock(g, Cards.FromId("VAC_942e"), me, me, n, n, 0);
				})
			}));
			cards.Add("VAC_942e", new CardDef(new Power { Enchant = Enchants.Enchants.AddAttackHealthScriptTag }));
			// [VAC_943] Sacrificial Imp (3) — Deathrattle: If it's your turn, summon a 6/6 Imp with Taunt.
			cards.Add("VAC_943", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => { if (g.CurrentPlayer == c) Summon(g, c, Cards.FromId("VAC_943t"), s); })
			}));
			// [VAC_944] Cursed Souvenir (2) — Give a minion +3/+3 and "At the start of your turn, deal 3 damage to your hero." (Its owner's
			// turn; the minion deals it — wiki: Lifesteal works.)
			cards.Add("VAC_944", new CardDef(TargetMinion, new Power { PowerTask = new AddEnchantmentTask("VAC_944e", EntityType.TARGET) }));
			cards.Add("VAC_944e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.AttackHealth_N(V("VAC_944", "stats", 3))),
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					EitherTurn = true,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is Enchantment e && e.Target is Minion m && m.Zone?.Type == Zone.PLAY && g.CurrentPlayer == m.Controller)
							Generic.DamageCharFunc(m, m.Controller.Hero, V("VAC_944", "damage", 3), false);
					})
				}
			}));
			// [VAC_945] Party Planner Vona (6) — Battlecry: If you've taken 8 damage on your turns, summon Ourobos. (Client 3110.)
			cards.Add("VAC_945", new CardDef(new Power
			{
				ThresholdCount = p => p.Controller[Generic.DAMAGE_TAKEN_ON_OWN_TURNS],
				ThresholdValue = V("VAC_945", "damage", 8),
				PowerTask = ComplexTask.If((g, c, s) => Generic.ThresholdMet((IPlayable)s), new SummonTask("VAC_945t"))
			}));
			// [VAC_945t] Ourobos, World Serpent (8) — Taunt. Deathrattle: Give a minion in your hand "Deathrattle: Summon Ourobos."
			cards.Add("VAC_945t", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					var minions = c.HandZone.GetAll().Where(p => Generic.IsMinionCard(p)).ToList();
					if (minions.Count > 0) Give(g, s, minions[g.Random.Next(minions.Count)], "VAC_945e");
				})
			}));
			cards.Add("VAC_945e", new CardDef(new Power
			{
				Enchant = new Enchant(new Effect(GameTag.DEATHRATTLE, EffectOperator.SET, 1)),
				DeathrattleTask = new SummonTask("VAC_945t")
			}));
			// [VAC_952] Felfire Bonfire (4) — Deal $4 damage to a minion. If it dies, your next Deathrattle minion costs (3) less.
			cards.Add("VAC_952", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					Generic.DamageCharFunc((IPlayable)s, m, V("VAC_952", "damage", 4), true);
					if (m.IsDead || m.ToBeDestroyed || m.Health <= 0) Generic.AddEnchantmentBlock(g, Cards.FromId("VAC_952e"), (IPlayable)s, c, 0, 0, 0);
				})
			}));
			cards.Add("VAC_952e", NextCardCost(Effects.ReduceCost(V("VAC_952", "discount", 3)), new SelfCondition(p => p is Minion m && m.HasDeathrattle), "VAC_952e2"));
			// [WORK_007] Deadline (2) — Tradeable, Temporary. Destroy a minion. (Temporary as its own keyword: Generic.PrintedTemporary.)
			cards.Add("WORK_007", new CardDef(TargetMinion, new Power { PowerTask = new DestroyTask(EntityType.TARGET) }));
			// [WORK_008] Fine Print (4) — Deal $4 damage to all minions. (Excess damage hits your hero.) (The excess of each, worked out
			// before the hits; summed into one hit — doubt.)
			cards.Add("WORK_008", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var minions = c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll()).Where(m => !m.IsDead).ToList();
					int amount = V("WORK_008", "damage", 4);
					int excess = minions.Sum(m => Generic.ExcessDamage((IPlayable)s, m, amount, true));
					DamageAll(s, minions.Cast<ICharacter>().ToList(), amount, true);
					if (excess > 0) Generic.DamageCharFunc((IPlayable)s, c.Hero, excess, false);
				})
			}));
			// [WORK_009] Imployee of the Month (2) — Battlecry: Give a friendly minion Lifesteal.
			cards.Add("WORK_009", new CardDef(BattlecryFriendlyMinion, new Power { PowerTask = new AddEnchantmentTask("Work_009e", EntityType.TARGET) }));
			cards.Add("Work_009e", new CardDef(new Power { Enchant = new Enchant(new Effect(GameTag.LIFESTEAL, EffectOperator.SET, 1)) }));
		}
	}
}
