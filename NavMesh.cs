using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Math;
using static Shiv.Global;
using static GTA.Native.Hash;
using static GTA.Native.Function;
using System.Threading;
using GTA.Native;

// pragma warning disable CS0649
namespace Shiv {

	public enum NodeHandle : ulong { Invalid = 0 };
	public enum RegionHandle : uint { Invalid = 0 };

	public static partial class Global {

		public static NodeHandle PlayerNode;
		public static RegionHandle PlayerRegion;

		public static float DistanceToSelf(NodeHandle n) => DistanceToSelf(NavMesh.Position(n));

		public static NodeHandle AimNode() => NavMesh.GetHandle(StopAtWater(PutOnGround(NavMesh.Position(NavMesh.GetHandle(AimPosition())) + (Up * .1f), 1f), 0f));

	}

	public partial class NavMesh : Script {

		public static bool Enabled = true;
		public static bool SaveEnabled = true;
		public static bool LoadEnabled = true;

		private const int magicBytes = 0x000FEED7; // if any constants/structure below should change, increment the magic bytes to invalidate the old data

		private const ulong mapRadius = 8192; // in the world, the map goes from -8192 to 8192
		private const float gridScale = .5f; // how big are the X,Y steps of the mesh
		private const int gridScaleShift = 1;
		private const float zScale = .25f; // how big are the Z steps of the mesh
		private const int zScaleShift = 2;
		private const float zDepth = 1000f; // how deep underwater can the mesh go
		// zDepth can be at most mapRadius*zScale, because zScale changes vertical node density
		// which makes mapShift too small, which corrupts NodeHandles

		public static NodeHandle LastGrown { get; private set; } = NodeHandle.Invalid;

		// how to pack Vector3 into a long:
		// add the mapRadius (so strictly positive)
		// divide X,Y,Z by their scales
		// round to a grid center and cast to ulong
		// knowing that all values will be in [0..mapRadius*2/scale] = 32k = 15 bits per coord
		// storing X,Y,Z this way uses 3 * 15 bits = 45 bits = 1 ulong with 19 bits to spare
		private static readonly int mapShift = (int)Math.Log(mapRadius * 2 / gridScale, 2);
		private static readonly ulong nodeHandleMask = (1u << (3 * mapShift)) - 1;
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static NodeHandle GetHandle(Vector3 v, bool debug=false) {
			if( v == Vector3.Zero ) {
				return NodeHandle.Invalid;
			}
			// v.X starts [-8192..8192] becomes [0..32k]
			ulong x = (ulong)Round((v.X + mapRadius) / gridScale);
			ulong y = (ulong)Round((v.Y + mapRadius) / gridScale);
			ulong z = (ulong)Round((v.Z + zDepth) / zScale);
			return (NodeHandle)( (x << (mapShift << 1)) | (y << mapShift) | z );
		}

		private const float regionScale = 128f;
		private const int regionShift = 7; // we use 3*7=21 bits of a NavRegion:uint
		
		public static RegionHandle Region(Vector3 v) {
			if( v == Vector3.Zero ) {
				return RegionHandle.Invalid;
			}
			// v.X starts [-8192..8192] becomes [0..128]
			uint x = (uint)(Round((v.X + mapRadius) / regionScale));
			uint y = (uint)(Round((v.Y + mapRadius) / regionScale));
			uint z = (uint)(Round((v.Z + zDepth) / regionScale));
			return (RegionHandle)( (x << (regionShift << 1)) | (y << regionShift) | z );
		}
		private static readonly uint mapShiftMask = (1u << mapShift) - 1;
		public static RegionHandle Region(NodeHandle a) {
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
		}

		// how to unpack a (reduced accuracy) Vector3 from inside a ulong
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static Vector3 Position(NodeHandle handle) {
			if( handle == NodeHandle.Invalid ) {
				return Vector3.Zero;
			}

			ulong mask = (ulong)(1 << mapShift) - 1; // make a 15-bit mask
			ulong h = (ulong)handle;
			return new Vector3(
				(gridScale * ((h >> (mapShift << 1)) & mask)) - mapRadius,
				(gridScale * ((h >> mapShift) & mask)) - mapRadius,
				(zScale * (h & mask)) - zDepth
			);
		}

		// constants to use when expanding the mesh
		private const IntersectOptions growRayOpts = IntersectOptions.Map | IntersectOptions.Objects | IntersectOptions.Water| IntersectOptions.Unk64 | IntersectOptions.Unk128 | IntersectOptions.Unk512;
		private const float capsuleSize = 0.30f; // when checking for obstructions, how big of a box to use for collision
		private const float maxGrowRange = 70f * 70f; // dont trust the game to have geometry loaded farther than this

