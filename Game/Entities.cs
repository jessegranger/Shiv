﻿

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using GTA.Native;
using System.Numerics;
using static GTA.Native.MemoryAccess;
using static GTA.Native.Hash;
using static GTA.Native.Function;
using static Shiv.NativeMethods;
using System.Runtime.InteropServices;

namespace Shiv {

	public static partial class Global {

		private static int[] GetAllObjects(int max = 512) {
			int[] ret;
			unsafe {
				fixed ( int* buf = new int[max] ) {
					int count = WorldGetAllObjects(buf, max);
					ret = new int[count];
					Marshal.Copy(new IntPtr(buf), ret, 0, count);
				}
			}
			return ret;
		}
		public static readonly Func<EntHandle[]> NearbyObjects = Throttle(307, () =>
			GetAllObjects()
				.Cast<EntHandle>()
				.Where(Exists)
				.OrderBy(DistanceToSelf)
				.ToArray()
		);
		private static int[] GetAllPickups(uint max = 512) {
			int[] ret;
			unsafe {
				fixed ( int* buf = new int[max] ) {
					int count = WorldGetAllPickups(buf, (int)max);
					ret = new int[count];
					Marshal.Copy(new IntPtr(buf), ret, 0, count);
				}
			}
			return ret;
		}
		public static readonly Func<EntHandle[]> NearbyPickups = Throttle(709, () =>
			GetAllPickups()
				.Cast<EntHandle>()
				.Where(Exists)
				.OrderBy(DistanceToSelf)
				.ToArray()
		);


		public static bool IsAlive(EntHandle ent) => ent == 0 ? false : Exists(ent) && !Call<bool>(IS_ENTITY_DEAD, ent);
		public static bool IsDead(EntHandle ent) => ent == 0 ? false : Exists(ent) && Call<bool>(IS_ENTITY_DEAD, ent);
		public static void Delete(VehicleHandle ped) => Delete((EntHandle)ped);
		public static void Delete(PedHandle ped) => Delete((EntHandle)ped);
		public static void Delete(EntHandle ent) {
			IsMissionEntity(ent, false);
			unsafe { Call(DELETE_ENTITY, new IntPtr(&ent)); }
		}
		public static void NotNeeded(EntHandle ent) {
			IsMissionEntity(ent, false);
			unsafe { Call(SET_ENTITY_AS_NO_LONGER_NEEDED, new IntPtr(&ent)); }
		}

		public static EntityType GetEntityType(EntHandle ent) => Call<EntityType>(GET_ENTITY_TYPE, ent);

		public static bool Exists(EntHandle ent) => ent == 0 ? false : Call<bool>(DOES_ENTITY_EXIST, ent);
		public static bool Exists(PedHandle ent) => ent == 0 ? false : Call<bool>(DOES_ENTITY_EXIST, ent);
		public static bool Exists(VehicleHandle ent) => ent == 0 ? false : Call<bool>(DOES_ENTITY_EXIST, ent);

		public static IntPtr Address(EntHandle ent) => GetEntityAddress((int)ent);
		public static IntPtr Address(PedHandle ent) => GetEntityAddress((int)ent);
		public static IntPtr Address(VehicleHandle ent) => GetEntityAddress((int)ent);

		public static Vector3 Position(EntHandle ent) => Position(Matrix(ent)); // Read<Vector3>(Address(ent), 0x90);
		public static Vector3 Position(PedHandle ent) => Position(Matrix(ent)); // Read<Vector3>(Address(ent), 0x90);
		public static Vector3 Position(VehicleHandle ent) => Position(Matrix(ent)); // Read<Vector3>(Address(ent), 0x90);

		public static Matrix4x4 Matrix(EntHandle ent) => Read<Matrix4x4>(Address(ent), 0x60);
		public static Matrix4x4 Matrix(PedHandle ent) => Read<Matrix4x4>(Address(ent), 0x60);
		public static Matrix4x4 Matrix(VehicleHandle ent) => Read<Matrix4x4>(Address(ent), 0x60);

