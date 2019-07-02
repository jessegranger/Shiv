﻿using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using static System.Math;
using static Shiv.Global;
using static GTA.Native.Hash;
using static GTA.Native.Function;

namespace Shiv {

	public struct ModelBox {
		public Matrix4x4 M;
		public Vector3 Front;
		public Vector3 Back;
	}
	public class PathRequest : Future<Path> {
		public NodeHandle Start;
		public NodeHandle Target;
		public uint Timeout = 100;
		public bool AvoidObjects = true;
		public bool AvoidPeds = false;
		public bool AvoidCars = true;
		public Stopwatch Running = new Stopwatch();
		public Stopwatch Stopped = new Stopwatch();
		public ConcurrentSet<NodeHandle> Blocked;
		public PathRequest(NodeHandle start, NodeHandle target, uint timeout, bool avoidPeds, bool avoidCars, bool avoidObjects, bool debug=false):base() {
			Start = start;
			Target = target;
			if( start == NodeHandle.Invalid ) {
				Reject(new ArgumentException(nameof(start)));
				return;
			}
			if( target == NodeHandle.Invalid ) {
				Reject(new ArgumentException(nameof(target)));
				return;
			}
			if( !IsGrown(Target) ) {
				Log($"[{Target}] target node not grown, trying to grow there.");
				NavMesh.Grow(Target, 10);
			}
			Timeout = timeout;
			AvoidPeds = avoidPeds;
			AvoidCars = avoidCars;
			AvoidObjects = avoidObjects;
			Blocked = Pathfinder.GetBlockedNodes(AvoidObjects, AvoidCars, AvoidPeds, debug);
			Log($"[{Target}] Pre-blocked {Blocked.Count} nodes.");
			Running.Start();
			ThreadPool.QueueUserWorkItem((object arg) => {
				RequestRegion(Region(Target)).Wait(500);
				RequestRegion(Region(Start)).Wait(500);
				if( !HasEdges(Target) ) {
					Log($"[{Target}] target node has no edges, groping for nearest graph...");
					// Target = NavMesh.Flood(Target, 2, cancel.Token, PossibleEdges).Where(HasEdges).OrderBy(DistanceToSelf).FirstOrDefault();
					if( Target == NodeHandle.Invalid ) {
						Reject(new Exception("Target node is not mappable."));
						return;
					}
				}
				try {
					PathStatus.Guard.Wait(1000, cancel.Token);
				} catch( Exception err ) {
					Reject(err);
					return;
				}
				PathStatus.Queue.Enqueue(this);
				try { Resolve(Pathfinder.FindPath(Start, Target, Blocked, Timeout, cancel.Token)); }
				catch( Exception err ) { Reject(err); }
				finally {
					PathStatus.Guard.Release();
					Running.Stop();
					Stopped.Start();
				}
			});
		}
		public override string ToString() {
			var elapsed = Running.ElapsedMilliseconds;
			return $"{Blocked.Count} in {elapsed}ms "
				+ (IsReady() ? GetResult().ToString() : "")
				+ (IsFailed() ? GetError().ToString() : "")
				+ (IsCanceled() ? "Canceled" : "");
		}
	}
	public static class PathStatus {
		public static ConcurrentQueue<PathRequest> Queue = new ConcurrentQueue<PathRequest>();
		public static SemaphoreSlim Guard = new SemaphoreSlim(1, 1); // only one at a time, available now
		public static void CancelAll() {
			while( Queue.TryDequeue(out PathRequest req) ) {
				if( ! req.IsDone() ) {
					req.Cancel();
				}
			}
		}
		static readonly float lineHeight = .02f;
		static readonly float padding = .005f;
		static readonly float top = .6f;
		static readonly float left = 0f;
		static readonly float width = .12f;
		public static void Draw() {
			int numLines = Queue.Count + 1;
			UI.DrawRect(left, top, width, padding + (lineHeight * numLines) + padding, Color.SlateGray);
			UI.DrawText(left, top, $"Pathfinder: {Queue.Count} Active");
			UI.DrawText(left, top - lineHeight, Pathfinder.Timers());
			int lineNum = 0;
			while( Queue.TryPeek(out PathRequest req) 
				&& req.IsDone()
				&& req.Stopped.ElapsedMilliseconds > 20000 ) {
				// let path requests sit around for a few seconds so they can be seen 
				Queue.TryDequeue(out PathRequest done);
			}
			foreach( PathRequest req in Queue ) {
				UI.DrawText(padding + left, padding + top + (++lineNum * lineHeight), req.ToString());
				if( lineNum > 7 ) {
					break;
				}
			}
		}
	}
	public class Path : IEnumerable<NodeHandle> {
		// Path is a type alias for a lazy list of NodeHandles
		// iter comes from UnrollPath inside FindPath
		private IEnumerable<NodeHandle> iter;
		public Path(IEnumerable<NodeHandle> path) => iter = path;
		public IEnumerator<NodeHandle> GetEnumerator() => iter.GetEnumerator();
		IEnumerator IEnumerable.GetEnumerator() => iter.GetEnumerator();
		public void Draw() => this.Select(Position)
				.Each(DrawSphere(.06f, Color.Yellow));
		public override string ToString() => $"({this.Count()} steps)";
	}

