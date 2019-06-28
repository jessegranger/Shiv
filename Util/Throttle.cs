
using System;
using System.Diagnostics;

namespace Shiv {
	public static partial class Global {
		/// <summary>
		/// Only call the given function at most once per period.
		/// </summary>
		/// <param name="ms"></param>
		/// <param name="function"></param>
		/// <returns>A new Action that, if invoked too frequently, will not invoke your function.</returns>
		public static Action Throttle(uint ms, Action function) {
			var s = new Stopwatch();
			s.Start();
			return () => {
				if( s.ElapsedMilliseconds >= ms ) {
					function();
					s.Restart();
				}
			};
		}
	}
}