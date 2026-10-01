using System.Diagnostics;
using System.Runtime.InteropServices;
using Cadence.Core.Interop;
using static Cadence.Core.Interop.OverlayNative;

namespace Cadence.Core.Overlay;

/// <summary>Was das Overlay gerade zeigen soll. null = ausblenden.</summary>
public sealed record OverlayFrame(
    int ProcessId,
    OverlayVariant Variant,
    OverlayCorner Corner,
    LiveSnapshot Data,
    string? Toast);

/// <summary>
/// Transparentes, klick-durchlaessiges Fenster, das ueber dem Spielfenster schwebt.
/// Laeuft auf einem eigenen Thread und zeichnet mit GDI in ein Bitmap mit Alphakanal
/// (UpdateLayeredWindow). Funktioniert im Fenster- und randlosen Vollbildmodus;
/// im exklusiven Vollbild zeigt Windows keine fremden Fenster ueber dem Spiel.
/// </summary>
public sealed unsafe class OverlayWindow : IDisposable
{
    private const uint FrameIntervalMs = 33; // ~30 Bilder pro Sekunde reichen fuer Zahlen und Mini-Graph
    private const double BackgroundAlpha = 0.80;

    // Farben als COLORREF (0x00BBGGRR)
    private static uint Rgb(byte r, byte g, byte b) => (uint)(r | (g << 8) | (b << 16));
    private static readonly uint TextColor = Rgb(0xF3, 0xF3, 0xF3);
    private static readonly uint MutedColor = Rgb(0xBD, 0xBD, 0xBD);
    private static readonly uint AccentColor = Rgb(0xB9, 0xA3, 0xFF);
    private static readonly uint AccentLight = Rgb(0xD3, 0xC5, 0xFF);
    private static readonly uint WarnColor = Rgb(0xE0, 0xA4, 0x58);
    private const uint BackgroundBgr = 0x00141414; // im DIB als 0x00RRGGBB (BGRA im Speicher)

    private readonly Func<OverlayFrame?> _source;
    private readonly Thread _thread;
    private uint _threadId;
    private IntPtr _hwnd;
    private IntPtr _className;
    private bool _visible;

    private readonly Dictionary<(int Size, int Weight), IntPtr> _fonts = [];
    private IntPtr _gameHwnd;
    private int _gamePid;

    public OverlayWindow(Func<OverlayFrame?> source)
    {
        _source = source;
        _thread = new Thread(Run) { IsBackground = true, Name = "Cadence Overlay" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    [UnmanagedCallersOnly]
    private static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam) =>
        DefWindowProc(hwnd, msg, wParam, lParam);

    private void Run()
    {
        _threadId = Native.GetCurrentThreadId();
        var instance = GetModuleHandle(IntPtr.Zero);
        _className = Marshal.StringToHGlobalUni("CadenceOverlay");

        var wc = new WNDCLASSEXW
        {
            cbSize = (uint)sizeof(WNDCLASSEXW),
            lpfnWndProc = (IntPtr)(delegate* unmanaged<IntPtr, uint, IntPtr, IntPtr, IntPtr>)&WndProc,
            hInstance = instance,
            lpszClassName = _className,
        };
        RegisterClassEx(ref wc);

        _hwnd = CreateWindowEx(
            WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
            _className, IntPtr.Zero, WS_POPUP, 0, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) return;

        SetTimer(_hwnd, 1, FrameIntervalMs, IntPtr.Zero);
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == WM_TIMER && msg.hwnd == _hwnd)
            {
                try { Tick(); } catch { /* Overlay darf die App nie mitreissen */ }
                continue;
            }
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        KillTimer(_hwnd, 1);
        DestroyWindow(_hwnd);
        foreach (var f in _fonts.Values) DeleteObject(f);
        UnregisterClass(_className, instance);
        Marshal.FreeHGlobal(_className);
    }

