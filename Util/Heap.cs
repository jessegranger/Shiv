using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static Shiv.Global;

namespace Shiv {
	internal class Heap<T> : IDisposable {

		public uint Count { get; private set; } = 0;
		public uint Capacity { get; private set; }

		private T[] heap;
		private float[] scoreOf;
		private Dictionary<T, uint> index = new Dictionary<T, uint>();

		private ReaderWriterLockSlim mainLock = new ReaderWriterLockSlim();
		private class WriteLock : IDisposable {
			private Heap<T> h;
			public WriteLock(Heap<T> heap) => (h = heap).mainLock.EnterWriteLock();
			public void Dispose() => h.mainLock.ExitWriteLock();
		}
		private class ReadLock : IDisposable {
			private Heap<T> h;
			public ReadLock(Heap<T> heap) => (h = heap).mainLock.EnterReadLock();
			public void Dispose() => h.mainLock.ExitReadLock();
		}

		public Heap() : this(16384) { }
		public Heap(uint capacity) {
			using( new WriteLock(this) ) {
				heap = new T[capacity];
				scoreOf = new float[capacity];
				Capacity = capacity;
			}
		}
		public void Clear() {
			using( new WriteLock(this) ) {
				for( int i = 0; i < Count; i++ ) {
					heap[i] = default;
					scoreOf[i] = float.MaxValue;
				}
				index.Clear();
				Count = 0;
			}
		}
		public bool Contains(T item) {
			using( new ReadLock(this) ) {
				return index.ContainsKey(item);
			}
		}
		public void AddOrUpdate(T item, float score) {
			if( Contains(item) ) {
				Update(item, score);
			} else {
				Add(item, score);
			}
		}
		public void Update(T item, float newScore) {
			using( new WriteLock(this) ) {
				uint i = index[item];
				float oldScore = scoreOf[i];
				scoreOf[i] = newScore;
				if( newScore > oldScore ) {
					SiftDown(i);
				} else {
					BubbleUp(i);
				}
			}
		}
		public void Remove(T item) {
			using( new WriteLock(this) ) {
				if( index.TryGetValue(item, out uint i) ) {
					MoveUp(i);
					SiftDown(i);
				}
			}
		}
		public float Get(T item) {
			using( new ReadLock(this) ) {
				return index.TryGetValue(item, out uint i) ? scoreOf[i] : float.MaxValue;
			}
		}
		public void Add(T item, float score) {
			using( new WriteLock(this) ) {
				Count += 1; // increment count first since we are 1-based
				if( Count == Capacity ) {
					Array.Resize<T>(ref heap, (int)(Capacity * 2));
					Array.Resize<float>(ref scoreOf, (int)(Capacity * 2));
					Capacity *= 2;
				}
				heap[Count] = item;
				scoreOf[Count] = score;
				index[item] = BubbleUp(Count);
			}
		}
		public T Peek() {
			using( new ReadLock(this) ) {
				return Count == 0 ? (default) : heap[1];
			}
		}
		public bool TryPeek(out T value) {
			using( new ReadLock(this) ) {
				if( Count == 0 ) {
					value = default;
					return false;
				} else {
					value = heap[1];
					return true;
				}
			}
		}
		public float PeekScore() {
			using( new ReadLock(this) ) { 
				return Count == 0 ? float.MaxValue : scoreOf[1];
			}
		}
		public bool TryPop(out T value) {
			value = default;
			using( new WriteLock(this) ) {
				if( Count <= 0 ) { return false; }
				value = MoveUp(1);
				SiftDown(1);
				return true;
			}
		}

		// The heap array is 1-indexed so we get simpler children indexors
		private uint leftOf(uint i) => 2 * i;
		private uint rightOf(uint i) => (2 * i) + 1;
		private uint parentOf(uint i) => i / 2;

