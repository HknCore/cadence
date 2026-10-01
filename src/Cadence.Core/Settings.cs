namespace Cadence.Core;

public enum HotkeyAction
{
    TargetUp,
    TargetDown,
    ToggleRecording,
    ToggleOverlay,
}

/// <summary>Ein Tastenkürzel: Modifier (Win32 MOD_*) plus virtueller Tastencode.</summary>
public sealed class HotkeyBinding
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;

    public HotkeyAction Action { get; set; }
    public uint Modifiers { get; set; }
    public uint Key { get; set; }

    public bool IsEmpty => Key == 0;

    public static List<HotkeyBinding> Defaults() =>
    [
        new() { Action = HotkeyAction.TargetUp, Modifiers = ModControl | ModAlt, Key = 0x26 },        // ↑
        new() { Action = HotkeyAction.TargetDown, Modifiers = ModControl | ModAlt, Key = 0x28 },      // ↓
        new() { Action = HotkeyAction.ToggleRecording, Modifiers = ModControl | ModAlt, Key = 0x52 }, // R
        new() { Action = HotkeyAction.ToggleOverlay, Modifiers = ModControl | ModAlt, Key = 0x4F },   // O
    ];

    public HotkeyBinding Clone() => new() { Action = Action, Modifiers = Modifiers, Key = Key };

    public bool SameKeys(HotkeyBinding other) => Modifiers == other.Modifiers && Key == other.Key && !IsEmpty;

    /// <summary>Lesbare Form, z. B. "Strg + Alt + R".</summary>
    public override string ToString()
    {
        if (IsEmpty) return "Nicht belegt";
        var parts = new List<string>();
        if ((Modifiers & ModControl) != 0) parts.Add("Strg");
        if ((Modifiers & ModAlt) != 0) parts.Add("Alt");
        if ((Modifiers & ModShift) != 0) parts.Add("Umschalt");
        if ((Modifiers & ModWin) != 0) parts.Add("Win");
        parts.Add(KeyName(Key));
        return string.Join(" + ", parts);
    }

    public static string KeyName(uint vk) => vk switch
    {
        >= 0x30 and <= 0x39 => ((char)vk).ToString(),          // 0–9
        >= 0x41 and <= 0x5A => ((char)vk).ToString(),          // A–Z
        >= 0x70 and <= 0x87 => "F" + (vk - 0x6F),              // F1–F24
        >= 0x60 and <= 0x69 => "Num " + (vk - 0x60),           // Ziffernblock
        0x26 => "↑", 0x28 => "↓", 0x25 => "←", 0x27 => "→",
        0x20 => "Leertaste", 0x0D => "Enter", 0x09 => "Tab", 0x08 => "Rücktaste",
        0x2D => "Einfg", 0x2E => "Entf", 0x24 => "Pos1", 0x23 => "Ende",
        0x21 => "Bild ↑", 0x22 => "Bild ↓", 0x13 => "Pause", 0x91 => "Rollen",
        0x6A => "Num *", 0x6B => "Num +", 0x6D => "Num -", 0x6F => "Num /", 0x6E => "Num ,",
        0xBB => "+", 0xBD => "-", 0xBC => ",", 0xBE => ".",
        _ => $"Taste {vk}",
    };

    /// <summary>Tasten, die allein keinen Sinn ergeben (Modifier selbst, Escape …).</summary>
    public static bool IsAllowedKey(uint vk) =>
        vk is not (0x10 or 0x11 or 0x12 or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 or 0x5B or 0x5C or 0x1B or 0x14 or 0x90);
}

public sealed class WindowPlacement
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool Maximized { get; set; }
}

/// <summary>App-weite Einstellungen (in profiles.json gespeichert).</summary>
public sealed class AppSettings
{
    /// <summary>Windows-Benachrichtigungen, wenn Cadence im Hintergrund läuft.</summary>
    public bool Notifications { get; set; } = true;

    /// <summary>Beim Start nach einer neuen Version auf GitHub suchen.</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>Version, die der Benutzer bewusst übersprungen hat.</summary>
    public string? SkippedVersion { get; set; }

    public WindowPlacement? Window { get; set; }

    public List<HotkeyBinding> Hotkeys { get; set; } = HotkeyBinding.Defaults();

    /// <summary>Fehlende Aktionen (z. B. nach einem Update) mit Standardwerten ergänzen.</summary>
    public void Normalize()
    {
        Hotkeys ??= [];
        Hotkeys = Hotkeys.GroupBy(h => h.Action).Select(g => g.First()).ToList();
        foreach (var d in HotkeyBinding.Defaults())
            if (Hotkeys.All(h => h.Action != d.Action)) Hotkeys.Add(d);
        Hotkeys = Hotkeys.OrderBy(h => h.Action).ToList();
    }

    public HotkeyBinding Hotkey(HotkeyAction action) =>
        Hotkeys.FirstOrDefault(h => h.Action == action) ?? new HotkeyBinding { Action = action };
}
