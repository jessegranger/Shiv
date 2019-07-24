
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using static GTA.Native.Function;
using static GTA.Native.Hash;

namespace Shiv {
	public static partial class Global {

		public static void ChangePlayerPed(PlayerHandle p, PedHandle ped, bool unk1, bool unk2) => Call(CHANGE_PLAYER_PED, p, unk1, unk2);

		public static bool AmAiming() => Call<bool>(IS_PLAYER_FREE_AIMING, CurrentPlayer);
		public static void AmClimbing() => Call(IS_PLAYER_CLIMBING, CurrentPlayer);

		public static bool AmInvincible() => Call<bool>(GET_PLAYER_INVINCIBLE, CurrentPlayer);
		public static void AmInvincible(bool value) => Call(SET_PLAYER_INVINCIBLE, CurrentPlayer, value);

		public static void IgnoredByPolice(bool value) => Call(SET_POLICE_IGNORE_PLAYER, CurrentPlayer, value);
		public static void IgnoredByEveryone(bool value) => Call(SET_EVERYONE_IGNORE_PLAYER, CurrentPlayer, value);
		public static void DispatchesCops(bool value) => Call(SET_DISPATCH_COPS_FOR_PLAYER, CurrentPlayer, value);

		public static bool CanControlCharacter() => Call<bool>(IS_PLAYER_CONTROL_ON, CurrentPlayer);
		public static void CanControlCharacter(bool value) => Call<bool>(SET_PLAYER_CONTROL, CurrentPlayer, value, 0);

		public static EntHandle AimingAtEntity() {
			EntHandle t = 0;
			unsafe { Call<bool>(GET_ENTITY_PLAYER_IS_FREE_AIMING_AT, CurrentPlayer, new IntPtr(&t)); }
			return t;
		}
		public static bool IsAimingAtEntity(PedHandle ent) => ent == 0 ? false : Call<bool>(IS_PLAYER_FREE_AIMING_AT_ENTITY, CurrentPlayer, ent);

		public static Vector3 WantedPosition() => CurrentPlayer == PlayerHandle.Invalid ? Vector3.Zero : Call<Vector3>(GET_PLAYER_WANTED_CENTRE_POSITION, CurrentPlayer);
		public static uint WantedLevel() => CurrentPlayer == PlayerHandle.Invalid ? 0 : Call<uint>(GET_PLAYER_WANTED_LEVEL, CurrentPlayer);
		public static void WantedLevel(uint value) {
			Call(SET_PLAYER_WANTED_LEVEL, CurrentPlayer, value, false);
			Call(SET_PLAYER_WANTED_LEVEL_NOW, CurrentPlayer, false);
		}
		public static void SetWantedLevelMultiplier(float f) => Call(SET_WANTED_LEVEL_MULTIPLIER, f);

		/// <param name="f">Max 1.0</param>
		public static void SetWantedLevelDifficulty(PlayerHandle p, float f) => Call(SET_WANTED_LEVEL_DIFFICULTY, p, f);
		public static uint MaxWantedLevel() => Call<uint>(GET_MAX_WANTED_LEVEL);
		public static void MaxWantedLevel(uint max) => Call(SET_MAX_WANTED_LEVEL, max);

		public static void ShowPoliceOnRadar(bool value) => Call(SET_POLICE_RADAR_BLIPS, value);
		public static void AllRandomPedsFlee(PlayerHandle p, bool value) => Call(SET_ALL_RANDOM_PEDS_FLEE, p, value);
		public static void AllRandomPedsFleeOnce(PlayerHandle p) => Call(SET_ALL_RANDOM_PEDS_FLEE_THIS_FRAME, p);
		public static void IgnoreLowPriorityEvents(PlayerHandle p, bool value) => Call(SET_IGNORE_LOW_PRIORITY_SHOCKING_EVENTS, p, value);
		public static bool CanStartMission(PlayerHandle p) => Call<bool>(CAN_PLAYER_START_MISSION, p);
		public static bool CanStartCutscene(PlayerHandle p) => Call<bool>(IS_PLAYER_READY_FOR_CUTSCENE, p);

