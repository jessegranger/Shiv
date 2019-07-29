using System;
using System.Linq;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace Shiv {

	public static partial class Global {
		public interface IFuture<T> {
			T GetResult();
			T WaitResult();
			T WaitResult(uint timeout);
			Exception GetError();
			bool IsDone();
			bool IsReady();
			bool IsFailed();
			bool IsCanceled();
			IFuture<T> Resolve(T item);
			void Reject(Exception err);
			void Wait();
			void Wait(uint timeout);
			void Cancel();
		}
		public class Future<T> : IFuture<T>, IDisposable {
			private T result = default;
			private Exception error;
			protected CancellationTokenSource cancel = new CancellationTokenSource();
			private CountdownEvent ready = new CountdownEvent(1);

			private ReaderWriterLockSlim guard = new ReaderWriterLockSlim();
			private class WriteLock : IDisposable {
				private Future<T> f;
				public WriteLock(Future<T> future) => (f = future).guard.EnterWriteLock();
				public void Dispose() => f.guard.ExitWriteLock();
			}
			private class ReadLock : IDisposable {
				private Future<T> f;
				public ReadLock(Future<T> future) => (f = future).guard.EnterReadLock();
				public void Dispose() => f.guard.ExitReadLock();
			}

			public Future() { }

			public Future(Func<T> func) : this() {
				ThreadPool.QueueUserWorkItem((object arg) => {
					try { Resolve(func()); } catch( Exception err ) { Reject(err); }
				});
			}
			public Future(Func<CancellationToken, T> func) : this() {
				ThreadPool.QueueUserWorkItem((object arg) => {
					try { Resolve(func(cancel.Token)); } catch( Exception err ) { Reject(err); }
				});
			}
			public T GetResult() {
				using( new ReadLock(this) ) {
					return result;
				}
			}
			public bool TryGetResult(out T result) {
				if( ready.IsSet ) {
					using( new ReadLock(this) ) {
						result = this.result;
						return true;
					}
				}
				result = default;
				return false;
			}

			public Exception GetError() { using( new ReadLock(this) ) { return error; } }
			public bool IsDone() => IsFailed() || IsCanceled() || IsReady();
			public bool IsFailed() { using( new ReadLock(this) ) { return error != null; } }
			public bool IsReady() { using( new ReadLock(this) ) { return error == null && ready.IsSet; } }
			public IFuture<T> Resolve(T item) {
				using( new WriteLock(this) ) {
					result = item;
					ready.Signal();
				}
				return this;
			}
			public void Reject(Exception err) {
				using( new WriteLock(this) ) {
					result = default;
					error = err;
					ready.Signal();
				}
			}
			public void Wait() => ready.Wait(cancel.Token);
			public void Wait(uint timeout) => ready.Wait((int)timeout, cancel.Token);
			public T WaitResult() => WaitResult(uint.MaxValue);
			public T WaitResult(uint timeout) {
				try {
					ready.Wait((int)timeout, cancel.Token);
					return GetResult();
				} catch( OperationCanceledException ) {
					return default;
				}
			}
			public void Cancel() { try { cancel.Cancel(); } catch( Exception ) { } }
			public bool IsCanceled() => cancel.IsCancellationRequested;

			#region IDisposable Support
			private bool disposed = false; // To detect redundant calls

			protected virtual void Dispose(bool disposing) {
				if( !disposed ) {
					if( disposing ) {
						// TODO: dispose managed state (managed objects).
						if( !cancel.IsCancellationRequested ) {
							cancel.Cancel();
						}
						if( !ready.IsSet ) {
							ready.Signal();
						}
					}

					// TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
					// TODO: set large fields to null.

					disposed = true;
				}
			}

			// TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
			// ~Future() {
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

		public class Immediate<T> : IFuture<T> { // a dummy future with no locks
			private readonly T result;
			public Immediate(T item) => result = item;
			public Exception GetError() => null;
			public T GetResult() => result;
			public bool IsDone() => true;
			public bool IsFailed() => false;
			public bool IsReady() => true;
			public bool IsCanceled() => false;
			public void Reject(Exception err) { }
			public IFuture<T> Resolve(T item) => this;
			public void Wait() { }
			public void Wait(uint timeout) { }
			public T WaitResult() => result;
			public T WaitResult(uint timeout) => result;
			public void Cancel() { }
		}

		public class ConcurrentSet<T> : IEnumerable<T> {
			private ConcurrentDictionary<T, bool> data = new ConcurrentDictionary<T, bool>();
			public virtual bool Contains(T k) => data.ContainsKey(k);
			public virtual void Remove(T k) => data.TryRemove(k, out var ignore);
			public virtual void Add(T k) => data.TryAdd(k, true);
			public virtual bool TryRemove(T k) => data.TryRemove(k, out var ignore);
			public virtual IEnumerator<T> GetEnumerator() => data.Keys.GetEnumerator();
			IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
			public uint Count => (uint)data.Count;
			public void Clear() => data.Clear();
		}
	}
}
