

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using GTA.Native;
using System.Numerics;
using static GTA.Native.MemoryAccess;
using static GTA.Native.Hash;
using static GTA.Native.Function;

namespace Shiv {

	public static partial class Globals {


		public static PedHandle Self { get; internal set; } = 0;
		public static PedHandle[] NearbyHumans { get; internal set; } = new PedHandle[0];

		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static IntPtr Address(PedHandle p) => Address((EntHandle)p);
		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static Matrix4x4 Matrix(PedHandle ent) => Matrix((EntHandle)ent);
		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector3 Position(PedHandle ent) => Position(Matrix(ent));
		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static float DistanceToSelf(PedHandle ent) => DistanceToSelf(Position(ent));

		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector3 Forward(PedHandle ent) => Forward(Matrix(ent));
		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector3 Right(PedHandle ent) => Right(Matrix(ent));
		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector3 UpVector(PedHandle ent) => UpVector(Matrix(ent));
		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector3 LeftPosition(PedHandle ent) => LeftPosition((EntHandle)ent);
		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector3 RightPosition(PedHandle ent) => RightPosition((EntHandle)ent);
		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector3 FrontPosition(PedHandle ent) => FrontPosition((EntHandle)ent);
		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector3 RearPosition(PedHandle ent) => RearPosition((EntHandle)ent);

		public static bool IsHuman(PedHandle ent) => ent == 0 ? false : Call<bool>(IS_PED_HUMAN, ent);
		public static bool IsAlive(PedHandle ent) => IsAlive((EntHandle)ent);

		public static float MaxHealthFloat(PedHandle ped) => Read<float>(Address(ped), 0x2A0);
		public static void MaxHealthFloat(PedHandle ped, float value) => Write(Address(ped), 0x2A0, value);

		public static float HealthFloat(PedHandle ped) => Read<float>(Address(ped), 0x280);
		public static void HealthFloat(PedHandle ped, float value) => Write(Address(ped), 0x280, value);

		public static float ArmorFloat(PedHandle ped) => Read<float>(Address(ped), 0x14B8);
		public static void ArmorFloat(PedHandle ped, float value) => Write(Address(ped), 0x14B8, value);

		public static VehicleSeat SeatIndex(PedHandle ped) => (VehicleSeat)Read<sbyte>(Address(ped), 0x15A2);

		public static bool IsAiming(PedHandle ped) => GetConfigFlag(ped, 78);
		public static bool GetConfigFlag(PedHandle ped, int id) => Call<bool>(GET_PED_CONFIG_FLAG, ped, id);

		public static float Heading(PedHandle ent) => Heading((EntHandle)ent);
		public static void Heading(PedHandle ent, float value) => Heading((EntHandle)ent, value);

		public static PedHash GetModel(PedHandle ent) => (PedHash)GetModel((EntHandle)ent);

		public static bool HasWeapon(PedHandle ent, WeaponHash weap) => ent == 0 ? false : Call<bool>(HAS_PED_GOT_WEAPON, ent, weap);

		public static bool IsInCombat(PedHandle ped, PedHandle target) => ped == PedHandle.Invalid || target == 0 ? false : Call<bool>(IS_PED_IN_COMBAT, ped, target);

		public static PedType GetPedType(PedHandle ped) => ped == PedHandle.Invalid ? PedType.Invalid : Call<PedType>(GET_PED_TYPE, ped);

		public static PedHandle CreatePed(PedType type, ModelHash model, Vector3 pos, float heading) {
			switch( RequestModel(model) ) {
				case AssetStatus.Invalid: return PedHandle.ModelInvalid;
				case AssetStatus.Loading: return PedHandle.ModelLoading;
				default: return Call<PedHandle>(CREATE_PED, type, model, pos.X, pos.Y, pos.Z, heading, false, true);
			}
		}

		public static PedHandle CreatePedInsideVehicle(VehicleHandle veh, PedType type, PedHash model, VehicleSeat seat) => CreatePedInsideVehicle(veh, type, (ModelHash)model, seat);

