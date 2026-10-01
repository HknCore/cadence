using System.Text.Json.Serialization;

namespace Cadence.Core;

public enum LimiterMode
{
    Balanced = 0,
    LowLatency = 1,
    Smooth = 2,
}

public enum HookState
{
    None = 0,
    Initializing = 1,
    Active = 2,
    Failed = 3,
}

[Flags]
public enum GraphicsApi
{
    None = 0,
    Dxgi = 1,
    OpenGL = 2,
}

public sealed class GameProfile
{
    /// <summary>Dateiname der Spiel-EXE, z. B. "Cyberpunk2077.exe" (Gross-/Kleinschreibung egal).</summary>
    public string ExeName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public double TargetFps { get; set; } = 60;
    public LimiterMode Mode { get; set; } = LimiterMode.Balanced;
    public bool AutoApply { get; set; } = true;
    public bool OverlayEnabled { get; set; } = true;
    public bool HotkeysEnabled { get; set; } = true;
    public DateTimeOffset? LastPlayed { get; set; }

    /// <summary>
    /// Ziel-FPS = Bildwiederholrate des Hauptbildschirms.
    /// null = automatisch: beim Standardprofil an, bei Spielprofilen aus.
    /// </summary>
    public bool? MatchRefreshRate { get; set; }

    [JsonIgnore]
    public bool FollowsRefreshRate => MatchRefreshRate ?? IsDefault;

    /// <summary>Uebernimmt die aktuelle Bildwiederholrate, falls das Profil ihr folgt.</summary>
    public bool ApplyRefreshRate()
    {
        if (!FollowsRefreshRate) return false;
        var hz = DisplayInfo.PrimaryRefreshRate();
        if (TargetFps == hz) return false;
        TargetFps = hz;
        return true;
    }

    [JsonIgnore]
    public bool IsDefault => ExeName.Length == 0;

    public GameProfile Clone() => (GameProfile)MemberwiseClone();
}

public enum OverlayVariant { Minimal = 0, Compact = 1, Detail = 2 }

public enum OverlayCorner { TopLeft = 0, TopRight = 1, BottomLeft = 2, BottomRight = 3 }

public sealed class OverlaySettings
{
    public bool Enabled { get; set; } = true;
    public OverlayVariant Variant { get; set; } = OverlayVariant.Compact;
    public OverlayCorner Corner { get; set; } = OverlayCorner.TopLeft;
}
