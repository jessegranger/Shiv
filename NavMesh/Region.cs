using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using static Shiv.Global;
using static System.Math;

namespace Shiv {
	public partial class NavMesh : Script {
		private const float regionScale = 128f; // how wide on each side is one region cube
		private const int regionShift = 7; // we use 7 bits each for X,Y,Z = 21 bits when we pack into RegionHandle

		public static RegionHandle Region(Vector3 v) {
			return Region(Handle(v));
			/*
			if( v == Vector3.Zero ) {
				return RegionHandle.Invalid;
			}
			// v.X starts [-8192..8192] becomes [0..128]
			uint x = (uint)(Round((v.X + mapRadius) / regionScale));
			uint y = (uint)(Round((v.Y + mapRadius) / regionScale));
			uint z = (uint)(Round((v.Z + zDepth) / regionScale));
			return (RegionHandle)((x << (regionShift << 1)) | (y << regionShift) | z);
			*/
		}

		public static RegionHandle Region(NodeHandle a) {
			return (RegionHandle)((ulong)a >> (mapShift * 3));
			/*
			ulong u = (ulong)a;
			ulong z = u & mapShiftMask;
			u >>= mapShift;
			ulong y = u & mapShiftMask;
			u >>= mapShift;
			ulong x = u & mapShiftMask;
			float scale = gridScale / regionScale;
			x = (ulong)Round(x * scale);
			y = (ulong)Round(y * scale);
			z = (ulong)Round(z * zScale / regionScale);
			return (RegionHandle)((x << (regionShift << 1)) | (y << regionShift) | z);
			*/
		}

		private static readonly ConcurrentDictionary<RegionHandle, Future<RegionHandle>> loadedRegions = new ConcurrentDictionary<RegionHandle, Future<RegionHandle>>();
		public static IFuture<RegionHandle> RequestRegion(RegionHandle r) {
			try {
				return r == RegionHandle.Invalid
					? new Immediate<RegionHandle>(r)
					: (IFuture<RegionHandle>)loadedRegions.GetOrAdd(r, (region) => new Future<RegionHandle>(() => {
						ReadFromFile(region, $"scripts/Shiv.{region}.mesh");
						return region;
					}));
			} finally {
			}
		}

		private static ConcurrentSet<RegionHandle> dirtyRegions = new ConcurrentSet<RegionHandle>();
		public static bool Dirty(RegionHandle r) => dirtyRegions.Contains(r);
		public static void Dirty(RegionHandle r, bool value) {
			if( value ) { dirtyRegions.Add(r); }
			else { dirtyRegions.Remove(r); }
		}

	}
}
