using System.Diagnostics;
using Cadence.App.Pages;
using Cadence.Core;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace Cadence.App;

public sealed partial class MainWindow : Window
{
    private static readonly Dictionary<string, Type> Pages = new()
    {
        ["overview"] = typeof(OverviewPage),
        ["profiles"] = typeof(ProfilesPage),
        ["stats"] = typeof(StatsPage),
        ["overlay"] = typeof(OverlayPage),
        ["settings"] = typeof(SettingsPage),
    };

    private Process? _suggested;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 800));
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Cadence.ico"));

        var c = App.Controller;
        c.GameSuggested += (_, e) => DispatcherQueue.TryEnqueue(() => ShowSuggestion(e));
        c.SessionBlocked += (_, e) => DispatcherQueue.TryEnqueue(() =>
        {
            BlockedBar.Message = $"{e.Game.ExeName}: {e.Reason} Cadence klinkt sich hier nicht ein.";
            BlockedBar.IsOpen = true;
        });
        c.AttachFailed += (_, msg) => DispatcherQueue.TryEnqueue(() => ShowError(msg));
        c.SessionStarted += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            SuggestBar.IsOpen = false;
            BlockedBar.IsOpen = false;
        });

        if (ContentFrame.Content is null) ContentFrame.Navigate(typeof(OverviewPage));
        CompositionTarget.Rendering += (_, _) => App.PumpFrames();

        RestorePlacement();

        AppWindow.Changed += (_, args) =>
        {
            if (AppWindow.Presenter is not OverlappedPresenter p) return;
            // Minimieren -> ab in den Infobereich (Tray)
            if (AppWindow.IsVisible && p.State == OverlappedPresenterState.Minimized)
            {
                AppWindow.Hide();
                return;
            }
            // Letzte "normale" Groesse und Position merken (nicht die maximierte).
            if (!AppWindow.IsVisible) return;
            if (p.State == OverlappedPresenterState.Restored && (args.DidPositionChange || args.DidSizeChange))
                _normalBounds = new RectInt32(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
            if (args.DidPresenterChange || args.DidSizeChange)
                _maximized = p.State == OverlappedPresenterState.Maximized;
        };
    }

    // ------------------------------------------------------------ Fensterposition
    private RectInt32? _normalBounds;
    private bool _maximized;

    /// <summary>Groesse und Position vom letzten Mal – nur, wenn sie noch auf einem Bildschirm liegen.</summary>
    private void RestorePlacement()
    {
        var w = App.Profiles.Settings.Window;
        if (w is null || w.Width < 600 || w.Height < 400) return;
        var rect = new RectInt32(w.X, w.Y, w.Width, w.Height);
        // Mindestens die Titelleiste muss sichtbar sein (z. B. nach Abstecken eines zweiten Monitors).
        var titleArea = new RectInt32(w.X + 40, w.Y, Math.Max(1, w.Width - 80), 40);
        if (DisplayArea.GetFromRect(titleArea, DisplayAreaFallback.None) is null) return;
        AppWindow.MoveAndResize(rect);
        _normalBounds = rect;
        _maximized = w.Maximized;
    }

    /// <summary>Nach dem ersten Anzeigen aufrufen: stellt den maximierten Zustand wieder her.</summary>
    public void ApplySavedMaximize()
    {
        if (_maximized && AppWindow.Presenter is OverlappedPresenter p) p.Maximize();
    }

    /// <summary>Beim Beenden aufrufen.</summary>
    public void SavePlacement()
    {
        if (_normalBounds is not { } b) return;
        App.Profiles.Settings.Window = new WindowPlacement
        {
            X = b.X, Y = b.Y, Width = b.Width, Height = b.Height, Maximized = _maximized,
        };
        App.Profiles.Save();
    }

    /// <summary>true, wenn der Benutzer Cadence gerade vor sich hat – dann braucht es keine Benachrichtigung.</summary>
    public bool IsInForeground =>
        AppWindow.IsVisible &&
        AppWindow.Presenter is OverlappedPresenter { State: not OverlappedPresenterState.Minimized } &&
        NativeMethods.GetForegroundWindow() == WinRT.Interop.WindowNative.GetWindowHandle(this);

    /// <summary>Fenster aus dem Tray zurueckholen.</summary>
    public void RestoreFromTray()
    {
        AppWindow.Show();
        if (AppWindow.Presenter is OverlappedPresenter p && p.State == OverlappedPresenterState.Minimized)
            p.Restore();
        Activate();
    }

    // ------------------------------------------------------------ Splash
    private Storyboard? _splashLoop;
    private DispatcherQueueTimer? _splashTimer;
    private bool _splashDone;

    private static readonly (int AtMs, string Text)[] SplashSteps =
    [
        (0, "Lade Profile …"),
        (550, "Suche laufende Spiele …"),
        (1050, "Kalibriere Timer …"),
        (1600, "Prüfe Anti-Cheat-Liste …"),
        (2050, "Bereit"),
    ];
    private const int SplashDurationMs = 2400;

    /// <summary>Startet den Splash-Screen (oder blendet ihn sofort aus).</summary>
    public void RunSplash(bool skip)
    {
        if (skip)
        {
            _splashDone = true;
            Splash.Visibility = Visibility.Collapsed;
            return;
        }
        Nav.Opacity = 0;
        SplashVersion.Text = "Version " + AppVersion;

        // Logo: drei Balken pulsieren versetzt – wie ein gleichmaessiger Takt.
        _splashLoop = new Storyboard();
        ScaleTransform[] bars = [Bar1Scale, Bar2Scale, Bar3Scale];
        for (var i = 0; i < bars.Length; i++)
            Add(_splashLoop, bars[i], "ScaleY", new DoubleAnimation
            {
                From = 0.3, To = 1, Duration = Ms(420), AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever, BeginTime = Ms(140 * i),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            });

        // Titel blendet von unten ein
        var outCubic = new CubicEase { EasingMode = EasingMode.EaseOut };
        Add(_splashLoop, SplashTitle, "Opacity", new DoubleAnimation { From = 0, To = 1, Duration = Ms(600), BeginTime = Ms(200) });
        Add(_splashLoop, TitleShift, "Y", new DoubleAnimation { From = 10, To = 0, Duration = Ms(700), BeginTime = Ms(200), EasingFunction = outCubic });

        _splashLoop.Begin();

        var clock = Stopwatch.StartNew();
        var step = -1;
        _splashTimer = DispatcherQueue.CreateTimer();
        _splashTimer.Interval = Ms(16);
        _splashTimer.Tick += (_, _) =>
        {
            var t = clock.ElapsedMilliseconds;
            ProgressScale.ScaleX = FakeProgress(t);
            var next = Array.FindLastIndex(SplashSteps, s => s.AtMs <= t);
            if (next != step) { step = next; SplashStatus.Text = SplashSteps[next].Text; }
            if (t >= SplashDurationMs) HideSplash();
        };
        _splashTimer.Start();
    }

    // "Fake"-Ladebalken: ruckelt wie ein echter in Etappen nach vorn.
    private static readonly (int From, int To, double Value)[] ProgressStages =
        [(250, 700, 0.30), (700, 1100, 0.44), (1100, 1700, 0.81), (1700, 2100, 0.93), (2100, 2250, 1.0)];

    private static double FakeProgress(long t)
    {
        var last = 0.0;
        foreach (var (from, to, value) in ProgressStages)
        {
            if (t < from) return last;
            if (t < to)
            {
                var k = (t - from) / (double)(to - from);
                var eased = 1 - Math.Pow(1 - k, 3); // ease-out
                return last + (value - last) * eased;
            }
            last = value;
        }
        return 1.0;
    }

    private void Splash_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e) => HideSplash();

    private void HideSplash()
    {
        if (_splashDone) return;
        _splashDone = true;
        _splashTimer?.Stop();

        var fade = new Storyboard();
        Add(fade, Splash, "Opacity", new DoubleAnimation
        {
            To = 0, Duration = Ms(380), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        });
        Add(fade, Nav, "Opacity", new DoubleAnimation { From = 0, To = 1, Duration = Ms(450), BeginTime = Ms(120) });
        fade.Completed += (_, _) =>
        {
            Splash.Visibility = Visibility.Collapsed;
            _splashLoop?.Stop();
            Nav.Opacity = 1;
        };
        fade.Begin();
    }

    public static string AppVersion =>
        typeof(MainWindow).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "";

    private static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);

    private static void Add(Storyboard board, DependencyObject target, string property, Timeline anim)
    {
        Storyboard.SetTarget(anim, target);
        Storyboard.SetTargetProperty(anim, property);
        board.Children.Add(anim);
    }

    public void ShowError(string message)
    {
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }

    private void ShowSuggestion(GameEventArgs e)
    {
        _suggested = e.Process;
        SuggestBar.Title = "Neues Spiel erkannt";
        SuggestBar.Message = $"{e.Process.ProcessName} läuft im Vollbild. Soll Cadence es begrenzen?";
        SuggestBar.IsOpen = true;
    }

    private void SuggestBar_Create(object sender, RoutedEventArgs e)
    {
        SuggestBar.IsOpen = false;
        if (_suggested is null || _suggested.HasExited) return;

        var c = App.Controller;
        var result = c.Attach(_suggested, null);
        if (result.Outcome == AttachOutcome.Attached)
            c.CreateProfileForActive();
        Navigate("overview");
    }

    public void Navigate(string tag)
    {
        foreach (var item in Nav.MenuItems.OfType<NavigationViewItem>())
            if ((string)item.Tag == tag) { Nav.SelectedItem = item; return; }
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = args.IsSettingsSelected ? "settings" : (args.SelectedItem as NavigationViewItem)?.Tag as string;
        if (tag is null || !Pages.TryGetValue(tag, out var page)) return;
        if (ContentFrame.CurrentSourcePageType == page) return;

        // Seite gleitet sanft von unten herein – wie in den Windows-Einstellungen.
        ContentFrame.Navigate(page, null, new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromBottom });
    }
}
