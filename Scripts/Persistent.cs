using System;
using System.Collections.Generic;
using System.Drawing;
using GTA.Native;
using static Shiv.Global;
using System.Numerics;
using System.Collections.Concurrent;
using System.Linq;

namespace Shiv {
	public abstract class Persistent : IDisposable {
		internal static readonly ConcurrentDictionary<Persistent, Persistent> instances = new ConcurrentDictionary<Persistent, Persistent>();
		public uint Remaining;
		public virtual void OnTick(uint dt) {
			if( dt > Remaining ) {
				Dispose();
			} else {
				Remaining = Remaining - dt;
			}
		}
		protected Persistent(uint dur=uint.MaxValue) {
			Remaining = dur;
			instances.TryAdd(this, this);
		}
		~Persistent() { Dispose(); }
		public void Dispose() => instances.TryRemove(this, out Persistent ignore);
	}
	public class Sphere : Persistent {
		public Vector3 Position;
		public float Radius;
		public Color color;
		private Sphere(Vector3 pos, float radius, Color color) : base(uint.MaxValue) { }
		private Sphere(Vector3 pos, float radius, Color color, uint dur):base(dur) {
			Position = pos;
			Radius = radius;
			this.color = color;
		}
		public override void OnTick(uint dt) {
			base.OnTick(dt);
			Function.Call(Hash.DRAW_MARKER, MarkerType.DebugSphere, 
				Position, Vector3.Zero, Vector3.Zero,
				new Vector3(Radius, Radius, Radius),
				color.R, color.G, color.B, color.A,
				false, false, 2, 0, 0, 0, 0);
		}
		public static void Add(Vector3 pos, float radius, Color color, uint duration) => new Sphere(pos, radius, color, duration);
	}
	public class Line : Persistent {
		public Vector3 Start;
		public Vector3 End;
		public Color color;
		private Line(Vector3 start, Vector3 end, Color color, uint dur) : base(dur) {
			Start = start;
			End = end;
			this.color = color;
		}
		public override void OnTick(uint dt) {
			base.OnTick(dt);
			Function.Call(Hash.DRAW_LINE, Start, End, color.R, color.G, color.B, color.A);
		}
		public static void Add(Vector3 start, Vector3 end, Color color, uint duration) => new Line(start, end, color, duration);
	}
	public class Text : Persistent {
		public Vector3 Position;
		public PointF Offset;
		public Color color;
		public string Value;
		private Text(Vector3 pos, string text, Color color) : this(pos, text, color, 0, 0, uint.MaxValue) { }
		private Text(Vector3 pos, string text, Color color, float dX, float dY, uint dur):base(dur) {
			Offset = new PointF(dX, dY);
			Position = pos;
			Value = text;
			this.color = color;
		}
		private Text(float x, float y, string text) : this(x, y, text, Color.White, uint.MaxValue) { }
		private Text(float x, float y, string text, Color color, uint dur):base(dur) {
			Offset = new PointF(x, y);
			Position = Vector3.Zero;
			Value = text;
			this.color = color;
		}
		public override void OnTick(uint dt) {
			base.OnTick(dt);
			if( Position != Vector3.Zero ) {
				UI.DrawTextInWorldWithOffset(Position, Offset.X, Offset.Y, Value);
			} else {
				UI.DrawText(Offset.X, Offset.Y, Value, .4f, 4, color);
			}
		}
		public static void Add(Vector3 pos, string text, uint duration, float dX=0, float dY=0) => new Text(pos, text, Color.White, dX, dY, duration);
	}
	
	public class PersistentScript : Script {
		public override void OnInit() => Log("Persistent::OnInit()");
		private uint lastTick = GameTime;
		public override void OnTick() {
			uint dt = GameTime - lastTick;
			lastTick = GameTime;
			foreach( Persistent p in Persistent.instances.Values.ToArray() ) {
				p.OnTick(dt);
			}
		}
		public override void OnAbort() {
			Persistent.instances.Clear();
			base.OnAbort();
		}
		public override string ToString() => $"<Persistent[{Persistent.instances.Count}]>";
	}
	
}

