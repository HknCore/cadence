using System.Collections.Concurrent;
using System.Diagnostics;
using Cadence.Core.Overlay;

namespace Cadence.Core;

public sealed class GameSession
{
    internal GameSession(Process process, GameProfile profile, SharedLink link)
    {
        Process = process;
        Profile = profile;
        Link = link;
        ExeName = process.ProcessName + ".exe";
        Cursor = link.WriteIndex;
    }

    public Process Process { get; }
    public int ProcessId => Process.Id;
    public string ExeName { get; }
    public string DisplayName => Profile.IsDefault ? Process.ProcessName : Profile.DisplayName;
    /// <summary>Aktives Profil (Standardprofil, wenn das Spiel kein eigenes hat).</summary>
    public GameProfile Profile { get; internal set; }
    public bool UsesDefaultProfile => Profile.IsDefault;
    public SharedLink Link { get; }
    internal uint Cursor;
}

public enum AttachOutcome { Attached, AlreadyAttached, Blocked, Failed }

public sealed record AttachResult(AttachOutcome Outcome, string Message);

/// <summary>
/// Zentrale Steuerung: verbindet Spielerkennung, Anti-Cheat-Schutz, Injektion,
/// Profile, Hotkeys, Aufzeichnung und Overlay.
/// </summary>
public sealed class SessionController : IDisposable
{
    public static readonly double[] FpsSteps = [24, 30, 40, 45, 48, 50, 60, 72, 75, 90, 100, 120, 144, 165, 180, 240];

    private readonly Dictionary<int, GameSession> _sessions = [];
    private readonly object _lock = new();
    private readonly GameWatcher _watcher;
    private readonly HotkeyService _hotkeys;
    private readonly OverlayWindow _overlay;
    private readonly Timer _poll;
    private readonly ConcurrentQueue<float[]> _uiQueue = new();
    private readonly List<RecordedSession> _recordings = [];
    private bool _enabled = true;
    private int _polling;
    private bool _disposed;

    private string? _toast;
    private long _toastUntil;

    public ProfileStore Profiles { get; }
    public FrameRecorder Recorder { get; } = new();
    public LiveStats Live { get; } = new();

    public event EventHandler<GameSession>? SessionStarted;
    public event EventHandler<int>? SessionEnded;
    public event EventHandler<(GameEventArgs Game, string Reason)>? SessionBlocked;
    public event EventHandler<GameEventArgs>? GameSuggested;
    public event EventHandler<string>? AttachFailed;
    /// <summary>Ziel, Modus, Hauptschalter oder Overlay haben sich geaendert (z. B. per Hotkey oder Tray).</summary>
    public event EventHandler? ActiveChanged;
    /// <summary>Aufnahme gestartet/gestoppt oder neue Aufnahme gespeichert.</summary>
    public event EventHandler? RecordingChanged;

    public SessionController(ProfileStore profiles)
    {
        Profiles = profiles;
        _watcher = new GameWatcher(profiles);
        _watcher.GameStarted += (_, e) => Attach(e.Process, e.Profile);
        _watcher.GameSuggested += (_, e) => GameSuggested?.Invoke(this, e);
        _watcher.GameExited += (_, pid) => OnExited(pid);

        _hotkeys = new HotkeyService(profiles.Settings.Hotkeys);
        _hotkeys.Pressed += (_, a) => OnHotkey(a);

        _overlay = new OverlayWindow(GetOverlayFrame);

        // Frametimes im Hintergrund abholen – unabhaengig davon, ob das Fenster sichtbar ist.
        _poll = new Timer(_ => Poll(), null, TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(20));
    }

    public GameSession? Active { get; private set; }

    public bool HotkeysAvailable => _hotkeys.Registered;

    /// <summary>Kürzel, die ein anderes Programm belegt (nach dem letzten Speichern).</summary>
    public IReadOnlyCollection<HotkeyAction> HotkeyConflicts => _hotkeys.Failed;

    /// <summary>Lesbarer Text des Kürzels, z. B. "Strg + Alt + R".</summary>
    public string HotkeyText(HotkeyAction action) => Profiles.Settings.Hotkey(action).ToString();

    /// <summary>Kürzel vorübergehend abschalten, z. B. während in den Einstellungen ein neues aufgenommen wird.</summary>
    public void SuspendHotkeys() => _hotkeys.Update([]);

    public void ResumeHotkeys() => _hotkeys.Update(Profiles.Settings.Hotkeys);

