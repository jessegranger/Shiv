using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using GTA.Native;
using System.Numerics;
using static GTA.Native.MemoryAccess;
using static GTA.Native.Function;
using static GTA.Native.Hash;
using System.Collections;

namespace Shiv {

	public static partial class Global {
		public static VehicleHandle[] NearbyVehicles { get; internal set; } = new VehicleHandle[0];

		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool Exists(VehicleHandle ent) => Exists((EntHandle)ent);
		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static IntPtr Address(VehicleHandle v) => Address((EntHandle)v);
		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector3 Position(VehicleHandle ent) => Position(Matrix((EntHandle)ent));
		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static Matrix4x4 Matrix(VehicleHandle ent) => Matrix((EntHandle)ent);
		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static float DistanceToSelf(VehicleHandle ent) => DistanceToSelf(Position(ent));
		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static float Heading(VehicleHandle ent) => Heading((EntHandle)ent);
		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static void Heading(VehicleHandle ent, float value) => Heading((EntHandle)ent, value);
		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static VehicleHash GetModel(VehicleHandle ent) => (VehicleHash)GetModel((EntHandle)ent);
		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static void GetModelDimensions(VehicleHash model, out Vector3 backLeft, out Vector3 frontRight) => GetModelDimensions((ModelHash)model, out backLeft, out frontRight);

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

		public enum VehicleNode : ulong { Invalid = 0 };

		public struct VehicleNodeData {
			public Vector3 Position;
			public float Heading;
			public int Kind;
			public int Flags;
			public int Density;
			public override int GetHashCode() => Position.GetHashCode();
		}

		public static bool TryGetVehicleNodeProperties(Vector3 node, out int density, out int flags) {
			int d = 0, f = 0;
			unsafe {
				bool ret = Call<bool>(GET_VEHICLE_NODE_PROPERTIES, node, new IntPtr(&d), new IntPtr(&f));
				density = d;
				flags = f;
				return ret;
			}
		}

		public static bool TryGetClosestVehicleNode(Vector3 pos, RoadType type, out Vector3 node) {
			var ret = new NativeVector3();
			try {
				unsafe {
					return Call<bool>(GET_CLOSEST_VEHICLE_NODE, pos, new IntPtr(&ret), type, 3.0f, 0);
				}
			} finally {
				node = ret;
			}
		}

		public static IEnumerable<VehicleNodeData> GetClosestVehicleNodes(Vector3 pos, RoadType type) {
			uint i = 1;
			var data = new VehicleNodeData();
			while( TryGetClosestVehicleNode(pos, type, i, out data.Position, out data.Heading, out data.Kind ) ) {
				yield return data;
				i += 1;
			}
		}

		public static bool TryGetClosestVehicleNode(Vector3 pos, RoadType type, uint index, out Vector3 node, out float heading, out int id) {
			var ret = new NativeVector3();
			float h = 0f;
			int i = 0;
			try {
				unsafe {
					return Call<bool>(GET_NTH_CLOSEST_VEHICLE_NODE_WITH_HEADING, pos, index,
						new IntPtr(&ret), new IntPtr(&h), new IntPtr(&i),
						type, 3.0f, 0);
				}
			} finally {
				node = ret;
				heading = h;
				id = i;
			}
		}

		public static bool IsInVehicle(PedHandle ent) => ent == PedHandle.Invalid ? false : Call<bool>(IS_PED_IN_ANY_VEHICLE, ent, 0);

		public static VehicleHandle CurrentVehicle(PedHandle ped) => ped == PedHandle.Invalid ? VehicleHandle.Invalid :
			Call<VehicleHandle>(GET_VEHICLE_PED_IS_IN, ped, false);

		public static Vector3 Velocity(VehicleHandle ent) => Velocity((EntHandle)ent);
		public static void Velocity(VehicleHandle ent, Vector3 value) => Velocity((EntHandle)ent, value);
		public static float Speed(VehicleHandle ent) => ent == 0 ? 0f : Call<float>(GET_ENTITY_SPEED, ent);

		public static bool IsBigVehicle(VehicleHandle v) => v == 0 ? false : Call<bool>(IS_BIG_VEHICLE, v);
		public static void SetEngineRunning(VehicleHandle v, bool value) {
			if( v != 0 ) { Call(SET_VEHICLE_ENGINE_ON, v, value, true); }
		}

		public static PedHandle GetPedInSeat(VehicleHandle v, VehicleSeat seat) => Call<PedHandle>(GET_PED_IN_VEHICLE_SEAT, v, seat);
		public static Dictionary<VehicleSeat, PedHandle> GetSeatMap(VehicleHandle veh) {
			var ret = new Dictionary<VehicleSeat, PedHandle>();
			Enum.GetValues(typeof(VehicleSeat)).Each<VehicleSeat>( seat => {
				PedHandle ped = GetPedInSeat(veh, seat);
				ret[seat] = IsAlive(ped) ? ped : 0;
			});
			return ret;
		}

		public static class VehicleOffsets {
			// these are coordinates relative to the bounding box of the model,
			// origin is at the center
			// so (-.5f, 0f, 0f) is the left side of the model, (.5f, 0f, 0f) is the right side 
			// scales based on the model's actual size later
			public static readonly Vector3 DriverDoor = new Vector3(-.6f, 0.05f, 0f);
			public static readonly Vector3 FrontGrill = new Vector3(0f, .5f, 0f);
			public static readonly Vector3 FrontLeftWheel = new Vector3(-.66f, .3f, 0f);
			public static readonly Vector3 FrontRightWheel = new Vector3(.66f, .3f, 0f);
			public static readonly Vector3 BackLeftWheel = new Vector3(-.66f, -.3f, 0f);
			public static readonly Vector3 BackRightWheel = new Vector3(.66f, -.3f, 0f);
			public static readonly Vector3 BackBumper = new Vector3(0f, -.66f, 0f);
		}
		public static Vector3 GetVehicleOffset(VehicleHandle v, Vector3 offset) => GetVehicleOffset(v, offset, Matrix(v));
		public static Vector3 GetVehicleOffset(VehicleHandle v, Vector3 offset, Matrix4x4 m) => GetVehicleOffset(v, offset, m, GetModel(v));
		public static Vector3 GetVehicleOffset(VehicleHandle v, Vector3 offset, Matrix4x4 m, VehicleHash model) {
			GetModelDimensions(model, out Vector3 backLeft, out Vector3 frontRight);
			return GetVehicleOffset(v, offset, m, frontRight, backLeft);
		}
		public static Vector3 GetVehicleOffset(VehicleHandle v, Vector3 offset, Matrix4x4 m, Vector3 frontRight, Vector3 backLeft) => Vector3.Transform(offset * (frontRight - backLeft), m);
	}
}