		public static BoneIndex GetBoneIndex(EntHandle ent, string name) => Call<BoneIndex>(GET_ENTITY_BONE_INDEX_BY_NAME, ent, name);
		public static BoneIndex GetBoneIndex(EntHandle ent, Bone bone) => Call<BoneIndex>(GET_PED_BONE_INDEX, ent, bone);
		public static Vector3 Position(EntHandle ent, BoneIndex bone) => Call<Vector3>(GET_WORLD_POSITION_OF_ENTITY_BONE, ent, bone);

		public static float DistanceToSelf(EntHandle ent) => DistanceToSelf(Position(ent));
		public static float DistanceToSelf(PedHandle ent) => DistanceToSelf(Position(ent));
		public static float Heading(EntHandle ent) => ent == 0 ? 0 : Call<float>(GET_ENTITY_HEADING, ent);
		public static void Heading(EntHandle ent, float value) => Call(SET_ENTITY_HEADING, ent, value);

		public static bool IsFacing(EntHandle ent, Vector3 pos) => IsFacing(Matrix(ent), pos);
		public static bool IsFacing(Matrix4x4 m, Vector3 pos) => Vector3.Dot(pos - Position(m), Forward(m)) > 0.0f;

		public static ModelHash GetModel(EntHandle ent) => ent == 0 ? 0 : Call<ModelHash>(GET_ENTITY_MODEL, ent);
		public static bool IsValid(ModelHash model) => model == ModelHash.Invalid ? false : Call<bool>(IS_MODEL_VALID, model);
		public static bool IsValid(VehicleHash model) => model == VehicleHash.Invalid ? false : Call<bool>(IS_MODEL_VALID, model);
		public static bool IsValid(PedHash model) => model == PedHash.Invalid ? false : Call<bool>(IS_MODEL_VALID, model);
		public static bool IsLoaded(ModelHash model) => model == 0 ? false : Call<bool>(HAS_MODEL_LOADED, model);
		public static bool IsLoaded(VehicleHash model) => model == 0 ? false : Call<bool>(HAS_MODEL_LOADED, model);
		public static bool IsLoaded(PedHash model) => model == 0 ? false : Call<bool>(HAS_MODEL_LOADED, model);
		public static AssetStatus RequestModel(VehicleHash model) => RequestModel((ModelHash)model);
		public static AssetStatus RequestModel(PedHash model) => RequestModel((ModelHash)model);
		public static AssetStatus RequestModel(ModelHash model) => (!IsValid(model))
				? AssetStatus.Invalid
				: Call<ModelHash>(REQUEST_MODEL, model) == model ? AssetStatus.Loaded : AssetStatus.Loading;

		public static bool IsBicycle(ModelHash m) => Call<bool>(IS_THIS_MODEL_A_BICYCLE, m);
		public static bool IsBicycle(VehicleHash m) => Call<bool>(IS_THIS_MODEL_A_BICYCLE, m);
		public static bool IsMotorbike(ModelHash m) => Call<bool>(IS_THIS_MODEL_A_BIKE, m);
		public static bool IsMotorbike(VehicleHash m) => Call<bool>(IS_THIS_MODEL_A_BIKE, m);
		public static bool IsBoat(ModelHash m) => Call<bool>(IS_THIS_MODEL_A_BOAT, m);
		public static bool IsBoat(VehicleHash m) => Call<bool>(IS_THIS_MODEL_A_BOAT, m);
		public static bool IsCar(ModelHash m) => Call<bool>(IS_THIS_MODEL_A_CAR, m);
		public static bool IsCar(VehicleHash m) => Call<bool>(IS_THIS_MODEL_A_CAR, m);
		public static bool IsAmphibiousCar(ModelHash m) => Call<bool>((Hash)0x633F6F44A537EBB6, m);
		public static bool IsAmphibiousCar(VehicleHash m) => Call<bool>((Hash)0x633F6F44A537EBB6, m);
		public static bool IsHeli(VehicleHash m) => Call<bool>(IS_THIS_MODEL_A_HELI, m);
		public static bool IsHeli(ModelHash m) => Call<bool>(IS_THIS_MODEL_A_HELI, m);
		public static bool IsPlane(ModelHash m) => Call<bool>(IS_THIS_MODEL_A_PLANE, m);
		public static bool IsPlane(VehicleHash m) => Call<bool>(IS_THIS_MODEL_A_PLANE, m);
		public static bool IsTrain(ModelHash m) => Call<bool>(IS_THIS_MODEL_A_TRAIN, m);
		public static bool IsTrain(VehicleHash m) => Call<bool>(IS_THIS_MODEL_A_TRAIN, m);
		public static bool IsVehicle(ModelHash m) => Call<bool>(IS_MODEL_A_VEHICLE, m);
		public static void NotNeeded(ModelHash m) => Call(SET_MODEL_AS_NO_LONGER_NEEDED, m);
		public static void NotNeeded(VehicleHash m) => Call(SET_MODEL_AS_NO_LONGER_NEEDED, m);