    /// <summary>Neue Belegung speichern und sofort aktivieren.</summary>
    public void ApplyHotkeys(IEnumerable<HotkeyBinding> bindings)
    {
        var list = bindings.Select(b => b.Clone()).ToList();
        Profiles.Settings.Hotkeys = list;
        Profiles.Settings.Normalize();
        Profiles.Save();
        _hotkeys.Update(Profiles.Settings.Hotkeys);
        ActiveChanged?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<RecordedSession> Recordings { get { lock (_recordings) return _recordings.ToList(); } }

    /// <summary>Hauptschalter: aus = alle Spiele laufen unbegrenzt (nur Messung).</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            lock (_lock)
                foreach (var s in _sessions.Values) s.Link.Enabled = value;
            ShowToast(value ? "Limiter an" : "Limiter aus – nur Messung");
            ActiveChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // ------------------------------------------------------------ Verbinden
    public AttachResult Attach(Process process, GameProfile? profile)
    {
        lock (_lock)
            if (_sessions.ContainsKey(process.Id))
                return new(AttachOutcome.AlreadyAttached, "Bereits verbunden.");

        var game = new GameEventArgs(process, profile);
        var guard = AntiCheatGuard.Check(process);
        if (guard.Blocked)
        {
            SessionBlocked?.Invoke(this, (game, guard.Reason));
            return new(AttachOutcome.Blocked, guard.Reason);
        }

        var effective = profile ?? Profiles.Default;
        if (effective.ApplyRefreshRate()) Profiles.Upsert(effective);
        SharedLink? link = null;
        try
        {
            link = SharedLink.Create(process.Id, effective, _enabled);
            if (!Injector.IsAlreadyLoaded(process))
                Injector.Inject(process);

            var session = new GameSession(process, effective, link);
            lock (_lock)
            {
                _sessions[process.Id] = session;
                Active = session;
            }
            Live.Clear();

            if (profile is not null)
            {
                profile.LastPlayed = DateTimeOffset.Now;
                Profiles.Upsert(profile);
            }

            ShowToast($"Profil geladen: {session.DisplayName} · {effective.TargetFps:0} FPS");
            SessionStarted?.Invoke(this, session);
            return new(AttachOutcome.Attached, "Verbunden.");
        }
        catch (Exception ex)
        {
            link?.Dispose();
            AttachFailed?.Invoke(this, ex.Message);
            return new(AttachOutcome.Failed, ex.Message);
        }
    }

    // ------------------------------------------------------------ Steuerung
    public void SetTargetFps(double fps)
    {
        var s = Active;
        if (s is null) return;
        if (s.UsesDefaultProfile) CreateProfileForActive(); // Aenderungen gelten pro Spiel
        fps = Math.Clamp(fps, 10, 1000);
        s.Profile.MatchRefreshRate = false;
        s.Profile.TargetFps = fps;
        s.Link.TargetFps = fps;
        Profiles.Upsert(s.Profile);
        ShowToast($"Ziel: {fps:0} FPS");
        ActiveChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetMode(LimiterMode mode)
    {
        var s = Active;
        if (s is null) return;
        if (s.UsesDefaultProfile) CreateProfileForActive();
        s.Profile.Mode = mode;
        s.Link.Mode = mode;
        Profiles.Upsert(s.Profile);
        ShowToast("Modus: " + ModeName(mode));
        ActiveChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool OverlayEnabled
    {
        get => Profiles.Overlay.Enabled;
        set
        {
            if (Profiles.Overlay.Enabled == value) return;
            Profiles.Overlay.Enabled = value;
            Profiles.Save();
            ActiveChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public static string ModeName(LimiterMode m) => m switch
    {
        LimiterMode.LowLatency => "Niedrige Latenz",
        LimiterMode.Smooth => "Max. Glätte",
        _ => "Ausgewogen",
    };

    /// <summary>Erstellt aus dem aktiven Spiel ein eigenes Profil (statt Standardprofil).</summary>
    public GameProfile? CreateProfileForActive()
    {
        var s = Active;
        if (s is null) return null;
        if (!s.UsesDefaultProfile) return s.Profile;
        var p = s.Profile.Clone();
        p.ExeName = s.ExeName;
        p.DisplayName = s.Process.MainWindowTitle is { Length: > 0 } t ? t : s.Process.ProcessName;
        p.LastPlayed = DateTimeOffset.Now;
        s.Profile = p;
        Profiles.Upsert(p);
        ActiveChanged?.Invoke(this, EventArgs.Empty);
        return p;
    }

    /// <summary>Ein Profil wurde in der Profilverwaltung geaendert: laufende Spiele sofort anpassen.</summary>
    public void ProfileEdited(GameProfile profile)
    {
        Profiles.Upsert(profile);
        lock (_lock)
        {
            foreach (var s in _sessions.Values)
            {
                var matches = profile.IsDefault
                    ? s.UsesDefaultProfile
                    : string.Equals(s.ExeName, profile.ExeName, StringComparison.OrdinalIgnoreCase);
                if (!matches) continue;
                s.Profile = profile;
                s.Link.Apply(profile, _enabled);
            }
        }
        ActiveChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnHotkey(HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.TargetUp: StepTarget(+1); break;
            case HotkeyAction.TargetDown: StepTarget(-1); break;
            case HotkeyAction.ToggleRecording: ToggleRecording(); break;
            case HotkeyAction.ToggleOverlay:
                OverlayEnabled = !OverlayEnabled;
                break;
        }
    }

    private void StepTarget(int direction)
    {
        var s = Active;
        if (s is null || !s.Profile.HotkeysEnabled) return;
        var cur = s.Profile.TargetFps;
        var next = direction > 0
            ? FpsSteps.FirstOrDefault(v => v > cur + 0.01, FpsSteps[^1])
            : FpsSteps.LastOrDefault(v => v < cur - 0.01, FpsSteps[0]);
        SetTargetFps(next);
    }

    // ------------------------------------------------------------ Aufnahme
    public bool StartRecording()
    {
        if (Active is null || Recorder.IsRecording) return false;
        Recorder.Start();
        var key = Profiles.Settings.Hotkey(HotkeyAction.ToggleRecording);
        ShowToast(key.IsEmpty ? "Aufnahme läuft" : $"Aufnahme läuft ({key} stoppt)");
        RecordingChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public RecordedSession? StopRecording()
    {
        if (!Recorder.IsRecording) return null;
        var s = Active;
        var rec = Recorder.Stop(s?.DisplayName ?? "Unbekannt", s?.Profile.TargetFps ?? 0, s?.Profile.Mode ?? LimiterMode.Balanced);
        if (rec is { Frametimes.Length: > 1 })
        {
            lock (_recordings) _recordings.Insert(0, rec);
            ShowToast($"Aufnahme gespeichert · Ø {rec.Stats.AverageFps:0.0} FPS · 1 % Low {rec.Stats.Low1Fps:0.0}");
        }
        else
        {
            rec = null;
            ShowToast("Aufnahme verworfen – keine Frames");
        }
        RecordingChanged?.Invoke(this, EventArgs.Empty);
        return rec;
    }

    public void ToggleRecording()
    {
        if (Recorder.IsRecording) StopRecording();
        else StartRecording();
    }

    // ------------------------------------------------------------ Daten
    private void Poll()
    {
        if (Interlocked.Exchange(ref _polling, 1) == 1) return;
        try
        {
            GameSession[] all;
            lock (_lock) all = _sessions.Values.ToArray();
            foreach (var s in all)
            {
                try { s.Link.Heartbeat(); } catch (ObjectDisposedException) { }
            }

            var active = Active;
            if (active is null) return;
            float[] data;
            try { data = active.Link.ReadSince(ref active.Cursor); }
            catch (ObjectDisposedException) { return; }
            if (data.Length == 0) return;

            Recorder.Add(data);
            Live.Add(data);
            // Fuer die Oberflaeche puffern; wird das Fenster laengere Zeit nicht gezeichnet, Altes verwerfen.
            _uiQueue.Enqueue(data);
            while (_uiQueue.Count > 100 && _uiQueue.TryDequeue(out _)) { }
        }
        finally
        {
            Volatile.Write(ref _polling, 0);
        }
    }

    /// <summary>Alle seit dem letzten Aufruf eingegangenen Frametimes (fuer Graph und Anzeige).</summary>
    public float[] PollActive()
    {
        if (_uiQueue.IsEmpty) return [];
        var parts = new List<float[]>();
        while (_uiQueue.TryDequeue(out var p)) parts.Add(p);
        return parts.Count == 1 ? parts[0] : parts.SelectMany(p => p).ToArray();
    }

    // ------------------------------------------------------------ Overlay
    public void ShowToast(string text, double seconds = 3)
    {
        _toast = text;
        Interlocked.Exchange(ref _toastUntil, Environment.TickCount64 + (long)(seconds * 1000));
    }

    private OverlayFrame? GetOverlayFrame()
    {
        var s = Active;
        var settings = Profiles.Overlay;
        if (s is null || !settings.Enabled || !s.Profile.OverlayEnabled) return null;
        if (s.Link.HookState != HookState.Active) return null;

        var (fps, lastMs, low1, recent) = Live.Read();
        var data = new LiveSnapshot(fps, lastMs, low1, s.Profile.TargetFps, _enabled, s.Profile.Mode, recent);
        var toast = Environment.TickCount64 < Interlocked.Read(ref _toastUntil) ? _toast : null;
        if (Recorder.IsRecording && toast is null) toast = $"● Aufnahme {Recorder.Elapsed:m\\:ss}";
        return new OverlayFrame(s.ProcessId, settings.Variant, settings.Corner, data, toast);
    }

    // ------------------------------------------------------------ Ende
    private void OnExited(int pid)
    {
        GameSession? s;
        lock (_lock)
        {
            if (!_sessions.Remove(pid, out s)) return;
            if (Active == s) Active = _sessions.Values.LastOrDefault();
        }
        if (Recorder.IsRecording && Active is null) StopRecording();
        Live.Clear();
        s.Link.Dispose();
        s.Process.Dispose();
        SessionEnded?.Invoke(this, pid);
    }

    /// <summary>
    /// Beendet Cadence: Alle Spiele laufen danach wieder unbegrenzt.
    /// (Die DLL bleibt bis zum Spielende geladen, reicht aber nur noch durch.)
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _poll.Dispose();
        _watcher.Dispose();
        _hotkeys.Dispose();
        _overlay.Dispose();
        lock (_lock)
        {
            foreach (var s in _sessions.Values)
            {
                try { s.Link.Enabled = false; } catch (ObjectDisposedException) { }
                s.Link.Dispose();
                s.Process.Dispose();
            }
            _sessions.Clear();
            Active = null;
        }
    }
}
