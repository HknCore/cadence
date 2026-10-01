using Cadence.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Cadence.App.Pages;

public sealed partial class OverviewPage : Page
{
    public OverviewViewModel Vm { get; } = new();

    private static readonly SolidColorBrush OkBrush = new(Windows.UI.Color.FromArgb(255, 0xB9, 0xA3, 0xFF));
    private static readonly SolidColorBrush IdleBrush = new(Windows.UI.Color.FromArgb(255, 0x8A, 0x8A, 0x8A));

    public OverviewPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public Brush StatusBrush(bool ok) => ok ? OkBrush : IdleBrush;
    public double CardOpacity(bool hasGame) => hasGame ? 1.0 : 0.45;

    private DispatcherTimer? _commitTimer;
    private TextBox? _targetInput;

    /// <summary>
    /// Die NumberBox uebernimmt Eingaben sonst erst mit Enter oder beim Fokuswechsel –
    /// ein Klick auf die freie Flaeche loest das in WinUI nicht aus. Darum kurz nach dem Tippen uebernehmen.
    /// </summary>
    private void HookTargetInput()
    {
        if (_targetInput is not null) return;
        _targetInput = FindChild<TextBox>(TargetBox);
        if (_targetInput is null) return;
        _commitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _commitTimer.Tick += (_, _) =>
        {
            _commitTimer.Stop();
            var text = _targetInput.Text.Trim().Replace(',', '.');
            if (double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)
                && v >= TargetBox.Minimum && v <= TargetBox.Maximum && Math.Round(v) != Math.Round(TargetBox.Value))
                TargetBox.Value = Math.Round(v);
        };
        _targetInput.TextChanged += (_, _) =>
        {
            if (_targetInput.FocusState == FocusState.Unfocused) return; // nur echte Eingaben
            _commitTimer.Stop();
            _commitTimer.Start();
        };
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T hit) return hit;
            if (FindChild<T>(child) is { } deeper) return deeper;
        }
        return null;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        HookTargetInput();
        var c = App.Controller;
        c.SessionStarted += OnSessionChanged;
        c.SessionEnded += OnSessionEnded;
        c.ActiveChanged += OnActiveChanged;
        Vm.Refresh();
        App.FramesArrived += OnFrames;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        var c = App.Controller;
        c.SessionStarted -= OnSessionChanged;
        c.SessionEnded -= OnSessionEnded;
        c.ActiveChanged -= OnActiveChanged;
        App.FramesArrived -= OnFrames;
    }

    private void OnSessionChanged(object? sender, Core.GameSession e) =>
        DispatcherQueue.TryEnqueue(() => { Graph.Clear(); Vm.Refresh(); });

    private void OnSessionEnded(object? sender, int pid) =>
        DispatcherQueue.TryEnqueue(() => { Graph.Clear(); Vm.Refresh(); });

    private void OnActiveChanged(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(Vm.Refresh);

    // Kommt pro gerendertem Bild -> fluessiger Graph.
    private void OnFrames(float[] data)
    {
        Vm.AddSamples(data);
        Graph.Push(data);
        Graph.SetTarget(Vm.GraphTargetMs);
        Graph.Render();
    }
}
