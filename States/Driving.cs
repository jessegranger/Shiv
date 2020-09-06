using System;
using System.Numerics;
using static Shiv.Global;
using static System.Math;
using static GTA.Native.Hash;
using static GTA.Native.Function;
using StateMachine;

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

		private static float SteerActivation(float x) => (Sigmoid(10f*x) * 2f) - 1f;
		public static MoveResult SteerToward(Vector3 target, float maxSpeed=10f, float stoppingRange=2f, bool stopAtEnd=false, bool debug=false) {
			if( target != Vector3.Zero ) {
				var v = CurrentVehicle(Self);
				if( v == VehicleHandle.Invalid ) {
					return MoveResult.Failed;
				}
				var model = GetModel(v);
				var vm = Matrix(v);
				var pv = Position(vm);
				var forward = Forward(vm);
				var desired_forward = target - pv;
				var desired_forward_len = desired_forward.Length();
				var cur_speed = Speed(v);
				if( desired_forward_len < stoppingRange ) {
					if( stopAtEnd && cur_speed > .01f ) {
						if( Vector3.Dot(Velocity(v), forward) > 0f ) {
							SetControlValue(1, Control.VehicleBrake, 1.0f);
						}
						return MoveResult.Continue;
					}
					return MoveResult.Complete;
				}
				desired_forward /= desired_forward_len; // Normalize, but we already had to compute len before
				float turn_right = Vector3.Dot(desired_forward, Right(vm));
				float push_forward = Vector3.Dot(desired_forward, forward);
				float angle = Rad2Deg(Atan2(turn_right, push_forward));
				turn_right = SteerActivation(turn_right);
				float accel = 0f, brake = 0f;
				float speed = Speed(v);
				if( stopAtEnd ) {
					// scale the max speed down toward zero as we get close
					maxSpeed = Math.Min(desired_forward_len*1.2f, maxSpeed);
				}
				if( speed < maxSpeed ) {
					var abs_angle = Abs(angle);
					if( abs_angle < 60 ) {
						accel = Abs(push_forward);
					} else if( abs_angle > 160 && desired_forward_len < 10f ) {
						brake = Abs(push_forward);
					} else {
						brake = 1f;
						turn_right = angle < 0 ? 1f : -1f;
					}
				} else if( speed > maxSpeed * 1.1 ) {
					brake = 1f;
				}
				if( IsBicycle((ModelHash)model) ) {
					SetControlValue(1, Control.VehiclePushbikePedal, accel);
					SetControlValue(1, Control.VehiclePushbikeRearBrake, brake);
				} else {
					SetControlValue(1, Control.VehicleAccelerate, accel);
					SetControlValue(1, Control.VehicleBrake, brake);
				}
				SetControlValue(1, Control.VehicleMoveLeftRight, turn_right);
				if( debug ) {
					UI.DrawText(.5f, .4f, $"Steer: angle {angle:F2} right {turn_right:F2} accel {accel:F2} brake {brake:F2}");
				}
				return MoveResult.Continue;
			}
			return MoveResult.Failed;
		}
	}

	public class FollowCar : State {
		public Func<VehicleHandle> Target;
		public Func<bool> Until;
		public float Distance = 20f;
		public FollowCar(Func<VehicleHandle> target, Func<bool> until) { Target = target; Until = until; }
		public FollowCar(VehicleHandle target) : this(() => target, () => false) { }
		public override State OnTick() {
			if( CanControlCharacter() ) {
				VehicleHandle target = Target();
				if( target == VehicleHandle.Invalid ) {
					return Fail;
				}

				Vector3 targetPos = Position(target);
				if( targetPos == Vector3.Zero ) {
					return Fail;
				}

				Vector3 pos = Position(PlayerVehicle);
				if( pos == Vector3.Zero ) {
					return Fail;
				}

				float maxSpeed = Speed(target) * ((targetPos - pos).Length() / Distance);
				SteerToward(targetPos, maxSpeed: maxSpeed);
			}
			return this;
		}
	}

	public class DriveTo : PedState {
		public Func<Vector3> Target;
		private Vector3 target;

		public VehicleDrivingFlags DrivingFlags = (VehicleDrivingFlags)DrivingStyle.Normal;
		public float StoppingRange = 10f;
		public float Speed = 1f;
		private uint Started = 0;
		public DriveTo(Func<Vector3> target, State next=null):base(next) => Target = target;
		public DriveTo(Vector3 target, State next=null) : this(() => target, next) { }

		/// <summary>
		/// Must set Target.
		/// </summary>
		public DriveTo(State next=null):base(next) { }

		private State Start() {
			if( Started == 0 && target != Vector3.Zero ) {
				Started = GameTime;
				TaskClearAll();
				Call(SET_DRIVER_RACING_MODIFIER, Actor, 1f);
				Call(SET_DRIVER_ABILITY, Actor, 1f);
				Call(SET_DRIVER_AGGRESSIVENESS, Actor, .5f);
				Call(TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE,
					Actor, CurrentVehicle(Actor), target,
					Speed,
					DrivingFlags,
					StoppingRange
				);
			}
			return this;
		}
		public State Restart() {
			Started = 0;
			return Start();
		}
		public override State OnTick() {
			Vector3 newTarget = Target();
			if( target == Vector3.Zero || target != newTarget ) {
				target = Target();
				return Restart();
			}
			Call(SET_DRIVE_TASK_CRUISE_SPEED, Actor, 15f - (NavMesh.Ungrown.Count/250f));
			int status = GetScriptTaskStatus(Actor, TaskStatusHash.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE);
			UI.DrawText($"Drive: (started {Started}) (status {status})");
			switch( status ) {
				case 1: return this;
				// TODO: case 2: AddBlockingBox(); return Restart();
				case 7: return Next;
			}
			return this;
		}
	}

	public class DriveWander : PedState {
		public VehicleDrivingFlags DrivingFlags = (VehicleDrivingFlags)DrivingStyle.Normal;
		public float StoppingRange = 4f;
		public float Speed = 1f;
		private uint Started = 0;
		public DriveWander(State next):base(next) { }

		private State Start() {
			if( Started == 0 ) {
				Started = GameTime;
				TaskClearAll();
				Call(SET_DRIVER_RACING_MODIFIER, Actor, 1f);
				Call(SET_DRIVER_ABILITY, Actor, 1f);
				Call(SET_DRIVER_AGGRESSIVENESS, Actor, .5f);
				Call(TASK_VEHICLE_DRIVE_WANDER,
					Actor, CurrentVehicle(Actor),
					Speed,
					DrivingFlags
				);
			}
			return this;
		}
		public override State OnTick() {
			if( Started == 0 ) {
				return Start();
			}
			Call(SET_DRIVE_TASK_CRUISE_SPEED, Actor, 15f - (NavMesh.Ungrown.Count/300f));
			int status = GetScriptTaskStatus(Actor, TaskStatusHash.TASK_VEHICLE_DRIVE_WANDER);
			UI.DrawText($"Drive: status {status}");
			switch( status ) {
				case 1: return this;
				case 7: return Next;
			}
			return this;
		}
	}

	public class EnterVehicle : PedState {
		public Func<VehicleHandle> Target;
		private VehicleHandle target = VehicleHandle.Invalid;
		public uint Timeout = 10000;
		public float Speed = 2f;
		public VehicleSeat Seat = VehicleSeat.Driver;
		public EnterVehicle(VehicleHandle veh, State next = null) : base(next) => Target = () => veh;
		public EnterVehicle(Func<VehicleHandle> veh, State next = null) : base(next) => Target = veh;

		/// <summary>
		/// Always set { Target = () => vehicle } if you construct it this way.
		/// </summary>
		public EnterVehicle(State next=null) :base(next) { }

		public uint Started = 0;

		public void Restart() {
			// TaskClearAll(Actor);
			Call(TASK_ENTER_VEHICLE, Actor, target, Timeout, Seat, Speed, 1, 0);
		}

		public override State OnTick() {
			if( GamePaused ) {
				return this;
			}
			VehicleHandle newTarget = Target();
			if( target == VehicleHandle.Invalid || target != newTarget ) {
				Log($"Setting new target {target}");
				target = newTarget;
				Restart();
			}
			if( target == VehicleHandle.Invalid ) {
				Timeout -= GameTime - LastGameTime;
				if( Timeout <= 0 ) {
					Log($"Timed out");
					return Fail;
				}
				UI.DrawHeadline($"Waiting for target...");
				return this; // wait until the Target() function returns a valid target
			}
			VehicleHandle ActorVehicle = CurrentVehicle(Actor);
			// in the right vehicle
			if( ActorVehicle == target ) {
				Broadcast.SendMessage("EnterVehicle", Actor, data:ActorVehicle);
				return Next;
			}
			// in the wrong vehicle
			if( ActorVehicle != VehicleHandle.Invalid ) {
				return new LeaveVehicle(this);
			}
			// fell out of the task for some reason
			if( ! IsTaskActive(Actor, TaskID.EnterVehicle) ) {
				Restart();
			}
			UI.DrawHeadline(Actor, "State: Enter Vehicle");
			return this;

		}
	}

	public class LeaveVehicle : PedState {
		public LeaveVehicle(State next = null):base(next) { }
		public override State OnTick() {
			if( GamePaused ) {
				return this;
			}
			if( !CanControlCharacter() ) {
				return Next;
			}
			VehicleHandle ActorVehicle = CurrentVehicle(Actor);
			if( ActorVehicle == VehicleHandle.Invalid ) {
				return Next;
			}
			if( ! IsTaskActive(Actor, TaskID.EnterVehicle) ) {
				Call(TASK_LEAVE_VEHICLE, Actor, ActorVehicle, 0);
			}
			return this;
		}
	}

	/*
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
			// Goals.Immediate(new DirectDrive(target) { StoppingRange = 10f, StopAtDestination = false });
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
	*/
}
