using System.Collections.ObjectModel;
using Cadence.App.ViewModels;
using Cadence.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Cadence.App.Pages;

public sealed class SessionRow(RecordedSession session)
{
    public RecordedSession Session { get; } = session;
    public string Name => $"{Session.Game} · {Session.TargetFps:0}";
    public string Detail => $"{ProfileItem.ModeName(Session.Mode)} · {Session.Duration:m\\:ss} min · {Session.StartedAt:HH:mm}";
    public string Avg => $"{Session.Stats.AverageFps:0.00}";
    public string Low1 => $"{Session.Stats.Low1Fps:0.00}";
    public string Low01 => $"{Session.Stats.Low01Fps:0.00}";
    public string Sd => $"{Session.Stats.StdDevMs:0.00} ms";
    public string Stutters => Session.Stats.Stutters.ToString();
}

public sealed partial class StatsPage : Page
{
    public ObservableCollection<SessionRow> Sessions { get; } = [];

    private const int BinCount = 9;
    private static readonly SolidColorBrush SecondaryText = new(Windows.UI.Color.FromArgb(255, 0xAB, 0xAB, 0xAB));
    private static readonly SolidColorBrush TertiaryText = new(Windows.UI.Color.FromArgb(255, 0x8A, 0x8A, 0x8A));
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private Storyboard? _blink;

    public StatsPage()
    {
        InitializeComponent();
        _clock.Tick += (_, _) => UpdateRecordingUi();
        Loaded += (_, _) =>
        {
            App.Controller.RecordingChanged += OnRecordingChanged;
            ReloadSessions(selectNewest: true);
            UpdateRecordingUi();
        };
        Unloaded += (_, _) =>
        {
            App.Controller.RecordingChanged -= OnRecordingChanged;
            _clock.Stop();
            _blink?.Stop();
        };
    }

