using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using static Shiv.Globals;
using static Shiv.NavMesh;

namespace Shiv {
	public enum PathHandle : int { Invalid = -1 }

	public class Pathfinder : Script {

		private static float Estimate(NodeHandle a, NodeHandle b) {
			var aPos = Position(a);
			var bPos = Position(b);
			return TryGetClosestVehicleNode(bPos, RoadType.Road, out Vector3 node )
				? (aPos - bPos).Length() + (node - bPos).Length()
				: (aPos - bPos).Length();
		}

		private static IEnumerable<NodeHandle> UnrollPath(Dictionary<NodeHandle, NodeHandle> cameFrom, NodeHandle cur) {
			if( cur != 0 ) {
				var ret = new List<NodeHandle> { cur };
				while( cameFrom.ContainsKey(cur) ) ret.Insert(0, cur = cameFrom[cur]);
				// Debug($"FindPath: result has {ret.Count} steps.");
				return ret;
			}
			return Enumerable.Empty<NodeHandle>();
		}

		public static IEnumerable<Vector3> FindPath(Vector3 start, Vector3 end) {
			NodeHandle startNode = GetHandle(start);
			NodeHandle targetNode = GetHandle(end);
			var closed = GetBlockedNodes();
			// Debug($"FindPath: Start: {startNode} {Round(start,2)} End: {targetNode} {Round(end,2)} with {closed.Count} blocked.");
			var path = FindPath(startNode, targetNode, closed, 25);
			return path == null ? Enumerable.Empty<Vector3>() : path.Select(n => Position(n));
		}
		private static IEnumerable<NodeHandle> FindPath(NodeHandle startNode, NodeHandle targetNode, HashSet<NodeHandle> closedSet, int maxMs = 1000) {
			Stopwatch s = new Stopwatch();
			Vector3 targetNodePos = PutOnGround(Position(targetNode), 1f);

			if( startNode == 0 ) {
				Debug("FindPath Failed: startNode is zero.");
				return null;
			}
			if( targetNode == 0 ) {
				Debug("FindPath Failed: targetNode is zero.");
				return null;
			}
			if( closedSet.Contains(startNode) ) {
				closedSet.Remove(startNode); // we need a path from here because we _are_ here, it cant start blocked
			}
			if( closedSet.Contains(targetNode) ) {
				Debug("FindPath Failed: targetNode is blocked.");
				return null;
			}
			if( ! IsGrown(startNode) ) {
				Grow(startNode, 5);
			}
			if( ! IsGrown(targetNode) ) {
				Debug("FindPath Failed: targetNode has not grown.");
				return null;
			}

			var fScore = new Dictionary<NodeHandle, float> {
				{ startNode, Estimate(startNode, targetNode) }
			};
			float FScore(NodeHandle n) => fScore.ContainsKey(n) ? fScore[n] : float.MaxValue;

			var openSet = new HashSet<NodeHandle>();
			var cameFrom = new Dictionary<NodeHandle, NodeHandle>();

			openSet.Add(startNode);

			var gScore = new Dictionary<NodeHandle, float>() { { startNode, 0 } };
			float GScore(NodeHandle n) => gScore.ContainsKey(n) ? gScore[n] : float.MaxValue;

			s.Start();
			NodeHandle prev = 0;
			while( openSet.Count > 0 ) {
				var cur = openSet.OrderBy(FScore).FirstOrDefault();
				prev = cur;
				openSet.Remove(cur);
				closedSet.Add(cur);
				var curPos = Position(cur);
				if( s.ElapsedMilliseconds > maxMs ) {
					Debug("FindPath Failed: searching for too long.");
					return null;
				}
				if( (curPos - targetNodePos).Length() < .5f ) { 
					return UnrollPath(cameFrom, cur);
				}

				foreach( NodeHandle e in Edges(cur) ) {
					if( closedSet.Contains(e) ) continue;
					if( !openSet.Contains(e) ) openSet.Add(e);

					var ePos = Position(e);
					float score = GScore(cur) + (curPos - ePos).Length();
					if( score < GScore(e) ) {
						cameFrom[e] = cur;
						gScore[e] = score;
						fScore[e] = GScore(e) + Estimate(e, targetNode) + Math.Abs(ePos.Z - curPos.Z);
					}
				}
			}
			Debug("FindPath Failed: searched all reachable nodes.");
			return null;
		}


		public static HashSet<NodeHandle> GetBlockedNodes() {
			var s = new Stopwatch();
			s.Start();
			var blocked = new HashSet<NodeHandle>();
			int count = 0;
			Matrix4x4 m;
			foreach(EntHandle v in NearbyObjects.Take(40) ) {
				if( !Exists(v) ) continue;
				m = Matrix(v);
				var model = GetModel(v);
				if( model == ModelHash.Planter )
					continue; // dont block with these planters (they have odd model size)
				if( model == ModelHash.WoodenDoor
					|| model == ModelHash.WoodenBathroomDoor
					|| model == ModelHash.GlassWoodDoubleDoor ) {
					GetDoorState(model, Position(m), out bool locked, out float heading);
					if( ! locked )
						continue; // dont block with unlocked doors
				}
				GetModelDimensions(model, out Vector3 backLeft, out Vector3 frontRight);
				// if( Math.Max(
					// Math.Abs(backLeft.X - frontRight.X),
					// Math.Max(
						// Math.Abs(backLeft.Y - frontRight.Y),
						// Math.Abs(backLeft.Z - frontRight.Z)
					// )) < .05f )
					// continue;
				backLeft.Z = 0;
				foreach(NodeHandle n in GetAllHandlesInBox(m, backLeft, frontRight)) {
					blocked.Add(n);
				}
				count += 1;
			}
			foreach( VehicleHandle v in NearbyVehicles.Take(20) ) {
				if( v == CurrentVehicle(Self) || !Exists(v) ) continue;
				m = Matrix(v);
				GetModelDimensions(GetModel(v), out Vector3 backLeft, out Vector3 frontRight);
				backLeft.Z = 0;
				foreach(NodeHandle n in GetAllHandlesInBox(m, backLeft, frontRight)) {
					blocked.Add(n);
				}
				count += 1;
			}
			foreach(PedHandle p in NearbyHumans.Take(20) ) {
				if( p == Self || !Exists(p) ) continue;
				m = Matrix(p);
				GetModelDimensions(GetModel(p), out Vector3 backLeft, out Vector3 frontRight);
				foreach( NodeHandle n in GetAllHandlesInBox(m, backLeft, frontRight) ) {
					blocked.Add(n);
				}
				count += 1;
			}
			s.Stop();
			// Debug($"Blocked: {blocked.Count} nodes from {count} entities in {s.ElapsedMilliseconds}ms.");
			return blocked;
		}

	}
}
