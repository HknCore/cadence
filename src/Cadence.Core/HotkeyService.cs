using Cadence.Core.Interop;

namespace Cadence.Core;

/// <summary>
/// Globale Tastenkürzel auf einem eigenen Thread mit Nachrichtenschleife.
/// Die Belegung kommt aus den Einstellungen und lässt sich zur Laufzeit ändern.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const uint WM_APP_REREGISTER = 0x8000 + 7;

    private readonly Thread _thread;
    private uint _threadId;
    private readonly ManualResetEventSlim _ready = new();
    private readonly object _lock = new();
    private List<HotkeyBinding> _bindings;
    private HashSet<HotkeyAction> _registered = [];
    private HashSet<HotkeyAction> _failed = [];
    private ManualResetEventSlim? _applied;

    public event EventHandler<HotkeyAction>? Pressed;

    public HotkeyService(IEnumerable<HotkeyBinding> bindings)
    {
        _bindings = bindings.Select(b => b.Clone()).ToList();
        _thread = new Thread(Run) { IsBackground = true, Name = "Cadence Hotkeys" };
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(2));
    }

    /// <summary>true, wenn alle belegten Kürzel registriert werden konnten.</summary>
    public bool Registered { get { lock (_lock) return _failed.Count == 0; } }

    /// <summary>Kürzel, die ein anderes Programm bereits belegt.</summary>
    public IReadOnlyCollection<HotkeyAction> Failed { get { lock (_lock) return _failed.ToList(); } }

    public bool IsRegistered(HotkeyAction action) { lock (_lock) return _registered.Contains(action); }

    /// <summary>Neue Belegung übernehmen. Wartet kurz, damit das Ergebnis (Konflikte) direkt abfragbar ist.</summary>
    public void Update(IEnumerable<HotkeyBinding> bindings)
    {
        var done = new ManualResetEventSlim();
        lock (_lock)
        {
            _bindings = bindings.Select(b => b.Clone()).ToList();
            _applied = done;
        }
        if (_threadId != 0 && Native.PostThreadMessage(_threadId, WM_APP_REREGISTER, IntPtr.Zero, IntPtr.Zero))
            done.Wait(TimeSpan.FromSeconds(1));
    }

    private void Run()
    {
        _threadId = Native.GetCurrentThreadId();
        Register();
        _ready.Set();

        while (Native.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == WM_APP_REREGISTER)
            {
                Unregister();
                Register();
                ManualResetEventSlim? done;
                lock (_lock) { done = _applied; _applied = null; }
                done?.Set();
                continue;
            }
            if (msg.message != Native.WM_HOTKEY) continue;
            var id = (int)msg.wParam;
            HotkeyAction? action;
            lock (_lock) action = id >= 1 && id <= _bindings.Count ? _bindings[id - 1].Action : null;
            if (action is { } a) Pressed?.Invoke(this, a);
        }
        Unregister();
    }

    // Laufen beide auf dem Hotkey-Thread: Win32 verlangt Registrieren und Empfangen im selben Thread.
    private void Register()
    {
        List<HotkeyBinding> list;
        lock (_lock) list = _bindings.ToList();
        var ok = new HashSet<HotkeyAction>();
        var failed = new HashSet<HotkeyAction>();
        for (var i = 0; i < list.Count; i++)
        {
            var b = list[i];
            if (b.IsEmpty) continue;
            if (Native.RegisterHotKey(IntPtr.Zero, i + 1, b.Modifiers | Native.MOD_NOREPEAT, b.Key)) ok.Add(b.Action);
            else failed.Add(b.Action);
        }
        lock (_lock) { _registered = ok; _failed = failed; }
    }

    private void Unregister()
    {
        int count;
        lock (_lock) count = Math.Max(_bindings.Count, 8);
        for (var i = 1; i <= count; i++) Native.UnregisterHotKey(IntPtr.Zero, i);
    }

    public void Dispose()
    {
        if (_threadId != 0) Native.PostThreadMessage(_threadId, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(1000);
        _ready.Dispose();
    }
}
