using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
					return new MoveTo(ungrown, this);
				}
			} else {
				Log($"Exploring last grown");
				return new MoveTo(Position(LastGrown), this);
			}
			return this;
		}
	}
}
