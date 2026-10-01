namespace Cadence.Core;

public readonly record struct FrameStatsResult(
    int Frames,
    double AverageFps,
    double Low1Fps,
    double Low01Fps,
    double StdDevMs,
    int Stutters)
{
    public static readonly FrameStatsResult Empty = new(0, 0, 0, 0, 0, 0);
}

public static class FrameStats
{
    /// <summary>
    /// Berechnet die Kennzahlen aus Frametimes in Millisekunden.
    /// 1 % / 0,1 % Low = FPS beim 99. bzw. 99,9. Perzentil der Frametime.
    /// Stotterer = Frames, die laenger als das Doppelte des Mittelwerts dauern.
    /// </summary>
    public static FrameStatsResult Compute(ReadOnlySpan<float> frametimesMs)
    {
        var n = frametimesMs.Length;
        if (n < 2) return FrameStatsResult.Empty;

        double sum = 0;
        foreach (var f in frametimesMs) sum += f;
        var mean = sum / n;
        if (mean <= 0) return FrameStatsResult.Empty;

        double varSum = 0;
        var stutters = 0;
        foreach (var f in frametimesMs)
        {
            var d = f - mean;
            varSum += d * d;
            if (f > 2 * mean) stutters++;
        }

        var sorted = frametimesMs.ToArray();
        Array.Sort(sorted);
        double Percentile(double p) => sorted[Math.Clamp((int)Math.Ceiling(p * n) - 1, 0, n - 1)];

        return new FrameStatsResult(
            n,
            1000.0 / mean,
            1000.0 / Percentile(0.99),
            1000.0 / Percentile(0.999),
            Math.Sqrt(varSum / n),
            stutters);
    }

    /// <summary>Verteilung der Frametimes um einen Mittelpunkt (fuer das Histogramm).</summary>
    public static (double From, double To, double Percent)[] Histogram(
        ReadOnlySpan<float> frametimesMs, double center, double binWidth, int bins)
    {
        var result = new (double, double, double)[bins];
        if (frametimesMs.Length == 0) return result;
        var start = center - binWidth * bins / 2.0;
        var counts = new int[bins];
        foreach (var f in frametimesMs)
        {
            var i = (int)Math.Floor((f - start) / binWidth);
            counts[Math.Clamp(i, 0, bins - 1)]++; // Ausreisser landen im ersten/letzten Balken
        }
        for (var i = 0; i < bins; i++)
            result[i] = (start + i * binWidth, start + (i + 1) * binWidth, 100.0 * counts[i] / frametimesMs.Length);
        return result;
    }
}
