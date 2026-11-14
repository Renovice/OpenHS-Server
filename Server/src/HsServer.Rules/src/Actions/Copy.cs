using SabberStoneCore.Enums;
using SabberStoneCore.Model.Entities;
using System.Collections.Generic;
using SabberStoneCore.Enchants;

namespace SabberStoneCore.Actions
{
	public partial class Generic
	{
		/// <summary>A copy of <paramref name="source"/> made by <paramref name="creator"/> and put into <paramref name="targetZone"/>
		/// (the copy is placed there: never add it again). <paramref name="position"/> (PLAY only, 0-based board slot): where the
		/// copy goes when the card says so by its live placement (Freya's Guardians of Life: each copy right of its original,
		/// live-20261007-vfx-decks 179049-179075); -1 = Blizzard's summon placement for the creator (Summon.SummonPosition).</summary>
		/// <param name="prepare">HsServer (TITANS review): what the copy gets before it enters its zone (Aman'Thul's Shape the Stars:
		/// "a copy of it with +2/+2" — live-20261007-vfx-decks 45066-45140 builds the copy, its copied enchantments and TTN_429e,
		/// then summons it).</param>
		/// <param name="keepEnchantments">HsServer (TIME_TRAVEL): set when the copy is set aside as an option for the zone its
		/// original's zone flows to (Generic.DiscoverCopyOf: a hand or deck card offered for the hand keeps its enchantments).</param>
		public static IPlayable Copy(in Controller controller, in IEntity creator, in IPlayable source, Zone targetZone, bool deathrattle = false, int position = -1,
			System.Action<IPlayable> prepare = null, bool? keepEnchantments = null)
		{
			// Determine whether enchantments should be also copied.
			// Whenever a card moves forward in that flow (Deck -> Hand, Hand -> Play, Deck -> Play),
			// it retains enchantments. If a card moves backwards in zones
			// (Play -> Hand, Hand -> Deck, Play -> Deck, Play/Hand/Deck -> Graveyard and
			// Graveyard -> Play/Hand/Deck), it loses enchantments.
			// https://playhearthstone.com/en-gb/blog/21965466
			bool copyEnchantments;

			Zone sourceZone = source.Zone?.Type ?? Zone.PLAY;

			if (sourceZone == Zone.GRAVEYARD)
				copyEnchantments = false;
			else if (sourceZone == targetZone)
				copyEnchantments = true;
			else if (targetZone == Zone.SETASIDE)
				copyEnchantments = false;
			else if (targetZone == Zone.PLAY)
				copyEnchantments = true;
			else if (sourceZone == Zone.DECK)
				copyEnchantments = true;
			else if (sourceZone == Zone.HAND && targetZone != Zone.DECK)
				copyEnchantments = true;
			else
				copyEnchantments = false;
			if (keepEnchantments.HasValue) copyEnchantments = keepEnchantments.Value;

			IPlayable copiedEntity;

			if (copyEnchantments)
			{
				var tags = new EntityData()
				{
					[GameTag.DISPLAYED_CREATOR] = creator.Id
				};

				copiedEntity = Entity.FromCard(in controller, source.Card, tags);
				copiedEntity[HsTag.COPIED_FROM_ENTITY_ID] = source.Id; // HsServer: live, the copy names its original
				// HsServer (WHIZBANG): and its own script numbers ("(Trade to upgrade!)" kept: live vfx 200789 Wind-Up Enforcer)
				foreach (GameTag num in new[] { GameTag.TAG_SCRIPT_DATA_NUM_1, GameTag.TAG_SCRIPT_DATA_NUM_2 })
					if (source[num] != source.Card[num]) copiedEntity[num] = source[num];

				int? modifiedCost = ((Playable)source)._modifiedCost;

				if (modifiedCost.HasValue)
					copiedEntity.Cost = modifiedCost.Value;

				if (copiedEntity is Character c)
					((Character)source).CopyInternalAttributes(in c);

				// HsServer (TITANS): wiki Titan — "When a Titan is copied, the state of its abilities is preserved" (its own new
				// ability entities, made with it, show it)
				CopyTitanState(source, copiedEntity);


				if (source.AppliedEnchantments != null)
				{
					foreach (Enchantment e in source.AppliedEnchantments)
					{
						Enchantment instance = Enchantment.GetInstance(in controller, e.Creator, copiedEntity, e.Card);
						if (e[GameTag.TAG_SCRIPT_DATA_NUM_1] > 0)
						{
							instance[GameTag.TAG_SCRIPT_DATA_NUM_1] = e[GameTag.TAG_SCRIPT_DATA_NUM_1];
							if (e[GameTag.TAG_SCRIPT_DATA_NUM_2] > 0)
								instance[GameTag.TAG_SCRIPT_DATA_NUM_2] = e[GameTag.TAG_SCRIPT_DATA_NUM_2];
						}
						instance.CapturedCard = e.CapturedCard;

						if (e.IsOneTurnActive)
							instance.Game.OneTurnEffectEnchantments.Add(instance);
					}
					
				}

				//var kvps = new KeyValuePair<GameTag, int>[source.NativeTags.Count];
				//source.NativeTags.CopyTo(kvps, 0);

				//for (int i = 0; i < kvps.Length; i++)
				//{
				//	switch (kvps[i].Key)
				//	{
				//		case GameTag.ENTITY_ID:
				//		case GameTag.CONTROLLER:
				//		case GameTag.ZONE:
				//		case GameTag.ZONE_POSITION:
				//		case GameTag.CREATOR:
				//		case GameTag.DISPLAYED_CREATOR:
				//		case GameTag.EXHAUSTED:
				//			continue;
				//		case GameTag.COST:
				//			copiedEntity.AuraEffects.ToBeUpdated = true;
				//			goto default;
				//		default:
				//			tags.Add(kvps[i]);
				//			break;
				//	}
				//}

				List<(int entityId, IEffect effect)> oneTurnEffects = controller.Game.OneTurnEffects;
				for (int i = oneTurnEffects.Count - 1; i >= 0; i--)
				{
					(int id, IEffect effect) = oneTurnEffects[i];
					if (id == source.Id)
						oneTurnEffects.Add((copiedEntity.Id, effect));
				}
			}
			else
			{
				copiedEntity = Entity.FromCard(in controller, source.Card);
				copiedEntity.NativeTags[GameTag.DISPLAYED_CREATOR] = creator.Id;
			}

			// HsServer: wiki Dormant: minions always start Dormant, "The exception is when the player copies a Dormant minion
			// that has already awakened" (a copy made straight into play of one in play and awake; BoardZone.Add)
			if (targetZone == Zone.PLAY && source is Minion original && original.Zone?.Type == Zone.PLAY && original[GameTag.DORMANT] == 0
				&& (original.Card.DormantTurns > 0 || original.Card.StartsDormant))
				copiedEntity[HsTag.AWAKENED_COPY] = 1;

			// HsServer: rules (user, 2026-10-01) — a minion that already came back through Reborn (HAS_BEEN_REBORN) has spent
			// it: returning or copying it (Redemption) gives no Reborn again. One that still had Reborn keeps it.
			if (source[GameTag.HAS_BEEN_REBORN] > 0 && copiedEntity is Minion && targetZone == Zone.PLAY)
			{
				copiedEntity[GameTag.HAS_BEEN_REBORN] = 1;
				copiedEntity[GameTag.REBORN] = 0;
			}

			// HsServer (VIOLET_HOLD): "a copy of an opponent's card" (Mind Sweeper, Unshackle Soul, Enthralled Shade, Theft Accusation)
			if (source.Controller != null && source.Controller != controller)
				copiedEntity[HsTag.COPIED_FROM_OPPONENT] = 1;

			prepare?.Invoke(copiedEntity);

			switch (targetZone)
			{
				case Zone.HAND:
					Generic.AddHandPhase.Invoke(controller, copiedEntity);
					break;
				case Zone.DECK:
					Generic.ShuffleIntoDeck.Invoke(controller, creator, copiedEntity);
					break;
				case Zone.PLAY:
					int slot = position >= 0 ? System.Math.Min(position, controller.BoardZone.Count) : -1;
					if (deathrattle)
					{
						slot = ((Minion) source).LastBoardPosition;
						if (slot > controller.BoardZone.Count)
							slot = controller.BoardZone.Count;
					}
					Generic.SummonBlock.Invoke(controller.Game, (Minion) copiedEntity, slot, creator);
					break;
				case Zone.SETASIDE:
					controller.SetasideZone.Add(copiedEntity);
					break;
			}

			if (copyEnchantments && source.OngoingEffect != null && copiedEntity.OngoingEffect == null)
				source.OngoingEffect.Clone(copiedEntity);

			return copiedEntity;
		}
	}
}
