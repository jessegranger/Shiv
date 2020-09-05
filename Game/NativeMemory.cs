using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using Shiv;
using static System.Runtime.InteropServices.Marshal;
using static System.Text.Encoding;
using static Shiv.NativeMethods;
using System.Numerics;
using static Shiv.Shiv;

namespace GTA {
	namespace Native {


		public class PinnedString : IDisposable {
			internal IntPtr Value;
			public PinnedString(string s) {
				unsafe {
					byte[] bytes = UTF8.GetBytes(s);
					int len = bytes.Length;
					IntPtr dest = AllocCoTaskMem(len + 1);
					if( dest != IntPtr.Zero ) {
						Copy(bytes, 0, dest, len);
						((byte*)dest.ToPointer())[len] = 0;
						Value = dest;
					}
				}
			}
			public static implicit operator IntPtr(PinnedString s) => s.Value;
			public static implicit operator ulong(PinnedString s) => (ulong)s.Value.ToInt64();
			public static implicit operator long(PinnedString s) => s.Value.ToInt64();

			public static readonly PinnedString STRING = new PinnedString("STRING");
			public static readonly PinnedString CELL_EMAIL_BCON = new PinnedString("CELL_EMAIL_BCON");
			public static readonly PinnedString EMPTY = new PinnedString("");

			#region IDisposable Support
			private bool disposed = false; // To detect redundant calls

			protected virtual void Dispose(bool disposing) {
				if( !disposed ) {
					if( disposing ) {
						// TODO: dispose managed state (managed objects).
					}
					if( Value != IntPtr.Zero ) {
						FreeCoTaskMem(Value);
					}
					Value = IntPtr.Zero;

					// TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
					// TODO: set large fields to null.

					disposed = true;
				}
			}

			// TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
			 ~PinnedString() {
				// Do not change this code. Put cleanup code in Dispose(bool disposing) above.
				Dispose(false);
			}

			// This code added to correctly implement the disposable pattern.
			public void Dispose() {
				// Do not change this code. Put cleanup code in Dispose(bool disposing) above.
				Dispose(true);
				// TODO: uncomment the following line if the finalizer is overridden above.
				GC.SuppressFinalize(this);
			}
			#endregion
		}

		public static class Function {
			private struct VoidType { public override string ToString() => "void"; };
			private static readonly VoidType VoidValue = new VoidType();

			public static void Call(Hash hash, params object[] args) => Call<VoidType>(hash, args);
			public static T Call<T>(Hash hash, params object[] args) {
				T ret = default;
				unsafe {
					NativeInit((ulong)hash);
					args.Each(PushObject);
					ret = (T)GetResult(typeof(T), NativeCall());
					return ret;
				}
			}

			private static void PushObject(object o) {
				if( o is null ) {
					NativePush64(0);
					return;
				}
				Type type = o.GetType();

				if( type.IsEnum ) {
					type = Enum.GetUnderlyingType(type);
				}

				try {
#pragma warning disable IDE0011 // Add braces
					if( type == typeof(bool) ) NativePush64((ulong)(((bool)o) ? 1 : 0));
					else if( type == typeof(int) ) NativePush64(unchecked((ulong)(int)o));
					else if( type == typeof(uint) ) NativePush64((uint)o);
					else if( type == typeof(byte) ) NativePush64((byte)o);
					else if( type == typeof(sbyte) ) NativePush64(unchecked((ulong)(sbyte)o));
					else if( type == typeof(short) ) NativePush64(unchecked((ulong)(short)o));
					else if( type == typeof(ushort) ) NativePush64((ushort)o);
					else if( type == typeof(long) ) NativePush64((ulong)(long)o);
					else if( type == typeof(ulong) ) NativePush64((ulong)o);
					else if( type == typeof(float) ) NativePush64(BitConverter.ToUInt32(BitConverter.GetBytes((float)o), 0));
					else if( type == typeof(double) ) NativePush64(BitConverter.ToUInt32(BitConverter.GetBytes((float)(double)o), 0));
					else if( type == typeof(string) ) throw new ArgumentException("Must pass a PinnedString, not a managed string.");
					else if( type == typeof(PinnedString) ) NativePush64((PinnedString)o);
					else if( type == typeof(IntPtr) ) NativePush64((ulong)((IntPtr)o).ToInt64());
#pragma warning restore IDE0011 // Add braces
					else if( type == typeof(Color) ) {
						var c = (Color)o;
						PushObject(c.R);
						PushObject(c.G);
						PushObject(c.B);
						PushObject(c.A);
					} else if( type == typeof(Vector3) ) {
						var v = (Vector3)o;
						PushObject(v.X);
						PushObject(v.Y);
						PushObject(v.Z);
					} else {
						throw new ArgumentException(string.Concat("Unable to cast object of type '", type.FullName, "' to native value"));
					}
				} catch( InvalidCastException err ) {
					Log($"Failed to cast {o} to {type.Name}");
					Log(err.StackTrace);
					throw err;
				}
			}

