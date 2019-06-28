using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Windows.Forms;
using System.Linq;

namespace Shiv {

	/// <summary>
	/// The base Script, uses the DependOn attribute to put all instances of subclasses of Script in order.
	/// </summary>
	public abstract class Script : IDisposable {
		/// <summary>
		/// The global ordering of all Script instances.
		/// </summary>
		internal static LinkedList<Script> Order = new LinkedList<Script>();

		/// <summary>
		/// The base constructor applies the DependOn attribute.
		/// Each script has a single instance created by the Main loop.
		/// </summary>
		internal Script() {
			DependOn a = GetType().GetCustomAttributes<DependOn>(true).FirstOrDefault();
			if( a != null ) {
				Order.AddAfter(this, (s) => s.GetType() == a.T);
			} else {
				Order.AddFirst(this);
			}
		}

		internal bool disposed = false;
		~Script() {
			if( !disposed ) {
				Dispose();
			}
		}
		public virtual void Dispose() => disposed = true;

		/// <summary>
		/// Called for every Key event. Return true to prevent later Scripts from being called.
		/// </summary>
		/// <returns>true if the key was consumed</returns>
		public virtual bool OnKey(Keys key, bool wasDownBefore, bool isUpNow) => false;

		/// <summary>
		/// Called once when the Main loop creates the single instance.
		/// </summary>
		public virtual void OnInit() { }

		/// <summary>
		/// Called every frame, even if the game is paused.
		/// </summary>
		public virtual void OnTick() { }

		/// <summary>
		/// Called when this script should cleanup and exit.
		/// </summary>
		public virtual void OnAbort() { }

	}

	public class ScriptComplete : Exception { }

	[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
	public class DependOn : Attribute {
		internal Type T;
		public DependOn(Type t) => T = t;
	}

	// Run some Func<bool> in the main loop 
	// until it returns true or the Duration expires.
	public class QuickScript : Script {
		public uint Duration;
		public Func<bool> Tick;
		private Stopwatch s = new Stopwatch();
		public QuickScript(Func<bool> func) : this(uint.MaxValue, func) { }
		public QuickScript(uint dur, Func<bool> func) {
			Duration = dur;
			Tick = func;
			s.Start();
		}
		public override void OnTick() {
			if( s.ElapsedMilliseconds > Duration || Tick() ) {
				Dispose();
			}
		}
		public override void Dispose() {
			if( !disposed ) {
				s.Stop();
			}

			base.Dispose();
		}
	}

}

