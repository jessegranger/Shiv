﻿using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using System.Linq;
using static GTA.Native.Function;
using static GTA.Native.Hash;
using static Shiv.Global;
using static Shiv.NavMesh;
using System.Threading.Tasks;

namespace Shiv {
	public static partial class Global {
		public static SenseSet Senses = new SenseSet();
	}

	public class SenseSet {
		private List<Sense> senses = new List<Sense>();
		public SenseSet Add(Sense s) {
			senses.Add(s);
			return s.Set = this;
		}
		public SenseSet Remove(Sense s) {
			if( s.Set == this ) {
				s.Set = null;
				senses.Remove(s);
			}
			return this;
		}

		public void OnTick() => senses.Each(s => s.OnTick());
	}
	public abstract class Sense {
		public SenseSet Set;
		public abstract void OnTick();
		public void Done() {
			if (Set != null) {
				Set.Remove(this);
			}
		}
	}

	public class SenseScript : Script {
		public override void OnInit() {
			Senses.Add(new DangerSense())
			.Add(new BlipSense());
			// .Add(new CoverSense());
		}
		public override void OnTick() => Senses.OnTick();
	}

	public class DangerSense : Sense {
		public static PedHandle[] NearbyDanger = new PedHandle[0];

		public static bool LooksDangerous(PedHandle ped) => GetColor(GetBlip(ped)) == Color.Red;
		public override void OnTick() {
			NearbyDanger = NearbyHumans().Where(LooksDangerous).ToArray();
			// NearbyHumans.Each(ped => { DrawSphere(HeadPosition(ped), .1f, Color.Beige); });
			foreach( PedHandle ped in NearbyDanger ) {
				UI.DrawTextInWorld(Position(ped), $"Danger: {GetModel(ped)}");
			}
		}
	}

	public class CoverSense : Sense {
		private static readonly NodeHandle[] Empty = new NodeHandle[0];
		public static NodeHandle[] NearbyCover = Empty;
		private bool IsStopped(Task t) => (t.IsCanceled || t.IsFaulted || t.IsCompleted);
		public CoverSense() => Log("CoverSense: Starting...");
		public override void OnTick() {
			UI.DrawText($"Cover: {NearbyCover.Length}");
			int i = 0;
			foreach( NodeHandle n in NearbyCover.Take(20) ) {
				DrawSphere(Position(n), .1f, Color.Blue);
				UI.DrawTextInWorld(Position(n) + (Up * i/55), $"{i}:{n}");
				i++;
			}
		}
	}

	public class BlipSense : Sense {
		public static bool ShowBlips = true;
		public override void OnTick() {
			int blipCount = 0;
			string msg = "";
			foreach( BlipHandle blip in GetAllBlips(BlipSprite.Standard)) {
				blipCount += 1;
				if( ShowBlips ) {
					Vector3 pos = Position(blip);
					BlipHUDColor blipColor = GetBlipHUDColor(blip);
					msg += $"{blipColor} ";
					Color color = GetColor(blipColor);
					DrawSphere(pos + Up, .3f, color);
				}
			}
			foreach( var blip in GetAllBlips(BlipSprite.Safehouse) ) {
				UI.DrawText($"Safehouse: {DistanceToSelf(Position(blip))}");
			}
			Items(BlipSprite.Standard, BlipSprite.PoliceOfficer).Each(s =>
				UI.DrawText($"Blips({s}): {string.Join(" ", GetAllBlips(s).Select(GetBlipColor))}")
			);
		}
	}

}
