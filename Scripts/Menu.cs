using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using Keys = System.Windows.Forms.Keys;
using static GTA.Native.Hash;
using static GTA.Native.Function;
using static Shiv.Global;
using static Shiv.NavMesh;
using static System.Math;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Shiv {

	public class MenuItem {
		public string Label;
		public bool IsActivating = false;
		public Action<MenuItem> OnClick = null;
		public Menu Parent = null;
		public Menu SubMenu = null;
		public MenuItem(string label) => Label = label;
		public MenuItem(string label, Action<MenuItem> click) {
			Label = label;
			OnClick = click;
		}
		public MenuItem(string label, Action func) {
			Label = label;
			OnClick = (item) => func();
		}
		public override string ToString() => Label;
	}
	public class InvincibleToggle : MenuItem {
		public InvincibleToggle() : base("[?] Invincible", () => IsInvincible(Self, !IsInvincible(Self))) { }
		public override string ToString() => AmInvincible() ? "[X] Invincible" : "[ ] Invincible";
	}
	public class CarRepair : MenuItem {
		public CarRepair() : base("[?] Repair Vehicle", () => Call(SET_VEHICLE_FIXED, CurrentVehicle(Self))) { }
		public override string ToString() => Call<bool>(_IS_VEHICLE_DAMAGED, CurrentVehicle(Self)) ? "[ ] Repair Vehicle" : "[X] Repair Vehicle";
	}
	public class ClearWanted : MenuItem {
		public ClearWanted() : base("[?] Clear Wanted", () => Call(CLEAR_PLAYER_WANTED_LEVEL, CurrentPlayer)) { }
		public override string ToString() => $"[{Call<int>(GET_PLAYER_WANTED_LEVEL, CurrentPlayer)}] Clear Wanted";
	}
	public class SpawnPed : State {
		public PedType Type;
		public PedHash Model;
		public Vector3 Location = Vector3.Zero;
		public float Heading = 0f;
		public SpawnPed(PedType type, PedHash hash, State next = null) : base(next) {
			Model = hash;
			Type = type;
		}
		public override string ToString() => $"Spawn({Model})";
		public override State OnTick() {
			if( Location == Vector3.Zero ) {
				Location = PlayerPosition + (Forward(PlayerMatrix) * 5f);
			}
			if( Heading == 0f ) {
				Heading = Heading(Self) - 90f;
			}
			if( TryCreatePed(Type, Model, Location, Heading, out PedHandle veh) ) {
				UI.DrawHeadline($"TryCreatePed: {veh}");
				switch( veh ) {
					case PedHandle.Invalid: return Fail;
					case PedHandle.ModelInvalid: return Fail;
					case PedHandle.ModelLoading: return this;
					default: return Next;
				}
			}
			UI.DrawHeadline("TryCreatePed: false");
			return this;
		}
	}
	public class SpawnVehicle : State {
		public VehicleHash Model;
		public Vector3 Location = Vector3.Zero;
		public float Heading = 0f;
		public SpawnVehicle(VehicleHash hash, State next = null) : base(next) => Model = hash;
		public override string ToString() => $"Spawn({Model})";
		public override State OnTick() {
			if( Location == Vector3.Zero ) {
				Location = PlayerPosition + (Forward(PlayerMatrix) * 6f);
			}
			if( Heading == 0f ) {
				Heading = Heading(Self) - 90f;
			}
			if( TryCreateVehicle(Model, Location, Heading, out VehicleHandle veh) ) {
				UI.DrawHeadline($"TryCreateVehicle: {veh}");
				switch( veh ) {
					case VehicleHandle.Invalid: return Fail;
					case VehicleHandle.ModelInvalid: return Fail;
					case VehicleHandle.ModelLoading: return this;
					default: return Next;
				}
			}
			UI.DrawHeadline("TryCreateVehicle: false");
			return this;
		}
	}
	public class VehicleSpawnMenuItem : MenuItem {
		static Menu heliMenu = new Menu();
		static Menu carMenu = new Menu();
		static Menu boatMenu = new Menu();
		static Menu planeMenu = new Menu();
		static Menu bikeMenu = new Menu();
		static Menu otherMenu = new Menu();
		static Menu spawnMenu = new Menu();
		static VehicleSpawnMenuItem() {
			Array values = Enum.GetValues(typeof(VehicleHash));
			Array.Sort(values.Select<VehicleHash, string>(v => $"{v}").ToArray(), values);
			values.Each<VehicleHash>(hash =>
					( IsHeli(hash) ? heliMenu :
						IsBicycle(hash) ? bikeMenu :
						IsMotorbike(hash) ? bikeMenu :
						IsBoat(hash) ? boatMenu :
						IsCar(hash) ? carMenu :
						IsPlane(hash) ? planeMenu :
						otherMenu
					).Item($"{hash}", () => new SpawnVehicle(hash))
				);
			spawnMenu
				.Item("Cars", carMenu)
				.Item("Bikes", bikeMenu)
				.Item("Boats", boatMenu)
				.Item("Planes", planeMenu)
				.Item("Helicopters", heliMenu)
				.Item("Other", otherMenu);
		}

		public VehicleSpawnMenuItem() : base("[?] Spawn Vehicle", (item) => {
			item.Parent.ShowSubmenu(spawnMenu);
		}) { }
		public override string ToString() => $"[{(PlayerVehicle == VehicleHandle.Invalid ? " " : "X")}] Spawn Vehicle";
	}

	public class MaxWantedLevel : MenuItem {
		uint level = 5;
		public MaxWantedLevel() : base("[?] Max Wanted Level") {
			level = Call<uint>(GET_MAX_WANTED_LEVEL);
			OnClick = (item) => {
				level = (level + 1) % 6;
				Call(SET_MAX_WANTED_LEVEL, level);
			};
		}
		public override string ToString() => $"[{level}] Max Wanted Level";
	}

	public class Menu : HudElement {
		public float ItemHeight = .03f;
		public float MenuWidth = .15f;
		public Color BackgroundColor = Color.DarkGray;
		public Color ItemColor = Color.DarkGreen;
		public Color HighlightColor = Color.Green;
		public Color ActivateColor = Color.Yellow;
		public float BorderWidth = .003f;
		public int HighlightIndex = 0;
		public int ScrollOffset = 0;
		public int LinesPerPage = 10;
		public List<MenuItem> Items = new List<MenuItem>();
		public Menu Parent = null;
		public Menu SubMenu = null;
		public Menu():this(MenuScript.DefaultLeft, MenuScript.DefaultTop, MenuScript.DefaultWidth) { }
		public Menu(float x, float y, float w) : base(x, y, w, 0) => MenuWidth = w;
		public Menu Item(MenuItem item) {
			Items.Add(item);
			item.Parent = this;
			H += ItemHeight;
			return this;
		}
		public Menu Item(string label, Action click) => Item(new MenuItem(label, click));
		public Menu Item(string label, Func<State> state) => Item(label, () => StateMachine.Run(state()));
		public Menu Item(string label, State state) => Item(label, () => StateMachine.Run(state));
		public Menu Item(string label, Menu subMenu) => Item(label, () => ShowSubmenu(subMenu));
		public void ShowSubmenu(Menu subMenu) {
			subMenu.Parent = this;
			this.SubMenu = subMenu;
			void closed(object _, object e) {
				subMenu.Closed -= closed;
				subMenu.Parent = null;
				this.SubMenu = null;
			}
			subMenu.Closed += closed;
		}
		private int Wrap(int i, int d) {
			i += d;
			return i < 0 ? Items.Count - 1
				: i >= Items.Count ? 0
				: i;
		}
		private int Scroll(int i) {
			ScrollOffset = Min(Max(0, Items.Count - LinesPerPage), Max(0, i - (LinesPerPage / 2)));
			return i;
		}
		public void Up() => HighlightIndex = Scroll(Wrap(HighlightIndex, -1));
		public void Down() => HighlightIndex = Scroll(Wrap(HighlightIndex, +1));
		public void Activate() {
			if( HighlightIndex >= 0 ) {
				MenuItem item = Items[HighlightIndex];
				item.IsActivating = true;
				if( item.OnClick != null ) {
					item.OnClick(item);
				} else if( item.SubMenu != null ) {
					item.SubMenu.Parent = this;
					SubMenu = item.SubMenu;
				}
			}
		}
		public event EventHandler<object> Closed;
		public void Back() {
			if( SubMenu == null ) {
				Closed?.Invoke(null, null);
				return;
			}

			SubMenu = SubMenu.Parent == this ? null : SubMenu.Parent;
		}
		public void Draw() {
			if( SubMenu != null ) {
				SubMenu.Draw();
			} else {
				int itemCount = Math.Min(LinesPerPage, Items.Count);
				float totalHeight = (2 * BorderWidth) + ((ItemHeight + (2 * BorderWidth)) * itemCount);
				float totalWidth = (2 * BorderWidth) + MenuWidth;
				float x = X, y = Y;
				UI.DrawRect(x, y, totalWidth, totalHeight, BackgroundColor);
				x += BorderWidth;
				y += BorderWidth;
				float itemWidth = MenuWidth - (2 * BorderWidth);
				for( var i = ScrollOffset; i < ScrollOffset + itemCount; i++ ) {
					var item = Items[i];
					UI.DrawRect(x, y, itemWidth, ItemHeight, (i == HighlightIndex ? (item.IsActivating ? ActivateColor: HighlightColor) :ItemColor));
					UI.DrawText(x + BorderWidth, y + BorderWidth, Items[i].ToString());
					y += ItemHeight + (2 * BorderWidth);
					item.IsActivating = false;
				}
			}
		}
		public void Close() => Closed?.Invoke(null, null);
	}

	public class MenuScript : Script {
		public MenuScript() { }

		public override bool OnKey(Keys key, bool downBefore, bool upNow) {
			if( rootMenu != null ) {
				Menu menu = rootMenu;
				while( menu.SubMenu != null ) {
					menu = menu.SubMenu;
				}
				if( !downBefore && !upNow ) {
					switch( key ) {
						case Keys.Up: menu.Up(); return true;
						case Keys.Down: menu.Down(); return true;
						case Keys.Right: menu.Activate(); return true;
						case Keys.Left: menu.Back(); return true;
						case Keys.Back: menu.Back(); return true;
						case Keys.End: menu.Back(); return true;
					}
				} else if( !upNow ) {
					switch( key ) { // allow repeats on scroll up/down
						case Keys.Up: menu.Up(); return true;
						case Keys.Down: menu.Down(); return true;
					}
				}
			}
			return false;
		}

		public override void OnAbort() => rootMenu = null;

		private static Menu rootMenu = null;

		public override void OnTick() {
			if( rootMenu != null ) {
				Call(DISABLE_CONTROL_ACTION, 0, Control.Phone, true);
				Call(_DISABLE_PHONE_THIS_FRAME, true);
				rootMenu.Draw();
			}
		}

		public static void Show(Menu menu) {
			if( rootMenu == null ) {
				rootMenu = menu;
				rootMenu.Closed += Detach;
				GamePaused = true;
			}
		}

		public static void Hide() {
			if( rootMenu != null ) {
				rootMenu.Close();
				Detach(null, null);
				GamePaused = false;
			}
		}

		private static void Detach(object sender, object e) {
			if( rootMenu != null ) {
				rootMenu.Closed -= Detach;
				rootMenu = null;
				GamePaused = false;
			}
		}

		public static float DefaultLeft = .6f;
		public static float DefaultTop = .5f;
		public static float DefaultWidth = .2f;
		public override void OnInit() {
			Controls.Bind(Keys.Pause, () => GamePaused = !GamePaused);
			Controls.Bind(Keys.Right, () => {
				MenuScript.Show(new Menu()
					.Item("Trainer", new Menu()
						.Item(new InvincibleToggle()) // "Set Invincible", () => Call(SET_PLAYER_INVINCIBLE, true))
						.Item(new ClearWanted())
						.Item("Give Weapons", () => GiveWeapons(Self,
							(uint)WeaponHash.Pistol, 90,
							(uint)WeaponHash.PumpShotgun, 200,
							(uint)WeaponHash.SniperRifle, 50,
							(uint)WeaponHash.RPG, 10,
							(uint)WeaponHash.Knife, 0,
							(uint)WeaponHash.Unarmed, 0,
							(uint)WeaponHash.CarbineRifle, 500
							)
						)
						.Item(new CarRepair())
						.Item(new VehicleSpawnMenuItem())
						.Item("Combat Mode", () => new Combat(NearbyHumans, null))
					)
					.Item("Missions", new Menu()
						.Item("Mission01", new Menu()
							.Item("Start", new WaitForControl(new Mission01_Approach()))
							.Item("GotoVault", new Mission01_GotoVault())
							.Item("MoveToCover", new Mission01_MoveToCover())
							.Item("MoveToButton", new Mission01_MoveToButton())
							.Item("Get Away", new Mission01_GetAway())
						)
					)
					.Item("Tests", new Menu()
						.Item("All", () => {
							return new TestExitVehicle() {
								Next = new TestSteering() {
									Next = new TestSteeringBicycle() {
										Next = new TestCreatePed() {
											Next = new TestKillPed()
										}
									}
								}
							};
						})
						.Item("Enter Vehicle", () => new TestEnterVehicle())
						.Item("Exit Vehicle", () => new TestExitVehicle())
						.Item("Steering", () => new TestSteering())
						.Item("Steering Bicycle", () => new TestSteeringBicycle())
						.Item("Create Ped", () => new TestCreatePed())
						.Item("Kill Ped", () => new TestKillPed())
						.Item("Command Ped", () => new TestCommandPed())
					)
					.Item("Show Path To", new Menu()
						.Item("Trevor's House", new DebugPath(new Vector3(1937.5f, 3814.5f, 33.4f)))
						.Item("Safehouse", new DebugPath(Position(GetAllBlips(BlipSprite.Safehouse).FirstOrDefault())))
						.Item("Red Blip", new DebugPath(Position(GetAllBlips().FirstOrDefault(BlipHUDColor.Red))))
						.Item("Green Blip", new DebugPath(Position(GetAllBlips().FirstOrDefault(BlipHUDColor.Green))))
						.Item("Yellow Blip", new DebugPath(Position(GetAllBlips().FirstOrDefault(BlipHUDColor.Yellow))))
						.Item("Aim Position", new DebugPath(AimPosition()))
						.Item("Closest Ungrown", () => {
							Vector3 node = NavMesh.Flood(PlayerNode, 20000, 20, default, Edges)
								.Without(IsGrown)
								.Select(Position)
								.Min(DistanceToSelf);
							StateMachine.Run(new DebugPath(node));
						})
					)
					.Item("Walk To", new Menu()
						.Item("Trevor's House", new WalkTo(new Vector3(1937.5f, 3814.5f, 33.4f)))
						.Item("Safehouse", () => new WalkTo(Position(First(GetAllBlips(BlipSprite.Safehouse)))))
						.Item("Wander", () => new Wander())
						.Item("Explore", () => new Explore())
						.Item("Yellow Blip", () => new WalkTo(Position(First(GetAllBlips(), BlipHUDColor.Yellow))))
						.Item("Blue Blip", () => new WalkTo(Position(First(GetAllBlips(), BlipHUDColor.Blue))))
						.Item("Green Blip", () => new WalkTo(Position(First(GetAllBlips(), BlipHUDColor.Green))))
						.Item("Red Blip", () => new WalkTo(Position(First(GetAllBlips(), BlipHUDColor.Red))))
					)
					.Item("Drive To", new Menu()
						.Item("Wander", new MultiState(
									new DriveWander(State.Idle) { Speed = 10f, DrivingFlags = VehicleDrivingFlags.Human },
									new LookAtPed(Throttle(5000, () => NearbyHumans().Random(.3f).FirstOrDefault()), null)
								)
						)
						.Item("Trevor's House", new MultiState(
									new DriveTo(new Vector3(1983f, 3829f, 32f), new MultiState.Clear(null)) { Speed = 10f },
									new LookAtPed(Throttle(5000, () => NearbyHumans().Random(.3f).FirstOrDefault()), null)
								)
						)
						.Item("Safeouse", new MultiState(
									new DriveTo(Position(First(GetAllBlips(BlipSprite.Safehouse))), new MultiState.Clear(null)) { Speed = 10f },
									new LookAtPed(Throttle(5000, () => NearbyHumans().Random(.3f).FirstOrDefault()), null)
								)
						)
						.Item("Waypoint", () => new MultiState(
									new DriveTo(Position(First(GetAllBlips(BlipSprite.Waypoint))), new MultiState.Clear(null)) { Speed = 10f },
									new LookAtPed(Throttle(5000, () => NearbyHumans().Random(.3f).FirstOrDefault()), null)
								)
						)
						.Item("Yellow Blip", () => new DriveTo(Position(First(GetAllBlips(), BlipHUDColor.Yellow)), State.Idle))
						.Item("Green Blip", () => new DriveTo(Position(First(GetAllBlips(), BlipHUDColor.Green)), State.Idle))
						.Item("Blue Blip", () => new DriveTo(Position(First(GetAllBlips(), BlipHUDColor.Blue)), State.Idle))
					)
					.Item("Teleport To", new Menu()
						.Item("Waypoint", () => new Teleport(Handle(Position(First(GetAllBlips(BlipSprite.Waypoint)))), null))
						.Item("Safehouse", () => new Teleport(Handle(Position(First(GetAllBlips(BlipSprite.Safehouse)))), null))
						.Item("Desert Airfield", () => new Teleport(NodeHandle.Airfield, null))
					)
					.Item("NavMesh", new Menu()
						.Item("Save NavMesh", SaveToFile)
						.Item("Clear Region", () => {
							if( AllNodes.Regions.TryGetValue(Region(PlayerNode), out var nodes) ) {
								nodes.Clear();
							}
						})
					)
					.Item("Test Key", new Menu()
						.Item("Context", new PressKey(2, Control.Context, 200))
						.Item("ScriptRUp", new PressKey(2, Control.ScriptRUp, 200))
						.Item("ScriptRLeft", new PressKey(2, Control.ScriptRLeft, 200))
					)
				);
			});

			Controls.Bind(Keys.O, () => {
				var sw = new Stopwatch();
				sw.Start();
				var queue = new Queue<NodeHandle>();
				var seen = new HashSet<NodeHandle>();
				var limit = 9;
				StateMachine.Run((state) => {
					queue.Enqueue(PlayerNode);
					seen.Clear();
					while( queue.Count > 0 ) {
						var n = queue.Dequeue();
						var pos = Position(n);
						var end = pos - (Up * 2f);
						DrawLine(pos, end, Color.Orange);
						var result = Raycast(pos, pos - (Up * 2f), IntersectOptions.Map, Self);
						if( result.DidHit ) {
							UI.DrawTextInWorld(result.HitPosition + (Up * .2f), $"Material: {result.Material}");
						}
						foreach( var e in Edges(n) ) {
							if( seen.Count < limit && !seen.Contains(e) ) {
								seen.Add(e);
								queue.Enqueue(e);
							}
						}
					}
					return state;
				});
			});
			Controls.Bind(Keys.G, () => {
				Goals.Immediate(new QuickGoal("CheckObstruction", () => {
					var p = Position(PlayerNode);
					foreach( NodeHandle e in Edges(PlayerNode) ) {
						var pos = Position(e);
						CheckObstruction(p, (pos - p), true);
					}
					return GoalStatus.Active;
				}));
			});
			Controls.Bind(Keys.End, () => {
				Goals.Clear();
				TaskClearAll();
				PathStatus.CancelAll();
				StateMachine.Clear();
				ForcedAim(CurrentPlayer, false);
			});
			Controls.Bind(Keys.X, () => {
				var aim = AimNode();
				Sphere.Add(Position(aim), .06f, Color.Green, 1000);
				Flood(aim, 200, 10, default, PossibleEdges)
					.ToArray()
					.Each(n => {
						Sphere.Add(Position(n), .02f, Color.Red, 1000);
						Remove(n);
					});
				Grow(aim, 10, debug: true);
			});
			Controls.Bind(Keys.B, () => {
				NavMesh.ShowEdges = !NavMesh.ShowEdges;
			});
			Controls.Bind(Keys.N, () => {
				Goals.Immediate(new QuickGoal("Show Blocked", () => {
					Pathfinder.GetBlockedNodes(true, true, false, true);
					// NavMesh.DebugVehicle();
					return GoalStatus.Active;
				}));
			});

			Controls.Bind(Keys.Z, () => {
				var aim = AimNode();
				var pos = Position(AimNode());
				Flood(aim, 30, 30, default, Edges)
					.Without(PlayerNode)
					.Where(n => (pos - Position(n)).LengthSquared() < .75f)
					.ToArray()
					.Each(Block);
			});

			Controls.Bind(Keys.J, () => {
				StateMachine.Run("Drive Direct", (state) => {
					var n = AimNode();
					var p = Position(AimNode());
					if( p != Vector3.Zero ) {
						DrawSphere(p, .1f, Color.Yellow);
						switch( SteerToward(p, 100f, 1f, false, debug:true) ) {
							case MoveResult.Complete:
								Log("Drive complete.");
								return null;
							case MoveResult.Continue:
								UI.DrawText($"Drive: distance {Math.Sqrt(DistanceToSelf(p)):F2} speed {Speed(CurrentVehicle(Self))}");
								return state;
							case MoveResult.Failed:
								Log("Drive failed");
								return null;
						}
					}
					return state;
				});
			});

			Controls.Bind(Keys.K, () => {
				StateMachine.Run(new Combat(NearbyHumans, null));
			});

			NodeHandle pathTarget = NodeHandle.Invalid;
			int repathEvery = 2000;
			Controls.Bind(Keys.L, () => {
				pathTarget = AimNode();
				var pos = Position(pathTarget);
				var req = new PathRequest(PlayerNode, pathTarget, 2000, false, true, true, 1, false);
				var started = GameTime;
				NodeHandle[] nodePath = null;
				SmoothPath path = null;
				StateMachine.Run("Follow Path", (state) => {
					if( req != null && req.IsReady() ) {
						nodePath = req.GetResult().ToArray(); // for debugging
						path = new SmoothPath(req.GetResult()); // .Select(Position).ToArray();
						req = null;
					}
					if( path != null ) {
						if( path.IsComplete() ) {
							return null;
						}
						if( repathEvery > 0 && (GameTime - started) > repathEvery ) { // || DistanceToSelf(path.NextStep()) > 20f ) {
							req = new PathRequest(PlayerNode, pathTarget, 3000, false, true, true, 1, false);
							started = GameTime;
						}
						// foreach(var n in nodePath.Take(30) ) { DrawSphere(Position(n), .04f, Color.Yellow); }
						var head = HeadPosition(Self);
						DrawLine(head, path.NextStep(), Color.Orange);
						path.Draw();

						return state;
						/*
						switch( FollowPath(path, 0.3f) ) {
							case MoveResult.Continue:
								FirstStep(path);
								UI.DrawText($"Walk");
								return state;
							case MoveResult.Complete:
								Log($"MoveResult.Complete");
								return null;
							case MoveResult.Failed:
								Log($"MoveResult.Failed");
								return null;
						}
						*/
					}
					return state;
				});
			});

			Controls.Bind(Keys.H, () => {
				StateMachine.Run(new Hover(new Vector3(PlayerPosition.X, PlayerPosition.Y, 100)));
			});

			Controls.Bind(Keys.T, () => {
				StateMachine.Run(new TestEnterVehicle() {
					Next = new TestExitVehicle() {
						Next = new TestSteering() {
							Next = new TestSteeringBicycle()
						}
					}
				});
			});

		}
	}

}
