using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Shiv.Global;

namespace Shiv {
	public partial class NavMesh : Script {
		public static bool ReadFrontierFile() {
			if( !LoadEnabled ) {
				return false;
			}
			var filename = "scripts/Shiv.Frontier.mesh";
			try {
				using( BinaryReader r = Codec.Reader(filename) ) {
					int magic = r.ReadInt32();
					if( magic != magicBytes ) {
						Log($"Wrong magic bytes {magic:X}");
						return false;
					}
					// let EndOfStreamException tell us when to stop
					while( true ) { Ungrown.Enqueue((NodeHandle)r.ReadInt64()); }
				}
			} catch( EndOfStreamException ) {
				return true;
			} catch( IOException ) {
				return false;
			} catch( ObjectDisposedException ) {
				return false;
			}
		}

		static Stopwatch totalReadTimer = new Stopwatch();
		static Stopwatch readBytesTimer = new Stopwatch();
		static Stopwatch applyBytesTimer = new Stopwatch();
		public static bool ReadFromFile(RegionHandle region, string filename) {
			if( !LoadEnabled ) {
				return false;
			}

			var s = new Stopwatch();
			s.Start();
			try {
				totalReadTimer.Start();
				using( BinaryReader r = Codec.Reader(filename) ) {
					readBytesTimer.Start();
					int magic = r.ReadInt32();
					if( magic == 0x000FEED6 ) { // upgrade this older version of mesh file
						int count = r.ReadInt32();
						if( count <= 0 ) {
							Log($"Invalid count: {count}");
							return false;
						}
						byte[] buf;
						ulong[] handles = new ulong[count];
						uint[] edges = new uint[count];
						buf = r.ReadBytes(count * sizeof(ulong));
						Buffer.BlockCopy(buf, 0, handles, 0, buf.Length);
						buf = r.ReadBytes(count * sizeof(uint));
						Buffer.BlockCopy(buf, 0, edges, 0, buf.Length);
						readBytesTimer.Stop();
						applyBytesTimer.Start();
						var queue = new ConcurrentQueue<NodeHandle>();
						Parallel.For(0, count, (i) => {
							var e = (NodeEdges)edges[i];
							// old (small) edges never have (room for) clearance
							if( e.HasFlag(NodeEdges.IsCover) ) {
								e |= (NodeEdges)((ulong)1 << 32); // set clearance to 1 for cover nodes
								queue.Enqueue((NodeHandle)handles[i]);
							} else {
								e |= NodeEdges.ClearanceMask; // set unknown clearance to max clear 15
							}
							AllEdges.TryAdd((NodeHandle)handles[i], (NodeEdges)e);
						});
						Log($"Propagating clearance from {queue.Count} cover nodes...");
						PropagateClearance(queue);
						Log($"[{region}] Loaded {count} (ver. 6) nodes in {s.ElapsedMilliseconds}ms");
						dirtyRegions.Add(region);
						applyBytesTimer.Stop();
					} else if( magic == 0x000FEED7 ) {
						int count = r.ReadInt32();
						if( count <= 0 ) {
							Log($"Invalid count: {count}");
							return false;
						}
						byte[] buf;
						ulong[] handles = new ulong[count];
						ulong[] edges = new ulong[count];
						buf = r.ReadBytes(count * sizeof(ulong));
						Buffer.BlockCopy(buf, 0, handles, 0, buf.Length);
						buf = r.ReadBytes(count * sizeof(ulong));
						Buffer.BlockCopy(buf, 0, edges, 0, buf.Length);
						readBytesTimer.Stop();
						applyBytesTimer.Start();
						var queue = new ConcurrentQueue<NodeHandle>();
						Parallel.For(0, count, (i) => {
							var e = (NodeEdges)edges[i];
							if( ((ulong)(e & NodeEdges.ClearanceMask) >> 32) == 0 ) { // if has empty clearance bits
								if( e.HasFlag(NodeEdges.IsCover) ) {
									e |= (NodeEdges)((ulong)1 << 32); // set clearance to 1 for cover nodes
									queue.Enqueue((NodeHandle)handles[i]);
								} else {
									e |= NodeEdges.ClearanceMask; // set unknown clearance to max clear 15
								}
							}
							AllEdges.TryAdd((NodeHandle)handles[i], e);
						});
						Log($"Propagating clearance from {queue.Count} cover nodes...");
						PropagateClearance(queue);
						Log($"[{region}] Loaded {count} (ver. 7) nodes in {s.ElapsedMilliseconds}ms");
						dirtyRegions.Add(region);
						applyBytesTimer.Stop();
					} else if( magic == 0x000FEED8 ) { // after FEED8, nodes have existing clearance counts
						int count = r.ReadInt32();
						if( count <= 0 ) {
							Log($"Invalid count: {count}");
							return false;
						}
						byte[] buf;
						ulong[] handles = new ulong[count];
						ulong[] edges = new ulong[count];
						buf = r.ReadBytes(count * sizeof(ulong));
						Buffer.BlockCopy(buf, 0, handles, 0, buf.Length);
						buf = r.ReadBytes(count * sizeof(ulong));
						Buffer.BlockCopy(buf, 0, edges, 0, buf.Length);
						readBytesTimer.Stop();
						applyBytesTimer.Start();
						Parallel.For(0, count, (i) => {
							AllEdges.TryAdd((NodeHandle)handles[i], (NodeEdges)edges[i]);
						});
						Log($"[{region}] Loaded {count} nodes in {s.ElapsedMilliseconds}ms");
						applyBytesTimer.Stop();
					} else {
						Log($"Invalid magic bytes: {magic}");
						return false;
					}
				}
			} catch( FileNotFoundException ) {
				Log($"[{region}] Loaded 0 nodes ({filename} not found) in {s.ElapsedMilliseconds}ms");
			} finally {
				totalReadTimer.Stop();
			}
			s.Stop();
			return true;
		}

