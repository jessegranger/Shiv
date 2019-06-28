using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using static Shiv.Global;
using static GTA.Native.Hash;
using static GTA.Native.Function;

namespace Shiv {

	public class PathRequest : Future<Path> {
		public NodeHandle Start;
		public NodeHandle Target;
		public uint Timeout = 100;
		public bool AvoidObjects = true;
		public bool AvoidPeds = false;
		public bool AvoidCars = true;
		public DateTime Started { get; private set; } = default;
		public DateTime Ended { get; private set; } = default;
		public ConcurrentSet<NodeHandle> Blocked;
		public PathRequest(NodeHandle start, NodeHandle target, uint timeout, bool avoidPeds, bool avoidCars, bool avoidObjects):base() {
			Start = start;
			Target = target;
			if( !IsGrown(Target) ) {
				Log($"[{Target}] target node not grown, trying to grow there.");
				NavMesh.Grow(Target, 10);
			}
			Timeout = timeout;
			AvoidPeds = avoidPeds;
			AvoidCars = avoidCars;
			AvoidObjects = avoidObjects;
			Blocked = Pathfinder.GetBlockedNodes(AvoidObjects, AvoidCars, AvoidPeds);
			ThreadPool.QueueUserWorkItem((object arg) => {
				if( !HasEdges(Target) ) {
					Log($"[{Target}] target node has no edges, groping for nearest graph...");
					Target = NavMesh.Flood(Target, 2).Where(HasEdges).OrderBy(DistanceToSelf).FirstOrDefault();
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
				Started = new DateTime();
				try { Resolve(Pathfinder.FindPath(Start, Target, Blocked, Timeout, cancel.Token)); }
				catch( Exception err ) { Reject(err); }
				finally {
					PathStatus.Guard.Release();
					Ended = new DateTime();
				}
			});
		}
		public override string ToString() {
			var elapsed = (Ended - Started).TotalMilliseconds;
			return $"[{Target}] ({Blocked.Count} in {elapsed}ms)"
				+ (IsReady() ? GetResult().ToString() : "")
				+ (IsFailed() ? GetError().ToString() : "")
				+ (IsCanceled() ? "Canceled" : "");
		}
	}
	public static class PathStatus {
		public static ConcurrentQueue<PathRequest> Queue = new ConcurrentQueue<PathRequest>();
		public static SemaphoreSlim Guard = new SemaphoreSlim(1, 1); // only one at a time, available now
		public static void Clear() {
			while( Queue.TryDequeue(out PathRequest req) ) {
				if( ! req.IsDone() ) {
					req.Cancel();
				}
			}
		}
		static readonly float lineHeight = .02f;
		static readonly float padding = .005f;
		static readonly float top = .5f;
		static readonly float left = 0f;
		static readonly float width = .1f;
		public static void Draw() {
			int numLines = Queue.Count + 1;
			UI.DrawRect(left, top, width, padding + (lineHeight * numLines) + padding, Color.SlateBlue);
			UI.DrawText(left, top, "Pathfinder:");
			int lineNum = 0;
			while( Queue.TryPeek(out PathRequest req) 
				&& req.IsDone() && (DateTime.Now - req.Ended).TotalMilliseconds > 3000 ) {
				// let path requests sit around for a few seconds so they can be seen 
				Queue.TryDequeue(out PathRequest done);
			}
			foreach( PathRequest req in Queue ) {
				UI.DrawText(padding + left, padding + top + (lineNum++ * lineHeight), req.ToString());
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
	}

	public static partial class Global {

		public static Vector3 Position(Path path) => path == null ? Vector3.Zero : Position(path.FirstOrDefault());

	}

	public class Pathfinder : Script {

		public override void OnTick() => PathStatus.Draw();

		private static float Estimate(NodeHandle a, NodeHandle b) => (Position(a) - Position(b)).Length();

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

		public static Future<Path> RequestPath(PathRequest req, bool debug = false) => new Future<Path>((CancellationToken cancel) => new Path(
				 FindPath(req.Start, req.Target,
					 GetBlockedNodes(req.AvoidObjects, req.AvoidCars, req.AvoidPeds),
					 req.Timeout, cancel,
					 debug: debug)));

		private static Path Fail(string msg) {
			Log(msg);
			UI.DrawText(.3f, .3f, msg);
			return new Path(Enumerable.Empty<NodeHandle>());
		}
		internal static Path FindPath(NodeHandle startNode, NodeHandle targetNode, ConcurrentSet<NodeHandle> closedSet, uint maxMs, CancellationToken cancelToken, bool debug=false) {
			var s = new Stopwatch();
			Vector3 targetNodePos = Position(targetNode);
			// Log($"[{targetNode}] FindPath Starting...");

			if( startNode == 0 ) {
				return Fail($"[{targetNode}] FindPath failed: startNode is zero.");
			}
			if( targetNode == 0 ) {
				return Fail($"[{targetNode}] FindPath failed: targetNode is zero.");
			}
			closedSet.Remove(startNode);
			if( closedSet.Contains(targetNode) ) {
				return Fail($"[{targetNode}] FindPath failed: targetNode is blocked");
			}

			var fScore = new Dictionary<NodeHandle, float>();
			fScore.TryAdd(startNode, Estimate(startNode, targetNode));
			float FScore(NodeHandle n) => fScore.ContainsKey(n) ? fScore[n] : float.MaxValue;

			// Log($"[{targetNode}] Creating openSet...");
			var openSet = new HashSet<NodeHandle>();
			var cameFrom = new Dictionary<NodeHandle, NodeHandle>();

			openSet.Add(startNode);

			var gScore = new Dictionary<NodeHandle, float>();
			gScore.TryAdd(startNode, 0);
			float GScore(NodeHandle n) => gScore.ContainsKey(n) ? gScore[n] : float.MaxValue;

			s.Start();
			while( openSet.Count > 0 ) {
				if( cancelToken.IsCancellationRequested ) {
					return Fail($"[{targetNode}] Cancelled.");
				}
				if( s.ElapsedMilliseconds > maxMs ) {
					return Fail($"[{targetNode}] Searching for too long, ({closedSet.Count} nodes in {s.ElapsedMilliseconds}ms.");
				}
				NodeHandle cur = openSet.OrderBy(FScore).FirstOrDefault();
				openSet.Remove(cur);
				closedSet.Add(cur);
				Vector3 curPos = Position(cur);
				if( (curPos - targetNodePos).LengthSquared() <= .5f ) {
					var ret = new Path(UnrollPath(cameFrom, cur, debug));
					Log($"[{targetNode}] Found a path of {ret.Count()} steps ({closedSet.Count} searched in {s.ElapsedMilliseconds}ms)");
					return ret;
				}

				foreach( NodeHandle e in Edges(cur) ) {
					if( !closedSet.Contains(e) ) {
						openSet.Add(e);
						Vector3 ePos = Position(e);
						float score = GScore(cur) + (curPos - ePos).Length();
						if( score < GScore(e) ) {
							cameFrom[e] = cur;
							gScore[e] = score;
							fScore[e] = GScore(e) + Estimate(e, targetNode) + Math.Abs(ePos.Z - curPos.Z);
						}
					}
				}
			}
			return Fail($"[{targetNode}] Searched all reachable nodes ({closedSet.Count} nodes in {s.ElapsedMilliseconds}ms).");
		}

		private static Random random = new Random();
		private static HashSet<long> ignoreModels = new HashSet<long>() {
			(long)ModelHash.Planter,
			1423868860, 416711217, 3231494328, 862871082, 3639322914, 1043035044,
			431612653, 148963242, 213616483, 1021745343, 3180272150, 729253480,
			3837338037, 1431982911, 1258634901, 4090487972, 1726633148, 3510238396,
			3585126029, 2745551480, 326972916, 1392246133, 672606124, 1105091386,
			4068091429, 2967538074, 3069248192, 1572003612
		};
		public static ConcurrentSet<NodeHandle> GetBlockedNodes(bool ents = true, bool vehicles = true, bool peds = true, bool debug = false) {
			var s = new Stopwatch();
			s.Start();
			var blocked = new ConcurrentSet<NodeHandle>();
			int count = 0;
			Matrix4x4 m;
			if( ents ) {
				foreach( EntHandle v in NearbyObjects.Take(40) ) {
					if( !Exists(v)
						|| !IsVisible(v)
						|| IsAttached(v) ) {
						continue; // dont block on items held or carried by peds
					}

					m = Matrix(v);
					ModelHash model = GetModel(v);
					if( ignoreModels.Contains((long)model) ) {
						continue;
					}
					GetModelDimensions(model, out Vector3 backLeft, out Vector3 frontRight);
					Vector3 pos = Position(v);
					int blockedCount = 0;
					foreach( NodeHandle n in NavMesh.GetAllHandlesInBox(m, backLeft, frontRight) ) {
						if( debug && random.NextDouble() < .1f ) {
							Vector3 npos = Position(n);
							DrawLine(pos, npos, Color.Yellow);
							DrawSphere(npos, .05f, Color.Red);
						}
						blocked.Add(n);
						blockedCount += 1;
					}
					if( blockedCount > 10000 ) {
						ignoreModels.Add((long)model);
					}

					if( debug ) {
						UI.DrawTextInWorldWithOffset(pos, 0f, .01f, $"{model} ({blockedCount})");
					}

					count += 1;
				}
			}

			if( vehicles ) {
				foreach( VehicleHandle v in NearbyVehicles.Take(20) ) {
					if( v == CurrentVehicle(Self) || !Exists(v) ) {
						continue;
					}

					m = Matrix(v);
					GetModelDimensions(GetModel(v), out Vector3 backLeft, out Vector3 frontRight);
					backLeft.Z = 0;
					foreach( NodeHandle n in NavMesh.GetAllHandlesInBox(m, backLeft, frontRight) ) {
						blocked.Add(n);
					}
					count += 1;
				}
			}

			if( peds ) {
				foreach( PedHandle p in NearbyHumans.Take(20) ) {
					if( p == Self || !Exists(p) ) {
						continue;
					}

					m = Matrix(p);
					GetModelDimensions(GetModel(p), out Vector3 backLeft, out Vector3 frontRight);
					foreach( NodeHandle n in NavMesh.GetAllHandlesInBox(m, backLeft, frontRight) ) {
						blocked.Add(n);
					}
					count += 1;
				}
			}

			s.Stop();
			UI.DrawText(.3f, .32f, $"Blocked: {blocked.Count} nodes from {count} entities in {s.ElapsedMilliseconds}ms.");
			return blocked;
		}

	}

	public class DebugPath : Goal {

		public bool AvoidObjects = true;
		public bool AvoidPeds = false;
		public bool AvoidCars = true;

		public NodeHandle TargetNode { get; private set; }

		private Future<Path> future;
		public DebugPath(Vector3 v) : this(GetHandle(PutOnGround(v, 1f))) { }
		public DebugPath(NodeHandle targetNode) {
			TargetNode = targetNode;
			if( !NavMesh.IsGrown(TargetNode) ) {
				NavMesh.Grow(TargetNode, 5);
			}

			future = new PathRequest(PlayerNode, TargetNode, 1000, AvoidPeds, AvoidCars, AvoidObjects);
		}
		public override GoalStatus OnTick() {
			DrawSphere(Position(TargetNode), .06f, Color.Yellow);
			DrawLine(HeadPosition(Self), Position(TargetNode), Color.Yellow);
			Pathfinder.GetBlockedNodes(true, true, false, true);
			if( future.IsReady() ) {
				future.GetResult().Draw();
			}
			return GoalStatus.Active;
		}

	}
}
