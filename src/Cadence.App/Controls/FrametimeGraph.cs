using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace Cadence.App.Controls;

/// <summary>
/// Live-Frametime-Graph. Skala, Ziel-Linie und Farbe gleiten weich zu neuen Werten,
/// statt zu springen. Wird pro gerendertem Frame mit <see cref="Render"/> aktualisiert.
/// </summary>
public sealed class FrametimeGraph : Grid
{
    private const int Capacity = 240;
    private const double AxisWidth = 28;

    private readonly float[] _ring = new float[Capacity];
    private int _count;
    private int _head;

    private readonly Canvas _canvas = new();
    private readonly Polyline _line = new() { StrokeThickness = 1.75, StrokeLineJoin = PenLineJoin.Round };
    private readonly Polygon _area = new();
    private readonly Line _targetLine = new() { StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 5 }, Opacity = 0.7 };
    private readonly Line[] _grid = new Line[4];
    private readonly TextBlock[] _labels = new TextBlock[4];
    private readonly SolidColorBrush _lineBrush = new();
    private readonly SolidColorBrush _areaBrush = new();

    private static readonly Color Accent = Color.FromArgb(255, 0xB9, 0xA3, 0xFF);
    private static readonly Color Warn = Color.FromArgb(255, 0xE0, 0xA4, 0x58);

    private double _yMax = 40;          // angezeigte Skala (ms), gleitet
    private double _shownTarget = 16.67; // angezeigtes Ziel (ms), gleitet
    private double _targetMs = 16.67;
    private bool _hasTarget = true;
    private double _warmth;              // 0 = Akzent (begrenzt), 1 = Warnfarbe (unbegrenzt)
    private double _warmthGoal;

    public FrametimeGraph()
    {
        MinHeight = 160;
        Children.Add(_canvas);

        var gridBrush = new SolidColorBrush(Color.FromArgb(255, 0x2C, 0x2C, 0x2C));
        var labelBrush = new SolidColorBrush(Color.FromArgb(255, 0x8A, 0x8A, 0x8A));
        for (var i = 0; i < 4; i++)
        {
            _grid[i] = new Line { Stroke = gridBrush, StrokeThickness = 1 };
            _labels[i] = new TextBlock { FontSize = 11, Foreground = labelBrush };
            _canvas.Children.Add(_grid[i]);
            _canvas.Children.Add(_labels[i]);
        }

        _line.Stroke = _lineBrush;
        _area.Fill = _areaBrush;
        _targetLine.Stroke = new SolidColorBrush(Accent);
        _canvas.Children.Add(_area);
        _canvas.Children.Add(_targetLine);
        _canvas.Children.Add(_line);

        SizeChanged += (_, _) => Render();
    }

    public void Clear()
    {
        _count = 0;
        _head = 0;
        Render();
    }

    public void Push(ReadOnlySpan<float> samples)
    {
        foreach (var s in samples)
        {
            _ring[_head] = s;
            _head = (_head + 1) % Capacity;
            if (_count < Capacity) _count++;
        }
    }

    /// <param name="targetMs">Ziel-Frametime; null = unbegrenzt.</param>
    public void SetTarget(double? targetMs)
    {
        _hasTarget = targetMs.HasValue;
        if (targetMs.HasValue) _targetMs = targetMs.Value;
        _warmthGoal = _hasTarget ? 0 : 1;
    }

    public void Render()
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= AxisWidth || h <= 10) return;

        // Skala: genug Platz fuer Ziel und Ausreisser, weich nachgefuehrt.
        double max = _hasTarget ? _targetMs * 2 : 20;
        for (var i = 0; i < _count; i++) max = Math.Max(max, _ring[i] * 1.15);
        max = Math.Clamp(Math.Ceiling(max / 10) * 10, 20, 200);
        _yMax += (max - _yMax) * 0.12;
        _shownTarget += (_targetMs - _shownTarget) * 0.18;
        _warmth += (_warmthGoal - _warmth) * 0.12;

        double Y(double ms) => h - Math.Clamp(ms / _yMax, 0, 1) * (h - 8);

        // Gitter mit Beschriftung
        for (var i = 0; i < 4; i++)
        {
            var ms = _yMax * (i + 1) / 4.0;
            var y = Y(ms);
            _grid[i].X1 = AxisWidth; _grid[i].X2 = w; _grid[i].Y1 = y; _grid[i].Y2 = y;
            _labels[i].Text = ms.ToString("0");
            Canvas.SetTop(_labels[i], y - 8);
        }

        // Kurve
        var pts = new PointCollection();
        var area = new PointCollection { new Point(w, h) };
        var plotW = w - AxisWidth;
        for (var i = 0; i < _count; i++)
        {
            var sample = _ring[(_head - _count + i + Capacity) % Capacity];
            var x = AxisWidth + plotW * (Capacity - _count + i) / (Capacity - 1);
            var p = new Point(x, Y(sample));
            pts.Add(p);
        }
        if (_count > 0)
        {
            area.Add(new Point(pts[0].X, h));
            foreach (var p in pts) area.Add(p);
        }
        _line.Points = pts;
        _area.Points = area;

        // Ziel-Linie
        var ty = Y(_shownTarget);
        _targetLine.X1 = AxisWidth; _targetLine.X2 = w; _targetLine.Y1 = ty; _targetLine.Y2 = ty;
        _targetLine.Opacity = 0.7 * (1 - _warmth);

        var c = Lerp(Accent, Warn, _warmth);
        _lineBrush.Color = c;
        _areaBrush.Color = Color.FromArgb(20, c.R, c.G, c.B);
    }

    private static Color Lerp(Color a, Color b, double t) => Color.FromArgb(
        255,
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));
}
