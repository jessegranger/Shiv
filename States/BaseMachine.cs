using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Shiv.Global;
using System.Numerics;

namespace Shiv {

	public class State {
		public State Next = null;
		public State Fail = null;
		public State() => Next = null;
		public State(State next) => Next = next;

		// OnTick gets called every frame and returns the next State
		public virtual State OnTick() => this;

		public virtual string Name => GetType().Name.Split('.').Last();
		public override string ToString() => $"{Name}{(Next == null ? "" : " then " + Next.Name)}";

		public static State Idle = new StateMachine.Runner("Idle", (state) => state);
	}

	public class StateMachine: Script {

		public static State CurrentState;
		public StateMachine() { }

		public static void Clear() => CurrentState = State.Idle;

		public static void Run(State state) {
			Shiv.Log($"[StateScript] Start {state}");
			CurrentState = state;
		}
		public static void Run(Func<State, State> func) => Run(new Runner(func));
		public static void Run(string name, Func<State, State> func) => Run(new Runner(name, func));

		internal class Runner : State {
			readonly Func<State, State> F;
			private readonly string name;
			public override string Name => name ?? base.Name;
			public Runner(Func<State, State> func) => F = func;
			public Runner(string name, Func<State, State> func) { F = func; this.name = name; }
			public override State OnTick() => F(CurrentState);
		}

		public override void OnInit() => CurrentState = State.Idle;
		public override void OnAbort() => CurrentState = null;
		public override void OnTick() {
			if( CurrentState != null ) {
				UI.DrawTextInWorldWithOffset(HeadPosition(Self), 0f, .02f, $"State: {CurrentState}");
				if( !GamePaused ) {
					State nextState = CurrentState.OnTick();
					if( CurrentState != nextState ) {
						Shiv.Log($"[StateScript] -> {nextState}");
						CurrentState = nextState;
					}
				}
			}
		}

	}

	class WaitForControl : State {
		public bool Value = true;
		public WaitForControl(bool value, State next = null) : base(next) => Value = value;
		public WaitForControl(State next) : base(next) { }
		public override State OnTick() => CanControlCharacter() == Value ? Next : (this);
		public override string Name => $"WaitForControl({Value})";
	}
	class WaitForCutscene : State {
		public WaitForCutscene(State next) : base(next) { }
		public override State OnTick() => new WaitForControl(false, new WaitForControl(true, Next));
	}
	class Delay : State {
		Stopwatch sw = new Stopwatch();
		readonly uint ms;
		public Delay(uint ms, State next = null) : base(next) {
			this.ms = ms;
			sw.Start();
		}
		public override State OnTick() => sw.ElapsedMilliseconds >= ms ? Next : (this);
		public override string Name => $"Delay({ms})";
	}
	class WaitForBlip : State {
		readonly BlipSprite Kind;
		readonly BlipHUDColor Color;
		public WaitForBlip(BlipHUDColor color, State next = null) : this(BlipSprite.Standard, color, next) { }
		public WaitForBlip(BlipSprite kind, BlipHUDColor color, State next = null) : base(next) {
			Kind = kind;
			Color = color;
		}
		public override State OnTick() => GetAllBlips(Kind).Any(b => GetBlipHUDColor(b) == Color) ? Next : this;
		public override string Name => $"WaitForBlip({Color})";
	}

	class MultiState : State {
		private LinkedList<State> States;
		public MultiState(params Func<State, State>[] states):this(states.Select(s => new StateMachine.Runner(s)).ToArray() ) { }
		public MultiState(params State[] states) => States = new LinkedList<State>(states);
		public override string ToString() => string.Join(" while ", States.Select(s => $"({s})")) + (Next == null ? "" : $" then {Next.Name}");
		public override State OnTick() {
			LinkedListNode<State> cur = States.First;
			while( cur != null ) {
				State next = cur.Value.OnTick();
				if( next == null ) {
					Log($"State {cur.Value.Name} finished.");
					cur = States.RemoveAndContinue(cur);
					continue;
				}
				if( next != cur.Value ) {
					Log($"State Change {cur.Value.Name} to {next.Name}");
					cur.Value = next;
				}
				cur = cur.Next;
			}
			if( States.Count == 0 ) {
				return Next;
			}
			return this;
		}
		public void Add(State state) => States.AddLast(state);
		public void Remove(State state) => States.Remove(state);
		public void Remove(Type stateType) {
			LinkedListNode<State> cur = States.First;
			while( cur != null ) {
				cur = cur.Value.GetType() == stateType ? States.RemoveAndContinue(cur) : cur.Next;
			}
		}
	}
}
