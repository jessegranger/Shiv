using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using GTA.Native;
using static GTA.Native.Function;
using static GTA.Native.Hash;
using System.Threading.Tasks;
using System.Threading;

namespace Shiv {


	public static partial class Global {

		public static Stopwatch TotalTime = new Stopwatch();

		/// <summary>
		/// Ask GTA engine to hash a string.
		/// </summary>
		/// <param name="s">string to hash</param>
		/// <returns>a hash code</returns>
		public static uint GenerateHash(string s) => MemoryAccess.GetHashKey(s);

		public static VersionNum GameVersion { get; internal set; }

		/// <summary> Number of milliseconds since the game launched. </summary>
		public static uint GameTime { get; internal set; } = 0;

		public static bool GamePaused { get; private set; } = false;
		/// <summary>
		/// Pauses the game. Does not show the Pause menu.
		/// Script OnTick functions continue to be called.
		/// </summary>
		public static void TogglePause() => Call(SET_GAME_PAUSED, GamePaused = !GamePaused);

		/// <summary> A moving average (over 10 samples) of the current framerate. </summary>
		public static float CurrentFPS { get; internal set; } = 0f;

		/// <summary>
		/// The PlayerHandle of the current Player.
		/// </summary>
		public static PlayerHandle CurrentPlayer { get; internal set; } = PlayerHandle.Invalid;

		/// <summary>
		/// The position and orientation of the current player. Updated once per frame.
		/// </summary>
		public static Matrix4x4 PlayerMatrix { get; internal set; }

		/// <summary>
		/// The position of the current player. Updated once per frame.
		/// </summary>
		public static Vector3 PlayerPosition { get; internal set; } = Vector3.Zero;

		/// <summary>
		/// The current vehicle, if the player is in one. Updated once per frame.
		/// </summary>
		public static VehicleHandle PlayerVehicle { get; internal set; } = VehicleHandle.Invalid;

		/// <summary>
		///  The position and orientation of the Gameplay Camera. Updated once per frame.
		/// </summary>
		public static Matrix4x4 CameraMatrix { get; internal set; }

	}
}

