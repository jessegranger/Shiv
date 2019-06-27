using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using static Shiv.Global;

namespace Shiv {
	class TakeCover : Goal {
		public Vector3 Target;
		public TakeCover(Vector3 target) {
			Target = Position(GetHandle(PutOnGround(target, 1f)));
		}
		public TakeCover():this(Position(NavMesh.FindClosestCover(PlayerNode, Position(DangerSense.NearbyDanger[0])))) { }
		enum States {
			Approach,
			Enter,
			Blacklist
		}
		private States State = States.Approach;

		private GoalStatus NextState(States next) {
			State = next;
			return Status;
		}
		private uint enterStarted = 0;
		private Random random = new Random();
		public override GoalStatus OnTick() {
			if( Target == Vector3.Zero )
				return Status = GoalStatus.Failed;
			if( IsInCover(Self) || IsGoingIntoCover(Self) )
				return Status = GoalStatus.Complete;
			var dist = DistanceToSelf(Target);
			switch( State ) {
				case States.Approach:
					if( dist < 3f ) return NextState(States.Enter);
					Goals.Immediate(new TaskWalk(Target) { Speed = 2f });
					break;
				case States.Enter:
					if( enterStarted == 0 ) enterStarted = GameTime;
					if( GameTime - enterStarted > 3000 ) return NextState(States.Blacklist);
					LookToward(Target + new Vector3((float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble()));
					Goals.Immediate(new KeyGoal(1, Control.Cover, 1f, 150));
					break;
				case States.Blacklist:
					NodeHandle node = GetHandle(Target);
					NavMesh.IsCover(node, false);
					Target = Position(NavMesh.FindClosestCover(node, Position(DangerSense.NearbyDanger[0])));
					break;
			}
			return Status;
		}
	}
}
