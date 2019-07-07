using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Math;

namespace Shiv {
	public partial class NavMesh : Script {
		/// <summary>
		/// Edges in the NavMesh are a bit field:
		///  * 0-29 are edge connections to nodes at fixed offsets.
		///  * 30 is the grown flag.
		///  * 31 is the cover flag.
		///  * 32-35 is clearance (how far from a fixed obstruction).
		/// Each NodeHandle (ulong) maps to a NodeEdges (ulong), through NavMesh.AllEdges dictionary.
		/// </summary>
		[Flags] public enum NodeEdges : ulong {
			Empty = 0,
			/* 0 - 29 used for connections to neighbors */
			EdgeMask = (1ul << 30) - 1,
			IsGrown = (1ul << 30),
			IsCover = (1ul << 31),
			ClearanceMask = (15ul << 32)
		}

		// because node positions (and thus NodeHandles) are at fixed intervals, you can compute the next NodeHandle by adding special offsets
		private static readonly uint xStep = (uint)Pow(2, 2*mapShift); // if you add X+1, how much does the NodeHandle change
		private static readonly uint yStep = (uint)Pow(2, 1*mapShift); // if you add Y+1, how much does the NodeHandle change
		private static readonly uint zStep = (uint)Pow(2, 0*mapShift); // if you add Z+1, how much does the NodeHandle change

		// for each of the 0-29 bits, specify the correct X+Y+Z offset
		// we will create a bi-directional map to answer both questions:
		// given an offset in 3d-space, which bit of the edges should I correspond to, and
		// given an edge bit, what is the offset in 3d-space
		// here is the first of those directions, this is a
		// Dictionary<NodeHandle offset, edge bit index>
		private static readonly Dictionary<long, int> whichEdgeBit = new Dictionary<long, int>() {

			{ (+0*xStep) + (+1*yStep) + (+2*zStep), 0 }, // a pair of { delta, index }
			{ (+1*xStep) + (+0*yStep) + (+2*zStep), 1 }, // to follow edge(i), go to node + delta
			{ (-1*xStep) + (+0*yStep) + (+2*zStep), 2 },
			{ (+0*xStep) + (-1*yStep) + (+2*zStep), 3 },

			{ (+1*xStep) + (+1*yStep) + (+1*zStep), 4 },
			{ (+0*xStep) + (+1*yStep) + (+1*zStep), 5 },
			{ (-1*xStep) + (+1*yStep) + (+1*zStep), 6 },
			{ (+1*xStep) + (+0*yStep) + (+1*zStep), 7 },
			{ (+0*xStep) + (+0*yStep) + (+1*zStep), 8 },
			// TODO: when we find ladders, build chains of edge 8 (up) and edge 25 (down) to model it
			// would have the option of never using 8 and 25, instead repurposing them

			{ (-1*xStep) + (+0*yStep) + (+1*zStep), 9 },
			{ (+1*xStep) + (-1*yStep) + (+1*zStep), 10 },
			{ (+0*xStep) + (-1*yStep) + (+1*zStep), 11 },
			{ (-1*xStep) + (-1*yStep) + (+1*zStep), 12 },

			{ (+1*xStep) + (+1*yStep) + (+0*zStep), 13 },
			{ (+0*xStep) + (+1*yStep) + (+0*zStep), 14 },
			{ (-1*xStep) + (+1*yStep) + (+0*zStep), 15 },
			{ (+1*xStep) + (+0*yStep) + (+0*zStep), 16 },
			{ (-1*xStep) + (+0*yStep) + (+0*zStep), 17 },
			{ (+1*xStep) + (-1*yStep) + (+0*zStep), 18 },
			{ (+0*xStep) + (-1*yStep) + (+0*zStep), 19 },
			{ (-1*xStep) + (-1*yStep) + (+0*zStep), 20 },

			{ (+1*xStep) + (+1*yStep) + (-1*zStep), 21 },
			{ (+0*xStep) + (+1*yStep) + (-1*zStep), 22 },
			{ (-1*xStep) + (+1*yStep) + (-1*zStep), 23 },
			{ (+1*xStep) + (+0*yStep) + (-1*zStep), 24 },
			{ (+0*xStep) + (+0*yStep) + (-1*zStep), 25 },
			{ (-1*xStep) + (+0*yStep) + (-1*zStep), 26 },
			{ (+1*xStep) + (-1*yStep) + (-1*zStep), 27 },
			{ (+0*xStep) + (-1*yStep) + (-1*zStep), 28 },
			{ (-1*xStep) + (-1*yStep) + (-1*zStep), 29 },

			// bit 30 used for IsGrown
			// bit 31 used for IsCover
			// bit 32-35 used for Clearance

		};
		// create the reverse mapping, given an edge bit index, return the NodeHandle delta
		// conceptually, a Dictionary<edge bit index, NodeHandle offset>, but edge bit index is always 0-29, so an array is enough
		// to follow node.edge[i], next = node + edgeOffsets[i]
		private static readonly long[] edgeOffsets = whichEdgeBit.Keys.OrderBy(k => whichEdgeBit[k]).ToArray();