		public static bool IsTargettingAnything(PlayerHandle p) => Call<bool>(IS_PLAYER_TARGETTING_ANYTHING, p);
		public static bool IsPlayerTargeting(PlayerHandle p, EntHandle ent) => Call<bool>(IS_PLAYER_TARGETTING_ENTITY, p, ent);
		public static bool TryGetPlayerTarget(PlayerHandle p, out EntHandle ent) {
			EntHandle ret = 0;
			unsafe {
				try {
					return Call<bool>(GET_PLAYER_TARGET_ENTITY, p, new IntPtr(&ret));
				} finally {
					ent = ret;
				}
			}
		}

		public static bool IsFreeAiming(PlayerHandle p) => Call<bool>(IS_PLAYER_FREE_AIMING, p);
		public static bool IsFreeAimingAt(PlayerHandle p, PedHandle e) => Call<bool>(IS_PLAYER_FREE_AIMING_AT_ENTITY, p, e);
		public static bool IsFreeAimingAt(PlayerHandle p, EntHandle e) => Call<bool>(IS_PLAYER_FREE_AIMING_AT_ENTITY, p, e);
		public static bool TryGetFreeAimEntity(PlayerHandle p, out EntHandle ent) {
			EntHandle ret = 0;
			unsafe {
				try {
					return Call<bool>(GET_ENTITY_PLAYER_IS_FREE_AIMING_AT, p, new IntPtr(&ret));
				} finally {
					ent = ret;
				}
			}
		}

		public static void CanDoDriveBy(PlayerHandle p, bool value) => Call(SET_PLAYER_CAN_DO_DRIVE_BY, p, value);
		public static void CanBeHassledByGangs(PlayerHandle p, bool value) => Call(SET_PLAYER_CAN_BE_HASSLED_BY_GANGS, p, value);
		public static void CanUseCover(PlayerHandle p, bool value) => Call(SET_PLAYER_CAN_USE_COVER, p, value);

		public static void RestoreStamina(PlayerHandle p) => Call(RESTORE_PLAYER_STAMINA, p);

		public static int MaxArmor(PlayerHandle p) => Call<int>(GET_PLAYER_MAX_ARMOUR, p);
		public static void MaxArmor(PlayerHandle p, int value) => Call<int>(SET_PLAYER_MAX_ARMOUR, p, value);

		public static bool IsBeingArrested(PlayerHandle p) => Call<bool>(IS_PLAYER_BEING_ARRESTED, p);

		public static void ResetArrestStart(PlayerHandle p) => Call(RESET_PLAYER_ARREST_STATE, p);

		public static VehicleHandle LastVehicle() => Call<VehicleHandle>(GET_PLAYERS_LAST_VEHICLE);

		public static int TimeSinceHitVehicle(PlayerHandle p) => Call<int>(GET_TIME_SINCE_PLAYER_HIT_VEHICLE, p);
		public static int TimeSinceHitPed(PlayerHandle p) => Call<int>(GET_TIME_SINCE_PLAYER_HIT_PED, p);
		public static int TimeSinceDroveOnPavement(PlayerHandle p) => Call<int>(GET_TIME_SINCE_PLAYER_DROVE_ON_PAVEMENT, p);
		public static int TimeSinceDroveAgainstTraffic(PlayerHandle p) => Call<int>(GET_TIME_SINCE_PLAYER_DROVE_AGAINST_TRAFFIC, p);
		public static int TimeSinceLastArrest() => Call<int>(GET_TIME_SINCE_LAST_ARREST);
		public static int TimeSinceLastDeath() => Call<int>(GET_TIME_SINCE_LAST_DEATH);

		public static bool IsFreeForAmbientTask(PlayerHandle p) => Call<bool>(IS_PLAYER_FREE_FOR_AMBIENT_TASK, p);

		public static void SetTargetingMode(TargetingMode mode) => Call(SET_PLAYER_TARGETING_MODE, mode);

