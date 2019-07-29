using System;
using System.Collections.Concurrent;
using System.Threading;

// pragma warning disable CS0649
namespace Shiv {
	/// <summary>
	/// A waitable ConcurrentQueue.
	/// </summary>
	public class Consumer<T> : IDisposable {
		private BlockingCollection<T> buf = new BlockingCollection<T>();
		private CancellationTokenSource cancel = new CancellationTokenSource();

		public int Count => buf.Count;

		/// <summary>
		/// Add an item to the queue. Signal to any threads blocked on WaitDequeue().
		/// </summary>
		public void Enqueue(T item) => buf.Add(item);

		/// <summary>
		/// Send a Cancel request to the default CancellationToken.
		/// </summary>
		public void Cancel() => cancel.Cancel();

		/// <summary>
		/// Blocks until an item is available. Observes the default CancellationToken.
		/// </summary>
		public bool WaitDequeue(out T result) => WaitDequeue(cancel.Token, out result);

		/// <summary>
		/// Blocks until an item is available, while observing a custom CancellationToken.
		/// </summary>
		internal bool WaitDequeue(CancellationToken cancel, out T result) {
			try {
				result = buf.Take(cancel);
				return true;
			} catch( Exception ) {
				result = default;
				return false;
			}
		}

		#region IDisposable Support
		private bool disposed = false; // To detect redundant calls

		protected virtual void Dispose(bool disposing) {
			if( !disposed ) {
				if( disposing ) {
					// TODO: dispose managed state (managed objects).
					cancel.Cancel();
					cancel.Dispose();
					buf.Dispose();
				}

				// TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
				// TODO: set large fields to null.

				disposed = true;
			}
		}

		// TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
		// ~Consumer() {
		//   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
		//   Dispose(false);
		// }

		// This code added to correctly implement the disposable pattern.
		public void Dispose() {
			// Do not change this code. Put cleanup code in Dispose(bool disposing) above.
			Dispose(true);
			// TODO: uncomment the following line if the finalizer is overridden above.
			// GC.SuppressFinalize(this);
		}
		#endregion
	}
}