			private static unsafe object GetResult(Type type, ulong* o) {
				if( type == typeof(VoidType) ) { return VoidValue; }
				if( type.IsEnum ) { type = Enum.GetUnderlyingType(type); }

#pragma warning disable IDE0011 // Add braces
				if( o == null ) return 0;
				else if( type == typeof(bool) ) return *(int*)o != 0;
				else if( type == typeof(int) ) return *(int*)o;
				else if( type == typeof(uint) ) return *(uint*)o;
				else if( type == typeof(long) ) return *(long*)o;
				else if( type == typeof(ulong) ) return *o;
				else if( type == typeof(float) ) return *(float*)o;
				else if( type == typeof(double) ) return (double)*(float*)o;
				else if( type == typeof(IntPtr) ) return new IntPtr((long)o);
				else if( type == typeof(string) ) return MemoryAccess.ReadString(new IntPtr((char*)*o), 0x0);
#pragma warning restore IDE0011 // Add braces
				else if( type == typeof(Vector3) ) {
					float* data = (float*)o;
					return new Vector3(data[0], data[2], data[4]); // NOTE: does not use Read<Vector3> because the script API passes NativeVector3 back and forth (with larger fields)
					// this code is automatically converting the NativeVector3 (on the wire) to a System.Numerics.Vector3 so it can use SIMD etc
				}
				throw new InvalidCastException(string.Concat("Unable to cast native value to object of type '", type.FullName, "'"));
			}

			public static void TraceCall(Hash hash, params object[] args) => TraceCall<VoidType>(hash, args);
			public static T TraceCall<T>(Hash hash, params object[] args) {
				var s = new Stopwatch();
				s.Start();
				T ret = default;
				try {
					return ret = Call<T>(hash, args);
				} finally {
					s.Stop();
					Log($"Function.Call({hash}, ...) = {ret} ({s.Elapsed})");
				}
			}
		}

		public unsafe static class MemoryAccess {

			private static readonly ulong* addrCheckpointPool;
			private static readonly ulong* addrGamePlayCamAddr;
			private static readonly int* addrCursorSprite;
			private static readonly ulong GetLabelTextByHashTable;

			internal static Func<int, ulong> EntityAddressFunc;
			internal static Func<ulong, int, ulong> CheckpointHandleAddr;
			internal static Func<ulong> CheckpointBaseAddr;
			internal static Func<ulong, int, ulong> GetLabelTextByHashFunc;

			private static T TraceGetDelegate<T>(IntPtr p) {
				Log($"Hooked {typeof(T).Name} at {p:X}");
				return GetDelegateForFunctionPointer<T>(p);
			}

			static MemoryAccess() {
				byte* addr;

				var sw = new Stopwatch();
				sw.Start();

				addr = FindPattern(new byte[] { 0xE8, 0x00, 0x00, 0x00, 0x00, 0x48, 0x8B, 0xD8, 0x48, 0x85, 0xC0, 0x74, 0x2E, 0x48, 0x83, 0x3D });
				EntityAddressFunc = TraceGetDelegate<Func<int, ulong>>(new IntPtr(*(int*)(addr + 1) + addr + 5));

				addr = FindPattern(new byte[] { 0x48, 0x89, 0x5C, 0x24, 0x08, 0x48, 0x89, 0x6C, 0x24, 0x18, 0x89, 0x54, 0x24, 0x10, 0x56, 0x57, 0x41, 0x56, 0x48, 0x83, 0xEC, 0x20 });
				GetLabelTextByHashFunc = TraceGetDelegate<Func<ulong, int, ulong>>(new IntPtr(addr));

				addr = FindPattern(new byte[] { 0x84, 0xC0, 0x74, 0x34, 0x48, 0x8D, 0x0D, 0x00, 0x00, 0x00, 0x00, 0x48, 0x8B, 0xD3 });
				GetLabelTextByHashTable = (ulong)(*(int*)(addr + 7) + addr + 11);

				addr = FindPattern(new byte[] { 0x8A, 0x4C, 0x24, 0x60, 0x8B, 0x50, 0x10, 0x44, 0x8A, 0xCE });
				CheckpointBaseAddr = TraceGetDelegate<Func<ulong>>(new IntPtr(*(int*)(addr - 19) + addr - 15));
				CheckpointHandleAddr = TraceGetDelegate<Func<ulong, int, ulong>>(new IntPtr(*(int*)(addr - 9) + addr - 5));
				addrCheckpointPool = (ulong*)(*(int*)(addr + 17) + addr + 21);

				addr = FindPattern(new byte[] { 0x74, 0x11, 0x8B, 0xD1, 0x48, 0x8D, 0x0D, 0x00, 0x00, 0x00, 0x00, 0x45, 0x33, 0xC0 });
				addrCursorSprite = (int*)(*(int*)(addr - 4) + addr);

				addr = FindPattern(new byte[] { 0x48, 0x8B, 0xC7, 0xF3, 0x0F, 0x10, 0x0D }) - 0x1D;
				addr = addr + *(int*)addr + 4;
				addrGamePlayCamAddr = (ulong*)(*(int*)(addr + 3) + addr + 7);

				sw.Stop();
				Log($"MemoryAccess initialized in {sw.Elapsed}");
			}

