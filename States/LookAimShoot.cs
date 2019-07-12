using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;
using static Shiv.Global;
using System.Diagnostics;

namespace Shiv {
	class AimAt : LookAt {

		public AimAt(Vector3 target, State next = null) : base(target, next) { }
		public AimAt(Func<Vector3> target, State next = null) : base(target, next) { }

		public AimAt(PedHandle ped, State next = null) : this(() => GetAimPosition(ped), next) { }
		public AimAt(Func<PedHandle> target, State next = null) : base(() => GetAimPosition(target()), next) { }

		public static Vector3 GetAimPosition(PedHandle ped) => HeadPosition(ped) + Velocity(ped) / CurrentFPS;
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
			if( Exists(target) && IsAlive(target) ) {
				if( IsAimingAtEntity(target) ) {
					SetControlValue(0, Control.Attack, 1f);
				}
			} else {
				return Next;
			}
			return base.OnTick();
		}
	}

	class LookAt : State {

		public LookAt(Vector3 target, State next = null) : this(() => target, next) { }
		public LookAt(Func<Vector3> target, State next = null):base(next) => Target = target;

		public LookAt(PedHandle ped, State next = null) : this(() => HeadPosition(ped), next) { }
		public LookAt(Func<PedHandle> target, State next = null) : this(() => HeadPosition(target()), next) { }

		protected readonly Func<Vector3> Target;
		public uint Duration = uint.MaxValue;
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
				LookToward(target);
				return this;
			}
			timer.Stop();
			return this;
		}
	}
}
