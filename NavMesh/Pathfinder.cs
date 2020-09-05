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
using static System.Math;
using static Shiv.Global;
using static Shiv.NavMesh;
using static GTA.Native.Hash;
using static GTA.Native.Function;
using System.Runtime.InteropServices;

namespace Shiv {

	public struct ModelBox {
		public ModelHash Model;
		public EntHandle Entity;
		public Matrix4x4 M;
		public Vector3 Front;
		public Vector3 Back;
	}
	[ComVisible(false)]
	public class PathRequest : Future<Path> {
		public NodeHandle Start;
		public NodeHandle Target;
		private NodeHandle Best;
		public uint Timeout = 100;
		public bool AvoidObjects = true;
		public bool AvoidPeds = false;
		public bool AvoidCars = true;
		public Stopwatch Running = new Stopwatch();
		public Stopwatch Stopped = new Stopwatch();
		public ConcurrentSet<NodeHandle> Blocked;
		public PathRequest(NodeHandle start, NodeHandle target, uint timeout, bool avoidPeds, bool avoidCars, bool avoidObjects, uint clearance, bool debug=false):base() {
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
			Timeout = timeout;
			AvoidPeds = avoidPeds;
			AvoidCars = avoidCars;
			AvoidObjects = avoidObjects;
			Blocked = Pathfinder.GetBlockedNodes(AvoidObjects, AvoidCars, AvoidPeds, false);
			Log($"[{Target}] Pre-blocked {Blocked.Count} nodes.");
			Running.Start();
			ThreadPool.QueueUserWorkItem((object arg) => {
				if( Blocked.Contains(Target) ) {
					Log($"[{Target}] target node is blocked, groping for nearest unblocked...");
					Vector3 originalPosition = Position(Target);
					Target = Flood(Target, 10000, 100, cancel.Token, PossibleEdges)
						.Where(HasEdges)
						.Without(Blocked.Contains)
						.Min(n => (originalPosition - Position(n)).LengthSquared());
				} else if( !HasEdges(Target) ) {
					Log($"[{Target}] target node has no edges, groping for nearest graph...");
					Vector3 originalPosition = Position(Target);
					Target = Flood(Target, 10000, 100, cancel.Token, PossibleEdges)
						.Where(HasEdges)
						.Without(Blocked.Contains)
						.Min(n => (originalPosition - Position(n)).LengthSquared());
					if( Target == NodeHandle.Invalid ) {
						Reject(new Exception("Target node is not mappable."));
						return;
					} else {
						Log($"[{Target}] Using replacement target.");
					}
				}
				try {
					PathStatus.Guard.Wait(1000, cancel.Token);
				} catch( Exception err ) {
					Reject(err);
					return;
				}
				PathStatus.Queue.Enqueue(this);
				try { Resolve(Pathfinder.FindPath(Start, Target, Blocked, Timeout, cancel.Token, clearance, (best) => Best = best)); }
				catch( Exception err ) { Reject(err); }
				finally {
					PathStatus.Guard.Release();
					Running.Stop();
					Stopped.Start();
				}
			});
		}
		private float bestBest = 0f;
		public override string ToString() {
			var elapsed = Running.ElapsedMilliseconds;
			var total = (Position(Target) - Position(Start)).Length();
			var dist = (Position(Target) - Position(Best)).Length();
			var pct = (1f - (dist / total));
			if( pct > bestBest ) {
				bestBest = pct;
			}
			return $"{Blocked.Count} {100f*bestBest:F0}% in {elapsed}ms "
				+ (IsReady() ? GetResult().ToString() : "")
				+ (IsFailed() ? GetError().ToString() : "")
				+ (IsCanceled() ? " cancel" : "");
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
		static readonly float top = .58f;
		static readonly float left = 0f;
		static readonly float width = .15f;
		public static void Draw() {
			int numLines = Min(7, Queue.Count + 1);
			UI.DrawRect(left, top, width, padding + (lineHeight * (numLines)) + padding, Color.SlateGray);
			UI.DrawText(left, top, $"Pathfinder: {Queue.Count} Active");
			UI.DrawText(left, top - (padding + lineHeight), Pathfinder.Timers());
			int lineNum = 0;
			while( Queue.TryPeek(out PathRequest req)
				&& req.IsDone()
				&& req.Stopped.ElapsedMilliseconds > 20000 ) {
				// let path requests sit around for a few seconds so they can be seen
				Queue.TryDequeue(out PathRequest done);
			}
			foreach( PathRequest req in Queue.Skip(Max(0, Queue.Count - numLines)) ) {
				UI.DrawText(padding + left, top + (++lineNum * lineHeight), req.ToString());
				if( lineNum >= numLines - 1 ) {
					break;
				}
			}
		}
	}
	public class SmoothPath {