			/// <summary> Read one unmanaged value from an (address + offset). </summary>
			/// <returns>default(T) if address is invalid.</returns>
			public static T Read<T>(IntPtr addr, int off) where T : unmanaged {
				if( addr != IntPtr.Zero ) {
					unsafe { return *(T*)(addr + off).ToPointer(); }
				}
				return default;
			}
			public static T TraceRead<T>(IntPtr addr, int off) where T : unmanaged {
				if( addr != IntPtr.Zero ) {
					var sw = new Stopwatch();
					sw.Start();
					T ret = default;
					unsafe { ret = *(T*)(addr + off).ToPointer(); }
					Log($"Read<{typeof(T).Name}>({addr.ToInt64()}, {off:X}) = {ret} ({sw.Elapsed})");
					return ret;
				}
				return default;
			}

			/// <summary> Write one unmanaged value to an (address + offset). </summary>
			public static void Write<T>(IntPtr addr, int off, T value) where T : unmanaged {
				if( addr != IntPtr.Zero ) {
					unsafe { *(T*)(addr + off).ToPointer() = value; }
				}
			}

			public static string ReadString(IntPtr addr, int off) {
				if( addr != IntPtr.Zero ) {
					unsafe {
						byte* start = (byte*)(addr + off).ToPointer();
						int len = 0;
						while( start[len] != 0 ) {
							++len;
						}
						if( len > 0 ) {
							return UTF8.GetString(start, len);
						}
					}
				}
				return string.Empty;
			}

			public static IntPtr ReadPtr(IntPtr addr, int off) {
				if( addr != IntPtr.Zero ) {
					unsafe {
						return new IntPtr(*(void**)(addr + off).ToPointer());
					}
				}
				return IntPtr.Zero;
			}

			public static void SetBit(IntPtr addr, int off, uint bit, bool value) {
				if( addr == IntPtr.Zero || bit > 31 ) {
					return;
				}

				int mask = 1 << (int)bit;
				unsafe {
					int* data = (int*)(addr + off).ToPointer();
					*data = (value ? *data | mask : *data & ~mask);
				}
			}

			public static bool IsBitSet(IntPtr addr, int off, uint bit) {
				if( addr != IntPtr.Zero && bit < 32 ) {
					int mask = 1 << (int)bit;
					unsafe {
						return (*(int*)(addr + off).ToPointer() & mask) != 0;
					}
				}
				return false;
			}

			public static uint GetHashKey(string toHash) {
				using( var handle = new PinnedString(toHash) ) {
					return handle == 0 ? 0 : Function.Call<uint>(Hash.GET_HASH_KEY, handle);
				}
			}

			internal static string GetLabelTextByHash(int entryLabelHash) {
				char* entryText = (char*)GetLabelTextByHashFunc(GetLabelTextByHashTable, entryLabelHash);
				return entryText != null ? ReadString(new IntPtr(entryText), 0x0) : $"LABEL_{entryLabelHash}";
			}

			public static IntPtr GetEntityAddress(int handle) => new IntPtr((long)EntityAddressFunc(handle));
			// internal static IntPtr GetPlayerAddress(int handle) => new IntPtr((long)PlayerAddressFunc(handle));

			internal static IntPtr GetCheckpointAddress(int handle) {
				ulong addr = CheckpointHandleAddr(CheckpointBaseAddr(), handle);
				return addr == 0 ? IntPtr.Zero : new IntPtr((long)((ulong)addrCheckpointPool + (96 * ((ulong)*(int*)(addr + 16)))));
			}

			internal static int ReadCursorSprite() {
				unsafe {
					return *addrCursorSprite;
				}
			}

			internal static IntPtr GetGameplayCameraAddress() => new IntPtr((long)*addrGamePlayCamAddr);

			[StructLayout(LayoutKind.Sequential)]
			internal unsafe struct Checkpoint {
				internal long padding;
				internal int padding1;
				internal int handle;
				internal long padding2;
				internal Checkpoint* next;
			}
			internal static int[] GetCheckpointHandles() {
				int[] handles = new int[64];
				ulong count = 0;
				for( Checkpoint* item = *(Checkpoint**)(CheckpointBaseAddr() + 48); item != null && count < 64; item = item->next ) {
					handles[count++] = item->handle;
				}
				int[] dataArray = new int[count];
				unsafe {
					fixed ( int* ptrBuffer = &dataArray[0] ) {
						Copy(handles, 0, new IntPtr(ptrBuffer), (int)count);
					}
				}
				return dataArray;
			}

			public unsafe static byte* FindPattern(byte[] pattern) {
				ProcessModule module = Process.GetCurrentProcess().MainModule;
				ulong addr = (ulong)module.BaseAddress.ToInt64();
				ulong end = addr + (ulong)module.ModuleMemorySize;
				for( ; addr < end; addr++ ) {
					for(int i = 0; i < pattern.Length; i++ ) {
						if( pattern[i] != 0 && pattern[i] != ((byte*)addr)[i] ) {
							break;
						} else if( i == pattern.Length - 1 ) {
							return (byte*)addr;
						}
					}
				}
				return null;
			}

		}
	}
}
