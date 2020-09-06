using System;
using System.Collections.Generic;
using System.Linq;
using static Shiv.Global;

namespace Shiv {

	public class PedState : State {
		public PedHandle Actor;
		public PedState(State next = null) => Actor = PedHandle.Invalid;
		public PedState(PedHandle actor, State next = null) : base(next) => Actor = actor;
	}
	public class PlayerState : PedState {
		public PlayerState(State next = null) => Actor = Self;
	}

	public static partial class Global {
		public static void AddState(PedHandle ped, params State[] states) => StateMachines.Add(ped, new State.Machine(states));
		public static void AddStateOnce(PedHandle ped, State state) {
			if( !StateMachines.HasState(ped, state.GetType()) ) {
				StateMachines.Add(ped, new State.Machine(state));
			}
		}
		public static void SetState(PedHandle ped, params State[] states) => StateMachines.Set(ped, new State.Machine(states));
		public static bool HasState(PedHandle ped, Type stateType) => StateMachines.HasState(ped, stateType);
		public static void RemoveState(PedHandle ped, Type stateType) => StateMachines.RemoveState(ped, stateType);
	}

	public class StateMachines: Script {

		private static Dictionary<PedHandle, State.Machine> machines = new Dictionary<PedHandle, State.Machine>();

		internal static void Add(PedHandle ped, State.Machine state) {
			Shiv.Log($"[StateScript] Add {state} to ped {ped}");
			if( !machines.ContainsKey(ped) ) {
				machines.Add(ped, state);
			} else {
				machines[ped].Add(state);
			}
		}

		internal static void Set(PedHandle ped, State.Machine state) {
			Shiv.Log($"[StateScript] Interrupt {ped} with {state}");
			if( !machines.ContainsKey(ped) ) {
				machines.Add(ped, state);
			} else {
				machines[ped].Abort();
				machines[ped] = state;
			}
		}

		internal static bool HasState(PedHandle ped, Type stateType) => machines.ContainsKey(ped) && machines[ped].HasState(stateType);
		internal static void RemoveState(PedHandle ped, Type stateType) {
			if( machines.ContainsKey(ped) ) {
				machines[ped].Remove(stateType);
			}
		}

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
				.ToArray() // read it all in advance so OnTick can modify machines dictionary
				.Where((kv, i) => !Exists(kv.Key) || Tick(kv.Key, kv.Value) == null) // tick all the machines for all the peds
				.Select(kv => kv.Key).ToArray() // select keys of dead machines and dead peds
				.Each((k) => machines.Remove(k)); // and remove them
		private static State Tick(PedHandle ped, State.Machine m) {
			UI.DrawHeadline(ped, $"State: {m.ToString()}");
			return m.OnTick();
		}

	}

	class WaitForControl : State {
		public bool Value = true;
		public WaitForControl(bool value, State next = null) : base(next) => Value = value;
		public WaitForControl(State next = null) : base(next) { }
		public override State OnTick() => CanControlCharacter() == Value ? Next : (this);
		public override string Name => $"WaitForControl({Value})";
	}
	class WaitForCutscene : State {
		public WaitForCutscene(State next = null) : base(next) { }
		public override State OnTick() => new WaitForControl(false, new WaitForControl(true, Next));
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
}
