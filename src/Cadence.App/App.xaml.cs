using Cadence.Core;
using Cadence.Core.Tray;
using Microsoft.UI.Xaml;

namespace Cadence.App;

public partial class App : Application
{
    public static ProfileStore Profiles { get; } = new();
    public static SessionController Controller { get; private set; } = null!;
    public static MainWindow MainWindow { get; private set; } = null!;

    /// <summary>
    /// Neue Frametimes des aktiven Spiels, einmal pro gerendertem UI-Bild (UI-Thread).
    /// Zentral abgeholt, damit Aufzeichnung und Graph auf jeder Seite weiterlaufen.
    /// </summary>
    public static event Action<float[]>? FramesArrived;

    internal static void PumpFrames()
    {
        var data = Controller.PollActive();
        FramesArrived?.Invoke(data);
    }

    // Der Installer erkennt am Mutex, dass Cadence laeuft, und bittet um Schliessen.
    private const string MutexName = "CadenceAppMutex";
    private const string ShowEventName = "CadenceShowWindow";
    private static Mutex? _instanceMutex;
    private static EventWaitHandle? _showEvent;

    public App()
    {
        CrashReporter.Install();

        // Laeuft Cadence schon (z. B. im Tray), das vorhandene Fenster zeigen und beenden.
        _instanceMutex = new Mutex(true, MutexName, out var isFirst);
        if (!isFirst)
        {
            if (EventWaitHandle.TryOpenExisting(ShowEventName, out var ev)) ev.Set();
            Environment.Exit(0);
        }
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);

        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            CrashReporter.Log("UI-Fehler", e.Exception);
            if (MainWindow is null) return; // beim Start: OnLaunched meldet den Fehler selbst
            // Im laufenden Betrieb nicht abstuerzen, sondern den Fehler anzeigen.
            e.Handled = true;
            var msg = string.IsNullOrWhiteSpace(e.Exception?.Message) ? e.Message : e.Exception.Message;
            MainWindow.ShowError($"{(string.IsNullOrWhiteSpace(msg) ? e.Exception?.GetType().Name : msg)} – Details: {CrashReporter.LogPath}");
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            Launch();
        }
        catch (Exception ex)
        {
            CrashReporter.FailStartup(ex);
        }
    }

    private void Launch()
    {
        ApplyAccent();
        Profiles.Load();
        Controller = new SessionController(Profiles);

        MainWindow = new MainWindow();
        MainWindow.Closed += (_, _) => Shutdown(exitApp: false);

        var minimized = Environment.GetCommandLineArgs().Contains("--minimized");
        ListenForSecondInstance();
        CreateTray();
        MainWindow.RunSplash(skip: minimized);
        MainWindow.Activate();

        // Autostart: direkt in den Tray, Profile greifen trotzdem.
        if (minimized && MainWindow.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
            presenter.Minimize();
        else
            MainWindow.ApplySavedMaximize();
    }

    private static void ListenForSecondInstance()
    {
        var t = new Thread(() =>
        {
            while (_showEvent!.WaitOne())
            {
                if (_shuttingDown) return;
                MainWindow.DispatcherQueue.TryEnqueue(MainWindow.RestoreFromTray);
            }
        }) { IsBackground = true, Name = "Cadence Single Instance" };
        t.Start();
    }

    // ------------------------------------------------------------ Tray
    public static TrayIcon? Tray { get; private set; }
    private static bool _shuttingDown;

    private const int CmdOpen = 1, CmdLimiter = 2, CmdOverlay = 3, CmdRecord = 4, CmdQuit = 9;
    private const int CmdFpsBase = 100, CmdModeBase = 200;
    private static readonly double[] TrayPresets = [30, 40, 45, 60, 72, 90, 120, 144];

    private void CreateTray()
    {
        Tray = new TrayIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Cadence.ico"), "Cadence", BuildTrayMenu);
        Tray.Activated += (_, _) => MainWindow.DispatcherQueue.TryEnqueue(MainWindow.RestoreFromTray);
        Tray.CommandInvoked += (_, id) => MainWindow.DispatcherQueue.TryEnqueue(() => OnTrayCommand(id));

        Controller.ActiveChanged += (_, _) => UpdateTrayTooltip();
        Controller.SessionStarted += (_, _) => UpdateTrayTooltip();
        Controller.SessionEnded += (_, _) => UpdateTrayTooltip();
        UpdateTrayTooltip();
    }

    private static void UpdateTrayTooltip()
    {
        var s = Controller.Active;
        var text = s is null
            ? "Cadence · kein Spiel aktiv"
            : Controller.Enabled
                ? $"Cadence · {s.DisplayName} · {s.Profile.TargetFps:0} FPS"
                : $"Cadence · {s.DisplayName} · Limiter aus";
        Tray?.SetTooltip(text);
    }

    // Laeuft auf dem Tray-Thread, liest nur Zustand.
    private static IReadOnlyList<TrayMenuItem> BuildTrayMenu()
    {
        var c = Controller;
        var s = c.Active;
        var hasGame = s is not null;
        var target = s?.Profile.TargetFps ?? 0;
        var mode = s?.Profile.Mode ?? LimiterMode.Balanced;

        var fps = TrayPresets
            .Select((v, i) => new TrayMenuItem(CmdFpsBase + i, $"{v:0} FPS", Checked: Math.Abs(v - target) < 0.01))
            .ToList();
        var modes = new[] { LimiterMode.Balanced, LimiterMode.LowLatency, LimiterMode.Smooth }
            .Select(m => new TrayMenuItem(CmdModeBase + (int)m, SessionController.ModeName(m), Checked: m == mode))
            .ToList();

        return
        [
            new TrayMenuItem(CmdOpen, "Cadence öffnen"),
            TrayMenuItem.Separator,
            new TrayMenuItem(0, hasGame ? s!.DisplayName : "Kein Spiel aktiv", Enabled: false),
            new TrayMenuItem(CmdLimiter, "Limiter aktiv", Checked: c.Enabled),
            new TrayMenuItem(0, $"Ziel-FPS{(hasGame ? $" ({target:0})" : "")}", Enabled: hasGame, Children: fps),
            new TrayMenuItem(0, "Modus", Enabled: hasGame, Children: modes),
            new TrayMenuItem(CmdOverlay, "Overlay anzeigen", Checked: c.OverlayEnabled),
            new TrayMenuItem(CmdRecord, c.Recorder.IsRecording ? "Aufnahme stoppen" : "Aufnahme starten", Enabled: hasGame),
            TrayMenuItem.Separator,
            new TrayMenuItem(CmdQuit, "Beenden"),
        ];
    }

    private void OnTrayCommand(int id)
    {
        var c = Controller;
        switch (id)
        {
            case CmdOpen: MainWindow.RestoreFromTray(); break;
            case CmdLimiter: c.Enabled = !c.Enabled; break;
            case CmdOverlay: c.OverlayEnabled = !c.OverlayEnabled; break;
            case CmdRecord: c.ToggleRecording(); break;
            case CmdQuit: Shutdown(exitApp: true); break;
            case >= CmdModeBase and < CmdModeBase + 3: c.SetMode((LimiterMode)(id - CmdModeBase)); break;
            case >= CmdFpsBase and < CmdFpsBase + 20:
                var i = id - CmdFpsBase;
                if (i < TrayPresets.Length) c.SetTargetFps(TrayPresets[i]);
                break;
        }
    }

    /// <summary>
    /// Beendet Cadence sauber: Tray weg, alle Spiele laufen wieder unbegrenzt.
    /// </summary>
    public static void Shutdown(bool exitApp)
    {
        if (_shuttingDown) return;
        _shuttingDown = true;
        try { MainWindow?.SavePlacement(); } catch (Exception ex) { CrashReporter.Log("Fensterposition", ex); }
        Tray?.Dispose();
        Controller.Dispose();
        if (exitApp) Current.Exit();
    }

    /// <summary>
    /// Lila Akzent statt der Windows-Akzentfarbe. Muss vor dem ersten Fenster gesetzt werden,
    /// damit Schalter, Buttons und Auswahl-Markierungen ihn uebernehmen.
    /// Im dunklen Design nutzt WinUI "Light2" als Fuellfarbe.
    /// </summary>
    private void ApplyAccent()
    {
        static Windows.UI.Color C(uint rgb) =>
            Windows.UI.Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

        var colors = new Dictionary<string, Windows.UI.Color>
        {
            ["SystemAccentColor"] = C(0x9D82F2),
            ["SystemAccentColorLight1"] = C(0xAB93F7),
            ["SystemAccentColorLight2"] = C(0xB9A3FF),
            ["SystemAccentColorLight3"] = C(0xD3C5FF),
            ["SystemAccentColorDark1"] = C(0x7F63D9),
            ["SystemAccentColorDark2"] = C(0x6448BE),
            ["SystemAccentColorDark3"] = C(0x4A31A0),
        };
        foreach (var (key, color) in colors) Resources[key] = color;

        // Die wichtigsten Akzent-Pinsel zusaetzlich direkt ueberschreiben.
        var light2 = colors["SystemAccentColorLight2"];
        var light3 = colors["SystemAccentColorLight3"];
        Resources["AccentFillColorDefaultBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(light2);
        Resources["AccentFillColorSecondaryBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(light2) { Opacity = 0.9 };
        Resources["AccentFillColorTertiaryBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(light2) { Opacity = 0.8 };
        Resources["AccentTextFillColorPrimaryBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(light3);
        Resources["AccentTextFillColorSecondaryBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(light3);
        Resources["AccentTextFillColorTertiaryBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(light2);
    }
}
