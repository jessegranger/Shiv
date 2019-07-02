using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Shiv.Global;

namespace Shiv {
	internal class Heap<T> {

		public uint Count { get; private set; } = 0;
		public uint Capacity { get; private set; }

		private T[] heap;
		private float[] scoreOf;
		private Dictionary<T, uint> index = new Dictionary<T, uint>();
		public Heap() : this(16384) { }
		public Heap(uint capacity) {
			heap = new T[capacity];
			scoreOf = new float[capacity];
			Capacity = capacity;
		}
		public bool Contains(T item) => index.ContainsKey(item);
		public void AddOrUpdate(T item, float score) {
			if( Contains(item) ) {
				uint i = index[item];
				float oldScore = scoreOf[i];
				scoreOf[i] = score;
				if( score > oldScore ) {
					SiftDown(i);
				} else {
					BubbleUp(i);
				}
			} else {
				Add(item, score);
			}
		}
		public void Remove(T item) {
			if( index.TryGetValue(item, out uint i ) ) {
				MoveUp(i);
				SiftDown(i);
			}
		}
		public float Get(T item) => index.TryGetValue(item, out uint i) ? scoreOf[i] : float.MaxValue;
		public void Add(T item, float score) {
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
		private T MoveUp(uint i) {
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
		public T Peek() => Count == 0 ? (default) : heap[1];
		public bool TryPop(out T value) {
			value = default;
			if( Count <= 0 ) {
				return false;
			}

			value = MoveUp(1);
			/*
			value = heap[1];
			index.Remove(value);
			heap[1] = heap[Count];
			scoreOf[1] = scoreOf[Count];
			index[heap[1]] = 1;
			// Debug.Print($"Moving {heap[1]} to top");
			heap[Count] = default;
			scoreOf[Count] = float.MaxValue;
			Count -= 1;
			*/
			SiftDown(1);

			return true;
		}

		// The heap array is 1-indexed so we get simpler children indexors
		private uint leftOf(uint i) => 2 * i;
		private uint rightOf(uint i) => (2 * i) + 1;
		private uint parentOf(uint i) => i / 2;

		private uint BubbleUp(uint i) {
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
		private void SiftDown(uint i) {
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
		private void Swap(uint i, uint j) {
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

		public IEnumerable<T> Take(uint n) {
			while( n-- > 0 && TryPop(out T value) ) {
				yield return value;
			}
		}
		private string ToString(uint i, string indent) {
			return i > Count ? "" : 
				$"\n{indent}\\_{heap[i]}{ToString(leftOf(i), indent + ". ")}{ToString(rightOf(i), indent + ". ")}";
		}
		public override string ToString() => ToString(1, "");

		public IEnumerable<T> Items(params T[] items) {
			foreach( T x in items ) {
				yield return x;
			}
		}

		public static void Test() {

			var sw = new Stopwatch();
			sw.Start();
			var intHeap = new Heap<ulong>(4096*1024);
			var random = new Random(1234);
			var total = 10*1024;
			for(int i = 0; i < total; i++ ) {
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
			Debug.Print($"{total} removes in {sw.ElapsedTicks} ticks {sw.ElapsedTicks / total} ticks/remove {total * 1000 / sw.ElapsedMilliseconds} remove/s {k} ({k*100/total}%) invariant faults");

			ulong[] data = new ulong[total];
			for(int i = 0; i < total; i++ ) {
				data[i] = (ulong)random.Next();
			}
			sw.Restart();
			for( int i = 0; i < total; i++ ) {
				int minItem = int.MaxValue;
				float minScore = float.MaxValue;
				for(int j = 0; j < total; j++ ) {
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

	}
}
