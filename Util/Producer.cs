using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using static Shiv.Global;

// pragma warning disable CS0649
namespace Shiv {

	public class Producer<T> : IDisposable {
		private ConcurrentQueue<T> buf = new ConcurrentQueue<T>();
		private CountdownEvent counter = null;
		private CancellationTokenSource cancel = new CancellationTokenSource();
		public ulong Count { get; private set; } = 0;
		public ulong Limit = ulong.MaxValue;
		public bool IsCancellationRequested => cancel.IsCancellationRequested;
		public virtual void Cancel() {
			cancel.Cancel();
			Close();
		}
		public virtual bool IsClosed { get; private set; } = false;
		public virtual bool IsEmpty => buf.IsEmpty;
		public virtual void Close() {
			IsClosed = true;
			if( counter != null ) {
				counter.Signal(counter.CurrentCount);
			}
		}
		public virtual bool TryConsume(out T item) => buf.TryDequeue(out item);
		public virtual bool Produce(T item) {
			if( Count >= Limit ) {
				cancel.Cancel();
				return false;
			}
			buf.Enqueue(item);
			Count += 1;
			if( counter != null && !counter.IsSet ) {
				counter.Signal();
			}
			return true;
		}
		// TODO: can currently only have a single waiter per producer
		public virtual IEnumerable<T> Wait(int count, int timeout) {
			if( counter == null ) {
				counter = new CountdownEvent(count);
				counter.Wait(timeout);
				counter = null;
				while( count > 0 && buf.TryDequeue(out T item) ) {
					yield return item;
					count -= 1;
				}
			}
		}

		#region IDisposable Support
		private bool disposed = false; // To detect redundant calls

		protected virtual void Dispose(bool disposing) {
			if( !disposed ) {
				if( disposing ) {
					// TODO: dispose managed state (managed objects).
					buf.Clear();
					cancel.Cancel();
					cancel.Dispose();
				}

				buf = null;
				cancel = null;

				// TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
				// TODO: set large fields to null.

				disposed = true;
			}
		}

		// TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
		// ~Producer() {
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
	public class DistinctProducer<T> : Producer<T> {
		private HashSet<T> seen = new HashSet<T>();
		public override bool Produce(T item) {
			if( !seen.Contains(item) ) {
				seen.Add(item);
				return base.Produce(item);
			}
			return false;
		}
	}

}