		// keep a linked list of the smoothed output realized so far
		private LinkedList<Vector3> steps;

		// slide a cursor up and down the list, as we move in the world
		private LinkedListNode<Vector3> cursor;

		// keep an open IEnumerator so we can proceed with Culled() as needed
		private IEnumerator<Vector3> source; // not an Enumerable, because we want precise control to only iterate once

		// as we cull, omit points where a capsule this big can pass over it without hitting anything
		public float CapsuleSize = .4f;

		// as we move cursor along the path, in UpdateCursor, how close to each node advances the cursor
		public float SteppingRange = .5f;

		public SmoothPath(IEnumerable<NodeHandle> path) : this(path.Select(Position)) { }
		public SmoothPath(IEnumerable<Vector3> path) {
			steps = new LinkedList<Vector3>();
			source = Culled(path).GetEnumerator();
			// will do an initial set of raycasts immediately, up to the first corner/obstruction
			Realize(1);
			cursor = steps.First;
		}
		private bool Realize(int i) {
			while( steps.Count <= i ) {
				try {
					if( source.MoveNext() ) {
						steps.AddLast(source.Current);
					} else {
						return false;
					}
				} catch( InvalidOperationException ) {
					return false;
				}
			}
			return true;
		}

		private Func<EntHandle, float> DistanceTo(Vector3 pos) => (e) => (Position(e) - pos).Length();

		// Culled does the real work of walking forward with a pair of pointers
		// yielding only the nodes where a capsule clipped the direct path
		private IEnumerable<Vector3> Culled(IEnumerable<Vector3> path) {
			Vector3[] orig = path.ToArray();
			if( orig.Length < 2 ) {
				foreach( Vector3 step in orig ) {
					yield return step;
				}
			} else {
				var seenEnts = new HashSet<EntHandle>();
				int i = 0;
				for( int j = 2; j < orig.Length - 1; j++ ) {
					if( Raycast(orig[i], orig[j], CapsuleSize, IntersectOptions.Everything ^ IntersectOptions.Vegetation, Self).DidHit ) {
						Log($"Culled() up to {j}");
						yield return orig[j - 1];
						i = j;
					} else {
						// as we move j forward, "light up" the closest ents it passes
						// once that ent has been activated (added to the seenEnts set),
						// we will keep checking it for collision as j advances
						foreach( EntHandle ent in NearbyObjects().OrderBy(DistanceTo(orig[j])).Take(4).Concat(seenEnts) ) {
							if( GetEntityType(ent) != EntityType.Invalid ) {
								if( ! seenEnts.Contains(ent) ) {
									seenEnts.Add(ent);
								}
								ModelHash model = GetModel(ent);
								Matrix4x4 m = Matrix(ent);
								GetModelDimensions(model, out Vector3 backLeft, out Vector3 frontRight);
								if( IntersectModel(orig[i], orig[j], m, backLeft, frontRight) ) {
									Log($"Culled() up to {j} (using IntersectModel)");
									yield return orig[j - 1];
									i = j;
									seenEnts.Clear();
									break;
								}
							}
						}
					}
				}
				// always keep the last two steps untouched, as an anchor
				yield return orig[orig.Length - 2];
				yield return orig[orig.Length - 1];
			}
		}
		public void Draw() => steps.Each(step => DrawSphere(step, .03f, Color.Orange));
		private void UpdateCursor(Vector3 actorPosition) {

			// read ahead if our cursor is near the end
			if( !readComplete && (cursor == steps.Last) ) {
				readComplete = !Realize(steps.Count);
			}

			if( IsComplete() ) {
				cursor = null;
				return;
			}

			float dot = 0f;
			if( cursor.Next != null ) {
				Vector3 forward = actorPosition - cursor.Value;
				if( forward.Length() < SteppingRange
					&& Vector3.Dot(forward, (cursor.Next.Value - cursor.Value)) > 0 ) {
					cursor = cursor.Next;
					Log($"Advancing cursor to {Round(cursor.Value,1)} {forward.Length():F2}");
				}
			}
			if( cursor.Previous != null ) {
				dot = 0f;
				Vector3 back = actorPosition - cursor.Previous.Value;
				if( (dot = Vector3.Dot(back, (cursor.Value - cursor.Previous.Value))) < 0 ) {
					cursor = cursor.Previous;
					Log($"Retreating cursor back to {Round(cursor.Value,1)}, dot: {dot:F2}");
				}
			}
		}
		private bool readComplete = false;
		public bool IsComplete() => readComplete
			&& (cursor == null
				|| (cursor == steps.Last && (cursor.Value - PlayerPosition).Length() < SteppingRange)
			);
		public Vector3 NextStep(Vector3 actorPosition) {
			UpdateCursor(actorPosition);
			return cursor == null ? Vector3.Zero : cursor.Value;
		}

	}
	public class Path : IEnumerable<NodeHandle> {
		// Path is a type alias for a lazy list of NodeHandles
		// iter comes from UnrollPath inside FindPath
		private IEnumerable<NodeHandle> iter;
		public Path(IEnumerable<NodeHandle> path) => iter = path;
		public void Pop() => iter = iter.Skip(1);
		public IEnumerator<NodeHandle> GetEnumerator() => iter.GetEnumerator();
		IEnumerator IEnumerable.GetEnumerator() => iter.GetEnumerator();
		public void Draw() => this.Take(20).Select(Position)
				.Each(DrawSphere(.03f, Color.Yellow));
		public override string ToString() => $"({this.Count()} steps)";

