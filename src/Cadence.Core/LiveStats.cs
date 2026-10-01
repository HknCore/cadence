namespace Cadence.Core;

/// <summary>Momentaufnahme der Live-Werte fuer das Overlay.</summary>
public sealed record LiveSnapshot(
    double Fps,
    double FrametimeMs,
    double Low1Fps,
    double TargetFps,
    bool Limited,
    LimiterMode Mode,
    float[] Recent);

/// <summary>
/// Gleitendes Fenster der letzten Frametimes (thread-sicher).
/// Wird vom Hintergrund-Thread gefuellt und vom Overlay gelesen.
/// </summary>
public sealed class LiveStats
{
    private const double WindowMs = 2000;
    private const int GraphSamples = 120;

    private readonly Queue<float> _window = new();
    private double _windowSum;
    private readonly object _lock = new();

    public void Clear()
    {
        lock (_lock) { _window.Clear(); _windowSum = 0; }
    }

    public void Add(ReadOnlySpan<float> samples)
    {
        if (samples.Length == 0) return;
        lock (_lock)
        {
            foreach (var f in samples) { _window.Enqueue(f); _windowSum += f; }
            while (_windowSum > WindowMs && _window.Count > 1) _windowSum -= _window.Dequeue();
        }
    }

    public (double Fps, double LastMs, double Low1Fps, float[] Recent) Read()
    {
        float[] all;
        double sum;
        lock (_lock) { all = _window.ToArray(); sum = _windowSum; }
        if (all.Length < 2 || sum <= 0) return (0, 0, 0, []);

        // Aktuelle FPS: Mittel der letzten halben Sekunde – ruhig genug zum Ablesen.
        double recentSum = 0;
        var n = 0;
        for (var i = all.Length - 1; i >= 0 && recentSum < 500; i--) { recentSum += all[i]; n++; }
        var fps = n > 0 && recentSum > 0 ? 1000.0 * n / recentSum : 0;

        var low1 = FrameStats.Compute(all).Low1Fps;
        var recent = all.Length > GraphSamples ? all[^GraphSamples..] : all;
        return (fps, all[^1], low1, recent);
    }
}
