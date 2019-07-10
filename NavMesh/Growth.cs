using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using static Shiv.Global;
using static Shiv.NavMesh;
using static System.Math;

namespace Shiv {
	public partial class NavMesh : Script {
		// constants to use when expanding the mesh
		private const IntersectOptions growRayOpts = IntersectOptions.Map | IntersectOptions.Objects | IntersectOptions.Water| IntersectOptions.Unk64 | IntersectOptions.Unk128 | IntersectOptions.Unk512;
		private const float capsuleSize = 0.30f; // when checking for obstructions, how big of a box to use for collision
		private const float maxGrowRange = 100f; // dont trust the game to have geometry loaded farther than this
		// track a 'frontier' of not-yet-explored nodes
		internal static ConcurrentQueue<NodeHandle> Ungrown = new ConcurrentQueue<NodeHandle>();
		// only a subset of edges are used by Grow() to expand the mesh
		private static int[] possibleGrowthEdges = new int[] { 0, 1, 2, 3, 4, 6, 10, 12 };
		internal static IEnumerable<NodeHandle> PossibleGrowthEdges(NodeHandle node) => possibleGrowthEdges.Select(i => AddEdgeOffset(node, i)); // (NodeHandle)((long)node + edgeOffsets[i]));
		private static IEnumerable<NodeHandle> GroundEdges(NodeHandle node) => PossibleGrowthEdges(node).Select(n => Handle(PutOnGround(Position(n), 1f)));

		/// <summary>
		/// The node most recently touched by Grow().
		/// </summary>
		public static NodeHandle LastGrown { get; private set; } = NodeHandle.Invalid;

		internal static NodeHandle AddEdgeOffset(NodeHandle n, int i) => Handle(Position((NodeHandle)((long)((ulong)n & handleMask) + edgeOffsets[i]))); // extract position, use to add region back in to handle
		public static IEnumerable<NodeHandle> GrowOne(NodeHandle node, HashSet<EntHandle> doors, bool debug=false) {
			if( IsGrown(node) ) {
				yield break;
			}
			var nodePos = Position(node);
			if( DistanceToSelf(nodePos) > maxGrowRange * maxGrowRange) {
				yield break;
			}
			IsGrown(node, true);
			foreach( var i in possibleGrowthEdges ) {
				var e = AddEdgeOffset(node, i);
				NodeHandle g = Handle(PutOnGround(Position(e), 1f));
				if( IsPossibleEdge(node, g) && !HasEdge(node, g) ) {
					Vector3 delta = Position(g) - nodePos;
					var len = delta.Length();
					Vector3 end = nodePos + ((delta / len) * (len - capsuleSize / 2));
					RaycastResult result = Raycast(nodePos, end, capsuleSize, growRayOpts, Self);
					if( (!result.DidHit) 
						|| (Exists(result.Entity) && doors.Contains(result.Entity))  ) {
						if( debug ) {
							Line.Add(Position(node), Position(g), Color.Yellow, 3000);
						}
						AddEdge(node, g);
						AddEdge(g, node);
						var nClear = Clearance(node);
						if( nClear == 0 ) {
							PropagateClearance(node, nClear = 15);
						}
						var gClear = Clearance(g);
						if( gClear == 0 || gClear > nClear + 1) {
							Clearance(g, nClear + 1);
						}
						if( ! IsGrown(g) ) {
							yield return g;
						}
					} else if( result.DidHit ) {
						Materials m = result.Material;
						if( m != Materials.metal_railing
							&& m != Materials.metal_garage_door
							&& m != Materials.bushes
							&& m != Materials.leaves
							&& Abs(Vector3.Dot(result.SurfaceNormal, Up)) < .01f ) {
							IsCover(node, true);
							PropagateClearance(node, 1);
							if( debug ) {
								Text.Add(Position(node), $"IsCover({m})", 5000, 0f, -.04f);
							}
						} else {
							if( debug ) {
								Text.Add(Position(node), $"IsBlocked({m})", 5000, 0f, -.06f);
							}
						}
					}
				}
			}
		}
		public static MovingAverage grownPerSecond = new MovingAverage(20);
		public static void Grow(NodeHandle start, uint maxMs, bool debug=false) {
			if( start == NodeHandle.Invalid ) {
				return;
			}
			var sw = new Stopwatch();
			sw.Start();
			int count = 0;
			var queue = new Queue<NodeHandle>();
			queue.Enqueue(start);
			var doors = new HashSet<EntHandle>(NearbyObjects().Where(e => Pathfinder.checkDoorModels.Contains(GetModel(e))));
			while( sw.ElapsedMilliseconds < maxMs ) {
				NodeHandle next;
				if( queue.Count > 0 ) {
					next = queue.Dequeue();
				} else if( ! Ungrown.TryDequeue(out next) ) {
					break;
				}
				if( IsGrown(next) ) {
					continue;
				}
				GrowOne(next, doors, debug).Each(queue.Enqueue);
				count += 1;
				LastGrown = next;
			}
			if( queue.Count > 0 ) {
				queue.Each(Ungrown.Enqueue);
			}
			double seconds = (float)(sw.ElapsedTicks + 1) / (float)TimeSpan.TicksPerSecond;
			grownPerSecond.Add((float)count / (float)seconds);
		}
	}
}
