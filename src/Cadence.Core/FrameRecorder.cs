using System.Globalization;
using System.Text;

namespace Cadence.Core;

/// <summary>Zeichnet Frametimes einer Sitzung auf (Benchmark) und exportiert sie als CSV.</summary>
public sealed class FrameRecorder
{
    private readonly List<float> _samples = new(capacity: 60 * 60 * 10);
    private readonly object _lock = new();
    private DateTimeOffset _startedAt;

    public bool IsRecording { get; private set; }
    public TimeSpan Elapsed => IsRecording ? DateTimeOffset.Now - _startedAt : TimeSpan.Zero;

    public void Start()
    {
        lock (_lock) _samples.Clear();
        _startedAt = DateTimeOffset.Now;
        IsRecording = true;
    }

    public RecordedSession? Stop(string game, double targetFps, LimiterMode mode)
    {
        if (!IsRecording) return null;
        IsRecording = false;
        float[] data;
        lock (_lock) data = _samples.ToArray();
        return new RecordedSession(game, targetFps, mode, _startedAt, DateTimeOffset.Now - _startedAt, data);
    }

    public void Add(ReadOnlySpan<float> frametimes)
    {
        if (!IsRecording || frametimes.Length == 0) return;
        lock (_lock) _samples.AddRange(frametimes.ToArray());
    }
}

public sealed record RecordedSession(
    string Game, double TargetFps, LimiterMode Mode, DateTimeOffset StartedAt, TimeSpan Duration, float[] Frametimes)
{
    public FrameStatsResult Stats { get; } = FrameStats.Compute(Frametimes);

    public string ToCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine("frame,time_ms,frametime_ms");
        double t = 0;
        for (var i = 0; i < Frametimes.Length; i++)
        {
            t += Frametimes[i];
            sb.Append(i).Append(',')
              .Append(t.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
              .AppendLine(Frametimes[i].ToString("F4", CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }
}
