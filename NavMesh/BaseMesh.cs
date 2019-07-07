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

		public static NodeHandle AimNode() => NavMesh.Handle(PutOnGround(AimPosition() + (Up * .5f), 1f));

	}

	public partial class NavMesh : Script {

		public static bool Enabled = true;
		public static bool SaveEnabled = false;
		public static bool LoadEnabled = false;

		private const int magicBytes = 0x000FEED8; // if any constants/structure below should change, increment the magic bytes and update Save/Read methods

		private const ulong mapRadius = 8192; // in the world, the map goes from -8192 to 8192
		private const float gridScale = .5f; // how big are the X,Y steps of the mesh
		private const float zScale = .25f; // how big are the Z steps of the mesh
		private const float zDepth = 1000f; // how deep underwater can the mesh go
																				// zDepth can be at most mapRadius*zScale (==1024), because zScale changes vertical node density
																				// which makes mapShift too small, which corrupts NodeHandles
		private const int mapShift = 15; //  (int)Math.Log(mapRadius * 2 / gridScale, 2);
		private static readonly uint mapShiftMask = (1u << mapShift) - 1;


		// how to pack Vector3 into a long:
		// add the mapRadius (so strictly positive)
		// divide X,Y,Z by their scales
		// round to a grid center and cast to ulong
		// knowing that all values will be in [0..mapRadius*2/scale] = 32k = 15 bits per coord
		// storing X,Y,Z this way uses 3 * 15 bits = 45 bits = 1 ulong with 19 bits to spare
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static NodeHandle Handle(Vector3 v) {
			if( v == Vector3.Zero ) {
				return NodeHandle.Invalid;
			}
			GetHandleTimer.Start();
			GetHandleCount += 1;
			// v.X starts [-8192..8192] becomes [0..32k]
			ulong x1 = (ulong)(v.X + mapRadius);
			ulong y1 = (ulong)(v.Y + mapRadius);
			ulong z1 = (ulong)((v.Z + zDepth) / zScale);
			ulong nx = (ulong)(x1 << 1); // << 1 equivalent to / gridScale (/.5f) == (*2) == (<<1)
			ulong ny = (ulong)(y1 << 1);
			ulong nz = (ulong)(z1); // << 2 equivalent to / zScale (/.25f) == (* 4) or (<< 2)
			uint rx = (uint)(x1 >> regionShift); // Round((v.X + mapRadius) / regionScale)); // (/128) == (>>7)
			uint ry = (uint)(y1 >> regionShift);
			uint rz = (uint)(z1 >> regionShift);
			ulong r = ((rx << (regionShift << 1)) | (ry << regionShift) | rz);
			var node = (NodeHandle)(
				(r << (mapShift * 3)) |
				(nx << (mapShift << 1)) |
				(ny << mapShift) |
				nz
			);
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
			ulong mask = (ulong)(1 << mapShift) - 1; // make a 15-bit mask
			ulong h = (ulong)handle;
			ulong nz = h & mask;
			h >>= mapShift;
			ulong ny = h & mask;
			h >>= mapShift;
			ulong nx = h & mask;
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
		public static ConcurrentDictionary<NodeHandle, NodeEdges> AllEdges = new ConcurrentDictionary<NodeHandle, NodeEdges>();
		public static bool IsLoaded => aborted == 0;
		public static int Count => AllEdges.Count;
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
				NodeHandle handle = AimNode();
				if( !IsGrown(handle) ) {
					Ungrown.Enqueue(handle);
				}
				DrawEdges(handle, 100);
				if( !IsGrown(PlayerNode) ) {
					Grow(PlayerNode, msPerGrow);
				} else if( Ungrown.TryDequeue(out var first) ) {
					DrawLine(HeadPosition(Self), Position(first), Color.Orange);
					Grow(first, msPerGrow);
				} else {
					/*
					Grow(Flood(PlayerNode, 10000, 20, default, Edges)
						.Without(IsGrown)
						.FirstOrDefault(), msPerGrow);
						*/
				}
			}
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
			DrawLine(HeadPosition(Self), Position(node), Color.Blue);

			var queue = new Queue<NodeHandle>();
			var seen = new HashSet<NodeHandle>();
			queue.Enqueue(node);
			seen.Add(node);
			while( queue.Count > 0 && seen.Count < maxNodes ) {
				NodeHandle cur = queue.Dequeue();
				var pos = Position(cur);
				var c = Clearance(cur);
				// UI.DrawTextInWorldWithOffset(pos, 0f, -.01f * Clearance(cur), $"{Clearance(cur)}");
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
			float top = .75f;
			int line = 0;
			UI.DrawText(left, top + (line++ * lineHeight), $"NavMesh: {PlayerNode} {Position(PlayerNode)} {Region(PlayerNode)}");
			UI.DrawText(left, top + (line++ * lineHeight), $"Growth: (growth {grownPerSecond.Value:F2}/s GetHandle {(ulong)GetHandleTimer.ElapsedTicks / GetHandleCount} ticks/op)");
			UI.DrawText(left, top + (line++ * lineHeight), $"{Ungrown.Count}/{AllEdges.Count} nodes in {loadedRegions.Count} regions ({dirtyRegions.Count} dirty)");
		}

		public static void Block(NodeHandle n) {
			PropagateClearance(n, 1);
			Remove(n);
			IsGrown(n, true);
			Text.Add(Position(n), "Blocked", 3000);
		}

		public static bool Remove(NodeHandle a) => AllEdges.TryRemove(a, out var edges);

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

		public override void OnInit() {
			if( CurrentPlayer != PlayerHandle.Invalid ) {
				var self = Call<PedHandle>(GET_PLAYER_PED, CurrentPlayer);
				if( self != PedHandle.Invalid ) {
					var pos = Global.Position(self);
					if( pos != Vector3.Zero ) {
						var r = Region(pos);
						Log($"OnInit: requesting region {r}");
						RequestRegion(r);
					}
				}
			}
			ReadFrontierFile();
			base.OnInit();
		}

		public override void OnAbort() {
			aborted = GameTime;
			if( AllEdges != null ) {
				AllEdges.Clear();
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

	}
}
