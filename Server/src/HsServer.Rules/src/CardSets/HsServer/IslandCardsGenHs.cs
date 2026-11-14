// HsServer addition (not upstream SabberStone): Perils in Paradise (ISLAND_VACATION, client set 1905, with the Traveling Travel
// Agency mini-set WORK_) — registration and the set's shared card pieces. Engine rules: Actions/Paradise.cs. Card scripts:
// IslandCardsGenHs2.cs … (one method per chunk). Every number goes through CardValues (V).
using System.Collections.Generic;
using SabberStoneCore.Actions;
using SabberStoneCore.Conditions;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks;
using SabberStoneCore.Tasks.SimpleTasks;
using SabberStoneCore.Triggers;
using SabberStoneCore.Enums;
using SabberStoneCore.Loader;
using SabberStoneCore.src.Loader;

namespace SabberStoneCore.CardSets.HsServer
{
	public static partial class CurrentCoreCardsGen
	{
		/// <summary>"… reopen this" (Horizon's Edge, Dangerous Cliffside, Tide Pools …): a trigger on the location whose task is
		/// Generic.Reopen (its TRIGGER block shows even when the location is already open: live Tide Pools).</summary>
		private static Trigger Reopens(TriggerType type, TriggerSource source = TriggerSource.FRIENDLY, SelfCondition condition = null) => new Trigger(type)
		{
			TriggerSource = source,
			Condition = condition,
			SingleTask = new CustomTask((g, c, s, t, stack) => Generic.Reopen(s))
		};

		/// <summary>A new card of <paramref name="card"/> to <paramref name="to"/>'s hand (CREATOR = the source); the entity or null.</summary>
		private static IPlayable IslandToHand(Controller to, IEntity source, Card card)
		{
			if (card == null || to.HandZone.IsFull) return null;
			IPlayable p = Entity.FromCard(to, card, new EntityData { [GameTag.CREATOR] = source.Id });
			return Generic.AddHandPhase.Invoke(to, p) ? p : null;
		}

		/// <summary>The set's card scripts.</summary>
		private static void Island(IDictionary<string, CardDef> cards)
		{
			IslandChunk1(cards);
			IslandChunk2(cards);
			IslandChunk3(cards);
			IslandChunk4(cards);
		}
	}
}
