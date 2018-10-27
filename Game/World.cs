
using System;
using System.Drawing;
using GTA.Native;
using System.Numerics;

namespace Shiv {
	public static partial class Globals {

		public static PointF ScreenCoords(Vector3 pos) {
			float x, y;
			unsafe { Function.Call<bool>(Hash._GET_SCREEN_COORD_FROM_WORLD_COORD, pos, new IntPtr(&x), new IntPtr(&y)); }
			return new PointF(x, y);
		}

		public static Vector3 PutOnGround(Vector3 pos, float off = 0) => new Vector3(pos.X, pos.Y, off + GetGroundZ(pos));
		public static float GetGroundZ(Vector3 pos) {
			float z = 0;
			unsafe {
				Function.Call(Hash.GET_GROUND_Z_FOR_3D_COORD,
					pos.X, pos.Y, pos.Z + .05f, 
					new IntPtr(&z), 0);
			}
			return z;
		}

		public static void PauseClock(bool value) => Function.Call(Hash.PAUSE_CLOCK, value);

		public static void Blackout(bool value) => Function.Call(Hash._SET_BLACKOUT, value);

		public static void ChangeWeather(Weather weather, float duration) => Function.Call(Hash._SET_WEATHER_TYPE_OVER_TIME, Enum.GetName(typeof(Weather), weather), duration);

		public static float Gravity() => MemoryAccess.ReadWorldGravity();
		public static void Gravity(float value) {
			MemoryAccess.WriteWorldGravity(value);
			Function.Call(Hash.SET_GRAVITY_LEVEL, 0);
			MemoryAccess.WriteWorldGravity(9.800000f);
		}

		public struct RaycastResult {
			public bool DidHit;
			public Vector3 HitPosition;
			public Vector3 SurfaceNormal;
			public Materials Material;
			public int Entity;
			public RaycastResult(int result) {
		    bool hit;
		    NativeVector3 hitPos;
		    NativeVector3 normal;
				int material;
		    int entity;
				unsafe { Function.Call(Hash._GET_SHAPE_TEST_RESULT_EX, result, 
					new IntPtr(&hit), 
					new IntPtr(&hitPos), 
					new IntPtr(&normal), 
					new IntPtr(&material), 
					new IntPtr(&entity)); }
				DidHit = hit;
				HitPosition = hitPos;
				SurfaceNormal = normal;
				Material = (Materials)material;
				Entity = entity;
			}
		}
		public static RaycastResult Raycast(Vector3 source, Vector3 target, IntersectOptions options, PedHandle ignoreEntity) => 
			Raycast(source, target, options, (int)ignoreEntity);
		public static RaycastResult Raycast(Vector3 source, Vector3 target, float radius, IntersectOptions options, PedHandle ignoreEntity) =>
			Raycast(source, target, radius, options, (int)ignoreEntity);
		public static RaycastResult Raycast(Vector3 source, Vector3 target, IntersectOptions options, int ignoreEntity) => 
			new RaycastResult(Function.Call<int>(Hash._START_SHAPE_TEST_RAY, source, target, options, ignoreEntity, 7));
		public static RaycastResult Raycast(Vector3 source, Vector3 target, float radius, IntersectOptions options, int ignoreEntity) =>
			new RaycastResult(Function.Call<int>(Hash.START_SHAPE_TEST_CAPSULE, source, target, radius, options, ignoreEntity, 7));
		public static bool IsCarMaterial(Materials m) =>
			m == Materials.car_metal ||
			m == Materials.car_engine ||
			m == Materials.car_plastic;

	}
}