		private struct ModelDims {
			public Vector3 backLeft;
			public Vector3 frontRight;
		}
		private static ConcurrentDictionary<ModelHash, ModelDims> modelDimensionCache = new ConcurrentDictionary<ModelHash, ModelDims>();
		public static void GetModelDimensions(ModelHash model, out Vector3 backLeft, out Vector3 frontRight) {
			if( modelDimensionCache.TryGetValue(model, out ModelDims dims) ) {
				backLeft = dims.backLeft;
				frontRight = dims.frontRight;
				return;
			}
			NativeVector3 a, b;
			unsafe { Call(GET_MODEL_DIMENSIONS, model, new IntPtr(&a), new IntPtr(&b)); }
			backLeft = a;
			frontRight = b;
			modelDimensionCache.TryAdd(model, new ModelDims() { backLeft = a, frontRight = b });
		}

		public static Vector3 GetOffsetPosition(EntHandle ent, Vector3 offset) => GetOffsetPosition(Matrix(ent), offset);
		// ent == 0 ? Vector3.Zero : Call<Vector3>(GET_OFFSET_FROM_ENTITY_IN_WORLD_COORDS, ent, offset);

		/// <summary>  Expensive. </summary>
		public static Vector3 GetPositionOffset(EntHandle ent, Vector3 pos) => GetPositionOffset(Matrix(ent), pos);
		// ent == 0 ? Vector3.Zero : Call<Vector3>(GET_OFFSET_FROM_ENTITY_GIVEN_WORLD_COORDS, ent, pos);

		public static Vector3 Velocity(EntHandle ent) => ent == 0 ? Vector3.Zero : Call<Vector3>(GET_ENTITY_VELOCITY, ent);
		public static void Velocity(EntHandle ent, Vector3 value) => Call(SET_ENTITY_VELOCITY, ent, value);
		public static Vector3 Velocity(PedHandle ent) => ent == 0 ? Vector3.Zero : Call<Vector3>(GET_ENTITY_VELOCITY, ent);
		public static void Velocity(PedHandle ent, Vector3 value) => Call(SET_ENTITY_VELOCITY, ent, value);
		public static Vector3 Velocity(VehicleHandle ent) => ent == 0 ? Vector3.Zero : Call<Vector3>(GET_ENTITY_VELOCITY, ent);
		public static void Velocity(VehicleHandle ent, Vector3 value) => Call(SET_ENTITY_VELOCITY, ent, value);

		public static Vector3 Rotation(EntHandle ent) => ent == 0 ? Vector3.Zero : Call<Vector3>(GET_ENTITY_ROTATION, ent, 0);
		public static void Rotation(EntHandle ent, Vector3 rot) => Call<Vector3>(SET_ENTITY_ROTATION, ent, rot, 0, true);

		public static Vector3 RotationVelocity(EntHandle ent) => ent == 0 ? Vector3.Zero : Call<Vector3>(GET_ENTITY_ROTATION_VELOCITY, ent);

		public static float Speed(EntHandle ent) => ent == 0 ? 0f : Call<float>(GET_ENTITY_SPEED, ent);
		public static float Speed(PedHandle ent) => ent == 0 ? 0f : Call<float>(GET_ENTITY_SPEED, ent);
		public static float Speed(VehicleHandle ent) => ent == 0 ? 0f : Call<float>(GET_ENTITY_SPEED, ent);

