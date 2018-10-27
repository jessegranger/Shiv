

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

namespace Shiv {

	public static partial class Globals {

		public static EntHandle[] NearbyObjects { get; internal set; } = new EntHandle[0];
		public static EntHandle[] NearbyPickups { get; internal set; } = new EntHandle[0];

		public static bool Exists(EntHandle ent) => ent == 0 ? false : Call<bool>(DOES_ENTITY_EXIST, ent);
		public static bool IsAlive(EntHandle ent) => ent == 0 ? false : Exists(ent) && !Call<bool>(IS_ENTITY_DEAD, ent);
		public static bool IsDead(EntHandle ent) => ent == 0 ? false : Exists(ent) && Call<bool>(IS_ENTITY_DEAD, ent);
		public static void Delete(EntHandle ent) {
			IsMissionEntity(ent, false);
			unsafe { Call(DELETE_ENTITY, new IntPtr(&ent)); }
		}
		public static void NotNeeded(EntHandle ent) {
			IsMissionEntity(ent, false);
			unsafe { Call(SET_ENTITY_AS_NO_LONGER_NEEDED, new IntPtr(&ent)); }
		}

		// flush this every frame
		internal static ConcurrentDictionary<EntHandle, Matrix4x4> MatrixCache = new ConcurrentDictionary<EntHandle, Matrix4x4>();
		internal static int MatrixCacheHits = 0;
		internal static int MatrixCacheMiss = 0;

		public static IntPtr Address(EntHandle ent) => GetEntityAddress((int)ent);
		public static Vector3 Position(EntHandle ent) => Position(Matrix(ent)); // Read<Vector3>(Address(ent), 0x90);
																																						// use inlines to make sure that Position() and friends all route to here
		public static Matrix4x4 Matrix(EntHandle ent) {
			if( MatrixCache.TryGetValue(ent, out Matrix4x4 ret) ) {
				MatrixCacheHits += 1;
				return ret;
			} else {
				MatrixCacheMiss += 1;
				return MatrixCache[ent] = Read<Matrix4x4>(Address(ent), 0x60);
			}
		}

		public static Matrix4x4 Matrix(EntHandle ent, BoneIndex bone) => Read<Matrix4x4>(GetEntityBoneMatrixAddress((int)ent, (uint)bone), 0x0);
		public static BoneIndex GetBoneIndex(EntHandle ent, string name) => Call<BoneIndex>(GET_ENTITY_BONE_INDEX_BY_NAME, ent, name);
		public static BoneIndex GetBoneIndex(EntHandle ent, Bone bone) => Call<BoneIndex>(GET_PED_BONE_INDEX, ent, bone);
		public static Vector3 Position(EntHandle ent, BoneIndex bone) => Call<Vector3>(GET_WORLD_POSITION_OF_ENTITY_BONE, ent, bone);
		public static Matrix4x4 Pose(EntHandle ent, BoneIndex bone) => Read<Matrix4x4>(GetEntityBonePoseAddress((int)ent, (uint)bone), 0x0);
		public static void Pose(EntHandle ent, BoneIndex bone, Matrix4x4 value) => Write(GetEntityBonePoseAddress((int)ent, (uint)bone), 0x0, value);


		public static float DistanceToSelf(EntHandle ent) => DistanceToSelf(Position(ent));
		public static float Heading(EntHandle ent) => ent == 0 ? 0 : Call<float>(GET_ENTITY_HEADING, ent);
		public static void Heading(EntHandle ent, float value) => Call(SET_ENTITY_HEADING, ent, value);


		public static ModelHash GetModel(EntHandle ent) => ent == 0 ? 0 : Call<ModelHash>(GET_ENTITY_MODEL, ent);
		public static bool IsValid(ModelHash model) => model == 0 ? false : Call<bool>(IS_MODEL_VALID, model);
		public static bool IsLoaded(ModelHash model) => model == 0 ? false : Call<bool>(HAS_MODEL_LOADED, model);
		public static AssetStatus RequestModel(VehicleHash model) => RequestModel((ModelHash)model);
		public static AssetStatus RequestModel(PedHash model) => RequestModel((ModelHash)model);
		public static AssetStatus RequestModel(ModelHash model) => (!IsValid(model))
				? AssetStatus.Invalid
				: Call<ModelHash>(REQUEST_MODEL, model) == model ? AssetStatus.Loaded : AssetStatus.Loading;

