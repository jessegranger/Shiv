using static Shiv.Global;
using static GTA.Native.Hash;
using static GTA.Native.Function;
using System.Numerics;

namespace Shiv {
	class Teleport : State {
		public Vector3 Target;
		public float Heading = 0f;
		public bool Started { get; private set; } = false;
		public Teleport(NodeHandle node, State next=null) : base(next) => Target = NavMesh.Position(node);
		public Teleport(Vector3 pos, State next=null) : base(next) => Target = pos;
		public override State OnTick() {
			if( GamePaused )
				return this;
			if( !Started ) {
				Log($"Starting teleport to ${Target}");
				Call(SET_ENTITY_HAS_GRAVITY, Self, false);
				StartTeleport(CurrentPlayer, Target, Heading, false, false);
				Started = true;
			} else if( IsTeleportComplete(CurrentPlayer) ) {
				Call(SET_ENTITY_HAS_GRAVITY, Self, true);
				return Next;
			}
			return this;
		}
	}
}