		public static void MaxSpeed(EntHandle ent, float value) => Call(SET_ENTITY_MAX_SPEED, ent, value);

		public static bool IsFrozen(EntHandle ent) => IsBitSet(Address(ent), 0x2E, 1);
		public static void IsFrozen(EntHandle ent, bool value) => Call(FREEZE_ENTITY_POSITION, ent, value);

		public static bool HasGravity(EntHandle ent) => IsBitSet(ReadPtr(Address(ent), 48), +26, 4);
		public static void HasGravity(EntHandle ent, bool value) => Call(SET_ENTITY_HAS_GRAVITY, ent, value);

		public static bool IsVisible(EntHandle ent) => ent == 0 ? false : Call<bool>(IS_ENTITY_VISIBLE, ent);
		public static void IsVisible(EntHandle ent, bool value) => Call(SET_ENTITY_VISIBLE, ent, value);

		public static bool IsRendered(EntHandle ent) => ent == 0 ? false : IsBitSet(Address(ent), 176, 4);

		public static bool IsUpsideDown(EntHandle ent) => ent == 0 ? false : Call<bool>(IS_ENTITY_UPSIDEDOWN, ent);

		public static bool IsInAir(EntHandle ent) => ent == 0 ? false : Call<bool>(IS_ENTITY_IN_AIR, ent);
		public static bool IsInWater(EntHandle ent) => ent == 0 ? false : Call<bool>(IS_ENTITY_IN_WATER, ent);

		public static bool IsMissionEntity(EntHandle ent) => ent == 0 ? false : Call<bool>(IS_ENTITY_A_MISSION_ENTITY, ent);
		public static void IsMissionEntity(EntHandle ent, bool value) => Call(SET_ENTITY_AS_MISSION_ENTITY, ent, value);

		public static bool IsBulletProof(EntHandle ent) => ent == 0 ? false : IsBitSet(Address(ent), 392, 4);
		public static void IsBulletProof(EntHandle ent, bool value) => SetBit(Address(ent), 392, 4, value);

		public static bool IsFireProof(EntHandle ent) => ent == 0 ? false : IsBitSet(Address(ent), 392, 5);
		public static void IsFireProof(EntHandle ent, bool value) => SetBit(Address(ent), 392, 5, value);

		public static bool IsMeleeProof(EntHandle ent) => ent == 0 ? false : IsBitSet(Address(ent), 392, 7);
		public static void IsMeleeProof(EntHandle ent, bool value) => SetBit(Address(ent), 392, 7, value);

		public static bool IsExplosionProof(EntHandle ent) => ent == 0 ? false : IsBitSet(Address(ent), 392, 11);
		public static void IsExplosionProof(EntHandle ent, bool value) => SetBit(Address(ent), 392, 11, value);

		public static bool IsCollisionProof(EntHandle ent) => ent == 0 ? false : IsBitSet(Address(ent), 392, 6);
		public static void IsCollisionProof(EntHandle ent, bool value) => SetBit(Address(ent), 392, 6, value);

		public static bool IsInvincible(EntHandle ent) => ent == 0 ? false : IsBitSet(Address(ent), 392, 8);
		public static void IsInvincible(EntHandle ent, bool value) => Call(SET_ENTITY_INVINCIBLE, ent, value);

