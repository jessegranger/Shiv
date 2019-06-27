using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using static Shiv.Global;

namespace Shiv {
	public enum GoalStatus {
		Active,
		Paused,
		Complete,
		Failed
	}
	public abstract class Goal : IDisposable {
		public GoalStatus Status = GoalStatus.Paused;
		public abstract GoalStatus OnTick();
		public virtual void OnPause() { Status = GoalStatus.Paused; }
		public virtual void OnResume() { Status = GoalStatus.Active; }
		public virtual void Dispose() { }
		public Goal() { }
		public override string ToString() => GetType().Name.Split('.').Last();
	}

	public class QuickGoal : Goal {
		private Func<GoalStatus> tick;
		private uint duration = uint.MaxValue;
		private uint started = 0;
		public string Name = "QuickGoal";
		public QuickGoal(string name, Func<GoalStatus> func) {
			Name = name;
			tick = func;
		}
		public QuickGoal(Func<GoalStatus> func) => tick = func;
		public QuickGoal(uint timeout, Action func) {
			duration = timeout;
			tick = () => { func(); return Status; };
		}
		public override GoalStatus OnTick() {
			if( started == 0 ) started = GameTime;
			if( GameTime - started > duration )
				return Status = GoalStatus.Complete;
			return Status = tick();
		}
		public override string ToString() {
			return Name;
		}
	}

	public class GoalSet {
		protected LinkedList<Goal> goals = new LinkedList<Goal>();
		protected LinkedListNode<Goal> cursor;
		public GoalSet() {
			goals = new LinkedList<Goal>();
			cursor = null;
		}
		public GoalSet Immediate(Goal g) { goals.AddFirst(g); return this; }
		public GoalSet Next(Goal g) { goals.AddAfter( goals.First, g); return this; }
		public GoalSet Enqueue(Goal g) { goals.AddLast(g); return this; }
		public Goal Pop() => goals.Pop();
		public Goal Peek() => goals.First?.Value;
		public GoalSet Clear() { goals.Clear(); return this; }
		public int Count => goals.Count;
		public override string ToString() => string.Join(", ", goals.Select(g => g.ToString().Split('.').Last()));
	}

	public static partial class Global {
		public static GoalSet Goals = new GoalSet();
	}

	public class GoalScript : Script {
		private Goal prevFirst = null;
		private static bool IsDone(GoalStatus? status) => status.HasValue && (status.Value == GoalStatus.Failed || status.Value == GoalStatus.Complete);
		public override void OnTick() {
			while( Goals.Count > 0 && IsDone(Goals.Peek().Status) ) {
				Goals.Pop();
				prevFirst = null;
			}
			if( Goals.Count > 0 ) {
				Goal curFirst = Goals.Peek();
				if( prevFirst != curFirst ) { // pause and resume pre-empted tasks as the top item changes
					if( prevFirst?.Status == GoalStatus.Active ) prevFirst.OnPause();
					if( curFirst.Status == GoalStatus.Paused ) curFirst.OnResume();
					prevFirst = curFirst;
				}
				curFirst.OnTick();
			}
			var head = HeadPosition(Self);
			var s = ScreenCoords(head);
			var str = Goals.ToString();
			UI.DrawRect(s.X, s.Y, .006f * str.Length, .022f, Color.SlateGray);
			UI.DrawText(s.X, s.Y - .001f, str);
		}
	}
}
