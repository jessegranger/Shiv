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
			var filename = "scripts/NavMesh/Frontier.mesh";
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
		public static ConcurrentDictionary<NodeHandle, NodeEdges> ReadFromFile(RegionHandle region) {
			try {
				totalReadTimer.Start();
				uint r = (uint)region >> 7;
				Directory.CreateDirectory($"scripts/NavMesh/{r}/");
				return ReadFromFile(region, $"scripts/NavMesh/{r}/{region}.mesh");
			} finally {
				totalReadTimer.Stop();
			}
		}
		public static ConcurrentDictionary<NodeHandle, NodeEdges> ReadFromFile(RegionHandle region, string filename) {
			var ret = new ConcurrentDictionary<NodeHandle, NodeEdges>();
			if( !LoadEnabled ) {
				return ret;
			}

			var s = new Stopwatch();
			s.Start();
			try {
				using( BinaryReader r = Codec.Reader(filename) ) {
					readBytesTimer.Start();
					int magic = r.ReadInt32();
					if( magic == 0x000FEED9 ) {
						int count = r.ReadInt32();
						if( count <= 0 ) {
							Log($"Invalid count: {count}");
							return ret;
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
							ret.TryAdd((NodeHandle)handles[i], (NodeEdges)edges[i]);
						});
						Log($"[{region}] Loaded {count} (ver. 9) nodes in {s.ElapsedMilliseconds}ms");
						applyBytesTimer.Stop();
					} else {
						Log($"Invalid magic bytes: {magic}");
						return ret;
					}
				}
			} catch( FileNotFoundException ) {
				Log($"[{region}] Loaded 0 nodes ({filename} not found) in {s.ElapsedMilliseconds}ms");
			}
			s.Stop();
			return ret;
		}

		public static void SaveToFile() {
			if( !SaveEnabled ) {
				return;
			}

			var sw = new Stopwatch();
			sw.Start();
			AllNodes.dirtyRegions.Each(region => { 
			// AllEdges.Keys.AsParallel().GroupBy(Region).Each(g => {
				// RegionHandle region = g.Key;
				if( AllNodes.dirtyRegions.TryRemove(region) ) {
					Log($"Saving dirty region {region}");
					uint r = (uint)region >> 7;
					Directory.CreateDirectory($"scripts/NavMesh/{r}/");
					var file = $"scripts/NavMesh/{r}/{region}.mesh";
					using( BinaryWriter w = Codec.Writer(file + ".tmp") ) {
						w.Write(magicBytes);
						var result = AllNodes.Regions[region].WaitResult();
						ulong[] handles = result.Keys.Cast<ulong>().ToArray();
						ulong[] edges = handles.Select(h => (ulong)result[(NodeHandle)h]).ToArray(); // use Select this way to guarantee they match the order of handles
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
			var filename = "scripts/NavMesh/Frontier.mesh";
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
