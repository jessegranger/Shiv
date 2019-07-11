
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;

namespace Shiv {
	public static partial class Global {

		public static IEnumerable<T> Select<S, T>(this Array array, Func<S, T> func) {
			foreach( S x in array ) {
				yield return func(x);
			}
		}

		public static Array Each<T>(this Array list, Action<T> func) {
			foreach( T x in list ) {
				func(x);
			}

			return list;
		}

		public static bool TryDequeue<T>(this Queue<T> queue, out T item) {
			if( queue.Count == 0 ) {
				item = default;
				return false;
			} else {
				item = queue.Dequeue();
				return true;
			}
		}

		public static T Min<T>(this IEnumerable<T> list, Func<T,float> score) {
			T minItem = default;
			float minScore = float.MaxValue;
			foreach( var x in list ) {
				float xScore = score(x);
				if( xScore < minScore ) {
					minItem = x;
					minScore = xScore;
				}
			}
			return minItem;
		}

		public static IEnumerable<T> Without<T>(this IEnumerable<T> list, T item) {
			foreach( T x in list ) {
				if( ! x.Equals(item) ) {
					yield return x;
				}
			}
		}
		public static IEnumerable<T> Without<T>(this IEnumerable<T> list, Predicate<T> pred) {
			foreach( T x in list ) {
				if( !pred(x) ) {
					yield return x;
				}
			}
		}

		public static IEnumerable<T> Each<T>(this IEnumerable<T> list, Action<T> func) {
			foreach( T x in list ) {
				func(x);
			}

			return list;
		}

		private static Random random = new Random();
		public static IEnumerable<T> Random<T>(this IEnumerable<T> list, float chance) {
			foreach( T x in list ) {
				if( random.NextDouble() <= chance ) {
					yield return x;
				}
			}
		}
		public static T Random<T>(this Array list) => list.Length == 0 ? (default) : (T)list.GetValue(random.Next(0, list.Length));

		public static IEnumerable<T> Items<T>(params T[] items) {
			foreach( T item in items ) {
				yield return item;
			}
		}

		public static IEnumerable<int> Range(int start, int end, int step = 1) {
			for( int i = start; i < end; i += step ) {
				yield return i;
			}
		}

		public static void Clear<T>(this ConcurrentQueue<T> Q) {
			while( Q.TryDequeue(out T ignore) ) { }
		}

		public static T Pop<T>(this LinkedList<T> list) {
			if( list.Count != 0 ) {
				try { return list.First.Value; } finally { list.RemoveFirst(); }
			}

			return default;
		}

		public static LinkedListNode<T> RemoveAndContinue<T>(this LinkedList<T> list, LinkedListNode<T> node) {
			LinkedListNode<T> next = node.Next;
			list.Remove(node);
			return next;
		}

		public static void Visit<T>(this LinkedList<T> list, Action<T> func) => list.Visit(s => { func(s); return false; });
		public static void Visit<T>(this LinkedList<T> list, Func<T, bool> func) {
			LinkedListNode<T> cur = list.First;
			while( cur != null ) {
				try {
					cur = func(cur.Value) ? list.RemoveAndContinue(cur) : cur.Next;
				} catch( ScriptComplete ) {
					cur = list.RemoveAndContinue(cur);
				} catch( Exception e ) {
					Log($"Uncaught exception in Visit<T>: {e.Message}");
					Log(e.StackTrace);
					cur = list.RemoveAndContinue(cur);
				}
			}
		}

		/// <summary> Add item to the list after the position where pred returns <see langword="true"/>. </summary>
		public static void AddAfter<T>(this LinkedList<T> list, T item, Func<T, bool> pred) {
			LinkedListNode<T> cur = list.First;
			while( cur != null && cur.Value != null ) {
				if( pred(cur.Value) ) {
					list.AddAfter(cur, item);
					return;
				}
				cur = cur.Next;
			}
			list.AddLast(item);
		}

		internal class Pool<T> where T : new() {
			private ConcurrentStack<T> free = new ConcurrentStack<T>();
			public T GetItem() => free.TryPop(out T ret) ? ret : new T();
			public void Release(T item) => free.Push(item);
		}

		public static bool TryAdd<TK,TV>(this Dictionary<TK,TV> dict, TK k, TV v) {
			lock( dict ) {
				try {
					return dict.ContainsKey(k);
				} finally { dict[k] = v; }
			}
		}
		public static bool TryRemove<TK, TV>(this Dictionary<TK, TV> dict, TK a, out TV val) {
			lock( dict ) {
				if( dict.ContainsKey(a) ) {
					val = dict[a];
					dict.Remove(a);
					return true;
				}
				val = default;
				return false;
			}
		}
		public static void AddOrUpdate<TK, TV>(this Dictionary<TK, TV> dict, TK a, TV b, Func<TK, TV, TV> update) {
			lock( dict ) {
				dict[a] = dict.ContainsKey(a) ? update(a, dict[a]) : b;
			}
		}


		public static IEnumerable<NodeHandle> InRange(this IEnumerable<NodeHandle> list, float range, uint limit = uint.MaxValue) {
			foreach( NodeHandle n in list ) {
				if( DistanceToSelf(n) <= range ) {
					yield return n;
					if( --limit <= 0 ) {
						yield break;
					}
				}
			}
		}

		public static IEnumerable<Vector3> InRange(this IEnumerable<Vector3> list, float range, uint limit = uint.MaxValue) {
			foreach( Vector3 n in list ) {
				if( DistanceToSelf(n) <= range ) {
					yield return n;
					if( --limit <= 0 ) {
						yield break;
					}
				}
			}
		}
	}
}