		public static void SaveToFile() {
			if( !SaveEnabled ) {
				return;
			}

			var sw = new Stopwatch();
			sw.Start();
			AllEdges.Keys.AsParallel().GroupBy(Region).Each(g => {
				RegionHandle region = g.Key;
				if( dirtyRegions.TryRemove(region) ) {
					var file = $"scripts/Shiv.{region}.mesh";
					using( BinaryWriter w = Codec.Writer(file + ".tmp") ) {
						w.Write(magicBytes);
						ulong[] handles = g.Cast<ulong>().ToArray();
						ulong[] edges = handles.Select(h => (ulong)AllEdges[(NodeHandle)h]).ToArray();
						byte[] buf;
						try {
							w.Write(handles.Length);
							buf = new byte[handles.Length * sizeof(NodeHandle)];
							Buffer.BlockCopy(handles, 0, buf, 0, buf.Length);
							w.Write(buf);
						} catch( Exception err ) {
							Log("Failed to write handles to file: " + err.ToString());
							return;
						}
						try {
							buf = new byte[edges.Length * sizeof(ulong)];
							Buffer.BlockCopy(edges, 0, buf, 0, buf.Length);
							w.Write(buf);
						} catch( Exception err ) {
							Log("Failed to write edges to file: " + err.ToString());
							return;
						}
					}
					try { File.Delete(file); } catch( FileNotFoundException ) { }
					try { File.Move(file + ".tmp", file); } catch( Exception e ) {
						Log("File.Move Failed: " + e.ToString());
					}
				}
			});
			var filename = "scripts/Shiv.Frontier.mesh";
			using( BinaryWriter w = Codec.Writer(filename + ".tmp") ) {
				w.Write(magicBytes);
				try {
					foreach( NodeHandle h in Ungrown ) {
						w.Write((ulong)h);
					}
				} catch( Exception err ) {
					Log("Failed: " + err.ToString());
					return;
				}
				w.Close();
			}
			try { File.Delete(filename); } catch( FileNotFoundException ) { }
			try { File.Move(filename + ".tmp", filename); } catch( Exception e ) {
				Log("File.Move Failed: " + e.ToString());
			}
			sw.Stop();
			Log($"Saved mesh in {sw.ElapsedMilliseconds}ms");
		}
	}
}
