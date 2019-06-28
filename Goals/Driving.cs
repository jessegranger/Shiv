using System;
using System.Numerics;
using static Shiv.Global;
using static System.Math;
using static GTA.Native.Hash;
using static GTA.Native.Function;
using System.Linq;

namespace Shiv {
	public static partial class Global {

		// void SET_DRIVER_RACING_MODIFIER(Ped driver, float racingModifier) // DED5AF5A0EA4B297 6D55B3B3
		// void SET_DRIVER_ABILITY(Ped driver, float ability) // B195FFA8042FC5C3 AAD4012C
		// void SET_DRIVER_AGGRESSIVENESS(Ped driver, float aggressiveness) // A731F608CA104E3C 8B02A8FB 
		// void TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE(Ped ped, Vehicle vehicle, float x, float y, float z, float speed, int driveMode, float stopRange) // 158BB33F920D360C 1490182A
		// BOOL IS_VEHICLE_DRIVEABLE(Vehicle vehicle, BOOL isOnFireCheck) // 4C241E39B23DF959 41A7267A 
		// void TASK_VEHICLE_DRIVE_TO_COORD(Ped ped, Vehicle vehicle, float x, float y, float z, float speed, Any p6, Hash vehicleModel, int drivingMode, float stopRange, float p10) // E2A2AA2F659D77A7 E4AC0387
		// void TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE(Ped ped, Vehicle vehicle, float x, float y, float z, float speed, int driveMode, float stopRange) // 158BB33F920D360C 1490182A
		// void TASK_VEHICLE_DRIVE_WANDER(Ped ped, Vehicle vehicle, float speed, int drivingStyle) // 480142959D337D00 36EC0EB0

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
				float speed = Speed(v);
				if( speed < maxSpeed ) {
					var abs_angle = Abs(angle);
					if( abs_angle < 50 ) {
						accel = Abs(forward);
					} else if( abs_angle > 160 ) {
						brake = Abs(forward);
					} else {
						brake = 1f;
						right = angle < 0 ? 1f : -1f;
					}
				} else if( speed > maxSpeed * 1.1 ) {
					brake = 1f;
				}
				SetControlValue(1, Control.VehicleAccelerate, accel);
				SetControlValue(1, Control.VehicleBrake, brake);
				SetControlValue(1, Control.VehicleMoveLeftRight, right);
			}
			return false;
		}
	}

	public class FollowCar : Goal {
		public Func<VehicleHandle> Target;
		public Func<bool> Until;
		public float Distance = 10f;
		public FollowCar(Func<VehicleHandle> target, Func<bool> until) { Target = target; Until = until; }
		public FollowCar(VehicleHandle target) : this(() => target, () => false) { }
		public override GoalStatus OnTick() {
			if( CanControlCharacter() ) {
				VehicleHandle target = Target();
				if( target == VehicleHandle.Invalid ) {
					return Status = GoalStatus.Complete;
				}

				Vector3 targetPos = Position(target);
				if( targetPos == Vector3.Zero ) {
					return Status = GoalStatus.Complete;
				}

				Vector3 pos = Position(PlayerVehicle);
				if( pos == Vector3.Zero ) {
					return Status = GoalStatus.Complete;
				}

				DriveToward(targetPos, maxSpeed: Speed(target) * ((targetPos - pos).Length() / Distance));
			}
			return Status;
		}
	}

	public class TaskDrive : Goal {
		public Func<Vector3> Target;
		public VehicleDrivingFlags DrivingFlags = (VehicleDrivingFlags)DrivingStyle.Normal;
		public float StoppingRange = 4f;
		public float Speed = 1f;
		private uint Started = 0;
		public TaskDrive(Func<Vector3> target) => Target = target;
		public TaskDrive(Vector3 target) : this(() => target) { }

		private GoalStatus Start() {
			if( Started == 0 ) {
				Started = GameTime;
				TaskClearAll();
				Call(SET_DRIVER_RACING_MODIFIER, Self, 1f);
				Call(SET_DRIVER_ABILITY, Self, 1f);
				Call(SET_DRIVER_AGGRESSIVENESS, Self, .5f);
				Call(TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE,
					Self, PlayerVehicle, Target,
					Speed,
					DrivingFlags,
					StoppingRange
				);
			}
			return Status;
		}
		public GoalStatus Done() => Status = GoalStatus.Complete;
		public override GoalStatus OnTick() {
			int status = GetScriptTaskStatus(Self, TaskStatusHash.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE);
			UI.DrawText($"TaskDrive: (started {Started}) (status {status})");
			switch( status ) {
				case 1: return Status;
				// TODO: case 2: AddBlockingBox(); return Restart();
				case 7: return Done();
			}
			return Status;
		}
	}

	public class DirectDrive : Goal {
		public Func<Vector3> Target;
		public float MaxSpeed = 10f;
		public float StoppingRange = 10f;
		public bool StopAtDestination = true;
		public DirectDrive(Vector3 target) {
			Target = () => target;
			if( target == Vector3.Zero ) {
				Status = GoalStatus.Failed;
			}
		}
		public DirectDrive(Func<Vector3> func) => Target = func;
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

		public override void Dispose() => blacklist.Dispose();

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
			Goals.Immediate(new DirectDrive(target) {
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
