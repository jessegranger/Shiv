using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using static Shiv.Global;

namespace Shiv {

	/// <summary>
	/// Keeps the Blacklist instances up to date each frame.
	/// </summary>
	public class BlacklistScript : Script {
		public BlacklistScript() { }
		public override void OnTick() => Blacklist.RemoveAllExpired();
	}
	public class Blacklist : IDisposable {

		private static List<Blacklist> instances = new List<Blacklist>();

		private Dictionary<ulong, uint> data = new Dictionary<ulong, uint>();
		public int Count => data.Count;
		public string Name;
		public Blacklist(string name) {
			Name = name;
			instances.Add(this);
		}
		~Blacklist() { Dispose(); }
		public void Dispose() {
			if( instances != null ) {
				instances.Remove(this);
			}

			if( data != null ) {
				data.Clear();
			}
		}

		internal static void RemoveAllExpired() => instances.Each(b => b.RemoveExpired());

		/// <summary>
		/// Remove any items past their duration.
		/// </summary>
		private void RemoveExpired() { // TODO: could optimize this so there is an array sorted by expiration time, but keep the dict to answer Contains()
			var done = data.Where(p => p.Value < GameTime).ToArray();
			foreach( var p in done ) {
				data.Remove(p.Key);
			}

			UI.DrawText($"Blacklist({Name}): {data.Count}");
		}

		/// <summary>
		/// Remove all banned items.
		/// </summary>
		public void Clear() => data.Clear();

		/// <summary>
		/// Ban an ID for a duration in milliseconds.
		/// </summary>
		public void Add(ulong id, uint duration) => data[id] = GameTime + duration;
		public void Add(int id, uint duration) => data[(ulong)id] = GameTime + duration;

		/// <summary>
		/// Blacklist a Ped.
		/// </summary>
		public void Add(PedHandle ped, uint duration) => Add((ulong)ped, duration);
		
		/// <summary>
		/// Blacklist an Entity.
		/// </summary>
		public void Add(EntHandle ent, uint duration) => Add((ulong)ent, duration);

		/// <summary>
		/// Blacklist a NavMesh node.
		/// </summary>
		public void Add(NodeHandle node, uint duration) => Add((ulong)node, duration);

		/// <summary>
		/// Blacklist a Vehicle.
		/// </summary>
		public void Add(VehicleHandle veh, uint duration) => Add((ulong)veh, duration);

		public void Remove(ulong id) => data.Remove(id);
		public void Remove(int id) => data.Remove((ulong)id);
		public void Remove(PedHandle handle) => data.Remove((ulong)handle);
		public void Remove(EntHandle handle) => data.Remove((ulong)handle);
		public void Remove(NodeHandle handle) => data.Remove((ulong)handle);
		public void Remove(VehicleHandle handle) => data.Remove((ulong)handle);

		public bool Contains(ulong id) => id != 0 && data.ContainsKey(id) && data[id] > GameTime;
		public bool Contains(int id) => Contains((ulong)id);
		public bool Contains(PedHandle ent) => Contains((ulong)ent);
		public bool Contains(EntHandle ent) => Contains((ulong)ent);
		public bool Contains(NodeHandle ent) => Contains((ulong)ent);
		public bool Contains(VehicleHandle ent) => Contains((ulong)ent);

	}

}

