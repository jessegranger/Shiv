using static Shiv.Global;
using System.Numerics;

namespace Shiv {
	class Teleport : State {
		public Vector3 Target;
		public bool Started { get; private set; } = false;
		public Teleport(NodeHandle node, State next) : base(next) => Target = NavMesh.Position(node);
		public Teleport(Vector3 pos, State next) : base(next) => Target = pos;
		public override State OnTick() {
			if( !Started ) {
				StartTeleport(CurrentPlayer, Target, 0f, false, false);
			} else if( IsTeleportComplete(CurrentPlayer) ) {
				return Next;
			}
			return this;
		}
	}
}