using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Shiv.Global;
using System.Numerics;
using static GTA.Native.Hash;
using static GTA.Native.Function;
using static System.Math;
using System.Drawing;

namespace Shiv {

	enum HeliMissionType {
		FlyTo = 4,
		FleeFrom = 8,
		CircleAround = 9,
		CopyTargetHeading = 10,
		LandNearPed = 20,
		Crash = 21
	}
	class HeliMission : State {

		public Func<VehicleHandle> Heli; // required
		private VehicleHandle lastHeli;

		public Func<VehicleHandle> TargetVehicle;
		private VehicleHandle lastTargetVehicle;

		public Func<PedHandle> TargetPed;
		private PedHandle lastTargetPed;

		public Func<Vector3> TargetLocation;
		private Vector3 lastTargetLocation;

		public float LandingRadius = -1f; // < 0f means do not land
		public float Speed = 10f;
		public float Heading = 0f;
		public HeliMissionType MissionType = HeliMissionType.FlyTo;

		/// <summary>
		/// <example>new HeliMission() { Heli = PlayerVehicle, TargetLocation = () => GetWaypoint() }</example>
		/// Can set either TargetLocation, TargetVehicle, or TargetPed. If none set, hover in place.
		/// </summary>
		/// <param name="next"></param>
		public HeliMission(State next = null) : base(next) { }


		private uint Started = 0;
		public State Restart() {
			Started = 0;
			return Start();
		}
		public bool HasTarget() => (lastTargetLocation != Vector3.Zero) || (lastTargetPed != PedHandle.Invalid) || (lastTargetVehicle != VehicleHandle.Invalid);
		private bool UpdateTargets() {
			bool acted = false;
			if( TargetVehicle != null ) {
				VehicleHandle newVehicle = TargetVehicle();
				if( lastTargetVehicle == VehicleHandle.Invalid || lastTargetVehicle != newVehicle ) {
					lastTargetVehicle = newVehicle;
					acted = true;
				}
			}
			if( TargetPed != null ) {
				PedHandle newPed = TargetPed();
				if( lastTargetPed == PedHandle.Invalid || lastTargetPed != newPed ) {
					lastTargetPed = newPed;
					acted = true;
				}
			}
			if( TargetLocation != null ) {
				Vector3 newLocation = TargetLocation();
				if( lastTargetLocation == Vector3.Zero || lastTargetLocation != newLocation ) {
					lastTargetLocation = newLocation;
					acted = true;
				}
			}
			if( Heli != null ) {
				VehicleHandle newHeli = Heli();
				if( lastHeli == VehicleHandle.Invalid || lastHeli != newHeli ) {
					lastHeli = newHeli;
					acted = true;
				}
			}
			return acted;
		}
		public State Start() {
			if( Started == 0 ) {
				Started = GameTime;
				if( HasTarget() ) {
					TaskClearAll(Actor);
					Call(TASK_HELI_MISSION, Actor, lastHeli, lastTargetVehicle, lastTargetPed, lastTargetLocation, MissionType, Speed, LandingRadius, Heading, -1f, -1f, -1f, (LandingRadius <= 0f ? 0 : 32));
				} else {
					return Next;
				}
			}
			return this;
		}

		public override State OnTick() {
			if( UpdateTargets() ) {
				return Restart();
			}
			int status;
			switch( status = GetScriptTaskStatus(Actor, TaskStatusHash.TASK_HELI_MISSION) ) {
				case 7:
					Log($"HeliMission ended because ScriptTaskStatus == 7");
					return Next; // we fell out of the task
				default:
					UI.DrawHeadline(Actor, $"HeliMission: status {status}");
					break;
			}
			return this;
		}

		public override string ToString() {
			string ret = $"{MissionType} ";
			if( lastTargetLocation != Vector3.Zero ) {
				ret += $"{Round(lastTargetLocation, 2)}";
				if( LandingRadius > 0f ) {
					ret += $" and land within {LandingRadius:F1}m";
				} else {
					ret += $" and hover";
				}
			}
			if( lastTargetPed != PedHandle.Invalid ) {
				ret += $"ped {lastTargetPed}";
			}
			if( lastTargetVehicle != VehicleHandle.Invalid ) {
				ret += $"vehicle {lastTargetVehicle}";
			}
			return ret;
		}
	}

	class Hover : State {
		public Func<Vector3> Target;
		private Vector3 location;
		public uint Duration = uint.MaxValue;
		public float MaxSpeed = 5f;
		private uint Started = 0;
		public Hover(State next = null) : base(next) { } // must init Location before calling OnTick
		public Hover(Vector3 pos, State next = null) : base(next) => Target = () => pos;
		public Hover(Func<Vector3> pos, State next = null) : base(next) => Target = pos;
		public override State OnTick() {
			if( PlayerVehicle == VehicleHandle.Invalid ) {
				return Fail;
			}
			if( Started == 0 ) {
				Started = GameTime;
			}
			if( (GameTime - Started) > Duration ) {
				return Next;
			}
			Vector3 newLocation = Target();
			if( location == Vector3.Zero || (location != newLocation && newLocation != Vector3.Zero) ) {
				location = newLocation;
			}
			if( location == Vector3.Zero ) {
				return Fail;
			}
			DrawSphere(location, .5f, Color.Yellow);

			float pitch = 0f, yaw = 0f, roll = 0f, throttle = 0f;

			// exactly counter any current roll, to stabilize
			roll = Call<float>(GET_ENTITY_ROLL, PlayerVehicle);
			pitch = Call<float>(GET_ENTITY_PITCH, PlayerVehicle);
			SetControlValue(0, Control.VehicleFlyRollLeftRight, roll / 10f);

			Vector3 Vh = Velocity(PlayerVehicle);

			// lift up if too low
			if( PlayerPosition.Z < location.Z && Vh.Z <= MaxSpeed ) {
				SetControlValue(0, Control.VehicleFlyThrottleUp, 1f);
				return this;
			}
			// sink down if too high
			if( PlayerPosition.Z > (location.Z * 1.3f) && Vh.Z > 0f ) {
				SetControlValue(0, Control.VehicleFlyThrottleDown, 1f);
				return this;
			}
			Matrix4x4 Mh = Matrix(PlayerVehicle);
			Vector3 Ph = Position(Mh);
			var dist = new Vector2(location.X - Ph.X, location.Y - Ph.Y).Length(); // Sqrt(Pow(location.X - Ph.X, 2) + Pow(location.Y - Ph.Y, 2));
			if( dist > 10f ) {
				// Vector3 Fh = Forward(Mh);
				// float Hh = Heading(Mh);
				YawToward(Mh, Heading(location - Ph));

				pitch = ((Speed(Self) / (MaxSpeed * 3f)) - 1f) / 2f;
				SetControlValue(0, Control.VehicleFlyPitchUpDown, pitch);
			}
			throttle = -Vh.Z;
			SetControlValue(0, Control.VehicleFlyThrottleUp, throttle);
			UI.DrawHeadline($"Speed:{Speed(Self):F2} Roll:{roll:F2} Pitch:{Call<float>(GET_ENTITY_PITCH, PlayerVehicle):F2} Yaw:{Call<Vector3>(GET_ENTITY_ROTATION, PlayerVehicle).Z:F2}");

			return this;

		}
	}

}
