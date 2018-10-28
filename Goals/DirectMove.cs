using System;
using System.Drawing;
using System.Numerics;
using static Shiv.Globals;
using static System.Math;

namespace Shiv {
	public class DirectMove : Goal {
		public Func<Vector3> Target;
		public float StoppingRange = 2f;
		public DirectMove(Func<Vector3> pos) {
			Target = pos;
		}
		public virtual bool CheckComplete(Vector3 target) {
			float dist = DistanceToSelf(target);
			return dist < StoppingRange * StoppingRange;
		}
		private float Sigmoid(float x) => (float)(1 / (1 + Pow(E, -x)));
		private float Activation(float x, float fps) => (Sigmoid(2 * x) * 4f) - 2f;
		private readonly float aspect_ratio = 2f; // TODO: get the real aspect ratio
		public override GoalStatus OnTick() {
			var target = Target();
			if( target != Vector3.Zero ) {
				DrawSphere(target, .1f, Color.Blue);
				DrawLine(Position(PlayerMatrix), target, Color.Orange);
				var delta = target - Position(PlayerMatrix);
				float dist = delta.Length();
				if( CheckComplete(target) ) {
					return Status = GoalStatus.Complete;
				}
				var forward = -Vector3.Dot(delta, Forward(CameraMatrix));
				var right = Vector3.Dot(delta, Right(CameraMatrix));
				forward = Activation(forward / aspect_ratio, CurrentFPS);
				right = Activation(right, CurrentFPS);
				// UI.DrawTextInWorld(PlayerPosition, $"FWD:{forward:F2} RGT:{right:F2}");
				SetControlValue(1, Control.MoveLeftRight, right);
				SetControlValue(1, Control.MoveUpDown, forward);
				var obs = CheckObstruction(PlayerPosition, delta);
				if( obs > .25f && obs < 1.5f && CurrentVehicle(Self) == 0 ) {
					PressControl(0, Globals.Control.Jump, 200);
				}
			}
			return Status;
		}

		static IntersectOptions opts = IntersectOptions.Map | IntersectOptions.MissionEntities | IntersectOptions.Objects;
		public static float CheckObstruction(Vector3 pos, Vector3 forward) {
			forward = Vector3.Normalize(forward) * .5f;
			pos = pos + forward + Up; // pick a spot in the air
			var end = new Vector3(pos.X, pos.Y, pos.Z - 1.6f); // try to drop it down
			var result = Raycast(pos, end, .2f, opts, Self);
			// if( result.DidHit ) {
				// DrawLine(pos, result.HitPosition, Color.Yellow);
				// DrawSphere(result.HitPosition, .1f, Color.Yellow);
			// }
			return result.DidHit ? (result.HitPosition - end).Length() : 0f;
		}
	}

}