		// how are edges (connections between nodes), defined
		// each NodeHandle (ulong) maps to a NodeEdges (uint)
		// a bit-field for which next NodeHandle we are connected to
		[Flags] public enum NodeEdges : ulong {
			Empty = 0,
			/* 0 - 29 used for connections to neighbors */
			IsGrown = (1u << 30),
			IsCover = (1u << 31),
		}
		private static readonly uint xStep = (uint)Pow(2, (2*mapShift)); // if you add X+1, how much does the NodeHandle change
		private static readonly uint yStep = (uint)Pow(2, (1*mapShift)); // if you add Y+1, how much does the NodeHandle change
		private static readonly uint zStep = (uint)Pow(2, (0*mapShift)); // if you add Z+1, how much does the NodeHandle change
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

		};

		public static bool IsGrown(NodeHandle a) => AllEdges.TryGetValue(a, out var flags) && (flags & NodeEdges.IsGrown) > 0;
		public static void IsGrown(NodeHandle a, bool value) {
			if( value ) {
				AllEdges.AddOrUpdate(a, NodeEdges.IsGrown, (key, oldValue) => oldValue | NodeEdges.IsGrown);
			} else {
				AllEdges.AddOrUpdate(a, 0, (key, oldValue) => oldValue & ~NodeEdges.IsGrown);
			}
			dirtyRegions.Add(Region(a));
		}

		public static bool IsCover(NodeHandle a) => AllEdges == null ? false : AllEdges.TryGetValue(a, out var flags) && (flags & NodeEdges.IsCover) > 0;
		public static void IsCover(NodeHandle a, bool value) {
			if( value ) {
				AllEdges.AddOrUpdate(a, NodeEdges.IsCover, (key, oldValue) => oldValue | NodeEdges.IsCover);
			} else {
				AllEdges.AddOrUpdate(a, 0, (key, oldValue) => oldValue & ~NodeEdges.IsCover);
			}
			dirtyRegions.Add(Region(a));
		}

		// create a reverse mapping, given an edge bit index, return the NodeHandle delta
		public static readonly long[] edgeOffsets = whichEdgeBit.Keys.OrderBy(k => whichEdgeBit[k]).ToArray();

		// track a 'frontier' of not-yet-explored nodes
		internal static ConcurrentQueue<NodeHandle> Ungrown = new ConcurrentQueue<NodeHandle>();

		public NavMesh() { }

		private static uint started = 0;
		private static uint saved = 0;
		private static uint aborted = 0;
		public static ConcurrentDictionary<NodeHandle, NodeEdges> AllEdges = new ConcurrentDictionary<NodeHandle, NodeEdges>();
		public static bool IsLoaded => AllEdges != null && aborted == 0;
		public static int Count => AllEdges == null ? 0 : AllEdges.Count;
		public static bool ShowEdges { get; internal set; } = false;

		internal static void DebugNode(NodeHandle node, bool grow) {
			var pos = Position(node);
			UI.DrawTextInWorld(pos, $"{node}");
		}
		private static int[] possibleGrowthEdges = new int[] { 0, 1, 2, 3, 4, 6, 10, 12 };
		private static IEnumerable<NodeHandle> PossibleGrowthEdges(NodeHandle node) => possibleGrowthEdges.Select(i => (NodeHandle)((long)node + edgeOffsets[i]));
		public static IEnumerable<NodeHandle> GroundEdges(NodeHandle node) => PossibleGrowthEdges(node).Each(n => StopAtWater(PutOnGround(Position(n), 1f), 0f));