    // Kommt auch per Hotkey (Strg + Alt + R) aus dem Spiel – also von einem anderen Thread.
    private void OnRecordingChanged(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            ReloadSessions(selectNewest: !App.Controller.Recorder.IsRecording);
            UpdateRecordingUi();
        });

    private void ReloadSessions(bool selectNewest)
    {
        var recordings = App.Controller.Recordings;
        if (recordings.Count == Sessions.Count) return;
        Sessions.Clear();
        foreach (var r in recordings) Sessions.Add(new SessionRow(r));
        if (selectNewest && Sessions.Count > 0) SessionList.SelectedIndex = 0;
    }

    private void Record_Click(object sender, RoutedEventArgs e)
    {
        var c = App.Controller;
        if (c.Recorder.IsRecording)
        {
            c.StopRecording();
        }
        else if (!c.StartRecording())
        {
            App.MainWindow.ShowError("Es läuft kein Spiel. Starte zuerst ein Spiel mit Cadence.");
        }
    }

    private void UpdateRecordingUi()
    {
        var r = App.Controller.Recorder;
        RecordText.Text = r.IsRecording ? $"Stopp  {r.Elapsed:m\\:ss}" : "Aufnehmen";
        if (r.IsRecording)
        {
            SessionInfo.Text = $"Aufnahme läuft · {App.Controller.Active?.DisplayName} · Strg + Alt + R stoppt auch im Spiel";
            if (!_clock.IsEnabled) { _clock.Start(); StartBlink(); }
        }
        else if (_clock.IsEnabled)
        {
            _clock.Stop();
            _blink?.Stop();
        }
    }

    private void StartBlink()
    {
        var anim = new DoubleAnimation
        {
            From = 1, To = 0.2, Duration = TimeSpan.FromMilliseconds(600),
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        Storyboard.SetTarget(anim, RecDot);
        Storyboard.SetTargetProperty(anim, "Opacity");
        _blink = new Storyboard();
        _blink.Children.Add(anim);
        _blink.Begin();
    }

    private void SessionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var row = SessionList.SelectedItem as SessionRow;
        CsvButton.IsEnabled = ImageButton.IsEnabled = row is not null;
        if (row is null) return;

        var s = row.Session;
        SessionInfo.Text = $"{s.Game} · {s.TargetFps:0} FPS · {ProfileItem.ModeName(s.Mode)} · Aufnahme von {s.Duration:m\\:ss} min";
        AvgText.Text = row.Avg;
        Low1Text.Text = row.Low1;
        Low01Text.Text = row.Low01;
        SdText.Text = row.Sd;
        StutterText.Text = row.Stutters;
        BuildHistogram(s);
    }

    private void BuildHistogram(RecordedSession s)
    {
        Bars.Children.Clear(); Bars.ColumnDefinitions.Clear();
        BarLabels.Children.Clear(); BarLabels.ColumnDefinitions.Clear();

        var center = s.TargetFps > 0 ? 1000.0 / s.TargetFps : 1000.0 / Math.Max(1, s.Stats.AverageFps);
        // Breite so waehlen, dass die typische Streuung sichtbar wird.
        var width = Math.Max(0.02, Math.Round(Math.Max(s.Stats.StdDevMs, 0.02) * 0.75, 2));
        var bins = FrameStats.Histogram(s.Frametimes, center, width, BinCount);
        var maxPct = Math.Max(1, bins.Max(b => b.Percent));
        CenterText.Text = $"Ziel {center:0.00} ms · Balken {width:0.00} ms";

        var board = new Storyboard();
        var accent = (SolidColorBrush)Application.Current.Resources["CadenceAccentBrush"];
        var muted = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x6F, 0x5F, 0xA8));

        for (var i = 0; i < BinCount; i++)
        {
            Bars.ColumnDefinitions.Add(new ColumnDefinition());
            BarLabels.ColumnDefinitions.Add(new ColumnDefinition());

            var col = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Spacing = 6 };
            col.Children.Add(new TextBlock
            {
                Text = $"{bins[i].Percent:0.0}%",
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = SecondaryText,
            });
            var scale = new ScaleTransform { ScaleY = 0 };
            var bar = new Rectangle
            {
                Height = Math.Max(2, bins[i].Percent / maxPct * 200),
                RadiusX = 3, RadiusY = 3,
                Fill = i == BinCount / 2 ? accent : muted,
                RenderTransform = scale,
                RenderTransformOrigin = new Windows.Foundation.Point(0.5, 1),
            };
            col.Children.Add(bar);
            Grid.SetColumn(col, i);
            Bars.Children.Add(col);

            var label = new TextBlock
            {
                Text = $"{(bins[i].From + bins[i].To) / 2:0.00}",
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = TertiaryText,
            };
            Grid.SetColumn(label, i);
            BarLabels.Children.Add(label);

            // Balken wachsen gestaffelt von unten herein.
            var grow = new DoubleAnimation
            {
                From = 0, To = 1,
                Duration = TimeSpan.FromMilliseconds(650),
                BeginTime = TimeSpan.FromMilliseconds(60 * i),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            Storyboard.SetTarget(grow, scale);
            Storyboard.SetTargetProperty(grow, "ScaleY");
            board.Children.Add(grow);
        }
        board.Begin();
    }

    private nint WindowHandle => WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);

    private async void Csv_Click(object sender, RoutedEventArgs e)
    {
        if (SessionList.SelectedItem is not SessionRow row) return;
        var picker = new FileSavePicker { SuggestedFileName = FileName(row.Session) };
        picker.FileTypeChoices.Add("CSV", [".csv"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowHandle);
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        await FileIO.WriteTextAsync(file, row.Session.ToCsv());
    }

    private async void Image_Click(object sender, RoutedEventArgs e)
    {
        if (SessionList.SelectedItem is not SessionRow row) return;
        var picker = new FileSavePicker { SuggestedFileName = FileName(row.Session) };
        picker.FileTypeChoices.Add("PNG-Bild", [".png"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowHandle);
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;

        // Die ganze Seite (Kennzahlen + Verteilung + Tabelle) als Bild speichern.
        var rtb = new RenderTargetBitmap();
        await rtb.RenderAsync(this);
        var pixels = await rtb.GetPixelsAsync();
        var dpi = XamlRoot.RasterizationScale * 96;
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        stream.Size = 0;
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            (uint)rtb.PixelWidth, (uint)rtb.PixelHeight, dpi, dpi, System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.ToArray(pixels));
        await encoder.FlushAsync();
    }

    private static string FileName(RecordedSession s) =>
        $"Cadence {s.Game} {s.TargetFps:0}fps {s.StartedAt:yyyy-MM-dd HHmm}";
}
