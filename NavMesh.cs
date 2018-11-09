using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using static System.Math;
using static Shiv.Globals;

// pragma warning disable CS0649
namespace Shiv {


	public static partial class Globals {

		public enum NodeHandle : ulong { Invalid = 0 };

		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static float DistanceToSelf(NodeHandle n) => DistanceToSelf(NavMesh.Position(n));

	}

	public class NavMesh : Script {

		public static bool Enabled = true;
		public static bool LoadEnabled = false;
		public static bool SaveEnabled = false;
		private const string DataFile = "scripts/LongMesh.dat";
		private const ulong map_adjust = 8192;
		private const float zScale = .25f;
		private const int zShift = 28; // 268435456
		private const int yShift = 14;
		private const int xShift = 0;
		private const IntersectOptions growRayOpts = IntersectOptions.Map | IntersectOptions.Objects;
		private const float capsuleSize = 0.25f;
		private const float maxGrowRange = 10f * 10f;
		internal static HashSet<NodeHandle> Ungrown = new HashSet<NodeHandle>();

		public static NodeHandle PlayerNode;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static NodeHandle GetHandle(Vector3 v) {
			if( v == Vector3.Zero ) return NodeHandle.Invalid;
			ulong x = (ulong)(map_adjust + Round(v.X, 0));
			ulong y = (ulong)(map_adjust + Round(v.Y, 0));
			return (NodeHandle)(x + (y << yShift) + ((ulong)(v.Z / zScale) << zShift));
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static Vector3 Position(NodeHandle handle) {
			ulong rem = (ulong)handle & ((1L << zShift) - 1);
			float z = (long)(handle - rem) >> zShift;
			ulong x = rem & ((1L << yShift) - 1);
			float y = (rem - x) >> yShift;
			return new Vector3(((float)x) - map_adjust, y - map_adjust, z * zScale);
		}

		public NavMesh() { }

		private static uint started = 0;
		private static uint loaded = 0;
		private static uint saved = 0;
		private static uint aborted = 0;
		public static ConcurrentDictionary<NodeHandle, int> AllEdges = new ConcurrentDictionary<NodeHandle, int>();
		public static bool Dirty = false;
		private const uint zStep = 268435456;
		private static readonly Dictionary<long, int> whichEdgeBit = new Dictionary<long, int>() {

			{ 536887296, 0 }, // (+0*xScale) + (+1*yScale) + (+2*zScale),
			{ 536870913, 1 }, // (+1*xScale) + (+0*yScale) + (+2*zScale),
			{ 536870911, 2 }, // (-1*xScale) + (+0*yScale) + (+2*zScale),
			{ 536854528, 3 }, // (+0*xScale) + (-1*yScale) + (+2*zScale),

			{ 268451841, 4 }, // (+1*xScale) + (+1*yScale) + (+1*zScale),
			{ 268451840, 5 }, // (+0*xScale) + (+1*yScale) + (+1*zScale),
			{ 268451839, 6 }, // (-1*xScale) + (+1*yScale) + (+1*zScale),
			{ 268435457, 7 }, // (+1*xScale) + (+0*yScale) + (+1*zScale),
			{ 268435456, 8 }, // (+0*xScale) + (+0*yScale) + (+1*zScale),
			{ 268435455, 9 }, // (-1*xScale) + (+0*yScale) + (+1*zScale),
			{ 268419073, 10 }, // (+1*xScale) + (-1*yScale) + (+1*zScale),
			{ 268419072, 11 }, // (+0*xScale) + (-1*yScale) + (+1*zScale),
			{ 268419071, 12 }, // (-1*xScale) + (-1*yScale) + (+1*zScale),

			{ 16385, 13 }, // (+1*xScale) + (+1*yScale) + (+0*zScale),
			{ 16384, 14 }, // (+0*xScale) + (+1*yScale) + (+0*zScale),
			{ 16383, 15 }, // (-1*xScale) + (+1*yScale) + (+0*zScale),
			{ 1, 16 }, // (+1*xScale) + (+0*yScale) + (+0*zScale),
			// (+0*xScale) + (+0*yScale) + (+0*zScale), // skip self links, so we have 2 bits leftover
			{ -1, 17 }, // (-1*xScale) + (+0*yScale) + (+0*zScale),
			{ -16383, 18 }, // (+1*xScale) + (-1*yScale) + (+0*zScale),
			{ -16384, 19 }, // (+0*xScale) + (-1*yScale) + (+0*zScale),
			{ -16385, 20 }, // (-1*xScale) + (-1*yScale) + (+0*zScale),

			{ -268419071, 21 }, // (+1*xScale) + (+1*yScale) + (-1*zScale),
			{ -268419072, 22 }, // (+0*xScale) + (+1*yScale) + (-1*zScale),
			{ -268419073, 23 }, // (-1*xScale) + (+1*yScale) + (-1*zScale),
			{ -268435455, 24 }, // (+1*xScale) + (+0*yScale) + (-1*zScale),
			{ -268435456, 25 }, // (+0*xScale) + (+0*yScale) + (-1*zScale),
			{ -268435457, 26 }, // (-1*xScale) + (+0*yScale) + (-1*zScale),
			{ -268451839, 27 }, // (+1*xScale) + (-1*yScale) + (-1*zScale),
			{ -268451840, 28 }, // (+0*xScale) + (-1*yScale) + (-1*zScale),
			{ -268451841, 29 }, // (-1*xScale) + (-1*yScale) + (-1*zScale),
			
			// bits 30, 31 left over

		};
		public static readonly long[] edgeOffsets = whichEdgeBit.Keys.OrderByDescending(k => k).ToArray();
		public static bool IsLoaded { get => AllEdges != null && aborted == 0; }
		public static int Count { get => AllEdges == null ? 0 : AllEdges.Count; }

		private NodeHandle prevNode;
		public override void OnTick() {
			if( (!Enabled) || aborted != 0 ) return;
			PlayerNode = GetHandle(PlayerPosition);
			if( PlayerNode != prevNode ) {
				AddEdge(prevNode, PlayerNode);
			}
			prevNode = PlayerNode;
			if( started == 0 ) {
				started = GameTime;
			} else if( GameTime - started < 1000 ) {
				UI.DrawText(.55f, .45f, $"Warmup time...{GameTime - started}ms");
			} else if( LoadEnabled && loaded == 0 ) {
				loaded = GameTime;
				Task.Run(() => {
					ReadFromFile(DataFile);
					saved = GameTime;
				});
			} else if( AllEdges != null ) {
				if( SaveEnabled && GameTime - saved > 90000 ) {
					saved = GameTime;
					Shiv.Log("Saving NavMesh...");
					Task.Run(() => { SaveToFile(DataFile); });
				}
				UI.DrawText($"PlayerNode: {PlayerNode} {DistanceToSelf(PlayerNode):F2}m", color: IsGrown(PlayerNode) ? Color.White : Color.Orange);
				UI.DrawText($"Ungrown: {Ungrown.Count}/{AllEdges.Count}");
				if( !IsGrown(PlayerNode) ) {
					Grow(PlayerNode, 5);
				} else {
					DrawEdges(PlayerNode, 3);
					if( Ungrown.Count > 0 ) {
						NodeHandle first = NodeHandle.Invalid;
						if( Ungrown.Count > 1000 ) {
							var nodes = Ungrown.OrderBy(DistanceToSelf).Take(500).ToArray();
							first = nodes[0];
							Ungrown.Clear();
							foreach( var n in nodes ) Ungrown.Add(n);
						} else {
							first = Ungrown.OrderBy(DistanceToSelf).First();
						}
						Ungrown.Remove(first);
						// DrawLine(HeadPosition(Self), Position(item), Color.Orange);
						Grow(first, 5);
					}
					// if( Ungrown.Count > 0 ) { foreach(var n in Ungrown ) { DrawSphere(Position(n), .5f, Color.Orange); } }
				}
			}
		}

		private void DrawEdges(NodeHandle node, uint depth) {
			DrawEdges(node, Position(node), depth, new HashSet<NodeHandle>());
		}
		private void DrawEdges(NodeHandle node, Vector3 nodePos, uint depth, HashSet<NodeHandle> stack) {
			if( depth <= 0 ) return;
			stack.Add(node);
			foreach( var e in Edges(node) ) {
				var ePos = Position(e);
				DrawLine(nodePos, Position(e), Color.Yellow);
				if( !stack.Contains(e) ) {
					DrawEdges(e, ePos, depth - 1, stack);
				}
			}
			stack.Remove(node);
		}

		internal static void Grow(NodeHandle node, uint ms) {
			var sw = new Stopwatch();
			sw.Start();
			Grow(node, ms, sw, Ungrown);
			// UI.DrawText($"Grew for {sw.Elapsed}");
		}
		private static void Grow(NodeHandle node, uint ms, Stopwatch sw, HashSet<NodeHandle> stack) {
			if( NavMesh.IsLoaded ) {
				Vector3 nodePos;
				if( node == NodeHandle.Invalid
					|| stack.Contains(node)
					|| IsGrown(node)
					) {
					return;
				}
				stack.Add(node);
				if( DistanceToSelf(nodePos = Position(node)) > maxGrowRange
					|| sw.ElapsedMilliseconds > ms ) {
					return;
				}

				int growCount = 0;
				// Push growth out on certain edges by following their edgeOffsets
				Items(0, 1, 2, 3 , 4, 6, 8, 10, 12).Select<int, NodeHandle>(i => {
					var e = (NodeHandle)((long)node + edgeOffsets[i]);
					var ePos = Position(e);
					// Line.Add(nodePos, ePos, Color.Green, 15000);
					var gPos = PutOnGround(ePos, 1f);
					// Line.Add(ePos, gPos, Color.Red, 15000);
					NodeHandle g = GetHandle(gPos);
					if( IsPossibleEdge(node, g) && !HasEdge(node, g) ) {
						var delta = gPos - nodePos;
						var len = delta.Length();
						Vector3 end = nodePos + (Vector3.Normalize(delta) * (len - capsuleSize));
						// Line.Add(nodePos, end, Color.Orange, 15000);
						var result = Raycast(nodePos, end, capsuleSize, growRayOpts, Self);
						if( result.DidHit ) {
							// Sphere.Add(gPos, .1f, Color.Red, 15000);
							/*
							if( Exists(result.Entity) ) {
								Text.Add(result.HitPosition, $"{result.Entity}", 1000);
								var model = GetModel(result.Entity);
								if( model == ModelHash.WoodenDoor ) {
									// GetDoorState(model, gPos, out bool locked, out float heading);
									// if( ! locked ) {
										AddEdge(node, g);
										AddEdge(g, node);
										return g;
									// }
								}
							}
							*/
							var m = result.Material;
							if( m != Materials.metal_railing
								&& m != Materials.metal_garage_door
								&& m != Materials.bushes
								&& Abs(Vector3.Dot(result.SurfaceNormal, Up)) < .01f ) {
								IsCover(node, true);
								Sphere.Add(nodePos, .1f, Color.Blue, 2000);
								return NodeHandle.Invalid;
							}
						} else {
							// Shiv.Log($"Adding Edge {node} => {g} (offset {g - node})");
							AddEdge(node, g);
							AddEdge(g, node);
							return g;
						}
					}
					return NodeHandle.Invalid;
				}).Each(e => {
					if( e != NodeHandle.Invalid ) {
						growCount += 1;
						Grow(e, ms, sw, stack);
					}
				});
				IsGrown(node, true);
				stack.Remove(node);
			}
		}

		public override void OnAbort() {
			aborted = GameTime;
			if( AllEdges != null ) {
				AllEdges.Clear();
				AllEdges = null;
			}
		}

		public static IEnumerable<NodeHandle> Edges(NodeHandle a) {
			if( AllEdges == null ) yield break;
			if( AllEdges.TryGetValue(a, out int flags) ) {
				for( int i = 0; i < 30; i++ ) {
					if( (flags & (1 << i)) > 0 ) {
						yield return (NodeHandle)((long)a + edgeOffsets[i]);
					}
				}
			}
		}

		public static bool IsPossibleEdge(NodeHandle a, NodeHandle b) => whichEdgeBit.ContainsKey((long)b - (long)a);
		public static bool HasEdge(NodeHandle a, NodeHandle b) {
			long d = (long)b - (long)a;
			if( !whichEdgeBit.ContainsKey(d) ) return false;

			// retrieve all the edges of a and check the bit for b
			return AllEdges.TryGetValue(a, out int edges)
				? (edges & (1 << whichEdgeBit[d])) > 0
				: false;
		}

		private const int coverFlag = 31;
		private const int grownFlag = 30;
		public static bool Remove(NodeHandle a) {
			return AllEdges == null ? false : AllEdges.TryRemove(a, out int edges);
		}
		internal static int GetRawEdges(NodeHandle a) {
			if( AllEdges == null ) return 0;
			AllEdges.TryGetValue(a, out int edges);
			int mask = (1 << grownFlag) | (1 << coverFlag);
			return edges & ~mask;
		}
		internal static void SetRawEdges(NodeHandle a, int edges, bool grown, bool cover) {
			if( AllEdges == null ) return;
			int g = grown ? (1 << grownFlag) : 0;
			int c = cover ? (1 << coverFlag) : 0;
			edges = edges | g | c;
			AllEdges.AddOrUpdate(a, edges, (k,v) => edges);
			Dirty = true;
		}
		public static bool SetEdge(NodeHandle a, NodeHandle b, bool value) {
			if( AllEdges == null ) return false;
			long d = (long)b - (long)a;
			if( !whichEdgeBit.ContainsKey(d) ) {
				return false;
			}
			int mask = (1 << whichEdgeBit[d]);
			if( value ) {
				AllEdges.AddOrUpdate(a, mask, (k, oldValue) => oldValue | mask);
			} else {
				AllEdges.AddOrUpdate(a, 0, (k, oldValue) => oldValue & ~mask);
			}
			Dirty = true;
			return true;
		}
		public static bool AddEdge(NodeHandle a, NodeHandle b) => SetEdge(a, b, true);
		public static void RemoveEdge(NodeHandle a, NodeHandle b, bool safe = true) => SetEdge(a, b, false);
		public static bool IsGrown(NodeHandle a) => AllEdges == null ? false : AllEdges.TryGetValue(a, out int flags) && (flags & (1 << grownFlag)) > 0;
		public static void IsGrown(NodeHandle a, bool value) {
			if( AllEdges == null ) return;
			int mask = (1 << grownFlag);
			if( value ) {
				AllEdges.AddOrUpdate(a, mask, (key, oldValue) => oldValue | mask);
			} else {
				AllEdges.AddOrUpdate(a, 0, (key, oldValue) => oldValue & ~mask);
			}
			Dirty = true;
		}
		public static bool IsCover(NodeHandle a) => AllEdges == null ? false : AllEdges.TryGetValue(a, out int flags) && (flags & (1 << coverFlag)) > 0;
		public static void IsCover(NodeHandle a, bool value) {
			if( AllEdges == null ) return;
			int mask = (1 << coverFlag);
			if( value ) {
				AllEdges.AddOrUpdate(a, mask, (key, oldValue) => oldValue | mask);
			} else {
				AllEdges.AddOrUpdate(a, 0, (key, oldValue) => oldValue & ~mask);
			}
			Dirty = true;
		}

		public static IEnumerable<NodeHandle> GetAllHandlesInBox(Matrix4x4 m, Vector3 backLeft, Vector3 frontRight) {
			float minX = Min(backLeft.X, frontRight.X);
			float minY = Min(backLeft.Y, frontRight.Y);
			float minZ = Min(backLeft.Z, frontRight.Z);
			float maxX = Max(backLeft.X, frontRight.X);
			float maxY = Max(backLeft.Y, frontRight.Y);
			float maxZ = Max(backLeft.Z, frontRight.Z);
			for( float x = minX; x < maxX + .5f; x += .5f ) {
				for( float y = minY; y < maxY + .5f; y += .5f ) {
					for( float z = minZ; z < maxZ + .1f; z += .25f ) {
						var v = new Vector3(x, y, z);
						var p = Vector3.Transform(v, m);
						// UI.DrawTextInWorld(p, $"{x:F1}/{maxX:F1}");
						yield return GetHandle(p);
					}
				}
			}
		}

		public static Color GetColor(NodeHandle a) {
			return (IsCover(a) ? Color.Blue :
				IsGrown(a) ? Color.Gray :
				Color.White);
		}


		public static bool ReadFromFile(string filename) {
			Stopwatch s = new Stopwatch();
			s.Start();
			try {
				using( BinaryReader r = Codec.Reader(filename) ) {
					Debug($"File open after {s.Elapsed}");
					int magic = r.ReadInt32();
					if( magic != magicBytes ) {
						Debug($"Wrong magic bytes {magic:X}");
						return false;
					}
					int count = r.ReadInt32();
					if( count <= 0 ) {
						Debug($"Invalid count: {count}");
						return false;
					}
					byte[] buf;
					ulong[] handles = new ulong[count];
					int[] edges = new int[count];
					buf = r.ReadBytes(handles.Length * sizeof(ulong));
					Buffer.BlockCopy(buf, 0, handles, 0, buf.Length);
					buf = r.ReadBytes(edges.Length * sizeof(int));
					Buffer.BlockCopy(buf, 0, edges, 0, buf.Length);
					Debug($"Reading {count} nodes, {handles.Length * sizeof(ulong)} bytes of handles.");
					// Debug($"First few handles: {String.Join(", ", handles.Take(3))}");
					// Debug($"First few edges: {String.Join(", ", edges.Take(3))}");
					var ret = new ConcurrentDictionary<NodeHandle, int>();
					int zeroCount = 0;
					Parallel.For(0, count, (i) => {
						if( edges[i] != 0 ) {
							ret.TryAdd((NodeHandle)handles[i], edges[i]);
						} else zeroCount++;
					});
					Debug($"Finished loading {ret.Count} nodes, after {s.Elapsed} ({zeroCount} nodes with no edges)");
					AllEdges = ret;
				}
			} catch( FileNotFoundException ) {
				Debug("File not found: {0}", filename);
				AllEdges = new ConcurrentDictionary<NodeHandle, int>();
			}
			Debug($"ReadFromFile complete after ${s.Elapsed}.");
			Dirty = false;
			s.Stop();
			return true;
		}
		private const int magicBytes = 0x000FEED0;
		public static void SaveToFile(string filename="scripts/LongMesh.dat") {
			if( !Dirty ) return;
			using( BinaryWriter w = Codec.Writer(filename+".tmp") ) {
				w.Write(magicBytes);
				NodeHandle[] handles;
				int[] edges;
				Shiv.Log("Reading handles array...");
				handles = AllEdges.Keys.ToArray();
				edges = handles.Select(h => AllEdges[h]).ToArray();
				Shiv.Log("Writing bytes to file...");
				w.Write(handles.Length);
				byte[] buf = new byte[handles.Length * sizeof(NodeHandle)];
				Buffer.BlockCopy(handles, 0, buf, 0, buf.Length);
				w.Write(buf);
				buf = new byte[edges.Length * sizeof(int)];
				Buffer.BlockCopy(edges, 0, buf, 0, buf.Length);
				w.Write(buf);
				w.Close();
			}
			try { File.Delete(filename); } catch( FileNotFoundException ) { }
			File.Move(filename + ".tmp", filename);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static IEnumerable<NodeHandle> Select(NodeHandle n, int maxDepth) => Select(n, maxDepth, null, new HashSet<NodeHandle>());
		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static IEnumerable<NodeHandle> Select(NodeHandle n, int maxDepth, Predicate<NodeHandle> pred) => Select(n, maxDepth, pred, new HashSet<NodeHandle>());
		private static IEnumerable<NodeHandle> Select(NodeHandle n, int maxDepth, Predicate<NodeHandle> pred, HashSet<NodeHandle> stack) {
			if( AllEdges != null
				&& AllEdges.ContainsKey(n)
				&& stack.Count <= maxDepth
				&& !stack.Contains(n) ) {
				if( (pred == null) || pred(n) ) yield return n;
				stack.Add(n);
				foreach( var e in Edges(n) )
					foreach( var r in Select(e, maxDepth, pred, stack) )
						yield return r;
				stack.Remove(n);
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static NodeHandle FirstOrDefault(NodeHandle n, int maxDepth, Predicate<NodeHandle> pred) => FirstOrDefault(n, maxDepth, pred, new HashSet<NodeHandle>());
		private static NodeHandle FirstOrDefault(NodeHandle n, int maxDepth, Predicate<NodeHandle> pred, HashSet<NodeHandle> stack) {
			foreach( NodeHandle x in Select(n, maxDepth, pred, stack) ) {
				return x;
			}
			return 0;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void Visit(NodeHandle n, int maxDepth, Action<NodeHandle> action) => Visit(n, maxDepth, action, new HashSet<NodeHandle>());
		private static void Visit(NodeHandle n, int maxDepth, Action<NodeHandle> action, HashSet<NodeHandle> stack) {
			if( n == 0
				|| stack == null
				|| stack.Count > maxDepth
				|| stack.Contains(n) ) return;
			else {
				action(n);
				stack.Add(n);
				foreach( NodeHandle e in Edges(n) ) {
					Visit(e, maxDepth, action, stack);
				}
				stack.Remove(n);
			}
		}


	}

}
