using System.Collections.Generic;
using System.Drawing;
using static GTA.Native.Function;
using static GTA.Native.Hash;
using static Shiv.Globals;

namespace Shiv {
	public static partial class Globals {
		public static SenseSet Senses = new SenseSet();
	}

	public class SenseSet {
		private List<Sense> senses = new List<Sense>();
		public void Add(Sense s) => senses.Add(s);
		public void OnTick() => senses.Each(s => s.OnTick());
	}
	public abstract class Sense {
		public abstract void OnTick();
	}

	public class SenseScript : Script {
		public override void OnInit() {
			Senses.Add(new DangerSense());
		}
		public override void OnTick() {
			Senses.OnTick();
		}
	}

	public class DangerSense : Sense {
		public override void OnTick() {
			foreach( PedHandle ped in NearbyHumans ) {
				BlipHandle blip = Call<BlipHandle>(GET_BLIP_FROM_ENTITY, ped);
				if( blip != BlipHandle.Invalid ) {
					if( GetColor(blip) == BlipColor.Red ) {
						DrawSphere(Position(ped) + Up, .1f, Color.Red);
					}
				}
			}
		}
	}
}
