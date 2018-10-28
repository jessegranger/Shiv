
using System.Collections.Generic;
using static GTA.Native.Function;
using static GTA.Native.Hash;
using static Shiv.Globals;

namespace Shiv {
	public static partial class Globals {

		public static void SetControlValue(int group, Control control, float value) { Call(_SET_CONTROL_NORMAL, group, control, value); }
		
		public static void PressControl(int group, Control control, uint duration) {
			ControlsScript.PressControl(group, control, duration);
		}

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
		}

	}
}