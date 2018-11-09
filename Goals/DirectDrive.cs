using System;
using System.Numerics;
using static Shiv.Globals;
using static System.Math;
using System.Linq;

namespace Shiv {
	public static partial class Globals {

		private static float DriveActivation(float x) => (Sigmoid(x) * 2f) - 1f;
		public static bool DriveToward(Vector3 target, float maxSpeed=10f, float stoppingRange=2f, bool stopAtEnd=false) {
			if( target != Vector3.Zero ) {
				var v = CurrentVehicle(Self);
				if( v == VehicleHandle.Invalid ) {
					return false;
				}
				var vm = Matrix(v);
				var pv = Position(vm);
				var fv = Forward(vm);
				var delta = target - pv;
				var delta_len = delta.Length();
				var cur_speed = Speed(v);
				if( delta_len < stoppingRange ) {
					if( stopAtEnd && cur_speed > .01f ) {
						if( Vector3.Dot(Velocity(v), fv) > 0f ) {
							SetControlValue(1, Control.VehicleBrake, 1.0f);
						}
					}
					return true;
				}
				delta /= delta_len; // Normalize, but we already had to compute len before
				var right = Vector3.Dot(delta, Right(vm));
				var forward = Vector3.Dot(delta, fv);
				var angle = Rad2Deg(Atan2(right, forward));
				right = DriveActivation(10 * right);
				float accel = 0f, brake = 0f;
				if( Speed(v) < maxSpeed ) {
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
			return false;
		}
	}
	public class DirectDrive : Goal {
		public Func<Vector3> Target;
		public float MaxSpeed = 10f;
		public float StoppingRange = 10f;
		public bool StopAtDestination = true;
		public DirectDrive(Vector3 target) {
			Target = () => target;
			if( target == Vector3.Zero )
				Status = GoalStatus.Failed;
		}
		public DirectDrive(Func<Vector3> func) {
			Target = func;
		}
		private Vector3 lastTarget;
		private float Activation(float x) => (Sigmoid(x) * 2f) - 1f;
		public override GoalStatus OnTick() {
			var target = Target();
			if( target != Vector3.Zero ) {
				var v = CurrentVehicle(Self);
				if( v == VehicleHandle.Invalid ) {
					return Status = GoalStatus.Failed;
				}
				lastTarget = target;
				if( DriveToward(target, MaxSpeed, StoppingRange, StopAtDestination) ) {
					return Status = GoalStatus.Complete;
				}
			}
			return Status;
		}
		public override string ToString() => lastTarget == Vector3.Zero ? "DriveTo" 
			: $"DriveTo({DistanceToSelf(lastTarget):F0}m)";
	}

	public class DriveWander : Goal {
		public DriveWander() { }

		public override void Dispose() { blacklist.Dispose(); }

		private readonly Blacklist blacklist = new Blacklist("DriveWander");
		public override GoalStatus OnTick() {
			VehicleNodeData node = GetNextNode();
			blacklist.Add(node.GetHashCode(), 30000);
			float h = Deg2Rad(node.Heading);
			Vector3 target = node.Position;
			target = node.Position+ new Vector3(
				(float)(node.Kind) * (float)Math.Cos(h),
				(float)(node.Kind) * (float)Math.Sin(h),
				.5f
			);
			Goals.Push(new DirectDrive(target) {
				StoppingRange = 10f,
				StopAtDestination = false
			});
			return Status;
		}

		private VehicleNodeData GetNextNode() {
			return GetClosestVehicleNodes(PlayerPosition, RoadType.Road)
				.Where(n => 
					(!blacklist.Contains(n.GetHashCode()))
					&& IsFacing(Self, n.Position)
					&& DistanceToSelf(n.Position) > 25f
					&& CanSee(Self, n.Position)
				).FirstOrDefault();
		}
		public override string ToString() => "Wander";
	}
}
