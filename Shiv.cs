using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using static GTA.Native.Function;
using static GTA.Native.Hash;
using static Shiv.Global;
using static Shiv.Imports;
using Keys = System.Windows.Forms.Keys;

namespace Shiv {


	/// <summary>
	/// Shiv is the special root class, searched for by the Loader (Shiv.asi).
	/// </summary>
	/// <remarks>
	/// The library loading order is something like:
	/// - GTA game dll
	/// - dinput8.dll (bootstraps the hook dll)
	/// - ScriptHookV.dll (does the real hooking of the engine)
	/// - Shiv.asi (sets up a CLR environ)
	/// - Main.shiv (this project, class Shiv)
	/// </remarks>
	public static class Shiv {
		private static int[] GetAllObjects(int max = 512) {
			int[] ret;
			unsafe {
				fixed ( int* buf = new int[max] ) {
					int count = WorldGetAllObjects(buf, max);
					ret = new int[count];
					Marshal.Copy(new IntPtr(buf), ret, 0, count);
				}
			}
			return ret;
		}
		private static readonly Action RefreshObjects = Throttle(304, () => {
			NearbyObjects = GetAllObjects()
				.Cast<EntHandle>()
				.Where(Exists)
				.OrderBy(DistanceToSelf)
				.ToArray();
		});

		private static int[] GetAllPeds(int max = 512) {
			int[] ret;
			unsafe {
				fixed ( int* buf = new int[max] ) {
					int count = WorldGetAllPeds(buf, max);
					ret = new int[count];
					Marshal.Copy(new IntPtr(buf), ret, 0, count);
				}
			}
			return ret;
		}
		private static readonly Action RefreshHumans = Throttle(201, () => {
			NearbyHumans = GetAllPeds().Cast<PedHandle>()
				.Where(h => h != Self && IsAlive(h) && IsHuman(h))
				.OrderBy(DistanceToSelf)
				.ToArray();
		});

		private static int[] GetAllPickups(uint max = 512) {
			int[] ret;
			unsafe {
				fixed ( int* buf = new int[max] ) {
					int count = WorldGetAllPickups(buf, (int)max);
					ret = new int[count];
					Marshal.Copy(new IntPtr(buf), ret, 0, count);
				}
			}
			return ret;
		}
		private static readonly Action RefreshPickups = Throttle(1003, () => {
			NearbyPickups = GetAllPickups()
				.Cast<EntHandle>()
				.Where(Exists)
				.OrderBy(DistanceToSelf)
				.ToArray();
		});

		private static int[] GetAllVehicles(uint max = 512) {
			if( max == 0 ) {
				throw new ArgumentException();
			}
			int[] ret;
			unsafe {
				fixed ( int* buf = new int[max] ) {
					int count = WorldGetAllVehicles(buf, (int)max);
					ret = new int[count];
					Marshal.Copy(new IntPtr(buf), ret, 0, count);
				}
			}
			return ret;
		}
		private static readonly Action RefreshVehicles = Throttle(302, () => {
			NearbyVehicles = GetAllVehicles().Cast<VehicleHandle>()
				.Where(v => v != PlayerVehicle && Exists(v))
				.OrderBy(DistanceToSelf)
				.ToArray();
		});

		private static TextWriter LogFile;
		public static void Log(params string[] strings) {
			string msg = $"{TotalTime.Elapsed} " + string.Join(" ", strings);
			if( LogFile != null ) {
				try {
					LogFile.WriteLine(msg);
					LogFile.Flush();
				} catch( ObjectDisposedException ) {
					LogFile = null;
				} catch( IOException ) {
					LogFile = null;
				}
			}
			Console.Log(msg);
		}

		private static void CreateScript(Type t) {
			if( (!t.IsAbstract) && t.IsSubclassOf(typeof(Script)) ) {
				Log($"CreateScript creating class: {t.Name}");
				try {
					Activator.CreateInstance(t);
				} catch( MissingMethodException ) {
					Log($"Ignoring class with no default constructor: {t.Name}");
				}
			}
		}

		private static readonly Random random = new Random();

		/// <summary>
		/// Called by the Loader, once when the library is loaded.
		/// </summary>
		/// <param name="logFile">Where to write log output</param>
		public static void OnInit(TextWriter logFile) {
			LogFile = logFile;
			TotalTime.Start();
			GameVersion = (VersionNum)GetGameVersion();
			Log($"Initializing game version {GameVersion}...");
			// Create all the scripts first so they can order themselves, using their DependsOn attribute
			Assembly.GetExecutingAssembly().GetTypes().Each(CreateScript);
			Log("Script Order: " + string.Join(" ", Script.Order.Select(s => s.GetType().Name.Split('.').Last())));
			// Then use OnInit() on each of them in their preferred order.
			// Loop using manual LinkedList methods so we can use RemoveAndContinue
			LinkedListNode<Script> cur = Script.Order.First;
			while( cur != null && cur.Value != null ) {
				try {
					cur.Value.OnInit();
					cur = cur.Next;
				} catch( Exception err ) {
					Log($"Failed to init {cur.Value.GetType().Name}: {err.Message}");
					Log(err.StackTrace);
					cur = Script.Order.RemoveAndContinue(cur);
				}
			}

		}

		private static MovingAverage fps = new MovingAverage(60);

		public static void OnTick() {
			var w = new Stopwatch();
			w.Start();
			FrameCount += 1;
			try {

				GameTime = Call<uint>(GET_GAME_TIMER);

				if( CurrentPlayer == PlayerHandle.Invalid ) {
					CurrentPlayer = Call<PlayerHandle>(GET_PLAYER_INDEX);
				}
				Self = Call<PedHandle>(GET_PLAYER_PED, CurrentPlayer);
				CameraMatrix = Matrix(GameplayCam.Handle);
				PlayerMatrix = Matrix(Self);
				PlayerPosition = Position(PlayerMatrix);
				PlayerVehicle = CurrentVehicle(Self);

				// throttled state refreshers
				RefreshHumans(); // scripts should use the static NearbyHumans, NearbyVehicles, etc
				RefreshVehicles();
				RefreshObjects();
				RefreshPickups();

				int dt = (int)GameTime - (int)LastGameTime;
				LastGameTime = GameTime;
				if( dt != 0 ) {
					fps.Add(1000 / dt);
				}
				CurrentFPS = fps.Value;

				// run any actions in response to key strokes
				Controls.OnTick();

				// run all Script instances in order
				Script.Order.Visit(s => s.OnTick());

			} catch( Exception err ) {
				Log($"Uncaught error in Main.OnTick: {err.Message} {err.StackTrace}");
				OnAbort();
			} finally {
				w.Stop();
			}
		}

		public static void OnKey(ulong key, ushort repeats, byte scanCode, bool wasDownBefore, bool isUpNow, bool ctrl, bool shift, bool alt) {
			Keys k = (Keys)(int)key
				| (shift ? Keys.Shift : 0)
				| (ctrl ? Keys.Control : 0)
				| (alt ? Keys.Alt : 0);
			Controls.Enqueue(k, wasDownBefore, isUpNow);
		}

		public static void OnAbort() {
			Log("Main: OnAbort()");
			// signal all scripts to shutdown cleanly
			Script.Order.Visit(s => {
				s.OnAbort();
				return true; // tell Visit to remove this from Script.Order
			});
			Log("OnAbort Finished.");
		}

	}
}

