using Cadence.Core;

namespace Cadence.App.ViewModels;

public sealed class OverviewViewModel : ObservableObject
{
    public static readonly double[] Presets = [30, 40, 45, 60, 90, 120, 144];

    private static readonly string[] ModeDescriptions =
    [
        "Gleichmässige Frametimes, funktioniert mit fast jedem Spiel.",
        "Wartet vor der Eingabe-Verarbeitung – weniger Input-Lag.",
        "Maximal flache Frametimes, ideal mit Controller.",
    ];

    private readonly SessionController _c = App.Controller;
    private readonly Queue<float> _window = new();
    private double _windowMs;
    private DateTime _lastStats = DateTime.MinValue;
    private DateTime _lastStatus = DateTime.MinValue;
    private bool _syncing;

    public OverviewViewModel() => Refresh();

    // ---- Spiel ---------------------------------------------------------
    private bool _hasGame;
    public bool HasGame { get => _hasGame; private set { if (SetProperty(ref _hasGame, value)) OnPropertyChanged(nameof(NoGame)); } }
    public bool NoGame => !_hasGame;

    private string _gameName = "";
    public string GameName { get => _gameName; private set => SetProperty(ref _gameName, value); }

    private string _gameDetails = "";
    public string GameDetails { get => _gameDetails; private set => SetProperty(ref _gameDetails, value); }

    private string _status = "";
    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    private bool _statusOk;
    public bool StatusOk { get => _statusOk; private set => SetProperty(ref _statusOk, value); }

    // ---- Steuerung -----------------------------------------------------
    public bool IsEnabled
    {
        get => _c.Enabled;
        set
        {
            if (_c.Enabled == value) return;
            _c.Enabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(EnabledLabel));
        }
    }

    public string EnabledLabel => _c.Enabled ? "Limiter an" : "Limiter aus";

    private double _targetFps = 60;
    public double TargetFps
    {
        get => _targetFps;
        set
        {
            if (double.IsNaN(value)) return;
            value = Math.Round(Math.Clamp(value, 10, 1000));
            if (!SetProperty(ref _targetFps, value)) return;
            OnPropertyChanged(nameof(TargetFrametime));
            SyncPresetIndex();
            if (!_syncing) _c.SetTargetFps(value);
        }
    }

    public string TargetFrametime => $"{1000.0 / _targetFps:0.00} ms";

    private int _presetIndex = -1;
    public int PresetIndex
    {
        get => _presetIndex;
        set
        {
            if (!SetProperty(ref _presetIndex, value) || value < 0 || _syncing) return;
            TargetFps = Presets[value];
        }
    }

    private int _modeIndex;
    public int ModeIndex
    {
        get => _modeIndex;
        set
        {
            if (value < 0 || !SetProperty(ref _modeIndex, value)) return;
            OnPropertyChanged(nameof(ModeDescription));
            if (!_syncing) _c.SetMode((LimiterMode)value);
        }
    }

    public string ModeDescription => ModeDescriptions[Math.Clamp(_modeIndex, 0, 2)];

    // ---- Messwerte -----------------------------------------------------
    private string _frametimeNow = "–";
    public string FrametimeNow { get => _frametimeNow; private set => SetProperty(ref _frametimeNow, value); }

    private string _avgFps = "–", _low1 = "–", _low01 = "–", _deviation = "–";
    public string AvgFps { get => _avgFps; private set => SetProperty(ref _avgFps, value); }
    public string Low1 { get => _low1; private set => SetProperty(ref _low1, value); }
    public string Low01 { get => _low01; private set => SetProperty(ref _low01, value); }
    public string Deviation { get => _deviation; private set => SetProperty(ref _deviation, value); }

    /// <summary>Ziel-Frametime fuer den Graphen, null = unbegrenzt.</summary>
    public double? GraphTargetMs => HasGame && _c.Enabled ? 1000.0 / _targetFps : null;

    /// <summary>Aktive Sitzung neu einlesen (Spielstart, -ende, Hotkey).</summary>
    public void Refresh()
    {
        var s = _c.Active;
        HasGame = s is not null;
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(EnabledLabel));
        if (s is null)
        {
            GameName = "Kein Spiel aktiv";
            GameDetails = "Starte ein Spiel mit Profil oder lege unter „Profile“ eines an.";
            Status = "Wartet auf ein Spiel";
            StatusOk = false;
            ResetStats();
            return;
        }

        _syncing = true;
        TargetFps = s.Profile.TargetFps;
        ModeIndex = (int)s.Profile.Mode;
        _syncing = false;

        GameName = s.DisplayName;
        UpdateStatus(force: true);
    }

    private void SyncPresetIndex()
    {
        var was = _syncing;
        _syncing = true;
        PresetIndex = Array.IndexOf(Presets, _targetFps);
        _syncing = was;
    }

    /// <summary>Neue Frametimes einspeisen; Kennzahlen werden 4x pro Sekunde neu berechnet.</summary>
    public void AddSamples(ReadOnlySpan<float> samples)
    {
        foreach (var f in samples)
        {
            _window.Enqueue(f);
            _windowMs += f;
        }
        while (_windowMs > 3000 && _window.Count > 1) _windowMs -= _window.Dequeue();

        var now = DateTime.UtcNow;
        if (now - _lastStatus > TimeSpan.FromMilliseconds(500)) { _lastStatus = now; UpdateStatus(); }
        if (now - _lastStats < TimeSpan.FromMilliseconds(250)) return;
        _lastStats = now;

        if (samples.Length > 0) FrametimeNow = $"{samples[^1]:0.00}";
        var r = FrameStats.Compute(_window.ToArray());
        if (r.Frames < 2) return;
        AvgFps = $"{r.AverageFps:0.0}";
        Low1 = $"{r.Low1Fps:0.0}";
        Low01 = $"{r.Low01Fps:0.0}";
        Deviation = $"± {r.StdDevMs:0.00} ms";
    }

    private void ResetStats()
    {
        _window.Clear();
        _windowMs = 0;
        FrametimeNow = AvgFps = Low1 = Low01 = Deviation = "–";
    }

    private void UpdateStatus(bool force = false)
    {
        var s = _c.Active;
        if (s is null) return;
        var link = s.Link;
        var api = link.Api switch
        {
            GraphicsApi.Dxgi => "DirectX 10–12",
            GraphicsApi.OpenGL => "OpenGL",
            GraphicsApi.Dxgi | GraphicsApi.OpenGL => "DirectX / OpenGL",
            _ => "Grafik-API wird erkannt …",
        };
        var profile = s.UsesDefaultProfile ? "Standardprofil" : "eigenes Profil";
        var details = $"{s.ExeName} · {api} · {profile}";
        if (force || details != GameDetails) GameDetails = details;

        (Status, StatusOk) = link.HookState switch
        {
            HookState.Active when _c.Enabled => ("Läuft · Limit aktiv", true),
            HookState.Active => ("Läuft · nur Messung", true),
            HookState.Initializing => ("Verbinde …", false),
            HookState.Failed => ($"Fehler: {link.LastError}", false),
            _ => ("Warte auf die Hook-DLL …", false),
        };
    }
}
