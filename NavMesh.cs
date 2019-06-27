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

// pragma warning disable CS0649
namespace Shiv {


	public static partial class Global {

		public enum NodeHandle : ulong { Invalid = 0 };

		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static float DistanceToSelf(NodeHandle n) => DistanceToSelf(NavMesh.Position(n));

		public static NodeHandle PlayerNode;

		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static NodeHandle GetHandle(Vector3 v) => NavMesh.GetHandle(v);
		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector3 Position(NodeHandle handle) => NavMesh.Position(handle);
		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static IEnumerable<NodeHandle> Edges(NodeHandle a) => NavMesh.Edges(a);

		public static float CheckObstruction(Vector3 pos, Vector3 forward) {
			var opts = IntersectOptions.Map | IntersectOptions.MissionEntities | IntersectOptions.Objects
				| IntersectOptions.Unk1 | IntersectOptions.Unk2 | IntersectOptions.Unk3 | IntersectOptions.Unk4;
			forward = Vector3.Normalize(forward) * .6f;
			pos = pos + forward + (Up * 1.2f); // pick a spot in the air
			var end = new Vector3(pos.X, pos.Y, pos.Z - 1.9f); // try to drop it down
			// DrawLine(pos, end, Color.Yellow);
			// DrawLine(HeadPosition(Self), end, Color.Orange);
			var result = Raycast(pos, end, .3f, opts, Self);
			// if( result.DidHit ) DrawSphere(result.HitPosition, .2f, Color.Orange);
			return result.DidHit ? (result.HitPosition - end).Length() : 0f;
		}

		public static class VehicleOffsets {
			// these are coordinates relative to the bounding box of the model,
			// origin is at the center
			// so (-.5f, 0f, 0f) is the left side of the model, (.5f, 0f, 0f) is the right side 
			// scales based on the model's actual size later
			public static readonly Vector3 DriverDoor = new Vector3(-.6f, 0.05f, 0f);
			public static readonly Vector3 FrontGrill = new Vector3(0f, .5f, 0f);
			public static readonly Vector3 FrontLeftWheel = new Vector3(-.66f, .3f, 0f);
			public static readonly Vector3 FrontRightWheel = new Vector3(.66f, .3f, 0f);
			public static readonly Vector3 BackLeftWheel = new Vector3(-.66f, -.3f, 0f);
			public static readonly Vector3 BackRightWheel = new Vector3(.66f, -.3f, 0f);
			public static readonly Vector3 BackBumper = new Vector3(0f, -.66f, 0f);
		}
		public static Vector3 GetVehicleOffset(VehicleHandle v, Vector3 offset) => GetVehicleOffset(v, offset, Matrix(v));
		public static Vector3 GetVehicleOffset(VehicleHandle v, Vector3 offset, Matrix4x4 m) => GetVehicleOffset(v, offset, m, GetModel(v));
		public static Vector3 GetVehicleOffset(VehicleHandle v, Vector3 offset, Matrix4x4 m, VehicleHash model) {
			GetModelDimensions(model, out Vector3 backLeft, out Vector3 frontRight);
			return GetVehicleOffset(v, offset, m, frontRight, backLeft);
		}
		public static Vector3 GetVehicleOffset(VehicleHandle v, Vector3 offset, Matrix4x4 m, Vector3 frontRight, Vector3 backLeft) {
			return Vector3.Transform(offset * (frontRight - backLeft), m);
		}

	}

	public class NavMesh : Script {

		public static bool Enabled = true;
		public static bool LoadEnabled = true;
		public static bool SaveEnabled = true;
		private const string DataFile = "scripts/LongMesh.dat";
		private const ulong mapShift = 8192;
		private readonly Vector3 Scale = new Vector3(1f, 1f, .5f);
		private const float gridScale = .5f;
		private const float zScale = .25f;
		public static NodeHandle LastGrown = NodeHandle.Invalid;


