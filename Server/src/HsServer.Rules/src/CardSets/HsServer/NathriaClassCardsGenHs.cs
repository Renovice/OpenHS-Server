// HsServer addition (not upstream SabberStone): Murder at Castle Nathria class cards (the client-only CORE_REV copies).
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
		/// <summary>On a Secret: Halkias's soul is stored in it (resummons Halkias when it's revealed).</summary>
		private const GameTag HALKIAS_SOUL = (GameTag)990009;

		private static readonly string[] Wildseeds = { "REV_360t", "REV_360t1", "REV_360t2" };

		// Imprisoned Jailers (REV_334): three 1/3 Jailers; +1/+1 at 20 or less Health (the branch ComplexTask.If picks).
		private static ISimpleTask Jailers(bool low) => new CustomTask((g, c, s, t, stack) =>
		{
			for (int i = 0; i < 3 && !c.BoardZone.IsFull; i++)
			{
				var jailer = (Minion)Entity.FromCard(c, Cards.FromId("REV_334t"));
				Generic.SummonBlock.Invoke(g, jailer, -1, s);
				if (low) Give(g, s, jailer, "REV_334e");
			}
		});
		private static readonly string[] BasicTotems = { "NEW1_009", "CS2_050", "CS2_051", "CS2_052" };

		/// <summary>A Core Infuse card that infuses in place (no linked infused card in the client): run the infused effect
		/// when the source carries INFUSED.</summary>
		private static ISimpleTask InfusedOr(ISimpleTask normal, ISimpleTask infused) => new CustomTask((g, c, s, t, stack) =>
			(IsInfused(s) ? infused : normal).Process(g, c, s, t, stack));

		// HsServer (WHIZBANG): the client's IMP tag (1965: 122 cards incl. Wretched Queen, Tiny Knight of Evil), not the name ("Imprisoned …")
		private static bool IsImp(IPlayable p) => p is Minion && p.Card[(GameTag)1965] > 0;
		private static bool IsTotem(IPlayable p) => p is Minion m && m.IsRace(Race.TOTEM);

		/// <summary>A random minion whose Cost differs from <paramref name="card"/>'s by <paramref name="delta"/> (Primordial Wave...).</summary>
		/// <summary>"Transform into one that costs (N) more / less": the shared cost-based transform rules (Cards.EvolvePick).</summary>
		private static Card CostShifted(Game g, IPlayable minion, int delta) => Cards.EvolvePick(g.FormatType, minion, delta, g.Random);

		private static void Relic(Game g, Controller c, IEntity s, string id)
		{
			int bonus = Relics(c);
			switch (id)
			{
				case "CORE_REV_834": // Relic of Extinction: deal 1 (+1 per Relic) to a random enemy minion, twice
					for (int i = 0; i < 2; i++)
					{
						var enemies = c.Opponent.BoardZone.GetAll().Where(m => !m.IsDead && !m.ToBeDestroyed).ToList();
						if (enemies.Count == 0) break;
						Generic.DamageCharFunc((IPlayable)s, enemies[g.Random.Next(enemies.Count)], 1 + bonus, true);
					}
					break;
				case "CORE_REV_943": // Relic of Phantasms: summon two 1/1 (+1/+1 per Relic) Spirits
					for (int i = 0; i < 2 && !c.BoardZone.IsFull; i++)
					{
						var spirit = (Minion)Entity.FromCard(c, Cards.FromId("REV_943t"));
						spirit.AttackDamage = 1 + bonus;
						spirit.BaseHealth = 1 + bonus;
						Generic.SummonBlock.Invoke(g, spirit, -1, s);
					}
					break;
				case "CORE_REV_508": // Relic of Dimensions: draw two cards and reduce their Cost by (1 + Relics)
					for (int i = 0; i < 2; i++)
						if (Generic.Draw(c) is IPlayable drawn && drawn.Zone?.Type == SabberStoneCore.Enums.Zone.HAND)
							drawn.Cost = System.Math.Max(0, drawn.Cost - (1 + bonus));
					break;
			}
			RelicPlayed(c);
		}

		private static void NathriaClasses(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Demon Hunter

			// [REV_507] Dispose of Evidence (0) — Give your hero +3 Attack this turn. Pick from 3 cards in your hand to shuffle into
			// your deck.
			cards.Add("CORE_REV_507", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					new AddEnchantmentTask("REV_507e", EntityType.HERO),
					// live (live-20261001-startofgame): options linked to the hand cards; the pick is shuffled in (Generic.DiscoverFromCards)
					new CustomTask((g, c, s, t, stack) =>
						Generic.DiscoverFromCards(c, s, Generic.PickFrom(c.HandZone, g, p => p.Id != s.Id),
							(g2, c2, s2, card) => Generic.MoveToDeck(c2, s2, card))))
			}));
			cards.Add("REV_507e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("REV_507", "attack", 3))) { IsOneTurnEffect = true } }));
			// Relics.
			foreach (string relic in new[] { "CORE_REV_834", "CORE_REV_943", "CORE_REV_508" })
			{
				string id = relic;
				cards.Add(id, new CardDef(new Power
				{
					PowerTask = new CustomTask((g, c, s, t, stack) => { for (int i = RelicCasts(g, c); i > 0; i--) Relic(g, c, s, id); }) // Relic Vault
				}));
			}
			// [REV_937] Artificer Xy'mox (8) — Battlecry: Discover and cast a Relic. Infuse (5): Cast all three instead.
			cards.Add("CORE_REV_937", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Card[] relics = { Cards.FromId("CORE_REV_834"), Cards.FromId("CORE_REV_943"), Cards.FromId("CORE_REV_508") };
					Generic.CreateChoiceCards.Invoke(c, s, null, ChoiceType.GENERAL, ChoiceAction.STACK, relics,
						new CustomTask((g2, c2, s2, chosen, stack2) => { if (chosen != null) Relic(g2, c2, s2, chosen.Card.Id); }));
				})
			}));
			cards.Add("REV_937t", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (string relic in new[] { "CORE_REV_834", "CORE_REV_943", "CORE_REV_508" }) Relic(g, c, s, relic);
				})
			}));
			// [REV_511] Bibliomite (2) — Battlecry: Choose a card in your hand to shuffle into your deck. (Hand target: client tag
			// CAN_TARGET_CARDS_IN_HAND; Power.HandTarget.)
			cards.Add("CORE_REV_511", new CardDef(new Power
			{
				HandTarget = SelfCondition.AnyCard,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t?.Zone?.Type == Zone.HAND) Generic.MoveToDeck(c, s, t);
				})
			}));
			// [REV_506] Sinful Brand (2) — Brand an enemy minion. Whenever it takes damage, deal 1 damage to the enemy hero.
			cards.Add("CORE_REV_506", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_ENEMY_TARGET, 0 } },
				new Power { PowerTask = new AddEnchantmentTask("REV_506e", EntityType.TARGET) }));
			cards.Add("REV_506e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TAKE_DAMAGE)
				{
					TriggerSource = TriggerSource.ENCHANTMENT_TARGET,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is Enchantment e && e.Target is Minion branded)
							Generic.DamageCharFunc(e.Creator ?? e, branded.Controller.Hero, V("REV_506", "damage", 1), false);
					})
				}
			}));
			// [REV_509] Magnifying Glaive (3) — After your hero attacks, draw until you have 3 cards.
			cards.Add("CORE_REV_509", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						for (int guard = 0; c.HandZone.Count < V("REV_509", "hand", 3) && guard < 10; guard++)
							if (Generic.Draw(c) == null) break;
					})
				}
			}));
			// [REV_510] Kryxis the Voracious (4) — Battlecry: Discard your hand. Deathrattle: Draw 3 cards.
			cards.Add("CORE_REV_510", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { foreach (IPlayable p in c.HandZone.ToList()) Generic.DiscardBlock.Invoke(c, p); }),
				DeathrattleTask = new EnqueueTask(V("REV_510", "cards", 3), new DrawTask())
			}));

			// ---------------------------------------------------------------- Druid

			// [REV_365] Convoke the Spirits (10) — Cast 8 random Druid spells (targets chosen randomly).
			cards.Add("CORE_REV_365", new CardDef(new Power
			{
				PowerTask = new EnqueueTask(V("REV_365", "spells", 8), new CastRandomSpellTask(card => card.HasClass(CardClass.DRUID)))
			}));
			// [REV_313] Planted Evidence (1) — Discover a spell. It costs (2) less this turn.
			cards.Add("CORE_REV_313", new CardDef(new Power
			{
				PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.SPELL, "REV_313e")
			}));
			cards.Add("REV_313e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.ReduceCost(V("REV_313", "discount", 2))),
				Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = RemoveEnchantmentTask.Task }
			}));
			// [REV_307] Natural Causes (2) — Deal 2 damage. Summon a 2/2 Treant.
			cards.Add("CORE_REV_307", new CardDef(TargetAny, new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("REV_307", "damage", 2), EntityType.TARGET, true), new SummonTask("REV_336t2", SummonSide.SPELL))
			}));
			// [REV_336] Plot of Sin (3) — Summon two 2/2 Treants. Infuse (5): Two 5/5 Ancients instead.
			cards.Add("CORE_REV_336", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power { PowerTask = new SummonTask("REV_336t2", 2, SummonSide.SPELL) }));
			cards.Add("REV_336t4", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power { PowerTask = new SummonTask("REV_336t3", 2, SummonSide.SPELL) }));
			// [REV_318] Widowbloom Seedsman (4) — Battlecry: Draw a Nature spell. Gain an empty Mana Crystal.
			cards.Add("CORE_REV_318", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					DrawWhere(g, c, p => p is Spell && p.Card[SPELL_SCHOOL] == 4); // NATURE
					if (c.BaseMana < 10) c.BaseMana++;
				})
			}));
			// [REV_310] Death Blossom Whomper (5) — Battlecry: Draw a Deathrattle minion and gain its Deathrattle.
			cards.Add("CORE_REV_310", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var withDeathrattle = c.DeckZone.Where(p => Generic.IsMinionCard(p) && p.Card.Deathrattle).ToList();
					if (withDeathrattle.Count == 0) return;
					IPlayable drawn = withDeathrattle[g.Random.Next(withDeathrattle.Count)];
					Generic.Draw(c, drawn);
					s[GameTag.TAG_SCRIPT_DATA_NUM_2] = drawn.Card.AssetId;
					Give(g, s, (IPlayable)s, "DRG_031e");
				})
			}));
			// [REV_314] Topior the Shrubbagazzor (7) — Battlecry: For the rest of the game, after you cast a Nature spell, summon a
			// 3/3 Whelp with Rush.
			cards.Add("CORE_REV_314", new CardDef(new Power { PowerTask = new AddEnchantmentTask("REV_314e", EntityType.CONTROLLER) }));
			cards.Add("REV_314e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p.Card[SPELL_SCHOOL] == 4),
					SingleTask = new CustomTask((g, c, s, t, stack) => Summon(g, c, Cards.FromId("REV_314t"), s))
				}
			}));
			// [REV_311] Nightshade Bud (8) — Choose One - Discover a minion from your deck to summon; or a spell to cast.
			cards.Add("CORE_REV_311", new CardDef(new Power()));
			cards.Add("REV_311t2", new CardDef(new Power // Moonlight Blossom: a minion from your deck to summon
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Generic.DiscoverFromDeck(c, s, Generic.PickFromDeck(c, p => p is Minion), (g2, c2, s2, deck) =>
					{
						if (c2.BoardZone.IsFull) return;
						c2.DeckZone.Remove(deck);
						Generic.SummonBlock.Invoke(g2, (Minion)deck, -1, s2);
					});
				})
			}));
			cards.Add("REV_311t", new CardDef(new Power // Sunlight Blossom: a spell from your deck to cast
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Generic.DiscoverFromDeck(c, s, Generic.PickFromDeck(c, p => p is Spell), (g2, c2, s2, deck) =>
					{
						if (deck is Spell spell) Generic.CastFromDeck(c2, s2, spell);
					});
				})
			}));
			// [REV_319] Sesselie of the Fae Court (8) — Taunt. Deathrattle: Draw a minion. Reduce its Cost by (8).
			cards.Add("CORE_REV_319", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					var minions = c.DeckZone.Where(p => Generic.IsMinionCard(p)).ToList();
					if (minions.Count == 0) return;
					if (Generic.Draw(c, minions[g.Random.Next(minions.Count)]) is IPlayable drawn && drawn.Zone?.Type == SabberStoneCore.Enums.Zone.HAND)
						Give(g, s, drawn, "REV_319e");
				})
			}));
			cards.Add("REV_319e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("REV_319", "discount", 8))) }));

			// ---------------------------------------------------------------- Hunter

			// [REV_356] Batty Guest (1) — Deathrattle: Summon a 2/1 Bat.
			cards.Add("CORE_REV_356", new CardDef(new Power { DeathrattleTask = new SummonTask("REV_350t", SummonSide.DEATHRATTLE) }));
			// [REV_350] Frenzied Fangs (2) — Summon two 2/1 Bats. Infuse (3): Give them +1/+2.
			cards.Add("CORE_REV_350", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power { PowerTask = new SummonTask("REV_350t", 2, SummonSide.SPELL) }));
			cards.Add("REV_350t2", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < 2 && !c.BoardZone.IsFull; i++)
					{
						var bat = (Minion)Entity.FromCard(c, Cards.FromId("REV_350t"));
						Generic.SummonBlock.Invoke(g, bat, -1, s);
						Give(g, s, bat, "REV_350e");
					}
				})
			}));
			cards.Add("REV_350e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("REV_350", "attack", 1)), Effects.Health_N(V("REV_350", "health", 2))) }));
			// Wildseeds: Dormant (generic, from their text); the Stag equips a 4/2 Greatbow when it awakens.
			cards.Add("REV_360t2", new CardDef(new Power
			{
				PowerTask = Nothing,
				AwakenTask = new CustomTask((g, c, s, t, stack) =>
				{
					var bow = (Weapon)Entity.FromCard(c, Cards.FromId("REV_360t4"));
					bow.Card.Power?.Aura?.Activate(bow);
					bow.Card.Power?.Trigger?.Activate(bow);
					c.Hero.AddWeapon(bow);
				})
			}));
			// [REV_360] Spirit Poacher (2) — Battlecry: Summon a random Dormant Wildseed.
			cards.Add("CORE_REV_360", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Summon(g, c, Cards.FromId(Wildseeds[g.Random.Next(Wildseeds.Length)]), s, ((Minion)s).ZonePosition + 1))
			}));
			// [REV_364] Stag Charge (3) — Deal 3 damage. Summon a random Dormant Wildseed.
			cards.Add("CORE_REV_364", new CardDef(TargetAny, new Power
			{
				PowerTask = ComplexTask.Create(
					new DamageTask(V("REV_364", "damage", 3), EntityType.TARGET, true),
					new CustomTask((g, c, s, t, stack) => Summon(g, c, Cards.FromId(Wildseeds[g.Random.Next(Wildseeds.Length)]), s)))
			}));
			// [REV_361] Wild Spirits (3) — Summon two different Dormant Wildseeds. Make your Wildseeds awaken 1 turn sooner.
			cards.Add("CORE_REV_361", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (string seed in Pick(g, Wildseeds.Select(Cards.FromId), 2).Select(x => x.Id)) Summon(g, c, Cards.FromId(seed), s);
					foreach (Minion m in c.BoardZone.GetDormant().Where(m => Wildseeds.Contains(m.Card.Id)))
						Generic.AwakenSooner(m, V("REV_361", "sooner", 1), atLeast: 1);
				})
			}));
			// [REV_363] Ara'lon (5) — Battlecry: Summon one of each Dormant Wildseed.
			cards.Add("CORE_REV_363", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { foreach (string seed in Wildseeds) Summon(g, c, Cards.FromId(seed), s); })
			}));
			// [REV_352] Stonebound Gargon (4) — Rush. Infuse (3): Also damages the minions next to whomever this attacks.
			cards.Add("CORE_REV_352", new CardDef(new Power { PowerTask = Nothing }));
			cards.Add("REV_352t", new CardDef(new Power { Trigger = Cleave() }));
			// [REV_353] Huntsman Altimor (7) — Battlecry: Summon a Gargon Companion. Infuse (4): another; Infuse (8): all three.
			string[] gargons = { "REV_353t3", "REV_353t4", "REV_353t5" };
			ISimpleTask Altimor(int n) => new CustomTask((g, c, s, t, stack) =>
			{
				foreach (Card gargon in Pick(g, gargons.Select(Cards.FromId), n)) Summon(g, c, gargon, s);
			});
			// Core Altimor infuses in place, twice (4, then 8 deaths): INFUSED counts the stages (Power.InfuseStages).
			cards.Add("CORE_REV_353", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Altimor(1 + System.Math.Min(2, s[Game.INFUSED])).Process(g, c, s, t, stack)),
				InfuseStages = 2 // Infuse (4) then Infuse (8), in place
			}));
			cards.Add("REV_353t", new CardDef(new Power { PowerTask = Altimor(2) }));
			cards.Add("REV_353t2", new CardDef(new Power { PowerTask = Altimor(3) }));
			cards.Add("REV_353t4", new CardDef(new Power { Aura = new Aura(AuraType.BOARD_EXCEPT_SOURCE, "REV_353t4e") }));
			cards.Add("REV_353t4e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("REV_353t4", "attack", 1))) }));
			// [REV_369] Collateral Damage (8) — Deal 6 damage to three random enemy minions. Excess damage hits the enemy hero.
			cards.Add("CORE_REV_369", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int damage = V("REV_369", "damage", 6) + c.CurrentSpellPower;
					if (s is Spell collateral) collateral[HsTag.SPELL_DAMAGE_COUNTED] = 1; // counted once (the excess to the hero comes from it)
					var targets = c.Opponent.BoardZone.GetAll().Where(m => !m.IsDead && !m.ToBeDestroyed).OrderBy(_ => g.Random.Next()).Take(3).ToList();
					foreach (Minion m in targets)
					{
						int excess = System.Math.Max(0, damage - m.Health);
						Generic.DamageCharFunc((IPlayable)s, m, damage, false);
						if (excess > 0) Generic.DamageCharFunc((IPlayable)s, c.Opponent.Hero, excess, false);
					}
				})
			}));

			// ---------------------------------------------------------------- Mage

			// [REV_601] Frozen Touch (2) — Deal 3 damage. Infuse (3): Add a Frozen Touch to your hand.
			cards.Add("CORE_REV_601", new CardDef(TargetAny, new Power { PowerTask = new DamageTask(V("REV_601", "damage", 3), EntityType.TARGET, true) }));
			cards.Add("REV_601t", new CardDef(TargetAny, new Power
			{
				PowerTask = ComplexTask.Create(
					new DamageTask(V("REV_601", "damage", 3), EntityType.TARGET, true),
					new CustomTask((g, c, s, t, stack) => AddToHand(c, Cards.FromId("CORE_REV_601"))))
			}));
			// [REV_516] Vengeful Visage (3) — Secret: After an enemy minion attacks your hero, summon a copy of it to attack the enemy hero.
			cards.Add("CORE_REV_516", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.OP_MINIONS,
					Condition = new SelfCondition(p => p.Game.CurrentEventData?.EventTarget is Hero h && h.Controller != p.Controller),
					SingleTask = ComplexTask.Secret(new CustomTask((g, c, s, t, stack) =>
					{
						if (!(t is Minion attacker) || c.BoardZone.IsFull) return;
						var copy = (Minion)Entity.FromCard(c, attacker.Card);
						Generic.SummonBlock.Invoke(g, copy, -1, s);
						EventMetaData temp = g.CurrentEventData;
						Generic.AttackBlock.Invoke(c, copy, c.Opponent.Hero, true, true);
						g.CurrentEventData = temp;
					}))
				}
			}));
			// [REV_504] Solid Alibi (3) — Until your next turn, your hero can only take 1 damage at a time.
			cards.Add("CORE_REV_504", new CardDef(new Power { PowerTask = new AddEnchantmentTask("REV_504e", EntityType.HERO) }));
			cards.Add("REV_504e", new CardDef(new Power
			{
				Trigger = new MultiTrigger(
					Generic.PreDamageCapTrigger(TriggerSource.ENCHANTMENT_TARGET, 1), // (only a hit above 1)
					new Trigger(TriggerType.TURN_START) { SingleTask = RemoveEnchantmentTask.Task })
			}));
			// [REV_515] Orion, Mansion Manager (4) — After a friendly Secret is revealed, cast a different Mage Secret and gain +2/+2.
			cards.Add("CORE_REV_515", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.SECRET_REVEALED)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						Card secret = RandomCard(g, x => x.IsSecret && x.HasClass(CardClass.MAGE) && x.Id != t?.Card.Id && !c.SecretZone.Any(z => z.Card.Id == x.Id));
						if (secret != null) PutSecretIntoPlay(c, (Spell)Entity.FromCard(c, secret));
						if (s is Minion me) Give(g, s, me, "REV_515e");
					})
				}
			}));
			cards.Add("REV_515e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("REV_515", "stats", 2))) }));
			// [REV_513] Chatty Bartender (4) — At the end of your turn, if you control a Secret, deal 2 damage to all enemies.
			cards.Add("CORE_REV_513", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = ComplexTask.Create(
						new ConditionTask(EntityType.SOURCE, SelfCondition.IsControllingSecret),
						new FlagTask(true, new DamageTask(V("REV_513", "damage", 2), EntityType.ENEMIES)))
				}
			}));
			// [REV_505] Cold Case (4) — Summon two 2/2 Volatile Skeletons. Gain 4 Armor.
			cards.Add("CORE_REV_505", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new SummonTask("CORE_REV_845", 2, SummonSide.SPELL), new ArmorTask(V("REV_505", "armor", 4)))
			}));
			// [REV_840] Deathborne (6) — Deal 2 damage to all minions. Summon a 2/2 Volatile Skeleton for each killed.
			cards.Add("CORE_REV_840", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var all = c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll()).ToList();
					foreach (Minion m in all) Generic.DamageCharFunc((IPlayable)s, m, V("REV_840", "damage", 2), true);
					int killed = all.Count(m => m.IsDead || m.ToBeDestroyed);
					g.DeathProcessingAndAuraUpdate();
					for (int i = 0; i < killed; i++) Summon(g, c, Cards.FromId("CORE_REV_845"), s);
				})
			}));
			// [REV_514] Kel'Thuzad, the Inevitable (8) — Battlecry: Resurrect your Volatile Skeletons. Any that can't fit on the
			// battlefield instantly explode!
			cards.Add("CORE_REV_514", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int skeletons = c.DiedThisGameCards.Count(p => p is Minion && p.Card.Name == "Volatile Skeleton");
					for (int i = 0; i < skeletons; i++)
					{
						if (!c.BoardZone.IsFull) Summon(g, c, Cards.FromId("CORE_REV_845"), s);
						else ComplexTask.DamageRandomTargets(1, EntityType.ENEMIES, V("REV_845", "damage", 2)).Process(g, c, s, null, new TaskStack());
					}
				})
			}));

			// ---------------------------------------------------------------- Neutral

			// [REV_906] Sire Denathrius (10) — Lifesteal. Battlecry: Deal 5 damage amongst enemies. Endlessly Infuse (2): Deal 1
			// more. Infused, it becomes REV_906t, whose damage (TAG_SCRIPT_DATA_NUM_2, shown by the client) grows every 2 deaths
			// (Power.EndlessInfuse).
			ISimpleTask Denathrius(Func<IEntity, int> damage) => new CustomTask((g, c, s, t, stack) =>
			{
				for (int i = damage(s); i > 0; i--)
				{
					var enemies = c.Opponent.BoardZone.GetAll().Where(m => !m.IsDead && !m.ToBeDestroyed).Cast<ICharacter>().Append(c.Opponent.Hero).ToList();
					Generic.DamageCharFunc((IPlayable)s, enemies[g.Random.Next(enemies.Count)], 1, false);
				}
			});
			cards.Add("CORE_REV_906", new CardDef(new Power { PowerTask = Denathrius(s => V("REV_906", "damage", 5)) }));
			cards.Add("REV_906t", new CardDef(new Power { PowerTask = Denathrius(s => s[GameTag.TAG_SCRIPT_DATA_NUM_2]), EndlessInfuse = true }));
			// [REV_839] Sinstone Totem (1) — At the end of your turn, gain +1 Health.
			cards.Add("CORE_REV_839", new CardDef(new Power { Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = new AddEnchantmentTask("REV_839e", EntityType.SOURCE) } }));
			cards.Add("REV_839e", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(V("REV_839", "health", 1))) }));
			// [REV_945] Sketchy Stranger (2) — Battlecry: Discover a Secret from another class.
			cards.Add("CORE_REV_945", new CardDef(new Power
			{
				PowerTask = DiscoverFromTask.Where((c, x) => x.IsSecret && !x.HasClass(c.HeroClass))
			}));
			// [REV_351] Roosting Gargoyle (2) — Battlecry: Give a friendly Beast +2 Attack.
			cards.Add("CORE_REV_351", new CardDef(new Dictionary<PlayReq, int>
				{
					{ PlayReq.REQ_TARGET_IF_AVAILABLE, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_TARGET_WITH_RACE, (int)Race.BEAST }
				},
				new Power { PowerTask = new AddEnchantmentTask("REV_351e", EntityType.TARGET) }));
			cards.Add("REV_351e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("REV_351", "attack", 2))) }));
			// [REV_841] Anonymous Informant (2) — Battlecry: The next Secret you play costs (0).
			cards.Add("CORE_REV_841", new CardDef(new Power { PowerTask = new AddEnchantmentTask("REV_841e", EntityType.CONTROLLER) }));
			cards.Add("REV_841e", NextCardEffect(Effects.SetCost(0), SelfCondition.IsSecret, thisTurnOnly: false));
			// [REV_900] Scuttlebutt Ghoul (4) — Taunt. Battlecry: If you control a Secret, summon a copy of this.
			cards.Add("CORE_REV_900", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					new ConditionTask(EntityType.SOURCE, SelfCondition.IsControllingSecret),
					new FlagTask(true, new SummonCopyTask(EntityType.SOURCE, side: SummonSide.RIGHT)))
			}));
			// [REV_370] Party Crasher (8) — Battlecry: Choose an enemy minion. Throw a random minion from your hand at it.
			cards.Add("CORE_REV_370", new CardDef(BattlecryEnemyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var minions = c.HandZone.Where(p => Generic.IsMinionCard(p)).ToList();
					if (minions.Count == 0 || !(t is Minion target)) return;
					IPlayable thrown = minions[g.Random.Next(minions.Count)];
					c.HandZone.Remove(thrown);
					c.GraveyardZone.Add(thrown);
					Generic.DamageCharFunc((IPlayable)s, target, thrown.Card.ATK, false);
				})
			}));

			// ---------------------------------------------------------------- Paladin

			// [REV_952] Sinful Sous Chef (1) — Deathrattle: Add 2 Silver Hand Recruits to your hand.
			cards.Add("CORE_REV_952", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => { for (int i = 0; i < 2; i++) AddToHand(c, Cards.FromId("CS2_101t")); })
			}));
			// [REV_842] Promotion (1) — Give a Silver Hand Recruit +3/+3 and Taunt.
			cards.Add("CORE_REV_842", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (t is Minion m && IsRecruit(m)) Give(g, s, m, "REV_842e"); })
			}));
			cards.Add("REV_842e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.Attack_N(V("REV_842", "stats", 3)), Effects.Health_N(V("REV_842", "stats", 3)), new Effect(GameTag.TAUNT, EffectOperator.SET, 1))
			}));
			// [REV_955] Stewart the Steward (3) — Deathrattle: Give the next Silver Hand Recruit you summon +3/+3 and this Deathrattle.
			ISimpleTask stewart = new AddEnchantmentTask("REV_955e", EntityType.CONTROLLER);
			cards.Add("CORE_REV_955", new CardDef(new Power { DeathrattleTask = stewart }));
			cards.Add("REV_955e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_SUMMON)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(IsRecruit),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Enchantment e) || !(t is Minion recruit)) return;
						Give(g, e, recruit, "REV_955e2");
						RemoveEnchantmentTask.Task.Process(e.Game, e.Controller, e, null); // reverts its effects too
					})
				}
			}));
			cards.Add("REV_955e2", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.AttackHealth_N(V("REV_955", "stats", 3))),
				DeathrattleTask = stewart
			}));
			// [REV_948] Service Bell (3) — Discover a Class card from your deck and draw all copies of it.
			cards.Add("CORE_REV_948", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Generic.DiscoverFromDeck(c, s, Generic.PickFromDeck(c, p => p.Card.HasClass(c.HeroClass)), (g2, c2, s2, deck) =>
					{
						Generic.Draw(c2, deck);
						foreach (IPlayable copy in c2.DeckZone.Where(p => p.Card.Id == deck.Card.Id).ToList()) Generic.Draw(c2, copy);
					});
				})
			}));
			// [REV_947] Muckborn Servant (3) — Taunt. Battlecry: Discover a Paladin card.
			cards.Add("CORE_REV_947", new CardDef(new Power { PowerTask = new DiscoverTask(CardType.INVALID, CardClass.PALADIN) }));
			// [REV_958] Buffet Biggun (4) — Battlecry: Summon two Silver Hand Recruits. Infuse (4): Give them +2 Attack and Divine Shield.
			cards.Add("CORE_REV_958", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { for (int i = 0; i < 2; i++) Summon(g, c, Cards.FromId("CS2_101t"), s); })
			}));
			cards.Add("REV_958t", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Generic.BeginSummonGroup(g, s, 2);
					for (int i = 0; i < 2 && !c.BoardZone.IsFull; i++)
					{
						var recruit = (Minion)Entity.FromCard(c, Cards.FromId("CS2_101t"));
						Generic.SummonBlock.Invoke(g, recruit, -1, s);
						Give(g, s, recruit, "REV_958e");
						recruit.HasDivineShield = true;
					}
				})
			}));
			cards.Add("REV_958e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("REV_958", "attack", 2))) }));
			// [REV_961] Elitist Snob (5) — Battlecry: For each Paladin card in your hand, randomly gain Divine Shield, Lifesteal,
			// Rush, or Taunt.
			cards.Add("CORE_REV_961", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					GameTag[] gifts = { GameTag.DIVINE_SHIELD, GameTag.LIFESTEAL, GameTag.RUSH, GameTag.TAUNT };
					int n = c.HandZone.Count(p => p.Card.HasClass(CardClass.PALADIN));
					for (int i = 0; i < n; i++)
					{
						GameTag gift = gifts[g.Random.Next(gifts.Length)];
						if (gift == GameTag.RUSH) ((Minion)s).IsRush = true; else s[gift] = 1;
					}
				})
			}));
			// [REV_951] The Countess (7) — Battlecry: If your deck has no Neutral cards, add 3 Legendary Invitations to your hand.
			cards.Add("CORE_REV_951", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => !c.DeckZone.Any(p => p.Card.HasClass(CardClass.NEUTRAL)),
					new CustomTask((g, c, s, t, stack) => { for (int i = 0; i < 3; i++) AddToHand(c, Cards.FromId("REV_951t")); }))
			}));
			cards.Add("REV_951t", new CardDef(new Power
			{
				PowerTask = new DiscoverFromTask((g, c, s) => OtherClassCards(g, c).Where(x => x.Type == CardType.MINION && x.Rarity == Rarity.LEGENDARY), "REV_951e")
			}));
			cards.Add("REV_951e", new CardDef(new Power { Enchant = new Enchant(Effects.SetCost(0)) }));
			// [REV_950] Divine Toll (7) — Shoot 5 rays at random minions. They give friendly minions +2/+2, and deal 2 damage to
			// enemy minions.
			cards.Add("CORE_REV_950", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("REV_950", "rays", 5); i++)
					{
						var targets = c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll()).Where(m => !m.IsDead && !m.ToBeDestroyed).ToList();
						if (targets.Count == 0) break;
						Minion m = targets[g.Random.Next(targets.Count)];
						if (m.Controller == c) Give(g, s, m, "REV_950e");
						else Generic.DamageCharFunc((IPlayable)s, m, V("REV_950", "damage", 2), true);
					}
				})
			}));
			cards.Add("REV_950e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("REV_950", "stats", 2))) }));

			// ---------------------------------------------------------------- Priest

			// [REV_249] The Light! It Burns! (1) — Deal damage to a minion equal to its Attack.
			cards.Add("CORE_REV_249", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (t is Minion m) Generic.DamageCharFunc((IPlayable)s, m, m.AttackDamage, true); })
			}));
			// [REV_250] Pelagos (3) — After you cast a spell on a friendly minion, set its Attack and Health to the higher of the two.
			cards.Add("CORE_REV_250", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY_SPELL_CASTED_ON_OWN_MINIONS,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(t is Spell spell) || spell.CardTarget == 0 || !(g.IdEntityDic[spell.CardTarget] is Minion m) || m.IsDead) return;
						int high = System.Math.Max(m.AttackDamage, m.Health);
						if (m.AttackDamage < high) m.AttackDamage += high - m.AttackDamage;
						if (m.Health < high) m.BaseHealth += high - m.Health;
					})
				}
			}));
			// [REV_253] Identity Theft (3) — Discover a copy of a card from your opponent's hand and deck.
			cards.Add("CORE_REV_253", new CardDef(new Power
			{
				PowerTask = new DiscoverFromTask((g, c, s) => c.Opponent.HandZone.Concat(c.Opponent.DeckZone).Select(p => p.Card),
					afterPick: new SetGameTagTask(COPIED_FROM_OPPONENT, 1, EntityType.TARGET))
			}));
			// [REV_248] Boon of the Ascended (4) — Give a minion +2 Health. Summon a Kyrian with its stats and Taunt.
			cards.Add("CORE_REV_248", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					Give(g, s, m, "REV_248e");
					if (c.BoardZone.IsFull) return;
					var kyrian = (Minion)Entity.FromCard(c, Cards.FromId("REV_248t"));
					kyrian.AttackDamage = m.AttackDamage;
					kyrian.BaseHealth = m.Health;
					Generic.SummonBlock.Invoke(g, kyrian, -1, s);
				})
			}));
			cards.Add("REV_248e", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(V("REV_248", "health", 2))) }));
			// [REV_252] Clean the Scene (5) — Destroy all minions with 3 or less Attack. Infuse (3): 6 or less.
			ISimpleTask CleanTheScene(int max) => new CustomTask((g, c, s, t, stack) =>
			{
				foreach (Minion m in c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll()).Where(m => m.AttackDamage <= max).ToList()) m.Destroy();
			});
			cards.Add("CORE_REV_252", new CardDef(new Power { PowerTask = CleanTheScene(V("REV_252", "attack", 3)) }));
			cards.Add("REV_252t", new CardDef(new Power { PowerTask = CleanTheScene(V("REV_252t", "attack", 6)) }));

			// ---------------------------------------------------------------- Rogue

			// [REV_938] Door of Shadows (1) — Draw a spell. Infuse (2): Add a Temporary copy of it to your hand.
			cards.Add("CORE_REV_938", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => DrawWhere(g, c, p => p is Spell)) }));
			cards.Add("REV_938t", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var spells = c.DeckZone.Where(p => p is Spell).ToList();
					if (spells.Count == 0) return;
					IPlayable drawn = Generic.Draw(c, spells[g.Random.Next(spells.Count)]);
					if (drawn == null || c.HandZone.IsFull) return;
					IPlayable copy = Entity.FromCard(c, drawn.Card);
					Generic.AddHandPhase.Invoke(c, copy);
					Generic.MakeTemporary(copy, s);
				})
			}));
			// [REV_939] Serrated Bone Spike (2) — Deal 3 damage to a minion. If it dies, your next card this turn costs (2) less.
			cards.Add("CORE_REV_939", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(
					new DamageTask(V("REV_939", "damage", 3), EntityType.TARGET, true),
					new CustomTask((g, c, s, t, stack) => stack.Flag = t is Minion m && (m.IsDead || m.ToBeDestroyed)),
					new FlagTask(true, new AddEnchantmentTask("REV_939e2", EntityType.CONTROLLER)))
			}));
			cards.Add("REV_939e2", NextCardEffect(Effects.ReduceCost(V("REV_939", "discount", 2)), new SelfCondition(p => true), thisTurnOnly: true));
			// [REV_827] Sticky Situation (2) — Secret: After your opponent casts a spell, summon a 3/4 Spider with Stealth.
			cards.Add("CORE_REV_827", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.ENEMY,
					SingleTask = ComplexTask.Secret(new SummonTask("REV_827t", SummonSide.SPELL))
				}
			}));
			// [REV_828] Kidnap (2) — Secret: After your opponent plays a minion, stuff it in a 0/4 Sack.
			cards.Add("CORE_REV_828", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_MINION)
				{
					TriggerSource = TriggerSource.ENEMY,
					SingleTask = ComplexTask.Secret(new CustomTask((g, c, s, t, stack) =>
					{
						if (!(t is Minion kidnapped) || kidnapped.Zone?.Type != SabberStoneCore.Enums.Zone.PLAY) return;
						int assetId = kidnapped.Card.AssetId;
						Generic.TransformBlock.Invoke(kidnapped.Controller, Cards.FromId("REV_828t"), kidnapped);
						if (g.IdEntityDic.TryGetValue(kidnapped.Id, out IPlayable sack)) sack[GameTag.TAG_SCRIPT_DATA_NUM_1] = assetId;
					}))
				}
			}));
			cards.Add("REV_828t", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					int assetId = s[GameTag.TAG_SCRIPT_DATA_NUM_1];
					if (assetId > 0) AddToHand(c, Cards.FromAssetId(assetId));
				})
			}));
			// [REV_825] Double Cross (2) — Secret: When your opponent spends all their Mana, draw two cards.
			cards.Add("CORE_REV_825", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_CARD)
				{
					TriggerSource = TriggerSource.ENEMY,
					Condition = new SelfCondition(p => p.Controller.RemainingMana == 0),
					SingleTask = ComplexTask.Secret(new EnqueueTask(2, new DrawTask()))
				}
			}));
			// [REV_959] Ghastly Gravedigger (3) — Battlecry: If you control a Secret, choose a card in your opponent's hand to
			// shuffle into their deck. (User, 2026-10-02: 3 random cards of their hand shown Discover-style, pick 1:
			// Generic.DiscoverFromCards; the move is the shared one.)
			cards.Add("CORE_REV_959", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => Generic.ControlsSecret(c), new CustomTask((g, c, s, t, stack) =>
					Generic.DiscoverFromCards(c, s, Generic.PickFrom(c.Opponent.HandZone, g, null),
						(g2, c2, s2, card) => Generic.MoveToDeck(c2.Opponent, s2, card))))
			}));
			// [REV_826] Private Eye (4) — Battlecry: Cast a Secret from your deck. Combo: Cast 2 instead.
			ISimpleTask CastSecretsFromDeck(int n) => new CustomTask((g, c, s, t, stack) =>
			{
				for (int i = 0; i < n; i++)
				{
					var secrets = c.DeckZone.Where(p => p is Spell sp && sp.IsSecret && !c.SecretZone.Any(z => z.Card.Id == p.Card.Id)).ToList();
					if (secrets.Count == 0 || c.SecretZone.IsFull) return;
					var secret = (Spell)secrets[g.Random.Next(secrets.Count)];
					c.DeckZone.Remove(secret);
					PutSecretIntoPlay(c, secret);
				}
			});
			cards.Add("CORE_REV_826", new CardDef(new Power { PowerTask = CastSecretsFromDeck(1), ComboTask = CastSecretsFromDeck(2) }));
			// [REV_829] Halkias (4) — Stealth. Deathrattle: Store Halkias's soul inside of a friendly Secret. It resummons Halkias when
			// triggered.
			cards.Add("CORE_REV_829", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					var secrets = c.SecretZone.Where(p => p is Spell sp && sp.IsSecret).ToList();
					if (secrets.Count == 0) return;
					secrets[g.Random.Next(secrets.Count)][HALKIAS_SOUL] = 1;
					Generic.AddEnchantmentBlock(g, Cards.FromId("REV_829e"), (IPlayable)s, c, 0, 0, 0);
				})
			}));
			cards.Add("REV_829e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.SECRET_REVEALED)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (t == null || t[HALKIAS_SOUL] == 0 || !(s is Enchantment e)) return;
						Summon(g, c, Cards.FromId("CORE_REV_829"), e);
						RemoveEnchantmentTask.Task.Process(e.Game, e.Controller, e, null); // reverts its effects too
					})
				}
			}));
			// [REV_940] Necrolord Draka (5) — Battlecry: Equip a 1/3 Dagger (+1 Attack for each other card you played this turn, up
			// to 10!).
			cards.Add("CORE_REV_940", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var dagger = (Weapon)Entity.FromCard(c, Cards.FromId("REV_940t"));
					dagger.AttackDamage = System.Math.Min(10, 1 + Generic.CardsPlayedBefore(c, s));
					dagger.Card.Power?.Aura?.Activate(dagger);
					dagger.Card.Power?.Trigger?.Activate(dagger);
					c.Hero.AddWeapon(dagger);
				})
			}));

			// ---------------------------------------------------------------- Shaman

			// [REV_838] Gigantotem (10) — Costs (1) less for each Totem you've summoned this game.
			cards.Add("CORE_REV_838", new CardDef(new Power { Aura = new AdaptiveCostEffect(p => p.Controller.NumTotemSummonedThisGame) }));
			// [REV_920] Convincing Disguise (1) — Transform a friendly minion into one that costs (2) more. Infuse (3): all.
			cards.Add("CORE_REV_920", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 }, { PlayReq.REQ_MINION_TARGET, 0 } },
				new Power
				{
					PowerTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (t is Minion m && CostShifted(g, m, 2) is Card card) Generic.TransformBlock.Invoke(c, card, m);
					})
				}));
			cards.Add("REV_920t", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in c.BoardZone.GetAll().ToList())
						if (CostShifted(g, m, 2) is Card card) Generic.TransformBlock.Invoke(c, card, m);
				})
			}));
			// [REV_917] Carving Chisel (2) — After your hero attacks, summon a random basic Totem.
			cards.Add("CORE_REV_917", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) => Summon(g, c, Cards.FromId(BasicTotems[g.Random.Next(BasicTotems.Length)]), s))
				}
			}));
			// [REV_935] Party Favor Totem (3) — At the end of your turn, summon a random basic Totem. Infuse (3): Summon two instead.
			ISimpleTask PartyFavor(int n) => new CustomTask((g, c, s, t, stack) =>
			{
				for (int i = 0; i < n; i++) Summon(g, c, Cards.FromId(BasicTotems[g.Random.Next(BasicTotems.Length)]), s, ((Minion)s).ZonePosition + 1);
			});
			cards.Add("CORE_REV_935", new CardDef(new Power { Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = InfusedOr(PartyFavor(1), PartyFavor(2)) } }));
			cards.Add("REV_935t", new CardDef(new Power { Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = PartyFavor(2) } }));
			// [REV_924] Primordial Wave (3) — Transform enemy minions into ones that cost (1) less and friendly minions into ones
			// that cost (1) more.
			cards.Add("CORE_REV_924", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in c.Opponent.BoardZone.GetAll().ToList())
						if (CostShifted(g, m, -1) is Card card) Generic.TransformBlock.Invoke(m.Controller, card, m);
					foreach (Minion m in c.BoardZone.GetAll().ToList())
						if (CostShifted(g, m, 1) is Card card) Generic.TransformBlock.Invoke(c, card, m);
				})
			}));
			// [REV_921] The Stonewright (4) — Battlecry: For the rest of the game, your Totems have +2 Attack.
			cards.Add("CORE_REV_921", new CardDef(new Power { PowerTask = new AddEnchantmentTask("REV_921e", EntityType.CONTROLLER) }));
			cards.Add("REV_921e", new CardDef(new Power
			{
				Aura = new MultiAura(
					new Aura(AuraType.BOARD, "REV_921t") { Condition = new SelfCondition(IsTotem) },
					new Aura(AuraType.HAND, "REV_921t") { Condition = new SelfCondition(IsTotem) })
			}));
			cards.Add("REV_921t", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("REV_921", "attack", 2))) }));
			// [REV_517] Criminal Lineup (3) — Choose a friendly minion. Summon 3 copies of it. Overload: (2)
			cards.Add("CORE_REV_517", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 }, { PlayReq.REQ_MINION_TARGET, 0 } },
				new Power
				{
					PowerTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(t is Minion m)) return;
						for (int i = 0; i < V("REV_517", "copies", 3); i++) Summon(g, c, m.Card, s, m.ZonePosition + 1);
					})
				}));

			// ---------------------------------------------------------------- Warlock

			// [REV_242] Flustered Librarian (2) — Has +1 Attack for each Imp you control.
			cards.Add("CORE_REV_242", new CardDef(new Power
			{
				Aura = new AdaptiveEffect(GameTag.ATK, EffectOperator.ADD, p => p.Controller.BoardZone.GetAll().Count(m => m.Id != p.Id && IsImp(m)))
			}));
			// [REV_245] Impending Catastrophe (2) — Draw a card. Repeat for each Imp you control.
			cards.Add("CORE_REV_245", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = 1 + c.BoardZone.GetAll().Count(IsImp);
					for (int i = 0; i < n; i++) Generic.Draw(c);
				})
			}));
			// [REV_374] Shadowborn (3) — Deathrattle: Reduce the Cost of the highest Cost Shadow spell in your hand by (3).
			cards.Add("CORE_REV_374", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					var shadow = c.HandZone.Where(p => p is Spell && p.Card[SPELL_SCHOOL] == SHADOW).OrderByDescending(p => p.Cost).FirstOrDefault();
					if (shadow != null) Give(g, s, shadow, "REV_374e");
				})
			}));
			cards.Add("REV_374e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("REV_374", "discount", 3))) }));
			// [REV_239] Suffocating Shadows (3) — When you play or discard this, destroy a random enemy minion.
			ISimpleTask suffocate = ComplexTask.DestroyRandomTargets(1, EntityType.OP_MINIONS);
			cards.Add("CORE_REV_239", new CardDef(new Power
			{
				PowerTask = suffocate,
				Trigger = new Trigger(TriggerType.DISCARD) { TriggerActivation = TriggerActivation.HAND, TriggerSource = TriggerSource.SELF, SingleTask = suffocate }
			}));
			// [REV_244] Mischievous Imp (4) — Battlecry: Summon a copy of this. Infuse (4): Summon two copies instead.
			cards.Add("CORE_REV_244", new CardDef(new Power { PowerTask = new SummonCopyTask(EntityType.SOURCE, side: SummonSide.RIGHT) }));
			cards.Add("REV_244t", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new SummonCopyTask(EntityType.SOURCE, side: SummonSide.RIGHT), new SummonCopyTask(EntityType.SOURCE, side: SummonSide.RIGHT))
			}));
			// [REV_373] Lady Darkvein (5) — Battlecry: Summon two 2/1 Shades. Each gains a Deathrattle to cast your last Shadow spell.
			cards.Add("CORE_REV_373", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var last = c.PlayHistory.LastOrDefault(e => e.SourceController == c.PlayerId && e.SourceCard.Type == CardType.SPELL
					                                          && e.SourceCard[SPELL_SCHOOL] == SHADOW);
					Generic.BeginSummonGroup(g, s, 2);
					for (int i = 0; i < 2 && !c.BoardZone.IsFull; i++)
					{
						var shade = (Minion)Entity.FromCard(c, Cards.FromId("REV_373t"));
						Generic.SummonBlock.Invoke(g, shade, -1, s); // the shared placement: both sides (live 1S2; was both on her right)
						if (last.SourceCard != null)
						{
							shade[GameTag.TAG_SCRIPT_DATA_NUM_1] = last.SourceCard.AssetId;
							Give(g, s, shade, "REV_373e");
						}
					}
				})
			}));
			cards.Add("REV_373e", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					int assetId = s[GameTag.TAG_SCRIPT_DATA_NUM_1];
					if (assetId > 0) new CastRandomSpellTask().Process(g, c, s, Entity.FromCard(c, Cards.FromAssetId(assetId)));
				})
			}));
			// [REV_372] Shadow Waltz (5) — Summon a 3/5 Shadow with Taunt. If a minion died this turn, summon another.
			cards.Add("CORE_REV_372", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => g.NumMinionsKilledThisTurn > 0,
					new CustomTask((g, c, s, t, stack) => { for (int i = 0; i < 2; i++) Summon(g, c, Cards.FromId("REV_372t"), s); }),
					new CustomTask((g, c, s, t, stack) => Summon(g, c, Cards.FromId("REV_372t"), s)))
			}));
			// [REV_835] Imp King Rafaam (6) — Battlecry: Resurrect four friendly Imps. Infuse (4): Give your Imps +2/+2.
			ISimpleTask Rafaam(bool buff) => new CustomTask((g, c, s, t, stack) =>
			{
				var imps = c.DiedThisGameCards.Where(p => p is Minion && IsImp(p)).Select(p => p.Card).ToList();
				foreach (Card imp in Pick(g, imps, 4)) Summon(g, c, imp, s);
				if (buff) GiveAll(g, s, c.BoardZone.GetAll().Where(IsImp), "REV_835e");
			});
			cards.Add("CORE_REV_835", new CardDef(new Power { PowerTask = Rafaam(false) }));
			cards.Add("REV_835t", new CardDef(new Power { PowerTask = Rafaam(true) }));
			cards.Add("REV_835e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("REV_835", "stats", 2))) }));
			// [REV_240] Tome Tampering (6) — Shuffle 1-Cost copies of cards in your hand into your deck, then discard your hand.
			cards.Add("CORE_REV_240", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var hand = c.HandZone.Where(p => p.Id != s.Id).ToList();
					foreach (IPlayable p in hand)
					{
						IPlayable copy = Entity.FromCard(c, p.Card);
						Generic.ShuffleIntoDeck.Invoke(c, s, copy);
						Give(g, s, copy, "REV_240e");
					}
					foreach (IPlayable p in hand) Generic.DiscardBlock.Invoke(c, p);
				})
			}));
			cards.Add("REV_240e", new CardDef(new Power { Enchant = new Enchant(Effects.SetCost(1)) }));

			// ---------------------------------------------------------------- Warrior

			// [REV_931] Conqueror's Banner (2) — Reveal a card from each player's deck, three times. Draw any of yours that cost more.
			cards.Add("CORE_REV_931", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < 3; i++)
					{
						var mine = c.DeckZone.ToList();
						var theirs = c.Opponent.DeckZone.ToList();
						if (mine.Count == 0) return;
						IPlayable a = mine[g.Random.Next(mine.Count)];
						int theirCost = theirs.Count > 0 ? theirs[g.Random.Next(theirs.Count)].Cost : -1;
						if (a.Cost > theirCost) Generic.Draw(c, a);
					}
				})
			}));
			// [REV_337] Riot! (2) — Your minions can't be reduced below 1 Health this turn. They each attack a random enemy minion.
			cards.Add("CORE_REV_337", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var mine = c.BoardZone.GetAll().ToList();
					GiveAll(g, s, mine, "REV_337e");
					EventMetaData temp = g.CurrentEventData;
					foreach (Minion m in mine)
					{
						var enemies = c.Opponent.BoardZone.GetAll().Where(x => !x.IsDead && !x.ToBeDestroyed).ToList();
						if (enemies.Count == 0 || m.IsDead || m.ToBeDestroyed) continue;
						Generic.AttackBlock.Invoke(c, m, enemies[g.Random.Next(enemies.Count)], true, true);
					}
					g.DeathProcessingAndAuraUpdate();
					g.CurrentEventData = temp;
				})
			}));
			cards.Add("REV_337e", new CardDef(new Power
			{
				Enchant = new Enchant(new Effect(GameTag.HEALTH_MINIMUM, EffectOperator.SET, 1)) { IsOneTurnEffect = true },
				// As Commanding Shout: the minimum holds at the Health loss (Character.TakeDamage, HEALTH_MINIMUM)
			}));
			// [REV_933] Imbued Axe (3) — After your hero attacks, give your damaged minions +1/+2. Infuse (2): +2/+2 instead.
			ISimpleTask ImbuedAxe(string enchant) => new CustomTask((g, c, s, t, stack) =>
				GiveAll(g, s, c.BoardZone.GetAll().Where(m => m.Damage > 0), enchant));
			cards.Add("CORE_REV_933", new CardDef(new Power { Trigger = new Trigger(TriggerType.AFTER_ATTACK) { TriggerSource = TriggerSource.HERO, SingleTask = ImbuedAxe("REV_933e") } }));
			cards.Add("REV_933t", new CardDef(new Power { Trigger = new Trigger(TriggerType.AFTER_ATTACK) { TriggerSource = TriggerSource.HERO, SingleTask = ImbuedAxe("REV_933e2") } }));
			cards.Add("REV_933e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("REV_933", "attack", 1)), Effects.Health_N(V("REV_933", "health", 2))) }));
			cards.Add("REV_933e2", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("REV_933t", "stats", 2))) }));
			// [REV_334] Burden of Pride (4) — Summon three 1/3 Jailers with Taunt. If you have 20 or less Health, give them +1/+1.
			cards.Add("CORE_REV_334", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c.Hero.Health <= V("REV_334", "health", 20), Jailers(true), Jailers(false))
			}));
			cards.Add("REV_334e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("REV_334", "stats", 1))) }));
			// [REV_934] Decimator Olgra (6) — Battlecry: Gain +1/+1 for each damaged minion, then attack all enemies.
			cards.Add("REV_934e", new CardDef(new Power { Enchant = Enchants.Enchants.AddAttackHealthScriptTag }));
			cards.Add("CORE_REV_934", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Minion me)) return;
					int damaged = c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll()).Count(m => m.Damage > 0);
					// engine rule: a stat change is the card's enchantment (client REV_934e "Increased stats"), amount in its script tag
					if (damaged > 0) Generic.AddEnchantmentBlock(g, Cards.FromId("REV_934e"), me, me, damaged, 0);
					Generic.AttackAllEnemies(c, me); // the shared "attack all enemies" (WILD_WEST: Theldurin the Lost too)
				})
			}));
			// [REV_316] Remornia, Living Blade (7) — Rush. After this attacks, equip it.
			cards.Add("CORE_REV_316", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Minion me) || me.IsDead || me.ToBeDestroyed) return;
						int atk = me.AttackDamage, health = me.Health;
						me.Zone.Remove(me);
						c.SetasideZone.Add(me);
						var blade = (Weapon)Entity.FromCard(c, Cards.FromId("REV_316t"));
						blade.AttackDamage = atk;
						blade.Durability = health;
						blade.Card.Power?.Trigger?.Activate(blade);
						c.Hero.AddWeapon(blade);
					})
				}
			}));
			cards.Add("REV_316t", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Weapon blade) || c.BoardZone.IsFull) return;
						var remornia = (Minion)Entity.FromCard(c, Cards.FromId("CORE_REV_316"));
						remornia.AttackDamage = blade.AttackDamage;
						remornia.BaseHealth = System.Math.Max(1, blade.Durability);
						blade.Destroy();
						Generic.SummonBlock.Invoke(g, remornia, -1, s);
					})
				}
			}));

			// Core Infuse cards without a linked infused card (they infuse in place): run the infused version's effect.
			// [MAW_028] Mawsworn Bailiff (5) — Taunt. Battlecry: If you have 4 or more Armor, gain +4/+4.
			cards.Add("MAW_028", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c.Hero.Armor >= V("MAW_028", "armor", 4), new AddEnchantmentTask("MAW_028e2", EntityType.SOURCE))
			}));
			cards.Add("MAW_028e2", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("MAW_028", "stats", 4))) }));

			// [REV_925] Baroness Vashj (4) — If this would transform into a minion, summon that minion instead.
			cards.Add("REV_925", new CardDef(new Power { SummonInsteadOfTransform = true }));

			foreach (var (core, infused) in new[] { ("CORE_REV_937", "REV_937t"), ("CORE_REV_336", "REV_336t4"), ("CORE_REV_350", "REV_350t2"), ("CORE_REV_958", "REV_958t") })
				cards[core].Power.PowerTask = InfusedOr(cards[core].Power.PowerTask, cards[infused].Power.PowerTask);
		}
	}
}