		public static bool IsBicycle(ModelHash m) => Call<bool>(IS_THIS_MODEL_A_BICYCLE, m);
		public static bool IsMotorbike(ModelHash m) => Call<bool>(IS_THIS_MODEL_A_BIKE, m);
		public static bool IsBoat(ModelHash m) => Call<bool>(IS_THIS_MODEL_A_BOAT, m);
		public static bool IsCar(ModelHash m) => Call<bool>(IS_THIS_MODEL_A_CAR, m);
		public static bool IsAmphibiousCar(ModelHash m) => Call<bool>((Hash)0x633F6F44A537EBB6, m);
		public static bool IsHeli(ModelHash m) => Call<bool>(IS_THIS_MODEL_A_HELI, m);
		public static bool IsPlane(ModelHash m) => Call<bool>(IS_THIS_MODEL_A_PLANE, m);
		public static bool IsTrain(ModelHash m) => Call<bool>(IS_THIS_MODEL_A_TRAIN, m);
		public static bool IsVehicle(ModelHash m) => Call<bool>(IS_MODEL_A_VEHICLE, m);
		public static void NotNeeded(ModelHash m) => Call(SET_MODEL_AS_NO_LONGER_NEEDED, m);

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

		public static Vector3 LeftPosition(EntHandle ent) {
			GetModelDimensions(GetModel(ent), out Vector3 backLeft, out Vector3 frontRight);
			return GetOffsetPosition(ent, new Vector3(backLeft.X, 0, 0));
		}

		public static Vector3 RightPosition(EntHandle ent) {
			GetModelDimensions(GetModel(ent), out Vector3 backLeft, out Vector3 frontRight);
			return GetOffsetPosition(ent, new Vector3(frontRight.X, 0, 0));
		}

		public static Vector3 FrontPosition(EntHandle ent) {
			GetModelDimensions(GetModel(ent), out Vector3 backLeft, out Vector3 frontRight);
			return GetOffsetPosition(ent, new Vector3(0, frontRight.Y, 0));
		}

		public static Vector3 RearPosition(EntHandle ent) {
			GetModelDimensions(GetModel(ent), out Vector3 backLeft, out Vector3 frontRight);
			return GetOffsetPosition(ent, new Vector3(0, backLeft.Y, 0));
		}

		public static Vector3 Velocity(EntHandle ent) => ent == 0 ? Vector3.Zero : Call<Vector3>(GET_ENTITY_VELOCITY, ent);
		public static void Velocity(EntHandle ent, Vector3 value) => Call(SET_ENTITY_VELOCITY, ent, value);

		public static float Speed(EntHandle ent) => ent == 0 ? 0f : Call<float>(GET_ENTITY_SPEED, ent);

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
		public static BlipColor GetColor(BlipHandle blip) => blip == 0 ? default : Call<BlipColor>(GET_BLIP_COLOUR, blip);
		public static IEnumerable<BlipHandle> GetAllBlips(BlipSprite type) {
			BlipHandle h = Call<BlipHandle>(GET_FIRST_BLIP_INFO_ID, type);
			while( Call<bool>(DOES_BLIP_EXIST, h) ) {
				yield return h;
				h = Call<BlipHandle>(GET_NEXT_BLIP_INFO_ID, type);
			}
		}

		public static void AttachTo(EntHandle a, EntHandle b, BoneIndex bone = BoneIndex.Invalid, Vector3 offset = default, Vector3 rot = default) => Call(ATTACH_ENTITY_TO_ENTITY, a, b, bone, offset, rot, 0, 0, 0, 0, 2, 1);
		public static bool IsAttached(EntHandle a) => Call<bool>(IS_ENTITY_ATTACHED, a);
		public static bool IsAttachedTo(EntHandle a, EntHandle b) => Call<bool>(IS_ENTITY_ATTACHED_TO_ENTITY, a, b);
		public static void Detach(EntHandle ent) => Call(DETACH_ENTITY, ent, true, true);
		public static EntHandle AttachedTo(EntHandle ent) => Call<EntHandle>(GET_ENTITY_ATTACHED_TO, ent);

		public static void ApplyForce(EntHandle ent, Vector3 dir, Vector3 rot, ForceType forceType = ForceType.MaxForceRot2, bool isRelative = false) => Call(APPLY_FORCE_TO_ENTITY, ent, forceType, dir, rot, false, isRelative, true, true, false, true);

