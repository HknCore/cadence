using System.Diagnostics;
using Cadence.Core.Interop;

namespace Cadence.Core;

public sealed class GameEventArgs(Process process, GameProfile? profile) : EventArgs
{
    public Process Process { get; } = process;
    public string ExeName { get; } = process.ProcessName + ".exe";
    /// <summary>null = Spiel ohne eigenes Profil.</summary>
    public GameProfile? Profile { get; } = profile;
}

/// <summary>
/// Erkennt laufende Spiele:
///  - Spiele mit Profil (AutoApply) -> GameStarted
///  - unbekannte Vollbild-Anwendung mit Grafik-API im Vordergrund -> GameSuggested
///  - beendete Spiele -> GameExited
/// Events kommen auf einem Hintergrund-Thread.
/// </summary>
public sealed class GameWatcher : IDisposable
{
    private static readonly string[] GraphicsModules = ["dxgi.dll", "d3d11.dll", "d3d12.dll", "opengl32.dll"];

    // Programme, die Grafik-APIs nutzen, aber keine Spiele sind.
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "chrome", "msedge", "firefox", "opera", "brave", "vivaldi", "discord", "steam",
        "steamwebhelper", "epicgameslauncher", "eadesktop", "galaxyclient", "battle.net", "ubisoftconnect",
        "obs64", "obs32", "code", "devenv", "rider64", "spotify", "teams", "ms-teams", "slack", "zoom",
        "photoshop", "blender", "unity", "unrealeditor", "vlc", "mpc-hc64", "applicationframehost",
        "searchhost", "startmenuexperiencehost", "shellexperiencehost", "textinputhost", "dwm",
        "nvcontainer", "snippingtool", "screenclippinghost", "screensketch", "msedgewebview2", "powertoys", "lockapp", "gamebar", "xboxgamebar", "nvidia app", "radeonsoftware", "msiafterburner", "rtss", "cadence",
    };

    private readonly ProfileStore _profiles;
    private readonly Timer _timer;
    private readonly HashSet<int> _tracked = [];
    private readonly HashSet<int> _suggested = [];
    private readonly int _ownPid = Environment.ProcessId;
    private int _busy;

    public event EventHandler<GameEventArgs>? GameStarted;
    public event EventHandler<GameEventArgs>? GameSuggested;
    public event EventHandler<int>? GameExited;

    public GameWatcher(ProfileStore profiles, TimeSpan? interval = null)
    {
        _profiles = profiles;
        var i = interval ?? TimeSpan.FromSeconds(1.5);
        // kurze Verzoegerung, damit alle Event-Handler verbunden sind, bevor der erste Durchlauf startet
        _timer = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(1), i);
    }

    /// <summary>Ein Spiel manuell verfolgen (z. B. nach "Profil anlegen").</summary>
    public void Forget(int pid)
    {
        lock (_tracked) { _tracked.Remove(pid); _suggested.Remove(pid); }
    }

    private void Tick()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            CheckExited();
            CheckProfiled();
            CheckForeground();
        }
        catch
        {
            // Prozesse koennen jederzeit verschwinden – beim naechsten Tick weiter.
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }

    private void CheckExited()
    {
        int[] pids;
        lock (_tracked) pids = _tracked.ToArray();
        foreach (var pid in pids)
        {
            bool gone;
            try { using var p = Process.GetProcessById(pid); gone = p.HasExited; }
            catch (ArgumentException) { gone = true; }
            catch { gone = false; }
            if (!gone) continue;
            lock (_tracked) { _tracked.Remove(pid); _suggested.Remove(pid); }
            GameExited?.Invoke(this, pid);
        }
    }

    private void CheckProfiled()
    {
        var games = _profiles.Games.Where(g => g.AutoApply).ToList();
        if (games.Count == 0) return;
        foreach (var game in games)
        {
            var name = Path.GetFileNameWithoutExtension(game.ExeName);
            foreach (var p in Process.GetProcessesByName(name))
            {
                bool isNew;
                lock (_tracked) isNew = _tracked.Add(p.Id);
                if (isNew) GameStarted?.Invoke(this, new GameEventArgs(p, game));
                else p.Dispose();
            }
        }
    }

    private void CheckForeground()
    {
        var hwnd = Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return;
        Native.GetWindowThreadProcessId(hwnd, out var pidU);
        var pid = (int)pidU;
        if (pid == 0 || pid == _ownPid) return;
        lock (_tracked) if (_tracked.Contains(pid) || _suggested.Contains(pid)) return;

        Process p;
        try { p = Process.GetProcessById(pid); } catch { return; }

        if (Excluded.Contains(p.ProcessName) || !CoversMonitor(hwnd) || !UsesGraphicsApi(p))
        {
            p.Dispose();
            return;
        }

        var profile = _profiles.Find(p.ProcessName + ".exe");
        lock (_tracked)
        {
            _suggested.Add(pid);
            _tracked.Add(pid); // damit auch das Beenden gemeldet wird
        }
        if (profile is { AutoApply: true }) GameStarted?.Invoke(this, new GameEventArgs(p, profile));
        else GameSuggested?.Invoke(this, new GameEventArgs(p, profile));
    }

    /// <summary>Vollbild oder randloses Fenster ueber den ganzen Monitor.</summary>
    private static bool CoversMonitor(IntPtr hwnd)
    {
        if (!Native.GetWindowRect(hwnd, out var r)) return false;
        var mon = Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONEAREST);
        var info = new Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
        if (!Native.GetMonitorInfo(mon, ref info)) return false;
        var m = info.rcMonitor;
        return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
    }

    private static bool UsesGraphicsApi(Process p)
    {
        try
        {
            foreach (ProcessModule m in p.Modules)
                if (GraphicsModules.Contains(m.ModuleName, StringComparer.OrdinalIgnoreCase))
                    return true;
        }
        catch
        {
            // kein Zugriff (z. B. geschuetzter Prozess)
        }
        return false;
    }

    public void Dispose() => _timer.Dispose();
}