		public static bool Exists(BlipHandle blip) => Call<bool>(DOES_BLIP_EXIST, blip);
		public static EntHandle GetEntity(BlipHandle blip) => Call<EntHandle>(GET_BLIP_INFO_ID_ENTITY_INDEX, blip);
		public static BlipHandle GetBlip(EntHandle ent) => Call<BlipHandle>(GET_BLIP_FROM_ENTITY, ent);
		public static BlipHandle GetBlip(PedHandle ent) => Call<BlipHandle>(GET_BLIP_FROM_ENTITY, ent);
		public static BlipHandle GetBlip(VehicleHandle ent) => Call<BlipHandle>(GET_BLIP_FROM_ENTITY, ent);
		public static void ShowRoute(BlipHandle blip, bool value) => Call(SET_BLIP_ROUTE, blip, value);
		public static bool IsFlashing(BlipHandle blip) => Call<bool>(IS_BLIP_FLASHING, blip);
		public static void IsFlashing(BlipHandle blip, bool value) => Call(SET_BLIP_FLASHES, blip, value);
		public static void Delete(BlipHandle blip) { unsafe { Call(REMOVE_BLIP, new IntPtr(&blip)); } }
		public static void Name(BlipHandle blip, string value) {
			Call(BEGIN_TEXT_COMMAND_SET_BLIP_NAME, PinnedString.STRING);
			Call(ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, value);
			Call(END_TEXT_COMMAND_SET_BLIP_NAME, blip);
		}
		public static int Alpha(BlipHandle blip) => Call<int>(GET_BLIP_ALPHA, blip);
		public static void Alpha(BlipHandle blip, int value) => Call(SET_BLIP_ALPHA, blip, value);
		public static void Priority(BlipHandle blip, int value) => Call(SET_BLIP_PRIORITY, blip, value);
		public static void Label(BlipHandle blip, int value) => Call(SHOW_NUMBER_ON_BLIP, blip, value);
		public static Vector3 Position(BlipHandle blip) => Call<Vector3>(GET_BLIP_INFO_ID_COORD, blip);
		public static void Position(BlipHandle blip, Vector3 pos) => Call(SET_BLIP_COORDS, blip, pos);
		public static void Rotation(BlipHandle blip, int value) => Call(SET_BLIP_ROTATION, blip, value);
		public static void Scale(BlipHandle blip, float value) => Call(SET_BLIP_SCALE, blip, value);
		public static BlipHUDColor GetBlipHUDColor(BlipHandle blip) => Call<BlipHUDColor>(GET_BLIP_HUD_COLOUR, blip);
		public static BlipColor GetBlipColor(BlipHandle blip) => Call<BlipColor>(GET_BLIP_COLOUR, blip);
		public static Color GetColor(BlipHandle blip) => blip == BlipHandle.Invalid ? (default) : GetColor(GetBlipHUDColor(blip));
		public static Color GetColor(BlipHUDColor color) {
			switch( color ) {
				case BlipHUDColor.Blue: return Color.Blue;
				case BlipHUDColor.Red: return Color.Red;
				case BlipHUDColor.Yellow: return Color.Yellow;
				case BlipHUDColor.Green: return Color.Green;
				default: return Color.White;
			}
		}

		public static IEnumerable<EntHandle> Where(this IEnumerable<EntHandle> list, Color blipColor) => list.Where(x => GetColor(GetBlip(x)) == blipColor);
		public static IEnumerable<PedHandle> Where(this IEnumerable<PedHandle> list, Color blipColor) => list.Where(x => GetColor(GetBlip(x)) == blipColor);
		public static IEnumerable<VehicleHandle> Where(this IEnumerable<VehicleHandle> list, Color blipColor) => list.Where(x => GetColor(GetBlip(x)) == blipColor);

		public static IEnumerable<EntHandle> Where(this IEnumerable<EntHandle> list, BlipHUDColor blipColor) => list.Where(x => GetBlipHUDColor(GetBlip(x)) == blipColor);
		public static IEnumerable<PedHandle> Where(this IEnumerable<PedHandle> list, BlipHUDColor blipColor) => list.Where(x => GetBlipHUDColor(GetBlip(x)) == blipColor);
		public static IEnumerable<VehicleHandle> Where(this IEnumerable<VehicleHandle> list, BlipHUDColor blipColor) => list.Where(x => GetBlipHUDColor(GetBlip(x)) == blipColor);

		public static IEnumerable<BlipHandle> Where(this IEnumerable<BlipHandle> list, BlipHUDColor blipColor) => list.Where(x => GetBlipHUDColor(x) == blipColor);

		public static bool Any(this IEnumerable<EntHandle> list, Color blipColor) => list.Any(x => GetColor(GetBlip(x)) == blipColor);
		public static bool Any(this IEnumerable<EntHandle> list, BlipHUDColor blipColor) => list.Any(x => GetBlipHUDColor(GetBlip(x)) == blipColor);
		public static bool Any(this IEnumerable<BlipHandle> list, BlipHUDColor blipColor) => list.Any(x => GetBlipHUDColor(x) == blipColor);

