using System.Linq;
using System.Numerics;
using static GTA.Native.Hash;
using static GTA.Native.Function;
using static Shiv.Global;
using static Shiv.NavMesh;
using static System.Math;
using System.Diagnostics;
using System;

namespace Shiv {

	class WalkToPed : WalkTo {

		public WalkToPed(PedHandle ped, State next = null) : base(Position(ped), next) => GetPed = () => ped;
		public WalkToPed(Func<PedHandle> ped, State next = null) : base(Position(ped()), next) => GetPed = ped;
		readonly Func<PedHandle> GetPed;

		public override State OnTick() {
			var ped = GetPed();
			if( ped == PedHandle.Invalid ) {
				return Next;
			}
			StoppingRange = 5f;
			var node = Handle(Position(GetPed()));
			if( Target != node ) {
				Target = node;
			}
			return base.OnTick();
		}

	}

	class WalkTo : State {
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
			if( Target == NodeHandle.Invalid ) {
				return Fail;
			}
			if( ! Started ) {

				Started = true;
				Restart();
				// while we are pathing, also be exiting cover, or exiting vehicle
				if( IsInCover(Self) ) {
					AddStateOnce(Self, new ExitCover());
				} else if( PlayerVehicle != VehicleHandle.Invalid ) {
					Origin = Handle(GetVehicleOffset(PlayerVehicle, VehicleOffsets.DriverDoor));
					return new LeaveVehicle(this);
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
					: request.IsFailed() ? (State)new TaskWalk(Position(target)) { Fail = Fail } 
					: this;
				}
			}
			
			if( smoothPath != null ) {
				var step = smoothPath.NextStep(PlayerPosition);
				if( smoothPath.IsComplete() ) {
					return Next;
				}
				switch( MoveToward(step, debug:true) ) {
					case MoveResult.Failed: // smooth path currently fails on chain link fences
						smoothPath = null; // fall back to the full path
						break;
					default:
						UI.DrawHeadline(Actor, $"Walking a smooth path (speed={Speed(Actor)}.");
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
						// if( Run && !IsRunning(Self) ) { ToggleSprint(); }
						/* disable stuck detection until we are using SmoothPath
						Vector3 step = Position(path.First());
						var vel = Vector3.Normalize(Velocity(Self));
						var expected = Vector3.Normalize(step - PlayerPosition);
						var overlap = Vector3.Dot(vel, expected);
						// UI.DrawTextInWorld(step, $"Overlap: {overlap}");
						if( sw.ElapsedMilliseconds > 3200 && overlap <= 0.01f && !IsClimbing(Self) && !IsJumping(Self) && !IsRagdoll(Self) && Speed(Self) < .02f ) {
							Log($"STUCK at {sw.ElapsedMilliseconds} overlap {overlap}");
							Stuck();
						}
						*/
						break;
					case MoveResult.Complete:
						path.Pop();
						sw.Restart();
						break;
					case MoveResult.Failed:
						Log("MoveResult.Failed");
						Stuck();
						return new TaskWalk(Position(target), this) { Fail = this };
				}
			}
			return this;
		}
		protected void Stuck() {
			if( path != null ) {
				var stuck = path.Skip(1).FirstOrDefault();
				var stuckPos = Position(stuck);
				Flood(stuck, 30, 30, default, Edges)
					.Without(PlayerNode)
					.Where(n => (stuckPos - Position(n)).LengthSquared() < .75f )
					.ToArray() // read it all so the first block doesn't interrupt the Flood
					.Each(Block);
				path = null;
				smoothPath = null;
			}
			Restart();
		}
		protected void Restart() {
			request?.Cancel();
			request = new PathRequest(Origin, Target, Timeout, false, true, true, 1);
			sw.Reset();
		}
	}
}
