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
		public PedHandle Actor = Self; // should this be an EntHandle maybe?
		public State() => Next = null;
		public State(State next) => Next = next;

		// OnTick gets called every frame and returns the next State
		public virtual State OnTick() => this;
		public virtual void OnAbort() { }

		public virtual string Name => GetType().Name.Split('.').Last();
		public override string ToString() => $"{Name}{(Next == null ? "" : " then " + Next.Name)}";

		public static State Runner(string label, Func<State, State> func) => new Ticker(label, func);
		public static State Runner(Func<State, State> func) => new Ticker(func);

		private class Ticker : State {
			readonly Func<State, State> F;
			public override string Name => name;
			private readonly string name = "...";
			public Ticker(Func<State, State> func) => F = func;
			public Ticker(string name, Func<State, State> func):this(func) => this.name = name;
			public override State OnTick() => F(this);
		}

		public static State Series(params State[] states) {
			if( states.Length == 0 ) {
				return (State)((s) => null);
			}
			for(int i = 0; i < states.Length - 1; i++) {
				states[i].Next = states[i + 1];
			}
			return states[0];
		}

		public static implicit operator State(Func<State, State> func) => Runner(func);
	}

	public static partial class Global {
		public static void AddState(PedHandle ped, params Func<State, State>[] states) => StateMachines.Add(ped, new StateMachine(ped, states));
		public static void AddState(PedHandle ped, params State[] states) => StateMachines.Add(ped, new StateMachine(ped, states));
		public static void AddStateOnce(PedHandle ped, State state) {
			if( !StateMachines.HasState(ped, state.GetType()) ) {
				StateMachines.Add(ped, new StateMachine(ped, state));
			}
		}
		public static void SetState(PedHandle ped, params State[] states) => StateMachines.Set(ped, new StateMachine(ped, states));
		public static bool HasState(PedHandle ped, Type stateType) => StateMachines.HasState(ped, stateType);
	}

	public class StateMachines: Script {

		private static Dictionary<PedHandle, StateMachine> machines = new Dictionary<PedHandle, StateMachine>();

		internal static void Add(PedHandle ped, StateMachine state) {
			Shiv.Log($"[StateScript] Add {state} to ped {ped}");
			state.Actor = ped;
			if( !machines.ContainsKey(ped) ) {
				machines.Add(ped, state);
			} else {
				machines[ped].Add(state);
			}
		}

		internal static void Set(PedHandle ped, StateMachine state) {
			Shiv.Log($"[StateScript] Interrupt {ped} with {state}");
			state.Actor = ped;
			if( !machines.ContainsKey(ped) ) {
				machines.Add(ped, state);
			} else {
				machines[ped].Abort();
				machines[ped] = state;
			}
		}

		internal static bool HasState(PedHandle ped, Type stateType) => machines[ped]?.HasState(stateType) ?? false;

		internal static void ClearAllStates(PedHandle ped) {
			if( machines.ContainsKey(ped) ) {
				machines[ped].Abort();
				machines.Remove(ped);
			}
		}
		internal static void ClearAllStates() {
			machines.Values.Each(m => m.Abort());
			machines.Clear();
		}

		public StateMachines() { }

		public override void OnInit() { }
		public override void OnAbort() => ClearAllStates();
		public override void OnTick() => machines
			.Where((kv, i) => !Exists(kv.Key) || kv.Value.OnTick() == null) // tick all the machines for all the peds
			.Select(kv => kv.Key).ToArray() // select keys of dead machines and dead peds
			.Each((k) => machines.Remove(k)); // and remove them

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

	internal class StateMachine : State {

		// each machine runs any number of states at once
		// when a machine is empty, it gets collected by the reaper
		private LinkedList<State> States;

		public State CurrentState => States.FirstOrDefault();

		public StateMachine(PedHandle actor, params Func<State, State>[] states) : this(actor, states.Cast<State>().ToArray()) { }
		public StateMachine(PedHandle actor, params State[] states) {
			States = new LinkedList<State>(states);
			Actor = actor;
		}

		public override string ToString() => string.Join(" while ", States.Select(s => $"({s})")) + (Next == null ? "" : $" then {Next.Name}");

		/// <summary>
		/// Clear is a special state that clears all running states from a MultiState.
		/// </summary>
		/// <example>new StateMachine(
		///   new WalkTo(X),
		///   new ShootAt(Y, // will walk and shoot at the same time, when shoot is finished, clear the whole machine (cancel the walk)
		///     new StateMachine.Clear(this)) );
		///  </example>
		public class Clear : State {
			public Clear(State next) : base(next) { }
			public override State OnTick() => Next;
		}

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
					if( next.GetType() == typeof(Clear) ) {
						Abort(except:cur.Value); // dont cancel cur.Value because it just ended (as far as it knows)
						return next.Next ?? Next;
					}
					next.Actor = Actor;
					cur.Value = next;
				}
				cur = cur.Next;
			}
			return States.Count == 0 ? Next : this;
		}
		public void Abort(State except=null) {
			States.Without(except).Each(s => s.OnAbort());
			States.Clear();
		}
		public void Add(State state) => States.AddLast(state);
		public void Remove(State state) => States.Remove(state);
		public void Remove(Type stateType) {
			LinkedListNode<State> cur = States.First;
			while( cur != null ) {
				cur = cur.Value.GetType() == stateType ? States.RemoveAndContinue(cur) : cur.Next;
			}
		}

		internal bool HasState(Type stateType) => States.Any(s => s.GetType() == stateType);
	}
}