    private void Tick()
    {
        var frame = _source();
        var game = frame is null ? IntPtr.Zero : FindGameWindow(frame.ProcessId);
        if (frame is null || game == IntPtr.Zero || IsIconic(game))
        {
            SetVisible(false);
            return;
        }

        var scale = Math.Max(1.0, GetDpiForWindow(game) / 96.0);
        Render(frame, game, scale);
        SetVisible(true);
    }

    private void SetVisible(bool visible)
    {
        if (_visible == visible) return;
        _visible = visible;
        ShowWindow(_hwnd, visible ? SW_SHOWNOACTIVATE : SW_HIDE);
    }

    /// <summary>Das Overlay erscheint nur, wenn das Spiel im Vordergrund ist.</summary>
    private IntPtr FindGameWindow(int pid)
    {
        var fg = Native.GetForegroundWindow();
        if (fg == IntPtr.Zero) return IntPtr.Zero;
        Native.GetWindowThreadProcessId(fg, out var fgPid);
        if (fgPid != (uint)pid) return IntPtr.Zero;
        if (_gamePid != pid || _gameHwnd != fg) { _gamePid = pid; _gameHwnd = fg; }
        return fg;
    }

    // ------------------------------------------------------------------ Zeichnen
    private void Render(OverlayFrame f, IntPtr game, double s)
    {
        int S(double v) => (int)Math.Round(v * s);
        var d = f.Data;
        var hasToast = !string.IsNullOrEmpty(f.Toast);

        var (w, h) = f.Variant switch
        {
            OverlayVariant.Minimal => (108, 32),
            OverlayVariant.Detail => (260, 128),
            _ => (200, 76),
        };
        if (hasToast) h += f.Variant == OverlayVariant.Minimal ? 26 : 28;
        if (hasToast && f.Variant == OverlayVariant.Minimal) w = 230;
        var pw = S(w);
        var ph = S(h);
        var radius = f.Variant == OverlayVariant.Minimal && !hasToast ? ph / 2.0 : S(8);

        var screenDc = GetDC(IntPtr.Zero);
        var memDc = CreateCompatibleDC(screenDc);
        var bmi = new BITMAPINFOHEADER
        {
            biSize = (uint)sizeof(BITMAPINFOHEADER),
            biWidth = pw,
            biHeight = -ph, // oben beginnend
            biPlanes = 1,
            biBitCount = 32,
        };
        var bitmap = CreateDIBSection(memDc, ref bmi, 0, out var bitsPtr, IntPtr.Zero, 0);
        var oldBitmap = SelectObject(memDc, bitmap);
        var bits = (uint*)bitsPtr;

        try
        {
            for (var i = 0; i < pw * ph; i++) bits[i] = BackgroundBgr;
            SetBkMode(memDc, TRANSPARENT);

            var fps = d.Fps > 0 ? d.Fps.ToString("0") : "–";
            var ft = d.FrametimeMs > 0 ? d.FrametimeMs.ToString("0.00") + " ms" : "– ms";
            var lineColor = d.Limited ? AccentColor : WarnColor;

            switch (f.Variant)
            {
                case OverlayVariant.Minimal:
                {
                    DrawDot(bits, pw, ph, S(16), S(16), S(3.5), lineColor);
                    var x = S(28);
                    x += Text(memDc, fps, x, S(7), S(15), 600, TextColor);
                    Text(memDc, " FPS", x, S(9), S(12), 400, MutedColor);
                    break;
                }
                case OverlayVariant.Detail:
                {
                    var x = S(16);
                    x += Text(memDc, fps, x, S(8), S(30), 600, TextColor);
                    Text(memDc, " FPS", x, S(22), S(12), 400, MutedColor);
                    var target = d.Limited ? $"Ziel {d.TargetFps:0}" : "unbegrenzt";
                    TextRight(memDc, target, pw - S(16), S(20), S(12), 600, d.Limited ? AccentColor : WarnColor);
                    Graph(memDc, d.Recent, S(16), S(50), pw - S(32), S(28), Math.Max(1, s), lineColor, d.TargetFps);
                    Label(memDc, "Frametime", ft, S(16), S(88), s);
                    Label(memDc, "1 % Low", d.Low1Fps > 0 ? d.Low1Fps.ToString("0.0") : "–", S(16 + 80), S(88), s);
                    Label(memDc, "Modus", ModeShort(d.Mode), S(16 + 156), S(88), s);
                    break;
                }
                default:
                {
                    var x = S(14);
                    x += Text(memDc, fps, x, S(6), S(26), 600, TextColor);
                    Text(memDc, " FPS", x, S(18), S(12), 400, MutedColor);
                    TextRight(memDc, ft, pw - S(14), S(18), S(12), 400, MutedColor);
                    Graph(memDc, d.Recent, S(14), S(46), pw - S(28), S(20), Math.Max(1, s), lineColor, d.TargetFps);
                    break;
                }
            }

            if (hasToast)
            {
                var y = ph - S(24);
                for (var x = S(12); x < pw - S(12); x++) bits[(y - S(4)) * pw + x] = 0x00303030; // Trennlinie
                Text(memDc, f.Toast!, S(14), y, S(12), 400, AccentLight);
            }

            ApplyAlpha(bits, pw, ph, radius);
            Place(game, memDc, pw, ph, f.Corner, S(16));
        }
        finally
        {
            SelectObject(memDc, oldBitmap);
            DeleteObject(bitmap);
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private static string ModeShort(LimiterMode m) => m switch
    {
        LimiterMode.LowLatency => "Latenz",
        LimiterMode.Smooth => "Glätte",
        _ => "Ausgew.",
    };

    private void Label(IntPtr dc, string label, string value, int x, int y, double s)
    {
        Text(dc, label, x, y, (int)Math.Round(11 * s), 400, MutedColor);
        Text(dc, value, x, y + (int)Math.Round(15 * s), (int)Math.Round(13 * s), 600, TextColor);
    }

    private IntPtr Font(int size, int weight)
    {
        if (_fonts.TryGetValue((size, weight), out var f)) return f;
        // Graustufen-Glaettung: ClearType-Farbsaeume wuerden auf transparentem Grund unschoen aussehen.
        f = CreateFont(-size, 0, 0, 0, weight, 0, 0, 0, 1, 0, 0, ANTIALIASED_QUALITY, 0, "Segoe UI Variable Display");
        _fonts[(size, weight)] = f;
        return f;
    }

    /// <returns>Breite des Textes in Pixeln.</returns>
    private int Text(IntPtr dc, string text, int x, int y, int size, int weight, uint color)
    {
        var old = SelectObject(dc, Font(size, weight));
        SetTextColor(dc, color);
        TextOut(dc, x, y, text, text.Length);
        GetTextExtentPoint32(dc, text, text.Length, out var ext);
        SelectObject(dc, old);
        return ext.Cx;
    }

    private void TextRight(IntPtr dc, string text, int right, int y, int size, int weight, uint color)
    {
        var old = SelectObject(dc, Font(size, weight));
        GetTextExtentPoint32(dc, text, text.Length, out var ext);
        SelectObject(dc, old);
        Text(dc, text, right - ext.Cx, y, size, weight, color);
    }

    private static void Graph(IntPtr dc, float[] samples, int x, int y, int w, int h, double s, uint color, double targetFps)
    {
        if (samples.Length < 2) return;
        // Skala um das Ziel zentriert, damit kleine Schwankungen sichtbar bleiben
        var center = targetFps > 0 ? 1000.0 / targetFps : samples.Average(v => (double)v);
        var range = Math.Max(2.0, samples.Max(v => Math.Abs(v - center)) * 1.2);
        var pts = new POINT[samples.Length];
        for (var i = 0; i < samples.Length; i++)
        {
            var t = Math.Clamp((samples[i] - center) / range, -1, 1);
            pts[i] = new POINT
            {
                X = x + (int)Math.Round((double)i * w / (samples.Length - 1)),
                Y = y + (int)Math.Round(h / 2.0 - t * h / 2.0), // laengere Frames nach oben
            };
        }
        var pen = CreatePen(PS_SOLID, Math.Max(1, (int)Math.Round(1.5 * s)), color);
        var old = SelectObject(dc, pen);
        Polyline(dc, pts, pts.Length);
        SelectObject(dc, old);
        DeleteObject(pen);
    }

    private static void DrawDot(uint* bits, int pw, int ph, int cx, int cy, double r, uint colorref)
    {
        // COLORREF (BGR) -> DIB-Pixel (0x00RRGGBB)
        var px = ((colorref & 0xFF) << 16) | (colorref & 0xFF00) | ((colorref >> 16) & 0xFF);
        for (var y = (int)(cy - r - 1); y <= cy + r + 1; y++)
        for (var x = (int)(cx - r - 1); x <= cx + r + 1; x++)
        {
            if (x < 0 || y < 0 || x >= pw || y >= ph) continue;
            var dx = x + 0.5 - cx;
            var dy = y + 0.5 - cy;
            if (dx * dx + dy * dy <= r * r) bits[y * pw + x] = px;
        }
    }

    /// <summary>
    /// Setzt den Alphakanal: halbtransparenter Hintergrund mit abgerundeten Ecken,
    /// vormultipliziert, wie UpdateLayeredWindow es erwartet.
    /// </summary>
    private static void ApplyAlpha(uint* bits, int pw, int ph, double radius)
    {
        for (var y = 0; y < ph; y++)
        for (var x = 0; x < pw; x++)
        {
            var coverage = Coverage(x + 0.5, y + 0.5, pw, ph, radius);
            var i = y * pw + x;
            if (coverage <= 0) { bits[i] = 0; continue; }

            var p = bits[i];
            // Text und Linien heben sich vom Hintergrund ab und bleiben dadurch kraeftiger.
            var isBackground = (p & 0x00FFFFFF) == BackgroundBgr;
            var a = (isBackground ? BackgroundAlpha : 0.96) * coverage;
            var r = (byte)(((p >> 16) & 0xFF) * a);
            var g = (byte)(((p >> 8) & 0xFF) * a);
            var b = (byte)((p & 0xFF) * a);
            bits[i] = ((uint)(a * 255) << 24) | ((uint)r << 16) | ((uint)g << 8) | b;
        }
    }

    private static double Coverage(double x, double y, int w, int h, double r)
    {
        var cx = Math.Clamp(x, r, w - r);
        var cy = Math.Clamp(y, r, h - r);
        var dx = x - cx;
        var dy = y - cy;
        var dist = Math.Sqrt(dx * dx + dy * dy);
        return Math.Clamp(r - dist + 0.5, 0, 1);
    }

    private void Place(IntPtr game, IntPtr memDc, int pw, int ph, OverlayCorner corner, int margin)
    {
        GetClientRect(game, out var rc);
        var origin = new POINT();
        ClientToScreen(game, ref origin);
        var gw = rc.Right - rc.Left;
        var gh = rc.Bottom - rc.Top;

        var dst = new POINT
        {
            X = corner is OverlayCorner.TopRight or OverlayCorner.BottomRight ? origin.X + gw - pw - margin : origin.X + margin,
            Y = corner is OverlayCorner.BottomLeft or OverlayCorner.BottomRight ? origin.Y + gh - ph - margin : origin.Y + margin,
        };
        var size = new SIZE { Cx = pw, Cy = ph };
        var src = new POINT();
        var blend = new BLENDFUNCTION { BlendOp = AC_SRC_OVER, SourceConstantAlpha = 255, AlphaFormat = AC_SRC_ALPHA };
        var screenDc = GetDC(IntPtr.Zero);
        UpdateLayeredWindow(_hwnd, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, ULW_ALPHA);
        ReleaseDC(IntPtr.Zero, screenDc);
    }

    public void Dispose()
    {
        if (_threadId != 0) Native.PostThreadMessage(_threadId, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(1000);
    }
}
