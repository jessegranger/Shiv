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

namespace Shiv {

	public class MenuItem {
		public string Label;
		public Action OnClick = null;
		public Menu SubMenu = null;
		public MenuItem(string label) => Label = label;
		public MenuItem(string label, Action func) {
			Label = label;
			OnClick = func ?? throw new ArgumentNullException(nameof(func));
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

	public class MaxWantedLevel : MenuItem {
		uint level = 5;
		public MaxWantedLevel() : base("[?] Max Wanted Level") {
			level = Call<uint>(GET_MAX_WANTED_LEVEL);
			OnClick = () => {
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
		public float BorderWidth = .003f;
		public int HighlightIndex = 0;
		public List<MenuItem> Items = new List<MenuItem>();
		public Menu Parent = null;
		public Menu SubMenu = null;
		public Menu(float x, float y, float w) : base(x, y, w, 0) => MenuWidth = w;
		public Menu Item(MenuItem item) {
			Items.Add(item);
			H += ItemHeight;
			return this;
		}
		public Menu Item(string label, Action click) => Item(new MenuItem(label, click));
		public Menu Item(string label, Menu subMenu) {
			return Item(label, () => {
				subMenu.Parent = this;
				this.SubMenu = subMenu;
				void closed(object _, object e) {
					subMenu.Closed -= closed;
					subMenu.Parent = null;
					this.SubMenu = null;
				}
				subMenu.Closed += closed;
			});
		}
		private int Wrap(int i, int d) {
			i += d;
			return i < 0 ? Items.Count - 1 :
				i >= Items.Count ? 0 : i;
		}
		public void Up() => HighlightIndex = Wrap(HighlightIndex, -1);
		public void Down() => HighlightIndex = Wrap(HighlightIndex, +1);
		public void Activate() {
			if( HighlightIndex >= 0 ) {
				MenuItem item = Items[HighlightIndex];
				if( item.OnClick != null ) {
					item.OnClick();
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
				float totalHeight = (2 * BorderWidth) + ((ItemHeight + (2 * BorderWidth)) * Items.Count);
				float totalWidth = (2 * BorderWidth) + MenuWidth;
				float x = X, y = Y;
				UI.DrawRect(x, y, totalWidth, totalHeight, BackgroundColor);
				x += BorderWidth;
				y += BorderWidth;
				float itemWidth = MenuWidth - (2 * BorderWidth);
				for( var i = 0; i < Items.Count; i++ ) {
					UI.DrawRect(x, y, itemWidth, ItemHeight, (i == HighlightIndex ? HighlightColor : ItemColor));
					UI.DrawText(x + BorderWidth, y + BorderWidth, Items[i].ToString());
					y += ItemHeight + (2 * BorderWidth);
				}
			}
		}
		public void Close() => Closed?.Invoke(null, null);
	}

	[DependOn(typeof(ControlsScript))]
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
						case Keys.Up:
							menu.Up();
							return true;
						case Keys.Down:
							menu.Down();
							return true;
						case Keys.Right:
							menu.Activate();
							return true;
						case Keys.Left:
							menu.Back();
							return true;
						case Keys.Back:
							menu.Back();
							return true;
						case Keys.End:
							Hide();
							return true;
					}
				}
			}
			return false;
		}

		public override void OnAbort() => rootMenu = null;

		private static Menu rootMenu = null;

		public override void OnTick() {
			if( rootMenu != null ) {
				Call(DISABLE_CONTROL_ACTION, 0, Global.Control.Phone, true);
				Call(_DISABLE_PHONE_THIS_FRAME, true);
				rootMenu.Draw();
			}
		}

		public static void Show(Menu menu) {
			if( rootMenu == null ) {
				rootMenu = menu;
				rootMenu.Closed += Detach;
			}
		}

		public static void Hide() {
			if( rootMenu != null ) {
				rootMenu.Close();
				Detach(null, null);
			}
		}

		private static void Detach(object sender, object e) {
			if( rootMenu != null ) {
				rootMenu.Closed -= Detach;
				rootMenu = null;
			}
		}

