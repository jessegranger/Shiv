using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
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
		private static readonly uint xStep = (uint)Pow(2, 15+13); // if you add X+1, how much does the NodeHandle change
		private static readonly uint yStep = (uint)Pow(2, 13); // if you add Y+1, how much does the NodeHandle change
		private static readonly uint zStep = (uint)Pow(2, 0); // if you add Z+1, how much does the NodeHandle change

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
				a[i] = AddEdgeOffset(node, i);// (NodeHandle)((long)node + edgeOffsets[i]);
			}
			return a;
		}

		/// <summary>
		/// Given two nodes, are they at an offset that matches a valid edge bit.
		/// </summary>
		public static bool IsPossibleEdge(NodeHandle a, NodeHandle b) => whichEdgeBit.ContainsKey(
			(long)((ulong)b & handleMask) -
			(long)((ulong)a & handleMask));


		public static bool HasFlag(NodeHandle n, NodeEdges flag) => (AllNodes.Get(n) & flag) != 0;

		/// <summary>
		/// Does this node have at least one connection to a neighbor.
		/// </summary>
		public static bool HasEdges(NodeHandle a) => HasFlag(a, NodeEdges.EdgeMask);

		/// <summary>
		/// Yield all neighbors connected to the given node.
		/// </summary>
		public static IEnumerable<NodeHandle> Edges(NodeHandle a) => Edges(a, AllNodes.Get(a));
		public static IEnumerable<NodeHandle> Edges(NodeHandle a, NodeEdges e) {
			for( int i = 0; i < edgeOffsets.Length; i++ ) {
				if( e.HasFlag((NodeEdges)(1ul << i)) ) {
					yield return AddEdgeOffset(a, i);
				}
			}
		}
		public static bool HasEdge(NodeEdges e, NodeHandle a, NodeHandle b) {
			long d = (long)((ulong)b & handleMask) - (long)((ulong)a & handleMask);
			if( !whichEdgeBit.ContainsKey(d) ) {
				return false;
			}
			return e.HasFlag((NodeEdges)(1ul << whichEdgeBit[d]));
		}
		public static bool HasEdge(NodeHandle a, NodeHandle b) {
			long d = (long)((ulong)b & handleMask) - (long)((ulong)a & handleMask);
			if( !whichEdgeBit.ContainsKey(d) ) {
				return false;
			}
			return HasFlag(a, (NodeEdges)(1ul << whichEdgeBit[d]));
		}
		public static bool SetEdge(NodeHandle a, NodeHandle b, bool value) {
			long d = (long)((ulong)b & handleMask) - (long)((ulong)a & handleMask);
			if( !whichEdgeBit.ContainsKey(d) ) {
				return false;
			}
			var mask = (NodeEdges)(1ul << whichEdgeBit[d]);
			if( value ) {
				AllNodes.AddOrUpdate(a, mask, (k, oldValue) => oldValue | mask);
			} else {
				AllNodes.AddOrUpdate(a, 0, (k, oldValue) => oldValue & ~mask);
			}
			AllNodes.SetDirty(a);
			AllNodes.SetDirty(b);
			return true;
		}
		public static NodeEdges AddEdge(NodeEdges e, NodeHandle a, NodeHandle b) {
			long d = (long)((ulong)b & handleMask) - (long)((ulong)a & handleMask);
			return !whichEdgeBit.ContainsKey(d) ? e : e | (NodeEdges)(1ul << whichEdgeBit[d]);
		}
		public static bool AddEdge(NodeHandle a, NodeHandle b) => SetEdge(a, b, true);
		public static void RemoveEdge(NodeHandle a, NodeHandle b) => SetEdge(a, b, false);
		public static NodeEdges GetEdges(NodeHandle a) => AllNodes.Get(a);
		public static void SetEdges(NodeHandle a, NodeEdges e) => AllNodes.Set(a, e);
		

		public static bool IsGrown(NodeEdges e) => e.HasFlag(NodeEdges.IsGrown);
		public static bool IsGrown(NodeHandle a) => HasFlag(a, NodeEdges.IsGrown);
		public static NodeEdges IsGrown(NodeEdges e, bool value) => value ? e | NodeEdges.IsGrown : e & ~NodeEdges.IsGrown;
		public static void IsGrown(NodeHandle a, bool value) {
			AllNodes.AddOrUpdate(a, value ? NodeEdges.IsGrown : 0, (key, e) => IsGrown(e, value));
			AllNodes.SetDirty(a);
		}

		public static bool IsCover(NodeHandle a) => HasFlag(a, NodeEdges.IsCover); // AllEdges.TryGetValue(a, out var flags) && (flags & NodeEdges.IsCover) > 0;
		public static NodeEdges IsCover(NodeEdges e, bool value) => value ? e | NodeEdges.IsCover : e & ~NodeEdges.IsCover;
		public static void IsCover(NodeHandle a, bool value) {
			AllNodes.AddOrUpdate(a, value ? NodeEdges.IsCover : 0, (key, e) => IsCover(e, value));
			AllNodes.SetDirty(a);
		}

		public static uint Clearance(NodeEdges e) => (uint)((ulong)(e & NodeEdges.ClearanceMask) >> 32);
		public static uint Clearance(NodeHandle a) => Clearance(AllNodes.Get(a));
		public static NodeEdges Clearance(NodeEdges e, uint value) {
			var mask = (NodeEdges)((ulong)value << 32);
			return (e & ~NodeEdges.ClearanceMask) | mask;
		}
		public static void Clearance(NodeHandle a, uint value) {
			value = Min(15, value);
			var mask = (NodeEdges)((ulong)value << 32);
			AllNodes.AddOrUpdate(a, mask, (key, e) => Clearance(e, value));
		}

		public static void PropagateClearance(NodeHandle a, uint value) {
			Clearance(a, value);
			PropagateClearance(a);
		}

		public static void PropagateClearance(NodeHandle a) {
			var q = new Queue<NodeHandle>();
			q.Enqueue(a);
			PropagateClearance(q);
		}

		public static void PropagateClearance(Queue<NodeHandle> queue) {
			while( queue.TryDequeue(out NodeHandle n) ) {
				var nEdges = GetEdges(n);
				var c = Clearance(nEdges);
				foreach( var e in Edges(n, nEdges) ) {
					var d = Clearance(e);
					if( d == 0 || d > c + 1 ) {
						Clearance(e, c + 1);
						queue.Enqueue(e);
					} else if( d < c - 1 ) {
						Clearance(n, d + 1);
						queue.Enqueue(n);
					}
				}
			}
		}

	}
}