		/// <summary>
		/// All (30) possible neighbors of the given node.
		/// </summary>
		public static IEnumerable<NodeHandle> PossibleEdges(NodeHandle node) {
			var a = new NodeHandle[30];
			for( int i = 0; i < 30; i++ ) {
				a[i] = (NodeHandle)((long)node + edgeOffsets[i]);
			}
			return a;
		}

		/// <summary>
		/// Given two nodes, are they at an offset that matches a valid edge bit.
		/// </summary>
		public static bool IsPossibleEdge(NodeHandle a, NodeHandle b) => whichEdgeBit.ContainsKey((int)b - (int)a);

		/// <summary>
		/// Does this node have at least one connection to a neighbor.
		/// </summary>
		public static bool HasEdges(NodeHandle a) => AllEdges.TryGetValue(a, out var flags) && (flags & NodeEdges.EdgeMask) != 0;


		/// <summary>
		/// All neighbors connected to the given node.
		/// </summary>
		public static IEnumerable<NodeHandle> Edges(NodeHandle a) {
			if( AllEdges.TryGetValue(a, out var flags) ) {
				long node = (long)a;
				for( int i = 0; i < edgeOffsets.Length; i++ ) {
					if( (flags & (NodeEdges)(1ul << i)) > 0 ) {
						yield return (NodeHandle)(node + edgeOffsets[i]);
					}
				}
			}
		}
		public static bool HasEdge(NodeHandle a, NodeHandle b) {
			long d = (long)b - (long)a;
			if( !whichEdgeBit.ContainsKey(d) ) {
				return false;
			}
			// retrieve all the edges of a and check the bit for b
			return AllEdges.TryGetValue(a, out var edges)
				? (edges & (NodeEdges)(1ul << whichEdgeBit[d])) > 0
				: false;
		}
		public static bool SetEdge(NodeHandle a, NodeHandle b, bool value) {
			long d = (long)b - (long)a;
			if( !whichEdgeBit.ContainsKey(d) ) {
				return false;
			}
			var mask = (NodeEdges)(1ul << whichEdgeBit[d]);
			if( value ) {
				AllEdges.AddOrUpdate(a, mask, (k, oldValue) => oldValue | mask);
			} else {
				AllEdges.AddOrUpdate(a, 0, (k, oldValue) => oldValue & ~mask);
			}
			dirtyRegions.Add(Region(a));
			dirtyRegions.Add(Region(b));
			return true;
		}
		public static bool AddEdge(NodeHandle a, NodeHandle b) => SetEdge(a, b, true);
		public static void RemoveEdge(NodeHandle a, NodeHandle b) => SetEdge(a, b, false);

		public static bool IsGrown(NodeHandle a) => AllEdges.TryGetValue(a, out var flags) && (flags & NodeEdges.IsGrown) > 0;
		public static void IsGrown(NodeHandle a, bool value) {
			if( value ) {
				AllEdges.AddOrUpdate(a, NodeEdges.IsGrown, (key, oldValue) => oldValue | NodeEdges.IsGrown);
			} else {
				AllEdges.AddOrUpdate(a, 0, (key, oldValue) => oldValue & ~NodeEdges.IsGrown);
			}
			dirtyRegions.Add(Region(a));
		}

		public static bool IsCover(NodeHandle a) => AllEdges.TryGetValue(a, out var flags) && (flags & NodeEdges.IsCover) > 0;
		public static void IsCover(NodeHandle a, bool value) {
			if( value ) {
				AllEdges.AddOrUpdate(a, NodeEdges.IsCover, (key, oldValue) => oldValue | NodeEdges.IsCover);
			} else {
				AllEdges.AddOrUpdate(a, 0, (key, oldValue) => oldValue & ~NodeEdges.IsCover);
			}
			dirtyRegions.Add(Region(a));
		}

		public static uint Clearance(NodeHandle a) => AllEdges.TryGetValue(a, out NodeEdges value) ? (uint)((ulong)(value & NodeEdges.ClearanceMask) >> 32) : 0;
		public static void Clearance(NodeHandle a, uint value) {
			value = Min(15, value);
			var mask = (NodeEdges)((ulong)value << 32);
			AllEdges.AddOrUpdate(a, mask, (key, oldValue) => (oldValue & ~NodeEdges.ClearanceMask) | mask);
		}
		public static void PropagateClearance(ConcurrentQueue<NodeHandle> queue) {
			while( queue.TryDequeue(out var a) ) {
				var c = Clearance(a);
				foreach(var e in Edges(a) ) {
					var d = Clearance(e);
					if( d == 0 || d > c + 1 ) {
						Clearance(e, c + 1);
						queue.Enqueue(e);
					} else if( d < c - 1 ) {
						Clearance(a, d + 1);
						queue.Enqueue(a);
					}
				}
			}
		}
		public static void PropagateClearance(NodeHandle a, uint value) {
			Clearance(a, value);
			var queue = new ConcurrentQueue<NodeHandle>();
			queue.Enqueue(a);
			PropagateClearance(queue);
		}
	}
}
