using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using static Shiv.Global;

// pragma warning disable CS0649
namespace Shiv {

	public partial class NavMesh {
		public class Producer<T> {
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
			public virtual bool TryConsume(out T item) {
				if( buf.TryDequeue(out item) ) {
					return true;
				}
				return false;
			}
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
}
