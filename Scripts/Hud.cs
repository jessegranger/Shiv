using System;
using System.Text;
using System.Threading.Tasks;
using static GTA.Native.Function;
using static GTA.Native.Hash;
using Keys = System.Windows.Forms.Keys;
using System.Windows.Forms;
using System.Drawing;

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
		public Color Color;
		public HudElement(float x, float y, float w, float h) : base(x, y, w, h) { }
		public void Hide() { IsVisible = false; }
		public void Show() { IsVisible = true; }
	}


}
