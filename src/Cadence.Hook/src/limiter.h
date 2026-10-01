// limiter.h – hochpraeziser Frame-Limiter
#pragma once
#include <windows.h>
#include <cstdint>
#include "cadence_shared.h"

namespace cadence {

class Limiter {
public:
    bool Init();
    void Shutdown();

    // Wird von jedem Present-Hook aufgerufen.
    //  beforePresent = true  -> Aufruf vor dem Original-Present
    //  beforePresent = false -> Aufruf nach dem Original-Present
    // Der Limiter entscheidet je nach Modus selbst, an welcher Stelle er wartet.
    void OnPresent(SharedBlock* shared, bool beforePresent);

private:
    int64_t Now() const;
    void    WaitUntil(int64_t deadline, double spinUs);
    void    Pace(SharedBlock* shared, double fps, Mode mode);
    void    Record(SharedBlock* shared, int64_t stamp);

    LARGE_INTEGER freq_{};
    HANDLE  timer_ = nullptr;
    bool    highRes_ = false;
    int64_t next_ = 0;        // naechster geplanter Zeitpunkt (QPC-Ticks)
    int64_t lastStamp_ = 0;   // letzter Frame-Zeitstempel fuer die Statistik
    double  lastFps_ = 0.0;
    Mode    lastMode_ = Mode::Balanced;
};

Limiter& GlobalLimiter();

} // namespace cadence
