
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using static GTA.Native.Function;
using static GTA.Native.Hash;
using static Shiv.Globals;
using static System.Math;

namespace Shiv {
	public static partial class Globals {

		public static void SetControlValue(int group, Control control, float value) { Call(_SET_CONTROL_NORMAL, group, control, value); }
		
		public static void PressControl(int group, Control control, uint duration) {
			ControlsScript.PressControl(group, control, duration);
		}

		private static float MoveActivation(float x, float fps) => (Sigmoid(x) * 4f) - 2f;
		public static void MoveToward(Vector3 pos) {
			var delta = pos - Position(PlayerMatrix);
			SetControlValue(1, Control.MoveLeftRight, MoveActivation(
				+2f * Vector3.Dot(delta, Right(CameraMatrix)), // TODO: 2 should be aspect ratio?
				CurrentFPS));
			SetControlValue(1, Control.MoveUpDown, MoveActivation(
				-1f * Vector3.Dot(delta, Forward(CameraMatrix)),
				CurrentFPS));
		}

		public static float LookActivation(float x) => Clamp(
			(float)Tanh(Sqrt(Abs(x / 4)) * x * CurrentFPS),
			-2f, 2f);
		public static bool LookToward(Vector3 pos) {
			pos = pos - Velocity(Self);
			DrawSphere(pos, .05f, Color.Yellow);
			Vector3 delta = Vector3.Normalize(pos - Position(CameraMatrix)) - Forward(CameraMatrix);
			// probably a way to do all this with one quaternion multiply or something
			SetControlValue(1, Control.LookLeftRight, LookActivation(
				Vector3.Dot(delta, Right(CameraMatrix))));
			SetControlValue(1, Control.LookUpDown, LookActivation(
				-Vector3.Dot(delta, UpVector(CameraMatrix))));
			return delta.LengthSquared() < 6f;
		}

		public static Vector3 LookTarget = Vector3.Zero;

	}

	public class ControlsScript : Script {
		private struct ControlEvent {
			public int group;
			public Control control;
			public uint expires;
		}
		private static List<ControlEvent> active = new List<ControlEvent>();
		public static void PressControl(int g, Control c, uint dur) {
			active.Add(new ControlEvent() { group = g, control = c, expires = GameTime + dur });
		}
		public override void OnTick() {
			active.RemoveAll(e => e.expires < GameTime);
			foreach( var e in active ) {
				SetControlValue(e.group, e.control, 1.0f);
			}
			if( LookTarget != Vector3.Zero )
				LookToward(LookTarget);
		}

	}
}