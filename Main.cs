using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using GTA.Native;
using static GTA.Native.Function;
using static GTA.Native.Hash;
using static Shiv.Globals;
using static Shiv.Imports;
using Keys = System.Windows.Forms.Keys;

namespace Shiv {


	public static partial class Globals {

		public static uint GenerateHash(string s) => MemoryAccess.GetHashKey(s);

		public static VersionNum GameVersion { get; internal set; }

		/// <summary> Number of milliseconds since the game launched. </summary>
		public static uint GameTime { get; internal set; } = 0;

		public static float CurrentFPS { get; internal set; } = 0f;

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

		public static void Debug(params string[] args) {
			Shiv.Log(args);
		}

		public static Vector3 PathTarget = Vector3.Zero;

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
				MenuScript.Show(new Menu(.4f, .4f, .2f)
					.Item("Goals", new Menu(.4f, .4f, .2f)
						.Item("SmokeAtHome", () => Goals.Push(new SmokeWeedAtHome()))
					)
					.Item("Press Key", new Menu(.4f, .4f, .2f)
						.Item("Context", () => PressControl(2, Control.Context, 200))
						.Item("ScriptRUp", () => PressControl(2, Control.ScriptRUp, 200))
						.Item("ScriptRLeft", () => PressControl(2, Control.ScriptRLeft, 200))
					).Item("Cancel", () => MenuScript.Hide()));
			});
			KeyBind(Keys.I, () => {
				PathTarget = PutOnGround(AimPosition(), 1f);
				Sphere.Add(PathTarget, .1f, Color.Blue, 10000);
			});
			KeyBind(Keys.J, () => {
				Goals.Push(new WalkTo(PathTarget));
			});
			KeyBind(Keys.O, () => {
				if( CurrentVehicle(Self) != 0 ) {
					Sphere.Add(AimPosition(), .2f, Color.Blue, 10000);
					Goals.Push(new DirectDrive(AimPosition()) { StoppingRange = 2f });
				} else {
					Goals.Push(new DirectMove(AimPosition()));
				}
			});
			KeyBind(Keys.End, () => {
				Goals.Clear();
				TaskClearAll();
				LookTarget = Vector3.Zero;
				PathTarget = Vector3.Zero;
				pathResult = Enumerable.Empty<Vector3>();
			});
			KeyBind(Keys.X, () => {
				Delete(NearbyObjects[0]);
			});

		}

		private static IEnumerable<Vector3> pathResult = Enumerable.Empty<Vector3>();

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
					fps.Add( CurrentFPS = 1000 / dt);
				}
				UI.DrawText($"Humans: {NearbyHumans.Length} Vehicles: {NearbyVehicles.Length}");
				UI.DrawText($"FPS:{fps.Value:F2} Position: {Round(PlayerPosition, 2)}");

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

				foreach( var n in NearbyObjects.Take(10) ) {
					UI.DrawTextInWorld(Position(n), $"{n}");
				}

				/*
				if( NearbyObjects.Length > 0 ) {
					var m = Matrix(NearbyObjects[0]);
					var model = GetModel(NearbyObjects[0]);
					UI.DrawTextInWorld(Position(m), $"{model:X}");
					GetModelDimensions(model, out Vector3 backLeft, out Vector3 frontRight);
					UI.DrawText($"{Round(backLeft, 2)} {Round(frontRight, 2)}");
					//frontRight.Z /= 2;
					if( true || Math.Max(
						Math.Abs(backLeft.X - frontRight.X),
						Math.Max(
							Math.Abs(backLeft.Y - frontRight.Y),
							Math.Abs(backLeft.Z - frontRight.Z)
						)) > .00f )
					{
						backLeft.Z = 0;
						var unique = new HashSet<NodeHandle>();
						foreach( var n in NavMesh.GetAllHandlesInBox(m, backLeft, frontRight) ) {
							if( !unique.Contains(n) ) {
								unique.Add(n);
								UI.DrawTextInWorld(NavMesh.Position(n), "X");
							}
							// DrawSphere(NavMesh.Position(n), .1f, Color.Red);
						}
					}
				}
				*/

				if( PathTarget != Vector3.Zero ) {
					pathResult = Pathfinder.FindPath(PlayerPosition, PathTarget);
					if( pathResult != null ) {
						foreach( var v in pathResult ) {
							DrawSphere(v, .1f, Color.Blue);
						}
					}
				}

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

