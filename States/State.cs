using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Shiv {

	public class State {

		// 'Next' defines the default State we will go to when this State is complete.
		// This value is just a suggestion, the real value is what gets returned by OnTick
		public State Next = null;
		// 'Fail' defines the State to go to if there is any kind of exception
		public State Fail = null;

		public State(State next = null) => Next = next;

		// OnEnter gets called once before the first OnTick
		public virtual State OnEnter() => this;

		// OnTick gets called every frame and should return the next State
		public virtual State OnTick() => this;

		// OnAbort gets called by the StateMachine, if it needs to cleanup an in-progress task before it's complete
		public virtual void OnAbort() { }

		public virtual string Name => GetType().Name.Split('.').Last();
		public override string ToString() => $"{Name}{(Next == null ? "" : " then " + Next.Name)}";

		// You can create a new State using any Func<State, State>
		public static State Create(string label, Func<State, State> func) => new Runner(label, func);
		public static implicit operator State(Func<State, State> func) => new Runner(func);
		public static implicit operator Func<State, State>(State state) => (s) => s.OnTick();

		// A Runner is a special State that uses a Func<State, State> to drive it
		public class Runner : State {
			readonly Func<State, State> F;
			public Runner(Func<State, State> func) => F = func;
			public override State OnTick() => F(this);
			public override string Name => name;
			private readonly string name = "...";
			public Runner(string name, Func<State, State> func) : this(func) => this.name = name;
		}

		public static State Series(params State[] states) {
			for( int i = 0; i < states.Length - 1; i++ ) {
				states[i].Next = states[i + 1];
			}
			return states?[0];
		}

		public class Machine : State {

			// each machine runs any number of states at once (in 'parallel' frame-wise)
			// when a machine is empty, it gets collected by the reaper
			private LinkedList<State> States;

			public State CurrentState => States.FirstOrDefault();

			// public Machine(params Func<State, State>[] states) : this(states.Cast<State>().ToArray()) { }
			public Machine(params State[] states) => States = new LinkedList<State>(states);

			public override string ToString() => string.Join(" while ", States.Select(s => $"({s})")) + (Next == null ? "" : $" then {Next.Name}");

			private static LinkedListNode<T> RemoveAndContinue<T>(LinkedList<T> list, LinkedListNode<T> node) {
				LinkedListNode<T> next = node.Next;
				list.Remove(node);
				return next;
			}

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

			private static Action<string> logDelegate;
			public static void EnableLogging(Action<string> logger) => logDelegate = logger;
			public static void DisableLogging() => logDelegate = null;
			private static void Log(string s) => logDelegate?.Invoke(s);

			public override State OnTick() {
				// Each State in the States list will be ticked in parallel
				LinkedListNode<State> curNode = States.First;
				while( curNode != null ) {
					State curState = curNode.Value;
					State gotoState = curState.OnTick();
					if( gotoState == null ) {
						Log($"State Finished: {curState.Name}.");
						curNode = RemoveAndContinue(States, curNode);
						continue;
					}
					if( gotoState != curState ) {
						gotoState = gotoState.OnEnter();
						Log($"State Changed: {curState.Name} to {gotoState.Name}");
						if( gotoState.GetType() == typeof(Clear) ) {
							Abort(except: curState); // call all OnAbort in State, except curState.OnAbort, because it just ended cleanly (as far as it knows)
							return gotoState.Next ?? Next;
						}
						curNode.Value = gotoState;
					}
					curNode = curNode.Next; // loop over the whole list
				}
				return States.Count == 0 ? Next : this;
			}
			public void Abort(State except = null) {
				foreach( State s in States ) if( s != except ) s.OnAbort();
				States.Clear();
			}
			public void Add(State state) => States.AddLast(state);
			public void Remove(State state) => States.Remove(state);
			public void Remove(Type stateType) {
				LinkedListNode<State> cur = States.First;
				while( cur != null ) {
					cur = cur.Value.GetType() == stateType ? RemoveAndContinue(States, cur) : cur.Next;
				}
			}

			public bool HasState(Type stateType) => States.Any(s => s.GetType() == stateType);
		}

	}

	public class Delay : State {
		Stopwatch sw = new Stopwatch();
		readonly uint ms;
		public Delay(uint ms, State next = null) : base(next) => this.ms = ms;
		public override State OnEnter() {
			sw.Restart();
			return this;
		}
		public override State OnTick() => sw.ElapsedMilliseconds >= ms ? Next : (this);
		public override string Name => $"Delay({ms})";
	}
}
