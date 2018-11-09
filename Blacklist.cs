using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using static Shiv.Globals;

namespace Shiv {

	public class BlacklistScript : Script {
		internal static List<Blacklist> instances = new List<Blacklist>();
		public BlacklistScript() {
		}
		public override void OnTick() {
			foreach( Blacklist b in instances ) {
				b.RemoveExpired();
			}
		}
	}
	public class Blacklist : IDisposable {
		private Dictionary<int, uint> data = new Dictionary<int, uint>();
		public int Count => data.Count;
		public string Name;
		public Blacklist(string name) {
			Name = name;
			BlacklistScript.instances.Add(this);
		}
		~Blacklist() {
			Dispose();
		}
		public void Dispose() {
			if( BlacklistScript.instances != null ) BlacklistScript.instances.Remove(this);
			if( data != null ) data.Clear();
		}
		internal void RemoveExpired() {
			var done = data.Where(p => p.Value < GameTime).ToArray();
			foreach( var p in done ) data.Remove(p.Key);
			UI.DrawText($"Blacklist({Name}): {data.Count}");
		}
		public void Clear() => data.Clear();

		public bool Add(int p, uint dur, string reason = "none") {
			if( p != 0 ) data[p] = GameTime + dur;
			return false; // always false, blacklists are bad
		}
		public bool Add(PedHandle p, uint dur, string reason = "none") => Add((int)p, dur, reason);
		public bool Add(EntHandle p, uint dur, string reason = "none") => Add((int)p, dur, reason);
		public bool Add(NodeHandle p, uint dur, string reason = "none") => Add((int)p, dur, reason);
		public bool Add(VehicleHandle p, uint dur, string reason = "none") => Add((int)p, dur, reason);

		public void Remove(int handle) => data.Remove(handle);
		public void Remove(PedHandle handle) => data.Remove((int)handle);
		public void Remove(EntHandle handle) => data.Remove((int)handle);
		public void Remove(NodeHandle handle) => data.Remove((int)handle);
		public void Remove(VehicleHandle handle) => data.Remove((int)handle);

		public bool Contains(int p) => p != 0 && data.ContainsKey(p) && data[p] > GameTime;
		public bool Contains(PedHandle ent) => Contains((int)ent);
		public bool Contains(EntHandle ent) => Contains((int)ent);
		public bool Contains(NodeHandle ent) => Contains((int)ent);
		public bool Contains(VehicleHandle ent) => Contains((int)ent);
	}

}

