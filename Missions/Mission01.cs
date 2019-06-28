using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using static GTA.Native.Hash;
using static GTA.Native.Function;
using static Shiv.Global;
using System.Drawing;

namespace Shiv {
	abstract class Mission : Goal {
		public override string ToString() => "Mission";
		public static bool IsInMission() => Call<bool>(GET_MISSION_FLAG);
	}
	class Mission01 : Mission {
		public static int InteriorID = 8706;
		public static readonly Vector3 StartLocation = new Vector3(5310.5f, -5211.87f, 83.52f);
		public enum Phase {
			Approach, // walk into back room
			Threaten, // scare everyone into vault
			Detonate, // use the phone to detonate bomb
			GotoMoney, // walk through the door, toward yellow dot money pickup
			GetMoney, // pick up green dots inside the vault
			WalkOut, // walk out of the vault
			SelectTrevor,
			ShootGuard, // shoot the guard in the head
			Complete, // wrap up
			MoveToCover,
			MoveToButton,
			KillAllCops,
			TakeCover,
		}
		public Phase CurrentPhase = Phase.Approach;
		bool WaitForControl = true;
		uint PauseStarted = 0;
		uint LastShift = 0;
		BlipHUDColor WaitForBlip = BlipHUDColor.Invalid;
		private static Blacklist blacklist = new Blacklist("combat");
		private GoalStatus NextPhase(Phase p, BlipHUDColor waitFor=BlipHUDColor.Invalid) {
			Log($"Going to next phase: {p}");
			WalkTarget = Vector3.Zero;
			AimTarget = Vector3.Zero;
			AimAtHead = PedHandle.Invalid;
			CurrentPhase = p;
			WaitForControl = true;
			WaitForBlip = waitFor;
			PauseStarted = 0;
			return Status;
		}
		private float Threat(PedHandle ped) {
			if( blacklist.Contains(ped) ) {
				return 0f;
			}

			if( GetColor(GetBlip(ped)) != Color.Red ) {
				return 0f;
			}

			float threat = 1 / DistanceToSelf(ped);
			threat += IsAiming(ped) ? .1f : 0f;
			threat += IsAimingFromCover(ped) ? .1f : 0f;
			UI.DrawTextInWorldWithOffset(Position(ped),0f, 0.02f, $"Threat:{threat}");
			return threat;
		}
		public override GoalStatus OnTick() {
			if (!IsInMission()) {
				return Status = GoalStatus.Complete;
			}

			UI.DrawText($"[Mission01] Phase: {CurrentPhase}");

			bool HasControl = CanControlCharacter();
			if( WaitForControl ) {
				if( HasControl ) {
					WaitForControl = false;
				}

				return Status;
			}
			var blips = GetAllBlips(BlipSprite.Standard);
			if( WaitForBlip != BlipHUDColor.Invalid ) {
				if( blips.Any(b => GetBlipHUDColor(b) == WaitForBlip) ) {
					WaitForBlip = BlipHUDColor.Invalid;
				} else {
					UI.DrawText(.5f, .5f, $"Waiting for blip {WaitForBlip}");
					return Status;
				}
			}
			PedHandle ped;
			Vector3 pos;
			if( PauseStarted == 0 ) {
				PauseStarted = GameTime;
			} else if( GameTime - PauseStarted > 200 ) {
				switch( CurrentPhase ) {
					case Phase.Approach:
						if( HasControl ) {
							if( NearbyVehicles.FirstOrDefault(v => GetColor(GetBlip(v)) == Color.Blue) != VehicleHandle.Invalid ) {
								return NextPhase(Phase.KillAllCops);
							}
							ped = DangerSense.NearbyDanger.FirstOrDefault();
							var model = GetModel(ped);
							if( model == PedHash.PrologueSec01Cutscene ) {
								return NextPhase(Phase.ShootGuard); // skip ahead, we must be restarting mid-mission
							} else if( model == PedHash.Snowcop01SMM ) {
								return NextPhase(Phase.KillAllCops);
							}
							AimTarget = HeadPosition(ped);
							if( AimTarget != Vector3.Zero ) {
								if( DistanceToSelf(AimTarget) > 2f ) {
									Goals.Immediate(new WalkTo(PutOnGround(Position(ped), 1f)));
								}
								return Status;
							}
						}
						return NextPhase(Phase.Threaten);
					case Phase.Threaten:
						if( HasControl ) {
							var targets = NearbyHumans.Where(p => GetBlipHUDColor(GetBlip(p)) == BlipHUDColor.Red);
							if( targets.Count() > 0 ) {
								if( GameTime - LastShift > 3000 ) {
									LastShift = GameTime;
									AimAtHead = targets.ToArray().Random<PedHandle>();
								}
								if( AimAtHead != PedHandle.Invalid ) {
									return Status;
								}
							}
						}
						return NextPhase(Phase.Detonate);
					case Phase.Detonate:
						if( HasControl ) {
							Goals.Immediate(new KeySequence() { Spacing = 100 }
								.Add(1, Control.Phone, 300)
								.Add(1, Control.PhoneSelect, 300)
								.Add(1, Control.PhoneSelect, 300));
						}
						return NextPhase(Phase.GotoMoney);
					case Phase.GotoMoney:
						Goals.Immediate(new TaskWalk(Position(blips.FirstOrDefault(b => GetBlipHUDColor(b) == BlipHUDColor.Yellow))));
						return NextPhase(Phase.GetMoney);
					case Phase.GetMoney:
						if( HasControl ) {
							// Log("Blip Colors: ", String.Join(" ", GetAllBlips(BlipSprite.Standard).Select(b => GetBlipColor(b).ToString())));
							var money = PutOnGround(Position(blips.FirstOrDefault(b => GetBlipColor(b) == BlipColor.MissionGreen)), 1.5f);
							if( money != Vector3.Zero ) {
								if( DistanceToSelf(money) < 10f ) {
									MoveToward(money);
								} else {
									Goals.Immediate(new WalkTo(money));
								}
								return Status;
							}
						}
						return NextPhase(Phase.WalkOut);
					case Phase.WalkOut:
						if( HasControl ) {
							if( blips.FirstOrDefault(b => GetColor(b) == Color.Yellow) == BlipHandle.Invalid ) {
								if( MoveResult.Continue == MoveToward(Position(NearbyHumans.FirstOrDefault())) ) {
									return Status;
								}
							}
						}
						return NextPhase(Phase.SelectTrevor);
					case Phase.SelectTrevor:
						ped = NearbyHumans.FirstOrDefault(p => GetModel(p) == PedHash.PrologueSec01Cutscene);
						if( ped != PedHandle.Invalid ) {
							Goals.Immediate(new KeySequence() { Spacing = 100 }.Add(1, Control.SelectCharacterTrevor, 500));
						}
						return NextPhase(Phase.ShootGuard);
					case Phase.ShootGuard:
						if( HasControl ) {
							ped = NearbyHumans.FirstOrDefault(p => GetModel(p) == PedHash.PrologueSec01Cutscene);
							if( ped != PedHandle.Invalid ) {
								UI.DrawText($"IS_PLAYER_TARGETING_ENTITY: {Call<bool>(IS_PLAYER_TARGETTING_ENTITY, CurrentPlayer, ped)}");
								UI.DrawText($"AimingAtEntity(): {AimingAtEntity()}");
								UI.DrawText($"IsFreeAiming(): {IsFreeAiming(CurrentPlayer)}");
								UI.DrawText($"IsFreeAimingAtEntity(ped): {IsFreeAimingAt(CurrentPlayer, ped)}");
								UI.DrawText($"IsAimingAtEntity(ped): {IsAimingAtEntity(ped)}");
								KillTarget = ped;
								return Status;
							}
						}
						Goals.Immediate(new Wait(12000, () => GetAllBlips(BlipSprite.Standard).Any(b => GetColor(b) == Color.Yellow)));
						return NextPhase(Phase.MoveToCover);
					case Phase.MoveToCover:
						if( HasControl && !IsInCover(Self) ) {
							pos = Position(blips.FirstOrDefault(b => GetColor(b) == Color.Yellow));
							if( pos != Vector3.Zero ) {
								Goals.Immediate(new TakeCover(pos));
								return Status;
							}
						}
						Goals.Immediate(new Wait(20000, () => GetAllBlips(BlipSprite.Standard).Any(b => GetColor(b) == Color.Green)));
						return NextPhase(Phase.MoveToButton);
					case Phase.MoveToButton:
						pos = Position(blips.FirstOrDefault(b => GetColor(b) == Color.Green));
						if( pos != Vector3.Zero ) {
							Goals.Immediate(new TaskWalk(pos));
							return Status;
						}
						Goals.Immediate(new Wait(20000, () => GetAllBlips(BlipSprite.Standard).Any(b => GetColor(b) == Color.Red)));
						return NextPhase(Phase.KillAllCops);
					case Phase.TakeCover:
						if( ! IsInCover(Self) ) {
							Goals.Immediate(new TakeCover());
							return Status;
						}
						return NextPhase(Phase.KillAllCops);
					case Phase.KillAllCops:
						if( HasControl ) {
							var cops = NearbyHumans.Where(p => GetColor(GetBlip(p)) == Color.Red);
							int count = cops.Count();
							if( count > 0 ) {
								KillTarget = cops.OrderByDescending(Threat).FirstOrDefault();
								WalkTarget = Position(KillTarget);
							} else {
								KillTarget = PedHandle.Invalid;
								WalkTarget = GetVehicleOffset(NearbyVehicles.FirstOrDefault(v => GetColor(GetBlip(v)) == Color.Blue), VehicleOffsets.DriverDoor);
								// in this case, once we get close to WalkTarget, we lose control, and WalkTarget clears itself
							}
							return Status;
						}
						return NextPhase(Phase.Complete);
					case Phase.Complete:
						WalkTarget = Vector3.Zero;
						KillTarget = PedHandle.Invalid;
						return Status = GoalStatus.Complete;
				}
			}

			return Status;
		}
	}
}
