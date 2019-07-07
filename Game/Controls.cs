
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Threading;
using static GTA.Native.Function;
using static GTA.Native.Hash;
using static Shiv.Global;
using static Shiv.NavMesh;
using static System.Math;
using Keys = System.Windows.Forms.Keys;

namespace Shiv {
	public static partial class Global {

		public static void SetControlValue(int group, Control control, float value) => Call(_SET_CONTROL_NORMAL, group, control, value);

		public static void PressControl(int group, Control control, uint duration) => ControlsScript.PressControl(group, control, duration);

		private static float MoveActivation(float x, float fps) => (Sigmoid(x) * 4f) - 2f;

		public enum MoveResult {
			Continue,
			Failed,
			Complete
		}
		public static MoveResult MoveToward(Vector3 pos, float stoppingRange=.25f, bool debug=false) {
			if( pos == Vector3.Zero ) { return MoveResult.Complete; }
			ObstructionFlags result = CheckObstruction(PlayerPosition, (pos - PlayerPosition), debug);
			if( ! IsWalkable(result ) ) {
				if( IsClimbable(result) ) {
					SetControlValue(0, Control.Jump, 1.0f); // Attempt to jump over a positive obstruction
				} else if( Speed(Self) < .01f ) {
					return MoveResult.Failed;
				}
			}

			Vector3 delta = pos - PlayerPosition; // Position(PlayerMatrix);
			float dX, dY;
			SetControlValue(1, Control.MoveLeftRight, dX = MoveActivation(
				+2f * Vector3.Dot(delta, Right(CameraMatrix)), // TODO: 2 should be aspect ratio?
				CurrentFPS));
			SetControlValue(1, Control.MoveUpDown, dY = MoveActivation(
				-1f * Vector3.Dot(delta, Forward(CameraMatrix)),
				CurrentFPS));
			DrawLine(HeadPosition(Self), pos, Color.Orange);
			var dist = DistanceToSelf(pos);
			if( debug ) {
				UI.DrawTextInWorld(pos, $"dX:{dX:F2} dY:{dY:F2} dist:{DistanceToSelf(pos):F2}");
			}

			return dist < stoppingRange ? MoveResult.Complete : MoveResult.Continue;
		}

		/*
		private static Vector3 GetFirstStep(IEnumerable<Vector3> path) {
			if( path == null || path.Count() < 1 )
				return Vector3.Zero;
			var a = path.First();
			if( path.Count() > 1 ) {
				var steps = Items(PlayerPosition).Concat(path.Take(5)).ToArray();
				a = Vector3.Lerp(a, 
					Bezier(1f / 5f, steps), 
					Clamp(0.9f / (PlayerPosition - a).Length(), 0f, 1f)
				);
			}
			return a;
		}
		*/
		public static Vector3 FirstStep(IEnumerable<NodeHandle> path) => FirstStep(path.Take(5).Select(NavMesh.Position));
		// public static Vector3 FirstStep(IEnumerable<Vector3> path) => Bezier(.25f, Items(PlayerPosition).Concat(path.Take(5)).ToArray());
		public static Vector3 FirstStep(IEnumerable<Vector3> path) {
			var first = path.First();
			var second = path.Skip(1).First();
			return Vector3.Lerp(first, second, Clamp(2f * (float)Pow(1f - DistanceToSelf(first), 2), 0f, 1f));
		}
		public static MoveResult FollowPath(IEnumerable<NodeHandle> path, float steppingRange=0.2f) => FollowPath(path.Take(5).Select(NavMesh.Position));
		public static MoveResult FollowPath(IEnumerable<Vector3> path, float steppingRange=0.2f) => path == null || path.Count() < 2 ? MoveResult.Complete : MoveToward(FirstStep(path), steppingRange);

