using System.Diagnostics;

namespace Cadence.Core;

public sealed record AntiCheatResult(bool Blocked, string Reason)
{
    public static readonly AntiCheatResult Clear = new(false, "");
}

/// <summary>
/// Schutz vor Banns: Cadence klinkt sich nie in Spiele mit Anti-Cheat ein.
/// Geprueft werden geladene Module, typische Ordner/Dateien neben der EXE und eine Liste
/// bekannter Online-Spiele. Im Zweifel wird blockiert.
/// </summary>
public static class AntiCheatGuard
{
    private static readonly string[] ModuleMarkers =
    [
        "easyanticheat", "eac_", "beclient", "battleye", "vgk", "vgc",
        "eaanticheat", "faceit", "xigncode", "x3.xem", "gameguard", "npggnt",
        "pbcl", "punkbuster", "ricochet", "atvi-", "eos_anticheat",
    ];

    private static readonly string[] FolderMarkers =
    [
        "EasyAntiCheat", "EasyAntiCheat_EOS", "BattlEye", "EAAntiCheat", "GameGuard", "XIGNCODE",
    ];

    private static readonly string[] FileMarkers =
    [
        "start_protected_game.exe", "EasyAntiCheat_EOS_Setup.exe", "EasyAntiCheat_Setup.exe",
        "BEService.exe", "BEService_x64.exe",
    ];

    // Bekannte Online-Spiele mit Anti-Cheat (inkl. VAC), die nicht immer erkennbare Spuren hinterlassen.
    private static readonly HashSet<string> KnownProtected = new(StringComparer.OrdinalIgnoreCase)
    {
        "VALORANT-Win64-Shipping.exe", "r5apex.exe", "r5apex_dx12.exe", "FortniteClient-Win64-Shipping.exe",
        "cs2.exe", "csgo.exe", "dota2.exe", "RainbowSix.exe", "RainbowSix_BE.exe", "TslGame.exe",
        "DestinyHost.exe", "destiny2.exe", "EscapeFromTarkov.exe", "RustClient.exe", "DayZ_x64.exe",
        "cod.exe", "ModernWarfare.exe", "BlackOps6.exe", "bf2042.exe", "bf6.exe", "TheFinals.exe",
        "Overwatch.exe", "LeagueClient.exe", "League of Legends.exe", "GTA5.exe", "GTA5_Enhanced.exe",
        "PathOfExile.exe", "PathOfExile_x64.exe", "NewWorld.exe", "Marvel-Win64-Shipping.exe",
        "deadlock.exe", "project8.exe", "eldenring_nightreign.exe",
    };

    public static AntiCheatResult Check(Process process)
    {
        string? exePath = null;
        try { exePath = process.MainModule?.FileName; }
        catch
        {
            // Geschuetzte Prozesse verweigern den Zugriff – typisch fuer Kernel-Anti-Cheat.
            return new(true, "Der Prozess ist geschützt (vermutlich Anti-Cheat).");
        }

        var exeName = exePath is null ? process.ProcessName + ".exe" : Path.GetFileName(exePath);
        if (KnownProtected.Contains(exeName))
            return new(true, "Bekanntes Online-Spiel mit Anti-Cheat.");

        try
        {
            foreach (ProcessModule m in process.Modules)
            {
                var name = m.ModuleName.ToLowerInvariant();
                foreach (var marker in ModuleMarkers)
                    if (name.Contains(marker))
                        return new(true, $"Anti-Cheat-Modul geladen ({m.ModuleName}).");
            }
        }
        catch
        {
            return new(true, "Module des Spiels sind nicht lesbar (vermutlich Anti-Cheat).");
        }

        if (exePath is not null)
        {
            var dir = Path.GetDirectoryName(exePath);
            // Spiele liegen oft in Unterordnern (z. B. bin\x64) – zwei Ebenen nach oben pruefen.
            for (var level = 0; level < 3 && dir is not null; level++)
            {
                if (FolderMarkers.Any(f => Directory.Exists(Path.Combine(dir, f))) ||
                    FileMarkers.Any(f => File.Exists(Path.Combine(dir, f))))
                    return new(true, "Anti-Cheat-Dateien im Spielordner gefunden.");
                dir = Path.GetDirectoryName(dir);
            }
        }

        return AntiCheatResult.Clear;
    }
}
