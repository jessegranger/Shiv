using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using Keys = System.Windows.Forms.Keys;
using static GTA.Native.Hash;
using static GTA.Native.Function;
using static Shiv.Global;

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
		public Menu Item(string label, Action click) {
			Items.Add(new MenuItem(label, click));
			H += ItemHeight;
			return this;
		}
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
					UI.DrawText(x + BorderWidth, y + BorderWidth, Items[i].Label);
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
						case Keys.Up: menu.Up(); return true;
						case Keys.Down: menu.Down(); return true;
						case Keys.Right: menu.Activate(); return true;
						case Keys.Left: menu.Back(); return true;
						case Keys.Back: menu.Back(); return true;
						case Keys.End: Hide(); return true;
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

				Log("Starting scan for something ungrown.");
				var future = new Future<NodeHandle>(() => {
					Log("Starting work inside thread.");
					try {
						return NavMesh.FirstOrDefault(PlayerNode, 100, (n) => !NavMesh.IsGrown(n));
					} finally {
						Log("Finished work inside thread.");
					}
				});
				Goals.Immediate(new QuickGoal("Find Growable", () => {
					if( future.IsFailed() ) {
						Log("Scan failed.");
						return GoalStatus.Failed;
					}
					if( future.IsReady() ) {
						var node = future.GetResult();
						Log($"Scan found: {node} at range {DistanceToSelf(node)}");
						NavMesh.Grow(node, 10);
						return GoalStatus.Complete;
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
			});
			Controls.Bind(Keys.X, () => {
				NavMesh.Visit(PlayerNode, 2, (node) => NavMesh.Remove(node));
			});
			Controls.Bind(Keys.B, () => {
				NavMesh.ShowEdges = !NavMesh.ShowEdges;
			});
		}
	}

}
