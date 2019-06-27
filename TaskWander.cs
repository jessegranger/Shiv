using static Shiv.Global;
using static GTA.Native.Hash;
using static GTA.Native.Function;

namespace Shiv {
	internal class TaskWander : Goal {
		public uint Started = 0;
		public override GoalStatus OnTick() {
			if( Started == 0 ) {
				Started = GameTime;
				Call(TASK_WANDER_STANDARD, Self, 10f, 10);
			} else {
				if( GetScriptTaskStatus(Self, TaskStatusHash.TASK_WANDER_STANDARD) == 7 ) {
					return GoalStatus.Complete;
				}
			}
			return GoalStatus.Active;
		}
	}
}