		public static EntHandle CreateObject(ModelHash model, Vector3 pos, bool dynamic) {
			return RequestModel(model) != AssetStatus.Loaded
				? EntHandle.Invalid
				: Call<EntHandle>(CREATE_OBJECT_NO_OFFSET, model,
				pos.X, pos.Y, pos.Z, 1, 1, dynamic);
		}

		public static WeaponTint Tint(PedHandle ent, WeaponHash weap) => Call<WeaponTint>(GET_PED_WEAPON_TINT_INDEX, ent, weap);
		public static void Tint(PedHandle ent, WeaponHash weap, WeaponTint value) => Call<WeaponTint>(SET_PED_WEAPON_TINT_INDEX, ent, weap, value);

		public static WeaponGroup GetGroup(WeaponHash weap) => Call<WeaponGroup>(GET_WEAPONTYPE_GROUP, weap);
		public static ModelHash GetModel(WeaponHash weap) => Call<ModelHash>(GET_WEAPONTYPE_MODEL, weap);
		public static uint CurrentAmmo(PedHandle ent, WeaponHash weap) => Call<uint>(GET_AMMO_IN_PED_WEAPON, ent, weap);
		public static void CurrentAmmo(PedHandle ent, WeaponHash weap, uint value) => Call(SET_PED_AMMO, ent, weap, value);

		public static void AmmoInClip(PedHandle ent, WeaponHash weap, int value) => Call(SET_AMMO_IN_CLIP, ent, weap, value);
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
		///  Example: GiveWeapons( Self, Pistol, 50, Shotgun, 200 );
		/// </summary>
		public static void GiveWeapons(PedHandle ped, params uint[] items) {
			uint weapon = 0;
			for( int i = 0; i < items.Length; i++ ) {
				if( weapon == 0 )
					weapon = items[i];
				else {
					GiveWeapon(ped, (WeaponHash)weapon, items[i], i == items.Length - 1);
					weapon = 0;
				}
			}
		}
		public static void RemoveWeapons(PedHandle ped, params WeaponHash[] weapons) {
			if( weapons.Length == 0 ) {
				Call(REMOVE_ALL_PED_WEAPONS, ped);
			} else {
				foreach( WeaponHash w in weapons )
					Call(REMOVE_WEAPON_FROM_PED, ped, w);
			}
		}

		public static string Label(WeaponHash weap) {
			switch( weap ) {
				case WeaponHash.Pistol:
					return "WT_PIST";
				case WeaponHash.CombatPistol:
					return "WT_PIST_CBT";
				case WeaponHash.APPistol:
					return "WT_PIST_AP";
				case WeaponHash.SMG:
					return "WT_SMG";
				case WeaponHash.MicroSMG:
					return "WT_SMG_MCR";
				case WeaponHash.AssaultRifle:
					return "WT_RIFLE_ASL";
				case WeaponHash.CarbineRifle:
					return "WT_RIFLE_CBN";
				case WeaponHash.AdvancedRifle:
					return "WT_RIFLE_ADV";
				case WeaponHash.MG:
					return "WT_MG";
				case WeaponHash.CombatMG:
					return "WT_MG_CBT";
				case WeaponHash.PumpShotgun:
					return "WT_SG_PMP";
				case WeaponHash.SawnOffShotgun:
					return "WT_SG_SOF";
				case WeaponHash.AssaultShotgun:
					return "WT_SG_ASL";
				case WeaponHash.HeavySniper:
					return "WT_SNIP_HVY";
				case WeaponHash.SniperRifle:
					return "WT_SNIP_RIF";
				case WeaponHash.GrenadeLauncher:
					return "WT_GL";
				case WeaponHash.RPG:
					return "WT_RPG";
				case WeaponHash.Minigun:
					return "WT_MINIGUN";
				case WeaponHash.AssaultSMG:
					return "WT_SMG_ASL";
				case WeaponHash.BullpupShotgun:
					return "WT_SG_BLP";
				case WeaponHash.Pistol50:
					return "WT_PIST_50";
				case WeaponHash.Bottle:
					return "WT_BOTTLE";
				case WeaponHash.Gusenberg:
					return "WT_GUSENBERG";
				case WeaponHash.SNSPistol:
					return "WT_SNSPISTOL";
				case WeaponHash.VintagePistol:
					return "TT_VPISTOL";
				case WeaponHash.Dagger:
					return "WT_DAGGER";
				case WeaponHash.FlareGun:
					return "WT_FLAREGUN";
				case WeaponHash.Musket:
					return "WT_MUSKET";
				case WeaponHash.Firework:
					return "WT_FWRKLNCHR";
				case WeaponHash.MarksmanRifle:
					return "WT_HMKRIFLE";
				case WeaponHash.HeavyShotgun:
					return "WT_HVYSHOT";
				case WeaponHash.ProximityMine:
					return "WT_PRXMINE";
				case WeaponHash.HomingLauncher:
					return "WT_HOMLNCH";
				case WeaponHash.CombatPDW:
					return "WT_COMBATPDW";
				case WeaponHash.KnuckleDuster:
					return "WT_KNUCKLE";
				case WeaponHash.MarksmanPistol:
					return "WT_MKPISTOL";
				case WeaponHash.Machete:
					return "WT_MACHETE";
				case WeaponHash.MachinePistol:
					return "WT_MCHPIST";
				case WeaponHash.Flashlight:
					return "WT_FLASHLIGHT";
				case WeaponHash.DoubleBarrelShotgun:
					return "WT_DBSHGN";
				case WeaponHash.CompactRifle:
					return "WT_CMPRIFLE";
				case WeaponHash.SwitchBlade:
					return "WT_SWBLADE";
				case WeaponHash.Revolver:
					return "WT_REVOLVER";
			}
			return "";
		}

