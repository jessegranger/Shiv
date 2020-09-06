using static Shiv.Global;
using static GTA.Native.Hash;
using static GTA.Native.Function;
using StateMachine;

namespace Shiv {
	class Wander : State {
		public override State OnTick() {
			if( GetScriptTaskStatus(Self, TaskStatusHash.TASK_WANDER_STANDARD) == 7 ) {
				Call(TASK_WANDER_STANDARD, Self, 10f, 10);
			}
			return this;
		}
	}
}