		public override void OnInit() {
			Controls.Bind(Keys.Pause, () => TogglePause());
			Controls.Bind(Keys.Right, (Action)(() => {
				MenuScript.Show(new Menu(.4f, .4f, .2f)
					.Item("Trainer", new Menu(.4f, .4f, .2f)
						.Item(new InvincibleToggle()) // "Set Invincible", () => Call(SET_PLAYER_INVINCIBLE, true))
						.Item(new ClearWanted())
						.Item("Give Weapons", () => GiveWeapons(Self,
							(uint)WeaponHash.Pistol, 50,
							(uint)WeaponHash.PumpShotgun, 100,
							(uint)WeaponHash.CarbineRifle, 200,
							(uint)WeaponHash.Knife, 0,
							(uint)WeaponHash.Unarmed, 0)
						)
						.Item(new CarRepair())
					)
					.Item("Missions", new Menu(.4f, .4f, .2f)
						.Item("Mission01", new Menu(.4f, .4f, .2f)
							.Item("Start", () => StateMachine.Run(new WaitForControl(new Mission01_Approach())))
							.Item("GotoVault", () => StateMachine.Run(new Mission01_GotoVault()))
							.Item("MoveToCover", () => StateMachine.Run(new Mission01_MoveToCover()))
							.Item("MoveToButton", () => StateMachine.Run(new Mission01_MoveToButton()))
							.Item("KillAllCops", () => StateMachine.Run(new Mission01_KillAllCops()))
						)
					)
					.Item("Show Path To", new Menu(.4f, .4f, .2f)
						.Item("Trevor's House", () => StateMachine.Run(new DebugPath(new Vector3(1937.5f, 3814.5f, 33.4f))))
						.Item("Safehouse", () => StateMachine.Run(new DebugPath(Position(GetAllBlips(BlipSprite.Safehouse).FirstOrDefault()))))
						.Item("Red Blip", () => StateMachine.Run(new DebugPath(Position(GetAllBlips().FirstOrDefault(BlipHUDColor.Red)))))
						.Item("Green Blip", () => StateMachine.Run(new DebugPath(Position(GetAllBlips().FirstOrDefault(BlipHUDColor.Green)))))
						.Item("Yellow Blip", () => StateMachine.Run(new DebugPath(Position(GetAllBlips().FirstOrDefault(BlipHUDColor.Yellow)))))
						.Item("Aim Position", () => StateMachine.Run(new DebugPath(AimPosition())))
						.Item("Closest Ungrown", () => {
							Vector3 node = NavMesh.Flood(PlayerNode, 20000, 20, default, Edges)
								.Without(IsGrown)
								.Select(Position)
								.Min(DistanceToSelf);
							StateMachine.Run(new DebugPath(node));
						})
					)
					.Item("Walk To", new Menu(.4f, .4f, .2f)
						.Item("Trevor's House", () => StateMachine.Run(new MoveTo(new Vector3(1937.5f, 3814.5f, 33.4f))))
						.Item("Safehouse", () => StateMachine.Run(new MoveTo(Position(GetAllBlips(BlipSprite.Safehouse).FirstOrDefault()))))
						.Item("Wander", () => StateMachine.Run(new Wander()))
						.Item("Explore", () => StateMachine.Run(new Explore()))
						.Item("Yellow Blip", () => StateMachine.Run(new MoveTo(Position(GetAllBlips().FirstOrDefault(BlipHUDColor.Yellow)))))
						.Item("Blue Blip", () => StateMachine.Run(new MoveTo(Position(GetAllBlips().FirstOrDefault(BlipHUDColor.Blue)))))
						.Item("Green Blip", () => StateMachine.Run(new MoveTo(Position(GetAllBlips().FirstOrDefault(BlipHUDColor.Green)))))
						.Item("Red Blip", () => StateMachine.Run(new MoveTo(Position(GetAllBlips().FirstOrDefault(BlipHUDColor.Red)))))
					)
					.Item("Drive To", new Menu(.4f, .4f, .2f)
						.Item("Wander", (Action)(() => StateMachine.Run(new DriveWander(State.Idle) { Speed = 10f, DrivingFlags = VehicleDrivingFlags.Human })))
						.Item("Trevor's House", () => StateMachine.Run(new DriveTo(new Vector3(1983f, 3829f, 32f), State.Idle) { Speed = 10f }))
						.Item("Safehouse", () => StateMachine.Run(new DriveTo(Position(GetAllBlips(BlipSprite.Safehouse).FirstOrDefault()), State.Idle) { Speed = 15f }))
						.Item("Waypoint", () => StateMachine.Run(new DriveTo(Position(GetAllBlips(BlipSprite.Waypoint).FirstOrDefault()), State.Idle) { Speed = 10f }))
						.Item("Yellow Blip", () => StateMachine.Run(new DriveTo(Position(GetAllBlips().FirstOrDefault(BlipHUDColor.Yellow)), State.Idle)))
						.Item("Green Blip", () => StateMachine.Run(new DriveTo(Position(GetAllBlips().FirstOrDefault(BlipHUDColor.Green)), State.Idle)))
						.Item("Blue Blip", () => StateMachine.Run(new DriveTo(Position(GetAllBlips().FirstOrDefault(BlipHUDColor.Blue)), State.Idle)))
					)
					.Item("Clear Region", () => {
						AllNodes.Regions.TryRemove(Region(PlayerNode), out var ignore);
					})
					.Item("Save NavMesh", () => NavMesh.SaveToFile())
					.Item("Press Key", new Menu(.4f, .4f, .2f)
						.Item("Context", () => PressControl(2, Control.Context, 200))
						.Item("ScriptRUp", () => PressControl(2, Control.ScriptRUp, 200))
						.Item("ScriptRLeft", () => PressControl(2, Control.ScriptRLeft, 200))
					)
				// .Item("Cancel", () => MenuScript.Hide())
				);
			}));
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

				// StateMachine.Run(new DebugPath(AimPosition()));

				// Heap<int>.Test();

				/*
				NavMesh.Flood(PlayerNode, 20000, 20, default, Edges)
					.Without(IsGrown)
					.Select(Position)
					.OrderBy(DistanceToSelf)
					.Take(1)
					.Each(v => Line.Add(HeadPosition(Self), v, Color.Red, 5000));
				Log($"Flood took {sw.ElapsedMilliseconds}ms");
				*/

				/*
				var producer = NavMesh.FloodThread(PlayerNode, 4000, 100, Edges);
				var nodes = producer.Wait(2000, 20); // wait 20 ms to get up to 1000 nodes
				int count = 0;
				foreach( var node in nodes ) {
					count += 1;
					Line.Add(PlayerPosition, Position(node), Color.Red, 2000);
				}
				Log($"Node count: {count}");
				*/

				/*
				Goals.Immediate(new QuickGoal(() => {
					while( producer.TryConsume(out NodeHandle node) ) {
						if( ! IsGrown(node) ) {
							Line.Add(PlayerPosition, Position(node), Color.Red, 2000);
						}
					}
					Log($"Flood visited {producer.Count} lines");
					if( producer.IsClosed && producer.IsEmpty ) {
						Log($"Flood finished");
						return GoalStatus.Complete;
					}
					return GoalStatus.Active;
				}));
				*/
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
				AimTarget = Vector3.Zero;
				AimAtHead = PedHandle.Invalid;
				KillTarget = PedHandle.Invalid;
				WalkTarget = Vector3.Zero;
				PathStatus.CancelAll();
				StateMachine.Clear();
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
				// NavMesh.UpdateClearanceFromCollision(AimNode());
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
						switch( SteerToward(p, 20f, 1f, false) ) {
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
				StateMachine.Run((state) => {
					var target = NearbyHumans().FirstOrDefault();
					var head = HeadPosition(target);
					var vel = Velocity(target);
					int line = 0;
					UI.DrawTextInWorldWithOffset(head, 0f, (line++ * .02f), $"V:{Round(vel, 2)}");
					var spot = head + (vel * 8f / CurrentFPS);
					DrawSphere(spot, .06f, Color.Yellow);
					DrawSphere(head, .12f, Color.Red);
					DrawLine(head, spot, Color.Yellow);
					AimTarget = spot;
					return state;
				});
			});

