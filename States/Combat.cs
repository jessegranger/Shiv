﻿using System.Linq;
using static GTA.Native.Hash;
using static GTA.Native.Function;
using static Shiv.Global;
using static Shiv.NavMesh;
using System.Drawing;
using System.Collections.Generic;
using System;
using System.Numerics;
using System.Diagnostics;

namespace Shiv {
	class ReloadWeapon : PedState {
		public override State OnTick() {
			var weapon = CurrentWeapon(Actor);
			var maxClip = MaxAmmoInClip(Actor, weapon);
			var ammo = AmmoInClip(Actor, weapon);
			if( ammo == maxClip ) {
				return Next;
			}
			if( !IsReloading(Actor) ) {
				Call(TASK_RELOAD_WEAPON, Actor, 1);
			}
			return this;
		}
	}

	class EnterCover : PedState {
		readonly Vector3 Target;
		readonly uint Timeout;
		Stopwatch sw = new Stopwatch();
		public EnterCover(Vector3 target, uint timeout, State next = null) : base(next) {
			Target = target;
			Timeout = timeout;
		}
		public EnterCover(Vector3 target, State next = null) : base(next) {
			Target = target;
			Timeout = uint.MaxValue;
		}
		private State Done() => Next;
		public override State OnTick() {
			if( IsInCover(Self) || IsGoingIntoCover(Self) ) {
				return Done();
			}
			if( ! sw.IsRunning ) {
				sw.Start();
			}
			return sw.ElapsedMilliseconds > Timeout
				? Fail
				: new Machine(
				new LookAt(Target, null) { Duration = 600 },
				new PressKey(1, Control.Cover, 300, new Delay(300, new Machine.Clear(this)))
			);
		}
	}

	class ExitCover : PedState {
		public uint Started = 0;
		public override State OnTick() {
			if( !IsInCover(Actor) ) {
				return Next;
			}
			if( Started == 0 ) {
				Started = GameTime;
				Call(TASK_EXIT_COVER, Actor, 20, Position(Actor));
			}
			return this;
		}
		public override string ToString() => $"ExitCover({GetScriptTaskStatus(Self, TaskStatusHash.SCRIPT_TASK_EXIT_COVER)})";
	}

	class Combat : State, IDisposable {
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
					Ph.Z = 0; // ignore any differences in height
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

		private VehicleHandle CoverVehicle() => First(NearbyVehicles().Where(veh => IsSeatFree(veh, VehicleSeat.Driver) && Speed(veh) == 0f).Without(coverVehicleBlacklist.Contains));
		public Vector3 CoverPosition(VehicleHandle veh, Vector3 danger) => FindCoverBehindVehicle(veh, danger);

		private VehicleHandle coverVehicle;
		private Vector3 coverPosition = Vector3.Zero;
		private PathRequest coverPathRequest;
		private static Blacklist coverVehicleBlacklist = new Blacklist("CoverVehicles");
		private SmoothPath coverPath;
		private uint lastCoverChange = 0;
		private void CheckCover() {
			if( target != PedHandle.Invalid && (GameTime - lastCoverChange) > 15000 ) {
				lastCoverChange = GameTime;
				var danger = Position(target);
				VehicleHandle newVehicle = CoverVehicle();
				if( coverVehicle == VehicleHandle.Invalid || coverVehicle != newVehicle ) {
					coverVehicle = newVehicle;
					coverPosition = Vector3.Zero;
				}
				Vector3 newCover = CoverPosition(coverVehicle, danger);
				if( coverPosition == Vector3.Zero || coverPosition != newCover ) {
					coverPosition = newCover;
					if( coverPathRequest != null ) {
						coverPathRequest.Cancel();
					}
					coverPathRequest = new PathRequest(PlayerNode, Handle(coverPosition), 1000, false, true, true, 1);
				}
			}
		}

