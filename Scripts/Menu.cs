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
using StateMachine;

namespace Shiv {

	public class MenuItem {
		public string Label;
		internal Stopwatch IsActivating = new Stopwatch();
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
	public class IgnoredToggle : MenuItem {
		public IgnoredToggle() : base("[?] Ignored", () => {
			if( HasState(Self, typeof(Ignored)) ) RemoveState(Self, typeof(Ignored));
			else AddState(Self, new Ignored());
		}) { }
		public override string ToString() => HasState(Self, typeof(Ignored)) ? "[X] Ignored" : "[ ] Ignored";
	}
	public class Ignored : State {
		public override State OnTick() {
			IgnoredByPolice(true);
			IgnoredByEveryone(true);
			WantedLevel(0);
			return this;
		}
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
		private PedHandle Created = PedHandle.Invalid;
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
			if( Created != PedHandle.Invalid ) {
				UI.DrawHeadline($"TryCreatePed: {Created} loading...");
				if( GetModel(Created) == Model ) {
					return Next;
				}
			}
			if( TryCreatePed(Type, Model, Location, Heading, out PedHandle veh) ) {
				UI.DrawHeadline($"TryCreatePed: {veh}");
				if( veh == PedHandle.Invalid || veh == PedHandle.ModelInvalid ) {
					return Fail;
				}
				if( veh != PedHandle.ModelLoading ) {
					Created = veh;
				}
				return this;
			}
			UI.DrawHeadline("TryCreatePed: false");
			return this;
		}
	}
	public class SpawnVehicle : State {
		public VehicleHash Model;
		public Vector3 Location = Vector3.Zero;
		public float Heading = 0f;

		private VehicleHandle Created = VehicleHandle.Invalid;

