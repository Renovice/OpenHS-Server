using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Tasks.SimpleTasks
{
	/// <summary>
	/// HsServer: <b>Counter</b> a card being played (Counterspell, Objection!…). Live (EX1_287 5/5, MAW_006 4/4): inside the
	/// countering block the card is REVEALED, named (TARGET: the block's recorded TARGETS) and gets CANT_PLAY; after the
	/// block the client pauses 3 s on it before it leaves (PlayCard: the countered spell / minion). The countering card's
	/// own "Counter" effect, when it has one, plays around the CANT_PLAY (live Objection!).
	/// </summary>
	public class CounterTask : SimpleTask
	{
		public CounterTask(EntityType entityType) => Type = entityType;

		public EntityType Type { get; set; }

		public override TaskState Process(in Game game, in Controller controller, in IEntity source, in IPlayable target,
			in TaskStack stack = null)
		{
			foreach (IPlayable p in IncludeTask.GetEntities(Type, in controller, source, target, stack?.Playables))
			{
				p[GameTag.REVEALED] = 1;
				// the countering card's own counter effect around it, when it has one (live Objection!:
				// TSCFX_BlademasterOkani_Counter_Impact_Super holding the CANT_PLAY)
				IPlayable countered = p;
				Actions.Generic.NamedFx(source as IPlayable, "_Counter_Impact", new[] { countered.Id }, () => countered[GameTag.CANT_PLAY] = 1);
			}
			return TaskState.COMPLETE;
		}
	}
}
