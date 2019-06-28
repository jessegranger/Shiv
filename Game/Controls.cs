
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
		public static MoveResult MoveToward(EntHandle ent) => MoveToward(Position(ent));
		public static MoveResult MoveToward(PedHandle ent) => MoveToward(Position(ent));
		public static MoveResult MoveToward(Vector3 pos) {
			if( pos == Vector3.Zero ) { return MoveResult.Complete; }
			var obs = CheckObstruction(PlayerPosition, (pos - PlayerPosition));
			if( obs < 0 ) {
				// TODO: try to shuffle around a side
				// TODO: maybe unlink some Edges() so we dont path here again
				Text.Add(pos, "Obstruction", 3000);
				return MoveResult.Failed; // currently impossible
			} else if( obs > .25f && obs < 2.4f && Speed(Self) < .01f ) {
				SetControlValue(0, Control.Jump, 1.0f); // Attempt to jump over a positive obstruction
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
			// UI.DrawTextInWorld(pos, $"dX:{dX:F2} dY:{dY:F2} dist:{DistanceToSelf(pos):F2}");
			return dist < .5f ? MoveResult.Complete : MoveResult.Continue;
		}
		public static MoveResult FollowPath(IEnumerable<Vector3> path) => path == null ? MoveResult.Complete : MoveToward(Bezier(.5f, path.Take(4).ToArray()));

		private static float LookActivation(float x, float factor) => (float)(x * CurrentFPS * Sqrt(Abs(CurrentFPS * x * factor)));
		public static bool LookToward(Vector3 pos) {
			pos = pos - Velocity(Self) / CurrentFPS;
			DrawSphere(pos, .05f, Color.Yellow);
			var forward = Forward(CameraMatrix);
			var cam = Position(CameraMatrix);
			var head = HeadPosition(Self);
			var desired = Vector3.Normalize(pos - cam);
			var delta = desired - forward;
			var end = cam + forward;
			var right = Vector3.Dot(delta, Right(CameraMatrix));
			var up = Vector3.Dot(delta, UpVector(CameraMatrix));
			// Vector3 delta = Vector3.Normalize(pos - Position(CameraMatrix)) - Forward(CameraMatrix);
			DrawLine(end, end + delta, Color.White);
			// probably a way to do all this with one quaternion multiply or something
			float dX = Clamp(LookActivation(right,.2f), -1f, 1f);
			float dY = Clamp(LookActivation(up,.2f), -1f, 1f);
			// Shiv.Log($"{Round(delta.X,2)} {Round(delta.Y,2)} = {Round(dX,2)} {Round(-dY,2)}");
			SetControlValue(1, Control.LookLeftRight, dX);
			SetControlValue(1, Control.LookUpDown, -dY);
			// UI.DrawText(.5f, .5f, $"Look Controls: {Round(delta.X,2)} {Round(delta.Z,2)} -> {Round(dX,1)} {Round(dY,1)} (len {delta.LengthSquared()})");
			UI.DrawText(.5f, .5f, $"Aim delta: {delta.LengthSquared():F5}");
			return delta.LengthSquared() < .0001f;
		}

		public static Vector3 AimTarget = Vector3.Zero;
		public static PedHandle AimAtHead = PedHandle.Invalid;
		public static PedHandle KillTarget = PedHandle.Invalid;

		private static Vector3 walkTarget = Vector3.Zero;
		internal static Future<Path> WalkPath = new Future<Path>();
		public static Vector3 WalkTarget {
			get => walkTarget;
			set {
				walkTarget = value;
				NodeHandle startNode = PlayerNode;
				NodeHandle targetNode = GetHandle(PutOnGround(value, 1f));
				if( targetNode == NodeHandle.Invalid ) {
					walkTarget = Vector3.Zero;
					return;
				}
				WalkPath = new PathRequest(PlayerNode, targetNode, 100, false, true, true);
			}
		}

		public static float CheckObstruction(Vector3 pos, Vector3 forward) {
			IntersectOptions opts = IntersectOptions.Map | IntersectOptions.MissionEntities | IntersectOptions.Objects
				| IntersectOptions.Unk1 | IntersectOptions.Unk2 | IntersectOptions.Unk3 | IntersectOptions.Unk4;
			forward = Vector3.Normalize(forward) * .4f;
			pos = pos + forward + (Up * 1.2f); // pick a spot in the air
			var end = new Vector3(pos.X, pos.Y, pos.Z - 1.9f); // try to drop it down
			RaycastResult result = Raycast(pos, end, .4f, opts, Self);
			return result.DidHit ? (result.HitPosition - end).Length() : 0f;
		}

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
			foreach( var e in active ) {
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
				if( LookToward(HeadPosition(KillTarget) + Velocity(KillTarget) * 3 / CurrentFPS)
					|| IsAimingAtEntity(KillTarget) ) {
					SetControlValue(1, Control.Attack, 1f);
				}
			} else {
				KillTarget = PedHandle.Invalid;
				AimAtHead = PedHandle.Invalid;
				ForcedAim(CurrentPlayer, false);
			}
			if( WalkTarget != Vector3.Zero ) {
				UI.DrawText($"WalkTarget: {WalkTarget}");
				if( DistanceToSelf(WalkTarget) < 2f
					|| WalkPath.IsFailed() ) {
					WalkTarget = Vector3.Zero;
				} else if( WalkPath.IsReady() ) {
					if( FollowPath(WalkPath.GetResult().Take(4).Select(Position)) == MoveResult.Failed ) {
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
			private static ConcurrentQueue<Event> keyEvents = new ConcurrentQueue<Event>();
			private static Dictionary<Keys, Action> keyBindings = new Dictionary<Keys, Action>();
			public static void Bind(Keys key, Action action) => keyBindings[key] = keyBindings.TryGetValue(key, out Action curr) ? (() => { curr(); action(); }) : action;
			public static void Enqueue(Keys key, bool downBefore, bool upNow) => keyEvents.Enqueue(new Event() { key = key, downBefore = downBefore, upNow = upNow });
			public static void DisableAllThisFrame(Type except = null) { Disabled = true; DisabledExcept = except; }
			public static Type DisabledExcept = null;
			public static bool Disabled = false;
			public static void OnTick() {
				while( keyEvents.TryDequeue(out Event evt) ) {
					if( (!evt.downBefore) && keyBindings.TryGetValue(evt.key, out Action action) ) {
						try {
							// when disabled, still consume the key strokes, just ignore actions
							if( !Disabled ) {
								action();
							}
						} catch( Exception err ) {
							Log($"OnKey({evt.key}) exception from key-binding: {err.Message} {err.StackTrace}");
							keyBindings.Remove(evt.key);
						}
					} else if( DisabledExcept != null ) {
						Script.Order.Where(s => s.GetType() == DisabledExcept).FirstOrDefault(s => s.OnKey(evt.key, evt.downBefore, evt.upNow));
					} else if( ! Disabled ) {
						Script.Order.FirstOrDefault(s => s.OnKey(evt.key, evt.downBefore, evt.upNow));
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