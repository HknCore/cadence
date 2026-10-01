using System.Runtime.InteropServices;

namespace Cadence.Core.Interop;

/// <summary>Win32/GDI-Funktionen fuer das Overlay-Fenster.</summary>
internal static partial class OverlayNative
{
    public const uint WS_POPUP = 0x80000000;
    public const uint WS_EX_LAYERED = 0x00080000;
    public const uint WS_EX_TRANSPARENT = 0x00000020;
    public const uint WS_EX_TOPMOST = 0x00000008;
    public const uint WS_EX_TOOLWINDOW = 0x00000080;
    public const uint WS_EX_NOACTIVATE = 0x08000000;

    public const int SW_HIDE = 0;
    public const int SW_SHOWNOACTIVATE = 4;

    public const uint WM_TIMER = 0x0113;
    public const uint ULW_ALPHA = 0x02;
    public const byte AC_SRC_OVER = 0x00;
    public const byte AC_SRC_ALPHA = 0x01;

    public const int TRANSPARENT = 1;
    public const uint ANTIALIASED_QUALITY = 4;
    public const int PS_SOLID = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public IntPtr lpszMenuName;
        public IntPtr lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE { public int Cx, Cy; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    // ---- user32 -----------------------------------------------------------
    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true)]
    public static partial ushort RegisterClassEx(ref WNDCLASSEXW wc);

    [LibraryImport("user32.dll", EntryPoint = "UnregisterClassW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterClass(IntPtr className, IntPtr hInstance);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true)]
    public static partial IntPtr CreateWindowEx(uint exStyle, IntPtr className, IntPtr windowName, uint style,
        int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    public static partial IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(IntPtr hwnd, int cmd);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT dst, ref SIZE size,
        IntPtr hdcSrc, ref POINT src, uint key, ref BLENDFUNCTION blend, uint flags);

    [LibraryImport("user32.dll")]
    public static partial nuint SetTimer(IntPtr hwnd, nuint id, uint elapse, IntPtr func);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool KillTimer(IntPtr hwnd, nuint id);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")]
    public static partial int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TranslateMessage(ref MSG msg);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    public static partial IntPtr DispatchMessage(ref MSG msg);

    [LibraryImport("user32.dll")]
    public static partial IntPtr GetDC(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    public static partial int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetClientRect(IntPtr hwnd, out RECT rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ClientToScreen(IntPtr hwnd, ref POINT pt);

    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForWindow(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsIconic(IntPtr hwnd);

    // ---- gdi32 ------------------------------------------------------------
    [LibraryImport("gdi32.dll")]
    public static partial IntPtr CreateCompatibleDC(IntPtr hdc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteDC(IntPtr hdc);

    [LibraryImport("gdi32.dll")]
    public static partial IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmi, uint usage,
        out IntPtr bits, IntPtr section, uint offset);

    [LibraryImport("gdi32.dll")]
    public static partial IntPtr SelectObject(IntPtr hdc, IntPtr obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(IntPtr obj);

    [LibraryImport("gdi32.dll", EntryPoint = "CreateFontW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr CreateFont(int height, int width, int escapement, int orientation, int weight,
        uint italic, uint underline, uint strikeOut, uint charSet, uint outPrecision, uint clipPrecision,
        uint quality, uint pitchAndFamily, string face);

    [LibraryImport("gdi32.dll")]
    public static partial int SetBkMode(IntPtr hdc, int mode);

    [LibraryImport("gdi32.dll")]
    public static partial uint SetTextColor(IntPtr hdc, uint color);

    [LibraryImport("gdi32.dll", EntryPoint = "TextOutW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TextOut(IntPtr hdc, int x, int y, string text, int length);

    [LibraryImport("gdi32.dll", EntryPoint = "GetTextExtentPoint32W", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetTextExtentPoint32(IntPtr hdc, string text, int length, out SIZE size);

    [LibraryImport("gdi32.dll")]
    public static partial IntPtr CreatePen(int style, int width, uint color);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Polyline(IntPtr hdc, POINT[] points, int count);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW")]
    public static partial IntPtr GetModuleHandle(IntPtr name);
}
