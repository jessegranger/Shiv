using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Shiv;
using static System.Runtime.InteropServices.Marshal;
using static System.Text.Encoding;
using static Shiv.Imports;
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
			public void Dispose() {
				if( Value != IntPtr.Zero ) {
					FreeCoTaskMem(Value);
				}
				Value = IntPtr.Zero;
			}
			public static implicit operator IntPtr(PinnedString s) => s.Value;
			public static implicit operator ulong(PinnedString s) => (ulong)s.Value.ToInt64();
			public static implicit operator long(PinnedString s) => s.Value.ToInt64();

			public static readonly PinnedString STRING = new PinnedString("STRING");
			public static readonly PinnedString CELL_EMAIL_BCON = new PinnedString("CELL_EMAIL_BCON");
			public static readonly PinnedString EMPTY = new PinnedString("");
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

				type = type.IsEnum ? Enum.GetUnderlyingType(type) : type;

				try {
					// Log($"PushArgument {type.Name} {o}");
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
				if( type.IsEnum ) type = Enum.GetUnderlyingType(type);

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
				else if( type == typeof(Vector3) ) { float* data = (float*)o;
					// NOTE: does not use Read<Vector3> because the script API passes NativeVector3 back and forth (with larger fields)
					return new Vector3(data[0], data[2], data[4]);
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

			#region Fields
			internal static ulong* addrCheckpointPool;
			internal static float* addrReadWorldGravity;
			internal static float* addrWriteWorldGravity;
			internal static ulong* addrGamePlayCamAddr;
			internal static int* addrCursorSprite;
			internal static ulong GetLabelTextByHashAddr;
			// internal static ulong* _entityPoolAddress;
			// internal static ulong* _vehiclePoolAddress;
			// internal static ulong* _pedPoolAddress;
			// internal static ulong* _objectPoolAddress;
			// internal static ulong* _cameraPoolAddress;
			// internal static ulong* _pickupObjectPoolAddress;
			/*
			internal static ulong CreateNmMessageFuncAddress;
			internal static ulong GiveNmMessageFuncAddress;
			private static readonly ulong modelHashTable;
			private static readonly ulong modelNum2;
			private static readonly ulong modelNum3;
			private static readonly ulong modelNum4;
			private static readonly int modelNum1;
			private static readonly int handlingIndexOffsetInModelInfo;
			private static readonly uint vehClassOff;
			private static readonly ushort modelHashEntries;
			*/
			#endregion

			#region Delegate Fields For Function Ponters
			internal delegate uint GetHashKeyDelegate(IntPtr stringPtr, uint initialHash);
			internal delegate ulong EntityAddressFuncDelegate(int handle);
			internal delegate ulong PlayerAddressFuncDelegate(int handle);
			internal delegate ulong ParticleFxAddressFuncDelegate(int handle);
			/*
			internal delegate int AddEntityToPoolFuncDelegate(ulong addr);
			internal delegate ulong EntityPositionFuncDelegate(ulong addr, float* position);
			internal delegate ulong EntityModel1FuncDelegate(ulong addr);
			internal delegate ulong EntityModel2FuncDelegate(ulong addr);
			internal delegate ulong GetHandlingDataByIndexDelegate(int index);
			internal delegate ulong GetHandlingDataByHashDelegate(IntPtr hashAddress);
			internal delegate byte SetNmBoolAddressDelegate(ulong messageAddress, IntPtr argumentNamePtr, [MarshalAs(UnmanagedType.I1)] bool value);
			internal delegate byte SetNmIntAddressDelegate(ulong messageAddress, IntPtr argumentNamePtr, int value);
			internal delegate byte SetNmFloatAddressDelegate(ulong messageAddress, IntPtr argumentNamePtr, float value);
			internal delegate byte SetNmVec3AddressDelegate(ulong messageAddress, IntPtr argumentNamePtr, float x, float y, float z);
			internal delegate byte SetNmStringAddressDelegate(ulong messageAddress, IntPtr argumentNamePtr, IntPtr stringPtr);
			*/
			internal delegate ulong CheckpointHandleAddrDelegate(ulong baseAddr, int handle);
			internal delegate ulong GetCheckpointBaseAddrDelegate();
			internal delegate ulong GetLabelTextByHashFuncDelegate(ulong addr, int labelHash);
			internal delegate ulong FuncUlongUlongDelegate(ulong T);

			internal static GetHashKeyDelegate GetHashKeyFunc;
			internal static EntityAddressFuncDelegate EntityAddressFunc;
			internal static PlayerAddressFuncDelegate PlayerAddressFunc;
			internal static ParticleFxAddressFuncDelegate ParticleFxAddressFunc;
			/*
			internal static EntityPositionFuncDelegate EntityPositionFunc;
			internal static AddEntityToPoolFuncDelegate AddEntityToPoolFunc;
			internal static EntityModel1FuncDelegate EntityModel1Func;
			internal static EntityModel2FuncDelegate EntityModel2Func;
			internal static GetHandlingDataByIndexDelegate GetHandlingDataByIndex;
			internal static GetHandlingDataByHashDelegate GetHandlingDataByHash;
			internal static SetNmBoolAddressDelegate SetNmBoolAddress;
			internal static SetNmIntAddressDelegate SetNmIntAddress;
			internal static SetNmFloatAddressDelegate SetNmFloatAddress;
			internal static SetNmVec3AddressDelegate SetNmVec3Address;
			internal static SetNmStringAddressDelegate SetNmStringAddress;
			*/
			internal static CheckpointHandleAddrDelegate CheckpointHandleAddr;
			internal static GetCheckpointBaseAddrDelegate CheckpointBaseAddr;
			internal static GetLabelTextByHashFuncDelegate GetLabelTextByHashFunc;
			#endregion


			static MemoryAccess() {
				byte* addr;

				// Get relative addr and add it to the instruction addr.
				addr = FindPattern("\xE8\x00\x00\x00\x00\x48\x8B\xD8\x48\x85\xC0\x74\x2E\x48\x83\x3D", "x????xxxxxxxxxxx");
				EntityAddressFunc = GetDelegateForFunctionPointer<EntityAddressFuncDelegate>(new IntPtr(*(int*)(addr + 1) + addr + 5));

				addr = FindPattern("\xB2\x01\xE8\x00\x00\x00\x00\x48\x85\xC0\x74\x1C\x8A\x88", "xxx????xxxxxxx");
				PlayerAddressFunc = GetDelegateForFunctionPointer<PlayerAddressFuncDelegate>(new IntPtr(*(int*)(addr + 3) + addr + 7));

				addr = FindPattern("\x74\x21\x48\x8B\x48\x20\x48\x85\xC9\x74\x18\x48\x8B\xD6\xE8", "xxxxxxxxxxxxxxx") - 10;
				ParticleFxAddressFunc = GetDelegateForFunctionPointer<ParticleFxAddressFuncDelegate>(new IntPtr(*(int*)(addr) + addr + 4));

				/*
				addr = FindPattern("\x48\x8B\xDA\xE8\x00\x00\x00\x00\xF3\x0F\x10\x44\x24", "xxxx????xxxxx");
				EntityPositionFunc = GetDelegateForFunctionPointer<EntityPositionFuncDelegate>(new IntPtr((addr - 6)));

				addr = FindPattern("\x48\xF7\xF9\x49\x8B\x48\x08\x48\x63\xD0\xC1\xE0\x08\x0F\xB6\x1C\x11\x03\xD8", "xxxxxxxxxxxxxxxxxxx");
				AddEntityToPoolFunc = GetDelegateForFunctionPointer<AddEntityToPoolFuncDelegate>(new IntPtr(addr - 0x68));

				addr = FindPattern("\x0F\x85\x00\x00\x00\x00\x48\x8B\x4B\x20\xE8\x00\x00\x00\x00\x48\x8B\xC8", "xx????xxxxx????xxx");
				EntityModel1Func = GetDelegateForFunctionPointer<EntityModel1FuncDelegate>(new IntPtr(*(int*)addr + 11 + addr + 15));
				addr = FindPattern("\x45\x33\xC9\x3B\x05", "xxxxx");
				EntityModel2Func = GetDelegateForFunctionPointer<EntityModel2FuncDelegate>(new IntPtr(addr - 0x46));

				addr = FindPattern("\x0F\x84\x00\x00\x00\x00\x8B\x8B\x00\x00\x00\x00\xE8\x00\x00\x00\x00\xBA\x09\x00\x00\x00", "xx????xx????x????xxxxx");
				GetHandlingDataByIndex = GetDelegateForFunctionPointer<GetHandlingDataByIndexDelegate>(new IntPtr(*(int*)(addr + 13) + addr + 17));
				handlingIndexOffsetInModelInfo = *(int*)(addr + 8);
				addr = FindPattern("\xE8\x00\x00\x00\x00\x48\x85\xC0\x75\x5A\xB2\x01", "x????xxxxxxx");
				GetHandlingDataByHash = GetDelegateForFunctionPointer<GetHandlingDataByHashDelegate>(new IntPtr(*(int*)(addr + 1) + addr + 5));

				addr = FindPattern("\x4C\x8B\x0D\x00\x00\x00\x00\x44\x8B\xC1\x49\x8B\x41\x08", "xxx????xxxxxxx");
				_entityPoolAddress = (ulong*)(*(int*)(addr + 3) + addr + 7);
				addr = FindPattern("\x48\x8B\x05\x00\x00\x00\x00\xF3\x0F\x59\xF6\x48\x8B\x08", "xxx????xxxxxxx");
				_vehiclePoolAddress = (ulong*)(*(int*)(addr + 3) + addr + 7);
				addr = FindPattern("\x48\x8B\x05\x00\x00\x00\x00\x41\x0F\xBF\xC8\x0F\xBF\x40\x10", "xxx????xxxxxxxx");
				_pedPoolAddress = (ulong*)(*(int*)(addr + 3) + addr + 7);
				addr = FindPattern("\x48\x8B\x05\x00\x00\x00\x00\x8B\x78\x10\x85\xFF", "xxx????xxxxx");
				_objectPoolAddress = (ulong*)(*(int*)(addr + 3) + addr + 7);
				addr = FindPattern("\x4C\x8B\x05\x00\x00\x00\x00\x40\x8A\xF2\x8B\xE9", "xxx????xxxxx");
				_pickupObjectPoolAddress = (ulong*)(*(int*)(addr + 3) + addr + 7);

				CreateNmMessageFuncAddress = (ulong)FindPattern("\x33\xDB\x48\x89\x1D\x00\x00\x00\x00\x85\xFF", "xxxxx????xx") - 0x42;
				GiveNmMessageFuncAddress = (ulong)FindPattern("\x48\x8b\xc4\x48\x89\x58\x08\x48\x89\x68\x10\x48\x89\x70\x18\x48\x89\x78\x20\x41\x55\x41\x56\x41\x57\x48\x83\xec\x20\xe8\x00\x00\x00\x00\x48\x8b\xd8\x48\x85\xc0\x0f", "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxx????xxxxxxx");
				addr = FindPattern("\x48\x89\x5C\x24\x00\x57\x48\x83\xEC\x20\x48\x8B\xD9\x48\x63\x49\x0C\x41\x8A\xF8", "xxxx?xxxxxxxxxxxxxxx");
				SetNmBoolAddress = GetDelegateForFunctionPointer<SetNmBoolAddressDelegate>(new IntPtr(addr));
				addr = FindPattern("\x40\x53\x48\x83\xEC\x30\x48\x8B\xD9\x48\x63\x49\x0C", "xxxxxxxxxxxxx");
				SetNmFloatAddress = GetDelegateForFunctionPointer<SetNmFloatAddressDelegate>(new IntPtr(addr));
				addr = FindPattern("\x48\x89\x5C\x24\x00\x57\x48\x83\xEC\x20\x48\x8B\xD9\x48\x63\x49\x0C\x41\x8B\xF8", "xxxx?xxxxxxxxxxxxxxx");
				SetNmIntAddress = GetDelegateForFunctionPointer<SetNmIntAddressDelegate>(new IntPtr(addr));
				addr = FindPattern("\x57\x48\x83\xEC\x20\x48\x8B\xD9\x48\x63\x49\x0C\x49\x8B\xE8", "xxxxxxxxxxxxxxx") - 15;
				SetNmStringAddress = GetDelegateForFunctionPointer<SetNmStringAddressDelegate>(new IntPtr(addr));
				addr = FindPattern("\x40\x53\x48\x83\xEC\x40\x48\x8B\xD9\x48\x63\x49\x0C", "xxxxxxxxxxxxx");
				SetNmVec3Address = GetDelegateForFunctionPointer<SetNmVec3AddressDelegate>(new IntPtr(addr));
				*/

				addr = FindPattern("\x48\x89\x5C\x24\x08\x48\x89\x6C\x24\x18\x89\x54\x24\x10\x56\x57\x41\x56\x48\x83\xEC\x20", "xxxxxxxxxxxxxxxxxxxxxx");
				GetLabelTextByHashFunc = GetDelegateForFunctionPointer<GetLabelTextByHashFuncDelegate>(new IntPtr(addr));
				addr = FindPattern("\x84\xC0\x74\x34\x48\x8D\x0D\x00\x00\x00\x00\x48\x8B\xD3", "xxxxxxx????xxx");
				GetLabelTextByHashAddr = (ulong)(*(int*)(addr + 7) + addr + 11);

				addr = FindPattern("\x8A\x4C\x24\x60\x8B\x50\x10\x44\x8A\xCE", "xxxxxxxxxx");
				CheckpointBaseAddr = GetDelegateForFunctionPointer<GetCheckpointBaseAddrDelegate>(new IntPtr(*(int*)(addr - 19) + addr - 15));
				CheckpointHandleAddr = GetDelegateForFunctionPointer<CheckpointHandleAddrDelegate>(new IntPtr(*(int*)(addr - 9) + addr - 5));
				addrCheckpointPool = (ulong*)(*(int*)(addr + 17) + addr + 21);

				addr = FindPattern("\x48\x8B\x0B\x33\xD2\xE8\x00\x00\x00\x00\x89\x03", "xxxxxx????xx");
				GetHashKeyFunc = GetDelegateForFunctionPointer<GetHashKeyDelegate>(new IntPtr(*(int*)(addr + 6) + addr + 10));

				addr = FindPattern("\x48\x63\xC1\x48\x8D\x0D\x00\x00\x00\x00\xF3\x0F\x10\x04\x81\xF3\x0F\x11\x05\x00\x00\x00\x00", "xxxxxx????xxxxxxxxx????");
				addrWriteWorldGravity = (float*)(*(int*)(addr + 6) + addr + 10);
				addrReadWorldGravity = (float*)(*(int*)(addr + 19) + addr + 23);

				addr = FindPattern("\x74\x11\x8B\xD1\x48\x8D\x0D\x00\x00\x00\x00\x45\x33\xC0", "xxxxxxx????xxx");
				addrCursorSprite = (int*)(*(int*)(addr - 4) + addr);

				addr = FindPattern("\x48\x8B\xC7\xF3\x0F\x10\x0D", "xxxxxxx") - 0x1D;
				addr = addr + *(int*)addr + 4;
				addrGamePlayCamAddr = (ulong*)(*(int*)(addr + 3) + addr + 7);

				/*
				addr = FindPattern("\x48\x8B\xC8\xEB\x02\x33\xC9\x48\x85\xC9\x74\x26", "xxxxxxxxxxxx") - 9;
				_cameraPoolAddress = (ulong*)(*(int*)(addr) + addr + 4);

				addr = FindPattern("\x66\x81\xF9\x00\x00\x74\x10\x4D\x85\xC0", "xxx??xxxxx") - 0x21;
				byte* baseFuncAddr = addr + *(int*)(addr) + 4;
				modelHashEntries = *(ushort*)(baseFuncAddr + *(int*)(baseFuncAddr + 3) + 7);
				modelNum1 = *(int*)(*(int*)(baseFuncAddr + 0x52) + baseFuncAddr + 0x56);
				modelNum2 = *(ulong*)(*(int*)(baseFuncAddr + 0x63) + baseFuncAddr + 0x67);
				modelNum3 = *(ulong*)(*(int*)(baseFuncAddr + 0x7A) + baseFuncAddr + 0x7E);
				modelNum4 = *(ulong*)(*(int*)(baseFuncAddr + 0x81) + baseFuncAddr + 0x85);
				modelHashTable = *(ulong*)(*(int*)(baseFuncAddr + 0x24) + baseFuncAddr + 0x28);
				vehClassOff = *(uint*)(addr + 0x31);
				*/

			}

			/// <summary> Read one unmanaged value from an (address + offset). </summary>
			/// <returns>default(T) if address is invalid.</returns>
			public static T Read<T>(IntPtr addr, int off) where T : unmanaged {
				if( addr != IntPtr.Zero ) {
					unsafe { return *(T*)(addr + off).ToPointer(); }
				}
				return default;
			}

			/// <summary> Write one unmanaged value to an (address + offset). </summary>
			public static void Write<T>(IntPtr addr, int off, T value) where T : unmanaged {
				if( addr != IntPtr.Zero ) {
					unsafe { *(T*)(addr + off).ToPointer() = value; }
				}
			}

			public static String ReadString(IntPtr addr, int off) {
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
				if( addr == IntPtr.Zero || bit > 31 )
					return;
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

			public static uint GetHashKey(String toHash) {
				using( var handle = new PinnedString(toHash) ) {
					return handle == 0 ? 0 : GetHashKeyFunc(handle, 0);
				}
			}

			internal static string GetLabelTextByHash(int entryLabelHash) {
				char* entryText = (char*)GetLabelTextByHashFunc(GetLabelTextByHashAddr, entryLabelHash);
				return entryText != null ? ReadString(new IntPtr(entryText), 0x0) : $"LABEL_{entryLabelHash}";
			}

			public static IntPtr GetEntityAddress(int handle) {
				return new IntPtr((long)EntityAddressFunc(handle));
			}
			internal static IntPtr GetPlayerAddress(int handle) {
				return new IntPtr((long)PlayerAddressFunc(handle));
			}
			internal static IntPtr GetCheckpointAddress(int handle) {
				ulong addr = CheckpointHandleAddr(CheckpointBaseAddr(), handle);
				return addr == 0 ? IntPtr.Zero : new IntPtr((long)((ulong)addrCheckpointPool + (96 * ((ulong)*(int*)(addr + 16)))));
			}

			internal static IntPtr GetParticleFxAddress(int handle) {
				return new IntPtr((long)ParticleFxAddressFunc(handle));
			}

			internal static IntPtr GetEntityBoneMatrixAddress(int handle, uint boneIndex) {
				ulong fragSkeletonData = GetEntitySkeletonData(handle);
				if( fragSkeletonData == 0 ) {
					return IntPtr.Zero;
				}
				unsafe {
					int maxBones = *(int*)(fragSkeletonData + 32);
					if( boneIndex < maxBones ) {
						ulong boneBase = *(ulong*)(fragSkeletonData + 24);
						return new IntPtr((long)(boneBase + (boneIndex * 0x40)));
					}
				}
				return IntPtr.Zero;
			}

			internal static IntPtr GetEntityBonePoseAddress(int handle, uint boneIndex) {
				ulong fragSkeletonData = GetEntitySkeletonData(handle);
				if( fragSkeletonData == 0 ) {
					return IntPtr.Zero;
				}
				unsafe {
					int maxBones = *(int*)(fragSkeletonData + 32);
					if( boneIndex < maxBones ) {
						ulong boneArrayBase = *(ulong*)(fragSkeletonData + 16);
						return new IntPtr((long)(boneArrayBase + (boneIndex * 0x40)));
					}
				}
				return IntPtr.Zero;
			}

			private unsafe static ulong GetEntitySkeletonData(int handle) {
				ulong MemAddress = EntityAddressFunc(handle);
				ulong Addr2, Addr3 = 0;
				FuncUlongUlongDelegate func2 = GetDelegateForFunctionPointer<FuncUlongUlongDelegate>(ReadIntPtr(ReadIntPtr(new IntPtr((long)MemAddress)) + 88));
				Addr2 = func2(MemAddress);
				if( Addr2 == 0 ) {
					Addr3 = *(ulong*)(MemAddress + 80);
					if( Addr3 == 0 ) {
						return 0;
					} else {
						Addr3 = *(ulong*)(Addr3 + 40);
					}
				} else {
					Addr3 = *(ulong*)(Addr2 + 104);
					if( Addr3 == 0 || *(ulong*)(Addr2 + 120) == 0 ) {
						return 0;
					} else {
						Addr3 = *(ulong*)(Addr3 + 376);
					}
				}
				return Addr3;
			}

			internal static float ReadWorldGravity() {
				unsafe {
					return *addrReadWorldGravity;
				}
			}
			internal static void WriteWorldGravity(float value) {
				unsafe {
					*addrWriteWorldGravity = value;
				}
			}

			internal static int ReadCursorSprite() {
				unsafe {
					return *addrCursorSprite;
				}
			}

			internal static IntPtr GetGameplayCameraAddress() {
				Log($"Reading Gameplay camera address {new IntPtr((long)*addrGamePlayCamAddr).ToInt64()} from {(long)addrGamePlayCamAddr}");
				return new IntPtr((long)*addrGamePlayCamAddr);
			}

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

			// we dont lock this because its only called from the static constructor (before any threads can start)
			// if that ever changes, it should hold at least a read lock for this whole search
			public unsafe static byte* FindPattern(string pattern, string mask) {
				ProcessModule module = Process.GetCurrentProcess().MainModule;

				ulong addr = (ulong)module.BaseAddress.ToInt64();
				ulong endAddress = addr + (ulong)module.ModuleMemorySize;

				for( ; addr < endAddress; addr++ ) {
					for( int i = 0; i < pattern.Length; i++ ) {
						if( mask[i] != '?' && ((byte*)addr)[i] != pattern[i] ) {
							break;
						} else if( i + 1 == pattern.Length ) {
							return (byte*)addr;
						}
					}
				}

				return null;
			}

		}
	}
}