		public override State OnTick() {
			if( GamePaused || !CanControlCharacter() ) {
				return this;
			}
			if( !IsAlive(Self) ) {
				return Next;
			}
			UI.DrawHeadline($"Kills: {killCount}");
			var weapon = CurrentWeapon(Self);
			var maxClip = MaxAmmoInClip(Self, weapon);
			if( weapon != WeaponHash.Invalid) { AmmoInClip(Self, weapon, MaxAmmoInClip(Self, weapon)); }
			var CameraForward = Forward(CameraMatrix);
			var CameraHeading = Heading(CameraForward);

			CheckVehicles();

			if( coverPathRequest != null ) {
				if( coverPathRequest.IsReady() ) {
					coverPath = new SmoothPath(coverPathRequest.GetResult());
					coverPathRequest = null;
				} else if( coverPathRequest.IsFailed() ) {
					coverVehicleBlacklist.Add(coverVehicle, 15000);
				} else if( coverPathRequest.IsCanceled() ) {
					coverPathRequest = null;
				}
			}

			if( coverPath != null ) {
				bool aiming = IsAiming(Self);
				bool running = IsRunning(Self);
				bool cover = IsInCover(Self);
				var step = coverPath.NextStep(PlayerPosition);
				if( !coverPath.IsComplete() ) {
					if( cover ) {
						return new ExitCover() { Next = this };
					} else if( aiming && !running ) {
						ToggleSprint();
					}
					MoveToward(step);
				} else if( !cover ) {
					coverPath = null;
					AddStateOnce(Self, new PressKey(Control.Cover, 200));
				}
			}

			if( target != PedHandle.Invalid && !IsAlive(target) ) {
				killCount += 1;
				target = PedHandle.Invalid;
			}

			if( target == PedHandle.Invalid || (GameTime - lastSwitch) > 3000 ) {
				var hostiles = GetHostiles();
				if( hostiles.Count() == 0 ) {
					return Next;
				}
				hostiles.Where(IsShooting).Each(blacklist.Remove);
				target = hostiles
					.Without(blacklist.Contains)
					.Where(ped => (!blacklist.Contains(ped)) && ped != Self && ped != target && Exists(ped) && IsAlive(ped))
					.OrderBy(DistanceToSelf)
					.Where(ped => DistanceToSelf(ped) < 150f*150f)
					.Take(3)
					.Min(ped => Math.Abs(Heading(Self, ped) - CameraHeading));
				if( target == PedHandle.Invalid ) {
					if( AmmoInClip(Self, weapon) < maxClip ) {
						return new ReloadWeapon() { Next = this };
					}
					ForcedAim(false);
					return this;
				}

				CheckCover();
				lastSwitch = GameTime;
			}
			var ray = Raycast(HeadPosition(Self), HeadPosition(target), IntersectOptions.Everything ^ IntersectOptions.Vegetation, Self);
			if( ray.DidHit && ray.Entity != (EntHandle)target ) {
				Sphere.Add(ray.HitPosition, .06f, Color.Orange, 1000);
				blacklist.Add(target, 1000);
				target = PedHandle.Invalid;
				return this;
			}
			/*
			if( IsInCover(target) && !(IsAimingFromCover(target) || IsAiming(target)) ) {
				blacklist.Add(target, 100);
				target = PedHandle.Invalid;
				return this;
			}
			*/
			ShootToKill(target);
			return this;
		}

		#region IDisposable
		private bool disposed = false; // To detect redundant calls

		protected virtual void Dispose(bool disposing) {
			if( !disposed ) {
				if( disposing ) {
					// TODO: dispose managed state (managed objects).
					if( coverPathRequest != null ) {
						coverPathRequest.Dispose();
						coverPathRequest = null;
					}
				}

				// TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
				// TODO: set large fields to null.

				disposed = true;
			}
		}

		// TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
		// ~Combat() {
		//   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
		//   Dispose(false);
		// }

		// This code added to correctly implement the disposable pattern.
		public void Dispose() {
			// Do not change this code. Put cleanup code in Dispose(bool disposing) above.
			Dispose(true);
			// TODO: uncomment the following line if the finalizer is overridden above.
			// GC.SuppressFinalize(this);
		}
		#endregion

	}
	/*
	class Mission01 : Mission {
		public static int InteriorID = 8706;
		public static readonly Vector3 StartLocation = new Vector3(5310.5f, -5211.87f, 83.52f);
	}
	*/
}
