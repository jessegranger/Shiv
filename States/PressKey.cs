using static Shiv.Global;
using System.Diagnostics;

namespace Shiv {
	class PressKey : State {
		readonly int Group = 0;
		readonly Control Key;
		readonly float Value = 1.0f;
		readonly uint Duration;
		Stopwatch sw = new Stopwatch();
		public PressKey(Control key, uint duration, State next=null) : this(1,key,duration,next) { }
		public PressKey(int group, Control key, uint duration, State next = null) : base(next) {
			Group = group;
			Key = key;
			Duration = duration;
		}
		public override State OnTick() {
			if( ! sw.IsRunning ) {
				sw.Start();
			}
			if( sw.ElapsedMilliseconds >= Duration ) {
				return Next;
			}
			SetControlValue(Group, Key, Value);
			return this;
		}
		public override string ToString() => $"PressKey({Group},{Key},{Value:F1}f,{Duration - sw.ElapsedMilliseconds}ms)";
	}
}
