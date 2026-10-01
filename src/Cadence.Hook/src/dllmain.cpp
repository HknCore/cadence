// dllmain.cpp – Einstiegspunkt der Cadence-Hook-DLL
#include <windows.h>
#include <cstdio>
#include "cadence_shared.h"
#include "hooks.h"

using namespace cadence;

static HANDLE       g_mapping = nullptr;
static SharedBlock* g_shared  = nullptr;

static SharedBlock* OpenShared() {
    wchar_t name[64];
    swprintf_s(name, L"Local\\Cadence_%lu", GetCurrentProcessId());
    g_mapping = OpenFileMappingW(FILE_MAP_ALL_ACCESS, FALSE, name);
    if (!g_mapping) return nullptr;
    auto* block = static_cast<SharedBlock*>(
        MapViewOfFile(g_mapping, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(SharedBlock)));
    if (!block || block->magic != kSharedMagic || block->version != kSharedVersion) {
        if (block) UnmapViewOfFile(block);
        CloseHandle(g_mapping);
        g_mapping = nullptr;
        return nullptr;
    }
    return block;
}

// Die eigentliche Initialisierung laeuft in einem eigenen Thread,
// weil in DllMain (Loader-Lock) kein D3D-Device erstellt werden darf.
static DWORD WINAPI InitThread(LPVOID) {
    g_shared = OpenShared();
    if (!g_shared) return 1; // ohne App-Verbindung bleibt die DLL passiv

    g_shared->hookState = static_cast<int32_t>(HookState::Initializing);
    const bool ok = InstallHooks(g_shared);
    g_shared->hookState = static_cast<int32_t>(ok ? HookState::Active : HookState::Failed);
    return ok ? 0 : 2;
}

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID reserved) {
    switch (reason) {
    case DLL_PROCESS_ATTACH: {
        DisableThreadLibraryCalls(module);
        HANDLE t = CreateThread(nullptr, 0, InitThread, nullptr, 0, nullptr);
        if (t) CloseHandle(t);
        break;
    }
    case DLL_PROCESS_DETACH:
        // reserved != nullptr: Prozess endet ohnehin, nichts mehr anfassen.
        if (reserved == nullptr && g_shared) {
            RemoveHooks();
            UnmapViewOfFile(g_shared);
            CloseHandle(g_mapping);
        }
        break;
    }
    return TRUE;
}
