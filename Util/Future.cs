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
			Exception GetError();
			bool IsDone();
			bool IsReady();
			bool IsFailed();
			bool IsCanceled();
			IFuture<T> Resolve(T item);
			void Reject(Exception err);
		}
		public class Future<T> : IFuture<T> {
			public Future() {
				cancel = new CancellationTokenSource();
				ready = new CountdownEvent(1);
			}
			public Future(Func<T> func):this() {
				ThreadPool.QueueUserWorkItem((object arg) => {
					try { Resolve(func()); }
					catch( Exception err ) { Reject(err); }
				});
			}
			public Future(Func<CancellationToken, T> func):this() {
				ThreadPool.QueueUserWorkItem((object arg) => {
					try { Resolve(func(cancel.Token)); }
					catch( Exception err ) { Reject(err); }
				});
			}
			private ReaderWriterLockSlim guard = new ReaderWriterLockSlim();
			private T result = default;
			public T GetResult() {
				if( guard.TryEnterReadLock(20) ) {
					try { return result; } finally { guard.ExitReadLock(); }
				}
				return default;
			}

			private Exception error;
			protected CancellationTokenSource cancel;
			private CountdownEvent ready;

			public Exception GetError() => error;
			public bool IsDone() => IsFailed() || IsCanceled() || IsReady();
			public bool IsFailed() => error != null;
			public bool IsReady() => ready.IsSet;
			public IFuture<T> Resolve(T item) {
				if( guard.TryEnterWriteLock(10) ) {
					try {
						result = item;
						ready.Signal();
					} finally {
						guard.ExitWriteLock();
					}
				} else {
					Reject(new TimeoutException());
				}
				return this;
			}
			public void Reject(Exception err) => error = err;
			public void Wait() => ready.Wait(cancel.Token);
			public void Wait(int timeout) => ready.Wait(timeout, cancel.Token);
			public void Cancel() { try { cancel.Cancel(); } catch( Exception ) { } }
			public bool IsCanceled() => cancel.IsCancellationRequested;
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
		}
		public class ConcurrentSet<T> : IEnumerable<T> {
			private ConcurrentDictionary<T, T> data = new ConcurrentDictionary<T, T>();
			public virtual bool Contains(T k) => data.ContainsKey(k);
			public virtual void Remove(T k) => data.TryRemove(k, out T ignore);
			public virtual void Add(T k) => data.TryAdd(k, k);

			public virtual IEnumerator<T> GetEnumerator() => data.Keys.GetEnumerator();
			IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

			public uint Count => (uint)data.Count;
		}
	}
}
