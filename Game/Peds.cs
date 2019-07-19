

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using GTA.Native;
using System.Numerics;
using static GTA.Native.MemoryAccess;
using static GTA.Native.Hash;
using static GTA.Native.Function;
using System.Drawing;
using System.Runtime.InteropServices;
using static Shiv.Imports;
using System.Linq;

namespace Shiv {

	public static partial class Global {


		public static PedHandle Self { get; internal set; } = 0;

		private static int[] GetAllPeds(int max = 512) {
			int[] ret;
			unsafe {
				fixed ( int* buf = new int[max] ) {
					int count = WorldGetAllPeds(buf, max);
					ret = new int[count];
					Marshal.Copy(new IntPtr(buf), ret, 0, count);
				}
			}
			return ret;
		}

		public static readonly Func<PedHandle[]> NearbyHumans = Throttle(101, () =>
			GetAllPeds().Cast<PedHandle>()
				.Where(h => h != Self && Exists(h) && IsAlive(h) && IsHuman(h))
				.OrderBy(DistanceToSelf)
				.ToArray()
		);

		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool Exists(PedHandle ent) => Exists((EntHandle)ent);
		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static IntPtr Address(PedHandle p) => Address((EntHandle)p);
		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static Matrix4x4 Matrix(PedHandle ent) => Matrix((EntHandle)ent);
		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector3 Position(PedHandle ent) => Position(Matrix(ent));
		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static float DistanceToSelf(PedHandle ent) => DistanceToSelf(Position(ent));

		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector3 Forward(PedHandle ent) => Forward(Matrix(ent));
		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector3 Right(PedHandle ent) => Right(Matrix(ent));
		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector3 UpVector(PedHandle ent) => UpVector(Matrix(ent));

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

		public static float Heading(PedHandle ped, PedHandle other) {
			var d = Vector3.Normalize(Position(other) - Position(ped));
			return Rad2Deg(Math.Atan2(d.Y, d.X));
		}
		public static float Heading(PedHandle ent) => Heading((EntHandle)ent);
		public static void Heading(PedHandle ent, float value) => Heading((EntHandle)ent, value);

		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static void GetModelDimensions(PedHash model, out Vector3 backLeft, out Vector3 frontRight)  => GetModelDimensions((ModelHash)model, out backLeft, out frontRight);
		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static PedHash GetModel(PedHandle ent) => (PedHash)GetModel((EntHandle)ent);

		public static bool HasWeapon(PedHandle ent, WeaponHash weap) => ent == 0 ? false : Call<bool>(HAS_PED_GOT_WEAPON, ent, weap);

		public static bool IsInCombat(PedHandle ped, PedHandle target) => ped == PedHandle.Invalid || target == 0 ? false : Call<bool>(IS_PED_IN_COMBAT, ped, target);

		public static PedType GetPedType(PedHandle ped) => ped == PedHandle.Invalid ? PedType.Invalid : Call<PedType>(GET_PED_TYPE, ped);

