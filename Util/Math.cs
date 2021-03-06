﻿
using System;
using System.Runtime.InteropServices;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Collections.Generic;

namespace Shiv {

	[StructLayout(LayoutKind.Explicit, Size = 24)]
	public struct NativeVector3 {
		[FieldOffset(0)] public float X;
		[FieldOffset(8)] public float Y;
		[FieldOffset(16)] public float Z;
		public static implicit operator Vector3(NativeVector3 value) => new Vector3(value.X, value.Y, value.Z);
		public static implicit operator NativeVector3(Vector3 value) => new NativeVector3() { X = value.X, Y = value.Y, Z = value.Z };
	}

	public static partial class Global {
		public static readonly Vector3 North = new Vector3(0, 1, 0);
		public static readonly Vector3 South = new Vector3(0, -1, 0);
		public static readonly Vector3 East = new Vector3(1, 0, 0);
		public static readonly Vector3 West = new Vector3(-1, 0, 0);
		public static readonly Vector3 Down = new Vector3(0, 0, -1);
		public static readonly Vector3 Up = new Vector3(0, 0, 1);

		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector3 Right(Matrix4x4 m) => new Vector3(m.M11, m.M12, m.M13);
		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector3 Forward(Matrix4x4 m) => new Vector3(m.M21, m.M22, m.M23);
		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector3 UpVector(Matrix4x4 m) => new Vector3(m.M31, m.M32, m.M33);
		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector3 Position(Matrix4x4 m) => new Vector3(m.M41, m.M42, m.M43);

		public static float Round(float z, int digits) => (float)Math.Round(z, digits);
		public static Vector3 Round(Vector3 v, int digits) {
			return new Vector3(
				Round(v.X, digits),
				Round(v.Y, digits),
				Round(v.Z, digits)
			);
		}

		public static float Distance(Vector3 a, Vector3 b) => (a - b).Length();
		public static float DistanceSq(Vector3 a, Vector3 b) => (a - b).LengthSquared();
		public static Func<Vector3, float> DistanceTo(Vector3 a) => (b) => Distance(a, b);
		public static Func<Vector3, float> DistanceSqTo(Vector3 a) => (b) => Distance(a, b);

		public static float Rad2Deg(double rad) => (float)(rad * 180 / Math.PI);
		public static float Deg2Rad(double deg) => (float)(deg * Math.PI / 180);

		public static float AbsHeading(float h) => h < 0 ? h + 360 : h;
		public static float Heading(Matrix4x4 m) => Rad2Deg(Math.Atan2(m.M22, m.M21));
		public static float Heading(Vector3 v) => Rad2Deg(Math.Atan2(v.Y, v.X));
		public static float RadHeading(Matrix4x4 m) => (float)Math.Atan2(m.M22, m.M21);
		public static float RadHeading(Vector3 v) => (float)Math.Atan2(v.Y, v.X);
		public static float RelativeHeading(float from, float to) {
			float d = to - from;
			if( d > 180 ) {
				d -= 360;
			}
			if( d < -180 ) {
				d += 360;
			}
			return d;
		}

		public static bool IsBetween(float min, float max, float value) => (value >= Math.Min(min,max)) && (value <= Math.Max(min,max));

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static float Clamp(float x, float min, float max) => Math.Max(min, Math.Min(max, x));

		public static float Sigmoid(float x) => (float)(1 / (1 + Math.Pow(Math.E, -x)));

		public static string BitString(int x, int bits = 32) => Convert.ToString(x, 2).PadLeft(bits, '0');
		public static string BitString(long x, int bits = 64) => Convert.ToString(x, 2).PadLeft(bits, '0');

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


		public static Func<Vector3, bool> Within(float range) => (Vector3 v) => DistanceToSelf(v) <= range;

		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static float DistanceToSelf(Vector3 pos) => (pos - PlayerPosition).LengthSquared();

		public static Vector3 GetOffsetPosition(Matrix4x4 m, Vector3 offset) => Vector3.Transform(offset, m);

		/// <summary>  Expensive. </summary>
		public static Vector3 GetPositionOffset(Matrix4x4 m, Vector3 pos) => Matrix4x4.Invert(m, out Matrix4x4 inv) ? Vector3.Transform(pos, inv) : Vector3.Zero;

		public static float GetVolume(Vector3 frontRight, Vector3 backLeft) {
			var diag = frontRight - backLeft;
			return Math.Abs(diag.X) * Math.Abs(diag.Y) * Math.Abs(diag.Z);
		}
	}
}