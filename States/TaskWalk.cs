using System;
using System.Drawing;
using System.Linq;
using System.Numerics;
using static GTA.Native.Hash;
using static GTA.Native.Function;
using static Shiv.Global;
using static Shiv.NavMesh;
using System.Threading.Tasks;
using System.Collections.Generic;
using static System.Math;

namespace Shiv {
	public static partial class Global {


	}
	/*
	class WalkTo : Goal {
		public NodeHandle TargetNode;
		public float StoppingRange = 2f;
		private uint Started = 0;
		private IFuture<Path> future = null;
		public static bool Debug = true;
		public WalkTo(NodeHandle target) => TargetNode = target;
		public WalkTo(Vector3 target) => TargetNode = GetHandle(PutOnGround(target, 1f));
		public override GoalStatus OnTick() {
			if( !CanControlCharacter() ) {
				return Status = GoalStatus.Complete;
			}

			var dist = DistanceToSelf(TargetNode);
			if( dist < StoppingRange ) {
				return Status = GoalStatus.Complete;
			}

			if( future == null ) {
				Log("WalkTo: Requesting path...");
				future = new PathRequest(PlayerNode, TargetNode, 120000, false, true, true);
			} else if( future.IsFailed() ) {
				Log($"WalkTo: Task failed (err={future.GetError()})");
				return Status = GoalStatus.Failed;
			} else if( future.IsReady() ) {
				var slice = future.GetResult().Take(6);
				if( slice.Count() == 0 ) {
					Log("WalkTo: Got an empty path.");
					return Status = GoalStatus.Failed;
				}
				Vector3[] steps = slice.Select(Position).ToArray();
				foreach( var v in steps ) {
					DrawSphere(v, .06f, Color.Yellow);
				}
				if( DistanceToSelf(steps[0]) < .3f ) {
					future = new Immediate<Path>(new Path(future.GetResult().Skip(1)));
					return GoalStatus.Active;
				}
				DrawSphere(Bezier(.1f, steps), .02f, Color.Orange);
				DrawSphere(Bezier(.3f, steps), .02f, Color.Orange);
				DrawSphere(Bezier(.5f, steps), .02f, Color.Orange);
				DrawSphere(Bezier(.7f, steps), .02f, Color.Orange);
				DrawSphere(Bezier(.9f, steps), .02f, Color.Orange);
				MoveResult result;
				var step = Bezier(.05f, steps);
				switch( result = MoveToward(step, .01f) ) {
					case MoveResult.Complete:
						future = new Immediate<Path>(new Path(future.GetResult().Skip(1)));
						break;
					case MoveResult.Continue:
						if( Started == 0 ) {
							Started = GameTime;
						} else if( GameTime - Started > 800 ) {

							var vel = Vector3.Normalize(Velocity(Self));
							var expected = Vector3.Normalize(step - PlayerPosition);
							var overlap = Vector3.Dot(vel, expected);
							UI.DrawTextInWorld(step, $"Overlap: {overlap}");
							if( overlap <= 0 && !IsClimbing(Self) && !IsJumping(Self) && !Call<bool>(IS_PED_RAGDOLL, Self) ) {
								Log("STUCK");
								Stuck();
							}
						}
						break;
					case MoveResult.Failed:
						Stuck();
						break;
					default:
						Log("WalkTo: MoveToward failed.");
						return Status = GoalStatus.Failed;
				}
				UI.DrawTextInWorldWithOffset(HeadPosition(Self), 0f, .02f, $"{result}");
			} else {
				UI.DrawTextInWorld(HeadPosition(Self) + (Up * .1f), "Recalculating...");
			}
			return Status;
		}
		private void Restart() {
			future = null;
			Started = 0;
		}
		private void Stuck() {
			NodeHandle cur = PlayerNode;
			Log($"Stuck! {PlayerNode}");
			Flood(future.GetResult().Skip(1).FirstOrDefault(), 10, 1, default, Edges).Without(PlayerNode).Each(Block);
			Restart();
		}
	}
	*/

	class TaskWalk : State {
		public Vector3 Target;
		public float StoppingRange = 2f;
		public float Speed = 1f;
		public int Timeout = -1;
		public bool PersistFollowing = false;
		private uint Started = 0;
		private float DistanceRemaining;
		public override string Name => Speed < 2f ? "Walk" : "Run";
		public TaskWalk(Vector3 target, State next):base(next) => Target = target;
		private State Done() {
			TaskClearAll();
			Call(REMOVE_NAVMESH_REQUIRED_REGIONS);
			return Next;
		}
		private State Start() {
			if( Started == 0 ) {
				Started = GameTime;
				TaskClearAll();
				Call(TASK_FOLLOW_NAV_MESH_TO_COORD, Self, Target,
					Speed, // speed
					Timeout, // timeout
					StoppingRange, // stopping range
					PersistFollowing, // persist
					0f // unknown float
				);
			}
			return this;
		}
		public State Wait(string msg) {
			UI.DrawText(.5f, .5f, $"Waiting: {msg}");
			return this;
		}
		private long blockHandle = -1;
		private void AddBlockingBox() {
			blockHandle = Call<int>(ADD_NAVMESH_BLOCKING_OBJECT, PlayerPosition, 2f, 2f, 2f, Call<int>(GET_ENTITY_HEADING, Self), 0, 7);
			Sphere.Add(PlayerPosition, .2f, Color.Red, 10000);
		}
		public State Restart() {
			return new TaskWalk(Target, Next) {
				StoppingRange = StoppingRange,
				Speed = Speed,
				Timeout = Timeout,
				PersistFollowing = PersistFollowing
			};
		}
		public override State OnTick() {
			if( Target == Vector3.Zero ) {
				return Done();
			}

			DistanceRemaining = DistanceToSelf(Target);
			if( DistanceRemaining < StoppingRange ) {
				return Done();
			}

			if( Started == 0 ) {
				return Start();
			}

			if( (GameTime - Started) > 500 && Velocity(Self).Length() == 0 ) {
				// Goals.Immediate(new WalkTo(Target));
				return Done();
			}
			if( !Call<bool>(IS_NAVMESH_LOADED_IN_AREA, PlayerPosition, Target) ) {
				Call(ADD_NAVMESH_REQUIRED_REGION, PlayerPosition.X, PlayerPosition.Y, DistanceRemaining);
				Call(ADD_NAVMESH_REQUIRED_REGION, Target.X, Target.Y, DistanceRemaining);
				return Wait("IS_NAVMESH_LOADED_IN_AREA is false");
			}
			int status = GetScriptTaskStatus(Self, TaskStatusHash.TASK_FOLLOW_NAVMESH_TO_COORD);
			UI.DrawText($"TaskWalk: (started {Started}) (dist {DistanceRemaining}) (status {status}) (blocked {blockHandle})");
			switch( status ) {
				case 1:
					break;
				case 2:
					AddBlockingBox();
					return Restart();
				case 7:
					return Done();
			}
			return this;
		}
	}

}
