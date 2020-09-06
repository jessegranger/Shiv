using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;
using System.IO.Compression;
using System.IO;

public class NativeFile : Dictionary<string, NativeNamespace> { }
public class NativeNamespace : Dictionary<string, NativeFunction> { }

public class NativeFunction {
	public string Name { get; set; }
	public List<NativeParams> Params { get; set; }
	public string Results { get; set; }
	public string JHash { get; set; }
}

public class NativeParams {
	public string TYPE { get; set; }
	public string Name { get; set; }
}

namespace GenNativeHash {
	class Program {
		static void Main(string[] args) {
			using( var wc = new WebClient() ) {
				var sb = new StringBuilder();
				int unknownCount = 0;
				sb.AppendLine("namespace GTA { namespace Native { public enum Hash : ulong {");
				Console.WriteLine("Downloading natives.json");
				wc.Headers.Add("Accept-Encoding: gzip, deflate, sdch");
				byte[] data = wc.DownloadData("http://www.dev-c.com/nativedb/natives.json");
				string str = Decompress(data);
				var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
				var file = JsonSerializer.Deserialize<NativeFile>(str, options);
				foreach(string nsk in file.Keys) {
					var ns = file[nsk];
					foreach(string fk in ns.Keys) {
						var f = ns[fk];
						if( f.Name == null || f.Name.Length == 0 ) {
							f.Name = string.Format("UNKNOWN_{0}", ++unknownCount);
						}
						sb.AppendFormat("{0} = {1}, /* {2} {0}(", f.Name, fk, f.Results);
						foreach(var p in f.Params) {
							sb.AppendFormat("{0} {1}, ", p.TYPE, p.Name);
						}
						sb.AppendLine(") */");
					}
				}
				sb.AppendLine("} } }");
				Console.Write(sb.ToString());
				File.WriteAllText("NativeHashes.cs", sb.ToString());
				Console.ReadKey();

			}
		}
		private static string Decompress(byte[] gzip) {
			using( var stream = new GZipStream(new MemoryStream(gzip), CompressionMode.Decompress) ) {
				byte[] buffer = new byte[gzip.Length];

				using( var memory = new MemoryStream() ) {
					int count;

					do {
						count = stream.Read(buffer, 0, gzip.Length);

						if( count > 0 ) {
							memory.Write(buffer, 0, count);
						}
					}
					while( count > 0 );

					return Encoding.UTF8.GetString(memory.ToArray());
				}
			}
		}
	}
}