		private NodeHandle prevNode;
		public override void OnTick() {
			if( (!Enabled) || aborted != 0 ) {
				return;
			}
			DrawStatus();
			PlayerNode = GetHandle(PlayerPosition);
			if( PlayerNode != prevNode ) {
				AddEdge(prevNode, PlayerNode);
			}
			prevNode = PlayerNode;
			PlayerRegion = Region(PlayerNode);
			if( !RequestRegion(PlayerRegion).IsDone() ) {
				UI.DrawText("Loading player region...");
				return;
			}
				
			if( started == 0 ) {
				started = GameTime;
				saved = GameTime;
			} else if( GameTime - started < 100 || !CanControlCharacter() ) {
				UI.DrawText(.45f, .65f, $"Cutscene...");
			} else if( AllEdges != null ) {
				if( SaveEnabled && GameTime - saved > 60000 ) {
					saved = GameTime;
					Task.Run(() => { SaveToFile(); });
				}
				// UI.DrawText($"NavMesh: {Ungrown.Count}/{AllEdges.Count}", color: IsGrown(PlayerNode) ? Color.White : Color.Orange);
				int msPerFrame = (int)(1000 / CurrentFPS);
				uint msPerGrow = (uint)Max(15, 35 - msPerFrame);
				DrawEdges(PlayerNode, 6);
				NodeHandle handle = GetHandle(StopAtWater(PutOnGround(AimPosition(), 1f), 0f));
				if( !IsGrown(handle) ) {
					Ungrown.Enqueue(handle);
				}
				if( !IsGrown(PlayerNode) ) {
					Grow(PlayerNode, msPerGrow);
				} else if( Ungrown.TryDequeue(out var first) ) {
					DrawLine(HeadPosition(Self), Position(first), Color.Orange);
					Grow(first, msPerGrow);
				} else {
					Grow(Flood(PlayerNode, 20000, 20, default, Edges)
						.Without(IsGrown)
						.FirstOrDefault(), msPerGrow);
				}
			}
		}

		private void DrawEdges(NodeHandle node, uint depth) {
			if (!ShowEdges) {
				return;
			}
			if( node == NodeHandle.Invalid ) {
				return;
			}

			var queue = new Queue<NodeHandle>();
			var seen = new HashSet<NodeHandle>();
			queue.Enqueue(node);
			while( queue.Count > 0 && seen.Count < 80 ) {
				NodeHandle cur = queue.Dequeue();
				var pos = Position(cur);
				seen.Add(cur);
				foreach( NodeHandle e in Edges(cur) ) {
					if( ! seen.Contains(e) ) {
						var epos = Position(e);
						DrawLine(pos, epos, Color.Yellow);
						if( IsCover(e) ) {
							DrawSphere(epos, .03f, Color.Blue);
						} else if( !IsGrown(e) ) {
							DrawSphere(epos, .03f, Color.Green);
						}
						queue.Enqueue(e);
					}
				}
			}

		}

		private static ConcurrentDictionary<RegionHandle, Future<RegionHandle>> loadedRegions = new ConcurrentDictionary<RegionHandle, Future<RegionHandle>>();
		public static IFuture<RegionHandle> RequestRegion(RegionHandle r) {
			if( r == RegionHandle.Invalid ) {
				return new Immediate<RegionHandle>(r);
			}
			return loadedRegions.GetOrAdd(r, (region) => new Future<RegionHandle>(() => {
				Log($"Reading Region: {region} {loadedRegions.Count}");
				try {
					ReadFromFile($"scripts/Shiv.{region}.mesh");
					return region;
				} catch( FileNotFoundException ) {
					Log($"Empty region: {region}");
					return region;
				}
			}));
		}

		public void DrawStatus() {
			float left = 0f;
			float top = .75f;
			float lineHeight = .019f;
			int line = 0;
			UI.DrawText(left, top + (line++ * lineHeight), $"NavMesh Status:");
			UI.DrawText(left, top + (line++ * lineHeight), $"{Ungrown.Count}/{AllEdges.Count} nodes in {loadedRegions.Count} regions ({dirtyRegions.Count} dirty)");
			if( LastGrown != NodeHandle.Invalid ) {
				UI.DrawText(left, top + (line++ * lineHeight), $"Grew:{Sqrt(DistanceToSelf(LastGrown)):F2} away ({grownPerSecond.Value:F2}/sec)");
				UI.DrawMeter(left + .01f, top + (line++ * lineHeight), .15f, lineHeight, Color.SlateGray, Color.Green, (float)Min(1.0f, Sqrt(DistanceToSelf(LastGrown)) / 100f));
			}
		}

		public override void OnAbort() {
			aborted = GameTime;
			if( AllEdges != null ) {
				AllEdges.Clear();
				AllEdges = null;
			}
		}

		// public static IEnumerable<NodeHandle> PossibleEdges(NodeHandle node) => edgeOffsets.Select(o => (NodeHandle)((long)node + o));
		public static IEnumerable<NodeHandle> PossibleEdges(NodeHandle node) {
			for( int i = 0; i < 30; i++ ) {
				yield return (NodeHandle)((long)node + edgeOffsets[i]);
			}
		}

