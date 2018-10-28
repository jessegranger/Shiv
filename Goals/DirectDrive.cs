using System;
using System.Numerics;
using static Shiv.Globals;
using static System.Math;

namespace Shiv.Goals {
	public class DirectDrive : Goal {
		public Func<Vector3> Target;
		public float MaxSpeed = 10f;
		public float StoppingRange = 10f;
		public DirectDrive(Vector3 target) {
			Target = () => target;
			if( target == Vector3.Zero )
				Status = GoalStatus.Failed;
		}
		public DirectDrive(Func<Vector3> func) {
			Target = func;
		}
		private float Activation(float x) => (Sigmoid(x) * 2f) - 1f;
		public override GoalStatus OnTick() {
			var target = Target();
			if( target != Vector3.Zero ) {
				var v = CurrentVehicle(Self);
				if( v == VehicleHandle.Invalid ) {
					return Status = GoalStatus.Failed;
				}
				var vm = Matrix(v);
				var pv = Position(vm);
				var delta = target - pv;
				var delta_len = delta.Length();
				if( delta_len < StoppingRange ) {
					return Status = GoalStatus.Complete;
				}
				delta /= delta_len; // Normalize, but we already had to compute len before
				var right = Vector3.Dot(delta, Right(vm));
				var forward = Vector3.Dot(delta, Forward(vm));
				var angle = Rad2Deg(Atan2(right, forward));
				right = Activation(20 * right);
				float accel = 0f, brake = 0f;
				if( Speed(v) < MaxSpeed ) {
					var abs_angle = Abs(angle);
					if( abs_angle < 50 ) {
						accel = Abs(forward);
					} else if( abs_angle > 160 ) {
						brake = Abs(forward);
					} else {
						brake = 1f;
						right = angle < 0 ? 1f : -1f;
					}
				}
				SetControlValue(1, Control.VehicleAccelerate, accel);
				SetControlValue(1, Control.VehicleBrake, brake);
				SetControlValue(1, Control.VehicleMoveLeftRight, right);
			}
			return Status;
		}
	}
}
