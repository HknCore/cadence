using System.Runtime.InteropServices;
using Cadence.Core.Interop;

namespace Cadence.Core.Tray;

/// <summary>Ein Eintrag im Tray-Menue. Id 0 = Trennlinie (wenn Text leer) oder nur Ueberschrift.</summary>
public sealed record TrayMenuItem(
    int Id,
    string Text,
    bool Checked = false,
    bool Enabled = true,
    IReadOnlyList<TrayMenuItem>? Children = null)
{
    public static readonly TrayMenuItem Separator = new(0, "");
}

/// <summary>
/// Symbol im Infobereich der Taskleiste mit Kontextmenue.
/// Laeuft auf einem eigenen Thread; Ereignisse kommen auf diesem Thread an.
/// </summary>
public sealed unsafe class TrayIcon : IDisposable
{
    private const uint WM_TRAY = 0x8000 + 1; // WM_APP + 1
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_NULL = 0x0000;
    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
    private const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4;
    private const uint MF_STRING = 0x0, MF_GRAYED = 0x1, MF_CHECKED = 0x8, MF_POPUP = 0x10, MF_SEPARATOR = 0x800;
    private const uint TPM_RIGHTBUTTON = 0x2, TPM_BOTTOMALIGN = 0x20, TPM_RETURNCMD = 0x100, TPM_NONOTIFY = 0x80;
    private const uint IMAGE_ICON = 1, LR_LOADFROMFILE = 0x10;
    private const int SM_CXSMICON = 49, SM_CYSMICON = 50;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        public fixed char szTip[128];
        public uint dwState;
        public uint dwStateMask;
        public fixed char szInfo[256];
        public uint uTimeoutOrVersion;
        public fixed char szInfoTitle[64];
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(uint message, ref NOTIFYICONDATAW data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImageW(IntPtr hInst, string name, uint type, int cx, int cy, uint load);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenuW(IntPtr menu, uint flags, nuint id, string? text);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern bool SetMenuDefaultItem(IntPtr menu, uint item, uint byPos);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT pt);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessageW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string name);

    private static TrayIcon? _instance; // fuer die statische Fensterprozedur
    private static uint _taskbarCreated;

    private readonly string _iconPath;
    private readonly Func<IReadOnlyList<TrayMenuItem>> _menu;
    private readonly Thread _thread;
    private uint _threadId;
    private IntPtr _hwnd;
    private IntPtr _icon;
    private IntPtr _className;
    private string _tooltip;

    /// <summary>Linksklick auf das Symbol.</summary>
    public event EventHandler? Activated;
    /// <summary>Ein Menue-Eintrag wurde gewaehlt (Id).</summary>
    public event EventHandler<int>? CommandInvoked;

    public TrayIcon(string iconPath, string tooltip, Func<IReadOnlyList<TrayMenuItem>> menu)
    {
        _iconPath = iconPath;
        _tooltip = tooltip;
        _menu = menu;
        _instance = this;
        _thread = new Thread(Run) { IsBackground = true, Name = "Cadence Tray" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    [UnmanagedCallersOnly]
    private static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        var self = _instance;
        if (self is not null)
        {
            try
            {
                if (msg == WM_TRAY)
                {
                    var mouse = (uint)(lParam.ToInt64() & 0xFFFF);
                    if (mouse == WM_LBUTTONUP) self.Activated?.Invoke(self, EventArgs.Empty);
                    else if (mouse == WM_RBUTTONUP) self.ShowMenu();
                    return IntPtr.Zero;
                }
                if (msg == _taskbarCreated && _taskbarCreated != 0)
                {
                    self.Add(); // Explorer wurde neu gestartet
                    return IntPtr.Zero;
                }
            }
            catch
            {
                // Fehler im Menue duerfen die App nicht beenden.
            }
        }
        return OverlayNative.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void Run()
    {
        _threadId = Native.GetCurrentThreadId();
        var instance = OverlayNative.GetModuleHandle(IntPtr.Zero);
        _className = Marshal.StringToHGlobalUni("CadenceTray");
        _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");

        var wc = new OverlayNative.WNDCLASSEXW
        {
            cbSize = (uint)sizeof(OverlayNative.WNDCLASSEXW),
            lpfnWndProc = (IntPtr)(delegate* unmanaged<IntPtr, uint, IntPtr, IntPtr, IntPtr>)&WndProc,
            hInstance = instance,
            lpszClassName = _className,
        };
        OverlayNative.RegisterClassEx(ref wc);
        // Unsichtbares Fenster, das die Nachrichten des Tray-Symbols empfaengt.
        _hwnd = OverlayNative.CreateWindowEx(OverlayNative.WS_EX_TOOLWINDOW, _className, IntPtr.Zero,
            OverlayNative.WS_POPUP, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);

        _icon = LoadImageW(IntPtr.Zero, _iconPath, IMAGE_ICON,
            GetSystemMetrics(SM_CXSMICON), GetSystemMetrics(SM_CYSMICON), LR_LOADFROMFILE);
        Add();

        while (OverlayNative.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            OverlayNative.TranslateMessage(ref msg);
            OverlayNative.DispatchMessage(ref msg);
        }

        var data = NewData();
        Shell_NotifyIconW(NIM_DELETE, ref data);
        if (_icon != IntPtr.Zero) DestroyIcon(_icon);
        OverlayNative.DestroyWindow(_hwnd);
        OverlayNative.UnregisterClass(_className, instance);
        Marshal.FreeHGlobal(_className);
    }

    private NOTIFYICONDATAW NewData()
    {
        var d = new NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(NOTIFYICONDATAW),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAY,
            hIcon = _icon,
        };
        var tip = _tooltip.Length > 127 ? _tooltip[..127] : _tooltip;
        for (var i = 0; i < tip.Length; i++) d.szTip[i] = tip[i];
        return d;
    }

    private void Add()
    {
        var d = NewData();
        Shell_NotifyIconW(NIM_ADD, ref d);
    }

    /// <summary>Text beim Darueberfahren (z. B. "Cadence · 60 FPS").</summary>
    public void SetTooltip(string text)
    {
        _tooltip = text;
        if (_hwnd == IntPtr.Zero) return;
        var d = NewData();
        Shell_NotifyIconW(NIM_MODIFY, ref d);
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        var subMenus = new List<IntPtr>();
        foreach (var item in _menu()) Append(menu, item, subMenus);
        SetMenuDefaultItem(menu, 0, 1);

        GetCursorPos(out var pt);
        // Noetig, damit sich das Menue beim Klick daneben wieder schliesst.
        SetForegroundWindow(_hwnd);
        var id = TrackPopupMenu(menu, TPM_RIGHTBUTTON | TPM_BOTTOMALIGN | TPM_RETURNCMD | TPM_NONOTIFY,
            pt.X, pt.Y, 0, _hwnd, IntPtr.Zero);
        PostMessageW(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);
        DestroyMenu(menu); // zerstoert auch die Untermenues

        if (id > 0) CommandInvoked?.Invoke(this, id);
    }

    private static void Append(IntPtr menu, TrayMenuItem item, List<IntPtr> subMenus)
    {
        if (item.Id == 0 && item.Text.Length == 0)
        {
            AppendMenuW(menu, MF_SEPARATOR, 0, null);
            return;
        }
        var flags = MF_STRING;
        if (item.Checked) flags |= MF_CHECKED;
        if (!item.Enabled) flags |= MF_GRAYED;

        if (item.Children is { Count: > 0 })
        {
            var sub = CreatePopupMenu();
            subMenus.Add(sub);
            foreach (var c in item.Children) Append(sub, c, subMenus);
            AppendMenuW(menu, flags | MF_POPUP, (nuint)sub, item.Text);
        }
        else
        {
            AppendMenuW(menu, flags, (nuint)item.Id, item.Text);
        }
    }

    public void Dispose()
    {
        if (_threadId != 0) Native.PostThreadMessage(_threadId, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(1000);
        if (_instance == this) _instance = null;
    }
}
