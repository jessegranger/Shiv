using System.Linq;
using static Shiv.Global;
using static Shiv.NavMesh;
using System.Numerics;

namespace Shiv {
	class Explore : State {
		public override State OnTick() {
			var pos = Position(NavMesh.LastGrown);
			if( pos == Vector3.Zero || DistanceToSelf(pos) < 2f ) {
				Log($"Exploring via Flood");
				NodeHandle ungrown = Flood(PlayerNode, 40000, 20, default, Edges)
					.Without(IsGrown)
					.FirstOrDefault();
				// .Min(DistanceToSelf);
				if( ungrown != NodeHandle.Invalid ) {
					return new WalkTo(ungrown, this);
				}
			} else {
				Log($"Exploring last grown");
				return new WalkTo(Position(LastGrown), this);
			}
			return this;
		}
	}
}
