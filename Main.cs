using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Numerics;
using GTA.Native;
using static Shiv.Globals;
using static Shiv.Imports;
using static GTA.Native.Function;
using static GTA.Native.Hash;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace Shiv {


	public static partial class Globals {

		public static uint GenerateHash(string s) => MemoryAccess.GetHashKey(s);

		public static VersionNum GameVersion { get; internal set; }

		/// <summary> Number of milliseconds since the game launched. </summary>
		public static uint GameTime { get; internal set; } = 0;

		public static PlayerHandle CurrentPlayer { get; internal set; } = PlayerHandle.Invalid;
		public static Matrix4x4 PlayerMatrix { get; internal set; }
		public static Vector3 PlayerPosition { get; internal set; } = Vector3.Zero;
		public static VehicleHandle PlayerVehicle { get; internal set; } = VehicleHandle.Invalid;
		public static IntPtr CameraAddress { get; internal set; } = IntPtr.Zero;
		public static Matrix4x4 CameraMatrix { get; internal set; }

		public class MovingAverage {
			public float Value = 0f;
			public int Period;
			public MovingAverage(int period) { Period = period; }
			public void Add(float sample) {
				Value = ((Value * (Period - 1)) + sample) / Period;
			}
		}

		public static Action Throttle(int ms, Action func) {
			var s = new Stopwatch();
			s.Start();
			return () => {
				if( s.ElapsedMilliseconds >= ms ) {
					func();
					s.Restart();
				}
			};
		}

		internal static Dictionary<Keys, Action> keyBindings = new Dictionary<Keys, Action>();
		public static void KeyBind(Keys key, Action action) {
			keyBindings[key] = keyBindings.TryGetValue(key, out Action curr) ? (() => { curr(); action(); }) : action;
		}

	}

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
		private static Stopwatch TotalTime = new Stopwatch();
		public static void Log(params string[] strings) {
			if( LogFile != null ) {
				LogFile.WriteLine($"{TotalTime.Elapsed} " + String.Join(" ", strings));
				LogFile.Flush();
			}
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

		public static void OnInit(TextWriter logFile) {
			LogFile = logFile;
			TotalTime.Start();
			Log("OnInit()");
			GameVersion = (VersionNum)GetGameVersion();
			Log($"OnInit() GameVersion: {GameVersion}");
			// Create all the scripts first so they can order themselves
			Assembly.GetExecutingAssembly().GetTypes().Each(CreateScript);
			// Directory.GetFiles("scripts", "*.script.dll", SearchOption.AllDirectories) .Each(path => Assembly.LoadFile(path).GetTypes().Each(CreateScript));
			// Then use OnInit() on them in their preferred order.
			var dead = new List<Script>();
			foreach( Script script in Script.Order ) {
				try {
					script.OnInit();
				} catch( Exception err ) {
					Log($"Failed to init {script.GetType().Name}: {err.Message} {err.StackTrace}");
					dead.Add(script);
				}
			}
			foreach( var script in dead ) Script.Order.Remove(script);

			KeyBind(Keys.N, () => {
				new QuickScript(1000, () => {
					return CreateVehicle(VehicleHash.Ninef, PlayerPosition + (Forward(Self) * 4f), 0f) != VehicleHandle.ModelLoading;
				});
				// Sphere.Add(PlayerPosition, .05f, Color.Yellow, 5000);
			});
		}

		public static uint LastGameTime = GameTime;
		public static uint FrameCount = 0;

		public static MovingAverage fps = new MovingAverage(60);

		public struct KeyEvent {
			public Keys key;
			public bool downBefore;
			public bool upNow;
		}
		public static ConcurrentQueue<KeyEvent> keyEvents = new ConcurrentQueue<KeyEvent>();

		public static void OnTick() {
			var w = new Stopwatch();
			w.Start();
			FrameCount += 1;
			try {

				MatrixCache.Clear();

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
				if( dt != 0 ) fps.Add(1000 / dt);
				UI.DrawText($"Humans: {NearbyHumans.Length} Vehicles: {NearbyVehicles.Length}");
				float eff = MatrixCacheHits / (MatrixCacheMiss + 1);
				UI.DrawText($"FPS:{fps.Value:F2} Cache:{eff:F2} Position: {Round(PlayerPosition, 2)}");

				// run any actions in response to key strokes
				while( keyEvents.TryDequeue(out KeyEvent evt) ) {
					if( (!evt.downBefore) && keyBindings.TryGetValue(evt.key, out Action action) ) {
						try {
							action();
						} catch( Exception err ) {
							Log($"OnKey({evt.key}) exception from key-binding: {err.Message} {err.StackTrace}");
							keyBindings.Remove(evt.key);
						}
					} else {
						Script.Order.Visit(s => s.OnKey(evt.key, evt.downBefore, evt.upNow));
					}
				}
				Script.Order.Visit(s => s.OnTick());
			} catch( Exception err ) {
				Log($"Uncaught error in Main.OnTick: {err.Message} {err.StackTrace}");
				OnAbort();
			} finally {
				w.Stop();
			}
		}

		public static void OnKey(ulong key, ushort repeats, byte scanCode, bool wasDownBefore, bool isUpNow, bool ctrl, bool shift, bool alt) {
			// Log($"Shiv::OnKey {key} {wasDownBefore} {isUpNow} {shift} {ctrl} {alt}");
			Keys k = (Keys)(int)key
				| (shift ? Keys.Shift : 0)
				| (ctrl ? Keys.Control : 0)
				| (alt ? Keys.Alt : 0);
			keyEvents.Enqueue(new KeyEvent() { key = k, downBefore = wasDownBefore, upNow = isUpNow });
		}

		public static void OnAbort() {
			Log("Main: OnAbort()");
			// signal all scripts to shutdown cleanly
			Script.Order.Visit(s => {
				s.OnAbort(); 
				return true; // tell Visit to remove this from .Order
			});
			Log("OnAbort Finished.");
		}

	}
}
