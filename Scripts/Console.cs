﻿using System;
using System.Collections.Generic;
using System.Linq;
using static Shiv.Global;
using System.Drawing;
using Keys = System.Windows.Forms.Keys;
using System.Collections.Concurrent;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Text;

namespace Shiv {
	public class Console : Script {

		public static Keys Toggle = Keys.F5;
		public static Color Background = Color.Blue;
		public static Color Foreground = Color.White;
		public static int StickyLines = 5;

		const float lineHeight = .019f;
		const float panelHeight = .5f;
		const float panelWidth = .6f;
		const float closedX = .25f;
		const int visibleLines = (int)(panelHeight / lineHeight) - 1;
		static ConcurrentQueue<string> output = new ConcurrentQueue<string>();
		static int outputScrollback = 0;
		static string inputLine = "";
		static int cursor = 0;
		static List<string> inputHistory = new List<string>();
		static int inputIndex = 0;
		private static readonly float stickyLineOffset = 2 * lineHeight;

		public static bool IsOpen { get; internal set; } = false;
		public static void Open() => IsOpen = true;
		public static void Close() => IsOpen = false;

		public static void Log(params string[] msgs) => Log(string.Join(" ", msgs));
		public static void Log(string msg) {
			while( msg.Length > 100 ) {
				Log(msg.Substring(0, 99));
				msg = msg.Substring(99);
			}
			output.Enqueue(msg);
			while( output.Count > 1000 ) {
				output.TryDequeue(out string discard);
			}
		}

		public override void OnTick() {
			int lineNum = 0;
			if( IsOpen ) {
				Controls.DisableAllThisFrame(this.GetType());
				UI.DrawRect(0f, 0f, panelWidth, panelHeight, Background);
				foreach( string line in output.Skip(output.Count - (visibleLines + outputScrollback)).Take(visibleLines) ) {
					UI.DrawText(0f, (lineNum++) * lineHeight, line, .4f, 4, Foreground);
				}
				string a = inputLine.Substring(0, cursor);
				string b = inputLine.Substring(cursor);
				UI.DrawText(0f, (visibleLines) * lineHeight, $"[{inputIndex}]> {a}|{b}", .4f, 4, Foreground);
			} else {
				foreach( string line in output.Skip(output.Count - StickyLines).Take(StickyLines) ) {
					UI.DrawText(closedX, .99f - stickyLineOffset - (StickyLines * lineHeight) + (lineNum++) * lineHeight, line, .4f, 4, Foreground);
				}
				UI.DrawText(closedX, .99f - stickyLineOffset, 
					$"{TotalTime.Elapsed.ToString().Substring(0,8)} FPS:{CurrentFPS:F0} (Nav:{NavMesh.Ungrown.Count}/{NavMesh.Count}) (H:{NearbyHumans.Length} V:{NearbyVehicles.Length} E:{NearbyObjects.Length})");
			}
		}

		private bool shiftDown = false;
		private bool ctrlDown = false;

		[DllImport("user32.dll")]
		private static extern int ToUnicode(uint virtualKeyCode, uint scanCode, byte[] keyboardState,
		[Out, MarshalAs(UnmanagedType.LPWStr, SizeConst = 64)]
		StringBuilder receivingBuffer,
		int bufferSize, uint flags);

		private string GetChar(Keys keys) {
			var buf = new StringBuilder(256);
			var keyboardState = new byte[256];
			if (shiftDown) {
				keyboardState[(int)Keys.ShiftKey] = 0xff;
			}

			if (ctrlDown) {
				keyboardState[(int)Keys.ControlKey] = 0xff;
				keyboardState[(int)Keys.Menu] = 0xff;
			}
			ToUnicode((uint)keys, 0, keyboardState, buf, 256, 0);
			return buf.ToString();
		}
		private void AddKey(Keys key) => AddString(GetChar(key));
		private void AddString(string c) {
			string a = inputLine.Substring(0, cursor);
			string b = inputLine.Substring(cursor);
			inputLine = a + c + b;
			cursor += c.Length;
		}

