
using System;
using System.Runtime.InteropServices;
using System.Numerics;

namespace Shiv {

	[StructLayout(LayoutKind.Explicit, Size = 24)]
	public struct NativeVector3 {
		[FieldOffset(0)] public float X;
		[FieldOffset(8)] public float Y;
		[FieldOffset(16)] public float Z;
		public static implicit operator Vector3(NativeVector3 value) => new Vector3(value.X, value.Y, value.Z);
		public static implicit operator NativeVector3(Vector3 value) => new NativeVector3() { X = value.X, Y = value.Y, Z = value.Z };
	}

	public static partial class Globals {
		public static readonly Vector3 North = new Vector3(0, 1, 0);
		public static readonly Vector3 South = new Vector3(0, -1, 0);
		public static readonly Vector3 East = new Vector3(1, 0, 0);
		public static readonly Vector3 West = new Vector3(-1, 0, 0);
		public static readonly Vector3 Down = new Vector3(0, 0, -1);
		public static readonly Vector3 Up = new Vector3(0, 0, 1);

		public static Vector3 Right(Matrix4x4 m) => new Vector3(m.M11, m.M12, m.M13);
		public static Vector3 Forward(Matrix4x4 m) => new Vector3(m.M21, m.M22, m.M23);
		public static Vector3 UpVector(Matrix4x4 m) => new Vector3(m.M31, m.M32, m.M33);
		public static Vector3 Position(Matrix4x4 m) => new Vector3(m.M41, m.M42, m.M43);

		public static float Round(float z, int digits) => (float)Math.Round(z, digits);
		public static Vector3 Round(Vector3 v, int digits) {
			return new Vector3(
				Round(v.X, digits),
				Round(v.Y, digits),
				Round(v.Z, digits)
			);
		}

		public static float Rad2Deg(double rad) { return (float)(rad * 180 / Math.PI); }
		public static float Deg2Rad(double deg) { return (float)(deg * Math.PI / 180); }

		public static float AbsHeading(float h) {
			return h < 0 ? h + 360 : h;
		}
		public static float Heading(Matrix4x4 m) {
			return Rad2Deg(Math.Atan2(m.M21, m.M22));
		}

		public static bool Between(float min, float max, float value) {
			return (value >= min) && (value <= max);
		}

		public static float Clamp(float x, float min, float max) => Math.Max(min, Math.Min(max, x));

		public static float Sigmoid(float x) => (float)(1 / (1 + Math.Pow(Math.E, -x)));

		public static Vector3 Min(params Vector3[] vectors) {
			var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
			foreach( Vector3 v in vectors ) {
				min.X = Math.Max(v.X, min.X);
				min.Y = Math.Max(v.Y, min.Y);
				min.Z = Math.Max(v.Z, min.Z);
			}
			return min;
		}

		public static Vector3 Max(params Vector3[] vectors) {
			var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
			foreach( Vector3 v in vectors ) {
				max.X = Math.Max(v.X, max.X);
				max.Y = Math.Max(v.Y, max.Y);
				max.Z = Math.Max(v.Z, max.Z);
			}
			return max;
		}

		public static float DistanceToSelf(Vector3 pos) => (pos - PlayerPosition).LengthSquared();

		public static Vector3 GetOffsetPosition(Matrix4x4 m, Vector3 offset) => Vector3.Transform(offset, m); // m.TransformPoint(offset);

		/// <summary>  Expensive. </summary>
		public static Vector3 GetPositionOffset(Matrix4x4 m, Vector3 pos) => Matrix4x4.Invert(m, out Matrix4x4 inv) ? Vector3.Transform(pos, inv) : Vector3.Zero;

	}
}