		public static PedHandle CreatePedInsideVehicle(VehicleHandle veh, PedType type, ModelHash model, VehicleSeat seat) {
			switch( RequestModel(model) ) {
				case AssetStatus.Invalid: return PedHandle.ModelInvalid;
				case AssetStatus.Loading: return PedHandle.ModelLoading;
				default: return Call<PedHandle>(CREATE_PED_INSIDE_VEHICLE, veh, type, model, seat, true, true);
			}
		}

			public static Vector3 GetSafeCoordForPed(Vector3 pos, bool onSidewalk = true) {
			NativeVector3 ret;
			unsafe { Call(GET_SAFE_COORD_FOR_PED, pos.X, pos.Y, pos.Z, onSidewalk, new IntPtr(&ret), 0); }
			return ret;
		}

		public static Vector3 Velocity(PedHandle ent) => Velocity((EntHandle)ent);
		public static void Velocity(PedHandle ent, Vector3 value) => Velocity((EntHandle)ent, value);
		public static float Speed(PedHandle ent) => ent == 0 ? 0f : Call<float>(GET_ENTITY_SPEED, ent);

		public static bool IsReloading(PedHandle ped) => ped == PedHandle.Invalid ? false : Call<bool>(IS_PED_RELOADING, ped);
		public static bool IsWeaponReadyToShoot(PedHandle ped) => ped == PedHandle.Invalid ? false : Call<bool>(IS_PED_WEAPON_READY_TO_SHOOT, ped);
		public static bool IsInCover(PedHandle ped) => ped == PedHandle.Invalid ? false : Call<bool>(IS_PED_IN_COVER, ped, false);
		public static bool IsAimingFromCover(PedHandle ped) => ped == PedHandle.Invalid ? false : Call<bool>(IS_PED_AIMING_FROM_COVER, ped);
		public static bool IsGoingIntoCover(PedHandle ped) => ped == PedHandle.Invalid ? false : Call<bool>(IS_PED_GOING_INTO_COVER, ped);

		public static Vector3 HeadPosition(PedHandle ped) => ped == PedHandle.Invalid ? Vector3.Zero : Call<Vector3>(GET_PED_BONE_COORDS, ped, Bone.SKEL_Head, 0.1f, 0.05f, 0.0f);

		// TODO: proper caching here
		public static bool CanSee(PedHandle self, VehicleHandle veh, IntersectOptions opts = IntersectOptions.Map | IntersectOptions.Objects) => CanSee(self, (PedHandle)veh, opts);
		public static bool CanSee(PedHandle self, PedHandle ped, IntersectOptions opts = IntersectOptions.Map | IntersectOptions.Objects) => self == 0 || ped == PedHandle.Invalid ? false : Call<bool>(HAS_ENTITY_CLEAR_LOS_TO_ENTITY, self, ped, opts);

		public static void TaskClearAll() => Call(CLEAR_PED_TASKS, Self);
		public static void TaskClearAll(PedHandle ped) => Call(CLEAR_PED_TASKS, ped);

		public static bool IsSprinting(PedHandle ped) => ped == PedHandle.Invalid ? false : Call<bool>(IS_PED_SPRINTING, ped);
		public static bool IsRunning(PedHandle ped) => ped == PedHandle.Invalid ? false : Call<bool>(IS_PED_RUNNING, ped);

		public static Action ToggleSprint = Throttle(2000, () => {
			SetControlValue(0, Control.Sprint, 1.0f);
		});

		public static bool IsTaskActive(PedHandle p, TaskID taskId) => Call<bool>(GET_IS_TASK_ACTIVE, p, taskId);
		public static int GetScriptTaskStatus(PedHandle p, TaskStatusHash hash) => Call<int>(GET_SCRIPT_TASK_STATUS, p, hash);
		public static void DebugAllTasks(PedHandle ent) {
			var s = ScreenCoords(HeadPosition(ent));
			Enum.GetValues(typeof(TaskID)).Each<TaskID>(id => {
				if( IsTaskActive(ent, id) ) {
					UI.DrawText(s.X, s.Y, $"{id}");
					s.Y += .019f;
				}
			});
		}
	}

}