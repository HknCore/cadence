using Cadence.Core.Interop;

namespace Cadence.Core;

public enum HotkeyAction
{
    TargetUp,
    TargetDown,
    ToggleRecording,
    ToggleOverlay,
}

/// <summary>
/// Globale Hotkeys auf einem eigenen Thread mit Nachrichtenschleife:
///  Strg + Alt + ↑ / ↓  Ziel-FPS hoch/runter
///  Strg + Alt + R      Aufnahme starten/stoppen
///  Strg + Alt + O      Overlay ein/aus
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private static readonly (HotkeyAction Action, uint Vk)[] Bindings =
    [
        (HotkeyAction.TargetUp, Native.VK_UP),
        (HotkeyAction.TargetDown, Native.VK_DOWN),
        (HotkeyAction.ToggleRecording, 0x52), // R
        (HotkeyAction.ToggleOverlay, 0x4F),   // O
    ];

    private readonly Thread _thread;
    private uint _threadId;
    private readonly ManualResetEventSlim _ready = new();
    private readonly HashSet<HotkeyAction> _registered = [];

    public event EventHandler<HotkeyAction>? Pressed;

    /// <summary>true, wenn alle Hotkeys registriert werden konnten.</summary>
    public bool Registered => _registered.Count == Bindings.Length;

    public bool IsRegistered(HotkeyAction action) => _registered.Contains(action);

    public HotkeyService()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "Cadence Hotkeys" };
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(2));
    }

    private void Run()
    {
        _threadId = Native.GetCurrentThreadId();
        const uint mods = Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT;
        // hwnd = 0: WM_HOTKEY landet in der Nachrichtenschlange dieses Threads.
        for (var i = 0; i < Bindings.Length; i++)
            if (Native.RegisterHotKey(IntPtr.Zero, i + 1, mods, Bindings[i].Vk))
                _registered.Add(Bindings[i].Action);
        _ready.Set();

        while (Native.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message != Native.WM_HOTKEY) continue;
            var index = (int)msg.wParam - 1;
            if (index >= 0 && index < Bindings.Length)
                Pressed?.Invoke(this, Bindings[index].Action);
        }

        for (var i = 0; i < Bindings.Length; i++)
            Native.UnregisterHotKey(IntPtr.Zero, i + 1);
    }

    public void Dispose()
    {
        if (_threadId != 0) Native.PostThreadMessage(_threadId, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(1000);
        _ready.Dispose();
    }
}
