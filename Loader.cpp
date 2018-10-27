/**
 * Copyright (C) 2015 crosire
 *
 * This software is  provided 'as-is', without any express  or implied  warranty. In no event will the
 * authors be held liable for any damages arising from the use of this software.
 * Permission  is granted  to anyone  to use  this software  for  any  purpose,  including  commercial
 * applications, and to alter it and redistribute it freely, subject to the following restrictions:
 *
 *   1. The origin of this software must not be misrepresented; you must not claim that you  wrote the
 *      original  software. If you use this  software  in a product, an  acknowledgment in the product
 *      documentation would be appreciated but is not required.
 *   2. Altered source versions must  be plainly  marked as such, and  must not be  misrepresented  as
 *      being the original software.
 *   3. This notice may not be removed or altered from any source distribution.
 */

using namespace System;
using namespace System::IO;
using namespace System::Reflection;


ref struct Loader
{
	static MethodInfo^ OnInit;
	static MethodInfo^ OnTick;
	static MethodInfo^ OnKey;
	static MethodInfo^ OnAbort;
	static TextWriter^ LogFile;
};

static bool sGameReloaded = false;
static bool sAbortRequested = false;

void Log(String ^message) {
	if (Loader::LogFile == nullptr) return;
	Loader::LogFile->WriteLine(message);
	Loader::LogFile->Flush();
}

void OnAbort() {
	Log("Loader::OnAbort()");
	if (Loader::OnAbort != nullptr) Loader::OnAbort->Invoke(nullptr, nullptr);
	Loader::OnInit = Loader::OnTick = Loader::OnKey = Loader::OnAbort = nullptr;
	if (Loader::LogFile != nullptr) Loader::LogFile->Flush();
	// Loader::LogFile = nullptr;
	Log("Loader:OnAbort complete.");
}

bool OnInit() {
	Log("Loader::Restarting..");
	OnAbort();
	if (Loader::LogFile == nullptr) {
		Loader::LogFile = TextWriter::Synchronized(gcnew IO::StreamWriter(
			Path::ChangeExtension(Assembly::GetExecutingAssembly()->Location, ".log"))
		);
	}
	Log("Loader::OnInit() starting");

	Assembly ^assembly;
	try {
		Log("Trying to load Main.shiv");
		assembly = Assembly::Load(File::ReadAllBytes("Main.shiv"));
	} catch (Exception ^ex) {
		Log("Fatal error loading Main.shiv:");
		Log(ex->ToString());
		return false;
	}
	Type ^type = nullptr; // ->GetType() never finds it by name so we search
	for each (auto _type in assembly->GetTypes()) {
		if (_type->Name->Equals("Shiv")) {
			Log("Found Type: " + _type->Name);
			type = _type;
			break;
		}
	}
	if (type == nullptr) {
		Log("Fatal error loading Main.shiv: No 'Shiv' class found as entry point.");
		return false;
	}
	Log(String::Concat("Found Shiv class:", type->GUID));
	try {
		Loader::OnInit = type->GetMethod("OnInit", BindingFlags::Public | BindingFlags::Static);
		Loader::OnTick = type->GetMethod("OnTick", BindingFlags::Public | BindingFlags::Static);
		Loader::OnKey = type->GetMethod("OnKey", BindingFlags::Public | BindingFlags::Static);
		Loader::OnAbort = type->GetMethod("OnAbort", BindingFlags::Public | BindingFlags::Static);
	}
	catch (AmbiguousMatchException ^e) {
		Log(String::Concat("Fatal: ", e->ToString()));
		Loader::OnInit = Loader::OnTick = Loader::OnKey = Loader::OnAbort = nullptr;
		return false;
	}
	Log(String::Concat("Calling Shiv::OnInit():", Loader::OnInit));
	if (Loader::OnInit != nullptr) {
		array<Object ^> ^args = gcnew array<Object ^>(1) { Loader::LogFile };
		try {
			Loader::OnInit->Invoke(nullptr, args);
		}
		catch (Exception^ err) {
			Log(String::Concat("Fatal error in OnInit:", err->ToString()));
			Loader::OnInit = Loader::OnTick = Loader::OnKey = Loader::OnAbort = nullptr;
			return false;
		}
	}

	if (Loader::OnInit == nullptr) Log("Warning: loaded Shiv class with no OnInit!");
	if (Loader::OnTick == nullptr) Log("Warning: loaded Shiv class with no OnTick!");
	if (Loader::OnKey == nullptr) Log("Warning: loaded Shiv class with no OnKey!");
	if (Loader::OnAbort == nullptr) Log("Warning: loaded Shiv class with no OnAbort!");

	Log("Loader: OnInit() complete.");
	return true;

}

