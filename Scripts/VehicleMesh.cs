using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using static Shiv.Global;
using static Shiv.NavMesh;
using static GTA.Native.Function;
using static GTA.Native.Hash;

namespace Shiv {
	public static partial class Global {


	}
/*

	public class VehicleMesh : Script {

		// TODO: figure out how to link vehicle nodes together properly
		// worst case is maybe brute force, for each node check the nearest 10,
		// do raytracing to guess if you can pass
		// the 'flood fill' part of that would be really fast, get a node, then get the next nearest etc
		public static IEnumerable<VehicleNode> Edges(VehicleNode a) {
			if( AllEdges.TryGetValue(a, out HashSet<VehicleNode> edges) ) {
				foreach( var n in edges ) {
					yield return n;
				}
			}
		}

		public static void AddEdge(VehicleNode a, VehicleNode b) {
			if( !AllEdges.ContainsKey(a) ) AllEdges[a] = new HashSet<VehicleNode>();
			AllEdges[a].Add(b);
		}

		public static VehicleNode GetHandle(Vector3 pos) => TryGetClosestVehicleNode(pos, RoadType.Road, out pos) ? (VehicleNode)pos.GetHashCode() : VehicleNode.Invalid;

		public static Dictionary<VehicleNode, HashSet<VehicleNode>> AllEdges = new Dictionary<VehicleNode, HashSet<VehicleNode>>();
		public static Dictionary<VehicleNode, Vector3> Positions = new Dictionary<VehicleNode, Vector3>();

		public void Grow(Vector3 node, VehicleNode a, float heading, int depth, HashSet<VehicleNode> stack) {
			if( depth < 1 || stack.Contains(a) || a == VehicleNode.Invalid || node == Vector3.Zero ) return;
			if( TryGetVehicleNodeProperties(node, out int density, out int flags) ) {
				UI.DrawTextInWorld(node, $"{heading:F2} {density} {flags}");
				if( (flags & 0x2) != 0x2 )
					return;
			}
			stack.Add(a);
			for( uint i = 1; i < 8; i++ ) {
				if( TryGetClosestVehicleNode(node, RoadType.Road, i, out Vector3 _node, out float _heading, out int kind) ) {
					var b = (VehicleNode)_node.GetHashCode();
					if( b == a ) continue;
					DrawSphere(_node, .2f, Color.Purple);
					DrawLine(node + (Up / 2), _node + (Up / 2), Color.Yellow);
					Positions[b] = _node;
					if( Math.Abs(heading - _heading) < 25 ) {
						var result = Raycast(node + Up, _node + Up, .7f, IntersectOptions.Map | IntersectOptions.Objects, Self);
						if( result.DidHit ) {
							DrawSphere(result.HitPosition, .2f, Color.Red);
						} else {
							DrawLine(node + Up, _node + Up, Color.Orange);
							AddEdge(a, b);
							Grow(_node, b, _heading, depth - 1, stack);
						}
					}
				}
			}
			stack.Remove(a);
		}

		public override void OnTick() {
			var node = GetClosestVehicleNodes(PlayerPosition, RoadType.Road)
				.Where(n => IsFacing(Self, n.Position))
				.FirstOrDefault();
			// DrawLine(HeadPosition(Self), node.Position, Color.Purple);
			// var a = (VehicleNode)node.GetHashCode();
			// Grow(node.Position, a, node.Heading, 3, new HashSet<VehicleNode>() { });
		}

	}
	*/
}
