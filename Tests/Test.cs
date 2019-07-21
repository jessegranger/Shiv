using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Shiv.Global;
using static System.Math;
using static GTA.Native.Hash;
using static GTA.Native.Function;
using System.Numerics;

namespace Shiv {
	class TestEnterVehicle : State {

		private uint Started = 0;

		public override State OnTick() {
			if( Started == 0 ) {
				Started = GameTime;
				return new Teleport(NodeHandle.DesertAirfield,
					new Delay(1000, (State)((s) => {
						NearbyVehicles().Take(10).Cast<EntHandle>().Each(Delete);
						return new SpawnVehicle(VehicleHash.Blade,
							new Delay(1000, (State)((t) => {
								return new EnterVehicle(NearbyVehicles().FirstOrDefault(), this);
							}))
						);
					}))
				);
			}
			bool test = PlayerVehicle != VehicleHandle.Invalid;
			Log($"Player should be in vehicle: {test}");
			return test ? Next : Fail;
		}
	}

	class TestExitVehicle : State {

		private uint Started = 0;

		public override State OnTick() {
			if( Started == 0 ) {
				Started = GameTime;
				return new Teleport(NodeHandle.DesertAirfield,
					new Delay(1000, (State)((s) => {
						NearbyVehicles().Take(10).Cast<EntHandle>().Each(Delete);
						NearbyHumans().Take(10).Cast<EntHandle>().Each(Delete);
						return new SpawnVehicle(VehicleHash.Blade,
							new Delay(1000, (State)((t) => {
								return new EnterVehicle(NearbyVehicles().FirstOrDefault(), 
									new Delay(1000,
										new LeaveVehicle(this)));
							}))
						);
					}))
				);
			}
			bool test = PlayerVehicle == VehicleHandle.Invalid;
			Log($"Player should not be in vehicle: {test}");
			return test ? Next : Fail;
		}
	}

	class TestSteering : State {

		private uint Started = 0;

		public float StoppingRange = 2f;
		public Vector3 Target = new Vector3(1771, 3239, 42);

		public override State OnTick() {
			if( Started == 0 ) {
				Started = GameTime;
				return new Teleport(NodeHandle.DesertAirfield,
					new Delay(1000, (State)((s) => {
						NearbyVehicles().Take(10).Cast<EntHandle>().Each(Delete);
						return new SpawnVehicle(VehicleHash.Blade,
							new Delay(1000, (State)((t) => {
								return new EnterVehicle(NearbyVehicles().FirstOrDefault(),
									(State)((u) => {
										switch( SteerToward(Target, maxSpeed: 7f, stoppingRange: StoppingRange, stopAtEnd: true) ) {
											case MoveResult.Complete: return this;
											case MoveResult.Failed: return Fail;
											default: return u;
										}
									})
								);
							}))
						);
					}))
				);
			}
			bool test = Sqrt(DistanceToSelf(Target)) < StoppingRange;
			Log($"PlayerVehicle should be in position: {test} {DistanceToSelf(Target):F2}");
			return test ? Next : Fail;
		}

	}

	class TestSteeringBicycle : State {

		private uint Started = 0;

		public float StoppingRange = 3f;
		public Vector3 Target = new Vector3(1771, 3239, 42);

		public override State OnTick() {
			if( Started == 0 ) {
				Started = GameTime;
				return new Teleport(NodeHandle.DesertAirfield,
					new Delay(1000, (State)((s) => {
						NearbyVehicles().Take(10).Cast<EntHandle>().Each(Delete);
						return new SpawnVehicle(VehicleHash.Bmx,
							new Delay(1000, (State)((t) => {
								return new EnterVehicle(NearbyVehicles().FirstOrDefault(),
									(State)((u) => {
										switch( SteerToward(Target, maxSpeed: 7f, stoppingRange: StoppingRange, stopAtEnd: true) ) {
											case MoveResult.Complete: return this;
											case MoveResult.Failed: return Fail;
											default: return u;
										}
									})
								);
							}))
						);
					}))
				);
			}
			bool test = Sqrt(DistanceToSelf(Target)) < StoppingRange;
			Log($"PlayerVehicle should be in position: {test} {DistanceToSelf(Target):F2}");
			return test ? Next : Fail;
		}

	}

