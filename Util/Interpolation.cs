using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Shiv.Global;
using static System.Math;
using System.Numerics;

namespace Shiv {
	static class Interp {
		private static Vector3[] BezierOnce(float f, Vector3[] points) {
			var n = points.Length;
			var s = 1.0f - f;
			var Q = new Vector3[n]; // TODO: can I re-use the points array, is it for sure on the heap as a params?
			for( int i = 0; i < n - 1; i++ ) {
				Q[i] = (points[i] * s) + (points[i + 1] * f);
			}
			return Q;
		}
		public static Vector3 Bezier(float percent, params Vector3[] points) {
			if( points.Length < 1 ) {
				return Vector3.Zero;
			}

			for( int i = 0; i < points.Length - 1; i++ ) {
				points = BezierOnce(percent, points);
			}
			return points[0];
		}
		public static Vector3 Bezier(float percent, IEnumerable<Vector3> points) => Bezier(percent, points.ToArray());

		public static Vector3 Lagrange(float percent, IEnumerable<Vector3> input) {
			Vector3[] x = input.ToArray();
			if( x.Length == 0 ) {
				return Vector3.Zero;
			}
			if( x.Length == 1 ) {
				return x[0];
			}
			// float t0 = x[0].X, vt0 = x[0].Y;
			// float t1 = x[1].X, vt1 = x[1].Y;
			// float t2 = x[2].X, vt2 = x[2].Y;
			// float t3 = x[3].X, vt3 = x[3].Y;
			float t = x[0].X + (percent * (x[Max(0, x.Length - 1)].X - x[0].X));
			float result = 0f;
			for(int i = 0; i < x.Length; i++ ) {
				float term = 1f;
				for(int j = 0; j < x.Length; j++ ) {
					if( i == j ) {
						continue;
					}
					term *= (t - x[j].X) / (x[i].X - x[j].X);
				}
				result += x[i].Y * term;
			}
			/*
				(((t - x[1].X) / (x[0].X - x[1].X)) * ((t - x[2].X) / (x[0].X - x[2].X)) * ((t - x[3].X) / (x[0].X - x[3].X)) * x[0].Y) + //  L(0, t) * x[0].Y
				(((t - x[0].X) / (x[1].X - x[0].X)) * ((t - x[2].X) / (x[1].X - x[2].X)) * ((t - x[3].X) / (x[1].X - x[3].X)) * x[1].Y) + // L(1, t) * vt1
				(((t - x[0].X) / (x[2].X - x[0].X)) * ((t - x[1].X) / (x[2].X - x[1].X)) * ((t - x[3].X) / (x[2].X - x[3].X)) * x[2].Y) + // L(2, t) * vt2
				(((t - x[0].X) / (x[3].X - x[0].X)) * ((t - x[1].X) / (x[3].X - x[1].X)) * ((t - x[2].X) / (x[3].X - x[2].X)) * x[3].Y) // L(3, t) * vt3;
				;
				*/
			return new Vector3(
				t, result, 
				x[0].Z + (percent * (x[Max(0, x.Length - 1)].Z - x[0].Z))); // this linear interpolation on the Z is likely going to break something, but will work for flat-ish paths for now

		}


		public static Vector3 CatmullRom(float percent, IEnumerable<Vector3> input) {
			Vector3[] v = input.Take(4).ToArray();
			return 
				v[0] * (-0.5f * percent * percent * percent + 1.0f * percent * percent - 0.5f * percent) +
				v[1] * (+1.5f * percent * percent * percent - 2.5f * percent * percent + 1.0f) +
				v[2] * (-1.5f * percent * percent * percent + 2.0f * percent * percent + 0.5f * percent) +
				v[3] * (+0.5f * percent * percent * percent - 0.5f * percent * percent);
		}
		public static Vector3 CatmullRom25(IEnumerable<Vector3> input) {
			Vector3[] v = input.Take(4).ToArray();
			return v[0] * -0.0703125f + v[1] * +0.8671875f + v[2] * +0.2265625f + v[3] * -0.0234375f;
		}
		public static Vector3 CatmullRom50(IEnumerable<Vector3> input) {
			Vector3[] v = input.Take(4).ToArray();
			return v[0] * -0.0625f + v[1] * +0.5625f + v[2] * +0.5625f + v[3] * -0.0625f;
		}
		public static Vector3 CatmullRom75(IEnumerable<Vector3> input) {
			Vector3[] v = input.Take(4).ToArray();
			return v[3] * -0.0703125f + v[2] * +0.8671875f + v[1] * +0.2265625f + v[0] * -0.0234375f;
		}
	}
}