		public static EntHandle FirstOrDefault(this IEnumerable<EntHandle> list, Color blipColor) => list.FirstOrDefault(x => GetColor(GetBlip(x)) == blipColor);
		public static PedHandle FirstOrDefault(this IEnumerable<PedHandle> list, Color blipColor) => list.FirstOrDefault(x => GetColor(GetBlip(x)) == blipColor);
		public static VehicleHandle FirstOrDefault(this IEnumerable<VehicleHandle> list, Color blipColor) => list.FirstOrDefault(x => GetColor(GetBlip(x)) == blipColor);
		public static BlipHandle FirstOrDefault(this IEnumerable<BlipHandle> list, Color blipColor) => list.FirstOrDefault(x => GetColor(x) == blipColor);

		public static EntHandle FirstOrDefault(this IEnumerable<EntHandle> list, BlipHUDColor blipColor) => list.FirstOrDefault(x => GetBlipHUDColor(GetBlip(x)) == blipColor);
		public static PedHandle FirstOrDefault(this IEnumerable<PedHandle> list, BlipHUDColor blipColor) => list.FirstOrDefault(x => GetBlipHUDColor(GetBlip(x)) == blipColor);
		public static VehicleHandle FirstOrDefault(this IEnumerable<VehicleHandle> list, BlipHUDColor blipColor) => list.FirstOrDefault(x => GetBlipHUDColor(GetBlip(x)) == blipColor);
		public static BlipHandle FirstOrDefault(this IEnumerable<BlipHandle> list, BlipHUDColor blipColor) => list.FirstOrDefault(x => GetBlipHUDColor(x) == blipColor);

		public static BlipHandle First(IEnumerable<BlipHandle> list, BlipHUDColor blipColor) => list.FirstOrDefault(x => GetBlipHUDColor(x) == blipColor);

		public static IEnumerable<BlipHandle> GetAllBlips() => GetAllBlips(BlipSprite.Standard);
		public static IEnumerable<BlipHandle> GetAllBlips(BlipSprite type) {
			BlipHandle h = Call<BlipHandle>(GET_FIRST_BLIP_INFO_ID, type);
			while( Exists(h) ) {
				yield return h;
				h = Call<BlipHandle>(GET_NEXT_BLIP_INFO_ID, type);
			}
		}
		public static bool TryGetBlip(BlipSprite type, out BlipHandle blip) => (blip = GetAllBlips(type).FirstOrDefault()) != default;
		public static bool TryGetBlip(BlipHUDColor color, out BlipHandle blip) => (blip = GetAllBlips(BlipSprite.Standard).Where(color).FirstOrDefault()) != default;

		public static void AttachTo(EntHandle a, EntHandle b, BoneIndex bone = BoneIndex.Invalid, Vector3 offset = default, Vector3 rot = default) => Call(ATTACH_ENTITY_TO_ENTITY, a, b, bone, offset, rot, 0, 0, 0, 0, 2, 1);
		public static bool IsAttached(EntHandle a) => Call<bool>(IS_ENTITY_ATTACHED, a);
		public static bool IsAttachedTo(EntHandle a, EntHandle b) => Call<bool>(IS_ENTITY_ATTACHED_TO_ENTITY, a, b);
		public static void Detach(EntHandle ent) => Call(DETACH_ENTITY, ent, true, true);
		public static EntHandle AttachedTo(EntHandle ent) => Call<EntHandle>(GET_ENTITY_ATTACHED_TO, ent);

		public static void ApplyForce(EntHandle ent, Vector3 dir, Vector3 rot, ForceType forceType = ForceType.MaxForceRot2, bool isRelative = false) => Call(APPLY_FORCE_TO_ENTITY, ent, forceType, dir, rot, false, isRelative, true, true, false, true);

		public static EntHandle CreateObject(ModelHash model, Vector3 pos, bool dynamic) {
			return RequestModel(model) != AssetStatus.Loaded
				? EntHandle.Invalid
				: Call<EntHandle>(CREATE_OBJECT_NO_OFFSET, model, pos, 1, 1, dynamic);
		}

