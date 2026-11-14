// HsServer addition (not upstream SabberStone): Whizbang's Workshop (WHIZBANG, set 1897) — chunk 2: Hunter, Mage, Druid (spec: research
// spec_chunk2). Shared pieces: Owlonius's Spell Damage doubling (Controller.CurrentSpellPower, HsTag.SPELL_DAMAGE_DOUBLERS), Secrets that
// triggered this game (HsTag.SECRET_TRIGGERED_ORDER), Discover a Secret with the class fallback (DiscoverFromTask.Secrets), Miniaturize /
// trade-to-upgrade (chunk 1). Every number goes through CardValues (V).
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
		/// <summary>1 damage to a random living enemy character (Jungle Gym, Triplewick Trickster, Toyrannosaurus), from <paramref name="source"/>.</summary>
		private static void DamageRandomEnemy(Game g, Controller c, IEntity source, int damage)
		{
			var enemies = Enemies(c).Where(x => !x.IsDead && !x.ToBeDestroyed && x.Health > 0).ToList();
			if (enemies.Count == 0) return;
			ICharacter victim = enemies[g.Random.Next(enemies.Count)];
			Generic.DamageCharFunc((IPlayable)source, victim, damage, false);
		}

		/// <summary>Puzzlemaster Khadgar's Wisdomball spells (the wiki's list): "helpful" now — a targeted one needs an enemy to aim at, a Secret
		/// one not already up (wiki: "The bar for what makes a spell useful is assumed to be very low"; "Spells with targets will always target
		/// enemies").</summary>
		private static readonly string[] WisdomballSpells =
			{ "CORE_CS2_023", "CS2_027", "CS2_022", "VAN_CS2_024", "CORE_CS2_029", "CORE_EX1_287", "CORE_EX1_289", "CORE_CS2_028", "EX1_277", "CS2_032" };

		private static bool WisdomballHelpful(Controller c, Card spell)
		{
			if (spell.IsSecret) return Generic.CanHoldSecret(c, spell);
			bool targeted = spell.RequiresTarget || spell.MustHaveTargetToPlay;
			if (!targeted) return spell.Text?.Contains("enem") != true || c.Opponent.BoardZone.Count > 0 || spell.Text.Contains("hero");
			bool minionOnly = spell.PlayRequirements.ContainsKey(PlayReq.REQ_MINION_TARGET);
			return c.Opponent.BoardZone.GetAll().Any(m => !m.Untouchable && !m.HasStealth && !m.CantBeTargetedBySpells) || !minionOnly;
		}

		private static void WhizbangChunk2(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Hunter
			// [MIS_104] Wilderness Pack (1) — Add 5 random Beasts to your hand. They are Temporary. (live INST29: GBL_999e, not MIS_104e)
			cards.Add("MIS_104", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("MIS_104", "beasts", 5); i++)
						if (GetCard(c, RandomCard(g, x => x.Type == CardType.MINION && x.IsRace(Race.BEAST)), s) is IPlayable got) Generic.MakeTemporary(got, s);
				})
			}));
			// [MIS_105] Bargain Bin (2) — Secret: After your opponent plays a minion, spell, or weapon, draw a card of the other 2 types.
			cards.Add("MIS_105", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_CARD)
				{
					TriggerSource = TriggerSource.ENEMY,
					Condition = new SelfCondition(p => p is Minion || p is Spell || p is Weapon),
					SingleTask = ComplexTask.Secret(new CustomTask((g, c, s, t, stack) =>
					{
						if (!(t is Minion)) DrawWhere(g, c, p => p is Minion);
						if (!(t is Spell)) DrawWhere(g, c, p => p is Spell);
						if (!(t is Weapon)) DrawWhere(g, c, p => p is Weapon);
					}))
				}
			}));
			// [MIS_914] Product 9 (5) — Battlecry: Recast every friendly Secret that triggered this game. (in the order they triggered; one copy
			// each, the Secret limit: forum t/135297)
			cards.Add("MIS_914", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var triggered = c.GraveyardZone.GetAll().Where(p => p is Spell && p[HsTag.SECRET_TRIGGERED_ORDER] > 0)
						.OrderBy(p => p[HsTag.SECRET_TRIGGERED_ORDER]).Select(p => p.Card).GroupBy(x => x.Id).Select(x => x.First()).ToList();
					foreach (Card secret in triggered)
					{
						if (!Generic.CanHoldSecret(c, secret)) continue;
						var made = (Spell)Entity.FromCard(c, secret, new EntityData { { GameTag.CREATOR, s.Id } });
						Generic.CastSpell.Invoke(c, g, made, null, 0);
					}
				})
			}));
			// [TOY_350] Painted Canvasaur (2) — Battlecry: Give each other friendly Beast a random Bonus Effect.
			cards.Add("TOY_350", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in c.BoardZone.GetAll().Where(m => m.Id != s.Id && m.IsRace(Race.BEAST)).ToList()) Generic.GainBonusEffects(g, m, 1);
				})
			}));
			// [TOY_351] Mystery Egg (4) — Miniaturize. Deathrattle: Get a copy of a random Beast in your deck. It costs (3) less.
			cards.Add("TOY_351", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					var beasts = c.DeckZone.GetAll().Where(p => Generic.IsMinionCard(p) && p.Card.IsRace(Race.BEAST)).ToList();
					if (beasts.Count == 0) return;
					if (Generic.Copy(c, s, beasts[g.Random.Next(beasts.Count)], Zone.HAND) is IPlayable copy && copy.Zone?.Type == Zone.HAND) Give(g, s, copy, "TOY_351e1");
				})
			}));
			cards.Add("TOY_351e1", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("TOY_351", "less", 3))) }));
			// [TOY_352] Fetch! (1) — Draw a minion. If it's a Beast, draw a spell.
			cards.Add("TOY_352", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (DrawOne(g, c, p => p is Minion) is IPlayable drawn && drawn.Card.IsRace(Race.BEAST)) DrawWhere(g, c, p => p is Spell);
				})
			}));
			// [TOY_353] Patchwork Pals (2) — Get all 3 Animal Companions. They cost (1) less. (RelatedCards order)
			cards.Add("TOY_353", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (string id in new[] { "NEW1_034", "NEW1_032", "NEW1_033" })
						if (GetCard(c, Cards.FromId(id), s) is IPlayable got) Give(g, s, got, "TOY_353e");
				})
			}));
			cards.Add("TOY_353e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("TOY_353", "less", 1))) }));
			// [TOY_354] R.C. Rampage (4) — Summon six 1/1 Hounds. Any that can't fit give the others +1/+1. (wiki: cumulative, this cast's Hounds)
			cards.Add("TOY_354", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int want = V("TOY_354", "hounds", 6);
					var hounds = new List<Minion>();
					for (int i = 0; i < want; i++) if (SummonNew(g, c, Cards.FromId("TOY_358t"), s) is Minion h) hounds.Add(h);
					int missing = want - hounds.Count;
					foreach (Minion h in hounds) for (int i = 0; i < missing; i++) Give(g, s, h, "TOY_354e");
				})
			}));
			cards.Add("TOY_354e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TOY_354", "stats", 1))) }));
			// [TOY_355] Hemet, Foam Marksman (3) — After a friendly Beast dies, get a random Legendary Beast from the past. It costs (2) less.
			cards.Add("TOY_355", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DEATH)
				{
					TriggerSource = TriggerSource.MINIONS_EXCEPT_SELF,
					Condition = SelfCondition.IsRace(Race.BEAST),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						var pool = Cards.All.Where(x => x.Collectible && x.Type == CardType.MINION && x.Rarity == Rarity.LEGENDARY && x.IsRace(Race.BEAST) &&
							!x.NeverGenerated && Cards.FromThePast(x)).ToList();
						if (pool.Count > 0 && GetCard(c, pool[g.Random.Next(pool.Count)], s) is IPlayable got) Give(g, s, got, "TOY_355e2");
					})
				}
			}));
			cards.Add("TOY_355e2", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("TOY_355", "less", 2))) }));
			// [TOY_356] Toyrannosaurus (7) — Rush. Deathrattle: Deal 7 damage to a random enemy.
			cards.Add("TOY_356", new CardDef(new Power { DeathrattleTask = new CustomTask((g, c, s, t, stack) => DamageRandomEnemy(g, c, s, V("TOY_356", "damage", 7))) }));
			// [TOY_357] King Plush (9) — Charge. Battlecry: Return all minions with less Attack than this to their owner's decks. (its controller's)
			cards.Add("TOY_357", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Minion king)) return;
					foreach (Minion m in c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll())
						         .Where(m => m.Id != king.Id && !(m is Location) && !m.Untouchable && m.AttackDamage < king.AttackDamage).ToList())
						Generic.MoveToDeck(m.Controller, s, m);
				})
			}));
			// [TOY_358] Remote Control (2, weapon) — After your hero attacks, summon a 1/1 Hound.
			cards.Add("TOY_358", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK) { TriggerSource = TriggerSource.HERO, SingleTask = new CustomTask((g, c, s, t, stack) => SummonNew(g, c, Cards.FromId("TOY_358t"), s)) }
			}));
			// [TOY_359] Jungle Gym (2, Location) — Deal 1 damage to a random enemy. Repeat for each friendly Beast.
			cards.Add("TOY_359", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int shots = 1 + c.BoardZone.GetAll().Count(m => m.IsRace(Race.BEAST));
					for (int i = 0; i < shots; i++) DamageRandomEnemy(g, c, s, V("TOY_359", "damage", 1));
				})
			}));

			// ---------------------------------------------------------------- Mage
			// [MIS_107] Malfunction (2) — Deal $3 damage split among all enemy minions. If your deck has no minions, deal $3 more.
			cards.Add("MIS_107", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new EnqueueTask(V("MIS_107", "damage", 3), ComplexTask.DamageRandomTargets(1, EntityType.OP_MINIONS, 1), true),
					ComplexTask.If((g, c, s) => NoMinionsInDeck(c), new EnqueueTask(V("MIS_107", "more", 3), ComplexTask.DamageRandomTargets(1, EntityType.OP_MINIONS, 1), true)))
			}));
			// [MIS_302] Buy One, Get One Freeze (3) — Freeze a minion. Summon a Frozen copy of it. (live INST29 25099: its enchantments copied)
			cards.Add("MIS_302", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					m.IsFrozen = true;
					if (!c.BoardZone.IsFull && Generic.Copy(c, s, m, Zone.PLAY) is Minion copy && copy.Zone?.Type == Zone.PLAY) copy.IsFrozen = true;
				})
			}));
			// [MIS_303] Darkmoon Magician (3) — Elusive. After you cast a spell, cast a random spell that costs (1) more. (printed Cost — doubt; its
			// own casts aren't yours: no chain)
			cards.Add("MIS_303", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (t == null) return;
						int cost = t.Card.Cost + V("MIS_303", "more", 1);
						new CastRandomSpellTask(x => x.Cost == cost).Process(g, c, s, null);
					})
				}
			}));
			// [TOY_037] Hidden Objects (2) — Discover a Secret. Set its Cost to (1). (live: GBL_001e "Cost = 1")
			cards.Add("TOY_037", new CardDef(new Power { PowerTask = DiscoverFromTask.Secrets("GBL_001e") }));
			// [TOY_370] Triplewick Trickster (4) — Battlecry: Deal 2 damage to a random enemy, three times.
			cards.Add("TOY_370", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("TOY_370", "times", 3); i++) DamageRandomEnemy(g, c, s, V("TOY_370", "damage", 2));
				})
			}));
			// [TOY_371] Manufacturing Error (5) — Draw 3 cards. If your deck has no minions, they cost (3) less. (checked after the draws)
			cards.Add("TOY_371", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var drawn = new List<IPlayable>();
					for (int i = 0; i < V("TOY_371", "cards", 3); i++) if (Generic.Draw(c) is IPlayable d && d.Zone?.Type == Zone.HAND) drawn.Add(d);
					if (NoMinionsInDeck(c)) foreach (IPlayable d in drawn) Give(g, s, d, "TOY_371e");
				})
			}));
			cards.Add("TOY_371e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("TOY_371", "less", 3))) }));
			// [TOY_372] Yogg in the Box (7) — Cast 5 random spells. If your deck has no minions, the spells cast cost (5) or more.
			cards.Add("TOY_372", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					bool big = NoMinionsInDeck(c);
					for (int i = 0; i < V("TOY_372", "spells", 5); i++)
					{
						if (c.Hero.IsDead || c.Opponent.Hero.IsDead) break;
						new CastRandomSpellTask(x => !big || x.Cost >= V("TOY_372", "cost", 5)).Process(g, c, s, null);
					}
				})
			}));
			// [TOY_373] Puzzlemaster Khadgar (6) — Battlecry: Equip a 0/6 Wisdomball that casts helpful Mage spells!
			cards.Add("TOY_373", new CardDef(new Power { PowerTask = new WeaponTask("TOY_373t") }));
			// [TOY_373t] Magic Wisdomball — At the end of your turn, cast a helpful Mage spell. Lose 1 Durability. (at enemies)
			cards.Add("TOY_373t", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Weapon ball)) return;
						var useful = WisdomballSpells.Select(Cards.TryFromId).Where(x => x != null && WisdomballHelpful(c, x)).ToList();
						if (useful.Count > 0)
							new CastRandomSpellTask(enemiesIfPossible: true) { Spell = useful[g.Random.Next(useful.Count)] }.Process(g, c, s, null);
						if (ball.Zone?.Type == Zone.PLAY && !ball.ToBeDestroyed) ball.Damage += 1;
					})
				}
			}));
			// [TOY_374] Spot the Difference (4) — Discover a 3-Cost minion to summon. If your deck has no minions, repeat this. (wiki: once)
			cards.Add("TOY_374", new CardDef(new Power
			{
				PowerTask = new DiscoverFromTask((g, c, s) => SpotTheDifferencePool(g, c), null,
					ComplexTask.If((g, c, s) => NoMinionsInDeck(c), new DiscoverFromTask((g, c, s) => SpotTheDifferencePool(g, c), null, null, ChoiceAction.SUMMON)),
					ChoiceAction.SUMMON)
			}));
			// [TOY_375] Sleet Skater (5) — Miniaturize. Battlecry: Freeze an enemy minion. Gain Armor equal to its Attack.
			cards.Add("TOY_375", new CardDef(BattlecryEnemyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					m.IsFrozen = true;
					if (m.AttackDamage > 0) new ArmorTask(m.AttackDamage).Process(g, c, s, null);
				})
			}));
			// [TOY_376] Watercolor Artist (3) — Battlecry: Draw a Frost spell. At the start of your turns, reduce its Cost by (1).
			cards.Add("TOY_376", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (DrawOne(g, c, p => IsFrostSpell(p)) is IPlayable drawn) Give(g, s, drawn, "TOY_376e1"); })
			}));
			cards.Add("TOY_376e1", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is Enchantment e && e.Target is IPlayable card && card.Zone?.Type == Zone.HAND && card.Cost > 0) Give(g, e, card, "TOY_376e");
					})
				}
			}));
			cards.Add("TOY_376e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("TOY_376", "less", 1))) }));
			// [TOY_377] Frost Lich Cross-Stitch (4) — Deal $3 damage to a character. If it dies, summon a 3/6 Water Elemental that Freezes.
			cards.Add("TOY_377", new CardDef(TargetAny, new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("TOY_377", "damage", 3), EntityType.TARGET, true), new CustomTask((g, c, s, t, stack) =>
				{
					if (t is ICharacter ch && (ch.IsDead || ch.ToBeDestroyed)) Summon(g, c, Cards.FromAssetId(s.Card[GameTag.COLLECTION_RELATED_CARD_DATABASE_ID]) ?? Cards.FromId("CORE_CS2_033"), s);
				}))
			}));
			// [TOY_378] The Galactic Projection Orb (10) — Recast a random spell of each Cost you've cast this game (targets enemies if possible).
			// (patch 36.6.0: the original Cost; ascending — doubt)
			cards.Add("TOY_378", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var byCost = SpellsCast(c).Where(h => h.SourceId != s.Id && h.SourceCard.Id != s.Card.Id).Select(h => h.SourceCard)
						.GroupBy(x => x.Cost).OrderBy(x => x.Key).ToList();
					foreach (var group in byCost)
					{
						if (c.Hero.IsDead || c.Opponent.Hero.IsDead) break;
						var options = group.ToList();
						new CastRandomSpellTask(enemiesIfPossible: true) { Spell = options[g.Random.Next(options.Count)] }.Process(g, c, s, null);
					}
				})
			}));

			// ---------------------------------------------------------------- Druid
			// [MIS_300] Snuggle Teddy (3) — Gigantify. Elusive, Lifesteal, Taunt. (keywords; the Gigantic copy: Generic.OnPlayReplacement)
			cards.Add("MIS_300", new CardDef(new Power { NoScript = true }));
			// [MIS_301] Overgrown Beanstalk (3) — Summon a 2/2 Treant. Draw a card for each Treant you control.
			cards.Add("MIS_301", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Summon(g, c, Cards.FromId("MIS_301t"), s);
					int treants = c.BoardZone.GetAll().Count(m => IsTreant(m));
					for (int i = 0; i < treants; i++) Generic.Draw(c);
				})
			}));
			// [MIS_712] Toyrantus (6) — Taunt, Elusive. Battlecry: If you have 10 Mana Crystals, gain +7/+7.
			cards.Add("MIS_712", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c.BaseMana >= V("MIS_712", "crystals", 10), new AddEnchantmentTask("MIS_712e", EntityType.SOURCE))
			}));
			cards.Add("MIS_712e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("MIS_712", "stats", 7))) }));
			// [TOY_800] Sparkling Phial (4) — Deal $2 damage. Your next card this turn costs that much less. (N = 2 + Spell Damage: one "next card
			// costs (1) less" per point — doubt on the shape)
			cards.Add("TOY_800", new CardDef(TargetAny, new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("TOY_800", "damage", 2), EntityType.TARGET, true), new CustomTask((g, c, s, t, stack) =>
				{
					int n = Generic.SpellDamageAmount((IPlayable)s, V("TOY_800", "damage", 2));
					for (int i = 0; i < n; i++) new AddEnchantmentTask("TOY_800e1", EntityType.CONTROLLER).Process(g, c, s, null);
				}))
			}));
			cards.Add("TOY_800e1", NextCardEffect(Effects.ReduceCost(1), new SelfCondition(p => true), thisTurnOnly: true));
			// [TOY_801] Chia Drake (4) — Miniaturize. Choose One - Gain Spell Damage +1; or Draw a spell.
			cards.Add("TOY_801b", new CardDef(new Power { PowerTask = new AddEnchantmentTask("TOY_801e", EntityType.SOURCE) }));
			cards.Add("TOY_801a", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => DrawWhere(g, c, p => p is Spell)) }));
			cards.Add("TOY_801e", new CardDef(new Power { Enchant = new Enchant(new Effect(GameTag.SPELLPOWER, EffectOperator.ADD, V("TOY_801", "spell damage", 1))) }));
			// [TOY_802] Wind-Up Sapling (2) — Tradeable. Battlecry: Refresh @ Mana Crystals. (Trade to upgrade!)
			cards.Add("TOY_802", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => RefreshMana(c, Math.Max(1, s[GameTag.TAG_SCRIPT_DATA_NUM_1]))) }));
			// [TOY_803] Jade Display (1) — Deathrattle: Your Jade Displays have +1/+1 this game. Shuffle 2 of them into your deck.
			cards.Add("TOY_803", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					new AddEnchantmentTask("TOY_803e", EntityType.CONTROLLER).Process(g, c, s, null);
					for (int i = 0; i < V("TOY_803", "displays", 2); i++)
						Generic.ShuffleIntoDeck.Invoke(c, s, Entity.FromCard(c, Cards.FromId("TOY_803"), new EntityData { { GameTag.CREATOR, s.Id } }));
				})
			}));
			cards.Add("TOY_803e", new CardDef(new Power { Aura = new Aura(AuraType.HAND_AND_BOARD, "TOY_803e2") { Condition = new SelfCondition(p => p.Card.Id == "TOY_803") } }));
			cards.Add("TOY_803e2", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TOY_803", "stats", 1))) }));
			// [TOY_804] Woodland Wonders (5) — Summon two 2/5 Beetles with Taunt. Costs (3) less if you have Spell Damage.
			cards.Add("TOY_804", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => p.Controller.CurrentSpellPower > 0 ? V("TOY_804", "less", 3) : 0),
				PowerTask = new CustomTask((g, c, s, t, stack) => { for (int i = 0; i < V("TOY_804", "beetles", 2); i++) Summon(g, c, Cards.FromId("TOY_804t"), s); })
			}));
			// [TOY_805] Ensmallen (3) — Reduce the Cost and Attack of minions in your deck by (1).
			cards.Add("TOY_805", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable p in c.DeckZone.GetAll().Where(p => Generic.IsMinionCard(p)).ToList())
					{
						Give(g, s, p, "TOY_805e");
						if (p is Minion m && m.AttackDamage > 0) Give(g, s, p, "TOY_805e2");
					}
				})
			}));
			cards.Add("TOY_805e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("TOY_805", "less", 1))) }));
			cards.Add("TOY_805e2", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(-V("TOY_805", "attack", 1))) }));
			// [TOY_806] Sky Mother Aviana (5) — Battlecry: Shuffle 10 random Legendary minions into your deck. They cost (1). (repeats allowed)
			cards.Add("TOY_806", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("TOY_806", "minions", 10) && !c.DeckZone.IsFull; i++)
					{
						if (!(RandomCard(g, x => x.Type == CardType.MINION && x.Rarity == Rarity.LEGENDARY) is Card legend)) break;
						IPlayable p = Entity.FromCard(c, legend, new EntityData { { GameTag.CREATOR, s.Id } });
						Generic.AddEnchantmentBlock(g, Cards.FromId("TOY_806e"), (IPlayable)s, p, 0, 0, 0);
						Generic.ShuffleIntoDeck.Invoke(c, s, p);
					}
				})
			}));
			cards.Add("TOY_806e", new CardDef(new Power { Enchant = new Enchant(Effects.SetCost(V("TOY_806", "cost", 1))) }));
			// [TOY_807] Owlonius (7) — Spell Damage +1. Your spells get double bonus from Spell Damage.
			cards.Add("TOY_807", new CardDef(new Power { Aura = new Aura(AuraType.CONTROLLER, new Effect(HsTag.SPELL_DAMAGE_DOUBLERS, EffectOperator.ADD, 1)) }));
			// [TOY_850] Magical Dollhouse (2, Location) — Gain Spell Damage +1 this turn only.
			cards.Add("TOY_850", new CardDef(new Power { PowerTask = new AddEnchantmentTask("TOY_850e", EntityType.CONTROLLER) }));
			cards.Add("TOY_850e", OneTurn(new Effect(GameTag.SPELLPOWER, EffectOperator.ADD, V("TOY_850", "spell damage", 1))));
			// [TOY_851] Bottomless Toy Chest (2) — Discover a card from your deck. If you have Spell Damage, copy it.
			cards.Add("TOY_851", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					Generic.DiscoverFromDeck(c, s, Generic.PickFromDeck(c, p => true), (g2, c2, s2, card) =>
					{
						if (Generic.Draw(c2, card) is IPlayable drawn && drawn.Zone?.Type == Zone.HAND && c2.CurrentSpellPower > 0) Generic.Copy(c2, s2, drawn, Zone.HAND);
					}))
			}));
		}

		/// <summary>Spot the Difference's pool: class and neutral 3-Cost minions that can be summoned.</summary>
		private static IEnumerable<Card> SpotTheDifferencePool(Game g, Controller c) =>
			Cards.FormatTypeCards(g.FormatType).Where(x => x.Type == CardType.MINION && x.Cost == V("TOY_374", "cost", 3) && !x.NeverRandomlyOnBoard &&
				(x.HasClass(c.HeroClass) || x.HasClass(CardClass.NEUTRAL)));
	}
}