	class TestCreatePed : State {
		private uint Started = 0;
		public uint Timeout = 3000;
		public override State OnTick() {
			if( Started == 0 ) {
				Started = GameTime;
				return new Teleport(NodeHandle.DesertAirfield,
					new Delay(1000, (State)((s) => {
						NearbyVehicles().Take(10).Cast<EntHandle>().Each(Delete);
						NearbyHumans().Take(10).Cast<EntHandle>().Each(Delete);
						return new SpawnPed(PedType.Cop, PedHash.Cop01SFY, this);
					}))
				);
			}
			if( (GameTime - Started) > Timeout ) {
				return Fail;
			}
			PedHandle ped = First(NearbyHumans().Where(p => GetPedType(p) == PedType.Cop));
			PedHash model = GetModel(ped);
			if( ped == PedHandle.Invalid || model == PedHash.Invalid ) {
				return this;
			}
			bool test = model == PedHash.Cop01SFY;
			Log($"Should have spawned a cop: {test} {ped} {model}");
			return test ? Next : Fail;
		}
	}

	class TestKillPed : State {
		private uint Started = 0;
		public override State OnTick() {
			if( Started == 0 ) {
				Started = GameTime;
				return new Teleport(NodeHandle.DesertAirfield,
					new Delay(1000, (State)((s) => {
						NearbyVehicles().Take(10).Cast<EntHandle>().Each(Delete);
						NearbyHumans().Take(10).Cast<EntHandle>().Each(Delete);
						GiveWeapon(Self, WeaponHash.Pistol, 100, true);
						PedHandle target = PedHandle.Invalid;
						return new SpawnPed(PedType.Cop, PedHash.Cop01SFY, (State)((t) => {
							if( target == PedHandle.Invalid ) {
								target = First(NearbyHumans().Where(p => GetModel(p) == PedHash.Cop01SFY));
								if( target == PedHandle.Invalid ) {
									return t;
								}
							} else if( !IsAlive(target) ) {
								return this;
							} else {
								ShootToKill(target);
							}
							return t;
						}));
					}))
				);
			}
			bool test = WantedLevel() > 0;
			Log($"Should have killed a cop: {test} {WantedLevel()}");
			WantedLevel(0);
			ForcedAim(false);
			return test ? Next : Fail;
		}
	}

	class TestCommandPed : State {
		private uint Started = 0;
		PedHandle target = PedHandle.Invalid;
		Vector3 Location = new Vector3(1771f, 3239f, 42f);
		public override State OnTick() {
			if( Started == 0 ) {
				Started = GameTime;
				return new Teleport(Location,
					new Delay(1000, (State)((s) => {
						NearbyVehicles().Take(10).Cast<EntHandle>().Each(Delete);
						NearbyHumans().Take(10).Cast<EntHandle>().Each(Delete);
						return new SpawnPed(PedType.Cop, PedHash.Cop01SFY, (State)((t) => {
							if( target == PedHandle.Invalid ) {
								target = NearbyHumans().Where(p => GetModel(p) == PedHash.Cop01SFY).FirstOrDefault();
								if( target == PedHandle.Invalid ) {
									return t;
								}
							} else {
								var A = new TaskWalk(PlayerPosition + (North * 5f)) {
									Actor = target, Speed = 3f,
									PersistFollowing = true
								};
								A.Next = new TaskWalk(PlayerPosition - (West * 5f)) {
									Actor = target, Speed = 3f,
									Next = new TaskWalk(PlayerPosition - (North * 5f)) {
										Actor = target, Speed = 3f,
										Next = new TaskWalk(PlayerPosition + (West * 5f)) {
											Actor = target, Speed = 3f,
											Next =  this
										}
									}
								};
								return A;
							}
							return t;
						}));
					}))
				);
			}
			float dist = (Position(target) - Location).Length();
			bool test = dist < 10f;
			Log($"Should have moved a cop: {test} {dist}");
			Delete(target);
			return test ? Next : Fail;
		}
	}