	public static partial class Global {

		public static Vector3 Position(Path path) => path == null ? Vector3.Zero : Position(path.FirstOrDefault());

	}

	public class Pathfinder : Script {

		public override void OnTick() => PathStatus.Draw();

		private static float Estimate(NodeHandle a, NodeHandle b) => (Position(a) - Position(b)).Length();
		private static float Estimate(Vector3 a, Vector3 b) => (a - b).Length();

		private static IEnumerable<NodeHandle> UnrollPath(Dictionary<NodeHandle, NodeHandle> cameFrom, NodeHandle cur, bool debug=false) {
			if( cur != NodeHandle.Invalid ) {
				var ret = new Stack<NodeHandle>();
				ret.Push(cur);
				while( cameFrom.ContainsKey(cur) ) {
					ret.Push(cur = cameFrom[cur]);
				}
				while( ret.Count > 0 ) {
					yield return ret.Pop();
				}
			}
		}

		// private static Stopwatch openSetTimer = new Stopwatch();
		private static Stopwatch closedSetTimer = new Stopwatch();
		private static Stopwatch regionTimer = new Stopwatch();
		private static Stopwatch findPathTimer = new Stopwatch();
		private static Stopwatch gScoreTimer = new Stopwatch();
		private static Stopwatch fScoreTimer = new Stopwatch();
		public static string Timers() {
			long ticks = findPathTimer.ElapsedTicks + 1; // no divide by zero
			return $"C:{100 * closedSetTimer.ElapsedTicks / ticks}% R:{100 * regionTimer.ElapsedTicks/ticks}% g:{100 * gScoreTimer.ElapsedTicks / ticks} f:{100 * fScoreTimer.ElapsedTicks / ticks}";
		}

