using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using GTA.Native;
using System.Numerics;
using static GTA.Native.MemoryAccess;
using static GTA.Native.Function;
using static GTA.Native.Hash;
using System.Collections;
using static Shiv.Global;
using static Shiv.NativeMethods;
using System.Linq;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Drawing;

namespace Shiv {

	public static partial class Global {
		private static int[] GetAllVehicles(uint max = 256) {
			if( max == 0 ) {
				throw new ArgumentException();
			}
			int[] ret;
			unsafe {
				fixed ( int* buf = new int[max] ) {
					int count = WorldGetAllVehicles(buf, (int)max);
					ret = new int[count];
					Marshal.Copy(new IntPtr(buf), ret, 0, count);
				}
			}
			return ret;
		}
		public static readonly Func<VehicleHandle[]> NearbyVehicles = Throttle(231, () =>
			 GetAllVehicles().Cast<VehicleHandle>()
				.Where(v => v != PlayerVehicle && Exists(v))
				.OrderBy(DistanceToSelf)
				.ToArray()
		);
		public static bool TryGetVehicle(BlipHUDColor color, out VehicleHandle vehicle) {
			foreach(var p in NearbyVehicles() ) {
				if( GetBlipHUDColor(GetBlip(p)) == color ) {
					vehicle = p;
					return true;
				}
			}
			vehicle = default;
			return false;
		}

		public static float DistanceToSelf(VehicleHandle v) => DistanceToSelf(Position(Matrix(v)));
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

