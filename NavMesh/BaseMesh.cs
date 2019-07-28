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

	public enum NodeHandle : ulong {
		Invalid = 0,
		DesertAirfield = 11147038923340768197,
		GameStart = 15145120214786049134,
		TrevorTrailerParking = 11437802712536523682,
		SafehouseDriveway = 9180935273294775195,
		SafehouseGarage = 9180935272221328283
	};
	public enum RegionHandle : uint { Invalid = 0 };

	public static partial class Global {

		public static NodeHandle PlayerNode;
		public static RegionHandle PlayerRegion;

		public static float DistanceToSelf(NodeHandle n) => DistanceToSelf(NavMesh.Position(n));

		public static NodeHandle AimNode() => NavMesh.Handle(PutOnGround(AimPosition() + (Up * .5f), 1f));

	}

	public partial class NavMesh : Script {

		public static bool Enabled = true;
		public static bool SaveEnabled = true;
		public static bool LoadEnabled = true;

		private const int versionBytes = 0x000FEED9; // if any constants/structure below should change, increment the magic bytes and update Save/Read methods

		private const ulong mapRadius = 8192; // in the world, the map goes from -8192 to 8192
		private const float gridScale = .5f; // how big are the X,Y steps of the mesh
		private const float zScale = .25f; // how big are the Z steps of the mesh
		private const float zDepth = 200f; // how deep underwater can the mesh go
																				// zDepth can be at most mapRadius*zScale (==1024), because zScale changes vertical node density
		internal const ulong handleMask = (1ul << worldBits) - 1;


		// how to pack Vector3 into a long:
		private const int regionShift = 7;
		private const int regionXBits = 8;
		private const int regionYBits = 8;
		private const int regionZBits = 5;
		private const int regionBits = regionXBits + regionYBits + regionZBits;
		private const int worldXBits = 15;
		private const int worldYBits = 15;
		private const int worldZBits = 13;
		private const int worldBits = worldXBits + worldYBits + worldZBits;
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static NodeHandle Handle(Vector3 v) {
			if( v == Vector3.Zero ) {
				return NodeHandle.Invalid;
			}
			GetHandleTimer.Start();
			GetHandleCount += 1;

			// shift to absolute positions (move 0,0 to southwest corner of the world)
			float x1 = (float)(v.X + mapRadius); // from [-8192..8192] to [0..16k]
			float y1 = (float)(v.Y + mapRadius); // from [-8192..8192] to [0..16k]
			float z1 = (float)(Clamp(v.Z,-zDepth,1024-zDepth) + zDepth); // from [-200..800] to [0..1k]

			ulong nx = (ulong)Round(x1 / gridScale); // from [0..16k] to [0..32k] = 15 bits per X coord
			ulong ny = (ulong)Round(y1 / gridScale); // from [0..16k] to [0..32k] = 15 bits per Y coord
			ulong nz = (ulong)Round(z1 / zScale); // from [0..1k] to [0..4k] = 13 bits per Z coord

			uint rx = (uint)(nx >> regionShift); // from [0..32k] to [0..256] = 8 bits per region x
			uint ry = (uint)(ny >> regionShift); // from [0..32k] to [0..256] = 8 bits per region y
			uint rz = (uint)(nz >> regionShift); // from [0..4k] to [0..32] = 5 bits per region z

			// total bits: 15 + 15 + 13 + 8 + 8 + 5 = 64

			ulong r = (rx << (regionYBits + regionZBits)) | (ry << regionZBits) | rz; // 21 bits of region coords
			ulong n = (nx << (worldYBits + worldZBits)) | (ny << worldZBits) | nz; // 43 bits of world coords
			NodeHandle node = (NodeHandle)( (r << worldBits) | n );
			// final output handle:
			// 00000000 00000000 00000000 00000000 00000000 00000000 00000000 00000000
			// |<-rx->| |<-ry->| | rz||<-    nx     ->||<-    ny     ->||<-   nz   ->|

			GetHandleTimer.Stop();
			return node;
		}
		public static Stopwatch GetHandleTimer = new Stopwatch();
		public static ulong GetHandleCount = 0;

		// how to unpack a (rounded) Vector3 from inside a ulong
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static Vector3 Position(NodeHandle handle) {
			if( handle == NodeHandle.Invalid ) {
				return Vector3.Zero;
			}
			ulong h = (ulong)handle;
			ulong nz = h & ((1 << 13) - 1); // extract bottom 13 bits for nz
			h >>= 13;
			ulong ny = h & ((1 << 15) - 1); // next 15 bits are ny
			h >>= 15;
			ulong nx = h & ((1 << 15) - 1); // next 15 bits are nx
			float x = (nx * gridScale) - mapRadius;
			float y = (ny * gridScale) - mapRadius;
			float z = (nz * zScale) - zDepth;
			return new Vector3(x, y, z);
				// (gridScale * ((h >> (mapShift << 1)) & mask)) - mapRadius,
				// (gridScale * ((h >> mapShift) & mask)) - mapRadius,
				// (zScale * (h & mask)) - zDepth
			// );
		}

		public NavMesh() { }

		private static uint started = 0;
		private static uint saved = 0;
		private static uint aborted = 0;
		public override void OnInit() {
			ReadFrontierFile();
			base.OnInit();
		}


		public override void OnAbort() {
			aborted = GameTime;
			AllNodes.Clear();
		}


		public class NodeSet {

			internal ConcurrentDictionary<RegionHandle, ConcurrentDictionary<NodeHandle, NodeEdges>> Regions = new ConcurrentDictionary<RegionHandle, ConcurrentDictionary<NodeHandle, NodeEdges>>();

			public int Count() => Regions.Values.Sum(d => d.Count);

			internal Heap<RegionHandle> recentRegions = new Heap<RegionHandle>(1024);
			internal ConcurrentSet<RegionHandle> dirtyRegions = new ConcurrentSet<RegionHandle>();
			private ConcurrentDictionary<NodeHandle, NodeEdges> regionFactory(RegionHandle region) {
				var ret = new ConcurrentDictionary<NodeHandle, NodeEdges>();
				ReadFromFile(region, ret);
				recentRegions.AddOrUpdate(region, TotalTime.ElapsedMilliseconds);
				return ret;
			}
			public NodeEdges Get(NodeHandle n) {
				RegionHandle r = Region(n);
				if( Regions.GetOrAdd(r, regionFactory).TryGetValue(n, out var edges) ) {
					recentRegions.AddOrUpdate(r, TotalTime.ElapsedMilliseconds);
					return edges;
				}
				return NodeEdges.Empty;
			}
			public void Set(NodeHandle n, NodeEdges e) {
				RegionHandle r = Region(n);
				Regions
					.GetOrAdd(r, regionFactory)
					.AddOrUpdate(n, e, (k, o) => e);
				recentRegions.AddOrUpdate(r, TotalTime.ElapsedMilliseconds);
				SetDirty(r);
			}
			public void SetDirty(NodeHandle n) => dirtyRegions.Add(Region(n));
			public void SetDirty(RegionHandle r) => dirtyRegions.Add(r);
			public NodeEdges AddOrUpdate(NodeHandle n, NodeEdges newValue, Func<NodeHandle, NodeEdges, NodeEdges> valueFactory) {
				RegionHandle r = Region(n);
				recentRegions.AddOrUpdate(r, TotalTime.ElapsedMilliseconds);
				SetDirty(r);
				return Regions.GetOrAdd(r, regionFactory).AddOrUpdate(n, newValue, valueFactory);
			}

			internal void PageOut(int itemLimit, long timeLimit) {
				if( Regions.Count > itemLimit && recentRegions.PeekScore() < timeLimit ) {
					while( Regions.Count > itemLimit && recentRegions.PeekScore() < timeLimit && recentRegions.TryPop(out var r) ) {
						if( dirtyRegions.Contains(r) ) {
							dirtyRegions.Remove(r);
							SaveToFile(r);
						}
						Regions.TryRemove(r, out var gone);
						// Log($"Paged out region {r}, {gone.Count} nodes.");
					}
				}
			}
			internal void Clear() {
				Regions.Clear();
				dirtyRegions.Clear();
				recentRegions.Clear();
			}
		}
		public static NodeSet AllNodes = new NodeSet();
		// public static ConcurrentDictionary<NodeHandle, NodeEdges> AllEdges = new ConcurrentDictionary<NodeHandle, NodeEdges>();
		public static bool IsLoaded => aborted == 0;
		public static bool ShowEdges { get; internal set; } = false;

		private NodeHandle prevNode;
		public override void OnTick() {
			if( (!Enabled) || aborted != 0 ) {
				return;
			}
			PlayerNode = Call<bool>(IS_PED_IN_FLYING_VEHICLE, Self) || Call<bool>(IS_PED_SWIMMING, Self)
				? Handle(PlayerPosition)
				: Handle(PutOnGround(PlayerPosition, 1f));
			if( PlayerNode != prevNode ) {
				AddEdge(prevNode, PlayerNode);
			}
			prevNode = PlayerNode;
			PlayerRegion = Region(PlayerNode);
			DrawStatus();
				
			if( started == 0 ) {
				started = GameTime;
				saved = GameTime;
			} else if( GameTime - started < 100 || !CanControlCharacter() ) {
				UI.DrawText(.45f, .65f, $"Cutscene...");
			} else {
				if( SaveEnabled && GameTime - saved > 33000 ) {
					saved = GameTime;
					Task.Run(SaveToFile);
				}
				// UI.DrawText($"NavMesh: {Ungrown.Count}/{AllEdges.Count}", color: IsGrown(PlayerNode) ? Color.White : Color.Orange);
				int msPerFrame = (int)(1000 / CurrentFPS);
				uint msPerGrow = (uint)Max(15, 35 - msPerFrame);
				NodeHandle handle = AimNode();
				if( !IsGrown(handle) ) {
					Ungrown.Enqueue(handle);
				}
				DrawEdges(handle, 100);
				if( !IsGrown(PlayerNode) ) {
					Grow(PlayerNode, msPerGrow);
				} else if( Ungrown.TryDequeue(out var first) ) {
					// DrawLine(HeadPosition(Self), Position(first), Color.Orange);
					Grow(first, msPerGrow);
				} else {
					/*
					Grow(Flood(PlayerNode, 10000, 20, default, Edges)
						.Without(IsGrown)
						.FirstOrDefault(), msPerGrow);
						*/
				}
			}
			AllNodes.PageOut(100, Max(0, TotalTime.ElapsedMilliseconds - 60000));
		}
		private static readonly Color[] clearanceColors = new Color[] {
			Color.FromArgb(255, 255, 255, 255), // 0, unknown clearance
			Color.FromArgb(255, 255, 0, 0), // 1, no clearance
			Color.FromArgb(255, 245, 45, 0),
			Color.FromArgb(255, 235, 60, 0),
			Color.FromArgb(255, 225, 35, 0),
			Color.FromArgb(255, 215, 90, 0),
			Color.FromArgb(255, 205, 105, 0),
			Color.FromArgb(255, 195, 120, 0),
			Color.FromArgb(255, 185, 135, 0),
			Color.FromArgb(255, 175, 150, 0),
			Color.FromArgb(255, 165, 165, 0),
			Color.FromArgb(255, 155, 180, 0),
			Color.FromArgb(255, 145, 195, 0),
			Color.FromArgb(255, 135, 210, 0),
			Color.FromArgb(255, 125, 225, 0),
			Color.FromArgb(255, 115, 240, 0), // 15, maximum clearance
		};

		private void DrawEdges(NodeHandle node, uint maxNodes) {
			if (!ShowEdges) {
				return;
			}
			if( node == NodeHandle.Invalid ) {
				return;
			}
			DrawLine(HeadPosition(Self), Position(node), Color.LightBlue);

			var queue = new Queue<NodeHandle>();
			var seen = new HashSet<NodeHandle>();
			queue.Enqueue(node);
			seen.Add(node);
			while( queue.Count > 0 && seen.Count < maxNodes ) {
				NodeHandle cur = queue.Dequeue();
				var pos = Position(cur);
				var c = Clearance(cur);
				UI.DrawTextInWorldWithOffset(pos, 0f, -.01f * c, $"{c}");
				foreach( NodeHandle e in Edges(cur) ) {
					var epos = Position(e);
					DrawLine(pos, epos, clearanceColors[c]);
					if( IsCover(e) ) {
						DrawSphere(epos, .03f, Color.Blue);
					} else if( !IsGrown(e) ) {
						DrawSphere(epos, .03f, Color.Green);
					}
					if( ! seen.Contains(e) ) {
						queue.Enqueue(e);
						seen.Add(e);
					}
				}
			}
		}

		public void DrawStatus() {
			float lineHeight = .02f;
			float left = 0f;
			float top = .73f;
			int line = 0;
			UI.DrawText(left, top + (line++ * lineHeight), $"NavMesh: {PlayerNode} {Region(PlayerNode)} {Round(Position(PlayerNode),2)}");
			UI.DrawText(left, top + (line++ * lineHeight), $"Growth: (growth {grownPerSecond.Value:F2}/s GetHandle {(ulong)GetHandleTimer.ElapsedTicks / GetHandleCount} ticks/op)");
			UI.DrawText(left, top + (line++ * lineHeight), $"{Ungrown.Count} ungrown in {AllNodes.Regions.Count} regions ({AllNodes.dirtyRegions.Count} dirty)");
		}

		public static void Block(NodeHandle n) {
			PropagateClearance(n, 1);
			Remove(n);
			IsGrown(n, true);
			Text.Add(Position(n), "Blocked", 3000);
		}

		public static bool Remove(NodeHandle a) => AllNodes.Regions.TryGetValue(Region(a), out var nodes) && nodes.TryRemove(a, out var edges);

		public static void GetAllHandlesInBox(ModelBox box, ConcurrentSet<NodeHandle> output) => GetAllHandlesInBox(box.M, box.Back, box.Front, output);
		public static void GetAllHandlesInBox(Matrix4x4 m, Vector3 backLeft, Vector3 frontRight, ConcurrentSet<NodeHandle> output) {
			float minX = Min(backLeft.X, frontRight.X);
			float minY = Min(backLeft.Y, frontRight.Y);
			float minZ = Min(backLeft.Z, frontRight.Z);
			float maxX = Max(backLeft.X, frontRight.X);
			float maxY = Max(backLeft.Y, frontRight.Y);
			float maxZ = Max(backLeft.Z, frontRight.Z);
			while( (maxX - minX) < 1f ) {
				minX -= .1f; maxX += .1f;
			}
			while( (maxY - minY) < 1f ) {
				minY -= .1f; maxY += .1f;
			}
			for( float x = minX; x <= maxX + .1f; x += gridScale ) {
				for( float y = minY; y <= maxY + .1f; y += gridScale ) {
					for( float z = minZ; z <= maxZ + .1f; z += zScale ) {
						var v = new Vector3(x, y, z);
						var p = Vector3.Transform(v, m);
						output.Add(Handle(p));
					}
				}
			}
		}

		public static Color GetColor(NodeHandle a) {
			return (IsCover(a) ? Color.Blue :
				IsGrown(a) ? Color.Gray :
				Color.White);
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

	}
}
