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

		public static uint FrameCount = 0;
		public static Stopwatch TotalTime = new Stopwatch();

		/// <summary>	Request hash from GTA5 internals. </summary>
		public static uint GenerateHash(string s) => MemoryAccess.GetHashKey(s);

		public static VersionNum GameVersion { get; internal set; }

		/// <summary> Number of milliseconds since the game launched. </summary>
		public static uint GameTime { get; internal set; } = 0;

		/// <summary> Value of GameTime at the start of the previous frame. </summary>
		public static uint LastGameTime = GameTime;

		/// <summary> A moving average (over 10 samples) of the current framerate. </summary>
		public static float CurrentFPS { get; internal set; } = 0f;

		/// <summary> Frames per second computed from the previous frame. </summary>
		public static float InstantFPS => (float)(1000f / Math.Max(1, GameTime - LastGameTime));

		/// <summary>
		/// Pauses the game. Does not show the Pause menu.
		/// Script OnTick functions continue to be called.
		/// </summary>
		public static bool GamePaused {
			get => gamePaused;
			set => Call(SET_GAME_PAUSED, gamePaused = value);
		}
		private static bool gamePaused = false;


		/// <summary> The PlayerHandle of the current Player. </summary>
		public static PlayerHandle CurrentPlayer { get; internal set; } = PlayerHandle.Invalid;

		/// <summary> The position and orientation of the current player. Updated once per frame. </summary>
		public static Matrix4x4 PlayerMatrix { get; internal set; }

		/// <summary> The position of the current player. Updated once per frame. </summary>
		public static Vector3 PlayerPosition { get; internal set; } = Vector3.Zero;

		/// <summary> The current vehicle, if the player is in one. Updated once per frame. </summary>
		public static VehicleHandle PlayerVehicle { get; internal set; } = VehicleHandle.Invalid;

		/// <summary> The position and orientation of the Gameplay Camera. Updated once per frame. </summary>
		public static Matrix4x4 CameraMatrix { get; internal set; }

		/// <summary> Log to Console and to the "Shiv.log" file. </summary>
		public static void Log(string s) => Shiv.Log(s);

	}
}