			NodeHandle pathTarget = NodeHandle.Invalid;
			int repathEvery = 0;
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
						if( path.Length < 2 || path.IsComplete() ) {
							return null;
						}
						if( repathEvery > 0 && (GameTime - started) > repathEvery ) { // || DistanceToSelf(path.NextStep()) > 20f ) {
							req = new PathRequest(PlayerNode, pathTarget, 3000, false, true, true, 1, false);
							started = GameTime;
						}
						foreach(var n in nodePath.Take(30) ) {
							DrawSphere(Position(n), .04f, Color.Yellow);
						}
						var head = HeadPosition(Self);
						DrawLine(head, path.NextStep(), Color.Orange);


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
				StateMachine.Run("GetHandle", (state) => {
					var node = AimNode();
					var p = Position(node);
					var r = Region(node);
					var head = HeadPosition(Self);
					DrawLine(head, p, Color.Red);
					int line = 0;
					UI.DrawTextInWorld(p, $"n: {node}");
					UI.DrawTextInWorldWithOffset(p, 0f, (++line * .02f), $"{Region(node)}, {(ulong)node & handleMask}");
					if( IsGrown(node) ) {
						DrawSphere(p, .02f, Color.Orange);
					} else {
						DrawSphere(p, .02f, Color.Green);
					}
					foreach( var e in Edges(node)) {
						line = 0;
						var end = Position(e) + (Up * .5f);
						DrawLine(p + (Up * .5f), end, Color.White);
						UI.DrawTextInWorldWithOffset(end, 0f, (line++ * .02f), $"e:{e}");
						UI.DrawTextInWorldWithOffset(end, 0f, (line++ * .02f), $"{Region(e)}, {(ulong)e & handleMask}");
					}
				
					foreach( var e in PossibleGrowthEdges(node) ) {
						line = 0;
						var ePos = Position(e);
						var eGround = PutOnGround(ePos, 1f);
						NodeHandle g = Handle(eGround);
						if( IsPossibleEdge(node, g) ) {
							var gPos = Position(g);
							UI.DrawTextInWorldWithOffset(gPos, 0f, (line++ * .02f), $"g: {g}");
							UI.DrawTextInWorldWithOffset(gPos, 0f, (line++ * .02f), $"{Region(g)}, {(ulong)g & handleMask}");
							if( IsGrown(g) ) {
								DrawSphere(gPos, .02f, Color.Orange);
							} else {
								DrawSphere(gPos, .02f, Color.Green);
							}
							DrawLine(p, gPos, IsPossibleEdge(node, g) ? Color.Green : Color.Orange);
						}
					}
					/*
					const ulong mapRadius = 8192; // in the world, the map goes from -8192 to 8192
					const float gridScale = .5f; // how big are the X,Y steps of the mesh
					const float zScale = .25f; // how big are the Z steps of the mesh
					const float zDepth = 1000f; // how deep underwater can the mesh go
					const float regionScale = 128f; // how wide on each side is one region cube
					const int regionShift = 7; // we use 7 bits each for X,Y,Z = 21 bits when we pack into RegionHandle
					const int mapShift = 15; //  (int)Math.Log(mapRadius * 2 / gridScale, 2);
					Vector3 v = PlayerPosition;

					// v.X starts [-8192..8192] becomes [0..32k]
					ulong x1 = (ulong)(v.X + mapRadius);
					ulong y1 = (ulong)(v.Y + mapRadius);
					ulong z1 = (ulong)(v.Z + zDepth);
					UI.DrawText($"x1:{x1} y1:{y1} z1:{z1}");
					ulong nx = (ulong)(x1 << 1); // << 1 equivalent to / gridScale (/.5f) == (*2) == (<<1)
					ulong ny = (ulong)(y1 << 1);
					ulong nz = (ulong)(z1 << 2); // << 2 equivalent to / zScale (/.25f) == (* 4) or (<< 2)
					UI.DrawText($"nx:{nx} ny:{ny} nz:{nz}");
					uint rx = (uint)(x1 >> regionShift); // Round((v.X + mapRadius) / regionScale)); // (/128) == (>>7)
					uint ry = (uint)(y1 >> regionShift);
					uint rz = (uint)(z1 >> regionShift);
					UI.DrawText($"rx:{rx} ry:{ry} rz:{rz}");
					ulong r = ((rx << (regionShift << 1)) | (ry << regionShift) | rz);
					ulong n =
						(nx << (mapShift << 1)) |
						(ny << mapShift) |
						nz;
					UI.DrawText($"n:{n} r:{n}");
					var node = (NodeHandle)(
						(r << (mapShift * 3)) |
						(nx << (mapShift << 1)) |
						(ny << mapShift) |
						nz
					);
					UI.DrawText($"node: {node} {Position(node)}");
					*/
					return state;
				});
			});

			Controls.Bind(Keys.M, () => {
				NodeHandle mark = AimNode();
				Path path = null;
				var req = new PathRequest(PlayerNode, mark, 1000, false, true, true, 1, true);
				var sw = new Stopwatch();
				sw.Start();
				StateMachine.Run("Draw Marked", (state) => {
					DrawSphere(Position(mark), .1f, Color.Yellow);
					if( req.IsReady() ) {
						path = req.GetResult();
					}
					if( req.IsDone() && sw.ElapsedMilliseconds > 300 ) {
						req = new PathRequest(PlayerNode, mark, 1000, false, true, true, 1, true);
						sw.Restart();
					}
					if( path != null ) {
						while( DistanceToSelf(path.FirstOrDefault()) < .15f ) {
							path.Pop();
						}
						if( path.Count() < 2 ) {
							path = null;
							return state;
						}
						req.Blocked.Select(Position).OrderBy(DistanceToSelf).Take(100).Each(DrawSphere(.02f, Color.Red));
						path.Draw();
						var steps = Items(PlayerPosition).Concat(path.Take(4).Select(Position)).ToArray();
						var first = steps.Skip(1).First();
						var second = steps.Skip(2).First();
						var step = Vector3.Lerp(first, second, Clamp(1f - DistanceToSelf(first), 0f, 1f));
						UI.DrawTextInWorld(first, $"{DistanceToSelf(first):F2}");
						//Bezier(.3f, steps);
						DrawLine(HeadPosition(Self), step, Color.Orange);
						DrawSphere(Interp.Bezier(.2f, steps), .02f, Color.Orange);
						DrawSphere(Interp.Bezier(.4f, steps), .02f, Color.Orange);
						DrawSphere(Interp.Bezier(.6f, steps), .02f, Color.Orange);
						DrawSphere(Interp.Bezier(.8f, steps), .02f, Color.Orange);
						DrawSphere(Interp.Bezier(1f, steps), .02f, Color.Orange);
					}
					return state;
				});
			});
		}
	}

}