		public static bool TryCreateVehicle(VehicleHash model, Vector3 pos, float heading, out VehicleHandle vehicle) {
			if( ! IsValid(model) ) {
				vehicle = VehicleHandle.ModelInvalid;
				return false;
			}
			if( ! IsLoaded(model) ) {
				vehicle = VehicleHandle.ModelLoading;
				RequestModel(model);
				return false;
			}
			vehicle = Call<VehicleHandle>(CREATE_VEHICLE, model, pos.X, pos.Y, pos.Z, heading, true, true);
			UI.DrawHeadline($"CREATE_VEHICLE returned = {vehicle}");
			return true;
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


		public static VehicleHandle CurrentVehicle(PedHandle ped) => ped == PedHandle.Invalid ? VehicleHandle.Invalid :
			Call<VehicleHandle>(GET_VEHICLE_PED_IS_IN, ped, false);

		public static bool IsBigVehicle(VehicleHandle v) => v == 0 ? false : Call<bool>(IS_BIG_VEHICLE, v);
		public static void SetEngineRunning(VehicleHandle v, bool value) {
			if( v != 0 ) { Call(SET_VEHICLE_ENGINE_ON, v, value, true); }
		}

		public static PedHandle GetPedInSeat(VehicleHandle v, VehicleSeat seat) => Call<PedHandle>(GET_PED_IN_VEHICLE_SEAT, v, seat);
		public static Dictionary<VehicleSeat, PedHandle> GetSeatMap(VehicleHandle veh) {
			var ret = new Dictionary<VehicleSeat, PedHandle>();
			Enum.GetValues(typeof(VehicleSeat)).Each<VehicleSeat>( seat => {
				PedHandle ped = GetPedInSeat(veh, seat);
				ret[seat] = IsAlive(ped) ? ped : PedHandle.Invalid;
			});
			return ret;
		}

		public static bool IsSeatFree(VehicleHandle v, VehicleSeat seat) => Call<bool>(IS_VEHICLE_SEAT_FREE, v, seat);

		/// <summary>
		/// Used in combination with <see cref="GetVehicleOffset(VehicleHandle, Vector3)"/>,
		/// these are coordinates that, when transformed by the model matrix,
		/// are positioned in useful places around a vehicle.
		/// </summary>
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
		/// <summary>
		/// Get a position in world-coords, adjacent to this Vehicle.
		/// Consider using <see cref="VehicleOffsets"/> to find useful preset offsets.
		/// </summary>
		public static Vector3 GetVehicleOffset(VehicleHandle v, Vector3 offset) => GetVehicleOffset(v, offset, Matrix(v));
		/// <summary>
		/// Get a position in world-coords, adjacent to this Vehicle.
		/// Consider using <see cref="VehicleOffsets"/> to find useful preset offsets.
		/// </summary>
		public static Vector3 GetVehicleOffset(VehicleHandle v, Vector3 offset, Matrix4x4 m) => GetVehicleOffset(v, offset, m, GetModel(v));
		/// <summary>
		/// Get a position in world-coords, adjacent to this Vehicle.
		/// Consider using <see cref="VehicleOffsets"/> to find useful preset offsets.
		/// </summary>
		public static Vector3 GetVehicleOffset(VehicleHandle v, Vector3 offset, Matrix4x4 m, VehicleHash model) {
			GetModelDimensions(model, out Vector3 backLeft, out Vector3 frontRight);
			return GetVehicleOffset(v, offset, m, frontRight, backLeft);
		}
		/// <summary>
		/// Get a position in world-coords, adjacent to this Vehicle.
		/// Consider using <see cref="VehicleOffsets"/> to find useful preset offsets.
		/// </summary>
		public static Vector3 GetVehicleOffset(VehicleHandle v, Vector3 offset, Matrix4x4 m, Vector3 frontRight, Vector3 backLeft) => Vector3.Transform(offset * (frontRight - backLeft), m);


		// In order, each entry in this array corresponds to a 60 degree slice
		// If the danger is in slice 0-60, we take cover at the offset from array index 0
		// If the danger is in slice 60-120, we take cover at the offset from array index 1
		private static readonly Vector3[] vehicleCoverOffset = new Vector3[6] {
			VehicleOffsets.BackBumper,
			VehicleOffsets.FrontLeftWheel,
			VehicleOffsets.BackLeftWheel,
			VehicleOffsets.FrontGrill,
			VehicleOffsets.BackRightWheel,
			VehicleOffsets.FrontRightWheel,
		};

		/// <summary>
		/// Return the position around this vehicle that provides the best cover from danger.
		/// </summary>
		public static Vector3 FindCoverBehindVehicle(VehicleHandle v, Vector3 danger, bool debug=false) {
			if( IsSeatFree(v, VehicleSeat.Driver) && Speed(v) == 0f ) {
				Matrix4x4 m = Matrix(v);
				Vector3 pos = Global.Position(m);
				Vector3 delta = danger - pos;
				float heading = AbsHeading(Heading(m) - Rad2Deg(Math.Atan2(delta.Y, delta.X)) - 45);
				int slot = (int)(6 * (heading / 360f));
				if( debug ) {
					int line = 0;
					DrawLine(pos, danger, Color.Red);
					UI.DrawTextInWorldWithOffset(pos, 0f, (line++ * .02f), $"Heading: {heading:F2} Slot: {slot}");
				}
				if( slot >= 0 && slot < vehicleCoverOffset.Length ) {
					return GetVehicleOffset(v, vehicleCoverOffset[slot], m);
				}
			}
			return Vector3.Zero;
		}

		public static void DebugVehicle() {

			VehicleHandle v = NearbyVehicles().FirstOrDefault();
			if( Exists(v) ) {
				Matrix4x4 m = Matrix(v);
				VehicleHash model = GetModel(v);
				GetModelDimensions(model, out Vector3 backLeft, out Vector3 frontRight);
				var sw = new Stopwatch();
				sw.Start();
				var blocked = new ConcurrentSet<NodeHandle>();
				NavMesh.GetAllHandlesInBox(m, backLeft, frontRight, blocked);
				DrawBox(m, backLeft, frontRight);
				UI.DrawText($"GetAllHandlesInBox: {blocked.Count} in {sw.ElapsedTicks} ticks");
				foreach( Vector3 n in blocked.Select(NavMesh.Position) ) {
					if( random.NextDouble() < .2 ) {
						DrawSphere(n, .05f, Color.Red);
					}
				}
			}

		}
	}
}
