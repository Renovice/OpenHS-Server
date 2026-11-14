// HsServer addition (not upstream SabberStone): "Excavate a treasure." (Generic.Excavate)
using SabberStoneCore.Actions;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Tasks.SimpleTasks
{
	public class ExcavateTask : SimpleTask
	{
		public override TaskState Process(in Game game, in Controller controller, in IEntity source, in IPlayable target,
			in TaskStack stack = null)
		{
			IPlayable treasure = Generic.Excavate(controller, (IPlayable)source);
			if (treasure != null && stack != null) stack.Playables = new System.Collections.Generic.List<IPlayable> { treasure };
			return TaskState.COMPLETE;
		}
	}
}
