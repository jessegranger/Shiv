using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Windows.Forms;

namespace Shiv {
	public abstract class Script : IDisposable {
		internal static LinkedList<Script> Order = new LinkedList<Script>();
		internal static List<String> Failures = new List<string>();
		internal bool disposed = false;
		internal Script() {
			foreach( DependOn a in GetType().GetCustomAttributes<DependOn>(true) ) {
				Order.AddAfter(this, (s) => s.GetType() == a.T);
				return;
			}
			Order.AddFirst(this);
		}
		~Script() { if( !disposed ) Dispose(); }

		public virtual void Dispose() => disposed = true;

		public virtual bool OnKey(Keys key, bool wasDownBefore, bool isUpNow) { return false; }
		public virtual void OnInit() { }
		public virtual void OnTick() { }
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
			if( s.ElapsedMilliseconds > Duration || Tick() ) Dispose();
		}
		public override void Dispose() {
			if( !disposed ) s.Stop();
			base.Dispose();
		}
	}

}

