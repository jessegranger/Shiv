using System;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using static Shiv.Global;
using static Shiv.NavMesh;
using static System.Math;

namespace Shiv {

	class FollowPed : PedState {
		public Func<PedHandle> Target;

		public Func<bool> Until;
		public Func<bool> While;

		public FollowPed(PedHandle ped, State next = null) : base(next) => Target = () => ped;
		public FollowPed(Func<PedHandle> func, State next = null) : base(next) => Target = func;

		public override State OnTick() {
			if( While == null || While() ) {
				if( Until == null || !Until()) {
					PedHandle target = Target();
					Vector3 src = Position(Actor);
					Vector3 end = Position(target);
					float dist = Distance(src, end);
					float speed = Speed(Actor);
					UI.DrawHeadline(Actor, $"Speed: {speed}");
					UI.DrawHeadline(target, $"Dist: {dist:F2} Speed: {Speed(target):F2}");
					if( dist > 3f ) {
						return new TaskWalk(end, this) { StoppingRange = 3f, Speed = dist / 3f };
					}
					MoveToward(end, maxSpeed:Max(1f, Speed(target)) );
					if( IsRunning(target) && !IsRunning(Actor) ) {
						ToggleSprint();
					}
					return this;
				}
			}
			return Next;
		}
	}

	class WalkToPed : WalkTo {
		public WalkToPed(PedHandle ped, State next = null) : base(Position(ped), next) => GetPed = () => ped;
		public WalkToPed(Func<PedHandle> ped, State next = null) : base(Position(ped()), next) => GetPed = ped;
		readonly Func<PedHandle> GetPed;

		public override State OnTick() {
			PedHandle ped = GetPed();
			if( ped == PedHandle.Invalid ) {
				return Next;
			}
			StoppingRange = 5f;
			NodeHandle node = Handle(Position(ped));
			if( Target != node ) {
				Target = node;
			}
			return base.OnTick();
		}

	}

	class WalkTo : PedState, IDisposable {
		public float StoppingRange = 1f;
		public uint Timeout = 120000;
		public bool Debug = true;
		public bool Run = false;

		protected PathRequest request;
		protected Path path;
		protected SmoothPath smoothPath;
		protected float steppingRange = .2f;
		private NodeHandle Origin = NodeHandle.Invalid;
		private NodeHandle target = NodeHandle.Invalid;
		protected NodeHandle Target {
			get => target;
			set {
				target = value;
				if( Started ) {
					Restart();
				}
			}
		}
		Stopwatch sw = new Stopwatch();

		private bool Started = false;
		public WalkTo(Vector3 target, State next = null) : this(Handle(PutOnGround(target, 1f)), next) { }
		public WalkTo(NodeHandle end, State next = null) : this(PlayerNode, end, next) { }
		public WalkTo(NodeHandle start, NodeHandle end, State next = null) : base(next) {
			Origin = start;
			Target = end;
		}
		public override State OnTick() {
			if( GamePaused ) {
				return this;
			}
			if( IsRagdoll(Self) || IsFalling(Self) || IsProne(Self) || IsClimbing(Self) ) {
				UI.DrawHeadline("WalkTo: Waiting for handicap...");
				return this;
			}
			if( Target == NodeHandle.Invalid ) {
				return Fail;
			}
			if( ! Started ) {

				Started = true;
				Restart();
				// while we are pathing, also be exiting cover, or exiting vehicle
				if( IsInCover(Self) ) {
					return new ExitCover() { Next = this };
				} else if( PlayerVehicle != VehicleHandle.Invalid ) {
					Origin = Handle(GetVehicleOffset(PlayerVehicle, VehicleOffsets.DriverDoor));
					return new LeaveVehicle() { Next = this };
				}

				// if we dont need to take care of any prep, just idle for at least one frame
				return this;
			}

			if( request != null ) {
				if( request.IsReady() ) {
					path = request.GetResult();
					smoothPath = new SmoothPath(path);
					request = null;
				} else if( path == null ) {
					return request.IsCanceled() ? null
					: request.IsFailed() ? (State)new TaskWalk(Position(target), this) { Fail = Fail } 
					: this;
				}
			}
			
			if( smoothPath != null ) {
				if( smoothPath.IsComplete() ) {
					return Next;
				}
				switch( MoveToward(smoothPath.NextStep(PlayerPosition), debug:true) ) {
					case MoveResult.Failed:
						return Stuck();
					default:
						if( !sw.IsRunning ) {
							sw.Start();
						}
						if( sw.ElapsedMilliseconds > 3000 && Speed(Self) < .02f ) {
							return Stuck();
						}
						UI.DrawHeadline(Actor, $"Walking a smooth path (speed {Speed(Actor):F1})");
						if( Run && !IsRunning(Self) ) {
							ToggleSprint();
						}
						return this;
				}
			}

			if( path != null ) {
				var dist = DistanceToSelf(Target);
				if( Sqrt(dist) < StoppingRange ) {
					return Next;
				}
				path.Draw();

				switch( FollowPath(path, 0.3f) ) {
					case MoveResult.Continue:
						if( !sw.IsRunning ) {
							sw.Start();
						}
						break;
					case MoveResult.Complete:
						path.Pop();
						sw.Restart();
						break;
					case MoveResult.Failed:
						Log("MoveResult.Failed");
						BlockAround(PlayerPosition + Forward(PlayerMatrix));
						return new TaskWalk(Position(target), this) { Fail = this };
				}
			}
			return this;
		}
		protected void BlockAround(Vector3 pos) {
			if( pos != Vector3.Zero ) {
				Flood(Handle(pos), 30, 30, default, Edges)
					.Without(PlayerNode)
					.Where(n => (pos - Position(n)).LengthSquared() < .75f)
					.ToArray() // read it all so the first block doesn't interrupt the Flood
					.Each(Block);
			}
		}
		protected State Stuck() {
			BlockAround(PlayerPosition + Forward(PlayerMatrix));
			return Restart();
		}
		protected State Restart() {
			request?.Cancel();
			path = null;
			smoothPath = null;
			request = new PathRequest(PlayerNode, Target, Timeout, false, true, true, 1);
			sw.Reset();
			return this;
		}

		#region IDisposable Support
		private bool disposed = false; // To detect redundant calls

		protected virtual void Dispose(bool disposing) {
			if( !disposed ) {
				if( disposing ) {
					// TODO: dispose managed state (managed objects).
					if( request != null ) {
						request.Dispose();
					}
				}

				request = null;
				path = null;
				smoothPath = null;

				// TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
				// TODO: set large fields to null.

				disposed = true;
			}
		}

		// TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
		// ~WalkTo() {
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
