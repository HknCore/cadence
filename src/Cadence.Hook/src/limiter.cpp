// limiter.cpp
#include "limiter.h"
#include <timeapi.h>

#ifndef CREATE_WAITABLE_TIMER_HIGH_RESOLUTION
#define CREATE_WAITABLE_TIMER_HIGH_RESOLUTION 0x00000002
#endif

namespace cadence {

Limiter& GlobalLimiter() {
    static Limiter instance;
    return instance;
}

bool Limiter::Init() {
    QueryPerformanceFrequency(&freq_);

    // Ab Windows 10 1803: hochaufloesender Timer (~0.5 ms) ohne timeBeginPeriod.
    timer_ = CreateWaitableTimerExW(nullptr, nullptr, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION,
                                    TIMER_ALL_ACCESS);
    highRes_ = timer_ != nullptr;
    if (!timer_) {
        // Fallback fuer aeltere Systeme: normaler Timer + 1-ms-Systemtakt.
        timer_ = CreateWaitableTimerExW(nullptr, nullptr, 0, TIMER_ALL_ACCESS);
        timeBeginPeriod(1);
    }
    return timer_ != nullptr;
}

void Limiter::Shutdown() {
    if (!highRes_) timeEndPeriod(1);
    if (timer_) CloseHandle(timer_);
    timer_ = nullptr;
}

int64_t Limiter::Now() const {
    LARGE_INTEGER t;
    QueryPerformanceCounter(&t);
    return t.QuadPart;
}

// Hybrides Warten: grob schlafen ueber den Timer, die letzten Mikrosekunden aktiv spinnen.
void Limiter::WaitUntil(int64_t deadline, double spinUs) {
    const double ticksPerUs = static_cast<double>(freq_.QuadPart) / 1e6;
    if (!highRes_) spinUs += 1000.0; // grober Timer -> laengeres Spin-Fenster

    for (;;) {
        const int64_t remain = deadline - Now();
        if (remain <= 0) return;

        const double remainUs = remain / ticksPerUs;
        if (timer_ && remainUs > spinUs + 50.0) {
            LARGE_INTEGER due;
            // negativ = relativ, Einheit 100 ns
            due.QuadPart = -static_cast<LONGLONG>((remainUs - spinUs) * 10.0);
            if (SetWaitableTimerEx(timer_, &due, 0, nullptr, nullptr, nullptr, 0)) {
                WaitForSingleObject(timer_, INFINITE);
                continue;
            }
        }
        YieldProcessor();
    }
}

void Limiter::Record(SharedBlock* shared, int64_t stamp) {
    if (lastStamp_ != 0) {
        const double ms = (stamp - lastStamp_) * 1000.0 / static_cast<double>(freq_.QuadPart);
        const uint32_t idx = shared->writeIndex;
        shared->frametimesMs[idx % kRingSize] = static_cast<float>(ms);
        MemoryBarrier();                // Wert sichtbar machen, bevor der Index steigt
        shared->writeIndex = idx + 1;
    }
    lastStamp_ = stamp;
    shared->frameCount = shared->frameCount + 1;
}

void Limiter::Pace(SharedBlock* shared, double fps, Mode mode) {
    const int64_t now = Now();

    // Bei Zieländerung oder Moduswechsel neu synchronisieren.
    if (fps != lastFps_ || mode != lastMode_) {
        next_ = 0;
        lastFps_ = fps;
        lastMode_ = mode;
    }

    if (fps <= 0.0) {          // unbegrenzt: nur messen
        next_ = 0;
        Record(shared, now);
        return;
    }

    const int64_t period = static_cast<int64_t>(static_cast<double>(freq_.QuadPart) / fps);

    if (next_ == 0) {          // erster Frame nach (Re-)Sync
        next_ = now + period;
        Record(shared, now);
        return;
    }

    const int64_t target = next_;
    if (now < target) {
        const double spinUs = (mode == Mode::Smooth) ? 2000.0 : 1000.0;
        WaitUntil(target, spinUs);
    }

    const int64_t after = Now();
    // Im Takt bleiben. Ist das Spiel mehr als einen ganzen Frame zu spaet,
    // neu ansetzen statt aufzuholen (sonst kaeme ein Schwall schneller Frames).
    next_ = (after - target > period) ? after + period : target + period;

    Record(shared, after);
}

void Limiter::OnPresent(SharedBlock* shared, bool beforePresent) {
    if (!shared) return;

    // Ohne Lebenszeichen der App (beendet oder abgestuerzt) laeuft das Spiel unbegrenzt weiter.
    const uint32_t hb = shared->heartbeatMs;
    const bool appAlive = hb == 0 || static_cast<uint32_t>(GetTickCount() - hb) < kHeartbeatTimeoutMs;
    const bool enabled = shared->enabled != 0 && appAlive;
    const double fps = enabled ? shared->targetFps : 0.0;
    Mode mode = static_cast<Mode>(shared->mode);
    if (mode != Mode::Balanced && mode != Mode::LowLatency && mode != Mode::Smooth)
        mode = Mode::Balanced;

    // Balanced/Smooth: vor Present warten -> die Bildausgabe kommt im exakten Takt.
    // LowLatency:      nach Present warten -> der naechste Frame startet im Takt,
    //                  Eingaben werden so spaet wie moeglich gelesen.
    const bool waitHere = (mode == Mode::LowLatency) ? !beforePresent : beforePresent;
    if (waitHere) Pace(shared, fps, mode);
}

} // namespace cadence
