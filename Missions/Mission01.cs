using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using static GTA.Native.Hash;
using static GTA.Native.Function;
using static Shiv.Global;
using static Shiv.NavMesh;
using System.Drawing;
using StateMachine;

namespace Shiv {

	class Mission01_Approach : State {
		public override string Name => "Approach Hostages";
		public override State OnTick() {
			PedHandle ped = NearbyHumans().FirstOrDefault(BlipHUDColor.Red);
			return ped == PedHandle.Invalid
				? Fail
				: new Machine(
					new AimAt(ped),
					new WalkTo(Position(ped)),
					new WaitForControl(false,
						new Machine.Clear(next: new Mission01_Threaten()))
			);
		}
	}

	class Mission01_Threaten : State {
		public override string Name => "Threaten Hostages";
		private uint LastShift = 0;
		private PedHandle Target;
		public override State OnTick() {
			IEnumerable<PedHandle> targets = NearbyHumans().Cast<EntHandle>().Where(BlipHUDColor.Red).Cast<PedHandle>().Where(p => CanSee(Self, p, IntersectOptions.Map));
			if( targets.Count() > 0 ) {
				if( GameTime - LastShift > 2000 ) {
					LastShift = GameTime;
					Target = targets.ToArray().Random<PedHandle>();
				}
				if( Target != PedHandle.Invalid ) {
					ForcedAim(CurrentPlayer, true);
					LookToward(HeadPosition(Target));
					return this;
				}
			}
			ForcedAim(CurrentPlayer, false);
			return new Delay(2000, new Mission01_Detonate());
		}
	}

	class Mission01_Detonate : State { // use the phone to detonate bomb
		public override string Name => "Detonate";
		public override State OnTick() {
			return Series(
				new PressKey(1, Control.Phone, 300),
				new Delay(1200),
				new PressKey(1, Control.PhoneSelect, 300),
				new Delay(1200),
				new PressKey(1, Control.PhoneSelect, 300),
				new WaitForCutscene(),
				new WaitForBlip(BlipHUDColor.Yellow),
				new Mission01_GotoVault()
			);
		}
	}

	class Mission01_GotoVault : State {
		public override string Name => "Goto Vault";
		public override State OnTick() {
			Vector3 vault = Position(GetAllBlips().FirstOrDefault(b => GetBlipHUDColor(b) == BlipHUDColor.Yellow));
			return retryCount++ > 10
				? new Mission01_GetMoney()
				: vault == Vector3.Zero
				? new Delay(500, this) // if we cant see the vault location yet, wait 500ms and try again
				: (State)new WalkTo(vault, new Mission01_GetMoney());
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
			if( money == Vector3.Zero ) {
				if( retryCount++ > 10 ) {
					return new Mission01_WalkOut();
				}
				return new Delay(500, this);
			}
			MoveToward(money);
			return this;
		}
		private uint retryCount = 0;
	}

	class Mission01_WalkOut : State {
		public override string Name => "Walk Out";
		public new State Next = new Mission01_SelectTrevor();
		public override State OnTick() {
			return CanControlCharacter()
				? new Machine(
						new WalkTo(Position(NearbyHumans().FirstOrDefault())),
						new WaitForCutscene(new Machine.Clear(Next)))
				: Next;
		}
	}

	class Mission01_SelectTrevor : State {
		public override string Name => "Select Trevor";
		public new State Next = new Mission01_ShootGuard();
		public override State OnTick() {
			if( GetModel(Self) == PedHash.Trevor ) {
				SetState(Self, Next); // dont just return a new state because the value of "Self" just changed
				return null;
			} else {
				return new PressKey(1, Control.SelectCharacterTrevor, 200, new Delay(900, this));
			}
		}
	}

	class Mission01_ShootGuard : State {
		public override string Name => "Shoot Guard";
		public new State Next = new Mission01_MoveToCover();
		public override State OnTick() {
			PedHandle ped = NearbyHumans().FirstOrDefault(p => GetModel(p) == PedHash.PrologueSec01Cutscene);
			return Exists(ped) && IsAlive(ped)
				? (State)new ShootAt(ped, this)
				: new WaitForBlip(BlipHUDColor.Yellow) { Next = Next };
		}
	}


	class Mission01_MoveToCover : State {
		public override string Name => "Move To Cover";
		public override State OnTick() {
			if( CanControlCharacter() && !IsInCover(Self) && !IsGoingIntoCover(Self) ) {
				Vector3 pos = Position(GetAllBlips().FirstOrDefault(BlipHUDColor.Yellow));
				if( pos != Vector3.Zero ) {
					return new WalkTo(pos, new EnterCover(pos, 2000, this) { Fail = this });
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
				Vector3 pos = Position(GetAllBlips().FirstOrDefault(b => GetColor(b) == Color.Green));
				var dist = DistanceToSelf(pos);
				if( dist < 2f ) {
					MoveToward(pos, stoppingRange: 0f);
					return this;
				}
				if( pos != Vector3.Zero ) {
					return new WalkTo(pos, this);
				}
			}
			return Series(
				new WaitForControl(),
				new WaitForBlip(BlipHUDColor.Red),
				new Mission01_GetAway()
			);
		}
	}

	class Mission01_GetAway : State {
		public override string Name => "Get away";

		public new State Next = new Mission01_Complete();

		public override State OnTick() {
			if( CanControlCharacter() ) {
				if( TryGetHuman(BlipHUDColor.Red, out PedHandle enemy) ) {
					return new Combat(this);
				}
				if( TryGetHuman(BlipHUDColor.Blue, out PedHandle friend) ) {
					MoveToward(Position(friend));
					return this;
				}
				if( TryGetVehicle(BlipHUDColor.Blue, out VehicleHandle car) ) {
					MoveToward(Position(car));
					return this;
				}
			}
			return Next;
		}
	}

	class Mission01_Complete : State { } // wrap up


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