		public static bool HasEdges(NodeHandle a) => AllEdges != null && AllEdges.TryGetValue(a, out var flags) && flags != 0;
		public static IEnumerable<NodeHandle> Edges(NodeHandle a) {
			if( AllEdges == null ) {
				yield break;
			}

			if( AllEdges.TryGetValue(a, out var flags) ) {
				for( int i = 0; i < edgeOffsets.Length; i++ ) {
					if( (flags & (NodeEdges)(1u << i)) > 0 ) {
						yield return (NodeHandle)((long)a + edgeOffsets[i]);
					}
				}
			}
		}

		public static bool IsPossibleEdge(NodeHandle a, NodeHandle b) => whichEdgeBit.ContainsKey((int)b - (int)a);
		public static bool HasEdge(NodeHandle a, NodeHandle b) {
			long d = (long)b - (long)a;
			if( !whichEdgeBit.ContainsKey(d) ) {
				return false;
			}

			// retrieve all the edges of a and check the bit for b
			return AllEdges.TryGetValue(a, out var edges)
				? (edges & (NodeEdges)(0x1u << whichEdgeBit[d])) > 0
				: false;
		}

		public static void Block(NodeHandle n) {
			Remove(n);
			IsGrown(n, true);
			Text.Add(Position(n), "Blocked", 3000);
		}

