using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Shiv.Global;
using System.Numerics;
using static GTA.Native.Hash;
using static GTA.Native.Function;
using static System.Math;
using System.Drawing;

namespace Shiv {

	class Hover : State {
		public Vector3 Location;
		public uint Duration = uint.MaxValue;
		public float MaxSpeed = 5f;
		private uint Started = 0;
		public Hover(State next = null) : base(next) { }
		public Hover(Vector3 pos, State next = null) : base(next) => Location = pos;
		public override State OnTick() {
			if( PlayerVehicle == VehicleHandle.Invalid ) {
				return Fail;
			}
			if( Started == 0 ) {
				Started = GameTime;
			}
			if( (GameTime - Started) > Duration ) {
				return Next;
			}
			DrawSphere(Location, .5f, Color.Yellow);

			float pitch = 0f, yaw = 0f, roll = 0f, throttle = 0f;

			// exactly counter any current roll, to stabilize
			roll = Call<float>(GET_ENTITY_ROLL, PlayerVehicle);
			pitch = Call<float>(GET_ENTITY_PITCH, PlayerVehicle);
			SetControlValue(0, Control.VehicleFlyRollLeftRight, roll / 10f);

			Vector3 Vh = Velocity(PlayerVehicle);

			// lift up if too low
			if( PlayerPosition.Z < Location.Z && Vh.Z <= MaxSpeed ) {
				SetControlValue(0, Control.VehicleFlyThrottleUp, 1f);
				return this;
			}
			// sink down if too high
			if( PlayerPosition.Z > (Location.Z * 1.3f) && Vh.Z > 0f ) {
				SetControlValue(0, Control.VehicleFlyThrottleDown, 1f);
				return this;
			}
			Matrix4x4 Mh = Matrix(PlayerVehicle);
			Vector3 Ph = Position(Mh);
			var dist = Sqrt(Pow(Location.X - Ph.X, 2) + Pow(Location.Y - Ph.Y, 2));
			if( dist > 1f ) {
				// Vector3 Fh = Forward(Mh);
				// float Hh = Heading(Mh);
				YawToward(Mh, Heading(Location - Ph));

				pitch = ((Speed(Self) / (MaxSpeed * 3f)) - 1f) / 2f;
				SetControlValue(0, Control.VehicleFlyPitchUpDown, pitch);
			}
			throttle = -Vh.Z;
			SetControlValue(0, Control.VehicleFlyThrottleUp, throttle);
			UI.DrawHeadline($"Speed:{Speed(Self):F2} Roll:{roll:F2} Pitch:{Call<float>(GET_ENTITY_PITCH, PlayerVehicle):F2}");

			return this;

		}
	}

}
