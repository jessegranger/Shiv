using System;
using System.Drawing;
using System.Numerics;
using static Shiv.Globals;
using static System.Math;

namespace Shiv {
	public class DirectMove : Goal {
		public Func<Vector3> Target;
		public float StoppingRange = 2f;
		public bool Run = false;
		public DirectMove(Vector3 pos) {
			Target = () => pos;
			if( pos == Vector3.Zero )
				Status = GoalStatus.Failed;
		}
		public DirectMove(Func<Vector3> pos) {
			Target = pos;
		}
		public virtual bool CheckComplete(Vector3 target) => DistanceToSelf(target) < StoppingRange * StoppingRange;
		public override GoalStatus OnTick() {
			var target = Target();
			if( target != Vector3.Zero ) {
				DrawSphere(target, .1f, Color.Blue);
				DrawLine(Position(PlayerMatrix), target, Color.Orange);
				if( CheckComplete(target) ) {
					return Status = GoalStatus.Complete;
				}
				MoveToward(target);
				var obs = CheckObstruction(PlayerPosition, (target - PlayerPosition));
				if( obs < 0 ) return Status = GoalStatus.Failed; // Impassable
				else if( obs > .25f && obs < 2.4f && Speed(Self) < .01f ) {
					SetControlValue(0, Control.Jump, 1.0f); // Attempt to jump over a positive obstruction
					if( CurrentVehicle(Self) != 0 ) return Status = GoalStatus.Failed;
				} else if( Run && ! IsRunning(Self) ) ToggleSprint();
			}
			return Status;
		}

		const IntersectOptions opts = IntersectOptions.Map | IntersectOptions.MissionEntities | IntersectOptions.Objects
			| IntersectOptions.Unk1
			| IntersectOptions.Unk2
			| IntersectOptions.Unk3
			| IntersectOptions.Unk4
			;
		public static float CheckObstruction(Vector3 pos, Vector3 forward) {
			forward = Vector3.Normalize(forward) * .6f;
			pos = pos + forward + (Up * 2.3f); // pick a spot in the air
			var end = new Vector3(pos.X, pos.Y, pos.Z - 2.9f); // try to drop it down
			DrawLine(pos, end, Color.Yellow);
			DrawLine(HeadPosition(Self), end, Color.Orange);
			var result = Raycast(pos, end, .3f, opts, Self);
			if( result.DidHit ) {
				DrawSphere(result.HitPosition, .2f, Color.Orange);
			}
			return result.DidHit ? (result.HitPosition - end).Length() : 0f;
		}
	}

}
