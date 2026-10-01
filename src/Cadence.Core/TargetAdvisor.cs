namespace Cadence.Core;

/// <summary>
/// Erkennt, wenn das Ziel über dem liegt, was das Spiel dauerhaft schafft.
/// Dann bremst der Limiter nur die schnellen Bilder, die langsamen bleiben langsam –
/// die Bildzeiten springen hin und her und werden unruhiger statt ruhiger.
/// </summary>
public sealed class TargetAdvisor
{
    private const double WindowMs = 8000;        // so viel Verlauf wird bewertet
    private const double MinWindowMs = 6000;     // vorher keine Aussage
    private const double MissTolerance = 1.06;   // Bilder bis 6 % über dem Ziel gelten noch als getroffen
    private const double MissShare = 0.25;       // ab 25 % verfehlten Bildern ist das Ziel zu hoch
    private const int StreakNeeded = 5;          // 5 Prüfungen in Folge (≈ 5 s), damit kurze Einbrüche nicht zählen

    private readonly Queue<float> _window = new();
    private double _windowSum;
    private int _sessionKey = -1;
    private double _target;
    private bool _limiting;
    private int _streak;
    private long _nextCheck;
    private double? _dismissedFor;

    /// <summary>Empfohlenes Ziel; null = alles in Ordnung.</summary>
    public double? Recommendation { get; private set; }

    /// <summary>Was das Spiel zuletzt im Schnitt geschafft hat (für den Hinweistext).</summary>
    public double AchievedFps { get; private set; }

    /// <summary>
    /// Neue Frametimes einspeisen. Liefert true, wenn sich die Empfehlung geändert hat.
    /// </summary>
    public bool Feed(ReadOnlySpan<float> samples, int sessionKey, double targetFps, bool limiting)
    {
        var before = Recommendation;

        // Neues Spiel oder neues Ziel: alte Messwerte gehören nicht mehr dazu.
        if (sessionKey != _sessionKey || Math.Abs(targetFps - _target) > 0.01 || limiting != _limiting)
        {
            if (sessionKey != _sessionKey) _dismissedFor = null;
            _sessionKey = sessionKey;
            _target = targetFps;
            _limiting = limiting;
            Reset();
        }

        foreach (var f in samples)
        {
            _window.Enqueue(f);
            _windowSum += f;
        }
        while (_windowSum > WindowMs && _window.Count > 1) _windowSum -= _window.Dequeue();

        var now = Environment.TickCount64;
        if (now >= _nextCheck)
        {
            _nextCheck = now + 1000;
            Evaluate();
        }
        return before != Recommendation;
    }

    /// <summary>Hinweis für dieses Ziel nicht mehr zeigen (bis zum nächsten Spielstart).</summary>
    public void Dismiss()
    {
        _dismissedFor = _target;
        Recommendation = null;
    }

    private void Reset()
    {
        _window.Clear();
        _windowSum = 0;
        _streak = 0;
        Recommendation = null;
    }

    private void Evaluate()
    {
        if (!_limiting || _sessionKey < 0 || _target <= 0 || _windowSum < MinWindowMs) { _streak = 0; return; }

        var frames = _window.ToArray();
        var achieved = 1000.0 * frames.Length / _windowSum;

        // Ladebildschirm, Menü im Hintergrund, Alt+Tab: kein Urteil fällen.
        if (achieved < _target * 0.35) return;

        var targetMs = 1000.0 / _target;
        var missed = frames.Count(f => f > targetMs * MissTolerance);
        if ((double)missed / frames.Length < MissShare)
        {
            _streak = 0;
            Recommendation = null;
            return;
        }

        if (++_streak < StreakNeeded || Recommendation is not null) return;
        if (_dismissedFor is { } d && Math.Abs(d - _target) < 0.01) return;

        // Ein Ziel, das das Spiel in 95 % der Bilder schafft – auf eine runde Zahl abgerundet.
        Array.Sort(frames);
        var p95 = frames[Math.Clamp((int)Math.Ceiling(0.95 * frames.Length) - 1, 0, frames.Length - 1)];
        var sustainable = 1000.0 / p95;
        var step = sustainable >= 100 ? 10 : 5;
        var rec = Math.Floor(sustainable / step) * step;

        AchievedFps = achieved;
        if (rec >= 20 && rec < _target - 1) Recommendation = rec;
    }
}
