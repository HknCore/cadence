// cadence_shared.h
// Gemeinsamer Speicherbereich zwischen der Cadence-App (C#) und der Hook-DLL.
// Das Layout ist fest und MUSS mit Cadence.Core/Interop/SharedLayout.cs uebereinstimmen.
#pragma once
#include <cstddef>
#include <cstdint>

namespace cadence {

constexpr uint32_t kSharedMagic   = 0x434E4443; // "CDNC"
constexpr uint32_t kSharedVersion = 1;
constexpr uint32_t kRingSize      = 4096;       // Frametimes im Ringpuffer
constexpr uint32_t kHeartbeatTimeoutMs = 2000;

// Name: L"Local\\Cadence_<PID>" – die App legt ihn vor der Injektion an.
constexpr const wchar_t* kSharedNameFormat = L"Local\\Cadence_%lu";

enum class Mode : int32_t {
    Balanced   = 0, // wartet vor Present, gleichmaessige Ausgabe
    LowLatency = 1, // wartet nach Present (Frame-Start im Takt), weniger Input-Lag
    Smooth     = 2, // wie Balanced, laengeres Spin-Fenster fuer maximale Praezision
};

enum class HookState : int32_t {
    None         = 0,
    Initializing = 1,
    Active       = 2,
    Failed       = 3,
};

enum ApiFlags : int32_t {
    ApiNone   = 0,
    ApiDxgi   = 1 << 0, // DirectX 10 / 11 / 12
    ApiOpenGL = 1 << 1,
};

#pragma pack(push, 8)
struct SharedBlock {
    // ---- Kopf ---------------------------------------------- Offset
    uint32_t magic;                 //  0
    uint32_t version;               //  4

    // ---- Konfiguration (schreibt die App) -------------------------
    volatile int32_t enabled;       //  8  0 = durchreichen, 1 = begrenzen
    volatile int32_t mode;          // 12  cadence::Mode
    volatile double  targetFps;     // 16  <= 0 bedeutet unbegrenzt
    // Herzschlag der App: GetTickCount() beim letzten Lebenszeichen, 0 = keine Ueberwachung.
    // Bleibt er laenger als kHeartbeatTimeoutMs aus (App beendet/abgestuerzt), gibt die DLL das Limit frei.
    volatile uint32_t heartbeatMs;  // 24
    int32_t          pad0;          // 28

    // ---- Status (schreibt die DLL) --------------------------------
    volatile int32_t hookState;     // 32  cadence::HookState
    volatile int32_t apiMask;       // 36  cadence::ApiFlags
    volatile int64_t frameCount;    // 40
    volatile uint32_t writeIndex;   // 48  naechster Slot im Ring (monoton steigend)
    uint32_t         pad1;          // 52
    float frametimesMs[kRingSize];  // 56
    char  lastError[256];           // 56 + 4 * 4096 = 16440
};                                  // Groesse: 16696
#pragma pack(pop)

static_assert(sizeof(SharedBlock) == 16696, "SharedBlock-Layout hat sich geaendert");
static_assert(offsetof(SharedBlock, targetFps) == 16, "Offset targetFps");
static_assert(offsetof(SharedBlock, hookState) == 32, "Offset hookState");
static_assert(offsetof(SharedBlock, frameCount) == 40, "Offset frameCount");
static_assert(offsetof(SharedBlock, writeIndex) == 48, "Offset writeIndex");
static_assert(offsetof(SharedBlock, frametimesMs) == 56, "Offset frametimesMs");
static_assert(offsetof(SharedBlock, lastError) == 16440, "Offset lastError");

} // namespace cadence