		private const int zShift = 30; // 1073741824
		private const int yShift = 15;
		private const int xShift = 0;
		private const IntersectOptions growRayOpts = IntersectOptions.Map | IntersectOptions.Objects | IntersectOptions.Unk1 | IntersectOptions.Unk2 | IntersectOptions.Unk3 | IntersectOptions.Unk4;
		private const float capsuleSize = 0.30f;
		private const float maxGrowRange = 70f * 70f;
		internal static HashSet<NodeHandle> Ungrown = new HashSet<NodeHandle>();

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static NodeHandle GetHandle(Vector3 v) {
			if( v == Vector3.Zero ) return NodeHandle.Invalid;
			ulong x = (ulong)Round((v.X + mapShift) / gridScale);
			ulong y = (ulong)Round((v.Y + mapShift) / gridScale);
			ulong z = (ulong)(v.Z / zScale);
			return (NodeHandle)((x << xShift) | (y << yShift) | (z << zShift));
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static Vector3 Position(NodeHandle handle) {
			if( handle == NodeHandle.Invalid )
				return Vector3.Zero;
			ulong mask = (ulong)(1 << yShift) - 1;
			ulong h = (ulong)handle;
			ulong ix = (h >> xShift) & mask;
			ulong iy = (h >> yShift) & mask;
			ulong iz = (h >> zShift) & mask;
			float x = (gridScale * ix) - mapShift;
			float y = (gridScale * iy) - mapShift;
			float z = (zScale * iz);
			return new Vector3(x, y, z);
		}

		public NavMesh() { }

		private static uint started = 0;
		private static uint loaded = 0;
		private static uint saved = 0;
		private static uint aborted = 0;
		public static ConcurrentDictionary<NodeHandle, uint> AllEdges = null; // new ConcurrentDictionary<NodeHandle, uint>();
		public static bool Dirty = false;
		private static readonly uint xStep = (uint)Pow(2, xShift);
		private static readonly uint yStep = (uint)Pow(2, yShift);
		private static readonly uint zStep = (uint)Pow(2, zShift);
		private static readonly Dictionary<long, int> whichEdgeBit = new Dictionary<long, int>() {

			{ (+0*xStep) + (+1*yStep) + (+2*zStep), 0 },
			{ (+1*xStep) + (+0*yStep) + (+2*zStep), 1 },
			{ (-1*xStep) + (+0*yStep) + (+2*zStep), 2 },
			{ (+0*xStep) + (-1*yStep) + (+2*zStep), 3 },

			{ (+1*xStep) + (+1*yStep) + (+1*zStep), 4 },
			{ (+0*xStep) + (+1*yStep) + (+1*zStep), 5 },
			{ (-1*xStep) + (+1*yStep) + (+1*zStep), 6 },
			{ (+1*xStep) + (+0*yStep) + (+1*zStep), 7 },
			{ (+0*xStep) + (+0*yStep) + (+1*zStep), 8 },
			// TODO: when we find ladders, build chains of edge 8 (up) and edge 25 (down) to model it

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

			// { (0*xStep) + (1*yStep) + (2*zStep), 30 },
			// { (1*xStep) + (0*yStep) + (2*zStep), 31 },
			// { (-1*xStep) + (0*yStep) + (2*zStep), 32 },
			// { (0*xStep) + (-1*yStep) + (2*zStep), 33 },


		};
		public static readonly long[] edgeOffsets = whichEdgeBit.Keys.OrderByDescending(k => k).ToArray();
		public static bool IsLoaded { get => AllEdges != null && aborted == 0; }
		public static int Count { get => AllEdges == null ? 0 : AllEdges.Count; }
		public static bool ShowEdges { get; internal set; } = false;

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
			} else if( GameTime - started < 100 || !CanControlCharacter() ) {
				UI.DrawText(.55f, .45f, $"Warmup...{new TimeSpan(0,0,0,0,(int)(GameTime - started))}");
			} else if( LoadEnabled && loaded == 0 ) {
				loaded = GameTime;
				Task.Run(() => {
					saved = GameTime;
					ReadFromFile(DataFile);
				});
			} else if( AllEdges != null ) {
				if( SaveEnabled && GameTime - saved > 90000 ) {
					saved = GameTime;
					Task.Run(() => { SaveToFile(DataFile); });
				}
				// UI.DrawText($"NavMesh: {Ungrown.Count}/{AllEdges.Count}", color: IsGrown(PlayerNode) ? Color.White : Color.Orange);
				int msPerFrame = (int)(1000 / CurrentFPS);
				uint msPerGrow = (uint)Max(5, 35 - msPerFrame);
				if( !IsGrown(PlayerNode) ) {
					Grow(PlayerNode, msPerGrow);
					/* Debug Edge connections
					var pos = Position(PlayerNode);
					Range(13,21).Each(i => {
						var e = (NodeHandle)((long)PlayerNode + edgeOffsets[i]);
						var epos = Position(e);
						DrawLine(pos, epos, Color.Teal);
						UI.DrawTextInWorld(epos, $"{i}");
					});
					*/
				} else {
					DrawEdges(PlayerNode, 7);
					if( Ungrown.Count > 0 ) {
						NodeHandle first = NodeHandle.Invalid;
						if( Ungrown.Count > 1200 ) {
							lock( Ungrown ) {
								Ungrown = Ungrown.OrderBy(DistanceToSelf).Take(Ungrown.Count / 2).ToHashSet();
							}
						}
						first = Ungrown.OrderBy(DistanceToSelf).FirstOrDefault();
						if( first != NodeHandle.Invalid ) {
							lock( Ungrown ) {
								Ungrown.Remove(first);
							}
							DrawLine(HeadPosition(Self), Position(first), Color.Orange);
							Grow(first, msPerGrow);
						}
					} else {
						var pos = PutOnGround(AimPosition(), 1f);
						var handle = GetHandle(pos);
						pos = Position(handle);
						if( !IsGrown(handle) ) {
							Sphere.Add(pos, .1f, Color.Red, 3000);
							Grow(handle, msPerGrow);
						}
					}
					// if( Ungrown.Count > 0 ) { foreach(var n in Ungrown ) { DrawSphere(Position(n), .1f, Color.Orange); } }
				}
			}
		}

