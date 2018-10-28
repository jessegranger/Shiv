using System;
using System.Runtime.InteropServices;

namespace Shiv {
	public static class Imports {
		[DllImport("ScriptHookV.dll", ExactSpelling = true, EntryPoint = "?nativeInit@@YAX_K@Z")] public static extern void NativeInit(ulong hash);
		[DllImport("ScriptHookV.dll", ExactSpelling = true, EntryPoint = "?nativePush64@@YAX_K@Z")] public static extern void NativePush64(ulong val);
		[DllImport("ScriptHookV.dll", ExactSpelling = true, EntryPoint = "?nativeCall@@YAPEA_KXZ")] public static extern unsafe ulong* NativeCall();
		[DllImport("ScriptHookV.dll", ExactSpelling = true, EntryPoint = "?worldGetAllObjects@@YAHPEAHH@Z")] public static extern unsafe int WorldGetAllObjects(int* storage, int max);
		[DllImport("ScriptHookV.dll", ExactSpelling = true, EntryPoint = "?worldGetAllPeds@@YAHPEAHH@Z")] public static extern unsafe int WorldGetAllPeds(int* storage, int max);
		[DllImport("ScriptHookV.dll", ExactSpelling = true, EntryPoint = "?worldGetAllPickups@@YAHPEAHH@Z")] public static extern unsafe int WorldGetAllPickups(int* storage, int max);
		[DllImport("ScriptHookV.dll", ExactSpelling = true, EntryPoint = "?worldGetAllVehicles@@YAHPEAHH@Z")] public static extern unsafe int WorldGetAllVehicles(int* storage, int max);
		[DllImport("ScriptHookV.dll", ExactSpelling = true, EntryPoint = "?getGameVersion@@YA?AW4eGameVersion@@XZ")] public static extern uint GetGameVersion();
		[DllImport("ScriptHookV.dll", ExactSpelling = true, EntryPoint = "?createTexture@@YAHPEBD@Z")] public static extern int CreateTexture(IntPtr fileNamePtr);
		[DllImport("ScriptHookV.dll", ExactSpelling = true, EntryPoint = "?drawTexture@@YAXHHHHMMMMMMMMMMMM@Z")] public static extern int DrawTexture(int id, int index, int level, int time, float sizeX, float sizeY, float centerX, float centerY, float posX, float posY, float rotation, float scaleFactor, float colorR, float colorG, float colorB, float colorA);
	}
}

