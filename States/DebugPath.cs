using System;
using System.Drawing;
using System.Linq;
using System.Numerics;
using static Shiv.Global;
using static Shiv.NavMesh;

namespace Shiv {
	class DebugPath : State {

		public bool AvoidObjects = true;
		public bool AvoidPeds = false;
		public bool AvoidCars = false;
		public uint Clearance = 1;

		public bool GrowIfNeeded = false;

		public NodeHandle TargetNode { get; private set; }

		private PathRequest req;
		private Path path;

		public DebugPath(Vector3 v) : this(Handle(PutOnGround(v, 1f))) { }
		public DebugPath(NodeHandle targetNode) {
			TargetNode = targetNode;
			if( !IsGrown(TargetNode) && GrowIfNeeded ) {
				Grow(TargetNode, 5);
			}
			Restart();
		}
		private readonly Random random = new Random();
		private void Restart() => req = new PathRequest(PlayerNode, TargetNode, 30000, AvoidPeds, AvoidCars, AvoidObjects, Clearance);
		public override State OnTick() {
			if( TargetNode == NodeHandle.Invalid ) {
				return Fail;
			}
			if( req.IsReady() ) {
				path = req.GetResult();
				Restart();
			}
			if( path != null ) {
				while( DistanceToSelf(path.FirstOrDefault()) < 1f ) {
					path.Pop();
				}
				path.Draw();
			}
			if( req.IsFailed() ) {
				return this;
			}
			if( req.Blocked != null ) {
				req.Blocked
					.Select(Position)
					.OrderBy(DistanceToSelf)
					.Take(100)
					.Each(DrawSphere(.02f, Color.Red));
			}
			return this;
		}

	}
}
