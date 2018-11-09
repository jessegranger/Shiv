using System;
using System.Collections.Generic;
using System.Linq;
using static Shiv.Globals;

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
	}

	public class GoalSet {
		private LinkedList<Goal> goals = new LinkedList<Goal>();
		public void Push(Goal g) => goals.AddFirst(g);
		public Goal Pop() => goals.Pop();
		public Goal Peek() => goals.First?.Value;
		public void Clear() => goals.Clear();
		public int Count => goals.Count;
		public override string ToString() => $"Goals({Count}): " + string.Join(", ", goals.Select(g => g.ToString()));
	}

	public static partial class Globals {
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
			UI.DrawText(Goals.ToString());
		}
	}
}
