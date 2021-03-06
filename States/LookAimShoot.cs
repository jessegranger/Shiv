﻿using System;
using System.Diagnostics;
using System.Numerics;
using static Shiv.Global;

namespace Shiv {
	class AimAt : LookAt {

		/// <summary> Aim at a single fixed position. </summary>
		public AimAt(Vector3 target, State next = null) : base(target, next) => DeadZone = .05f;

		/// <summary> Aim at a series of fixed positions. </summary>
		public AimAt(Func<Vector3> target, State next = null) : base(target, next) => DeadZone = .05f;

		/// <summary> Aim at a single Ped. </summary>
		public AimAt(PedHandle ped, State next = null) : this(() => GetAimPosition(ped), next) { }

		/// <summary> Aim at a series of Peds. </summary>
		public AimAt(Func<PedHandle> target, State next = null) : base(() => GetAimPosition(target()), next) { }

		public static Vector3 GetAimPosition(PedHandle ped) {
			double dist = Math.Sqrt(DistanceToSelf(ped));
			Vector3 pos = HeadPosition(ped);
			float leadFactor = Clamp((float)Math.Sqrt(dist), 1f, 8f);
			pos = pos + (Velocity(ped) * leadFactor / InstantFPS);
			pos.Z -= .02f; // closer to the neck, a cautious bias
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
			if( Exists(target) && IsAlive(target) ) {
				base.OnTick();
				if( IsAimingAtEntity(target) || AimEntity() == (EntHandle)target ) {
					SetControlValue(0, Control.Attack, 1f);
				}
				return this;
			} else {
				return Next;
			}
		}
	}

	class LookAtPed : PlayerState {
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

	class LookAt : PlayerState {
		protected readonly Func<Vector3> Target;
		protected float DeadZone = 4f;

		public LookAt(Vector3 target, State next = null) : this(() => target, next) { }
		public LookAt(Func<Vector3> target, State next = null):base(next) => Target = target;

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
				LookToward(target, DeadZone);
				return this;
			}
			timer.Stop();
			return this;
		}
	}
}
