using System.Linq;
using static GTA.Native.Hash;
using static GTA.Native.Function;
using static Shiv.Global;
using static Shiv.NavMesh;
using System.Drawing;
using System.Collections.Generic;
using System;
using System.Numerics;

namespace Shiv {
	class Combat : State {
		static readonly Blacklist blacklist = new Blacklist("Combat");
		static readonly Blacklist vehicles = new Blacklist("Vehicles");
		public readonly Func<IEnumerable<PedHandle>> GetHostiles = NearbyHumans;
		public Combat(Func<IEnumerable<PedHandle>> hostiles, State next) : base(next) => GetHostiles = hostiles;
		public Combat(State next):base(next) { }

		private uint lastSwitch = 0;
		private uint killCount = 0;
		private PedHandle target = PedHandle.Invalid;

		public void CheckVehicles() {
			foreach( VehicleHandle veh in NearbyVehicles() ) {
				if( vehicles.Contains(veh) || !Exists(veh) ) {
					continue;
				}
				Dictionary<VehicleSeat, PedHandle> seats = GetSeatMap(veh);
				VehicleHash model = GetModel(veh);
				if( IsHeli(model) ) {
					if( seats[VehicleSeat.Driver] == PedHandle.Invalid ) {
						seats.Values.Without(PedHandle.Invalid).Each(ped => blacklist.Add(ped, 2000));
						vehicles.Add(veh, 3000);
						continue;
					}
					vehicles.Add(veh, 300);
					var Mh = Matrix(veh);
					var Ph = Position(Mh);
					var Ps = Position(Self);
					Ph.Z = 0;
					Ps.Z = 0;
					if( (Ph - Ps).Length() < 20f ) {
						seats.Values.Each(ped => blacklist.Add(ped, 1000));
						continue;
					}
					var Fh = Forward(Mh);
					var Fs = Forward(PlayerMatrix);
					var Rs = Right(PlayerMatrix);
					var Fh_dot_Fs = Vector3.Dot(Fh, Fs);
					var Fh_dot_Rs = Vector3.Dot(Fh, Rs);
					Text.Add(Position(Mh), $"Fh*Fs:{Fh_dot_Fs:F2} Fh*Rs:{Fh_dot_Rs:F2}", 300, 0f, .02f);
					if( Fh_dot_Rs < -.10f ) { // facing left
						blacklist.Add(seats[VehicleSeat.RightFront], 1000);
						blacklist.Add(seats[VehicleSeat.RightRear], 1000);
						blacklist.Remove(seats[VehicleSeat.LeftFront]);
						blacklist.Remove(seats[VehicleSeat.LeftRear]);
					} else if( Fh_dot_Rs > .10f ) { // facing right
						blacklist.Add(seats[VehicleSeat.LeftFront], 1000);
						blacklist.Add(seats[VehicleSeat.LeftRear], 1000);
						blacklist.Remove(seats[VehicleSeat.RightFront]);
						blacklist.Remove(seats[VehicleSeat.RightRear]);
					}

					if( Fh_dot_Fs < -.5f ) { // facing toward us
						blacklist.Remove(seats[VehicleSeat.Driver]);
					} else if( Fh_dot_Fs > .95f ) { // facing away from us
						seats.Values.Each(ped => blacklist.Add(ped, 300));
					}
				} else if( model == VehicleHash.Blimp || model == VehicleHash.Blimp2 ) {
					seats.Values.Without(PedHandle.Invalid).Each(ped => blacklist.Add(ped, 10000));
					vehicles.Add(veh, 10000);
				} else {
					vehicles.Add(veh, 10000);
				}
			}
		}

		public override State OnTick() {
			if( GamePaused || !CanControlCharacter() ) {
				return this;
			}
			UI.DrawHeadline($"Kills: {killCount}");
			var weapon = CurrentWeapon(Self);
			if( weapon != WeaponHash.Invalid) {
				AmmoInClip(Self, weapon, MaxAmmoInClip(Self, weapon));
			}
			var CameraForward = Forward(CameraMatrix);
			var CameraHeading = Rad2Deg(Math.Atan2(CameraForward.Y, CameraForward.X));

			CheckVehicles();

			if( target != PedHandle.Invalid && !IsAlive(target) ) {
				killCount += 1;
				target = PedHandle.Invalid;
			}

			if( target == PedHandle.Invalid || (GameTime - lastSwitch) > 3000 ) {

				target = GetHostiles()
					.Without(blacklist.Contains)
					.Where(ped => (!blacklist.Contains(ped)) && ped != Self && ped != target && Exists(ped) && IsAlive(ped))
					.OrderBy(DistanceToSelf)
					.Take(4)
					.Min(ped => Math.Abs(Heading(Self, ped) - CameraHeading));
				if( target == PedHandle.Invalid ) {
					ForcedAim(false);
					return this;
				}
				lastSwitch = GameTime;
			}
			var ray = Raycast(HeadPosition(Self), HeadPosition(target), IntersectOptions.Everything ^ IntersectOptions.Vegetation, Self);
			if( ray.DidHit && ray.Entity != (EntHandle)target ) {
				Sphere.Add(ray.HitPosition, .06f, Color.Orange, 1000);
				blacklist.Add(target, 1000);
				target = PedHandle.Invalid;
				return this;
			}
			if( IsInCover(target) && !(IsAimingFromCover(target) || IsAiming(target)) ) {
				blacklist.Add(target, 100);
				target = PedHandle.Invalid;
				return this;
			}
			ShootToKill(target);
			return this;
		}
	}
	/*
	class Mission01 : Mission {
		public static int InteriorID = 8706;
		public static readonly Vector3 StartLocation = new Vector3(5310.5f, -5211.87f, 83.52f);
	}
	*/
}
