using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Shiv.Globals;
using static GTA.Native.Function;
using static GTA.Native.Hash;
using System.Drawing;
using Keys = System.Windows.Forms.Keys;

namespace Shiv {
	public class ScreenRect {
		public float X, Y, W, H;
		public float Right { get { return X + W; } }
		public float Bottom { get { return Y + H; } }
		public ScreenRect(float x, float y, float w, float h) {
			X = x;
			Y = y;
			W = h;
			H = h;
		}
	}
	public class HudElement : ScreenRect {
		public bool IsVisible;
		public HudElement(float x, float y, float w, float h) : base(x, y, w, h) { }
		public void Hide() { IsVisible = false; }
		public void Show() { IsVisible = true; }
	}
	public class MenuItem {
		public string Label;
		public Action OnClick = null;
		public Menu SubMenu = null;
		public MenuItem(string label) { Label = label; }
		public MenuItem(string label, Action func) { Label = label; OnClick = func; }
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
		public Menu(float x, float y, float w) : base(x, y, w, 0) {
			MenuWidth = w;
		}
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
			if( i < 0 )
				i = Items.Count - 1;
			if( i >= Items.Count )
				i = 0;
			return i;
		}
		public void Up() { HighlightIndex = Wrap(HighlightIndex, -1); }
		public void Down() { HighlightIndex = Wrap(HighlightIndex, +1); }
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

			if( SubMenu.Parent == this )
				SubMenu = null;
			else
				SubMenu = SubMenu.Parent;
		}
		public void Draw() {
			if( SubMenu != null )
				SubMenu.Draw();
			else {
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
		public void Close() {
			Closed?.Invoke(null, null);
		}
	}

	public class MenuScript : Script {
		public MenuScript() { }

		public override void OnKey(Keys key, bool downBefore, bool upNow) {
			if( rootMenu != null ) {
				var menu = rootMenu;
				while( menu.SubMenu != null ) {
					menu = menu.SubMenu;
				}
				if( upNow ) {
					switch( key ) {
						case Keys.End: menu = null; break;
						case Keys.Up: menu.Up(); break;
						case Keys.Down: menu.Down(); break;
						case Keys.Right: menu.Activate(); break;
						case Keys.Left: menu.Back(); break;
					}
				}
			}
		}

		public override void OnAbort() {
			rootMenu = null;
		}

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
	}
}