		public SpawnVehicle(VehicleHash hash, State next = null) : base(next) => Model = hash;
		public override string ToString() => $"Spawn({Model})";
		public override State OnTick() {
			if( Location == Vector3.Zero ) {
				Location = PlayerPosition + (Forward(PlayerMatrix) * 6f);
			}
			if( Heading == 0f ) {
				Heading = Heading(Self) - 90f;
			}
			if( Created != VehicleHandle.Invalid ) { // wait until really loaded to move to the next state
				if( GetModel(Created) == Model ) {
					return Next;
				}
				return this;
			}
			if( TryCreateVehicle(Model, Location, Heading, out VehicleHandle veh) ) {
				UI.DrawHeadline($"TryCreateVehicle: {veh}");
				if( veh == VehicleHandle.Invalid || veh == VehicleHandle.ModelInvalid ) {
					return Fail;
				}
				if( veh != VehicleHandle.ModelLoading ) {
					Created = veh;
				}
				return this;
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
		public int LinesPerPage = 7;
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
		public Menu Item(string label, Func<State> state) => Item(label, () => AddState(Self, state()));
		public Menu Item(string label, State state) => Item(label, () => AddState(Self, state));
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
				item.IsActivating.Start();
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
					MenuItem item = Items[i];
					Color c = i == HighlightIndex ?
						item.IsActivating.IsRunning ?
						ActivateColor : HighlightColor : ItemColor;
					if( item.IsActivating.ElapsedMilliseconds > 100 ) {
						item.IsActivating.Reset();
					}
					UI.DrawRect(x, y, itemWidth, ItemHeight, c);
					UI.DrawText(x + BorderWidth, y + BorderWidth, $"{i+1}. {Items[i]}");
					y += ItemHeight + (2 * BorderWidth);
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
			Controls.Bind(Keys.Right, (Action)(() => {
			MenuScript.Show(new Menu()
				.Item("Trainer", new Menu()
					.Item(new InvincibleToggle()) // "Set Invincible", () => Call(SET_PLAYER_INVINCIBLE, true))
					.Item(new IgnoredToggle())
					.Item(new ClearWanted())
					.Item("Respawn Here", () => {
						Call(_SET_NEXT_RESPAWN_TO_CUSTOM);
						Call(_SET_CUSTOM_RESPAWN_POSITION, PlayerPosition, Heading(Self));
						Call(IGNORE_NEXT_RESTART, true);
						Call(SET_FADE_OUT_AFTER_DEATH, false);
					})
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
					.Item("Combat Mode", () => AddState(Self, new Combat(NearbyHumans, null)))
					)
					.Item("Missions", new Menu()
						.Item("Mission01", new Menu()
							.Item("Start", () => new WaitForControl(new Mission01_Approach()))
							.Item("GotoVault", () => new Mission01_GotoVault())
							.Item("MoveToCover", () => new Mission01_MoveToCover())
							.Item("MoveToButton", () => new Mission01_MoveToButton())
							.Item("Get Away", () => new Mission01_GetAway())
						)
						.Item("Generic", () => AddState(Self, new GenericMission()))
					)
					.Item("Tests", new Menu()
						.Item("All", () => SetState(Self, State.Series(
								new TestSteering(),
								new TestExitVehicle(),
								new TestSteeringBicycle(),
								new TestCreatePed(),
								new TestKillPed(),
								new TestCommandPed(),
								new TestPedCanDrive(),
								new TestPedCanFly()
							))
						)
						.Item("Enter Vehicle", () => new TestEnterVehicle())
						.Item("Exit Vehicle", () => new TestExitVehicle())
						.Item("Steering", () => new TestSteering())
						.Item("Steering Bicycle", () => new TestSteeringBicycle())
						.Item("Create Ped", () => new TestCreatePed())
						.Item("Kill Ped", () => new TestKillPed())
						.Item("Command Ped", () => new TestCommandPed())
						.Item("Ped Can Drive", () => new TestPedCanDrive())
						.Item("Ped Can Fly", () => new TestPedCanFly())
					)
					.Item("Debug", new Menu()
						.Item(new DebugAimMenuItem())
					)
					.Item("Walk To", new Menu()
						.Item("Trevor's House", new WalkTo(NodeHandle.TrevorTrailerParking))
						.Item("Safehouse", () => new WalkTo(Position(First(GetAllBlips(BlipSprite.Safehouse)))))
						.Item("Wander", () => new Wander())
						.Item("Explore", () => new Explore())
						.Item("Blip", new Menu()
							.Item("Yellow", () => new WalkTo(Position(First(GetAllBlips(), BlipHUDColor.Yellow)), null))
							.Item("Green", () => new WalkTo(Position(First(GetAllBlips(), BlipHUDColor.Green)), null))
							.Item("Blue", () => new WalkTo(Position(First(GetAllBlips(), BlipHUDColor.Blue)), null))
						)
					)
					.Item("Drive To", new Menu()
						.Item("Wander", () => AddState(Self, 
								new DriveWander(null) { Speed = 10f, DrivingFlags = VehicleDrivingFlags.Human },
								new LookAtPed(Throttle(5000, () => NearbyHumans().Random(.3f).FirstOrDefault()), null)
							)
						)
						.Item("Trevor's House", () => AddState(Self,
								new DriveTo(new Vector3(1983f, 3829f, 32f), new State.Machine.Clear(null)) { Speed = 10f },
								new LookAtPed(Throttle(5000, () => NearbyHumans().Random(.3f).FirstOrDefault()), null)
							)
						)
						.Item("Safeouse", () => AddState(Self,
								new DriveTo(Position(First(GetAllBlips(BlipSprite.Safehouse))), new State.Machine.Clear(null)) { Speed = 10f },
								new LookAtPed(Throttle(5000, () => NearbyHumans().Random(.3f).FirstOrDefault()), null)
							)
						)
						.Item("Waypoint", () => AddState(Self,
								new DriveTo(Position(First(GetAllBlips(BlipSprite.Waypoint))), new State.Machine.Clear(null)) { Speed = 10f },
								new LookAtPed(Throttle(5000, () => NearbyHumans().Random(.3f).FirstOrDefault()), null)
							)
						)
						.Item("Blip", new Menu()
							.Item("Yellow", () => new DriveTo(Position(First(GetAllBlips(), BlipHUDColor.Yellow)), null))
							.Item("Green", () => new DriveTo(Position(First(GetAllBlips(), BlipHUDColor.Green)), null))
							.Item("Blue", () => new DriveTo(Position(First(GetAllBlips(), BlipHUDColor.Blue)), null))
						)
					)
					.Item("Teleport To", new Menu()
						.Item("Waypoint", () => new Teleport(Handle(Position(First(GetAllBlips(BlipSprite.Waypoint)))), null))
						.Item("Safehouse", () => new Teleport(Handle(Position(First(GetAllBlips(BlipSprite.Safehouse)))), null))
						.Item("Desert Airfield", () => new Teleport(NodeHandle.DesertAirfield, null))
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
			}));

			Controls.Bind(Keys.O, () => {
				var sw = new Stopwatch();
				sw.Start();
				var queue = new Queue<NodeHandle>();
				var seen = new HashSet<NodeHandle>();
				var limit = 9;
				AddState(Self, new State.Runner("Show Materials", (state) => {
					queue.Enqueue(PlayerNode);
					seen.Clear();
					while( queue.Count > 0 ) {
						NodeHandle n = queue.Dequeue();
						Vector3 pos = Position(n);
						Vector3 end = pos - (Up * 2f);
						DrawLine(pos, end, Color.Orange);
						RaycastResult result = Raycast(pos, pos - (Up * 2f), IntersectOptions.Map, Self);
						if( result.DidHit ) {
							UI.DrawTextInWorld(result.HitPosition + (Up * .2f), $"Material: {result.Material}");
						}
						foreach( NodeHandle e in Edges(n) ) {
							if( seen.Count < limit && !seen.Contains(e) ) {
								seen.Add(e);
								queue.Enqueue(e);
							}
						}
					}
					return state;
				}));
			});
			Controls.Bind(Keys.G, () => {
				SetState(Self, new State.Runner("Check Obstruction", (state) => {
					Vector3 p = Position(PlayerNode);
					foreach( NodeHandle e in Edges(PlayerNode) ) {
						Vector3 pos = Position(e);
						CheckObstruction(p, (pos - p), true);
					}
					return state;
				}));
			});
			Controls.Bind(Keys.End, () => {
				ForcedAim(CurrentPlayer, false);
				TaskClearAll();
				StateMachines.ClearAllStates();
				PathStatus.CancelAll();
			});
			Controls.Bind(Keys.X, () => {
				NodeHandle aim = AimNode();
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
				SetState(Self, new State.Runner("Show Blocked", (state) => {
					foreach( EntHandle ent in NearbyObjects().Take(10) ) {
						switch( GetEntityType(ent) ) {
							case EntityType.Ped:
							case EntityType.Vehicle:
							case EntityType.Prop:
								Matrix4x4 m = Matrix(ent);
								ModelHash model = GetModel(ent);
								if( IsValid(model) ) {
									GetModelDimensions(model, out Vector3 backLeft, out Vector3 frontRight);
									Vector3 rayStart = Position(CameraMatrix);
									Vector3 rayEnd = rayStart + (10f * Forward(CameraMatrix));
									if( IntersectModel(rayStart, rayEnd, m, backLeft, frontRight) ) {
										DrawBox(m, backLeft, frontRight);
										foreach( FinitePlane plane in GetModelPlanes(m, backLeft, frontRight) ) {
											DrawLine(plane.Center, plane.Center + plane.Normal, Color.Green);
										}
										Vector3 center = GetCenter(m, backLeft, frontRight);
										int line = 0;
										UI.DrawTextInWorldWithOffset(center, 0f, (line++ * .02f), $"ent:{ent} {GetEntityType(ent)} {model}");
										UI.DrawTextInWorldWithOffset(center, 0f, (line++ * .02f), $"volume:{GetVolume(frontRight, backLeft)}");
										UI.DrawTextInWorldWithOffset(center, 0f, (line++ * .02f), $"object:{Call<int>(GET_KEY_FOR_ENTITY_IN_ROOM, ent)}");
									}
									// float volume = GetVolume(frontRight, backLeft);
								}
								break;
						}
					}
					return state;
				}));
			});

			Controls.Bind(Keys.Z, () => {
				NodeHandle aim = AimNode();
				Vector3 pos = Position(AimNode());
				Flood(aim, 30, 30, default, Edges)
					.Without(PlayerNode)
					.Where(n => (pos - Position(n)).LengthSquared() < .75f)
					.ToArray()
					.Each(Block);
			});

			Controls.Bind(Keys.J, () => {
				NodeHandle n = AimNode();
				Vector3 p = Position(AimNode());
				SetState(Self, (State)((state) => {
					if( p != Vector3.Zero ) {
						DrawSphere(p, .1f, Color.Yellow);
						MoveResult result;
						if( PlayerVehicle != VehicleHandle.Invalid ) {
							VehicleHash model = GetModel(PlayerVehicle);
							result = IsHeli(model) 
								? FlyToward(p, 10f, 20f)
								: SteerToward(p, 100f, 1f, false, debug: true);
						} else {
							result = MoveToward(p, debug: true);
						}
						switch( result ) {
							case MoveResult.Complete:
								Log("Move complete.");
								return null;
							case MoveResult.Continue:
								UI.DrawText($"Move: distance {Sqrt(DistanceToSelf(p)):F2} speed {Speed(Self)}");
								return state;
							case MoveResult.Failed:
								Log("Move failed");
								return null;
						}
					}
					return state;
				}));
			});

			Controls.Bind(Keys.K, () => {
				AddState(Self, new Combat(NearbyHumans, null));
			});

			Controls.Bind(Keys.L, () => {
				AddState(Self, new Teleport(AimNode()) { KeepVehicle = PlayerVehicle != VehicleHandle.Invalid });
			});

			Controls.Bind(Keys.H, () => {
				AddState(Self, new Hover(new Vector3(PlayerPosition.X, PlayerPosition.Y, 50f)));
			});

			Controls.Bind(Keys.T, () => {
				SetState(Self, new TestExitVehicle() {
					Next = new TestSteering() {
						Next = new TestKillPed() {
							Next = new TestPedCanDrive()
						}
					}
				});
			});

			Controls.Bind(Keys.Y, () => {
				SetState(Self, (State)((state) => {
					Vector3 rayStart = HeadPosition(Self);
					Vector3 rayEnd = Position(CameraMatrix) + (Forward(CameraMatrix) * 10f);
					DrawLine(rayStart, rayEnd, Color.Red);
					foreach( VehicleHandle veh in NearbyVehicles().Take(1) ) {
						VehicleHash model = GetModel(veh);
						Matrix4x4 m = Matrix(veh);
						GetModelDimensions(model, out Vector3 backLeft, out Vector3 frontRight);
						int count = 0;
						Vector3 rayDir = rayStart - rayEnd;
						foreach( FinitePlane plane in GetModelPlanes(m, backLeft, frontRight) ) {
							if( TryIntersectPlane(rayStart, rayDir, plane, out Vector3 point) ) {
								DrawLine(plane.Center, plane.Center + plane.Normal, Color.Yellow);
								DrawSphere(point, .1f, Color.Blue);
								DrawLine(point, plane.Center, Color.Blue);
								count += 1;
							} else {
								DrawLine(plane.Center, plane.Center + plane.Normal, Color.Green);
							}
						}
						UI.DrawHeadline(Self, $"Count: {count}");
						// if( IntersectModel(rayStart, rayEnd, m, backLeft, frontRight) ) {
						DrawBox(m, backLeft, frontRight);
						// }
					}
					return state;
				}));
			});

		}
	}

}
