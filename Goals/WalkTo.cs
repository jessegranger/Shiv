using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using static Shiv.Globals;
using System.Numerics;
using System.Drawing;

namespace Shiv {
	public static partial class Globals {

		private static Vector3[] BezierOnce(float f, Vector3[] points) {
			var n = points.Length;
			var s = 1.0f - f;
			var Q = new Vector3[n]; // TODO: can I re-use the points array, is it for sure on the heap as a params?
			for(int i = 0; i < n - 1; i++ ) {
				Q[i] = (points[i] * s) + (points[i + 1] * f);
			}
			return Q;
		}
		public static Vector3 Bezier(float f, params Vector3[] points) {
			if( points.Length < 1 )
				return Vector3.Zero;
			for(int i = 0; i < points.Length - 1; i++) {
				points = BezierOnce(f, points);
			}
			return points[0];
		}
	}
	class WalkTo : Goal {
		public Func<Vector3> Target;
		public WalkTo(Func<Vector3> target) { Target = target; }
		public WalkTo(Vector3 target) : this(() => target) { }
		public override GoalStatus OnTick() {
			var target = Target();
			var path = Pathfinder.FindPath(PlayerPosition, target);
			if( path == null ) {
				return Status = GoalStatus.Failed;
			} else {
				int n = path.Count();
				if( n < 2 ) return Status = GoalStatus.Complete;
				if( n > 4 ) LookToward(target);
				var step = Bezier(.5f, path.Take(4).ToArray());
				DrawLine(HeadPosition(Self), step, Color.Orange);
				MoveToward(step);
			}
			return Status;
		}
	}
}
