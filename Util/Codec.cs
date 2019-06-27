using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace Shiv {
	public static class CodecExtensions {
		public static Vector3 ReadVector(this BinaryReader r) {
			return new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
		}
		public static List<Vector3> ReadVectors(this BinaryReader r) {
			var ret = new List<Vector3>();
			for (int n = r.ReadInt32(); n > 0; n--) {
				ret.Add(r.ReadVector());
			}
			return ret;
		}
		public static List<float> ReadFloats(this BinaryReader r) {
			var ret = new List<float>();
			for (int n = r.ReadInt32(); n > 0; n--) {
				ret.Add(r.ReadSingle());
			}
			return ret;
		}
		public static void Write(this BinaryWriter w, Vector3 v) {
			w.Write(v.X);
			w.Write(v.Y);
			w.Write(v.Z);
		}
		public static void Write(this BinaryWriter w, List<Vector3> v) {
			foreach( Vector3 x in v ) w.Write(x);
		}
		public static void Write(this BinaryWriter w, List<float> v) {
			w.Write(v.Count);
			foreach( float f in v ) w.Write(f);
		}
	}
	internal class Codec {
		// filenames are like "scripts/MyFile.dat"
		public static BinaryWriter Writer(string filename) {
			try { return new BinaryWriter(File.OpenWrite(filename)); }
			catch( IOException ) { // always retry once, sometimes on a reload the old file handle is not closed yet
				return new BinaryWriter(File.OpenWrite(filename));
			}
		}
		public static BinaryReader Reader(string filename) {
			try { return new BinaryReader(File.OpenRead(filename)); }
			catch( IOException ) {
				return new BinaryReader(File.OpenRead(filename));
			}
		}
	}
}
