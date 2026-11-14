// HsServer addition (not upstream SabberStone): activating a Location. Wire format: RESEARCH/locations-protocol.md.
using System.Collections.Generic;
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks;
using SabberStoneCore.Tasks.SimpleTasks;

namespace SabberStoneCore.Actions
{
	public static partial class Generic
	{
		/// <summary>
		/// HsServer (livecheck 2026-10-10): is this a minion? A Location is a Minion entity in the engine (it takes a board slot), but
		/// not a minion card (wiki Location.txt:13: "while minions and locations share the same zone, locations are not classified as
		/// minions, as they are their own card type"). Every "a minion" check on cards in a hand or deck, and SelfCondition.IsMinion,
		/// asks this (live session-20260930-071519 9819: playing a Location left Mug's Magic's "first minion" discount up).
		/// </summary>
		public static bool IsMinionCard(IEntity e) => e is Minion && !(e is Location);

		/// <summary>
		/// Activate a Location (optionally on a target: a character, or a card in its owner's hand), in live order:
		/// PLAY block [CARD_TARGET] → POWER block (the card's power, then LOCATION_ACTION_COOLDOWN=1) → player uses counter →
		/// DAMAGE+1 → EXHAUSTED=1, or at DAMAGE &gt;= HEALTH it breaks (DEATHS block inside the PLAY block) → options played.
		/// </summary>
		public static bool ActivateLocation(Controller c, Location location, IPlayable target)
		{
			Game game = c.Game;
			if (!location.CanActivate)
				return false;
			List<IPlayable> targets = location.ActivationTargets();
			if (target != null && (targets == null || !targets.Contains(target)))
				return false;
			if (target == null && targets != null && targets.Count > 0 && (location.Card.MustHaveTargetToPlay || location.TargetsCardsInHand || location.Card.RequiresTargetIfAvailable))
				return false;

			game.CurrentEventData = new EventMetaData(location, target);
			if (game.History)
				game.PowerHistory.Add(PowerHistoryBuilder.BlockStart(BlockType.PLAY, location.Id, "", 0, target?.Id ?? 0));
			if (target != null)
				location.CardTarget = target.Id;

			// HsServer (ISLAND_VACATION): the last charge of a location that summons breaks it first (wiki Location, patch
			// 32.0.0.217964: "Using the last charge on a location that summons a minion will now clear the location away first
			// and then summon the minion"; RESEARCH/sources/wiki/Location.txt). Its card text says it summons.
			bool breaksFirst = location.Damage + 1 >= location.Health && SummonsOnUse(location.Card);
			if (breaksFirst)
			{
				location.Damage += 1;
				game.DeathProcessingAndAuraUpdate();
			}

			// The effect and the cooldown share the POWER block (live sets the cooldown as the block's last change).
			ISimpleTask cooldown = new CustomTask((g, controller, source, t, stack) => ((Location)source).OnCooldown = true);
			ISimpleTask effect = location.Card.Power?.PowerTask;
			game.TaskQueue.StartEvent();
			game.TaskQueue.Enqueue(effect == null ? cooldown : ComplexTask.Create(effect, cooldown), c, location, target);
			game.ProcessTasks();
			game.TaskQueue.EndEvent();

			c[Location.LOCATION_USES_THIS_GAME] = c[Location.LOCATION_USES_THIS_GAME] + 1;
			// HsServer (ISLAND_VACATION): "After you use a location" (XB-931 Housekeeper) — after its power, in its PLAY block
			// (unrecorded position).
			game.TaskQueue.StartEvent();
			game.TriggerManager.OnLocationUsedTrigger(location);
			game.ProcessTasks();
			game.TaskQueue.EndEvent();
			if (!breaksFirst)
			{
				location.Damage += 1; // durability: breaks (ToBeDestroyed) at DAMAGE >= HEALTH
				if (!location.ToBeDestroyed)
					location.IsExhausted = true;
			}
			game.DeathProcessingAndAuraUpdate();

			// HsServer (TIME_TRAVEL): "Advance to the present!" — a location whose DISPLAY_CARD_ON_MOUSEOVER names another location
			// changes into it after its use, keeping its used durability and cooldown (Past → Present → Future Silvermoon)
			if (!location.ToBeDestroyed && location.Zone?.Type == Zone.PLAY && location.Card[GameTag.DISPLAY_CARD_ON_MOUSEOVER] > 0 &&
			    Cards.FromAssetId(location.Card[GameTag.DISPLAY_CARD_ON_MOUSEOVER]) is Card next && next.Type == CardType.LOCATION &&
			    location.Card.Text?.Contains("Advance to the") == true)
				ChangeEntityBlock(c, location, next, false);

			c.NumOptionsPlayedThisTurn++;
			if (game.History)
				game.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
			EndPlayerPlay(game, location);
			game.CurrentEventData = null;
			return true;
		}

		/// <summary>The location's power summons (its client text says "summon"): its last charge breaks it first.</summary>
		private static bool SummonsOnUse(Card card) =>
			card?.Text != null && System.Text.RegularExpressions.Regex.IsMatch(card.Text, @"[Ss]ummon");
	}
}