		public static PedHandle CreatePed(PedType type, PedHash model, Vector3 pos, float heading) {
			switch( RequestModel(model) ) {
				case AssetStatus.Invalid: return PedHandle.ModelInvalid;
				case AssetStatus.Loading: return PedHandle.ModelLoading;
				default: return Call<PedHandle>(CREATE_PED, type, model, pos.X, pos.Y, pos.Z, heading, false, true);
			}
		}
		public static bool TryCreatePed(PedType type, PedHash model, Vector3 pos, float heading, out PedHandle ped) {
			if( ! IsValid(model) ) {
				ped = PedHandle.ModelInvalid;
				return false;
			}
			if( ! IsLoaded(model) ) {
				ped = PedHandle.ModelLoading;
				RequestModel(model);
				return false;
			}
			ped = Call<PedHandle>(CREATE_PED, type, model, pos.X, pos.Y, pos.Z, heading, false, true);
			return true;
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
		public static bool IsInCoverFacingLeft(PedHandle ped) => ped == PedHandle.Invalid ? false : Call<bool>(IS_PED_IN_COVER_FACING_LEFT, ped, false);
		public static bool IsAimingFromCover(PedHandle ped) => ped == PedHandle.Invalid ? false : Call<bool>(IS_PED_AIMING_FROM_COVER, ped);
		public static bool IsGoingIntoCover(PedHandle ped) => ped == PedHandle.Invalid ? false : Call<bool>(IS_PED_GOING_INTO_COVER, ped);

		public static Vector3 HeadPosition(PedHandle ped) => ped == PedHandle.Invalid ? Vector3.Zero : Call<Vector3>(GET_PED_BONE_COORDS, ped, Bone.SKEL_Head, 0.1f, 0.05f, 0.0f);

		// TODO: proper caching here
		public static bool CanSee(PedHandle self, VehicleHandle veh, IntersectOptions opts = IntersectOptions.Map | IntersectOptions.Objects) => CanSee(self, (PedHandle)veh, opts);
		public static bool CanSee(PedHandle self, PedHandle ped, IntersectOptions opts = IntersectOptions.Map | IntersectOptions.Objects) => self == 0 || ped == PedHandle.Invalid ? false : Call<bool>(HAS_ENTITY_CLEAR_LOS_TO_ENTITY, self, ped, opts);
		public static bool CanSee(PedHandle ped, Vector3 pos, IntersectOptions opts = IntersectOptions.Map | IntersectOptions.Objects ) {
			Vector3 start = HeadPosition(ped);
			float len = (start - pos).Length();
			RaycastResult result = Raycast(start, pos, opts, ped);
			return result.DidHit ? (start - result.HitPosition).Length() / len > .99f : false;
		}

		/// <summary>
		/// A partial CanSee(). eg NearbyHumans().Where(CanSee(Self, opts));
		/// </summary>
		public static Func<PedHandle, bool> CanSee(PedHandle self, IntersectOptions opts) => (PedHandle p) => CanSee(self, p, opts);

		public static void TaskClearAll() => Call(CLEAR_PED_TASKS, Self);
		public static void TaskClearAll(PedHandle ped) => Call(CLEAR_PED_TASKS, ped);

		public static bool IsSprinting(PedHandle ped) => ped == PedHandle.Invalid ? false : Call<bool>(IS_PED_SPRINTING, ped);
		public static bool IsRunning(PedHandle ped) => ped == PedHandle.Invalid ? false : Call<bool>(IS_PED_RUNNING, ped);

		public static Func<bool> ToggleSprint = Throttle(2000, () => {
			SetControlValue(0, Control.Sprint, 1.0f);
			return true;
		});

		public static bool IsTaskActive(PedHandle p, TaskID taskId) => Call<bool>(GET_IS_TASK_ACTIVE, p, taskId);
		public static int GetScriptTaskStatus(PedHandle p, TaskStatusHash hash) => Call<int>(GET_SCRIPT_TASK_STATUS, p, hash);
		public static void DebugAllTasks(PedHandle ent) {
			PointF s = ScreenCoords(HeadPosition(ent));
			UI.DrawText(s.X, s.Y, $"Model: {GetModel(ent)}");
			s.Y += .019f;
			Enum.GetValues(typeof(TaskID)).Each<TaskID>(id => {
				if( IsTaskActive(ent, id) ) {
					UI.DrawText(s.X, s.Y, $"{id}");
					s.Y += .019f;
				}
			});
		}
		public static void AsSequence(Action a) {
			int seq;
			unsafe { Call(OPEN_SEQUENCE_TASK, new IntPtr(&seq)); }
			a();
			Call(CLOSE_SEQUENCE_TASK, seq);
			Call(TASK_PERFORM_SEQUENCE, Self, seq);
			unsafe { Call(CLEAR_SEQUENCE_TASK, new IntPtr(&seq)); }
		}
		public static bool IsJumping(PedHandle ped) => ped != PedHandle.Invalid && Call<bool>(IS_PED_JUMPING, ped);
		public static bool IsClimbing(PedHandle ped) => ped != PedHandle.Invalid && Call<bool>(IS_PED_CLIMBING, ped);
		public static bool IsFalling(PedHandle ped) => ped != PedHandle.Invalid && Call<bool>(IS_PED_FALLING, ped);
		public static bool IsVaulting(PedHandle ped) => ped != PedHandle.Invalid && Call<bool>(IS_PED_VAULTING, ped);
		public static bool IsDiving(PedHandle ped) => ped != PedHandle.Invalid && Call<bool>(IS_PED_DIVING, ped);
		public static bool IsJumpingOutOfVehicle(PedHandle ped) => ped != PedHandle.Invalid && Call<bool>(IS_PED_JUMPING_OUT_OF_VEHICLE, ped);
		public static bool IsDucking(PedHandle ped) => ped != PedHandle.Invalid && Call<bool>(IS_PED_DUCKING, ped);
		public static bool IsHangingOntoVehicle(PedHandle ped) => ped != PedHandle.Invalid && Call<bool>(IS_PED_HANGING_ON_TO_VEHICLE, ped);
		public static bool IsProne(PedHandle ped) => ped != PedHandle.Invalid && Call<bool>(IS_PED_PRONE, ped);
		public static bool IsInCombat(PedHandle ped) => ped != PedHandle.Invalid && Call<bool>(IS_PED_IN_COMBAT, ped);
		public static bool IsDoingDriveBy(PedHandle ped) => ped != PedHandle.Invalid && Call<bool>(IS_PED_DOING_DRIVEBY, ped);
		public static bool IsJacking(PedHandle ped) => ped != PedHandle.Invalid && Call<bool>(IS_PED_JACKING, ped);
		public static bool IsBeingJacked(PedHandle ped) => ped != PedHandle.Invalid && Call<bool>(IS_PED_BEING_JACKED, ped);
		public static bool IsStunned(PedHandle ped) => ped != PedHandle.Invalid && Call<bool>(IS_PED_BEING_STUNNED, ped);
		public static bool IsFleeing(PedHandle ped) => ped != PedHandle.Invalid && Call<bool>(IS_PED_FLEEING, ped);
			// _IS_PED_STANDING_IN_COVER = 0x6A03BF943D767C93, // 
		public static bool IsInTaxi(PedHandle ped) => ped != PedHandle.Invalid && Call<bool>(IS_PED_IN_ANY_TAXI, ped);
		public static bool IsInBoat(PedHandle ped) => ped != PedHandle.Invalid && Call<bool>(IS_PED_IN_ANY_BOAT, ped);
		public static bool IsInPlane(PedHandle ped) => ped != PedHandle.Invalid && Call<bool>(IS_PED_IN_ANY_PLANE, ped);
		public static bool IsInPoliceVehicle(PedHandle ped) => ped != PedHandle.Invalid && Call<bool>(IS_PED_IN_ANY_POLICE_VEHICLE, ped);
		public static bool IsInTrain(PedHandle ped) => ped != PedHandle.Invalid && Call<bool>(IS_PED_IN_ANY_TRAIN, ped);
		public static bool IsInSubmarine(PedHandle ped) => ped != PedHandle.Invalid && Call<bool>(IS_PED_IN_ANY_SUB, ped);
		public static bool IsInVehicle(PedHandle ped) => ped != PedHandle.Invalid && Call<bool>(IS_PED_IN_ANY_VEHICLE, ped, 0);
		public static bool IsInjured(PedHandle ped) => ped != PedHandle.Invalid && Call<bool>(IS_PED_INJURED, ped);
		public static bool IsHurt(PedHandle ped) => ped != PedHandle.Invalid && Call<bool>(IS_PED_HURT, ped);
		public static bool IsFatal(PedHandle ped) => ped != PedHandle.Invalid && Call<bool>(IS_PED_FATALLY_INJURED, ped);
		public static bool IsDeadOrDying(PedHandle ped) => ped != PedHandle.Invalid && Call<bool>(IS_PED_DEAD_OR_DYING, ped);
		public static bool IsPlayer(PedHandle ped) => ped != PedHandle.Invalid && Call<bool>(IS_PED_A_PLAYER, ped);
		public static bool IsStopped(PedHandle ped) => ped != PedHandle.Invalid && Call<bool>(IS_PED_STOPPED, ped);
		public static bool IsInMelee(PedHandle ped) => ped != PedHandle.Invalid && Call<bool>(IS_PED_IN_MELEE_COMBAT, ped);
		public static bool IsShooting(PedHandle ped) => ped != PedHandle.Invalid && Call<bool>(IS_PED_SHOOTING, ped);
		public static bool IsMale(PedHandle ped) => ped != PedHandle.Invalid && Call<bool>(IS_PED_MALE, ped);
		public static bool IsOnVehicle(PedHandle ped) => ped != PedHandle.Invalid && Call<bool>(IS_PED_ON_VEHICLE, ped);
		public static bool IsInvincible(PedHandle ent) => IsInvincible((EntHandle)ent);
		public static void IsInvincible(PedHandle ent, bool value) => IsInvincible((EntHandle)ent, value);
		public static bool IsRagdoll(PedHandle ped) => ped != PedHandle.Invalid && Call<bool>(IS_PED_RAGDOLL, ped);

		public static WeaponHash CurrentWeapon(PedHandle ped) {
			uint w = 0;
			unsafe { Call(GET_CURRENT_PED_WEAPON, Self, new IntPtr(&w)); }
			return (WeaponHash)w;
		}
	}


}