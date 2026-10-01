using Cadence.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Cadence.App.Pages;

public sealed partial class OverlayPage : Page
{
    private static readonly string[] VariantHints =
    [
        "Nur die FPS als kleine Pille.",
        "FPS, Frametime und Mini-Graph.",
        "Zusätzlich Ziel, 1 % Low und Modus.",
    ];

    private bool _loading;

    public OverlayPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            App.Controller.ActiveChanged += OnChanged; // Hotkey Strg + Alt + O
            Load();
        };
        Unloaded += (_, _) => App.Controller.ActiveChanged -= OnChanged;
    }

    private void OnChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(Load);

    private void Load()
    {
        _loading = true;
        var o = App.Profiles.Overlay;
        EnabledSwitch.IsOn = o.Enabled;
        VariantBox.SelectedIndex = (int)o.Variant;
        CornerBox.SelectedIndex = (int)o.Corner;
        VariantHint.Text = VariantHints[(int)o.Variant];
        _loading = false;
    }

    private void Enabled_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_loading) App.Controller.OverlayEnabled = EnabledSwitch.IsOn;
    }

    private void Variant_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || VariantBox.SelectedIndex < 0) return;
        App.Profiles.Overlay.Variant = (OverlayVariant)VariantBox.SelectedIndex;
        App.Profiles.Save();
        VariantHint.Text = VariantHints[VariantBox.SelectedIndex];
    }

    private void Corner_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || CornerBox.SelectedIndex < 0) return;
        App.Profiles.Overlay.Corner = (OverlayCorner)CornerBox.SelectedIndex;
        App.Profiles.Save();
    }
}
