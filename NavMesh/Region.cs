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

		public static RegionHandle Region(Vector3 v) => Region(Handle(v));

		public static RegionHandle Region(NodeHandle a) => (RegionHandle)((ulong)a >> 43);

	}
}
