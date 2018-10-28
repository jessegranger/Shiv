using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using GTA.Native;
using System.Numerics;
using static GTA.Native.MemoryAccess;
using static GTA.Native.Function;
using static GTA.Native.Hash;

namespace Shiv {

	public static partial class Globals {
		public static VehicleHandle[] NearbyVehicles { get; internal set; } = new VehicleHandle[0];

		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool Exists(VehicleHandle ent) => Exists((EntHandle)ent);
		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static IntPtr Address(VehicleHandle v) => Address((EntHandle)v);
		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector3 Position(VehicleHandle ent) => Position(Matrix((EntHandle)ent));
		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static Matrix4x4 Matrix(VehicleHandle ent) => Matrix((EntHandle)ent);
		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static float DistanceToSelf(VehicleHandle ent) => DistanceToSelf(Position(ent));
		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static float Heading(VehicleHandle ent) => Heading((EntHandle)ent);
		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static void Heading(VehicleHandle ent, float value) => Heading((EntHandle)ent, value);
		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static ModelHash GetModel(VehicleHandle ent) => GetModel((EntHandle)ent);
		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector3 LeftPosition(VehicleHandle ent) => LeftPosition((EntHandle)ent);
		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector3 RightPosition(VehicleHandle ent) => RightPosition((EntHandle)ent);
		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector3 FrontPosition(VehicleHandle ent) => FrontPosition((EntHandle)ent);
		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector3 RearPosition(VehicleHandle ent) => RearPosition((EntHandle)ent);

		public static float Fuel(VehicleHandle ent) => Read<float>(Address(ent), 0x7F4);
		public static void Fuel(VehicleHandle ent, float value) => Write(Address(ent), 0x7F4, value);
		public static float MaxFuel(VehicleHandle ent) => Read<float>(Address(ent), 0x100);

		public static float Oil(VehicleHandle ent) => Read<float>(Address(ent), 0x7E8);
		public static void Oil(VehicleHandle ent, float value) => Write(Address(ent), 0x7E8, value);
		public static float MaxOil(VehicleHandle ent) => Read<float>(Address(ent), 0x104);

		public static float Gravity(VehicleHandle ent) => Read<float>(Address(ent), 0xBCC);
		public static void Gravity(VehicleHandle ent, float value) => Write(Address(ent), 0xBCC, value);

		public static float CurrentRPM(VehicleHandle ent) => Read<float>(Address(ent), 0x864);
		public static void CurrentRPM(VehicleHandle ent, float value) => Write(Address(ent), 0x864, value);

		public static float Turbo(VehicleHandle ent) => Read<float>(Address(ent), 0x888);
		public static void Turbo(VehicleHandle ent, float value) => Write(Address(ent), 0x888, value);

		public static uint WheelCount(VehicleHandle ent) => Read<uint>(Address(ent), 0xB18);

		public static VehicleHandle CreateVehicle(ModelHash model, Vector3 pos, float heading) => CreateVehicle((VehicleHash)model, pos, heading);
		public static VehicleHandle CreateVehicle(VehicleHash model, Vector3 pos, float heading) {
			switch( RequestModel(model) ) {
				case AssetStatus.Invalid: return VehicleHandle.Invalid;
				case AssetStatus.Loading: return VehicleHandle.ModelLoading;
				default: return Call<VehicleHandle>(CREATE_VEHICLE, model, pos.X, pos.Y, pos.Z, heading, true, true);
			}
		}

		public static Vector3 GetClosestVehicleNode(Vector3 pos, RoadType type) {
			var ret = new NativeVector3();
			unsafe {
				Call<bool>(GET_CLOSEST_VEHICLE_NODE,
					PlayerPosition.X, PlayerPosition.Y, PlayerPosition.Z,
					new IntPtr(&ret),
					type,
					3.0f,
					0);
			}
			return ret;
		}

		public static bool IsInVehicle(PedHandle ent) => ent == PedHandle.Invalid ? false : Call<bool>(IS_PED_IN_ANY_VEHICLE, ent, 0);

		public static VehicleHandle CurrentVehicle(PedHandle ped) => ped == PedHandle.Invalid ? VehicleHandle.Invalid :
			Call<VehicleHandle>(GET_VEHICLE_PED_IS_IN, ped, false);

		public static float Speed(VehicleHandle ent) => ent == 0 ? 0f : Call<float>(GET_ENTITY_SPEED, ent);

		public static bool IsBigVehicle(VehicleHandle v) => v == 0 ? false : Call<bool>(IS_BIG_VEHICLE, v);
		public static void SetEngineRunning(VehicleHandle v, bool value) {
			if( v != 0 ) { Call(SET_VEHICLE_ENGINE_ON, v, value, true); }
		}

		public static Dictionary<VehicleSeat, PedHandle> GetSeatMap(VehicleHandle veh) {
			var ret = new Dictionary<VehicleSeat, PedHandle>();
			Enum.GetValues(typeof(VehicleSeat)).Each<VehicleSeat>( seat => {
				PedHandle ped = Call<PedHandle>(GET_PED_IN_VEHICLE_SEAT, veh, seat);
				ret[seat] = IsAlive(ped) ? ped : 0;
			});
			return ret;
		}

	}
}