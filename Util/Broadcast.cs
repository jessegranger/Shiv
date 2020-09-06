using System;
using System.Collections.Concurrent;
using System.Linq;
using static Shiv.Global;
using StateMachine;

namespace Shiv {
	class Broadcast {

		private class WaitingObject {
			public string Kind;
			public PedHandle From;
			public PedHandle To;
			public Action<Message> response;
		}

		public class Message {
			public string Kind;
			public PedHandle From;
			public PedHandle To;
			public object Data;
		}

		private static ConcurrentDictionary<WaitingObject, bool> waiting = new ConcurrentDictionary<WaitingObject, bool>();

		private static bool Match(Message m, string kind, PedHandle from, PedHandle to) => (m.Kind == kind) 
			&& (from == PedHandle.Invalid || m.From == PedHandle.Invalid || from == m.From)
			&& (to == PedHandle.Invalid || m.To == PedHandle.Invalid || to == m.To);

		public static void SendMessage(string kind, PedHandle from, PedHandle to=default, object data=null) {
			var m = new Message() { Kind = kind, From = from, To = to, Data = data };
			foreach( var wait in waiting.Keys.ToArray() ) {
				if( Match(m, wait.Kind, wait.From, wait.To) ) {
					wait.response(m);
					waiting.TryRemove(wait, out bool _);
				}
			}
		}

		public static void WaitForMessage(string kind, PedHandle from, PedHandle to, Action<Message> response) => waiting.TryAdd(new WaitingObject() { Kind = kind, From = from, To = to, response = response }, true);
		public static void WaitForMessage(string kind, Action<Message> response) => WaitForMessage(kind, PedHandle.Invalid, PedHandle.Invalid, response);

	}

	class WaitForMessage : State {
		private readonly string Kind;
		private readonly PedHandle To;
		private readonly PedHandle From;
		private uint Started = 0;
		public uint Timeout = uint.MaxValue;
		public WaitForMessage(string kind, PedHandle to=default, PedHandle from=default, State next=null):base(next) {
			Kind = kind;
			From = from;
			To = to;
		}
		private Broadcast.Message message = null;
		public override State OnTick() {
			if( Started == 0 ) {
				Started = GameTime;
				Broadcast.WaitForMessage(Kind, From, To, (m) => message = m);
			}
			return (GameTime - Started) > Timeout || message != null ? Next : this;
		}
		public override string ToString() => $"WaitForMessage({Kind}{(From != PedHandle.Invalid ? $" from: {From}" : "")}{(To != PedHandle.Invalid ? $" to: {To}" : "")})";
	}
}
