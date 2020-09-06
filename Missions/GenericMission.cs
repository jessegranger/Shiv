using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Shiv.Global;
using static GTA.Native.Hash;
using static GTA.Native.Function;
using System.Numerics;

namespace Shiv {

	class ChaseAndKill : PedState {
		public PedHandle Target;
		public override State OnTick() {
			if( Exists(Target) && IsAlive(Target) ) {
				if( CurrentVehicle(Actor) == VehicleHandle.Invalid ) {
					return Fail;
				}
				var pos = Position(Actor);
				var end = Position(Target);
				var posDelta = (end - pos);
				var ray = Raycast(HeadPosition(Actor) + Vector3.Normalize(posDelta), HeadPosition(Target), IntersectOptions.Everything ^ IntersectOptions.Vegetation, Self);
				if( ray.DidHit && ray.Entity == (EntHandle)Target ) {
					TaskClearAll(Actor);
					ShootToKill(Target);
					return this;
				} else {
					var veh = CurrentVehicle(Target);
					if( veh != VehicleHandle.Invalid && GetScriptTaskStatus(Actor, TaskStatusHash.TASK_VEHICLE_CHASE) == 7 ) {
						Call(TASK_VEHICLE_CHASE, Actor, Target);
					}
					LookToward(end, deadZone: 4f);
					return this;
				}
			}
			return Next;
		}
	}

	class GenericMission : State {
		public GenericMission(State next=null):base(next) { }
		public bool IsInMission() => Call<bool>(GET_MISSION_FLAG);
		public override State OnTick() {
			if( GamePaused ) {
				return this;
			}
			if( ! IsInMission() ) {
				return Next;
			}
			BlipHandle blip;
			if( PlayerVehicle == VehicleHandle.Invalid ) {
				if( TryGetBlip(BlipHUDColor.Blue, out blip) ) {
					EntHandle ent = GetEntity(blip);
					switch( GetEntityType(ent) ) {
						case EntityType.Ped:
							return new FollowPed((PedHandle)ent, this);
						case EntityType.Vehicle:
							return new EnterVehicle((VehicleHandle)ent, this);
					}
				} else if( TryGetBlip(BlipHUDColor.Red, out blip) ) {
					EntHandle ent = GetEntity(blip);
					switch( GetEntityType(ent) ) {
						case EntityType.Ped:
							return new FollowPed((PedHandle)ent, this);
						case EntityType.Vehicle:
							return new EnterVehicle(First(NearbyVehicles()), this);
					}
				}
			} else { // we are in a vehicle
				if( TryGetBlip(BlipHUDColor.Yellow, out blip) ) {
					var pos = Position(blip);
					if( pos != Vector3.Zero ) {
						return new DriveTo(pos, this) { DrivingFlags = VehicleDrivingFlags.Human };
					}
				} else if( TryGetBlip(BlipHUDColor.Red, out blip) ) {
					EntHandle ent = GetEntity(blip);
					switch( GetEntityType(ent) ) {
						case EntityType.Ped:
							var ped = (PedHandle)ent;
							if( CurrentVehicle(ped) != VehicleHandle.Invalid ) {
								return new ChaseAndKill() { Target = ped };
							}
							break;
						case EntityType.Vehicle:
							if( !IsSeatFree((VehicleHandle)ent, VehicleSeat.Driver) ) {
								return new ChaseAndKill() { Target = GetPedInSeat((VehicleHandle)ent, VehicleSeat.Driver) };
							}
							break;
					}
				}
			}
			return this;
		}
	}
}
