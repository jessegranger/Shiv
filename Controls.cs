using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using static GTA.Native.Function;
using static GTA.Native.Hash;
using Keys = System.Windows.Forms.Keys;

namespace Shiv {


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
							if( !Disabled ) action();
						} catch( Exception err ) {
							Shiv.Log($"OnKey({evt.key}) exception from key-binding: {err.Message} {err.StackTrace}");
							keyBindings.Remove(evt.key);
						}
					} else if( DisabledExcept != null ) {
						Script.Order.Where(s => s.GetType() == DisabledExcept).FirstOrDefault(s => s.OnKey(evt.key, evt.downBefore, evt.upNow));
					} else if( ! Disabled ) {
						Script.Order.FirstOrDefault(s => s.OnKey(evt.key, evt.downBefore, evt.upNow));
					}
				}
				if( Disabled ) Call(DISABLE_ALL_CONTROL_ACTIONS, 1);
				Disabled = false;
				DisabledExcept = null;
			}
		}

	}
}

