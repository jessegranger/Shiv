using System;
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
using static Shiv.Global;
using static Shiv.Imports;
using Keys = System.Windows.Forms.Keys;
using Shiv.Missions;
using System.Threading.Tasks;
using System.Threading;

namespace Shiv {


	public static partial class Global {

		public static Stopwatch TotalTime = new Stopwatch();

		public static uint GenerateHash(string s) => MemoryAccess.GetHashKey(s);

		public static VersionNum GameVersion { get; internal set; }

		/// <summary> Number of milliseconds since the game launched. </summary>
		public static uint GameTime { get; internal set; } = 0;
		public static bool GamePaused { get; private set; } = false;
		public static void TogglePause() => Call(SET_GAME_PAUSED, GamePaused = !GamePaused);

		public static float CurrentFPS { get; internal set; } = 0f;

		public static PlayerHandle CurrentPlayer { get; internal set; } = PlayerHandle.Invalid;
		public static Matrix4x4 PlayerMatrix { get; internal set; }
		public static Vector3 PlayerPosition { get; internal set; } = Vector3.Zero;
		public static VehicleHandle PlayerVehicle { get; internal set; } = VehicleHandle.Invalid;
		public static IntPtr CameraAddress { get; internal set; } = IntPtr.Zero;
		public static Matrix4x4 CameraMatrix { get; internal set; }
		public static int SequenceProgress { get; internal set; } = 0;

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