void OnTick() {
	try {
		if (Loader::OnTick != nullptr) {
			Loader::OnTick->Invoke(nullptr, nullptr);
		}
	} catch (Exception ^e) {
		Log(String::Concat("Loader: In Shiv::OnTick():", e->ToString()));
		OnAbort();
	}
}

void OnKey(unsigned long key, unsigned short repeats, unsigned char scanCode, bool wasDownBefore, bool isUpNow, bool statusCtrl, bool statusShift, bool statusAlt) {
	// F3
	if (key == 114 && isUpNow && wasDownBefore ) {
		sAbortRequested = true;
	}
	else {
		if (Loader::OnKey != nullptr) {
			array<Object ^> ^args = gcnew array<Object ^>(8) { key, repeats, scanCode, wasDownBefore, isUpNow, statusCtrl, statusShift, statusAlt };
			try {
				Loader::OnKey->Invoke(nullptr, args);
			}
			catch (Exception ^e) {
				Log(String::Concat("Loader: In Shiv::OnKey():", e->ToString()));
			}
		}
	}
}


#pragma unmanaged

#include <Main.h>
#include <Windows.h>

PVOID sMainFib = nullptr;
PVOID sScriptFib = nullptr;

static void ScriptMain()
{
	sGameReloaded = true;

	// ScriptHookV already turned the current thread into a fiber, so we can safely retrieve it.
	sMainFib = GetCurrentFiber();

	// Check if our CLR fiber already exists. It should be created only once for the entire lifetime of the game process.
	if (sScriptFib == nullptr)
	{
		const LPFIBER_START_ROUTINE FiberMain = [](LPVOID lpFiberParameter) {
			while (true) {
				OnInit();
				sGameReloaded = false;

				// If the game is reloaded, ScriptHookV will call the script main function again.
				// This will set the global 'sGameReloaded' variable to 'true',
				// on the next fiber switch to our CLR fiber, run into this condition,
				// then exiting the inner loop to re-call OnInit().
				while (!sGameReloaded) {
					OnTick();
					SwitchToFiber(sMainFib);
					if (sAbortRequested) {
						OnAbort();
						sAbortRequested = false;
						break;
					}
				}
			}
		};

		// Create our own fiber for hosting the CLR
		sScriptFib = CreateFiber(0, FiberMain, nullptr);
	}

	while (true) {
		// Yield execution and give it back to ScriptHookV.
		scriptWait(0);

		// Switch to our CLR fiber and wait for it to switch back.
		SwitchToFiber(sScriptFib);
	}
}
static void ScriptKey(DWORD key, WORD repeats, BYTE scanCode, BOOL isExtended, BOOL isWithAlt, BOOL wasDownBefore, BOOL isUpNow)
{
	OnKey(key, repeats, scanCode, wasDownBefore, isUpNow,
		(GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0,
		(GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0,
		isWithAlt != FALSE);
}

BOOL WINAPI DllMain(HMODULE hModule, DWORD fdwReason, LPVOID lpvReserved)
{
	switch (fdwReason)
	{
	case DLL_PROCESS_ATTACH:
		DisableThreadLibraryCalls(hModule);
		scriptRegister(hModule, &ScriptMain);
		keyboardHandlerRegister(&ScriptKey);
		break;
	case DLL_PROCESS_DETACH:
		DeleteFiber(sScriptFib);
		scriptUnregister(hModule);
		keyboardHandlerUnregister(&ScriptKey);
		break;
	}

	return TRUE;
}
