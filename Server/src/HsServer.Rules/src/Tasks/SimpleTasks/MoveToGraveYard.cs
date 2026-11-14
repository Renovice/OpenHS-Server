#region copyright
// SabberStone, Hearthstone Simulator in C# .NET Core
// Copyright (C) 2017-2019 SabberStone Team, darkfriend77 & rnilva
//
// SabberStone is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as
// published by the Free Software Foundation, either version 3 of the
// License.
// SabberStone is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.
#endregion
using SabberStoneCore.Actions;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Tasks.SimpleTasks
{
	public class MoveToGraveYard : SimpleTask
	{
		public MoveToGraveYard(EntityType type)
		{
			Type = type;
		}

		public EntityType Type { get; set; }

		public override TaskState Process(in Game game, in Controller controller, in IEntity source,
			in IPlayable target,
			in TaskStack stack = null)
		{
			//List<IPlayable> entities = IncludeTask.GetEntities(Type, in controller, source, target, stack?.Playables);
			//entities.ForEach(p =>
			var entities = IncludeTask.GetEntities(Type, in controller, source, target, stack?.Playables);
			foreach (IPlayable p in entities)
			{
				// HsServer: a deck card is revealed into the graveyard as live (Gnomeferatu); several at once without the
				// per-card animation
				if (p.Zone?.Type == Zone.DECK)
				{
					Generic.DeckCardToGraveyard(p, source?.Id ?? 0, entities.Count > 1);
					continue;
				}
				// HsServer: a Secret in play is destroyed by the source, as live (Generic.DestroySecret)
				if (p.Zone?.Type == Zone.SECRET && p is Spell secret)
				{
					Generic.DestroySecret(secret, reveal: secret[GameTag.REVEALED] == 1, source);
					continue;
				}
				p.Controller.GraveyardZone.Add(p.Zone.Remove(p));
				if (p.Card.IsSecret && p[GameTag.REVEALED] == 1)
				{
					IPlayable revealed = p;
					game.RaiseEvent(() => revealed.Game.TriggerManager.OnSecretRevealedTrigger(revealed));
				}
			}
			return TaskState.COMPLETE;
		}
	}
}