		private static float LookActivation(float x, float factor) => (float)(x * CurrentFPS * Sqrt(Abs(CurrentFPS * x * factor)));
		public static bool LookToward(Vector3 pos, bool debug=false) {
			pos = pos - Velocity(Self) / CurrentFPS;
			DrawSphere(pos, .02f, Color.Yellow);
			Vector3 forward = Forward(CameraMatrix);
			Vector3 cam = Position(CameraMatrix);
			Vector3 delta = Vector3.Normalize(pos - cam) - forward;
			Vector3 end = cam + forward;
			float right = Vector3.Dot(delta, Right(CameraMatrix));
			float up = Vector3.Dot(delta, UpVector(CameraMatrix));
			// Vector3 delta = Vector3.Normalize(pos - Position(CameraMatrix)) - Forward(CameraMatrix);
			DrawLine(end, end + delta, Color.White);
			// probably a way to do all this with one quaternion multiply or something
			float dX = Clamp(LookActivation(right,.2f), -1f, 1f);
			float dY = Clamp(LookActivation(up,.2f), -1f, 1f);
			// Shiv.Log($"{Round(delta.X,2)} {Round(delta.Y,2)} = {Round(dX,2)} {Round(-dY,2)}");
			SetControlValue(1, Control.LookLeftRight, dX);
			SetControlValue(1, Control.LookUpDown, -dY);
			// UI.DrawText(.5f, .5f, $"Look Controls: {Round(delta.X,2)} {Round(delta.Z,2)} -> {Round(dX,1)} {Round(dY,1)} (len {delta.LengthSquared()})");
			if( debug ) { UI.DrawText(.5f, .5f, $"Aim delta: {delta.LengthSquared():F5}"); }
			return delta.LengthSquared() < .0001f;
		}

		public static Vector3 LookTarget = Vector3.Zero;
		public static Vector3 AimTarget = Vector3.Zero;
		public static PedHandle AimAtHead = PedHandle.Invalid;
		public static PedHandle KillTarget = PedHandle.Invalid;

		private static Vector3 walkTarget = Vector3.Zero;
		internal static Future<Path> WalkPath { get; private set; } = new Future<Path>();
		public static Vector3 WalkTarget {
			get => walkTarget;
			set {
				walkTarget = value;
				NodeHandle startNode = PlayerNode;
				NodeHandle targetNode = Handle(PutOnGround(value, 1f));
				if( targetNode == NodeHandle.Invalid ) {
					walkTarget = Vector3.Zero;
					return;
				}
				WalkPath = new PathRequest(PlayerNode, targetNode, 50, false, true, true, 1);
			}
		}

		[Flags]
		public enum ObstructionFlags {
			None,
			CannotClimb = 1,
			MaxClimb = 2,
			CanClimb = 4,
			Overhead = 8,
			Head = 16,
			Shoulder = 32,
			Chest = 64,
			Stomach = 128,
			Waist = 256,
			Knee = 512,
			Ankle =  1024
		}
		public static bool IsClimbable(ObstructionFlags flags) => (flags & ObstructionFlags.CannotClimb) == 0;
		public static bool IsWalkable(ObstructionFlags flags) => 0 == (flags &
			(ObstructionFlags.Waist | ObstructionFlags.Stomach | ObstructionFlags.Chest | ObstructionFlags.Shoulder | ObstructionFlags.Head));

