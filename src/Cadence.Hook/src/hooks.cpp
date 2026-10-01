// hooks.cpp – Hooks fuer DXGI (DirectX 10/11/12) und OpenGL
#include <windows.h>
#include <dxgi1_2.h>
#include <d3d11.h>
#include <d3d12.h>
#include <cstdio>
#include <MinHook.h>

#include "hooks.h"
#include "limiter.h"

namespace cadence {

static SharedBlock* g_shared = nullptr;

// Verhindert doppeltes Begrenzen, wenn ein Present intern ein anderes aufruft
// (z. B. Present -> Present1 oder fremde Overlays, die ebenfalls hooken).
static thread_local int t_depth = 0;

struct DepthGuard {
    bool outer;
    DepthGuard() : outer(t_depth++ == 0) {}
    ~DepthGuard() { --t_depth; }
};

static void SetError(const char* msg) {
    if (!g_shared) return;
    strncpy_s(g_shared->lastError, sizeof(g_shared->lastError), msg, _TRUNCATE);
}

// ------------------------------------------------------------------ DXGI ---
// Je nach Windows-Version liegen D3D11- und D3D12-Swapchains auf unterschiedlichen
// Present-Funktionen. Deshalb bis zu zwei Ziele je Funktion, jedes mit eigenem Original.
constexpr int kMaxTargets = 2;

using PresentFn  = HRESULT(STDMETHODCALLTYPE*)(IDXGISwapChain*, UINT, UINT);
using Present1Fn = HRESULT(STDMETHODCALLTYPE*)(IDXGISwapChain1*, UINT, UINT,
                                               const DXGI_PRESENT_PARAMETERS*);

static PresentFn  g_presentOrig[kMaxTargets]  = {};
static Present1Fn g_present1Orig[kMaxTargets] = {};
static void*      g_presentTargets[kMaxTargets]  = {};
static void*      g_present1Targets[kMaxTargets] = {};

template <int N>
static HRESULT STDMETHODCALLTYPE PresentDetour(IDXGISwapChain* sc, UINT sync, UINT flags) {
    DepthGuard guard;
    // DXGI_PRESENT_TEST zeigt nichts an -> nicht begrenzen und nicht zaehlen.
    const bool limit = guard.outer && !(flags & DXGI_PRESENT_TEST);
    if (limit) GlobalLimiter().OnPresent(g_shared, true);
    const HRESULT hr = g_presentOrig[N](sc, sync, flags);
    if (limit) GlobalLimiter().OnPresent(g_shared, false);
    return hr;
}

template <int N>
static HRESULT STDMETHODCALLTYPE Present1Detour(IDXGISwapChain1* sc, UINT sync, UINT flags,
                                                const DXGI_PRESENT_PARAMETERS* params) {
    DepthGuard guard;
    const bool limit = guard.outer && !(flags & DXGI_PRESENT_TEST);
    if (limit) GlobalLimiter().OnPresent(g_shared, true);
    const HRESULT hr = g_present1Orig[N](sc, sync, flags, params);
    if (limit) GlobalLimiter().OnPresent(g_shared, false);
    return hr;
}

static void* const kPresentDetours[kMaxTargets]  = {(void*)&PresentDetour<0>,  (void*)&PresentDetour<1>};
static void* const kPresent1Detours[kMaxTargets] = {(void*)&Present1Detour<0>, (void*)&Present1Detour<1>};

// Ein unsichtbares Fenster fuer die Hilfs-Swapchains.
class DummyWindow {
public:
    DummyWindow() {
        WNDCLASSEXW wc{};
        wc.cbSize = sizeof(wc);
        wc.lpfnWndProc = DefWindowProcW;
        wc.hInstance = GetModuleHandleW(nullptr);
        wc.lpszClassName = L"CadenceDummyWindow";
        RegisterClassExW(&wc);
        hwnd = CreateWindowExW(0, wc.lpszClassName, L"", WS_OVERLAPPEDWINDOW, 0, 0, 8, 8,
                               nullptr, nullptr, wc.hInstance, nullptr);
    }
    ~DummyWindow() {
        if (hwnd) DestroyWindow(hwnd);
        UnregisterClassW(L"CadenceDummyWindow", GetModuleHandleW(nullptr));
    }
    HWND hwnd = nullptr;
};

template <typename T>
static void SafeRelease(T*& p) {
    if (p) { p->Release(); p = nullptr; }
}

// GetProcAddress liefert FARPROC; ueber void* casten, um Warnungen zu vermeiden.
template <typename T>
static T ProcAs(HMODULE mod, const char* name) {
    return reinterpret_cast<T>(reinterpret_cast<void*>(GetProcAddress(mod, name)));
}

static void* VtableEntry(void* obj, int index) {
    return (*reinterpret_cast<void***>(obj))[index];
}

// IDXGISwapChain::Present = Index 8, IDXGISwapChain1::Present1 = Index 22
constexpr int kVtPresent  = 8;
constexpr int kVtPresent1 = 22;

static void CollectFromSwapChain(IDXGISwapChain* sc, void*& present, void*& present1) {
    present = VtableEntry(sc, kVtPresent);
    IDXGISwapChain1* sc1 = nullptr;
    if (SUCCEEDED(sc->QueryInterface(__uuidof(IDXGISwapChain1), reinterpret_cast<void**>(&sc1)))) {
        present1 = VtableEntry(sc1, kVtPresent1);
        sc1->Release();
    }
}

static bool ProbeD3D11(HWND hwnd, void*& present, void*& present1) {
    HMODULE mod = LoadLibraryW(L"d3d11.dll");
    if (!mod) return false;
    auto create = ProcAs<PFN_D3D11_CREATE_DEVICE_AND_SWAP_CHAIN>(mod, "D3D11CreateDeviceAndSwapChain");
    if (!create) return false;

    DXGI_SWAP_CHAIN_DESC desc{};
    desc.BufferCount = 2;
    desc.BufferDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    desc.BufferDesc.Width = 8;
    desc.BufferDesc.Height = 8;
    desc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    desc.OutputWindow = hwnd;
    desc.SampleDesc.Count = 1;
    desc.Windowed = TRUE;
    desc.SwapEffect = DXGI_SWAP_EFFECT_FLIP_DISCARD;

    IDXGISwapChain* sc = nullptr;
    ID3D11Device* dev = nullptr;
    ID3D11DeviceContext* ctx = nullptr;
    const D3D_DRIVER_TYPE drivers[] = {D3D_DRIVER_TYPE_HARDWARE, D3D_DRIVER_TYPE_WARP};
    HRESULT hr = E_FAIL;
    for (D3D_DRIVER_TYPE d : drivers) {
        hr = create(nullptr, d, nullptr, 0, nullptr, 0, D3D11_SDK_VERSION, &desc, &sc, &dev,
                    nullptr, &ctx);
        if (SUCCEEDED(hr)) break;
    }
    if (FAILED(hr) || !sc) return false;

    CollectFromSwapChain(sc, present, present1);
    SafeRelease(sc);
    SafeRelease(ctx);
    SafeRelease(dev);
    return present != nullptr;
}

static bool ProbeD3D12(HWND hwnd, void*& present, void*& present1) {
    // Nur, wenn das Spiel D3D12 bereits geladen hat – sonst nicht unnoetig laden.
    HMODULE d3d12 = GetModuleHandleW(L"d3d12.dll");
    HMODULE dxgi = GetModuleHandleW(L"dxgi.dll");
    if (!d3d12 || !dxgi) return false;

    auto createDevice = ProcAs<PFN_D3D12_CREATE_DEVICE>(d3d12, "D3D12CreateDevice");
    using CreateFactoryFn = HRESULT(WINAPI*)(REFIID, void**);
    auto createFactory = ProcAs<CreateFactoryFn>(dxgi, "CreateDXGIFactory1");
    if (!createDevice || !createFactory) return false;

    ID3D12Device* dev = nullptr;
    ID3D12CommandQueue* queue = nullptr;
    IDXGIFactory2* factory = nullptr;
    IDXGISwapChain1* sc1 = nullptr;
    bool ok = false;

    if (SUCCEEDED(createDevice(nullptr, D3D_FEATURE_LEVEL_11_0, __uuidof(ID3D12Device),
                               reinterpret_cast<void**>(&dev)))) {
        D3D12_COMMAND_QUEUE_DESC qd{};
        qd.Type = D3D12_COMMAND_LIST_TYPE_DIRECT;
        if (SUCCEEDED(dev->CreateCommandQueue(&qd, __uuidof(ID3D12CommandQueue),
                                              reinterpret_cast<void**>(&queue))) &&
            SUCCEEDED(createFactory(__uuidof(IDXGIFactory2), reinterpret_cast<void**>(&factory)))) {
            DXGI_SWAP_CHAIN_DESC1 desc{};
            desc.Width = 8;
            desc.Height = 8;
            desc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
            desc.SampleDesc.Count = 1;
            desc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
            desc.BufferCount = 2;
            desc.SwapEffect = DXGI_SWAP_EFFECT_FLIP_DISCARD;
            if (SUCCEEDED(factory->CreateSwapChainForHwnd(queue, hwnd, &desc, nullptr, nullptr, &sc1))) {
                present = VtableEntry(sc1, kVtPresent);
                present1 = VtableEntry(sc1, kVtPresent1);
                ok = true;
            }
        }
    }
    SafeRelease(sc1);
    SafeRelease(factory);
    SafeRelease(queue);
    SafeRelease(dev);
    return ok;
}

static void AddTarget(void* target, void** targets, int& count) {
    if (!target) return;
    for (int i = 0; i < count; ++i)
        if (targets[i] == target) return;
    if (count < kMaxTargets) targets[count++] = target;
}

static bool InstallDxgi() {
    if (!GetModuleHandleW(L"dxgi.dll")) return false; // Spiel nutzt kein DXGI

    DummyWindow wnd;
    if (!wnd.hwnd) { SetError("Hilfsfenster konnte nicht erstellt werden"); return false; }

    int nPresent = 0, nPresent1 = 0;
    void* p = nullptr;
    void* p1 = nullptr;
    if (ProbeD3D11(wnd.hwnd, p, p1)) {
        AddTarget(p, g_presentTargets, nPresent);
        AddTarget(p1, g_present1Targets, nPresent1);
    }
    p = p1 = nullptr;
    if (ProbeD3D12(wnd.hwnd, p, p1)) {
        AddTarget(p, g_presentTargets, nPresent);
        AddTarget(p1, g_present1Targets, nPresent1);
    }

    bool any = false;
    for (int i = 0; i < nPresent; ++i) {
        if (MH_CreateHook(g_presentTargets[i], kPresentDetours[i],
                          reinterpret_cast<void**>(&g_presentOrig[i])) == MH_OK)
            any = true;
    }
    for (int i = 0; i < nPresent1; ++i) {
        if (MH_CreateHook(g_present1Targets[i], kPresent1Detours[i],
                          reinterpret_cast<void**>(&g_present1Orig[i])) == MH_OK)
            any = true;
    }
    if (!any) SetError("DXGI-Present konnte nicht gehookt werden");
    return any;
}

// ---------------------------------------------------------------- OpenGL ---
using SwapBuffersFn = BOOL(WINAPI*)(HDC);
static SwapBuffersFn g_wglSwapOrig = nullptr;

static BOOL WINAPI WglSwapBuffersDetour(HDC dc) {
    DepthGuard guard;
    if (guard.outer) GlobalLimiter().OnPresent(g_shared, true);
    const BOOL ok = g_wglSwapOrig(dc);
    if (guard.outer) GlobalLimiter().OnPresent(g_shared, false);
    return ok;
}

static bool InstallOpenGL() {
    HMODULE gl = GetModuleHandleW(L"opengl32.dll");
    if (!gl) return false;
    void* target = ProcAs<void*>(gl, "wglSwapBuffers");
    if (!target) return false;
    return MH_CreateHook(target, reinterpret_cast<void*>(&WglSwapBuffersDetour),
                         reinterpret_cast<void**>(&g_wglSwapOrig)) == MH_OK;
}

// ------------------------------------------------------------------ API ---
bool InstallHooks(SharedBlock* shared) {
    g_shared = shared;
    if (!GlobalLimiter().Init()) { SetError("Timer konnte nicht erstellt werden"); return false; }
    if (MH_Initialize() != MH_OK) { SetError("MinHook-Initialisierung fehlgeschlagen"); return false; }

    int32_t apis = ApiNone;
    if (InstallDxgi())   apis |= ApiDxgi;
    if (InstallOpenGL()) apis |= ApiOpenGL;

    if (apis == ApiNone) {
        SetError("Keine unterstuetzte Grafik-API gefunden (DirectX 10-12 oder OpenGL)");
        return false;
    }
    if (MH_EnableHook(MH_ALL_HOOKS) != MH_OK) {
        SetError("Hooks konnten nicht aktiviert werden");
        return false;
    }
    if (g_shared) g_shared->apiMask = apis;
    return true;
}

void RemoveHooks() {
    MH_DisableHook(MH_ALL_HOOKS);
    MH_Uninitialize();
    GlobalLimiter().Shutdown();
}

} // namespace cadence
