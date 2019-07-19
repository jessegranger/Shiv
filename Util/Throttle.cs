
using System;
using System.Diagnostics;

namespace Shiv {
	public static partial class Global {

		/// <summary>
		/// Only call the given function at most once per period.
		/// </summary>
		/// <returns>A new Action that, if invoked too frequently, will return prior results.</returns>
		public static Func<T> Throttle<T>(uint ms, Func<T> function) {
			var s = new Stopwatch();
			s.Start();
			T prev = default;
			return () => {
				if( prev == default || s.ElapsedMilliseconds >= ms ) {
					s.Restart();
					return prev = function();
				}
				return prev;
			};
		}

		/// <summary>
		/// Only call the given function at most once per frame.
		/// </summary>
		/// <returns>A new Action that, if invoked too frequently, will return prior results.</returns>
		public static Func<T> FrameThrottle<T>(Func<T> function) {
			T prev = default;
			uint lastFrame = 0;
			return () => {
				if( lastFrame != FrameCount ) {
					lastFrame = FrameCount;
					return prev = function();
				}
				return prev;
			};
		}

	}
}