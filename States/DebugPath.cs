using System;
using System.Drawing;
using System.Linq;
using System.Numerics;
using static Shiv.Global;
using static Shiv.NavMesh;

namespace Shiv {
	class DebugPath : State, IDisposable {

		public bool AvoidObjects = true;
		public bool AvoidPeds = false;
		public bool AvoidCars = false;
		public uint Clearance = 1;

		public bool GrowIfNeeded = false;
		private bool Started = false;

		public NodeHandle TargetNode { get; private set; }

		private PathRequest req;
		private Path path;

		public DebugPath(Vector3 v) : this(Handle(PutOnGround(v, 1f))) { }
		public DebugPath(NodeHandle targetNode) => TargetNode = targetNode;
		private void Restart() => req = new PathRequest(PlayerNode, TargetNode, 3000, AvoidPeds, AvoidCars, AvoidObjects, Clearance);
		public override State OnTick() {
			if( TargetNode == NodeHandle.Invalid ) {
				return Fail;
			}
			if( ! Started ) {
				Started = true;
				if( !IsGrown(TargetNode) && GrowIfNeeded ) {
					Grow(TargetNode, 5);
				}
				Restart();
				return this;
			}
			if( req.IsReady() ) {
				path = req.GetResult();
				Restart();
			}
			if( path != null ) {
				while( DistanceToSelf(path.FirstOrDefault()) < .6f ) {
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
					.Take(50)
					.Each(DrawSphere(.02f, Color.Red));
			}
			return this;
		}

		#region IDisposable Support
		private bool disposed = false; // To detect redundant calls

		protected virtual void Dispose(bool disposing) {
			if( !disposed ) {
				if( disposing ) {
					// TODO: dispose managed state (managed objects).
					req.Dispose();
				}

				// TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
				// TODO: set large fields to null.

				disposed = true;
			}
		}

		// TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
		// ~DebugPath() {
		//   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
		//   Dispose(false);
		// }

		// This code added to correctly implement the disposable pattern.
		public void Dispose() {
			// Do not change this code. Put cleanup code in Dispose(bool disposing) above.
			Dispose(true);
			// TODO: uncomment the following line if the finalizer is overridden above.
			// GC.SuppressFinalize(this);
		}
		#endregion

	}
}