		private void DrawEdges(NodeHandle node, uint depth) {
			if (!ShowEdges) return;
			DrawLine(Global.Position(Self), Position(node), Color.Pink);
			DrawEdges(node, Position(node), depth, new HashSet<NodeHandle>(), new HashSet<NodeHandle>(), 0);
		}
		private void DrawEdges(NodeHandle node, Vector3 nodePos, uint depth, HashSet<NodeHandle> stack, HashSet<NodeHandle> seen, int count) {
			if( ShowEdges == false
				|| depth <= 0
				|| stack.Contains(node)
				)
				return;
			if( !seen.Contains(node) ) {
				seen.Add(node); // prevent double visit
				stack.Add(node); // prevent cycles
				if( IsCover(node) ) {
					DrawSphere(nodePos, .05f, Color.Blue);
				}
				//UI.DrawTextInWorldWithOffset(nodePos, 0f, -.005f * count, $"{count}");
				foreach( var e in Edges(node) ) {
					var ePos = Position(e);
					count += 1;
					DrawLine(nodePos, ePos, Color.Yellow);
					DrawEdges(e, ePos, depth - 1, stack, seen, count);
				}
				stack.Remove(node);
			}
		}

		internal static void Grow(NodeHandle node, uint ms, bool debug=false) {
			if( !NavMesh.IsLoaded )
				return;
			var sw = new Stopwatch();
			sw.Start();
			var result = Grow(node, ms, sw, Ungrown, debug);
			while( result == GrowResult.IsGrowing && sw.ElapsedMilliseconds < ms ) {
				node = Ungrown.OrderBy(DistanceToSelf).FirstOrDefault();
				Grow(node, ms, sw, Ungrown, debug);
			}
			if( debug ) UI.DrawText(.3f, .4f, $"Grew for {sw.ElapsedMilliseconds} / {ms} ms ({result})");
		}
		public static bool ShowGrowth = false;
		private enum GrowResult {
			InvalidNode,
			NodeOnStack,
			IsGrown,
			TooFar,
			Timeout,
			NotLoaded,
			IsGrowing
		}
		private static GrowResult Grow(NodeHandle node, uint ms, Stopwatch sw, HashSet<NodeHandle> stack, bool debug = false) {
			if( !IsLoaded ) {
				return GrowResult.NotLoaded;
			}
			if( node == NodeHandle.Invalid ) {
				return GrowResult.InvalidNode;
			}
			Vector3 nodePos = Position(node);
			if( stack.Contains(node) ) {
				if( debug ) UI.DrawTextInWorld(nodePos, "OnStack");
				return GrowResult.NodeOnStack;
			}
			if( IsGrown(node) ) {
				if( debug ) UI.DrawTextInWorld(nodePos, "IsGrown");
				return GrowResult.IsGrown;
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

			try {
				// Push growth out on certain edges by following their edgeOffsets
				Items(0,1,2,3,4,6,10,12).Select(i => {
				// Range(0, 13).Select(i => {
					var e = (NodeHandle)((long)node + edgeOffsets[i]);
					var ePos = Position(e);
					var gPos = PutOnGround(ePos, 1f);
					NodeHandle g = GetHandle(gPos);
					gPos = Position(g);
					if( debug ) {
						DrawLine(nodePos, gPos, Color.White);
						UI.DrawTextInWorld(gPos, $"{i}");
					}
					if( IsPossibleEdge(node, g) && !HasEdge(node, g) ) {
						var delta = gPos - nodePos;
						var len = delta.Length();
						Vector3 end = nodePos + (Vector3.Normalize(delta) * (len - capsuleSize / 2));
						var result = Raycast(nodePos, end, capsuleSize, growRayOpts, Self);
						if( result.DidHit ) {
							if( debug ) {
								DrawLine(nodePos + Up * .01f, end + Up * .01f, Color.Red);
								DrawLine(nodePos, result.HitPosition, Color.Red);
								DrawSphere(result.HitPosition, .05f, Color.Red);
							}
							/*
							if( Exists(result.Entity) ) {
								Text.Add(result.HitPosition, $"Grow Hit: {result.Entity}", 1000);
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
								if( debug )
									UI.DrawTextInWorld(nodePos, "IsCover");
								if( ShowGrowth )
									Sphere.Add(nodePos, .11f, Color.Blue, 1000);
								return NodeHandle.Invalid;
							}
						} else {
							if( debug ) {
								DrawLine(nodePos + Up * .01f, gPos + Up * .01f, Color.Green);
								DrawSphere(gPos, .03f, Color.Green);
							}
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
				LastGrown = node;
				IsGrown(node, true);
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

		public static IEnumerable<NodeHandle> Edges(NodeHandle a) {
			if( AllEdges == null )
				yield break;
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
			if( !whichEdgeBit.ContainsKey(d) )
				return false;

			// retrieve all the edges of a and check the bit for b
			return AllEdges.TryGetValue(a, out uint edges)
				? (edges & (0x1u << whichEdgeBit[d])) > 0
				: false;
		}

		private const int coverFlag = 31;
		private const int grownFlag = 30;
		public static bool Remove(NodeHandle a) {
			return AllEdges == null ? false : AllEdges.TryRemove(a, out uint edges);
		}
		internal static uint GetRawEdges(NodeHandle a) {
			if( AllEdges == null )
				return 0;
			AllEdges.TryGetValue(a, out uint edges);
			uint mask = unchecked((0x1u << grownFlag) | (0x1u << coverFlag));
			return edges & ~mask;
		}
		internal static void SetRawEdges(NodeHandle a, uint edges, bool grown, bool cover) {
			if( AllEdges == null )
				return;
			edges = edges
				| (grown ? (1u << grownFlag) : 0)
				| (cover ? (1u << coverFlag) : 0);
			AllEdges.AddOrUpdate(a, edges, (k, v) => edges);
			Dirty = true;
		}
		public static bool SetEdge(NodeHandle a, NodeHandle b, bool value) {
			if( AllEdges == null )
				return false;
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
			Dirty = true;
			return true;
		}
		public static bool AddEdge(NodeHandle a, NodeHandle b) => SetEdge(a, b, true);
		public static void RemoveEdge(NodeHandle a, NodeHandle b) => SetEdge(a, b, false);
		public static bool IsGrown(NodeHandle a) => AllEdges == null ? false : AllEdges.TryGetValue(a, out uint flags) && (flags & (1u << grownFlag)) > 0;
		public static void IsGrown(NodeHandle a, bool value) {
			if( AllEdges == null )
				return;
			uint mask = (1u << grownFlag);
			if( value ) {
				AllEdges.AddOrUpdate(a, mask, (key, oldValue) => oldValue | mask);
			} else {
				AllEdges.AddOrUpdate(a, 0, (key, oldValue) => oldValue & ~mask);
			}
			Dirty = true;
		}
		public static bool IsCover(NodeHandle a) => AllEdges == null ? false : AllEdges.TryGetValue(a, out uint flags) && (flags & (1u << coverFlag)) > 0;
		public static void IsCover(NodeHandle a, bool value) {
			if( AllEdges == null )
				return;
			uint mask = (1u << coverFlag);
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
			for( float x = minX; x <= maxX; x += gridScale ) {
				for( float y = minY; y <= maxY; y += gridScale ) {
					for( float z = minZ; z <= maxZ; z += zScale ) {
						var v = new Vector3(x, y, z);
						var p = Vector3.Transform(v, m);
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
					int magic = r.ReadInt32();
					if( magic != magicBytes ) {
						Shiv.Log($"Wrong magic bytes {magic:X}");
						return false;
					}
					int count = r.ReadInt32();
					if( count <= 0 ) {
						Shiv.Log($"Invalid count: {count}");
						return false;
					}
					byte[] buf;
					ulong[] handles = new ulong[count];
					uint[] edges = new uint[count];
					Shiv.Log($"Reading {count} edges...");
					buf = r.ReadBytes(count * sizeof(ulong));
					Buffer.BlockCopy(buf, 0, handles, 0, buf.Length);
					buf = r.ReadBytes(count * sizeof(uint));
					Buffer.BlockCopy(buf, 0, edges, 0, buf.Length);
					var newEdges = new ConcurrentDictionary<NodeHandle, uint>();
					Parallel.For(0, count, (i) => newEdges.TryAdd((NodeHandle)handles[i], edges[i]) );
					int ungrownCount = r.ReadInt32();
					if( ungrownCount > 0 ) {
						Shiv.Log($"Reading {ungrownCount} ungrown nodes...");
						ulong[] ungrown = new ulong[ungrownCount];
						buf = r.ReadBytes(ungrownCount * sizeof(ulong));
						Buffer.BlockCopy(buf, 0, ungrown, 0, buf.Length);
						Ungrown = new HashSet<NodeHandle>(ungrown.Cast<NodeHandle>());
					}
					Shiv.Log($"Finished loading {newEdges.Count}+{ungrownCount} nodes, after {s.Elapsed}");
					AllEdges = newEdges;
				}
			} catch( FileNotFoundException ) {
				Shiv.Log($"File not found: {filename}");
				AllEdges = new ConcurrentDictionary<NodeHandle, uint>();
			}
			Dirty = false;
			s.Stop();
			return true;
		}
		private const int magicBytes = 0x000FEED4;
		public static void SaveToFile(string filename = "scripts/LongMesh.dat") {
			if( !Dirty ) {
				return;
			}
			using( BinaryWriter w = Codec.Writer(filename + ".tmp") ) {
				w.Write(magicBytes);
				NodeHandle[] handles;
				uint[] edges;
				handles = AllEdges.Keys.ToArray();
				edges = handles.Select(h => AllEdges[h]).ToArray();
				byte[] buf;
				try {
					Shiv.Log($"Writing {handles.Length} handles to file...");
					w.Write(handles.Length);
					buf = new byte[handles.Length * sizeof(NodeHandle)];
					Buffer.BlockCopy(handles.Cast<ulong>().ToArray(), 0, buf, 0, buf.Length);
					w.Write(buf);
				} catch( Exception err ) {
					Shiv.Log("Failed: " + err.ToString());
					return;
				}
				try {
					Shiv.Log($"Writing {edges.Length} edges to file...");
					buf = new byte[edges.Length * sizeof(uint)];
					Buffer.BlockCopy(edges, 0, buf, 0, buf.Length);
					w.Write(buf);
				} catch( Exception err ) {
					Shiv.Log("Failed: " + err.ToString());
					return;
				}
				try {
					Shiv.Log($"Writing {Ungrown.Count} ungrown nodes to file...");
					ulong[] ungrown;
					lock( Ungrown ) {
						ungrown = Ungrown.Cast<ulong>().ToArray();
					}
					w.Write(ungrown.Length);
					buf = new byte[ungrown.Length * sizeof(NodeHandle)];
					Buffer.BlockCopy(ungrown, 0, buf, 0, buf.Length);
					w.Write(buf);
				} catch( Exception err ) {
					Shiv.Log("Failed: " + err.ToString());
					return;
				}
				w.Close();
				Shiv.Log("All bytes written.");
			}
			try { File.Delete(filename); } catch( FileNotFoundException ) { }
			try { File.Move(filename + ".tmp", filename); } catch( Exception e ) {
				Shiv.Log("File.Move Failed: " + e.ToString());
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static IEnumerable<NodeHandle> Select(NodeHandle n, int maxDepth) => Select(n, maxDepth, null, new HashSet<NodeHandle>(), new HashSet<NodeHandle>());
		[MethodImpl(MethodImplOptions.AggressiveInlining)] public static IEnumerable<NodeHandle> Select(NodeHandle n, int maxDepth, Predicate<NodeHandle> pred) => Select(n, maxDepth, pred, new HashSet<NodeHandle>(), new HashSet<NodeHandle>());
		private static IEnumerable<NodeHandle> Select(NodeHandle n, int maxDepth, Predicate<NodeHandle> pred, HashSet<NodeHandle> stack, HashSet<NodeHandle> seen) {
			if( AllEdges == null
				|| !AllEdges.ContainsKey(n)
				|| stack.Count >= maxDepth
				|| stack.Contains(n) ) {
				yield break;
			}
			try {
				stack.Add(n);
				if( (pred == null) || pred(n) ) {
					if( !seen.Contains(n) ) {
						seen.Add(n);
						yield return n;
					}
				}
				foreach( var e in Edges(n) ) {
					foreach( var r in Select(e, maxDepth, pred, stack, seen) ) {
						yield return r;
					}
				}
			} finally {
				stack.Remove(n);
			}
		}

		public static NodeHandle FirstOrDefault(NodeHandle n, int maxDepth, Predicate<NodeHandle> pred) {
			foreach( NodeHandle x in Select(n, maxDepth, pred, new HashSet<NodeHandle>(), new HashSet<NodeHandle>()) ) {
				return x;
			}
			return NodeHandle.Invalid;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void Visit(NodeHandle n, int maxDepth, Action<NodeHandle> action) => Visit(n, maxDepth, action, new HashSet<NodeHandle>(), new HashSet<NodeHandle>());
		private static void Visit(NodeHandle n, int maxDepth, Action<NodeHandle> action, HashSet<NodeHandle> stack, HashSet<NodeHandle> seen) {
			if( n == 0
				|| stack == null
				|| stack.Count > maxDepth
				|| stack.Contains(n) )
				return;
			stack.Add(n);
			if( !seen.Contains(n) ) {
				seen.Add(n);
				action(n);
			}
			try {
				foreach( NodeHandle e in Edges(n) ) {
					Visit(e, maxDepth, action, stack, seen);
				}
			} finally {
				stack.Remove(n);
			}
		}

		private static Random random = new Random();
		public static void DebugVehicle() {
			if( NearbyVehicles.Length == 0 )
				return;
			var v = NearbyVehicles[0];
			var m = Matrix(v);
			var model = GetModel(v);
			GetModelDimensions(model, out Vector3 backLeft, out Vector3 frontRight);

			foreach( Vector3 n in NavMesh.GetAllHandlesInBox(m, backLeft, frontRight).Select(Position) ) {
				if( random.NextDouble() > .5 ) DrawSphere(n, .05f, Color.Red);
			}

			var d = frontRight - backLeft;
			// UI.DrawTextInWorld(Vector3.Transform(new Vector3(0f, 0f, dZ/2), m), "dZ/2 HERE");
			// UI.DrawTextInWorld(Vector3.Transform(new Vector3(0f, dY/2, 0f), m), "dY/2 HERE");
			// UI.DrawTextInWorld(Vector3.Transform(new Vector3(dX/2f, 0f, 0f), m), "dX/2 HERE");
			Items(VehicleOffsets.DriverDoor, VehicleOffsets.FrontGrill, VehicleOffsets.FrontLeftWheel, VehicleOffsets.FrontRightWheel, VehicleOffsets.BackLeftWheel, VehicleOffsets.BackRightWheel)
				.Each((offset) => DrawSphere(Vector3.Transform(offset * d, m), .05f, Color.Aquamarine));
			UI.DrawTextInWorld(Vector3.Transform(VehicleOffsets.DriverDoor * d, m), "DriverDoor");
			UI.DrawTextInWorld(Vector3.Transform(VehicleOffsets.FrontGrill * d, m), "FrontGrill");
			UI.DrawTextInWorld(Vector3.Transform(VehicleOffsets.FrontLeftWheel * d, m), "FrontLeftWheel");
			UI.DrawTextInWorld(Vector3.Transform(VehicleOffsets.FrontRightWheel * d, m), "FrontRightWheel");
			UI.DrawTextInWorld(Vector3.Transform(VehicleOffsets.BackLeftWheel * d, m), "BackLeftWheel");
			UI.DrawTextInWorld(Vector3.Transform(VehicleOffsets.BackRightWheel * d, m), "BackRightWheel");
			UI.DrawTextInWorld(Vector3.Transform(VehicleOffsets.BackBumper * d, m), "BackBumper");
			FindCoverBehindVehicle(PlayerPosition, 2).ToArray();

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
			foreach( var v in NearbyVehicles.Take(maxScan) ) {
				var count = GetSeatMap(v).Values.Where(IsValid).Count();
				if( count > 0 ) {
					UI.DrawTextInWorld(Global.Position(v), $"Seat Map Occupied: {count}");
					continue;
				}
				var m = Matrix(v);
				var pos = Global.Position(m);
				var delta = (danger - pos);
				var heading = AbsHeading(Heading(m) - Rad2Deg(Atan2(delta.Y, delta.X)));
				int slot = (int)(6 * (heading / 360));
				var loc = GetVehicleOffset(v, vehicleCoverOffset[slot], m);
				yield return GetHandle(loc);
			}
		}

		public static NodeHandle FindClosestCover(NodeHandle node, Vector3 danger) {
			return FirstOrDefault(node, 6, (n) => {
				if( IsCover(n) ) {
					if( danger == Vector3.Zero ) {
						return true;
					}
					var pos = Position(n);
					var end = pos + Vector3.Normalize(danger - pos) * 2f;
					if( Raycast(pos, end, IntersectOptions.Map | IntersectOptions.Objects, Self).DidHit ) {
						Line.Add(pos, end, Color.Red, 5000);
						return true;
					}
				}
				return false;
			});
		}

	}
}