		// Move the last element in the heap up into index i
		private T MoveUp(uint i) { // only called from inside a write lock
			T value = heap[i];
			index.Remove(value);

			heap[i] = heap[Count];
			scoreOf[i] = scoreOf[Count];
			index[heap[i]] = i;
			heap[Count] = default;
			scoreOf[Count] = default;
			Count -= 1;
			return value;
		}
		// Keep swapping index i with its parent until order is restored
		private uint BubbleUp(uint i) { // only called from inside a write lock
			while( i > 0 ) {
				uint p = parentOf(i);
				if( p <= 0 || scoreOf[p] < scoreOf[i] ) {
					break;
				}
				Swap(p, i);
				i = p;
			}
			return i;
		}
		// Keep swapping index i with the smallest child until order is restored
		private void SiftDown(uint i) { // only called from inside a write lock
			while( i <= Count ) {
				uint left = leftOf(i);
				if( left > Count ) { // if there's no left child, then there's no children at all and we are done
					break;
				}
				float leftValue = scoreOf[left];
				uint right = rightOf(i);
				if( right > Count ) { // if there's no right child, there is only a left child, check that swap
					if( scoreOf[i] > leftValue ) {
						// Debug.Print($"Swapping {heap[i]} with left:{heap[left]} because {scoreOf[i]} > {leftValue}");
						Swap(i, left);
						i = left;
						continue;
					}
				}
				float rightValue = scoreOf[right];
				uint minSide = left;
				float minScore = leftValue;
				if( rightValue < leftValue ) {
					minSide = right;
					minScore = rightValue;
				}
				if( scoreOf[i] <= minScore ) {
					break;
				}
				// Debug.Print($"Swapping {heap[i]} with min:{heap[minSide]} because {scoreOf[i]} > {minScore}");
				Swap(i, minSide);
				i = minSide;
			}
		}
		private void Swap(uint i, uint j) { // only called from inside a write lock
			lock( this ) {
				if( i > Count || j > Count ) {
					return;
				}

				T x = heap[i];
				float s = scoreOf[i];

				heap[i] = heap[j];
				scoreOf[i] = scoreOf[j];

				heap[j] = x;
				scoreOf[j] = s;

				index[heap[i]] = i;
				index[heap[j]] = j;
			}
		}

		public IEnumerable<T> Take(uint n) {
			// Log($"Take({n})");
			while( n > 0 ) {
				bool ret = TryPop(out T value);
				// Log($"Take({n}): {ret} {value}");
				if( ret ) {
					yield return value;
				}
				n -= 1;
			}
		}
		private string ToString(uint i, string indent) {
			return i > Count ? "" :
				$"\n{indent}\\_{heap[i]}{ToString(leftOf(i), indent + ". ")}{ToString(rightOf(i), indent + ". ")}";
		}
		public override string ToString() => ToString(1, "");

		public static void Test() {

			var sw = new Stopwatch();
			sw.Start();
			var intHeap = new Heap<ulong>(4096 * 1024);
			var random = new Random(1234);
			var total = 10 * 1024;
			for( int i = 0; i < total; i++ ) {
				ulong j = (ulong)random.Next();
				intHeap.Add(j, (float)Math.Sqrt(j));
			}
			Debug.Print($"{total} adds in {sw.ElapsedTicks} ticks {sw.ElapsedTicks / total:F2} ticks/insert {total * 1000 / sw.ElapsedMilliseconds} insert/s");
			// Debug.Print(intHeap.ToString());
			sw.Restart();
			ulong prev = ulong.MinValue;
			int k = 0;
			while( intHeap.TryPop(out var value) ) {
				// Debug.Print($"Pop() removed {value}");
				// Debug.Print($"{intHeap.ToString()}");
				if( value < prev ) {
					k += 1;
					Debug.Print($"Pop() invariant violated {k}. {value} < {prev} {(float)Math.Sqrt(value)} <? {(float)Math.Sqrt(prev)}");
				}
				prev = value;
			}
			Debug.Print($"{total} removes in {sw.ElapsedTicks} ticks {sw.ElapsedTicks / total} ticks/remove {total * 1000 / sw.ElapsedMilliseconds} remove/s {k} ({k * 100 / total}%) invariant faults");

			ulong[] data = new ulong[total];
			for( int i = 0; i < total; i++ ) {
				data[i] = (ulong)random.Next();
			}
			sw.Restart();
			for( int i = 0; i < total; i++ ) {
				int minItem = int.MaxValue;
				float minScore = float.MaxValue;
				for( int j = 0; j < total; j++ ) {
					if( data[j] < minScore ) {
						minItem = j;
						minScore = data[j];
					}
				}
				if( minItem < total ) {
					data[minItem] = ulong.MaxValue; // dont find the same one twice
				}
			}
			sw.Stop();
			Debug.Print($"{total} linear searches in {sw.ElapsedTicks} ticks {total * 1000 / sw.ElapsedMilliseconds} scan/s");
		}

		#region IDisposable Support
		private bool disposed = false; // To detect redundant calls

		protected virtual void Dispose(bool disposing) {
			if( !disposed ) {
				if( disposing ) {
					// TODO: dispose managed state (managed objects).
					mainLock.Dispose();
				}

				// TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
				// TODO: set large fields to null.
				heap = null;
				scoreOf = null;
				index.Clear();
				index = null;

				disposed = true;
			}
		}

		// TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
		// ~Heap() {
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
