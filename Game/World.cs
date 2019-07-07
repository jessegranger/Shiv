
using System;
using System.Drawing;
using GTA.Native;
using System.Numerics;
using static GTA.Native.Function;
using static GTA.Native.Hash;

namespace Shiv {
	public static partial class Global {

		public static PointF ScreenCoords(Vector3 pos) {
			float x, y;
			unsafe {
				Call<bool>(_GET_SCREEN_COORD_FROM_WORLD_COORD,
					pos, new IntPtr(&x), new IntPtr(&y)); }
			return new PointF(x, y);
		} // TODO: measure the speed of this versus using CameraMatrix directly

		public static Vector3 PutOnGround(Vector3 pos, float off = 0) =>
			pos == Vector3.Zero ? pos 
			: DistanceToSelf(pos) > (210f * 210f) ? pos
			: new Vector3(pos.X, pos.Y, off + GetGroundZ(pos));
		public static float GetGroundZ(Vector3 pos) {
			float z = 0;
			unsafe {
				Call(GET_GROUND_Z_FOR_3D_COORD,
					pos.X, pos.Y, pos.Z + .05f, 
					new IntPtr(&z), 0);
			}
			return z;
		}
		public static Vector3 StopAtWater(Vector3 pos, float delta) => new Vector3(pos.X, pos.Y, Math.Max(pos.Z, delta));

		public static void DrawLine(Vector3 start, Vector3 end, Color color) => Call(DRAW_LINE, start, end, color);
		public static Action<Vector3> DrawSphere(float radius, Color color) => (Vector3 v) => DrawSphere(v, radius, color);
		public static void DrawSphere(Vector3 pos, float radius, Color color) {
			Call(DRAW_MARKER, MarkerType.DebugSphere, 
				pos, Vector3.Zero, Vector3.Zero,
				new Vector3(radius, radius, radius),
				color, false, false, 2, 0, 0, 0, 0);
		}
		public static void DrawBox(Matrix4x4 m, Vector3 backLeft, Vector3 frontRight) {
			var w = West * (frontRight.X - backLeft.X);
			var d = North * (frontRight.Y - backLeft.Y);
			var h = Up * (frontRight.Z - backLeft.Z);
			var tFront = Vector3.Transform(frontRight, m);
			var tBack = Vector3.Transform(backLeft, m);
			DrawLine(tFront, Vector3.Transform(frontRight + w, m), Color.Pink);
			DrawLine(tFront, Vector3.Transform(frontRight - h, m), Color.Pink);
			DrawLine(tFront, Vector3.Transform(frontRight - d, m), Color.Pink);
			DrawLine(tBack, Vector3.Transform(backLeft - w, m), Color.Pink);
			DrawLine(tBack, Vector3.Transform(backLeft + h, m), Color.Pink);
			DrawLine(tBack, Vector3.Transform(backLeft + d, m), Color.Pink);
		}

		public static void PauseClock(bool value) => Call(PAUSE_CLOCK, value);

		public static void Blackout(bool value) => Call(_SET_BLACKOUT, value);

		public static void ChangeWeather(Weather weather, float duration) => Call(_SET_WEATHER_TYPE_OVER_TIME, Enum.GetName(typeof(Weather), weather), duration);

		public static float Gravity() => MemoryAccess.ReadWorldGravity();
		public static void Gravity(float value) {
			MemoryAccess.WriteWorldGravity(value);
			Call(SET_GRAVITY_LEVEL, 0);
			MemoryAccess.WriteWorldGravity(9.800000f);
		}

		public struct RaycastResult {
			public bool DidHit;
			public Vector3 HitPosition;
			public Vector3 SurfaceNormal;
			public Materials Material;
			public EntHandle Entity;
			public RaycastResult(int result) {
		    bool hit;
		    NativeVector3 hitPos;
		    NativeVector3 normal;
				int material;
		    int entity;
				unsafe { Call(_GET_SHAPE_TEST_RESULT_EX, result, 
					new IntPtr(&hit), 
					new IntPtr(&hitPos), 
					new IntPtr(&normal), 
					new IntPtr(&material), 
					new IntPtr(&entity)); }
				DidHit = hit;
				HitPosition = hitPos;
				SurfaceNormal = normal;
				Material = (Materials)material;
				Entity = (EntHandle)entity;
			}
		}
		public static RaycastResult Raycast(Vector3 source, Vector3 target, IntersectOptions options, PedHandle ignoreEntity) => Raycast(source, target, options, (int)ignoreEntity);
		public static RaycastResult Raycast(Vector3 source, Vector3 target, float radius, IntersectOptions options, PedHandle ignoreEntity) => Raycast(source, target, radius, options, (int)ignoreEntity);
		public static RaycastResult Raycast(Vector3 source, Vector3 target, IntersectOptions options, int ignoreEntity) => 
			new RaycastResult(Call<int>(_START_SHAPE_TEST_RAY, source, target, options, ignoreEntity, 7));
		public static RaycastResult Raycast(Vector3 source, Vector3 target, float radius, IntersectOptions options, int ignoreEntity) =>
			new RaycastResult(Call<int>(START_SHAPE_TEST_CAPSULE, source, target, radius, options, ignoreEntity, 7));
		public static bool IsCarMaterial(Materials m) =>
			m == Materials.car_metal ||
			m == Materials.car_engine ||
			m == Materials.car_plastic;

		public static void GetDoorState(ModelHash doorModel, Vector3 pos, out bool locked, out float heading) {
			bool _lock = true;
			float h = 0f;
			unsafe {
				Call(GET_STATE_OF_CLOSEST_DOOR_OF_TYPE, doorModel, pos, new IntPtr(&_lock), new IntPtr(&h));
			}
			locked = _lock;
			heading = h;
		}
	}
}