		private static Path Fail(string msg) {
			Log(msg);
			UI.DrawText(.3f, .3f, msg);
			return new Path(Enumerable.Empty<NodeHandle>());
		}
		internal static Path FindPath(NodeHandle startNode, NodeHandle targetNode, ConcurrentSet<NodeHandle> closedSet, uint maxMs, CancellationToken cancelToken, bool debug=false) {
			var s = new Stopwatch();
			findPathTimer.Start();
			try {
				Vector3 targetNodePos = Position(targetNode);

				if( startNode == 0 ) {
					return Fail($"[{targetNode}] FindPath failed: startNode is zero.");
				}
				if( targetNode == 0 ) {
					return Fail($"[{targetNode}] FindPath failed: targetNode is zero.");
				}

				closedSetTimer.Start();
				closedSet.Remove(startNode);
				if( closedSet.Contains(targetNode) ) {
					return Fail($"[{targetNode}] FindPath failed: targetNode is blocked");
				}
				closedSetTimer.Stop();

				// TODO: it would be best if we could combine fScore and openSet
				// fScore should be a heap that re-heaps when a value updates
				// isOpen(node) becomes fScore.Contains(node)
				// var fScore = new Dictionary<NodeHandle, float>();
				var fScore = new Heap<NodeHandle>();
				fScore.Add(startNode, Estimate(startNode, targetNode));

				// var openSet = new HashSet<NodeHandle>();
				// var openSet = new SortedSet<NodeHandle>(Comparer<NodeHandle>.Create((NodeHandle a, NodeHandle b) => FScore(a).CompareTo(FScore(b))));
				// var openSet = new Heap<NodeHandle>(10000, FScore);
				var cameFrom = new Dictionary<NodeHandle, NodeHandle>();

				var gScore = new Dictionary<NodeHandle, float>();
				gScore.TryAdd(startNode, 0);
				float GScore(NodeHandle n) => gScore.ContainsKey(n) ? gScore[n] : float.MaxValue;

				s.Start();
				while( fScore.TryPop(out NodeHandle best) ) {
					if( cancelToken.IsCancellationRequested ) {
						return Fail($"[{targetNode}] Cancelled.");
					}
					if( s.ElapsedMilliseconds > maxMs ) {
						return Fail($"[{targetNode}] Searching for too long, ({closedSet.Count} nodes in {s.ElapsedMilliseconds}ms.");
					}

					closedSetTimer.Start();
					closedSet.Add(best);
					closedSetTimer.Stop();
					/*
					if( closedSet.Count > 100 ) {
						return Fail($"[{targetNode}] Searching too many nodes {closedSet.Count}");
					}
					*/

					regionTimer.Start();
					RequestRegion(Region(best)).Wait(100);
					regionTimer.Stop();

					Vector3 curPos = Position(best);
					float dist = (curPos - targetNodePos).LengthSquared();
					// Log($"dist = {dist:F2}");
					if( dist <= .5f ) {
						var ret = new Path(UnrollPath(cameFrom, best, debug));
						Log($"[{targetNode}] Found a path of {ret.Count()} steps ({closedSet.Count} searched in {s.ElapsedMilliseconds}ms)");
						return ret;
					}

					foreach( NodeHandle e in Edges(best) ) {
						closedSetTimer.Start();
						bool closed = closedSet.Contains(e);
						closedSetTimer.Stop();

						if( !closed ) {

							gScoreTimer.Start();
							Vector3 ePos = Position(e);
							float scoreOfNewPath = GScore(best) + (curPos - ePos).Length();
							float scoreOfOldPath = GScore(e);
							gScoreTimer.Stop();

							if( scoreOfNewPath < scoreOfOldPath ) {
								cameFrom[e] = best;
								gScore[e] = scoreOfNewPath;
								fScoreTimer.Start();
								fScore.AddOrUpdate(e,
									gScore[e] // best path to e so far
									+ Estimate(ePos, targetNodePos) // plus standard A* estimate
									+ Abs(ePos.Z - curPos.Z) // plus a penalty for going vertical
								);
								fScoreTimer.Stop();
							}
						}
					}
				}
				return Fail($"[{targetNode}] Searched all reachable nodes ({closedSet.Count} nodes in {s.ElapsedMilliseconds}ms).");
			} finally {
				findPathTimer.Stop();
			}
		}