		public void FastForward(Vector3 pos) {
			while( iter.Count() > 1 && (pos - Position(First(iter))).Length() > (pos - Position(First(iter.Skip(1)))).Length() ) {
				iter = iter.Skip(1);
			}
		}

	}

	public static partial class Global {

		public static Vector3 Position(Path path) =>
			path == null ? Vector3.Zero : NavMesh.Position(path.FirstOrDefault());

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
			return $"C:{100 * closedSetTimer.ElapsedTicks / ticks}% R:{100 * regionTimer.ElapsedTicks/ticks}% g:{100 * gScoreTimer.ElapsedTicks / ticks}% f:{100 * fScoreTimer.ElapsedTicks / ticks}%";
		}

		private static Path Fail(string msg) {
			fScoreTimer.Stop();
			Log(msg);
			UI.DrawText(.3f, .3f, msg);
			return new Path(Enumerable.Empty<NodeHandle>());
		}
		internal static Path FindPath(NodeHandle startNode, NodeHandle targetNode, ConcurrentSet<NodeHandle> closedSet, uint maxMs, CancellationToken cancelToken, uint clearance, Action<NodeHandle> progress, bool debug=false) {
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
				fScoreTimer.Start();
				var fScore = new Heap<NodeHandle>();
				fScore.Add(startNode, Estimate(startNode, targetNode));
				fScoreTimer.Stop();

				var cameFrom = new Dictionary<NodeHandle, NodeHandle>();

				var gScore = new Dictionary<NodeHandle, float>();
				gScore.TryAdd(startNode, 0);
				float GScore(NodeHandle n) => gScore.ContainsKey(n) ? gScore[n] : float.MaxValue;

				s.Start();
				fScoreTimer.Start();
				while( fScore.TryPop(out NodeHandle best) ) {
					fScoreTimer.Stop();
					if( cancelToken.IsCancellationRequested ) {
						return Fail($"[{targetNode}] Cancelled.");
					}
					if( s.ElapsedMilliseconds > maxMs ) {
						return Fail($"[{targetNode}] Searching for too long, ({closedSet.Count} nodes in {s.ElapsedMilliseconds}ms.");
					}

					// close this node we are just about to visit
					closedSetTimer.Start();
					closedSet.Add(best);
					closedSetTimer.Stop();

					// update the progress callback
					progress(best);

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

						if( !closed && Clearance(e) >= clearance ) {

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
									+ ((15 - Clearance(e)) * .3f) // plus a penalty for low clearance
								);
								fScoreTimer.Stop();
							}
						}
					}
					fScoreTimer.Start();
				}
				return Fail($"[{targetNode}] Searched all reachable nodes ({closedSet.Count} nodes in {s.ElapsedMilliseconds}ms).");
			} finally {
				findPathTimer.Stop();
				fScoreTimer.Stop();
			}
		}

		private static readonly Random random = new Random();
		private static HashSet<long> ignoreModels = new HashSet<long>() {
			(long)ModelHash.Planter, // (long)ModelHash.WoodenDoor,
			1423868860, 416711217, 3231494328, 862871082, 3639322914, 1043035044,
			431612653, 148963242, 213616483, 1021745343, 3180272150, 729253480,
			3837338037, 1431982911, 1258634901, 4090487972, 1726633148, 3510238396,
			3585126029, 2745551480, 326972916, 1392246133, 672606124, 1105091386,
			4068091429, 2967538074, 3069248192, 1572003612, 2491942629
		};
		internal static HashSet<ModelHash> checkDoorModels = new HashSet<ModelHash>() {
			ModelHash.WoodenDoor, ModelHash.WoodenFireDoor, ModelHash.WoodenBathroomDoor
		};
		public static IEnumerable<ModelBox> GetBlockingEnts(int limit=30, bool debug=false) {
			foreach( EntHandle ent in NearbyObjects().Take(limit) ) {
				if( IsAttached(ent) ) {
					continue; // dont block on items held or carried by peds
				}
				Matrix4x4 m = Matrix(ent);
				Vector3 pos = Position(m);
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
				float volume = GetVolume(frontRight, backLeft);
				if( debug ) { DrawBox(m, backLeft, frontRight); }
				if( debug ) { UI.DrawTextInWorldWithOffset(pos, 0f, .02f, $"{model} ({volume:F2})"); }
				if( volume > 170 ) {
					ignoreModels.Add((long)model);
				} else {
					yield return new ModelBox() { Model = model, Entity = ent, M = m, Front = frontRight, Back = backLeft };
				}
			}
		}
		public static IEnumerable<ModelBox> GetBlockingVehicles(int limit=20, bool debug=false) {
			VehicleHandle ignore = VehicleHandle.Invalid;
			if( IsOnVehicle(Self) ) {
				RaycastResult result = Raycast(PlayerPosition, PlayerPosition - (2 * Up), .3f, IntersectOptions.Vehicles, Self);
				if( result.DidHit ) {
					ignore = (VehicleHandle)result.Entity;
				}
			}
			foreach( VehicleHandle v in NearbyVehicles().Take(limit) ) {
				if( v == PlayerVehicle || v == ignore ) {
					continue;
				}
				if( ! IsSeatFree(v, VehicleSeat.Driver) ) {
					// cars with drivers need a different solution (avoidance not blocking)
					continue;
				}
				VehicleHash model = GetModel(v);
				GetModelDimensions(model, out Vector3 backLeft, out Vector3 frontRight);
				Matrix4x4 m = Matrix(v);
				if( debug ) { DrawBox(m, backLeft, frontRight); }
				yield return new ModelBox() { Model = (ModelHash)model, Entity = (EntHandle)v, M = m, Front = frontRight, Back = backLeft };
			}

		}
		public static IEnumerable<ModelBox> GetBlockingPeds(int limit=20, bool debug=false) {
			foreach( PedHandle p in NearbyHumans().Take(limit) ) {
				if( p == Self || !IsAlive(p) ) {
					continue;
				}
				PedHash model = GetModel(p);
				GetModelDimensions(model, out Vector3 backLeft, out Vector3 frontRight);
				yield return new ModelBox() { Model = (ModelHash)model, Entity = (EntHandle)p, M = Matrix(p), Front = frontRight, Back = backLeft };
			}
		}
		public static ConcurrentSet<NodeHandle> GetBlockedNodes(bool ents = true, bool vehicles = true, bool peds = true, bool debug = false) {
			var s = new Stopwatch();
			s.Start();
			var boxes = new List<ModelBox>();
			if( ents ) {
				boxes.AddRange(GetBlockingEnts(100, debug));
			}

			if( vehicles ) {
				boxes.AddRange(GetBlockingVehicles(30, debug));
			}

			if( peds ) {
				boxes.AddRange(GetBlockingPeds(20, debug));
			}
			var blocked = new ConcurrentSet<NodeHandle>();
			Parallel.For(0, boxes.Count, i => GetAllHandlesInBox(boxes[i], blocked));

			s.Stop();
			if( debug ) {
				UI.DrawText(.3f, .32f, $"Blocked: {blocked.Count} nodes in {s.ElapsedMilliseconds}ms.");
			}
			return blocked;
		}

	}
}