		public static WeaponTint Tint(PedHandle ent, WeaponHash weap) => Call<WeaponTint>(GET_PED_WEAPON_TINT_INDEX, ent, weap);
		public static void Tint(PedHandle ent, WeaponHash weap, WeaponTint value) => Call<WeaponTint>(SET_PED_WEAPON_TINT_INDEX, ent, weap, value);

		public static WeaponGroup GetGroup(WeaponHash weap) => Call<WeaponGroup>(GET_WEAPONTYPE_GROUP, weap);
		public static ModelHash GetModel(WeaponHash weap) => Call<ModelHash>(GET_WEAPONTYPE_MODEL, weap);
		public static uint CurrentAmmo(PedHandle ent, WeaponHash weap) => Call<uint>(GET_AMMO_IN_PED_WEAPON, ent, weap);
		public static void CurrentAmmo(PedHandle ent, WeaponHash weap, uint value) => Call(SET_PED_AMMO, ent, weap, value);

		/// <summary>
		/// Set the number of bullets in the current clip.
		/// </summary>
		public static void AmmoInClip(PedHandle ent, WeaponHash weap, uint value) => Call(SET_AMMO_IN_CLIP, ent, weap, value);
		/// <summary>
		/// The number of bullets in the current clip. When zero, reload is required.
		/// </summary>
		public static uint AmmoInClip(PedHandle ent, WeaponHash weap) {
			uint ammo = 0;
			unsafe { Call<uint>(GET_AMMO_IN_CLIP, ent, weap, new IntPtr(&ammo)); }
			return ammo;
		}
		public static uint MaxAmmo(PedHandle ent, WeaponHash weap) => Call<uint>(GET_MAX_AMMO, ent, weap);
		public static uint MaxAmmoInClip(PedHandle ent, WeaponHash weap) => Call<uint>(GET_MAX_AMMO_IN_CLIP, ent, weap);

		public static void InfiniteAmmo(PedHandle ent, bool value) => Call(SET_PED_INFINITE_AMMO_CLIP, ent, value);

		public static void GiveWeapon(PedHandle ped, WeaponHash w, uint ammo, bool equip) {
			Call(GIVE_WEAPON_TO_PED, ped, w, 0, false, equip);
			CurrentAmmo(ped, w, ammo);
		}

		/// <summary>
		///  Example: GiveWeapons( Self, Pistol, 50, Shotgun, 200, Unarmed, 0 );
		///  The last weapon in the list will be equipped.
		/// </summary>
		public static void GiveWeapons(PedHandle ped, params uint[] items) {
			uint weapon = 0;
			for( int i = 0; i < items.Length; i++ ) {
				if( weapon == 0 ) {
					weapon = items[i];
				} else {
					GiveWeapon(ped, (WeaponHash)weapon, items[i], i == items.Length - 1);
					weapon = 0;
				}
			}
		}

		public static void RemoveWeapons(PedHandle ped, params WeaponHash[] weapons) {
			if( weapons.Length == 0 ) {
				Call(REMOVE_ALL_PED_WEAPONS, ped);
			} else {
				foreach( WeaponHash w in weapons ) {
					Call(REMOVE_WEAPON_FROM_PED, ped, w);
				}
			}
		}