		public static ObstructionFlags CheckObstruction(Vector3 start, Vector3 heading, bool debug = false) {

			ObstructionFlags ret = ObstructionFlags.None;
			if( Call<bool>(IS_PED_RAGDOLL, Self) ) {
				return ret;
			}

			heading = new Vector3(heading.X, heading.Y, 0f);

			IntersectOptions opts = IntersectOptions.Map | IntersectOptions.Objects | IntersectOptions.Vehicles;
			float capsuleSize = .12f;
			float stepSize = .25f;
			heading = Vector3.Normalize(heading) * .5f;
			var head = HeadPosition(Self) + (Up * 5 * stepSize);
			if( debug ) {
				// DrawLine(head, head + heading, Color.Orange);
			}
			for(int i = 1; i <= (int)ObstructionFlags.Knee; i*=2 ) { // do probes from top to bottom
				var headRay = Raycast(head, head + heading, capsuleSize, opts, Self);
				if( headRay.DidHit ) {
					if( debug ) {
						DrawSphere(headRay.HitPosition, .01f, Color.Red);
					}
					ret |= (ObstructionFlags)i;
				}
				head.Z -= stepSize;
			}
			if( debug ) {
				string str = IsWalkable(ret) ? "Walkable" : IsClimbable(ret) ? "Climbable" : "Blocked";
				UI.DrawTextInWorld(HeadPosition(Self) + heading, $"{str}");
			}
			return ret;
		}
			/*
			IntersectOptions opts = IntersectOptions.Map | IntersectOptions.MissionEntities | IntersectOptions.Objects
				| IntersectOptions.Unk1 | IntersectOptions.Unk2 | IntersectOptions.Unk3 | IntersectOptions.Unk4;
			forward = Vector3.Normalize(forward) * .4f;
			pos = pos + forward + (Up * 1.2f); // pick a spot in the air
			if( debug ) {
				DrawSphere(pos, .02f, Color.Orange);
			}
			var end = new Vector3(pos.X, pos.Y, pos.Z - 1.65f); // try to drop it down
			if( debug ) {
				DrawLine(pos, end, Color.Orange);
			}
			RaycastResult result = Raycast(pos, end, .25f, opts, Self);
			if( debug ) {
				if( result.DidHit ) {
					var len = (result.HitPosition - end).Length();
					DrawSphere(result.HitPosition, .02f, Color.Red);
					UI.DrawTextInWorld(result.HitPosition, $"{len:F2}m");
				}
			}
			// result is larger if more obstructed
			return result.DidHit ? (result.HitPosition - end).Length() : 0f;
		}
		*/

	}

	public class ControlsScript : Script {
		private struct ControlEvent {
			public int group;
			public Global.Control control;
			public uint expires;
		}
		private static List<ControlEvent> active = new List<ControlEvent>();
		public static void PressControl(int g, Global.Control c, uint dur) => active.Add(new ControlEvent() { group = g, control = c, expires = GameTime + dur });
		public override void OnTick() {
			active.RemoveAll(e => e.expires < GameTime);
			foreach( ControlEvent e in active ) {
				SetControlValue(e.group, e.control, 1.0f);
			}
			if( AimTarget != Vector3.Zero ) {
				UI.DrawText($"AimTarget: {AimTarget}");
				LookToward(AimTarget);
				ForcedAim(CurrentPlayer, true);
			} else if( Exists(AimAtHead) && IsAlive(AimAtHead) ) {
				UI.DrawText($"AimAtHead: {AimAtHead}");
				LookToward(HeadPosition(AimAtHead) + Velocity(AimAtHead) / CurrentFPS);
				ForcedAim(CurrentPlayer, true);
			} else if( Exists(KillTarget) && IsAlive(KillTarget) ) {
				ForcedAim(CurrentPlayer, true);
				if( LookToward(HeadPosition(KillTarget) + Velocity(KillTarget) / CurrentFPS)
					|| IsAimingAtEntity(KillTarget) ) {
					SetControlValue(1, Control.Attack, 1f);
				}
			} else if( LookTarget != Vector3.Zero ) {
				LookToward(LookTarget);
			} else {
				KillTarget = PedHandle.Invalid;
				AimAtHead = PedHandle.Invalid;
				ForcedAim(CurrentPlayer, false);
			}
			if( WalkTarget != Vector3.Zero ) {
				if( WalkPath.IsFailed() ) {
					Log($"WalkPath Failed: {WalkPath.GetError()}");
					WalkTarget = Vector3.Zero;
				} else if( DistanceToSelf(WalkTarget) < 2f ) {
					Log($"WalkPath Complete.");
					WalkTarget = Vector3.Zero;
				} else if( WalkPath.IsReady() ) {
					if( FollowPath(WalkPath.GetResult()) == MoveResult.Failed ) {
						MoveToward(WalkTarget);
					}
				}
			}
		}
	}

