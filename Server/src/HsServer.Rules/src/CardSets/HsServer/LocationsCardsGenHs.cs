// HsServer addition (not upstream SabberStone): the Core Locations. A Location's PowerTask is its activation and its play
// requirements are the activation's targeting (Model/Entities/Location.cs; wire format RESEARCH/locations-protocol.md).
// Every number goes through CardValues (V).
using System.Collections.Generic;
using System.Linq;
using SabberStoneCore.Actions;
using SabberStoneCore.Enchants;
using SabberStoneCore.Enums;
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
		/// <summary>Controller tag: the turn a Relic Vault was used ("the next Relic you play this turn casts twice").</summary>
		private const GameTag RELIC_VAULT_TURN = (GameTag)990012;

		private static readonly Dictionary<PlayReq, int> LocationFriendlyMinion = new Dictionary<PlayReq, int>
			{ { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 }, { PlayReq.REQ_MINION_TARGET, 0 } };

		/// <summary>A played Relic casts twice after a Relic Vault this turn (once per Vault use).</summary>
		private static int RelicCasts(Game g, Controller c)
		{
			if (c[RELIC_VAULT_TURN] != g.Turn) return 1;
			c[RELIC_VAULT_TURN] = 0;
			return 2;
		}

		private static void Locations(IDictionary<string, CardDef> cards)
		{
			// [REV_290] Cathedral of Atonement (3) — Give a minion +2/+1 and draw a card.
			cards.Add("CORE_REV_290", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(new AddEnchantmentTask("REV_290e", EntityType.TARGET), new DrawTask())
			}));
			cards.Add("REV_290e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.Attack_N(V("REV_290", "attack", 2)), Effects.Health_N(V("REV_290", "health", 1)))
			}));
			// [REV_333] Hedge Maze (3) — Trigger a friendly minion's Deathrattle.
			cards.Add("CORE_REV_333", new CardDef(new Dictionary<PlayReq, int>
				{
					{ PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_TARGET_WITH_DEATHRATTLE, 0 }
				},
				new Power { PowerTask = new ActivateDeathrattleTask(EntityType.TARGET) }));
			// [REV_362] Castle Kennels (2) — Give a friendly minion +2 Attack. If it's a Beast, give it Rush.
			cards.Add("CORE_REV_362", new CardDef(LocationFriendlyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					Give(g, s, m, "REV_362e");
					if (m.IsRace(Race.BEAST)) m.IsRush = true;
				})
			}));
			cards.Add("REV_362e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("REV_362", "attack", 2))) }));
			// [REV_371] Vile Library (2) — Give a friendly minion +1/+1. Repeat for each Imp you control.
			cards.Add("CORE_REV_371", new CardDef(LocationFriendlyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					int times = 1 + c.BoardZone.GetAll().Count(IsImp);
					for (int i = 0; i < times; i++) Give(g, s, m, "REV_371e");
				})
			}));
			cards.Add("REV_371e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("REV_371", "stats", 1))) }));
			// [REV_602] Nightcloak Sanctum (3) — Freeze a minion. Summon a 2/2 Volatile Skeleton.
			cards.Add("CORE_REV_602", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(ComplexTask.Freeze(EntityType.TARGET), new SummonTask("CORE_REV_845", SummonSide.DEFAULT))
			}));
			// [REV_750] Sinstone Graveyard (3) — Summon a 1/1 Ghost. (Has +1/+1 for each other card you played this turn!)
			cards.Add("CORE_REV_750", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c.BoardZone.IsFull) return;
					int stats = V("REV_750", "stats", 1) + c.NumCardsPlayedThisTurn; // a Location's use isn't a card played
					var ghost = (Minion)Entity.FromCard(c, Cards.FromId("REV_750t2"));
					ghost.AttackDamage = stats;
					ghost.BaseHealth = stats;
					Generic.SummonBlock.Invoke(g, ghost, -1, s);
				})
			}));
			// [REV_923] Muck Pools (1) — Transform a friendly minion into one that costs (1) more.
			cards.Add("CORE_REV_923", new CardDef(LocationFriendlyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is Minion m && CostShifted(g, m, V("REV_923", "cost", 1)) is Card card) Generic.TransformBlock.Invoke(c, card, m);
				})
			}));
			// [REV_942] Relic Vault (2) — The next Relic you play this turn casts twice.
			cards.Add("CORE_REV_942", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => c[RELIC_VAULT_TURN] = g.Turn)
			}));
			// [REV_983] Great Hall (2) — Set a minion's Attack and Health to 3.
			cards.Add("CORE_REV_983", new CardDef(TargetMinion, new Power { PowerTask = new AddEnchantmentTask("REV_983e", EntityType.TARGET) }));
			cards.Add("REV_983e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.SetAttack(V("REV_983", "attack", 3)), Effects.SetMaxHealth(V("REV_983", "health", 3)))
			}));
			// [REV_990] Sanguine Depths (1) — Deal 1 damage to a minion and give it +2 Attack.
			cards.Add("CORE_REV_990", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(
					new DamageTask(V("REV_990", "damage", 1), EntityType.TARGET),
					new AddEnchantmentTask("REV_990e", EntityType.TARGET))
			}));
			cards.Add("REV_990e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("REV_990", "attack", 2))) }));

			// ---------------------------------------------------------------- Newer Locations (captured live: RESEARCH/locations-protocol.md)

			// [JAIL_887] Zuramat's Prison (5) — Choose a card to discard to summon a 5/5 Taunt. Deathrattle: Free Zuramat who plays one
			// each turn! (Its target is a card in your hand: CAN_TARGET_CARDS_IN_HAND.)
			cards.Add("JAIL_887", new CardDef(TargetAny, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t == null || t.Zone?.Type != SabberStoneCore.Enums.Zone.HAND) return;
					RememberDiscard(s, t.Card.AssetId);
					Generic.DiscardBlock.Invoke(c, t);
					Summon(g, c, Cards.FromId("JAIL_887t3"), s);
				}),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c.BoardZone.IsFull) return;
					var zuramat = (Minion)Entity.FromCard(c, Cards.FromId("JAIL_887t2"));
					foreach (GameTag slot in PrisonDiscards) zuramat[slot] = s[slot]; // what the Prison discarded
					Generic.SummonBlock.Invoke(g, zuramat, -1, s);
				})
			}));
			// [JAIL_887t2] Zuramat the Obliterator (8) — At the end of your turn, play a card discarded by Zuramat's Prison.
			cards.Add("JAIL_887t2", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						var stored = PrisonDiscards.Where(slot => s[slot] > 0).ToList();
						if (stored.Count == 0) return;
						GameTag slot = stored[g.Random.Next(stored.Count)];
						Card card = Cards.FromAssetId(s[slot]);
						s[slot] = 0;
						if (card != null) PlayForFree(g, c, s, card);
					})
				}
			}));
			// [JAIL_987] Low Security Wing (1) — Get a random Shaman minion. It's locked in your hand until you play another card.
			cards.Add("JAIL_987", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Card minion = RandomCard(g, x => x.Type == CardType.MINION && x.HasClass(CardClass.SHAMAN) && x.Collectible);
					if (minion == null || c.HandZone.IsFull) return;
					IPlayable locked = Entity.FromCard(c, minion);
					Generic.AddHandPhase.Invoke(c, locked);
					Give(g, s, locked, "JAIL_987e");
				})
			}));
			// [JAIL_987e] Low Security — Can't be played until another card is played. (LITERALLY_UNPLAYABLE + UNPLAYABLE_VISUALS, as live.)
			cards.Add("JAIL_987e", new CardDef(new Power
			{
				Enchant = new Enchant(new Effect(HsTag.LITERALLY_UNPLAYABLE, EffectOperator.SET, 1), new Effect(HsTag.UNPLAYABLE_VISUALS, EffectOperator.SET, 1)),
				Trigger = new Trigger(TriggerType.PLAY_CARD)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					TriggerActivation = TriggerActivation.HAND_OR_PLAY, // it sits on a card in hand
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is Enchantment e && t != null && t.Id != e.Target.Id) RemoveEnchantmentTask.Task.Process(e.Game, e.Controller, e, null); // reverts its effects too
					})
				}
			}));
			// [TTN_090] Prison of Yogg-Saron (7) — Choose a character. Cast 4 random spells (targeting it if possible).
			cards.Add("TTN_090", new CardDef(TargetAny, new Power
			{
				PowerTask = new EnqueueTask(V("TTN_090", "spells", 4), new CastRandomSpellTask(preferTarget: true))
			}));
			// [CATA_301] Ruby Sanctum (1) — Your next Healing effect this turn deals damage instead.
			cards.Add("CATA_301", new CardDef(new Power
			{
				// A fresh "next Healing effect": forget which heal a previous activation converted.
				PowerTask = ComplexTask.Create(new SetControllerGameTagTask(HsTag.NEXT_HEAL_SOURCE, 0),
					new AddEnchantmentTask("CATA_301e", EntityType.CONTROLLER))
			}));
			cards.Add("CATA_301e", new CardDef(new Power
			{
				Enchant = new Enchant(new Effect(HsTag.NEXT_HEAL_DAMAGES, EffectOperator.SET, 1)) { IsOneTurnEffect = true }
			}));
			// [ETC_449] Fan Club (1) — Restore 3 Health to all friendly characters.
			cards.Add("ETC_449", new CardDef(new Power { PowerTask = new HealTask(V("ETC_449", "heal", 3), EntityType.FRIENDS) }));
		}

		private static readonly GameTag[] PrisonDiscards = { HsTag.PRISON_DISCARD_1, HsTag.PRISON_DISCARD_2, HsTag.PRISON_DISCARD_3 };

		/// <summary>Zuramat's Prison keeps what it discarded (up to its three uses) for Zuramat.</summary>
		private static void RememberDiscard(IEntity prison, int assetId)
		{
			foreach (GameTag slot in PrisonDiscards)
				if (prison[slot] == 0) { prison[slot] = assetId; return; }
		}

		/// <summary>"Play" a card for free outside the hand: a minion is summoned, a spell cast (random targets), a weapon equipped.</summary>
		/// <summary>"Play" is not "summon": the shared engine action (far right, Battlecry resolves, spells cast).</summary>
		private static void PlayForFree(Game g, Controller c, IEntity source, Card card) => Generic.PlayByEffect(g, c, source, card);
	}
}