		private static IntPtr gpCamAddr = IntPtr.Zero;
		public static IntPtr Address(GameplayCam cam) => gpCamAddr == IntPtr.Zero ? gpCamAddr = GetGameplayCameraAddress() : gpCamAddr;
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

		private static uint CashKey() {
			switch( GetModel(Self) ) {
				case PedHash.Michael:
					return GenerateHash("SP0_TOTAL_CASH");
				case PedHash.Franklin:
					return GenerateHash("SP1_TOTAL_CASH");
				case PedHash.Trevor:
					return GenerateHash("SP2_TOTAL_CASH");
			}
			return 0;
		}
		public static int Money() {
			int money = 0;
			uint hash = CashKey();
			if( hash != 0 )
				unsafe { Call(STAT_GET_INT, hash, new IntPtr(&money), -1); }
			return money;
		}
		public static void Money(int value) {
			uint hash = CashKey();
			if( hash != 0 )
				Call(STAT_SET_INT, hash, value, 1);
		}

		public static uint WantedLevel() =>
			CurrentPlayer == PlayerHandle.Invalid ? 0 :
				Call<uint>(GET_PLAYER_WANTED_LEVEL, CurrentPlayer);

		public static void WantedLevel(uint value) {
			Call(SET_PLAYER_WANTED_LEVEL, CurrentPlayer, value, false);
			Call(SET_PLAYER_WANTED_LEVEL_NOW, CurrentPlayer, false);
		}

		public static bool AmAiming() => Call<bool>(IS_PLAYER_FREE_AIMING, CurrentPlayer);
		public static void AmClimbing() => Call(IS_PLAYER_CLIMBING, CurrentPlayer);

		public static bool AmInvincible() => Call<bool>(GET_PLAYER_INVINCIBLE, CurrentPlayer);
		public static void AmInvincible(bool value) => Call(SET_PLAYER_INVINCIBLE, CurrentPlayer, value);

		public static void IgnoredByPolice(bool value) => Call(SET_POLICE_IGNORE_PLAYER, CurrentPlayer, value);
		public static void IgnoredByEveryone(bool value) => Call(SET_EVERYONE_IGNORE_PLAYER, CurrentPlayer, value);
		public static void DispatchesCops(bool value) => Call(SET_DISPATCH_COPS_FOR_PLAYER, CurrentPlayer, value);

		public static bool CanControlCharacter() => Call<bool>(IS_PLAYER_CONTROL_ON, CurrentPlayer);
		public static void CanControlCharacter(bool value) => Call<bool>(SET_PLAYER_CONTROL, CurrentPlayer, value, 0);

		public static Vector3 WantedPosition() =>
			CurrentPlayer == PlayerHandle.Invalid ? Vector3.Zero :
				Call<Vector3>(GET_PLAYER_WANTED_CENTRE_POSITION, CurrentPlayer);

		public static EntHandle AimingAtEntity() {
			EntHandle t = 0;
			unsafe { Call<bool>(GET_ENTITY_PLAYER_IS_FREE_AIMING_AT, CurrentPlayer, new IntPtr(&t)); }
			return t;
		}
		public static bool IsAimingAtEntity(PedHandle ent) => ent == 0 ? false : Call<bool>(IS_PLAYER_FREE_AIMING_AT_ENTITY, CurrentPlayer, ent);


	}

}