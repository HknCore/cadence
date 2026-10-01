using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Cadence.App.Controls;

/// <summary>
/// Schlanke Segment-Auswahl (ersetzt das CommunityToolkit-Steuerelement, das sonst das komplette
/// Windows App SDK samt KI-Bibliotheken mitziehen wuerde). Die Markierung gleitet zum gewaehlten Eintrag.
///
/// Verwendung: &lt;local:SegmentedBar Items="30|40|60" SelectedIndex="{x:Bind ..., Mode=TwoWay}" /&gt;
/// </summary>
public sealed class SegmentedBar : Grid
{
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items), typeof(string), typeof(SegmentedBar), new PropertyMetadata("", (d, _) => ((SegmentedBar)d).Rebuild()));

    public static readonly DependencyProperty SelectedIndexProperty = DependencyProperty.Register(
        nameof(SelectedIndex), typeof(int), typeof(SegmentedBar), new PropertyMetadata(-1, (d, e) => ((SegmentedBar)d).OnSelected((int)e.NewValue)));

    /// <summary>Beschriftungen, getrennt mit "|".</summary>
    public string Items
    {
        get => (string)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public int SelectedIndex
    {
        get => (int)GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    /// <summary>Wird ausgeloest, wenn der Benutzer einen Eintrag waehlt.</summary>
    public event EventHandler<int>? SelectionChanged;

    public static readonly DependencyProperty UniformProperty = DependencyProperty.Register(
        nameof(Uniform), typeof(bool), typeof(SegmentedBar), new PropertyMetadata(false, (d, _) => ((SegmentedBar)d).Rebuild()));

    /// <summary>true = gleich breite Segmente (Modus-Auswahl), false = so breit wie der Text (Zahlen).</summary>
    public bool Uniform
    {
        get => (bool)GetValue(UniformProperty);
        set => SetValue(UniformProperty, value);
    }

    private static readonly SolidColorBrush Track = new(Color.FromArgb(255, 0x1F, 0x1F, 0x1F));
    private static readonly SolidColorBrush TrackBorder = new(Color.FromArgb(255, 0x3A, 0x3A, 0x3A));
    private static readonly SolidColorBrush Accent = new(Color.FromArgb(255, 0xB9, 0xA3, 0xFF));
    private static readonly SolidColorBrush OnAccent = new(Color.FromArgb(255, 0x1A, 0x10, 0x33));
    private static readonly SolidColorBrush Normal = new(Color.FromArgb(255, 0xC8, 0xC8, 0xC8));
    private static readonly SolidColorBrush Hover = new(Color.FromArgb(255, 0x2D, 0x2D, 0x2D));
    private static readonly SolidColorBrush Clear = new(Color.FromArgb(0, 0, 0, 0));

    private readonly Grid _row = new();
    private readonly Border _indicator = new()
    {
        Background = Accent,
        CornerRadius = new CornerRadius(4),
        HorizontalAlignment = HorizontalAlignment.Left,
    };
    private readonly List<Button> _buttons = [];

    public SegmentedBar()
    {
        Background = Track;
        BorderBrush = TrackBorder;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(6);
        Padding = new Thickness(3);
        Height = 38;

        // Gleiten statt Springen
        _indicator.TranslationTransition = new Vector3Transition { Duration = TimeSpan.FromMilliseconds(280) };
        Children.Add(_indicator);
        Children.Add(_row);
        SizeChanged += (_, _) => MoveIndicator(animate: false);
        _row.SizeChanged += (_, _) => MoveIndicator(animate: false);
        Loaded += (_, _) => MoveIndicator(animate: false);
    }

    private void Rebuild()
    {
        _row.Children.Clear();
        _row.ColumnDefinitions.Clear();
        _buttons.Clear();
        var labels = (Items ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < labels.Length; i++)
        {
            _row.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = Uniform ? new GridLength(1, GridUnitType.Star) : GridLength.Auto,
            });
            var index = i;
            var b = new Button
            {
                Content = labels[i],
                MinWidth = Uniform ? 0 : 44,
                Height = 30,
                Padding = new Thickness(12, 0, 12, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = Clear,
                BorderThickness = new Thickness(0),
                Foreground = Normal,
                CornerRadius = new CornerRadius(4),
                FontSize = 13,
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b, labels[i]);
            b.Resources["ButtonBackgroundPointerOver"] = Hover;
            b.Resources["ButtonBackgroundPressed"] = Hover;
            b.Resources["ButtonForegroundPointerOver"] = Normal;
            b.Click += (_, _) =>
            {
                if (SelectedIndex == index) return;
                SelectedIndex = index;
                SelectionChanged?.Invoke(this, index);
            };
            SetColumn(b, i);
            _row.Children.Add(b);
            _buttons.Add(b);
        }
        OnSelected(SelectedIndex);
    }

    private void OnSelected(int index)
    {
        for (var i = 0; i < _buttons.Count; i++)
        {
            var on = i == index;
            _buttons[i].Foreground = on ? OnAccent : Normal;
            _buttons[i].Resources["ButtonForegroundPointerOver"] = on ? OnAccent : Normal;
            _buttons[i].Resources["ButtonBackgroundPointerOver"] = on ? Clear : Hover;
            _buttons[i].FontWeight = on ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
        }
        MoveIndicator(animate: true);
    }

    private void MoveIndicator(bool animate)
    {
        var index = SelectedIndex;
        if (index < 0 || index >= _buttons.Count || _buttons[index].ActualWidth <= 0)
        {
            _indicator.Opacity = 0;
            return;
        }
        var target = _buttons[index];
        var pos = target.TransformToVisual(_row).TransformPoint(new Windows.Foundation.Point(0, 0));
        _indicator.Width = target.ActualWidth;
        _indicator.Height = target.ActualHeight;
        _indicator.VerticalAlignment = VerticalAlignment.Center;

        if (!animate || _indicator.Opacity == 0)
        {
            var t = _indicator.TranslationTransition;
            _indicator.TranslationTransition = null;
            _indicator.Translation = new System.Numerics.Vector3((float)pos.X, 0, 0);
            _indicator.TranslationTransition = t;
        }
        else
        {
            _indicator.Translation = new System.Numerics.Vector3((float)pos.X, 0, 0);
        }
        _indicator.Opacity = 1;
    }
}