		public static float GetVolume(Vector3 frontRight, Vector3 backLeft) {
			var diag = frontRight - backLeft;
			return Abs(diag.X) * Abs(diag.Y) * Abs(diag.Z);
		}
		private static readonly Random random = new Random();
		private static HashSet<long> ignoreModels = new HashSet<long>() {
			(long)ModelHash.Planter, // (long)ModelHash.WoodenDoor,
			1423868860, 416711217, 3231494328, 862871082, 3639322914, 1043035044,
			431612653, 148963242, 213616483, 1021745343, 3180272150, 729253480,
			3837338037, 1431982911, 1258634901, 4090487972, 1726633148, 3510238396,
			3585126029, 2745551480, 326972916, 1392246133, 672606124, 1105091386,
			4068091429, 2967538074, 3069248192, 1572003612
		};
		private static HashSet<ModelHash> checkDoorModels = new HashSet<ModelHash>() {
			ModelHash.WoodenDoor
		};
		public static IEnumerable<ModelBox> GetBlockingEnts(int limit=30) {
			foreach( EntHandle ent in NearbyObjects.Take(limit) ) {
				if( IsAttached(ent) ) {
					continue; // dont block on items held or carried by peds
				}
				Vector3 pos = Position(ent);
				if( pos == Vector3.Zero || DistanceToSelf(pos) > 100f * 100f ) {
					continue;
				}

				ModelHash model = GetModel(ent);
				if( checkDoorModels.Contains(model) ) {
					GetDoorState(model, pos, out bool locked, out float heading);
					if( !locked ) {
						continue;
					}
				}
				if( ignoreModels.Contains((long)model) ) {
					continue;
				}

				GetModelDimensions(model, out Vector3 backLeft, out Vector3 frontRight);
				if( GetVolume(frontRight, backLeft) > 10000 ) {
					ignoreModels.Add((long)model);
				} else {
					yield return new ModelBox() { M = Matrix(ent), Front = frontRight, Back = backLeft };
				}
			}
		}
		public static IEnumerable<ModelBox> GetBlockingVehicles(int limit=20) {
			VehicleHandle ignore = VehicleHandle.Invalid;
			if( IsOnVehicle(Self) ) {
				var result = Raycast(PlayerPosition, PlayerPosition - (2 * Up), .3f, IntersectOptions.Vehicles, Self);
				if( result.DidHit ) {
					ignore = (VehicleHandle)result.Entity;
				}
			}
			foreach( VehicleHandle v in NearbyVehicles.Take(limit) ) {
				if( v == PlayerVehicle || v == ignore ) {
					continue;
				}
				if( ! Call<bool>(IS_VEHICLE_SEAT_FREE, v, VehicleSeat.Driver) ) {
					// cars with drivers need a different solution (avoidance not blocking)
					continue;
				}
				VehicleHash model = GetModel(v);
				GetModelDimensions(model, out Vector3 backLeft, out Vector3 frontRight);
				yield return new ModelBox() { M = Matrix(v), Front = frontRight, Back = backLeft };
			}

		}
		public static IEnumerable<ModelBox> GetBlockingPeds(int limit=20) {
			foreach( PedHandle p in NearbyHumans.Take(limit) ) {
				if( p == Self || !IsAlive(p) ) {
					continue;
				}
				GetModelDimensions(GetModel(p), out Vector3 backLeft, out Vector3 frontRight);
				yield return new ModelBox() { M = Matrix(p), Front = frontRight, Back = backLeft };
			}
		}
		public static ConcurrentSet<NodeHandle> GetBlockedNodes(bool ents = true, bool vehicles = true, bool peds = true, bool debug = false) {
			var s = new Stopwatch();
			s.Start();
			var boxes = new List<ModelBox>();
			if( ents ) {
				boxes.AddRange(GetBlockingEnts(50));
			}

			if( vehicles ) {
				boxes.AddRange(GetBlockingVehicles(30));
			}

			if( peds ) {
				boxes.AddRange(GetBlockingPeds(20));
			}
			var blocked = new ConcurrentSet<NodeHandle>();
			Parallel.For(0, boxes.Count, i => NavMesh.GetAllHandlesInBox(boxes[i], blocked));

			s.Stop();
			UI.DrawText(.3f, .32f, $"Blocked: {blocked.Count} nodes in {s.ElapsedMilliseconds}ms.");
			return blocked;
		}

	}

	public class DebugPath : Goal {

		public bool AvoidObjects = false;
		public bool AvoidPeds = false;
		public bool AvoidCars = false;

		public bool GrowIfNeeded = false;

		public NodeHandle TargetNode { get; private set; }

		private PathRequest future;
		public DebugPath(Vector3 v) : this(GetHandle(PutOnGround(v, 1f))) { }
		public DebugPath(NodeHandle targetNode) {
			TargetNode = targetNode;
			if( !NavMesh.IsGrown(TargetNode) && GrowIfNeeded ) {
				NavMesh.Grow(TargetNode, 5);
			}

			future = new PathRequest(PlayerNode, TargetNode, 30000, AvoidPeds, AvoidCars, AvoidObjects);
		}
		private readonly Random random = new Random();
		public override GoalStatus OnTick() {
			UI.DrawText($"TargetNode: {TargetNode} {Position(TargetNode)}");
			DrawSphere(Position(TargetNode), .06f, Color.Yellow);
			DrawLine(HeadPosition(Self), Position(TargetNode), Color.Yellow);
			var blocked = future.Blocked.Select(Position).OrderBy(DistanceToSelf).Take(100).Each(DrawSphere(.02f, Color.Red));
			/*
			Vector3 prev = Position(blocked.FirstOrDefault());
			foreach( NodeHandle node in blocked.Skip(1) ) {
				Vector3 cur = Position(node);
				if( (cur - prev).LengthSquared() < 4f ) {
					DrawLine(prev, cur, Color.Yellow);
				}
				prev = cur;
			}
			*/
			if( future.IsReady() ) {
				future.GetResult().Draw();
			}
			return GoalStatus.Active;
		}

	}
}
