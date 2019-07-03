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
	public enum NavRegion : uint { Invalid = 0 };

	public static partial class Global {

		public static NodeHandle PlayerNode;
		public static NavRegion PlayerRegion;

		public static float DistanceToSelf(NodeHandle n) => DistanceToSelf(NavMesh.Position(n));

	}

	public partial class NavMesh : Script {

		public static bool Enabled = true;
		public static bool SaveEnabled = true;

		private const int magicBytes = 0x000FEED6; // if any constants/structure below should change, increment the magic bytes to invalidate the old data
		private const ulong mapRadius = 8192; // in the world, the map goes from -8192 to 8192
		private const float gridScale = .5f; // how big are the X,Y steps of the mesh
		private const float zScale = .25f; // how big are the Z steps of the mesh
		private const float zDepth = 1000f; // how deep underwater can the mesh go
		// zDepth can be at most mapRadius*zScale, because zScale changes vertical node density
		// which makes mapShift too small, which corrupts NodeHandles

		public static NodeHandle LastGrown { get; private set; } = NodeHandle.Invalid;
		public static long LastGrownMS = 0;

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
			return (NodeHandle)(
				(x << (2 * mapShift)) |
				(y << (1 * mapShift)) |
				(z << (0 * mapShift))
			);
		}

		private const float regionScale = 128f;
		private const int regionShift = 7; // we use 3*7=21 bits of a NavRegion:uint
		
		public static NavRegion Region(Vector3 v) {
			if( v == Vector3.Zero ) {
				return NavRegion.Invalid;
			}
			// v.X starts [-8192..8192] becomes [0..128]
			uint x = (uint)(Round((v.X + mapRadius) / regionScale));
			uint y = (uint)(Round((v.Y + mapRadius) / regionScale));
			uint z = (uint)(Round((v.Z + zDepth) / regionScale));
			return (NavRegion)(
				(x << (2 * regionShift)) |
				(y << (1 * regionShift)) |
				(z << (0 * regionShift))
			);
		}
		private static readonly uint mapShiftMask = (1u << mapShift) - 1;
		public static NavRegion Region(NodeHandle a) {
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
			return (NavRegion)((x << (2 * regionShift))
				| (y << (1 * regionShift))
				| z);
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
				(gridScale * ((h >> (2 * mapShift)) & mask)) - mapRadius,
				(gridScale * ((h >> (1 * mapShift)) & mask)) - mapRadius,
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
		[Flags] public enum NodeEdges : uint { Empty = 0 }
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

			// ----
			// if I had a few spare bits, I could use them to do Clearance
			// a buffer around sides of the mesh that would replace IsCover
			// each new nodes gets either Clearance = 0 (IsCover)
			// or Clearance = 1 + Edges().Select(Clearance).Min()
			// if we had 3 bits to spare (assuming we cut out straight up and down)
			// we could have Clearance = 0, 1, 2, 3
			// any node with 3 has about 1.5m of space around it
			// paths should prefer to follow higher
			// if we changed NodeEdges to be ulong instead of uint
			// a 25% increase in save/load times, and stored file size
			// but we would have lots of bits (dont have to sacrifice up and down)
			// and could store Clearance values large enough to use with vehicles

		};


		private const int grownBit = 30;
		private const int coverBit = 31;

		// create a reverse mapping, given an edge bit index, return the NodeHandle delta
		public static readonly long[] edgeOffsets = whichEdgeBit.Keys.OrderBy(k => whichEdgeBit[k]).ToArray();

		// track a 'frontier' of not-yet-explored nodes
		internal static HashSet<NodeHandle> Ungrown = new HashSet<NodeHandle>();

		public NavMesh() { }

		private static uint started = 0;
		private static uint saved = 0;
		private static uint aborted = 0;
		public static ConcurrentDictionary<NodeHandle, uint> AllEdges = new ConcurrentDictionary<NodeHandle, uint>();
		public static bool IsLoaded => AllEdges != null && aborted == 0;
		public static int Count => AllEdges == null ? 0 : AllEdges.Count;
		public static bool ShowEdges { get; internal set; } = false;

		internal static void DebugNode(NodeHandle node, bool grow) {
			var pos = Position(node);
			UI.DrawTextInWorld(pos, $"{node}");
			/*
			if( grow ) {
				var sw = new Stopwatch();
				sw.Start();
				GrowResult result = Grow(node, 10, sw, new HashSet<NodeHandle>(), debug: true);
				UI.DrawTextInWorldWithOffset(pos, 0f, -.02f, $"GrowResult:{result}");
			}
			int i = 0;
			var sw = new Stopwatch();
			sw.Start();
			foreach(NodeHandle e in Flood(node, 2, default, PossibleEdges)) {
				var epos = Position(e);
				// UI.DrawTextInWorldWithOffset(epos, 0f, 0f, $"{i}");
				i++;
			}
			UI.DrawTextInWorldWithOffset(pos, .01f, -.02f, $"Flood:{i} in {sw.ElapsedMilliseconds}ms");
			*/
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
				uint msPerGrow = (uint)Max(5, 35 - msPerFrame);
				DrawEdges(PlayerNode, 6);
				if( !IsGrown(PlayerNode) ) {
					Grow(PlayerNode, msPerGrow, debug:false);
				} else {
					if( Ungrown.Count > 0 ) {
						NodeHandle first = NodeHandle.Invalid;
						if( Ungrown.Count > 1200 ) {
							lock( Ungrown ) {
								Ungrown = Ungrown.OrderBy(DistanceToSelf).Take(Ungrown.Count / 2).ToHashSet();
							}
						}
						lock( Ungrown ) {
							first = Ungrown.Min(DistanceToSelf);
						}
						if( first != NodeHandle.Invalid ) {
							DrawLine(HeadPosition(Self), Position(first), Color.Orange);
							if( RequestRegion(Region(first)).IsDone() ) {
								lock( Ungrown ) {
									Ungrown.Remove(first);
								}
								Grow(first, msPerGrow);
							}
						}
					} else {
						Vector3 pos = StopAtWater(PutOnGround(AimPosition(), 1f), 0f);
						NodeHandle handle = GetHandle(pos);
						pos = Position(handle);
						if( !IsGrown(handle) ) {
							if( RequestRegion(Region(handle)).IsDone() ) {
								Sphere.Add(pos, .1f, Color.Red, 3000);
								Grow(handle, msPerGrow);
							}
						}
					}
					// if( Ungrown.Count > 0 ) { foreach(var n in Ungrown ) { DrawSphere(Position(n), .1f, Color.Orange); } }
				}
			}
		}

		private void DrawEdges(NodeHandle node, uint depth) {
			if (!ShowEdges) {
				return;
			}

			UI.DrawText($"PlayerNode: {PlayerNode} {Position(PlayerNode)}");
			DrawLine(HeadPosition(Self), Position(node), Color.Pink);
			DrawEdges(node, Position(node), depth, new HashSet<NodeHandle>(), new HashSet<NodeHandle>(), 0);
		}
		private void DrawEdges(NodeHandle node, Vector3 nodePos, uint depth, HashSet<NodeHandle> stack, HashSet<NodeHandle> seen, int count) {
			if( ShowEdges == false
				|| depth <= 0
				|| stack.Contains(node)
				) {
				return;
			}

			if( !seen.Contains(node) ) {
				seen.Add(node); // prevent double visit
				stack.Add(node); // prevent cycles
				if( IsCover(node) ) {
					DrawSphere(nodePos, .05f, Color.Blue);
				}
				// UI.DrawTextInWorld(nodePos, $"{node}");
				// UI.DrawTextInWorldWithOffset(nodePos, 0f, -.005f * count, $"{count}");
				foreach( NodeHandle e in Edges(node) ) {
					Vector3 ePos = Position(e);
					count += 1;
					DrawLine(nodePos, ePos, Color.Yellow);
					DrawEdges(e, ePos, depth - 1, stack, seen, count);
				}
				stack.Remove(node);
			}
		}

		private static ConcurrentDictionary<NavRegion, Future<NavRegion>> loadedRegions = new ConcurrentDictionary<NavRegion, Future<NavRegion>>();
		public static IFuture<NavRegion> RequestRegion(NavRegion r) {
			if( r == NavRegion.Invalid ) {
				return new Immediate<NavRegion>(r);
			}
			return loadedRegions.GetOrAdd(r, (region) => new Future<NavRegion>(() => {
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
				UI.DrawText(left, top + (line++ * lineHeight), $"Grew:{Sqrt(DistanceToSelf(LastGrown)):F2} away for {LastGrownMS}ms");
				UI.DrawMeter(left + .01f, top + (line++ * lineHeight), .15f, lineHeight, Color.SlateGray, Color.Green, (float)Sqrt(DistanceToSelf(LastGrown)) / 70f);
			}
		}

		internal static void Grow(NodeHandle node, uint ms, bool debug=false) {
			if( !IsLoaded ) {
				return;
			}

			var sw = new Stopwatch();
			sw.Start();
			Grow(node, ms, sw, new HashSet<NodeHandle>(), debug);
			while( node != NodeHandle.Invalid && sw.ElapsedMilliseconds < ms && Ungrown.Count > 0 ) {
				node = Ungrown.Min(DistanceToSelf);
				Grow(node, ms, sw, new HashSet<NodeHandle>(), debug);
			}
			LastGrownMS = sw.ElapsedMilliseconds;
		}
		public static bool ShowGrowth = false;
		internal enum GrowResult {
			InvalidNode,
			NodeOnStack,
			IsGrown,
			TooFar,
			Timeout,
			NotLoaded,
			IsGrowing
		}
		internal static GrowResult Grow(NodeHandle node, uint ms, Stopwatch sw, HashSet<NodeHandle> stack, bool debug = false) {
			if( !IsLoaded ) {
				return GrowResult.NotLoaded;
			}
			if( node == NodeHandle.Invalid ) {
				return GrowResult.InvalidNode;
			}
			Vector3 nodePos = Position(node);
			if( stack.Contains(node) ) {
				if( debug ) {
					UI.DrawTextInWorld(nodePos, "OnStack");
				}
				return GrowResult.NodeOnStack;
			}
			if( IsGrown(node) ) {
				return GrowResult.IsGrown;
			}
			if( !RequestRegion(Region(node)).IsDone() ) {
				lock( Ungrown ) { Ungrown.Add(node); }
				return GrowResult.NotLoaded;
			}
			if( sw.ElapsedMilliseconds > ms ) {
				lock( Ungrown ) { Ungrown.Add(node); }
				return GrowResult.Timeout;
			}
			if( DistanceToSelf(nodePos) > maxGrowRange ) {
				lock( Ungrown ) { Ungrown.Add(node); }
				return GrowResult.TooFar;
			}
			stack.Add(node);
			LastGrown = node;
			IsGrown(node, true);

			try {
				// Push growth out on certain edges by following their edgeOffsets
				possibleGrowthEdges.Select(i => { 
				// Items(0,1,2,3,4,6,10,12).Select(i => {
					var e = (NodeHandle)((long)node + edgeOffsets[i]);
					Vector3 ePos = Position(e);
					Vector3 gPos = StopAtWater(PutOnGround(ePos, 1f), 0f);
					NodeHandle g = GetHandle(gPos);
					gPos = Position(g);
					if( IsPossibleEdge(node, g) && !HasEdge(node, g) ) {
						Vector3 delta = gPos - nodePos;
						var len = delta.Length();
						Vector3 end = nodePos + (Vector3.Normalize(delta) * (len - capsuleSize / 2));
						RaycastResult result = Raycast(nodePos, end, capsuleSize, growRayOpts, Self);
						if( result.DidHit ) {
							Materials m = result.Material;
							if( m != Materials.metal_railing
								&& m != Materials.metal_garage_door
								&& m != Materials.bushes
								&& Abs(Vector3.Dot(result.SurfaceNormal, Up)) < .01f ) {
								IsCover(node, true);

								if( ShowGrowth ) {
									Sphere.Add(nodePos, .11f, Color.Blue, 1000);
								}

								return NodeHandle.Invalid;
							}
						} else {
							AddEdge(node, g);
							AddEdge(g, node);
							return g;
						}
					}
					return NodeHandle.Invalid;
				}).Each(e => {
					if( e != NodeHandle.Invalid ) {
						Grow(e, ms, sw, stack, debug: debug);
					}
				});
				return GrowResult.IsGrowing;
			} finally {
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

		// public static IEnumerable<NodeHandle> PossibleEdges(NodeHandle node) => edgeOffsets.Select(o => (NodeHandle)((long)node + o));
		public static IEnumerable<NodeHandle> PossibleEdges(NodeHandle node) {
			for( int i = 0; i < 30; i++ ) {
				yield return (NodeHandle)((long)node + edgeOffsets[i]);
			}
		}

		public static bool HasEdges(NodeHandle a) => AllEdges != null && AllEdges.TryGetValue(a, out uint flags) && flags != 0;
		public static IEnumerable<NodeHandle> Edges(NodeHandle a) {
			if( AllEdges == null ) {
				yield break;
			}

			if( AllEdges.TryGetValue(a, out uint flags) ) {
				for( int i = 0; i < edgeOffsets.Length; i++ ) {
					if( (flags & (1u << i)) > 0 ) {
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
			return AllEdges.TryGetValue(a, out uint edges)
				? (edges & (0x1u << whichEdgeBit[d])) > 0
				: false;
		}

		public static void Block(NodeHandle n) {
			Remove(n);
			IsGrown(n, true);
			Text.Add(Position(n), "Blocked", 3000);
		}

		public static bool Remove(NodeHandle a) => AllEdges == null ? false : AllEdges.TryRemove(a, out uint edges);
		private static uint GetRawEdges(NodeHandle a) {
			if( AllEdges == null ) {
				return 0;
			}

			AllEdges.TryGetValue(a, out uint edges);
			uint mask = unchecked((0x1u << grownBit) | (0x1u << coverBit));
			return edges & ~mask;
		}
		public static bool SetEdge(NodeHandle a, NodeHandle b, bool value) {
			if( AllEdges == null ) {
				return false;
			}

			long d = (long)b - (long)a;
			if( !whichEdgeBit.ContainsKey(d) ) {
				return false;
			}
			uint mask = (1u << whichEdgeBit[d]);
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
		public static bool IsGrown(NodeHandle a) => AllEdges == null ? false : AllEdges.TryGetValue(a, out uint flags) && (flags & (1u << grownBit)) > 0;

		private const uint grownBitMask = (1u << grownBit);
		public static void IsGrown(NodeHandle a, bool value) {
			if( AllEdges == null ) {
				return;
			}

			if( value ) {
				AllEdges.AddOrUpdate(a, grownBitMask, (key, oldValue) => oldValue | grownBitMask);
			} else {
				AllEdges.AddOrUpdate(a, 0, (key, oldValue) => oldValue & ~grownBitMask);
			}
			dirtyRegions.Add(Region(a));
		}
		public static bool IsCover(NodeHandle a) => AllEdges == null ? false : AllEdges.TryGetValue(a, out uint flags) && (flags & (1u << coverBit)) > 0;
		public static void IsCover(NodeHandle a, bool value) {
			if( AllEdges == null ) {
				return;
			}

			uint mask = (1u << coverBit);
			if( value ) {
				AllEdges.AddOrUpdate(a, mask, (key, oldValue) => oldValue | mask);
			} else {
				AllEdges.AddOrUpdate(a, 0, (key, oldValue) => oldValue & ~mask);
			}
			dirtyRegions.Add(Region(a));
		}

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
			var filename = "scripts/Shiv.Frontier.mesh";
			try {
				using( BinaryReader r = Codec.Reader(filename) ) {
					int magic = r.ReadInt32();
					if( magic != magicBytes ) {
						Log($"Wrong magic bytes {magic:X}");
						return false;
					}
					int count = r.ReadInt32();
					if( count <= 0 ) {
						Log($"Invalid count: {count}");
						return false;
					}
					ulong[] handles = new ulong[count];
					byte[] buf = r.ReadBytes(count * sizeof(ulong));
					Buffer.BlockCopy(buf, 0, handles, 0, buf.Length);
					Ungrown = new HashSet<NodeHandle>(handles.Cast<NodeHandle>());
				}
			} catch( FileNotFoundException ) {
				return false;
			}
			return false;
		}

		public static bool ReadFromFile(string filename) {
			var s = new Stopwatch();
			s.Start();
			try {
				using( BinaryReader r = Codec.Reader(filename) ) {
					int magic = r.ReadInt32();
					if( magic != magicBytes ) {
						Log($"Wrong magic bytes {magic:X}");
						return false;
					}
					int count = r.ReadInt32();
					if( count <= 0 ) {
						Log($"Invalid count: {count}");
						return false;
					}
					byte[] buf;
					ulong[] handles = new ulong[count];
					uint[] edges = new uint[count];
					Log($"Reading {count} nodes...");
					buf = r.ReadBytes(count * sizeof(ulong));
					Buffer.BlockCopy(buf, 0, handles, 0, buf.Length);
					buf = r.ReadBytes(count * sizeof(uint));
					Buffer.BlockCopy(buf, 0, edges, 0, buf.Length);
					Parallel.For(0, count, (i) => AllEdges.TryAdd((NodeHandle)handles[i], edges[i]) );
					Log($"Finished loading {count} nodes, after {s.Elapsed}");
				}
			} catch( FileNotFoundException ) {
				Log($"File not found: {filename}");
			}
			s.Stop();
			return true;
		}
		private static ConcurrentSet<NavRegion> dirtyRegions = new ConcurrentSet<NavRegion>();
		public static void SaveToFile() {
			AllEdges.Keys.GroupBy(Region).Each(g => {
				NavRegion region = g.Key;
				if( dirtyRegions.TryRemove(region) ) {
					Log($"Saving dirty region: {region}");
					var file = $"scripts/Shiv.{region}.mesh";
					using( BinaryWriter w = Codec.Writer(file + ".tmp") ) {
						w.Write(magicBytes);
						ulong[] handles = g.Cast<ulong>().ToArray();
						uint[] edges = handles.Select(h => AllEdges[(NodeHandle)h]).ToArray();
						byte[] buf;
						try {
							Log($"Writing {handles.Length} handles to file...");
							w.Write(handles.Length);
							buf = new byte[handles.Length * sizeof(NodeHandle)];
							Buffer.BlockCopy(handles, 0, buf, 0, buf.Length);
							w.Write(buf);
						} catch( Exception err ) {
							Log("Failed to write handles to file: " + err.ToString());
							return;
						}
						try {
							Log($"Writing {edges.Length} edges to file...");
							buf = new byte[edges.Length * sizeof(uint)];
							Buffer.BlockCopy(edges, 0, buf, 0, buf.Length);
							w.Write(buf);
						} catch( Exception err ) {
							Log("Failed: " + err.ToString());
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
					ulong[] ungrown;
					lock( Ungrown ) {
						ungrown = Ungrown.Cast<ulong>().ToArray();
					}
					w.Write(ungrown.Length);
					byte[] buf = new byte[ungrown.Length * sizeof(NodeHandle)];
					Buffer.BlockCopy(ungrown, 0, buf, 0, buf.Length);
					w.Write(buf);
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
					Log($"Flood exhausted search area");
					yield break;
				}
				if( seen.Count >= maxNodes ) {
					Log($"Flood stopped at limit {maxNodes}");
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
			Stopwatch sw = new Stopwatch();
			sw.Start();
			ConcurrentSet<NodeHandle> blocked = new ConcurrentSet<NodeHandle>();
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
