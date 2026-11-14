// HsServer addition (not upstream SabberStone): The Great Dark Beyond (SPACE, client set 1935, with the Heroes of StarCraft
// mini-set SC_) — registration and the set's shared card pieces. Engine rules: Actions/Starship.cs. Card scripts:
// SpaceCardsGenHs2.cs … (one method per chunk). Every number goes through CardValues (V).
using System;
using System.Collections.Generic;
using System.Linq;
using SabberStoneCore.Actions;
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
		private const Race DRAENEI = (Race)2; // client TAG_RACE.DRAENEI

		/// <summary>StarCraft factions (client tags ZERG 3457, TERRAN 3458, PROTOSS 3469).</summary>
		private static bool IsZerg(IPlayable p) => p != null && (p[(GameTag)3457] > 0 || p.Card[(GameTag)3457] > 0);
		private static bool IsTerran(IPlayable p) => p != null && (p[(GameTag)3458] > 0 || p.Card[(GameTag)3458] > 0);
		private static bool IsProtoss(IPlayable p) => p != null && (p[(GameTag)3469] > 0 || p.Card[(GameTag)3469] > 0);
		private static bool IsZergCard(Card x) => x[(GameTag)3457] > 0;
		private static bool IsTerranCard(Card x) => x[(GameTag)3458] > 0;
		private static bool IsProtossCard(Card x) => x[(GameTag)3469] > 0;

		/// <summary>"The next Draenei you play gains …" (Starlight Wanderer, Stranded Spaceman, Ace Wayfinder …): a player enchantment
		/// that waits for a played Draenei (PLAY_MINION, before its Battlecry: Rush works this turn), applies, then leaves.</summary>
		private static CardDef DraeneiFutureBuff(Action<Game, Enchantment, Minion> apply) => new CardDef(new Power
		{
			Trigger = new Trigger(TriggerType.PLAY_MINION)
			{
				TriggerSource = TriggerSource.FRIENDLY,
				Condition = new SelfCondition(p => p is Minion m && m.IsRace(DRAENEI)),
				SingleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Enchantment e) || !((t ?? g.CurrentEventData?.EventSource as IPlayable) is Minion played)) return;
					apply(g, e, played);
					e.Remove();
				})
			}
		});

		/// <summary>The 4/4 Crewmates (client tag 3631: Dirdra's eight, each with its Bonus Effect baked in).</summary>
		private static List<Card> Crewmates() => Cards.All.Where(x => x[(GameTag)3631] > 0 && x.Type == CardType.MINION).OrderBy(x => x.Id).ToList();

		/// <summary>"Battlecry: Summon every adjoining Crewmate" (wiki Dirdra: the Crewmates connected to the played one in hand —
		/// "playing Crewmate 5 summons Crewmates 3, 4, and 6"): the run of Crewmates left and right of the slot it was played from.</summary>
		private static readonly ISimpleTask SummonAdjoiningCrewmates = new CustomTask((g, c, s, t, stack) =>
		{
			foreach (IPlayable p in Generic.AdjoiningRunInHand(c, x => x.Card[(GameTag)3631] > 0))
			{
				if (c.BoardZone.IsFull) break;
				if (p is Minion m && m.Zone?.Type == Zone.HAND) Generic.SummonFromHand(c, m, s);
			}
		});

		/// <summary>An Asteroid's damage: its card's number (TAG_SCRIPT_DATA_NUM_1) plus the player's "Your Asteroids deal N more"
		/// (Bolide Behemoth). Also the shown "@" (Power.HandCounter keeps it on the card).</summary>
		private static int AsteroidDamage(IPlayable p) => p.Card[GameTag.TAG_SCRIPT_DATA_NUM_1] + (p.Controller?[HsTag.ASTEROID_BONUS] ?? 0);

		/// <summary>An Asteroid's hit: <see cref="AsteroidDamage"/> ($: Spell Damage) at a random enemy.</summary>
		private static readonly ISimpleTask AsteroidHit = new CustomTask((g, c, s, t, stack) =>
		{
			var enemies = Enemies(c).Where(x => !x.IsDead && !x.ToBeDestroyed).ToList();
			if (enemies.Count == 0) return;
			Generic.DamageCharFunc((IPlayable)s, enemies[g.Random.Next(enemies.Count)], AsteroidDamage((IPlayable)s), true);
		});

		/// <summary>Shuffle <paramref name="n"/> Asteroids (GDB_430) into the deck.</summary>
		private static void ShuffleAsteroids(Controller c, IEntity source, int n)
		{
			for (int i = 0; i < n; i++)
				Generic.ShuffleIntoDeck.Invoke(c, source, Entity.FromCard(c, Cards.FromId("GDB_430"), new EntityData { [GameTag.CREATOR] = source.Id }));
		}

		/// <summary>"Shuffle your hand" (Eldritch Being): the hand's cards in a random order.</summary>
		private static void ShuffleHand(Game g, Controller c)
		{
			var cards = c.HandZone.GetAll().ToList();
			if (cards.Count < 2) return;
			foreach (IPlayable p in cards) c.HandZone.Remove(p);
			foreach (IPlayable p in cards.OrderBy(_ => g.Random.Next())) c.HandZone.Add(p);
		}

		/// <summary>A Starport token (its RelatedCards' 2/1 launch pieces).</summary>
		private static readonly string[] StarportPieces = { "SC_403a", "SC_403b", "SC_403c", "SC_403d", "SC_403f" };

		/// <summary>"If this costs (0), …" (Librams of Clarity/Divinity/Faith, Void Ray): the cost it was played for was 0
		/// (HsTag.PLAYED_COST_PLUS1, set at the payment). Pair it with Power.GlowAtZeroCost for the hand glow.</summary>
		private static bool PlayedForZero(IEntity s) => s is IPlayable p && p[HsTag.PLAYED_COST_PLUS1] == 1;

		/// <summary>
		/// "Spellburst: … (<i>School</i> spells don't remove this Spellburst.)" (K'ure: Holy, K'ara: Shadow). Wiki K'ure: "Holy
		/// spells trigger the Spellburst effect but do not consume it". After each friendly spell the effect runs; a spell of
		/// another school then spends it (SPELLBURST 0 last, as live's Spellburst blocks end, and the trigger goes).
		/// </summary>
		private static Trigger SpellburstKeptBy(int school, ISimpleTask effect) => new Trigger(TriggerType.AFTER_CAST)
		{
			TriggerSource = TriggerSource.FRIENDLY,
			SingleTask = ComplexTask.Create(effect, new CustomTask((g, c, s, t, stack) =>
			{
				IPlayable spell = t ?? g.CurrentEventData?.EventSource as IPlayable;
				if (spell != null && spell[SPELL_SCHOOL] == school) return;
				if (!(s is IPlayable me)) return;
				me[HsTag.SPELLBURST] = 0;
				me.ActivatedTrigger?.Remove();
			}))
		};

		/// <summary>The set's card scripts.</summary>
		private static void Space(IDictionary<string, CardDef> cards)
		{
			SpaceChunk1(cards);
			SpaceChunk2(cards);
			SpaceChunk3(cards);
			SpaceChunk4(cards);
		}
	}
}
