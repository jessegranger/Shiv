using System.Diagnostics;
using System.Linq;
using System.Numerics;
using static Shiv.Global;
using static Shiv.NavMesh;

namespace Shiv {
	public static partial class Global {
		public static NodeHandle FindNearbyCover() {
			return Flood(PlayerNode, 10000, 100, default, Edges)
				.Where(n => IsCover(n))
				.Min(DistanceToSelf);
		}
		public static NodeHandle FindNearbyCover(Blacklist blacklist) {
			return Flood(PlayerNode, 10000, 100, default, Edges)
				.Where(n => IsCover(n) && !blacklist.Contains(n))
				.Min(DistanceToSelf);
		}

		/// <summary>
		/// Experimental idea. Follow Clearance gradient to cover instead of explicit Flood/FindPath.
		/// Fails if you are in the middle of the street, so you are 15 and so are all neighbors.
		/// </summary>
		public static bool IsPathToCover(NodeHandle node) => Clearance(node) < Clearance(PlayerNode);

	}
	class FindCover : State {
		NodeHandle Target = NodeHandle.Invalid;
		Vector3 Danger;

		static Blacklist blacklist = new Blacklist("Cover");
		public FindCover(Vector3 danger, State next) : base(next) => Danger = danger;
		private State Done() => Next;
		public override State OnTick() {
			if( IsInCover(Self) || IsGoingIntoCover(Self) ) {
				return Done();
			}
			if( Target == NodeHandle.Invalid ) {
				Target = NearbyVehicles().Take(10)
					.Select(v => Handle(FindCoverBehindVehicle(v, Danger)))
					.Without(n => blacklist.Contains(n))
					.Min(DistanceToSelf);
				if( Target == NodeHandle.Invalid ) {
					return Fail;
				}
			} else {
				var pos = Position(Target);
				var fail = Runner("Fail", (state) => {
					blacklist.Add(Target, 10000);
					Log($"FindCover: target {Target} failed, adding to blacklist ({blacklist.Count})");
					Target = NodeHandle.Invalid;
					return this;
				});
				if( DistanceToSelf(pos) < 2f ) {
					return new EnterCover(pos, 2000, this) { Fail = fail };
				} else {
					return new WalkTo(Target, this) { Run = true, Fail = fail };
				}
			}
			return this;
		}
	}
}
