
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Shiv {
	public static partial class Globals {

		public static IEnumerable<T> Select<S, T>(this Array array, Func<S, T> func) {
			foreach( S x in array ) yield return func(x);
		}

		public static Array Each<T>(this Array list, Action<T> func) {
			foreach( T x in list ) func(x);
			return list;
		}

		public static IEnumerable<T> Each<T>(this IEnumerable<T> list, Action<T> func) {
			foreach( T x in list ) func(x);
			return list;
		}

		public static void Clear<T>(this ConcurrentQueue<T> Q) {
			while( Q.TryDequeue(out T ignore) ) { }
		}

		public static LinkedListNode<T> RemoveAndContinue<T>(this LinkedList<T> list, LinkedListNode<T> node) {
			var next = node.Next;
			list.Remove(node);
			return next;
		}
		public static void Visit<T>(this LinkedList<T> list, Action<T> func) => list.Visit(s => { func(s); return false; });
		public static void Visit<T>(this LinkedList<T> list, Func<T, bool> func) {
			var cur = list.First;
			while( cur != null ) {
				try {
					cur = func(cur.Value) ? list.RemoveAndContinue(cur) : cur.Next;
				} catch( ScriptComplete ) {
					cur = list.RemoveAndContinue(cur);
				} catch( Exception e ) {
					Shiv.Log($"Uncaught exception in Visit<T>: {e.Message} {e.StackTrace}");
					cur = list.RemoveAndContinue(cur);
				}
			}
		}

		public static void AddAfter<T>(this LinkedList<T> list, T item, Func<T,bool> pred) {
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

	}
}