		private static Dictionary<WeaponHash, string> weaponLabels = new Dictionary<WeaponHash, string>() {
			{ WeaponHash.Pistol, "WT_PIST" },
			{ WeaponHash.CombatPistol, "WT_PIST_CBT" },
			{ WeaponHash.APPistol, "WT_PIST_AP" },
			{ WeaponHash.SMG, "WT_SMG" },
			{ WeaponHash.MicroSMG, "WT_SMG_MCR" },
			{ WeaponHash.AssaultRifle, "WT_RIFLE_ASL" },
			{ WeaponHash.CarbineRifle, "WT_RIFLE_CBN" },
			{ WeaponHash.AdvancedRifle, "WT_RIFLE_ADV" },
			{ WeaponHash.MG, "WT_MG" },
				{ WeaponHash.CombatMG, "WT_MG_CBT" },
				{ WeaponHash.PumpShotgun, "WT_SG_PMP" },
				{ WeaponHash.SawnOffShotgun, "WT_SG_SOF" },
				{ WeaponHash.AssaultShotgun, "WT_SG_ASL" },
				{ WeaponHash.HeavySniper, "WT_SNIP_HVY" },
				{ WeaponHash.SniperRifle, "WT_SNIP_RIF" },
				{ WeaponHash.GrenadeLauncher, "WT_GL" },
				{ WeaponHash.RPG, "WT_RPG" },
				{ WeaponHash.Minigun, "WT_MINIGUN" },
				{ WeaponHash.AssaultSMG, "WT_SMG_ASL" },
				{ WeaponHash.BullpupShotgun, "WT_SG_BLP" },
				{ WeaponHash.Pistol50, "WT_PIST_50" },
				{ WeaponHash.Bottle, "WT_BOTTLE" },
				{ WeaponHash.Gusenberg, "WT_GUSENBERG" },
				{ WeaponHash.SNSPistol, "WT_SNSPISTOL" },
				{ WeaponHash.VintagePistol, "TT_VPISTOL" },
				{ WeaponHash.Dagger, "WT_DAGGER" },
				{ WeaponHash.FlareGun, "WT_FLAREGUN" },
				{ WeaponHash.Musket, "WT_MUSKET" },
				{ WeaponHash.Firework, "WT_FWRKLNCHR" },
				{ WeaponHash.MarksmanRifle, "WT_HMKRIFLE" },
				{ WeaponHash.HeavyShotgun, "WT_HVYSHOT" },
				{ WeaponHash.ProximityMine, "WT_PRXMINE" },
				{ WeaponHash.HomingLauncher, "WT_HOMLNCH" },
				{ WeaponHash.CombatPDW, "WT_COMBATPDW" },
				{ WeaponHash.KnuckleDuster, "WT_KNUCKLE" },
				{ WeaponHash.MarksmanPistol, "WT_MKPISTOL" },
				{ WeaponHash.Machete, "WT_MACHETE" },
				{ WeaponHash.MachinePistol, "WT_MCHPIST" },
				{ WeaponHash.Flashlight, "WT_FLASHLIGHT" },
				{ WeaponHash.DoubleBarrelShotgun, "WT_DBSHGN" },
				{ WeaponHash.CompactRifle, "WT_CMPRIFLE" },
				{ WeaponHash.SwitchBlade, "WT_SWBLADE" },
				{ WeaponHash.Revolver, "WT_REVOLVER" },
		};

		public static string Label(WeaponHash weap) => weaponLabels.TryGetValue(weap, out string ret) ? ret : "";

		public static IntPtr Address(GameplayCam cam) => GetGameplayCameraAddress();
		public static Matrix4x4 Matrix(GameplayCam cam) => Read<Matrix4x4>(Address(cam), 0x1F0);

		public static IntPtr Address(CheckpointHandle cp) => GetCheckpointAddress((int)cp);
		public static Vector3 Position(CheckpointHandle cp) => Read<Vector3>(Address(cp), 0x0);
		public static void Position(CheckpointHandle cp, Vector3 value) => Write(Address(cp), 0x0, value);
		public static Vector3 Target(CheckpointHandle cp) => Read<Vector3>(Address(cp), 0x10);
		public static void Target(CheckpointHandle cp, Vector3 value) => Write(Address(cp), 0x10, value);
		public static float Radius(CheckpointHandle cp) => Read<float>(Address(cp), 0x3C);
		public static void Radius(CheckpointHandle cp, float value) => Write(Address(cp), 0x3C, value);
		public static Color GetColor(CheckpointHandle cp) => Color.FromArgb(Read<int>(Address(cp), 0x50));
		public static Color GetIconColor(CheckpointHandle cp) => Color.FromArgb(Read<int>(Address(cp), 0x54));
		public static IEnumerable<CheckpointHandle> GetCheckpoints() => GetCheckpointHandles().Cast<CheckpointHandle>();




	}

}