		private bool IsValidInput(Keys key) =>
			key == Keys.Space
			|| (key >= Keys.D0 && key <= Keys.Z)
			|| (key >= Keys.Multiply && key <= Keys.Divide)
			|| (key >= Keys.OemSemicolon && key <= Keys.OemBackslash)
			|| (key >= (Keys.Shift | Keys.D0) && key <= (Keys.Shift | Keys.Z))
			|| (key >= (Keys.Shift | Keys.OemSemicolon) && key <= (Keys.Shift | Keys.OemBackslash))
			;
			

		public override bool OnKey(Keys key, bool wasDownBefore, bool isUpNow) {
			switch( key ) {
				case Keys.ShiftKey: shiftDown = !isUpNow; break;
				case (Keys.ShiftKey | Keys.Shift): shiftDown = !isUpNow; break;
				case Keys.ControlKey: ctrlDown = !isUpNow; break;
				case (Keys.ControlKey | Keys.Control): ctrlDown = !isUpNow; break;
			}
			bool onDown = !wasDownBefore && !isUpNow;
			bool onRepeat = wasDownBefore && !isUpNow;
			bool onUp = wasDownBefore && isUpNow;
			// if( wasDownBefore && !isUpNow ) { addString($"(rep {key})"); }
			// if( wasDownBefore && isUpNow ) { addString($"(up {key})"); }
			if( onDown || onRepeat ) {
				if( key == Toggle ) {
					if( IsOpen ) {
						Close();
					} else {
						Open();
					}
					return true;
				}
				if( IsOpen ) {
					if( key == Keys.Back ) {
						if( inputLine.Length > 0 ) {
							string a = inputLine.Substring(0, cursor);
							string b = inputLine.Substring(cursor);
							if( a.Length > 0 ) {
								inputLine = a.Substring(0, a.Length - 1) + b;
								cursor -= 1;
							}
						}
					} else if( key == Keys.Delete ) {
						if( inputLine.Length > 0 ) {
							string a = inputLine.Substring(0, cursor);
							string b = inputLine.Substring(cursor);
							if( b.Length > 0 ) {
								inputLine = a + b.Substring(1);
							}
						}
					} else if( key == Keys.Left ) {
						cursor = Math.Max(0, cursor - 1);
					} else if( key == Keys.Right ) {
						cursor = Math.Min(inputLine.Length, cursor + 1);
					} else if( key == Keys.Home ) {
						cursor = 0;
					} else if( key == Keys.End ) {
						cursor = inputLine.Length;
					} else if( key == Keys.Up ) {
						inputIndex = Math.Max(0, inputIndex - 1);
						inputLine = inputHistory[inputIndex];
						cursor = inputLine.Length;
					} else if( key == Keys.Down ) {
						if( inputIndex >= inputHistory.Count - 1 ) {
							inputLine = "";
							cursor = 0;
							inputIndex = inputHistory.Count;
						} else {
							inputIndex = Math.Min(inputHistory.Count - 1, inputIndex + 1);
							inputLine = inputHistory[inputIndex];
							cursor = inputLine.Length;
						}
					} else if( key == Keys.PageUp ) {
						outputScrollback = Math.Min(output.Count - visibleLines, outputScrollback + 4);
					} else if( key == Keys.PageDown ) {
						outputScrollback = Math.Max(0, outputScrollback - 4);
					} else if( key == Keys.Enter ) {
						RunCommand(inputLine);
						inputHistory.Add(inputLine);
						inputIndex = inputHistory.Count;
						inputLine = "";
						cursor = 0;
						outputScrollback = 0;
					} else if( IsValidInput(key) ) {
						AddKey(key);
					} else {
						return false;
					}
					// if( key != Keys.Back ) addString(key.ToString());
					return true;
				}
			}
			return false;
		}

		public void RunCommand(string cmd) {
			string[] chunks = cmd.Split(' ');
			Log(string.Join(" ", chunks.Select(GenerateHash)));
		}

	}
}