		public static void ForcedAim(bool value) => Call(SET_PLAYER_FORCED_AIM, CurrentPlayer, value);
		public static void ForcedAim(PlayerHandle p, bool value) => Call(SET_PLAYER_FORCED_AIM, p, value);
		public static void ForcedZoom(PlayerHandle p, bool value) => Call(SET_PLAYER_FORCED_ZOOM, p, value);
		public static void SkipAimIntro(PlayerHandle p, bool value) => Call(SET_PLAYER_FORCE_SKIP_AIM_INTRO, p, value);

		public static void DisableFiring(PlayerHandle p, bool value) => Call(DISABLE_PLAYER_FIRING, p, value);

		public static void SpecialAbilityDeactivate(PlayerHandle p) => Call(SPECIAL_ABILITY_DEACTIVATE_FAST, p);
		public static void SpecialAbilityRefill(PlayerHandle p) => Call(SPECIAL_ABILITY_FILL_METER, p, true);
		public static bool IsSpecialAbilityActive(PlayerHandle p) => Call<bool>(IS_SPECIAL_ABILITY_ACTIVE, p);
		public static bool IsSpecialAbilityFull(PlayerHandle p) => Call<bool>(IS_SPECIAL_ABILITY_METER_FULL, p);
		public static bool IsSpecialAbilityUnlocked(PlayerHandle p) => Call<bool>(IS_SPECIAL_ABILITY_UNLOCKED, p);

		public static void StartTeleport(PlayerHandle p, Vector3 pos, float heading, bool keepVehicle, bool keepVelocity) => Call(START_PLAYER_TELEPORT, p, pos, heading, keepVehicle, keepVelocity, false);
		public static bool IsTeleportComplete(PlayerHandle p) => Call<bool>(_HAS_PLAYER_TELEPORT_FINISHED, p);
		public static bool IsTeleportActive() => Call<bool>(IS_PLAYER_TELEPORT_ACTIVE);
		public static void StopTeleport() => Call(STOP_PLAYER_TELEPORT);

		public static float StealthNoise(PlayerHandle p) => Call<float>(GET_PLAYER_CURRENT_STEALTH_NOISE, p);

		private static Vector3 aimPosition;
		private static EntHandle aimEntity;
		private static Materials aimMaterial;
		private static Vector3 aimNormal;
		private static Func<(Vector3, EntHandle, Materials, Vector3)> DoAimProbe = FrameThrottle(() => {
			Vector3 start = Position(CameraMatrix);
			Vector3 end = start + (Forward(CameraMatrix) * 400f);
			var result = Raycast(start, end,
				IntersectOptions.Everything ^ IntersectOptions.Vegetation, Self);
			if( result.DidHit ) {
				aimPosition = result.HitPosition;
				aimEntity = result.Entity;
				aimMaterial = result.Material;
				aimNormal = result.SurfaceNormal;
			} else {
				aimPosition = end;
				aimEntity = EntHandle.Invalid;
				aimMaterial = Materials.Invalid;
				aimNormal = Vector3.Zero;
			}
			return (aimPosition, aimEntity, aimMaterial, aimNormal);
		});
		public static Vector3 AimPosition() => DoAimProbe().Item1;
		public static EntHandle AimEntity() => DoAimProbe().Item2;
		public static Materials AimMaterial() => DoAimProbe().Item3;
		public static Vector3 AimNormal() => DoAimProbe().Item4;

		private static Dictionary<PedHash, uint> CashKeys = new Dictionary<PedHash, uint>() {
			{ PedHash.Michael, GenerateHash("SP0_TOTAL_CASH") },
			{ PedHash.Franklin, GenerateHash("SP1_TOTAL_CASH") },
			{ PedHash.Trevor, GenerateHash("SP2_TOTAL_CASH") },
		};
		public static int Money() {
			int money = 0;
			if( CashKeys.TryGetValue(GetModel(Self), out uint hash) ) {
				unsafe { Call(STAT_GET_INT, hash, new IntPtr(&money), -1); }
			}
			return money;
		}
		public static void Money(int value) {
			if( CashKeys.TryGetValue(GetModel(Self), out uint hash) ) {
				Call(STAT_SET_INT, hash, value, 1);
			}
		}
		public static bool IsClimbing(PlayerHandle p) => Call<bool>(IS_PLAYER_CLIMBING, p);
	}
}