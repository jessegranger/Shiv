
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
		} // TODO: measure the speed of this versus using CameraMatrix directly (Since we already pay to fetch it every frame)

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

		public static void GetNormals(Matrix4x4 m, Vector3 backLeft, Vector3 frontRight, out Vector3[] corners, out Vector3[] centers, out Vector3[] normals) {
			corners = new Vector3[6];
			centers = new Vector3[6];
			normals = new Vector3[6];
			var w = West * (frontRight.X - backLeft.X);
			var d = North * (frontRight.Y - backLeft.Y);
			var h = Up * (frontRight.Z - backLeft.Z);
			var tFront = Vector3.Transform(frontRight, m);
			var tBack = Vector3.Transform(backLeft, m);
			corners[0] = Vector3.Transform(frontRight + w, m);
			corners[1] = Vector3.Transform(frontRight - h, m);
			corners[2] = Vector3.Transform(frontRight - d, m);
			corners[3] = Vector3.Transform(backLeft - w, m);
			corners[4] = Vector3.Transform(backLeft + h, m);
			corners[5] = Vector3.Transform(backLeft + d, m);

			centers[0] = (corners[0] + corners[1]) * .5f;
			centers[1] = (corners[0] + corners[2]) * .5f;
			centers[2] = (corners[1] + corners[2]) * .5f;
			centers[3] = (corners[3] + corners[4]) * .5f;
			centers[4] = (corners[3] + corners[5]) * .5f;
			centers[5] = (corners[4] + corners[5]) * .5f;

			normals[0] = Vector3.Normalize(Vector3.Cross(corners[1] - tFront, corners[0] - tFront));
			normals[1] = Vector3.Normalize(Vector3.Cross(corners[0] - tFront, corners[2] - tFront));
			normals[2] = Vector3.Normalize(Vector3.Cross(corners[2] - tFront, corners[1] - tFront));
			normals[3] = -normals[0];
			normals[4] = -normals[1];
			normals[5] = -normals[2];

		}

		public static Vector3 GetCenter(Matrix4x4 m, Vector3 backLeft, Vector3 frontRight) => Vector3.Transform((backLeft + frontRight) / 2f, m);

		public static bool TryIntersectPlane(Vector3 rayStart, Vector3 rayEnd, Vector3 corner, Vector3 normal, Vector3 size, out Vector3 point) {
			point = Vector3.Zero;
			Vector3 rayDir = rayEnd - rayStart;

			float denom = Vector3.Dot(normal, rayDir);
			if( denom == 0 ) {
				return false;
			}
			float r = Vector3.Dot(normal, (corner - rayStart)) / denom;
			if( r >= 0f && r <= 1f ) {
				point = rayStart + (r * rayDir);
				// check if point is inside the rect
				UI.DrawTextInWorld(corner, $"r:{r}");
				return IsBetween(corner.X - size.X, corner.X + size.X, point.X)
					&& IsBetween(corner.Y - size.Y, corner.Y + size.Y, point.Y)
					&& IsBetween(corner.Z - size.Z, corner.Z + size.Z, point.Z);
			}
			return false;
		}

		public static bool IntersectModel(Vector3 rayStart, Vector3 rayEnd, Matrix4x4 m, Vector3 backLeft, Vector3 frontRight) {
			Vector3 rayDir = (rayEnd - rayStart);
			GetNormals(m, backLeft, frontRight, out var corners, out var centers, out var normals);
			/*
			foreach( var n in normals ) {
				if( Vector3.Dot(rayDir, n) == 0f ) {
					return false;
				}
			}
			*/

			for( int i = 0; i < 6; i++) {
				var s = Vector3.Dot(normals[i], centers[i] - rayStart) / Vector3.Dot(normals[i], rayDir);
				UI.DrawTextInWorld(centers[i], $"s:{s:F2}");
				if( s > 0f && s < 1f ) {
					return true;
				}
			}

			return false;
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