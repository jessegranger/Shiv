using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using static GTA.Native.Hash;
using static GTA.Native.Function;
using static Shiv.Global;
using static Shiv.NavMesh;
using System.Drawing;
using System.Diagnostics;

namespace Shiv {

	class Mission01_Detonate : State { // use the phone to detonate bomb
		public override string Name => "Detonate";
		public override State OnTick() {
			return new PressKey(1, Control.Phone, 300,
				new Delay(2000,
				new PressKey(1, Control.PhoneSelect, 300,
				new Delay(2000,
				new PressKey(1, Control.PhoneSelect, 300,
				new WaitForCutscene(
				new WaitForBlip(BlipHUDColor.Yellow,
				new Mission01_GotoVault())
				))))));
		}
	}

	class Mission01_GotoVault : State {
		public override string Name => "Goto Vault";
		public override State OnTick() {
			Vector3 vault = Position(GetAllBlips().FirstOrDefault(b => GetBlipHUDColor(b) == BlipHUDColor.Yellow));
			return retryCount++ > 10
				? new Mission01_GetMoney()
				: vault == Vector3.Zero
				? new Delay(500, this)
				: (State)new MoveTo(vault, new Mission01_GetMoney());
		}
		private uint retryCount = 0;
	}

	class Mission01_GetMoney : State {
		public override string Name => "Get Money";
		// todo, could enumerate pickups
		public override State OnTick() {
			if( !CanControlCharacter() ) {
				return new WaitForControl(new Mission01_WalkOut());
			}
			Vector3 money = Position(GetAllBlips().FirstOrDefault(b => GetBlipColor(b) == BlipColor.MissionGreen));
			if( retryCount++ > 10 ) {
				return new Mission01_WalkOut();
			}
			if( money == Vector3.Zero ) {
				return new Delay(500, this);
			}
			MoveToward(money);
			return this;
		}
		private uint retryCount = 0;
	}

	class Mission01_WalkOut : State {
		public override string Name => "Walk Out";
		public override State OnTick() {
			if( CanControlCharacter() ) {
				return new MoveTo(Position(NearbyHumans().FirstOrDefault()), this);
			}
			return new WaitForControl(true, new Mission01_SelectTrevor());
		}
	}

	class Mission01_SelectTrevor : State {
		public override string Name => "Select Trevor";
		public override State OnTick() {
			return GetModel(Self) != PedHash.Trevor
				? new PressKey(1, Control.SelectCharacterTrevor, 300, new Delay(500, this))
				: (State)new Mission01_ShootGuard();
		}
	}

	class Mission01_ShootGuard : State {
		public override string Name => "Shoot Guard";
		public override State OnTick() {
			PedHandle ped = NearbyHumans().FirstOrDefault(p => GetModel(p) == PedHash.PrologueSec01Cutscene);
			if( Exists(ped) && IsAlive(ped) ) {
				KillTarget = ped;
				return this;
			} else {
				return new WaitForControl(new WaitForBlip(BlipHUDColor.Yellow, new Mission01_MoveToCover()));
			}
		}
	}
	class Mission01_Complete : State { } // wrap up
	class Mission01_MoveToCover : State {
		public override string Name => "Move To Cover";
		public override State OnTick() {
			if( CanControlCharacter() && !IsInCover(Self) && !IsGoingIntoCover(Self) ) {
				var pos = Position(GetAllBlips().FirstOrDefault(BlipHUDColor.Yellow));
				if( pos != Vector3.Zero ) {
					return new MoveTo(pos, new EnterCover(pos, 2000, this) { Fail = this });
				}
			}
			return new WaitForControl(new WaitForBlip(BlipHUDColor.Green, new Mission01_MoveToButton()));
		}
	}
	class Mission01_MoveToButton : State {
		public override string Name => "Move To Button";
		public override State OnTick() {
			if( CanControlCharacter() ) {
				if( IsInCover(Self) ) {
					return new PressKey(1, Control.Cover, 200, this);
				}
				var pos = Position(GetAllBlips().FirstOrDefault(b => GetColor(b) == Color.Green));
				var dist = DistanceToSelf(pos);
				if( dist < 2f ) {
					MoveToward(pos, stoppingRange: 0f);
					return this;
				}
				if( pos != Vector3.Zero ) {
					return new MoveTo(pos, this);
				}
			}
			return new WaitForControl(
				new WaitForBlip(BlipHUDColor.Red,
				new Mission01_KillAllCops()
			));
		}
	}

	class Mission01_KillAllCops : State {
		public override string Name => "Kill Cops";
		readonly Blacklist blacklist = new Blacklist("Cops");
		public override State OnTick() {
			if( CanControlCharacter() ) {
				var hostile = NearbyHumans().Where(BlipHUDColor.Red);
				if( Call<int>(GET_PLAYER_WANTED_LEVEL, CurrentPlayer) > 0 ) {
					hostile = hostile.Concat(GetAllBlips(BlipSprite.PoliceOfficer).Select(GetEntity).Cast<PedHandle>());
				}
				var getaway = NearbyVehicles().Where(BlipHUDColor.Blue).FirstOrDefault();
				if( hostile.Count() > 0 ) {
					KillTarget = hostile.Without(p => IsInCover(p) && !IsAimingFromCover(p)).Min(DistanceToSelf);
					if( KillTarget == PedHandle.Invalid && !IsInCover(Self) ) {
						return new FindCover(Position(hostile.FirstOrDefault()), this);
					}
					WalkTarget = Position(KillTarget);
				} else if( getaway != VehicleHandle.Invalid ) {
					return new MoveTo(Position(getaway), this);
				}
			}
			return new Mission01_Complete();
		}
	}

	class Mission01_Threaten : State {
		public override string Name => "Threaten Hostages";
		public uint LastShift = 0;
		public override State OnTick() {
			IEnumerable<PedHandle> targets = NearbyHumans().Where(BlipHUDColor.Red).Where(p => CanSee(Self, p, IntersectOptions.Map));
			if( targets.Count() > 0 ) {
				if( GameTime - LastShift > 3000 ) {
					LastShift = GameTime;
					AimTarget = Vector3.Zero;
					AimAtHead = targets.ToArray().Random<PedHandle>();
				}
				if( AimAtHead != PedHandle.Invalid ) {
					return this;
				}
			}
			AimAtHead = PedHandle.Invalid;
			return new Delay(2000, new Mission01_Detonate());
		}
	}

	class Mission01_Approach : State {
		public override string Name => "Approach Hostages";
		public override State OnTick() {
			if( !CanControlCharacter() ) {
				AimTarget = Vector3.Zero;
				return new Mission01_Threaten();
			}
			PedHandle ped = DangerSense.NearbyDanger.FirstOrDefault();
			AimTarget = HeadPosition(ped);
			if( AimTarget != Vector3.Zero ) {
				if( DistanceToSelf(AimTarget) > 2f ) {
					return new MoveTo(PutOnGround(Position(ped), 1f), this);
				}
			}
			return this;
		}
	}
	/*
	abstract class Mission : Goal {
		public override string ToString() => "Mission";
		public static bool IsInMission() => Call<bool>(GET_MISSION_FLAG);
		public override GoalStatus OnTick() => !IsInMission() ? (Status = GoalStatus.Complete) : Status;
	}
	class Mission01 : Mission {
		public static int InteriorID = 8706;
		public static readonly Vector3 StartLocation = new Vector3(5310.5f, -5211.87f, 83.52f);
	}
	*/
}
