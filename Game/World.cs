
using System;
using System.Drawing;
using GTA.Native;
using System.Numerics;
using static GTA.Native.Function;
using static GTA.Native.Hash;
using static System.Math;
using System.Collections.Generic;

namespace Shiv {
	public struct FinitePlane {
		public Vector3 Center;
		public Vector3 Normal;
		public Vector3 Size;
	}
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


		public static IEnumerable<FinitePlane> GetModelPlanes(Matrix4x4 m, Vector3 backLeft, Vector3 frontRight) {
			var corners = new Vector3[6];
			var centers = new Vector3[6];
			var normals = new Vector3[6];
			var sizes = new Vector3[6];
			var w = West * (frontRight.X - backLeft.X);
			var d = North * (frontRight.Y - backLeft.Y);
			var h = Up * (frontRight.Z - backLeft.Z);
			var tFront = Vector3.Transform(frontRight, m);
			// var tBack = Vector3.Transform(backLeft, m);
			corners[0] = Vector3.Transform(frontRight + w, m);
			corners[1] = Vector3.Transform(frontRight - h, m);

			centers[0] = (corners[0] + corners[1]) * .5f;
			normals[0] = Vector3.Normalize(Vector3.Cross(corners[1] - tFront, corners[0] - tFront));
			sizes[0] = new Vector3(Abs(corners[0].X - centers[0].X), Abs(corners[0].Y - centers[0].Y), Abs(corners[0].Z - centers[0].Z));
			yield return new FinitePlane() { Center = centers[0], Normal = normals[0], Size = sizes[0] };

			corners[2] = Vector3.Transform(frontRight - d, m);
			centers[1] = (corners[0] + corners[2]) * .5f;
			normals[1] = Vector3.Normalize(Vector3.Cross(corners[0] - tFront, corners[2] - tFront));
			sizes[1] = new Vector3(Abs(corners[1].X - centers[1].X), Abs(corners[1].Y - centers[1].Y), Abs(corners[1].Z - centers[1].Z));
			yield return new FinitePlane() { Center = centers[1], Normal = normals[1], Size = sizes[1] };

			centers[2] = (corners[1] + corners[2]) * .5f;
			normals[2] = Vector3.Normalize(Vector3.Cross(corners[2] - tFront, corners[1] - tFront));
			sizes[2] = new Vector3(Abs(corners[2].X - centers[2].X), Abs(corners[2].Y - centers[2].Y), Abs(corners[2].Z - centers[2].Z));
			yield return new FinitePlane() { Center = centers[2], Normal = normals[2], Size = sizes[2] };
			
			corners[3] = Vector3.Transform(backLeft - w, m);
			corners[4] = Vector3.Transform(backLeft + h, m);
			centers[3] = (corners[3] + corners[4]) * .5f;
			normals[3] = -normals[0];
			sizes[3] = new Vector3(Abs(corners[3].X - centers[3].X), Abs(corners[3].Y - centers[3].Y), Abs(corners[3].Z - centers[3].Z));
			yield return new FinitePlane() { Center = centers[3], Normal = normals[3], Size = sizes[3] };

			corners[5] = Vector3.Transform(backLeft + d, m);
			centers[4] = (corners[3] + corners[5]) * .5f;
			normals[4] = -normals[1];
			sizes[4] = new Vector3(Abs(corners[4].X - centers[4].X), Abs(corners[4].Y - centers[4].Y), Abs(corners[4].Z - centers[4].Z));
			yield return new FinitePlane() { Center = centers[4], Normal = normals[4], Size = sizes[4] };

			centers[5] = (corners[4] + corners[5]) * .5f;
			normals[5] = -normals[2];
			sizes[5] = new Vector3(Abs(corners[5].X - centers[5].X), Abs(corners[5].Y - centers[5].Y), Abs(corners[5].Z - centers[5].Z));
			yield return new FinitePlane() { Center = centers[5], Normal = normals[5], Size = sizes[5] };

		}

		public static Vector3 GetCenter(Matrix4x4 m, Vector3 backLeft, Vector3 frontRight) => Vector3.Transform((backLeft + frontRight) / 2f, m);

		public static bool TryIntersectPlane(Vector3 rayStart, Vector3 rayDir, FinitePlane plane, out Vector3 point) {
			point = Vector3.Zero;

			float denom = Vector3.Dot(plane.Normal, rayDir);
			if( denom == 0 ) {
				return false;
			}
			float r = Vector3.Dot(plane.Normal, (plane.Center - rayStart)) / denom;
			if( r >= 0f && r <= 1f ) {
				point = rayStart + (r * rayDir);
				// check if point is inside the rect
				return 
					IsBetween(plane.Center.X - plane.Size.X, plane.Center.X + plane.Size.X, point.X) &&
					IsBetween(plane.Center.Y - plane.Size.Y, plane.Center.Y + plane.Size.Y, point.Y) &&
					IsBetween(plane.Center.Z - plane.Size.Z, plane.Center.Z + plane.Size.Z, point.Z);
			}
			return false;
		}

		public static bool IntersectModel(Vector3 rayStart, Vector3 rayEnd, Matrix4x4 m, Vector3 backLeft, Vector3 frontRight) {
			Vector3 rayDir = rayEnd - rayStart;
			foreach(var plane in GetModelPlanes(m, backLeft, frontRight) ) {
				if( TryIntersectPlane(rayStart, rayDir, plane, out Vector3 _) ) {
					return true;
				}
			}
			return false;
		}

		public static void PauseClock(bool value) => Call(PAUSE_CLOCK, value);

		public static void Blackout(bool value) => Call(SET_ARTIFICIAL_LIGHTS_STATE, value);

		public static void ChangeWeather(Weather weather, float duration) => Call(_SET_WEATHER_TYPE_OVER_TIME, Enum.GetName(typeof(Weather), weather), duration);

		public static float Gravity() => MemoryAccess.ReadWorldGravity();
		public static void Gravity(float value) {
			MemoryAccess.WriteWorldGravity(value); // store value as gravity profile 0
			Call(SET_GRAVITY_LEVEL, 0); // load gravity profile 0
			MemoryAccess.WriteWorldGravity(9.800000f); // reset to default profile 0
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