
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

		public static void PressControl(int group, Control control, uint duration) => Controls.PressControl(group, control, duration);

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
		public static Vector3 FirstStep(IEnumerable<NodeHandle> path) => FirstStep(path.Take(3).Select(NavMesh.Position));
		// public static Vector3 FirstStep(IEnumerable<Vector3> path) => Bezier(.25f, Items(PlayerPosition).Concat(path.Take(5)).ToArray());
		public static Vector3 FirstStep(IEnumerable<Vector3> path) {
			var factor = (float)Pow(Math.Max(0f, 1f - DistanceToSelf(path.First())), 2);
			return Interp.Bezier(factor, path.Take(3).ToArray());
			// var first = path.First();
			// var second = path.Skip(1).First();
			// return Vector3.Lerp(first, second, Clamp(2f * (float)Pow(1f - DistanceToSelf(first), 2), 0f, 1f));
		}
		public static MoveResult FollowPath(IEnumerable<NodeHandle> path, float steppingRange=0.2f) => FollowPath(path.Take(3).Select(NavMesh.Position));
		public static MoveResult FollowPath(IEnumerable<Vector3> path, float steppingRange=0.2f) => path == null || path.Count() < 2 ? MoveResult.Complete : MoveToward(FirstStep(path), steppingRange);

		private static float LookActivation(float x) => (float)(1f * ((Sigmoid(x * CurrentFPS / 4f) * 2f) - 1f));
		// private static float LookActivation(float x) => (float)(x * CurrentFPS * .2f);
		// private static float LookActivation(float x) => (float)(x * CurrentFPS * Sqrt(Abs(CurrentFPS * x * .2f)));
		// private static float LookActivation(float x) => (float)(x * CurrentFPS * Sqrt(Abs(x * .8f)));
		public static bool LookToward(Vector3 pos, bool debug=false) {
			pos = pos - (Velocity(Self) * 7f / CurrentFPS);
			Vector3 forward = Forward(CameraMatrix);
			Vector3 cam = Position(CameraMatrix);
			Vector3 delta = Vector3.Normalize(pos - cam) - forward;
			Vector3 end = cam + forward;
			float right = Vector3.Dot(delta, Right(CameraMatrix));
			float up = Vector3.Dot(delta, UpVector(CameraMatrix));
			if( debug ) { DrawLine(end, end + delta, Color.White); }
			// probably a way to do all this with one quaternion multiply or something
			if( debug ) { UI.DrawText(.45f, .43f, $"Look: activating: {right:F4} {up:F4}"); }
			float dX = Clamp(LookActivation(right), -10f, 10f);
			float dY = Clamp(LookActivation(up), -10f, 10f);
			SetControlValue(1, Control.LookLeftRight, dX);
			SetControlValue(1, Control.LookUpDown, -dY);
			if( debug ) { UI.DrawText(.45f, .45f, $"Look: dx:{dX:F4} dy:{dY:F4} (err {delta.LengthSquared():F5})"); }
			return delta.LengthSquared() < .0001f;
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
			if( IsRagdoll(Self) ) { // Call<bool>(IS_PED_RAGDOLL, Self) ) {
				return ret;
			}
			heading = new Vector3(heading.X, heading.Y, 0f);
			IntersectOptions opts = IntersectOptions.Map | IntersectOptions.Objects | IntersectOptions.Vehicles;
			float capsuleSize = .12f;
			float stepSize = .25f;
			heading = Vector3.Normalize(heading) * .5f;
			var head = HeadPosition(Self) + (Up * 5 * stepSize);
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
	}

	public static class Controls {
		private struct Event {
			public Keys key;
			public bool downBefore;
			public bool upNow;
		}
		private struct ControlCommand {
			public int group;
			public Control control;
			public uint expires;
		}
		private static List<ControlCommand> active = new List<ControlCommand>();
		public static void PressControl(int g, Control c, uint dur) => active.Add(new ControlCommand() { group = g, control = c, expires = GameTime + dur });
		// this queue is filled up by the main Shiv class
		private static ConcurrentQueue<Event> keyEvents = new ConcurrentQueue<Event>();

		// this dictionary is filled up by Controls.Bind(), just below.
		private static Dictionary<Keys, Action> keyBindings = new Dictionary<Keys, Action>();

		/// <summary>
		/// In all future frames, pressing the specified key will invoke the given Action.
		/// </summary>
		public static void Bind(Keys key, Action action) => keyBindings[key] = keyBindings.TryGetValue(key, out Action curr) ? (() => { curr(); action(); }) : action;

		/// <summary>
		/// Register that a key was pressed. All such key events will be processed at the start of the next frame.
		/// </summary>
		public static void Enqueue(Keys key, bool downBefore, bool upNow) => keyEvents.Enqueue(new Event() { key = key, downBefore = downBefore, upNow = upNow });

		/// <summary>
		/// For one frame, consume and ignore all key events.
		/// Game inputs will also be disabled.
		/// Scripts that want exclusive keyboard access should call DisableAllThisFrame(GetType()), every frame.
		/// </summary>
		/// <param name="except">Allow one Script type to keep getting events.</param>
		public static void DisableAllThisFrame(Type except = null) { Disabled = true; DisabledExcept = except; }

		private static Type DisabledExcept = null;
		public static bool Disabled { get; private set; } = false;

		public static void OnTick() {
			// Process all the keys that we are artificially pressing
			active.RemoveAll(e => e.expires < GameTime);
			foreach( var e in active ) {
				SetControlValue(e.group, e.control, 1.0f);
			}

			// Process all the keys being pressed by the real player
			while( keyEvents.TryDequeue(out Event evt) ) {
				IEnumerable<Script> items;
				if( DisabledExcept == null ) {
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