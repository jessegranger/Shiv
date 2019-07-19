using System;
using System.Drawing;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using static Shiv.Global;
using static GTA.Native.Function;
using static GTA.Native.Hash;
using GTA.Native;

namespace Shiv {
	public static partial class UI {
		private static readonly float originX = .44f;
		private static readonly float originY = .01f;
		private static float lastX = originX;
		private static float lastY = originY;
		private struct DrawTextCommand {
			internal float x;
			internal float y;
			internal float scale;
			internal int font;
			internal string text;
			internal Color color;
		}
		private struct DrawRectCommand {
			internal float x;
			internal float y;
			internal float w;
			internal float h;
			internal Color color;
		}
		private struct DrawSubtitleCommand {
			internal string text;
			internal int dur;
			internal bool urgent;
		}

		private class Pool<T> where T : new() {
			private ConcurrentStack<T> free = new ConcurrentStack<T>();
			public T GetItem() => free.TryPop(out T ret) ? ret : new T();
			public void Release(T item) => free.Push(item);
		}
		private static Pool<DrawTextCommand> textPool = new Pool<DrawTextCommand>();

		private static ConcurrentQueue<DrawTextCommand> textQueue = new ConcurrentQueue<DrawTextCommand>();
		private static ConcurrentQueue<DrawRectCommand> rectQueue = new ConcurrentQueue<DrawRectCommand>();
		private static ConcurrentQueue<DrawSubtitleCommand> subtitleQueue = new ConcurrentQueue<DrawSubtitleCommand>();
		internal static void OnTick() {
			while( rectQueue.TryDequeue(out DrawRectCommand cmd) ) {
				DrawRect(cmd);
			}
			while( textQueue.TryDequeue(out DrawTextCommand cmd) ) {
				DrawText(cmd);
				textPool.Release(cmd);
			}
			while( subtitleQueue.TryDequeue(out DrawSubtitleCommand cmd) ) {
				ShowSubtitle(cmd);
			}
			lastX = originX;
			lastY = originY;
			headlineCounts.Clear();
		}
		public static void DrawText(string text, float scale = .4f, int font = 4, Color color = default) => DrawText(lastX, lastY += .019f, text, scale, font, color);
		public static void DrawText(float x, float y, string text, float scale = .4f, int font = 4) => DrawText(x, y, text, scale, font, Color.White);
		public static void DrawTextInWorldWithOffset(Vector3 pos, float dx, float dy, string text) => DrawTextInWorld(pos, text, .4f, 4, Color.White, dx, dy);
		public static void DrawTextInWorld(Vector3 pos, string text) => DrawTextInWorld(pos, text, .4f, 4, Color.White, 0, 0);
		public static void DrawTextInWorld(Vector3 pos, string text, float scale, int font, Color color, float dx, float dy) {
			var sPos = ScreenCoords(pos);
			DrawText(sPos.X + dx, sPos.Y + dy, text, scale, font, color);
		}

		private static ConcurrentDictionary<PedHandle, int> headlineCounts = new ConcurrentDictionary<PedHandle, int>();
		public static void DrawHeadline(string text) => DrawHeadline(Self, text);
		public static void DrawHeadline(PedHandle ped, string text) {
			headlineCounts.TryGetValue(ped, out int line);
			DrawTextInWorldWithOffset(HeadPosition(ped), 0f, (line++ * .02f), text);
			headlineCounts.AddOrUpdate(ped, line, (p, old) => Math.Max(old+1,line));
		}
		// 		=> DrawTextInWorldWithOffset(HeadPosition(Self), 0f, (headlineCount++ * .02f), text);
		public static void DrawText(float x, float y, string text, float scale, int font, Color color) {
			if( color == default ) {
				color = Color.White;
			}

			var cmd = textPool.GetItem();
			cmd.x = x;
			cmd.y = y;
			cmd.text = text;
			cmd.color = color;
			cmd.scale = scale;
			cmd.font = font;
			textQueue.Enqueue(cmd);
		}
		private static void DrawText(DrawTextCommand cmd) {
			using( var text = new PinnedString(cmd.text.Substring(0, Math.Min(99, cmd.text.Length))) ) {
				Call(SET_TEXT_FONT, cmd.font);
				Call(SET_TEXT_SCALE, cmd.scale, cmd.scale);
				Call(SET_TEXT_COLOUR, cmd.color);
				Call(BEGIN_TEXT_COMMAND_DISPLAY_TEXT, PinnedString.CELL_EMAIL_BCON);
				Call(ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, text);
				Call(END_TEXT_COMMAND_DISPLAY_TEXT, cmd.x, cmd.y);
			}
		}
		public static void ShowSubtitle(string text, int duration, bool urgent = false) => subtitleQueue.Enqueue(new DrawSubtitleCommand() { text = text, dur = duration, urgent = urgent });
		private static void ShowSubtitle(DrawSubtitleCommand cmd) {
			using( var text = new PinnedString(cmd.text) ) {
				Call(BEGIN_TEXT_COMMAND_PRINT, PinnedString.STRING);
				Call(ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, text);
				Call(END_TEXT_COMMAND_PRINT, cmd.dur, cmd.urgent);
			}
		}
		public static void DrawRect(float x, float y, float w, float h, Color color) => rectQueue.Enqueue(new DrawRectCommand() { x = x, y = y, w = w, h = h, color = color });
		private static void DrawRect(DrawRectCommand cmd) => Call(DRAW_RECT, cmd.x + (cmd.w / 2), cmd.y + (cmd.h / 2), cmd.w, cmd.h, cmd.color.R, cmd.color.G, cmd.color.B, cmd.color.A);

		private static float border = .002f;
		public static void DrawMeter(float x, float y, float w, float h, Color outside, Color inside, float percent, string label, Color labelColor) {
			DrawRect(x, y, w, h, outside);
			DrawRect(x + border, y + border, (w - border) * percent, h - border, inside);
			DrawText(x + border, y - border, label, .4f, 4, labelColor);
		}
		public static void DrawMeter(float x, float y, float w, float h, Color outside, Color inside, float percent) {
			DrawRect(x, y, w, h + border, outside);
			DrawRect(x + border, y + border, (w - border) * percent, h - border, inside);
		}
		public static void DrawMeterInWorld(Vector3 pos, float w, float h, Color outside, Color inside, float percent) {
			var sPos = ScreenCoords(pos);
			DrawMeter(sPos.X - (w / 2), sPos.Y - (h / 2), w, h, outside, inside, percent);
		}
	}

	public class UIScript : Script {
		public override void OnTick() => UI.OnTick();
	}
}
