using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;
using static Shiv.Global;
using System.Diagnostics;
using System.Drawing;

namespace Shiv {
	class AimAt : LookAt {

		public AimAt(Vector3 target, State next = null) : base(target, next) { }
		public AimAt(Func<Vector3> target, State next = null) : base(target, next) { }

		public AimAt(PedHandle ped, State next = null) : this(() => GetAimPosition(ped), next) { }
		public AimAt(Func<PedHandle> target, State next = null) : base(() => GetAimPosition(target()), next) { }

		public static Vector3 GetAimPosition(PedHandle ped) {
			var pos = HeadPosition(ped) + Velocity(ped) / CurrentFPS;
			UI.DrawTextInWorld(pos, "X");
			// DrawSphere(pos, .1f, Color.Yellow);
			return pos;
		}
		public override State OnTick() {
			ForcedAim(CurrentPlayer, IsFacing(CameraMatrix, Target()));
			return base.OnTick();
		}

	}

	class ShootAt : AimAt {
		readonly Func<PedHandle> GetPed;

		public ShootAt(PedHandle ped, State next = null) : this(() => ped, next) { }
		public ShootAt(Func<PedHandle> func, State next = null) : base(func, next) => GetPed = func;

		public override State OnTick() {
			PedHandle target = GetPed();
			base.OnTick();
			if( Exists(target) && IsAlive(target) ) {
				if( IsAimingAtEntity(target) ) {
					SetControlValue(0, Control.Attack, 1f);
				}
				return this;
			} else {
				return Next;
			}
		}
	}

	class LookAtPed : State {
		public LookAtPed(Func<PedHandle> ped, State next) : base(next) => GetPed = ped;
		public LookAtPed(PedHandle ped, State next): this(() => ped, next) { }

		readonly Func<PedHandle> GetPed;
		private Stopwatch sw = new Stopwatch();
		public long Duration = long.MaxValue;

		public override State OnTick() {
			if( GamePaused ) {
				return this;
			}
			if( !CanControlCharacter() ) {
				return Next;
			}
			PedHandle ped = GetPed();
			if( ped == PedHandle.Invalid ) {
				return this;
			}
			if( ! sw.IsRunning ) {
				sw.Start();
			}
			if( sw.ElapsedMilliseconds > Duration ) {
				sw.Stop();
				return Next;
			}
			LookToward(GetPed(), deadZone:4f);
			return this;
		}
	}

	class LookAt : State {

		public LookAt(Vector3 target, State next = null) : this(() => target, next) { }
		public LookAt(Func<Vector3> target, State next = null):base(next) => Target = target;

		protected readonly Func<Vector3> Target;
		public long Duration = long.MaxValue;
		private Stopwatch timer = new Stopwatch();
		public override State OnTick() {
			if( GamePaused ) {
				return this;
			}
			if( !CanControlCharacter() ) {
				return Next;
			}
			Vector3 target = Target();
			if( target != Vector3.Zero ) {
				if( ! timer.IsRunning ) {
					timer.Start();
				}
				if( timer.ElapsedMilliseconds > Duration ) {
					return Next;
				}
				LookToward(target, deadZone:4f);
				return this;
			}
			timer.Stop();
			return this;
		}
	}
}