	class TestPedCanDrive : State {
		private uint Started = 0;
		PedHandle target = PedHandle.Invalid;
		Vector3 Location = new Vector3(1771f, 3239f, 42f);
		public override State OnTick() {
			if( Started == 0 ) {
				Started = GameTime;
				return new Teleport(NodeHandle.DesertAirfield,
					new Delay(1000, (State)((s) => {
						NearbyVehicles().Take(10).Cast<EntHandle>().Each(Delete);
						NearbyHumans().Take(10).Cast<EntHandle>().Each(Delete);
						return new SpawnPed(PedType.Cop, PedHash.Cop01SFY, (State)((t) => {
							target = First(NearbyHumans().Where(p => GetModel(p) == PedHash.Cop01SFY));
							return new TaskWalk(Location) {
								Actor = target,
								Speed = 3f,
								Next = new SpawnVehicle(VehicleHash.Blade) {
									Location = (Location * .8f) + (PlayerPosition * .2f),
									Next = new EnterVehicle() {
										Target = () => First(NearbyVehicles().Where(v => GetModel(v) == VehicleHash.Blade)),
										Actor = target,
										Next = new DriveTo() {
											Target = () => PlayerPosition - (West * 5f),
											Actor = target,
											Next = new LeaveVehicle() {
												Actor = target,
												Next = new TaskWalk(PlayerPosition) {
													Actor = target,
													Speed = 3f,
													Next = this
												}
											}
										}
									}
								}
							};
						}));
					}))
				);
			}
			float dist = (Position(target) - Location).Length();
			bool test = dist < 10f;
			Log($"Should have moved a cop: {test} {dist}");
			Delete(target);
			return test ? Next : Fail;
		}
	}

	class TestShootingRange : State {
		public override State OnTick() {

			return this;
		}
	}

	class ClearArea : State {
		public ClearArea(State next = null) : base(next) { }
		public override State OnTick() {
			NearbyVehicles().Take(10).Cast<EntHandle>().Each(Delete);
			NearbyHumans().Take(10).Cast<EntHandle>().Each(Delete);
			return Next;
		}
	}

	class TestPedCanFly : State {
		private uint Started = 0;
		Vector3 Location = new Vector3(1771f, 3239f, 42f);
		public override State OnTick() {
			if( Started == 0 ) {
				Started = GameTime;
				return Series(
					new Teleport(NodeHandle.DesertAirfield),
					new Delay(1000),
					new ClearArea(),
					this
				);
			} else {

				PedHandle cop = First(NearbyHumans().Where(p => GetModel(p) == PedHash.Cop01SFY));
				if( cop == PedHandle.Invalid ) {
					return new SpawnPed(PedType.Cop, PedHash.Cop01SFY, this);
				}

				VehicleHandle heli = First(NearbyVehicles().Where(v => GetModel(v) == VehicleHash.Buzzard));
				if( heli == VehicleHandle.Invalid ) {
					return new SpawnVehicle(VehicleHash.Buzzard) {
						Location = (Location * .2f) + (PlayerPosition * .8f),
						Next = this
					};
				}

				if( CurrentVehicle(cop) == VehicleHandle.Invalid ) {
					AddStateOnce(cop, new EnterVehicle(heli));
					return new WaitForMessage("EnterVehicle", from: cop, next: this);
				} else {
					AddStateOnce(cop, new HeliMission() {
						Heli = () => heli,
						TargetLocation = () => Location,
						LandingRadius = 10f,
						Heading = Heading(PlayerPosition - Location),
						Next = new LeaveVehicle(null)
					});
					return new WaitForMessage("LeaveVehicle", from: cop, next: this);
				}
			}
		}
	}

}