		public static bool Remove(NodeHandle a) => AllEdges.TryRemove(a, out var edges);
		public static bool SetEdge(NodeHandle a, NodeHandle b, bool value) {
			long d = (long)b - (long)a;
			if( !whichEdgeBit.ContainsKey(d) ) {
				return false;
			}
			var mask = (NodeEdges)(1u << whichEdgeBit[d]);
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


		public static void GetAllHandlesInBox(ModelBox box, ConcurrentSet<NodeHandle> output) => GetAllHandlesInBox(box.M, box.Back, box.Front, output);
		public static void GetAllHandlesInBox(Matrix4x4 m, Vector3 backLeft, Vector3 frontRight, ConcurrentSet<NodeHandle> output) {
			float minX = Min(backLeft.X, frontRight.X);
			float minY = Min(backLeft.Y, frontRight.Y);
			float minZ = Min(backLeft.Z, frontRight.Z);
			float maxX = Max(backLeft.X, frontRight.X);
			float maxY = Max(backLeft.Y, frontRight.Y);
			float maxZ = Max(backLeft.Z, frontRight.Z);
			for( float x = minX; x <= maxX; x += gridScale ) {
				for( float y = minY; y <= maxY; y += gridScale ) {
					for( float z = minZ; z <= maxZ; z += zScale ) {
						var v = new Vector3(x, y, z);
						var p = Vector3.Transform(v, m);
						output.Add(GetHandle(p));
					}
				}
			}
		}

		public static Color GetColor(NodeHandle a) {
			return (IsCover(a) ? Color.Blue :
				IsGrown(a) ? Color.Gray :
				Color.White);
		}

		public override void OnInit() {
			RequestRegion(Region(Global.Position(Call<PedHandle>(GET_PLAYER_PED, CurrentPlayer))));
			ReadFrontierFile();
			base.OnInit();
		}

		public static bool ReadFrontierFile() {
			if( !LoadEnabled ) {
				return false;
			}
			var filename = "scripts/Shiv.Frontier.mesh";
			try {
				using( BinaryReader r = Codec.Reader(filename) ) {
					int magic = r.ReadInt32();
					if( magic != magicBytes ) {
						Log($"Wrong magic bytes {magic:X}");
						return false;
					}
					// let EndOfStreamException tell us when to stop
					while( true ) { Ungrown.Enqueue((NodeHandle)r.ReadInt64()); }
				}
			} catch( EndOfStreamException ) {
				return true;
			} catch( IOException ) {
				return false;
			} catch( ObjectDisposedException ) {
				return false;
			}
		}

		public static bool ReadFromFile(string filename) {
			if( !LoadEnabled ) {
				return false;
			}

			var s = new Stopwatch();
			s.Start();
			try {
				using( BinaryReader r = Codec.Reader(filename) ) {
					int magic = r.ReadInt32();
					if( magic == 0x000FEED6 ) { // upgrade this older version of mesh file
						int count = r.ReadInt32();
						if( count <= 0 ) {
							Log($"Invalid count: {count}");
							return false;
						}
						byte[] buf;
						ulong[] handles = new ulong[count];
						uint[] edges = new uint[count];
						Log($"Reading {count} (old) nodes...");
						buf = r.ReadBytes(count * sizeof(ulong));
						Buffer.BlockCopy(buf, 0, handles, 0, buf.Length);
						buf = r.ReadBytes(count * sizeof(uint));
						Buffer.BlockCopy(buf, 0, edges, 0, buf.Length);
						Parallel.For(0, count, (i) => AllEdges.TryAdd((NodeHandle)handles[i], (NodeEdges)edges[i]));
						Log($"Finished loading {count} nodes, after {s.Elapsed}");
					} else if ( magic == magicBytes ) {
						int count = r.ReadInt32();
						if( count <= 0 ) {
							Log($"Invalid count: {count}");
							return false;
						}
						byte[] buf;
						ulong[] handles = new ulong[count];
						ulong[] edges = new ulong[count];
						Log($"Reading {count} nodes...");
						buf = r.ReadBytes(count * sizeof(ulong));
						Buffer.BlockCopy(buf, 0, handles, 0, buf.Length);
						buf = r.ReadBytes(count * sizeof(ulong));
						Buffer.BlockCopy(buf, 0, edges, 0, buf.Length);
						Parallel.For(0, count, (i) => AllEdges.TryAdd((NodeHandle)handles[i], (NodeEdges)edges[i]));
						Log($"Finished loading {count} nodes, after {s.Elapsed}");
					} else {
						Log($"Invalid magic bytes: {magic}");
						return false;
					}
				}
			} catch( FileNotFoundException ) {
				Log($"File not found: {filename}");
			}
			s.Stop();
			return true;
		}
		private static ConcurrentSet<RegionHandle> dirtyRegions = new ConcurrentSet<RegionHandle>();
		public static void SaveToFile() {
			if( !SaveEnabled ) {
				return;
			}

			AllEdges.Keys.GroupBy(Region).AsParallel().Each(g => {
				RegionHandle region = g.Key;
				if( dirtyRegions.TryRemove(region) ) {
					var file = $"scripts/Shiv.{region}.mesh";
					using( BinaryWriter w = Codec.Writer(file + ".tmp") ) {
						w.Write(magicBytes);
						ulong[] handles = g.Cast<ulong>().ToArray();
						ulong[] edges = handles.Select(h => (ulong)AllEdges[(NodeHandle)h]).ToArray();
						byte[] buf;
						try {
							Log($"Saving dirty region: {region} {handles.Length} nodes...");
							w.Write(handles.Length);
							buf = new byte[handles.Length * sizeof(NodeHandle)];
							Buffer.BlockCopy(handles, 0, buf, 0, buf.Length);
							w.Write(buf);
						} catch( Exception err ) {
							Log("Failed to write handles to file: " + err.ToString());
							return;
						}
						try {
							buf = new byte[edges.Length * sizeof(ulong)];
							Buffer.BlockCopy(edges, 0, buf, 0, buf.Length);
							w.Write(buf);
						} catch( Exception err ) {
							Log("Failed to write edges to file: " + err.ToString());
							return;
						}
					}
					try { File.Delete(file); } catch( FileNotFoundException ) { }
					try { File.Move(file + ".tmp", file); } catch( Exception e ) {
						Log("File.Move Failed: " + e.ToString());
					}
				}
			});
			var filename = "scripts/Shiv.Frontier.mesh";
			using( BinaryWriter w = Codec.Writer(filename + ".tmp") ) {
				w.Write(magicBytes);
				try {
					Log($"Writing {Ungrown.Count} ungrown nodes to file...");
					foreach( NodeHandle h in Ungrown ) {
						w.Write((ulong)h);
					}
				} catch( Exception err ) {
					Log("Failed: " + err.ToString());
					return;
				}
				w.Close();
				Log("All bytes written.");
			}
			try { File.Delete(filename); } catch( FileNotFoundException ) { }
			try { File.Move(filename + ".tmp", filename); } catch( Exception e ) {
				Log("File.Move Failed: " + e.ToString());
			}
		}

		public static IEnumerable<NodeHandle> Flood(NodeHandle cur, int maxNodes, int maxDepth, CancellationToken cancel, Func<NodeHandle, IEnumerable<NodeHandle>> edges) {
			var queue = new Queue<NodeHandle>();
			var seen = new HashSet<NodeHandle>();
			queue.Enqueue(cur);
			while( true ) {
				if( cancel != default && cancel.IsCancellationRequested ) {
					Log($"Flood cancelled.");
					yield break;
				}
				if( queue.Count == 0 ) {
					// Log($"Flood exhausted search area");
					yield break;
				}
				if( seen.Count >= maxNodes ) {
					// Log($"Flood stopped at limit {maxNodes}");
					yield break;
				}
				cur = queue.Dequeue();
				if( !seen.Contains(cur) ) {
					seen.Add(cur);
					yield return cur;
					edges(cur).Each(queue.Enqueue);
				}
			}
		}

		public static IEnumerable<NodeHandle> GrowOne(NodeHandle node, HashSet<EntHandle> doors, bool debug=false) {
			if( IsGrown(node) ) {
				yield break;
			}
			var nodePos = Position(node);
			if( DistanceToSelf(nodePos) > 100f * 100f ) {
				yield break;
			}
			IsGrown(node, true);
			foreach( var i in possibleGrowthEdges ) {
				var e = (NodeHandle)((long)node + edgeOffsets[i]);
				NodeHandle g = GetHandle(StopAtWater(PutOnGround(Position(e), 1f), 0f));
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
			var doors = new HashSet<EntHandle>(NearbyObjects.Where(e => Pathfinder.checkDoorModels.Contains(GetModel(e))));
			while( sw.ElapsedMilliseconds < maxMs ) {
				NodeHandle next;
				if( queue.Count > 0 ) {
					next = queue.Dequeue();
				} else if( ! Ungrown.TryDequeue(out next) ) {
					break;
				}
				RequestRegion(Region(next)).Wait(20);
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

		public static Producer<NodeHandle> FloodThread(NodeHandle n, int maxNodes, int maxDepth, Func<NodeHandle, IEnumerable<NodeHandle>> edges) {
			var producer = new DistinctProducer<NodeHandle>() { Limit = (ulong)maxNodes };
			ThreadPool.QueueUserWorkItem((object state) => {
				var queue = new Queue<NodeHandle>();
				queue.Enqueue(n);
				while( true ) {
					if( queue.Count == 0 ) {
						Log("FloodThread: Exhausted search area.");
						break;
					}
					if( producer.IsCancellationRequested ) {
						Log($"FloodThread: Cancelled after {producer.Count}");
						break;
					}
					NodeHandle cur = queue.Dequeue();
					if( producer.Produce(cur) && queue.Count < maxDepth * 8 ) {
						foreach( var e in edges(cur) ) {
							queue.Enqueue(e);
						}
					}
				}
				Log("FloodThread: Closing");
				producer.Close();
			});
			return producer;
		}

		private static Random random = new Random();
		public static void DebugVehicle() {
			if( NearbyVehicles.Length == 0 ) {
				return;
			}

			VehicleHandle v = NearbyVehicles[0];
			Matrix4x4 m = Matrix(v);
			VehicleHash model = GetModel(v);
			GetModelDimensions(model, out Vector3 backLeft, out Vector3 frontRight);
			var sw = new Stopwatch();
			sw.Start();
			var blocked = new ConcurrentSet<NodeHandle>();
			GetAllHandlesInBox(m, backLeft, frontRight, blocked);
			UI.DrawText($"GetAllHandlesInBox: {blocked.Count} in {sw.ElapsedTicks} ticks");

			foreach( Vector3 n in blocked.Select(Position) ) {
				if( random.NextDouble() < .2 ) {
					DrawSphere(n, .05f, Color.Red);
				}
			}

		}


		private static readonly Vector3[] vehicleCoverOffset = new Vector3[6] {
			VehicleOffsets.FrontLeftWheel,
			VehicleOffsets.BackLeftWheel,
			VehicleOffsets.FrontGrill,
			VehicleOffsets.BackRightWheel,
			VehicleOffsets.FrontRightWheel,
			VehicleOffsets.BackBumper,
		};
		public static IEnumerable<NodeHandle> FindCoverBehindVehicle(Vector3 danger, int maxScan = 10) {
			foreach( VehicleHandle v in NearbyVehicles.Take(maxScan) ) {
				var count = GetSeatMap(v).Values.Where(IsValid).Count();
				if( count > 0 ) {
					UI.DrawTextInWorld(Global.Position(v), $"Seat Map Occupied: {count}");
					continue;
				}
				Matrix4x4 m = Matrix(v);
				Vector3 pos = Global.Position(m);
				Vector3 delta = danger - pos;
				float heading = AbsHeading(Heading(m) - Rad2Deg(Atan2(delta.Y, delta.X)));
				int slot = (int)(6 * (heading / 360));
				Vector3 loc = GetVehicleOffset(v, vehicleCoverOffset[slot], m);
				yield return GetHandle(loc);
			}
		}

	}
}