		private static Random random = new Random();
		public static void OnInit(TextWriter logFile) {
			LogFile = logFile;
			TotalTime.Start();
			GameVersion = (VersionNum)GetGameVersion();
			Log($"Initializing game version {GameVersion}...");
			// Create all the scripts first so they can order themselves, using DependsOn attribute
			Assembly.GetExecutingAssembly().GetTypes().Each(CreateScript);
			// Then use OnInit() on them in their preferred order.
			Log("Script Order: " + string.Join(" ", Script.Order.Select(s => s.GetType().Name.Split('.').Last())));
			var cur = Script.Order.First;
			while( cur != null && cur.Value != null ) {
				var script = cur.Value;
				try {
					script.OnInit();
					cur = cur.Next;
				} catch( Exception err ) {
					Log($"Failed to init {script.GetType().Name}: {err.Message} {err.StackTrace}");
					cur = Script.Order.RemoveAndContinue(cur);
				}
			}

			Controls.Bind(Keys.Pause, () => TogglePause());
			Controls.Bind(Keys.N, () => {
				MenuScript.Show(new Menu(.4f, .4f, .2f)
					.Item("Goals", new Menu(.4f, .4f, .2f)
						.Item("Mission01", new Menu(.4f, .4f, .2f)
							.Item("Approach", () => Goals.Immediate(new Mission01() { CurrentPhase = Mission01.Phase.Approach }))
							.Item("Threaten", () => Goals.Immediate(new Mission01() { CurrentPhase = Mission01.Phase.Threaten }))
							.Item("GotoMoney", () => Goals.Immediate(new Mission01() { CurrentPhase = Mission01.Phase.GotoMoney }))
							.Item("GetMoney", () => Goals.Immediate(new Mission01() { CurrentPhase = Mission01.Phase.GetMoney }))
							.Item("WalkOut", () => Goals.Immediate(new Mission01() { CurrentPhase = Mission01.Phase.WalkOut }))
							.Item("MoveToCover", () => Goals.Immediate(new Mission01() { CurrentPhase = Mission01.Phase.MoveToCover }))
							.Item("KillAllCops", () => Goals.Immediate(new Mission01() { CurrentPhase = Mission01.Phase.KillAllCops }))
						)
						.Item("Wander", () => Goals.Immediate(new TaskWander()) )
						.Item("Explore", () => Goals.Immediate(new QuickGoal(() => {
							Goals.Immediate(new WalkTo(Position(NavMesh.LastGrown)));
							return GoalStatus.Active;
						})))
					)
					.Item("Show Path To", new Menu(.4f, .4f, .2f)
						.Item("Red Blip", () => Goals.Immediate(new DebugPath(Position(GetAllBlips().FirstOrDefault(b => GetColor(b) == Color.Red)))))
						.Item("Green Blip", () => Goals.Immediate(new DebugPath(Position(GetAllBlips().FirstOrDefault(b => GetColor(b) == Color.Green)))))
					)
					.Item("Drive To", new Menu(.4f, .4f, .2f)
						.Item("Yellow Blip", () => Goals.Immediate(new TaskDrive(Position(GetAllBlips().FirstOrDefault(b => GetColor(b) == Color.Yellow)))))
					)
					.Item("Save NavMesh", () => NavMesh.SaveToFile())
					.Item("Toggle Blips", () => BlipSense.ShowBlips = !BlipSense.ShowBlips)
					.Item("Press Key", new Menu(.4f, .4f, .2f)
						.Item("Context", () => PressControl(2, Control.Context, 200))
						.Item("ScriptRUp", () => PressControl(2, Control.ScriptRUp, 200))
						.Item("ScriptRLeft", () => PressControl(2, Control.ScriptRLeft, 200))
					).Item("Cancel", () => MenuScript.Hide()));
			});
			Controls.Bind(Keys.O, () => {
				if( CurrentVehicle(Self) != 0 ) {
					Sphere.Add(AimPosition(), .2f, Color.Blue, 10000);
					Goals.Immediate(new DirectDrive(AimPosition()) { StoppingRange = 2f });
				} else {
					Goals.Immediate(new DirectMove(AimPosition()));
				}
			});
			Controls.Bind(Keys.G, () => {

				var future = new Future<NodeHandle>(() => {
					return NavMesh.FirstOrDefault(PlayerNode, 10, (n) => !NavMesh.IsGrown(n));
				});
				Goals.Immediate(new QuickGoal("Find Growable", () => {
					if( future.IsFailed() ) {
						return GoalStatus.Failed;
					}
					if( future.IsReady() ) {
						NavMesh.Grow(future.GetResult(), 10);
						return GoalStatus.Complete;
					}
					return GoalStatus.Active;
				}));

				/*
				Goals.Immediate(new QuickGoal(() => {
					var pos = PutOnGround(AimPosition(), 1f);
					var head = HeadPosition(Self);
					// var dir = pos - head;
					// var dist = dir.Length();
					// pos = head + Vector3.Normalize(dir) * dist * .95f;
					var targetNode = GetHandle(pos);
					var nodePos = Position(targetNode);
					var money = Position(GetHandle(PutOnGround(Position(GetAllBlips().FirstOrDefault(b => GetBlipColor(b) == BlipColor.MissionGreen)), 1f)));
					DrawLine(PlayerPosition, money, Color.Purple);
					DrawSphere(nodePos, .07f, Color.LightGreen);
					DrawLine(head, nodePos, Color.LightGreen);
					NavMesh.IsGrown(targetNode, false);
					NavMesh.Grow(targetNode, 5, debug: true);
					return GoalStatus.Active;
				}));
				*/

				/*
				Goals.Immediate(new QuickGoal(() => {
					Goals.Immediate(new WalkTo(Position(NavMesh.LastGrown)));
					return GoalStatus.Active;
				}));
				*/

				/*
				var pos = PutOnGround(AimPosition(), 1f);
				var targetNode = GetHandle(pos);
				var startNode = PlayerNode;
				var blocked = Pathfinder.GetBlockedNodes(false, true, false);
				var future = new Future<IEnumerable<NodeHandle>>(() => Pathfinder.FindPath(startNode, targetNode, blocked, 10000, false));
				Goals.Immediate(new QuickGoal(() => {
					if( future.IsReady ) {
						foreach(var step in future.Result) {
							DrawSphere(Position(step), .06f, Color.Yellow);
						}
						foreach(var node in blocked) {
							var p = Position(node);
							if( DistanceToSelf(p) < 8f ) {
								DrawSphere(p, .05f, Color.Red);
							}
						}
					}
					return GoalStatus.Active;
				}));
				*/

				/*
				var money = PutOnGround(Position(GetAllBlips(BlipSprite.Standard).FirstOrDefault(b => GetBlipColor(b) == BlipColor.MissionGreen)), 1.5f);
				NavMesh.Grow(GetHandle(money), 5);
				var future = new Future<IEnumerable<NodeHandle>>();
				uint started = 0;
				var blocked = Pathfinder.GetBlockedNodes(false, true, false);
				Goals.Immediate(new QuickGoal(() => {
					if( started == 0 ) {
						started = GameTime;
						var startNode = PlayerNode;
						var targetNode = GetHandle(money);
						Log($"Starting future");
						ThreadPool.QueueUserWorkItem((object arg) => {
							Log("Starting work inside thread.");
							future.Resolve(Pathfinder.FindPath(startNode, targetNode, blocked, 10000, false));
							Log("Finished work inside thread.");
						});
					}
					if( future.IsFailed ) {
						return GoalStatus.Failed;
					}
					float chance = Clamp(200f / (blocked.Count + 1), 0f, 1f);
					foreach( var item in blocked ) {
						var pos = Position(item);
						if( DistanceToSelf(pos) < 8f ) {
							DrawSphere(Position(item), .05f, Color.Red);
						}
					}
					DrawLine(HeadPosition(Self), money, Color.Green);
					DrawLine(HeadPosition(Self), Position(GetHandle(money)), Color.Teal);
					DrawSphere(Position(GetHandle(money)), .05f, Color.Purple);
					UI.DrawText($"Money: {money} {GetHandle(money)} {Position(GetHandle(money))}");
					UI.DrawText(.5f, .6f, $"{blocked.Count} blocked in {GameTime - started}ms");
					UI.DrawText(.5f, .62f, $"{chance * 100}% chance of showing blocked node"); 
					if( future.IsReady ) {
						foreach( var step in future.Result ) {
							DrawSphere(Position(step), .06f, Color.Yellow);
						}
						if( future.Result.Count() == 0 ) {
							started = 0;
						}
						// var path = future.Result.Take(4);
						// if( FollowPath(path.Select(Position)) == MoveResult.Complete ) {
							// future.Result = future.Result.Skip(1);
						// }
						// return GoalStatus.Complete;
					}
					return GoalStatus.Active;
				}));
				*/

				// Goals.Immediate(new WalkTo(Position(NearbyHumans.FirstOrDefault())));
			});
			Controls.Bind(Keys.End, () => {
				Goals.Clear();
				TaskClearAll();
				AimTarget = Vector3.Zero;
				AimAtHead = PedHandle.Invalid;
				KillTarget = PedHandle.Invalid;
				WalkTarget = Vector3.Zero;
			});
			Controls.Bind(Keys.X, () => {
				NavMesh.Visit(PlayerNode, 2, (node) => NavMesh.Remove(node));
			});
			Controls.Bind(Keys.B, () => {
				NavMesh.ShowEdges = !NavMesh.ShowEdges;
			});
			// Controls.Bind(Keys.F, () => {
			// AimTarget = AimPosition(); // HeadPosition(DangerSense.NearbyDanger[0]);
			// Goals.Immediate(new TaskWalk(Position(GetAllBlips(BlipSprite.Standard).Where(blip => GetBlipColor(blip) == BlipColor.MissionYellow).FirstOrDefault())));
			/*
			AimAtHead = NearbyHumans.FirstOrDefault(p =>
				Exists(p) &&
				IsAlive(p) &&
				CanSee(Self, p) &&
				GetBlipHUDColor(GetBlip(p)) == BlipHUDColor.Blue
			);
			*/
			// Goals.Immediate(new TakeCover(AimPosition()));
			// Goals.Immediate(new Mission01() { CurrentPhase = Mission01.Phase.TakeCover });

			// Items(BlipSprite.Standard).Each(s => { Log($"Blip Colors ({s}): ", string.Join(" ", GetAllBlips(s).Select(b => GetBlipHUDColor(b).ToString()))); });
			// });

		}

		public static uint LastGameTime = GameTime;
		public static uint FrameCount = 0;

		public static MovingAverage fps = new MovingAverage(60);


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
				SequenceProgress = Self == 0 ? -1 : Call<int>(GET_SEQUENCE_PROGRESS, Self);

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


				// Pathfinder.FindPath(PlayerPosition, Position(GetHandle(PutOnGround(AimPosition(),1f))), debug: true);
				// NavMesh.DebugVehicle();
				// NavMesh.FindCoverBehindVehicle(Position(DangerSense.NearbyDanger.FirstOrDefault())).ToArray();

				/*
				int visitCount = 0;
				Vector3 prevPos = Vector3.Zero;
				NavMesh.Visit(PlayerNode, 2, (n) => {
					DrawSphere(Position(n), .1f, Color.Orange);
					var pos = Position(n) + (Up * visitCount / 50);
					UI.DrawTextInWorld(pos, $"{visitCount}");
					if( prevPos != Vector3.Zero ) {
						DrawLine(prevPos, pos, Color.Yellow);
					}
					prevPos = pos;
					visitCount++;
				});
				*/

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