	public struct KeyPress {
		public int group;
		public Control key;
		public float value;
		public uint ms;
	}
	public class Wait : Goal {
		public readonly Func<bool> predicate;
		public readonly uint timeout;
		private uint started = 0;
		public Wait(uint timeout, Func<bool> predicate) {
			this.timeout = timeout;
			this.predicate = predicate;
		}
		public Wait(uint timeout) {
			this.timeout = timeout;
			this.predicate = () => false;
		}
		public Wait(Func<bool> predicate) {
			this.timeout = uint.MaxValue;
			this.predicate = predicate;
		}
		public override GoalStatus OnTick() {
			if( started == 0 ) {
				started = GameTime;
			} else if( GameTime - started > timeout ) {
				return Status = GoalStatus.Complete;
			}
			return Status = predicate() ? GoalStatus.Complete : Status;
		}
	}
	public class KeyGoal : Goal {
		private uint started = 0;
		private readonly int group;
		private readonly Control control;
		private readonly float value;
		private readonly uint duration;
		public KeyGoal(int group, Control control, float value, uint duration) {
			this.group = group;
			this.control = control;
			this.value = value;
			this.duration = duration;
		}
		public override GoalStatus OnTick() {
			if( started == 0 ) {
				started = GameTime;
			} else if( GameTime - started > duration ) {
				return Status = GoalStatus.Complete;
			}
			SetControlValue(group, control, value);
			return Status;
		}
	}
	public class KeySequence : Goal {
		private LinkedList<KeyPress> Sequence = new LinkedList<KeyPress>();
		public uint Spacing = 0;
		public KeySequence Add(int group, Control key, uint duration) {
			Sequence.AddLast(new KeyPress() { group = group, key = key, value = 1f, ms = duration });
			return this;
		}
		public KeySequence Add(int group, Control key, float value, uint duration) {
			Sequence.AddLast(new KeyPress() { group = group, key = key, value = value, ms = duration });
			return this;
		}
		public override GoalStatus OnTick() {
			if( Sequence.Count < 1 ) {
				return Status = GoalStatus.Complete;
			}

			KeyPress v = Sequence.Pop();
			Goals.Immediate(new KeyGoal(v.group, v.key, v.value, v.ms));
			if( Sequence.Count > 0 ) {
				Goals.Next(new Wait(Spacing));
			}

			return Status;
		}
	}

	public static partial class Global {
		public static class Controls {
			private struct Event {
				public Keys key;
				public bool downBefore;
				public bool upNow;
			}
			// this queue is filled up by the main Shiv class
			private static ConcurrentQueue<Event> keyEvents = new ConcurrentQueue<Event>();
			private static Dictionary<Keys, Action> keyBindings = new Dictionary<Keys, Action>();
			public static void Bind(Keys key, Action action) => keyBindings[key] = keyBindings.TryGetValue(key, out Action curr) ? (() => { curr(); action(); }) : action;
			public static void Enqueue(Keys key, bool downBefore, bool upNow) => keyEvents.Enqueue(new Event() { key = key, downBefore = downBefore, upNow = upNow });
			public static void DisableAllThisFrame(Type except = null) { Disabled = true; DisabledExcept = except; }
			public static Type DisabledExcept = null;
			public static bool Disabled = false;
			public static void OnTick() {
				while( keyEvents.TryDequeue(out Event evt) ) {
					IEnumerable<Script> items;
					if( DisabledExcept == null ) {
						// Log($"Only allowing {DisabledExcept} to consume keys this frame.");
						if( Disabled ) {
							continue;
						}
						items = Script.Order;
					} else {
						items = Script.Order.Where(s => s.GetType() == DisabledExcept);
					}
					// lets scripts consume the key
					Script consumer = items.FirstOrDefault(s => s.OnKey(evt.key, evt.downBefore, evt.upNow));
					if( consumer == null ) {
						// if no script did consume it, and it's newly pressed, trigger the Console.Bind() binding
						if( (!evt.downBefore) && keyBindings.TryGetValue(evt.key, out Action action) ) {
							try {
								action();
							} catch( Exception err ) {
								Log($"OnKey({evt.key}) exception from key-binding: {err.Message}");
								Log(err.StackTrace);
								keyBindings.Remove(evt.key);
							}
						}
					}
				}
				if( Disabled ) {
					Call(DISABLE_ALL_CONTROL_ACTIONS, 1);
				}
				Disabled = false;
				DisabledExcept = null;
			}
		}
	}


}