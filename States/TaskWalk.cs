using System;
using System.Drawing;
using System.Linq;
using System.Numerics;
using static GTA.Native.Hash;
using static GTA.Native.Function;
using static Shiv.Global;
using static Shiv.NavMesh;
using StateMachine;

namespace Shiv {

	class TaskWalk : PedState {
		public Vector3 Target;
		public float StoppingRange = 2f;
		public float Speed = 1f;
		public int Timeout = -1;
		public bool PersistFollowing = false;
		private uint Started = 0;
		private float DistanceRemaining;
		public override string Name => Speed < 2f ? "Walk" : "Run";
		public TaskWalk(Vector3 target, State next = null) : base(next) => Target = target;
		private State Done() {
			TaskClearAll();
			Call(REMOVE_NAVMESH_REQUIRED_REGIONS);
			return Next;
		}
		private State Start() {
			if( Started == 0 ) {
				Started = GameTime;
				TaskClearAll();
				Call(TASK_FOLLOW_NAV_MESH_TO_COORD, Actor, Target,
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
			UI.DrawHeadline($"Waiting: {msg}");
			return this;
		}
		private long blockHandle = -1;
		private void AddBlockingBox(Vector3 pos) {
			blockHandle = Call<int>(ADD_NAVMESH_BLOCKING_OBJECT, pos, 2f, 2f, 2f, Call<int>(GET_ENTITY_HEADING, Actor), 0, 7);
			Sphere.Add(pos, .2f, Color.Red, 10000);
		}
		public State Restart() {
			return new TaskWalk(Target, Next) {
				StoppingRange = StoppingRange,
				Speed = Speed,
				Timeout = Timeout,
				PersistFollowing = PersistFollowing,
				Fail = Fail
			};
		}
		public override State OnTick() {
			if( Target == Vector3.Zero ) {
				return Done();
			}

			Vector3 ActorPosition = Position(Actor);
			DistanceRemaining = (Target - ActorPosition).Length();

			if( DistanceRemaining < StoppingRange ) {
				return Done();
			}

			if( Started == 0 ) {
				return Start();
			}

			if( (GameTime - Started) > 2500 && Velocity(Actor).Length() == 0 ) {
				return Done();
			}
			if( !Call<bool>(IS_NAVMESH_LOADED_IN_AREA, ActorPosition, Target) ) {
				Call(ADD_NAVMESH_REQUIRED_REGION, ActorPosition.X, ActorPosition.Y, DistanceRemaining);
				Call(ADD_NAVMESH_REQUIRED_REGION, Target.X, Target.Y, DistanceRemaining);
				return Wait("IS_NAVMESH_LOADED_IN_AREA is false");
			}
			int status = GetScriptTaskStatus(Actor, TaskStatusHash.TASK_FOLLOW_NAVMESH_TO_COORD);
			UI.DrawText($"TaskWalk: (started {Started}) (dist {DistanceRemaining}) (status {status}) (blocked {blockHandle})");
			switch( status ) {
				case 1:
					return this;
				case 2:
					AddBlockingBox(ActorPosition);
					return Restart();
				case 7:
					return Done();
			}
			return this;
		}
	}

}
