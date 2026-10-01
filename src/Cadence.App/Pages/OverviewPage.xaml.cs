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

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
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
