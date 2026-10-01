using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Cadence.Core;
using Windows.ApplicationModel.DataTransfer;

namespace Cadence.App.Services;

/// <summary>Sammelt alles, was zur Fehlersuche nötig ist, und öffnet ein GitHub-Issue.</summary>
public static class DiagnosticsReport
{
    private const int LogLines = 80;

    public static string Build()
    {
        var c = App.Controller;
        var sb = new StringBuilder();
        sb.AppendLine("### System");
        sb.AppendLine($"- Cadence: {MainWindow.AppVersion}");
        sb.AppendLine($"- Windows: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        sb.AppendLine($"- .NET: {RuntimeInformation.FrameworkDescription}");
        sb.AppendLine($"- Monitor: {DisplayInfo.PrimaryRefreshRate()} Hz");
        sb.AppendLine($"- Installationsort: {AppContext.BaseDirectory}");
        sb.AppendLine();
        sb.AppendLine("### Cadence");
        sb.AppendLine($"- Limiter: {(c.Enabled ? "an" : "aus")} · Overlay: {(c.OverlayEnabled ? "an" : "aus")}");
        sb.AppendLine($"- Profile: {App.Profiles.Games.Count} + Standard ({App.Profiles.Default.TargetFps:0} FPS)");
        var conflicts = c.HotkeyConflicts;
        sb.AppendLine($"- Tastenkürzel: {string.Join(", ", App.Profiles.Settings.Hotkeys.Select(h => $"{h.Action}={h}"))}" +
                      (conflicts.Count > 0 ? $" · belegt: {string.Join(", ", conflicts)}" : ""));

        var s = c.Active;
        if (s is null)
        {
            sb.AppendLine("- Aktives Spiel: keines");
        }
        else
        {
            sb.AppendLine($"- Aktives Spiel: {s.DisplayName} ({s.ExeName})");
            try
            {
                sb.AppendLine($"- Hook: {s.Link.HookState} · API: {s.Link.Api} · Frames: {s.Link.FrameCount}");
                var err = s.Link.LastError;
                if (!string.IsNullOrWhiteSpace(err)) sb.AppendLine($"- Letzter Hook-Fehler: {err}");
            }
            catch (ObjectDisposedException) { sb.AppendLine("- Hook: Spiel wurde gerade beendet"); }
            sb.AppendLine($"- Ziel: {s.Profile.TargetFps:0} FPS · Modus: {SessionController.ModeName(s.Profile.Mode)}");
            var (fps, lastMs, low1, _) = c.Live.Read();
            sb.AppendLine($"- Gemessen: {fps:0.0} FPS · {lastMs:0.00} ms · 1 % Low {low1:0.0}");
        }

        sb.AppendLine();
        sb.AppendLine("### Fehlerprotokoll (letzte Einträge)");
        sb.AppendLine("```");
        sb.AppendLine(ReadLogTail());
        sb.AppendLine("```");
        return sb.ToString();
    }

    private static string ReadLogTail()
    {
        try
        {
            if (!File.Exists(CrashReporter.LogPath)) return "keine Einträge";
            var lines = File.ReadAllLines(CrashReporter.LogPath);
            return lines.Length == 0 ? "keine Einträge" : string.Join(Environment.NewLine, lines.TakeLast(LogLines));
        }
        catch (Exception ex)
        {
            return "Protokoll nicht lesbar: " + ex.Message;
        }
    }

    /// <summary>Bericht in die Zwischenablage legen und ein neues Issue im Browser öffnen.</summary>
    public static void CopyAndOpenIssue()
    {
        var report = Build();
        var package = new DataPackage();
        package.SetText(report);
        Clipboard.SetContent(package);
        Clipboard.Flush(); // bleibt auch nach dem Beenden von Cadence erhalten

        var body = "**Was ist passiert?**\n\n\n**Was hast du erwartet?**\n\n\n" +
                   "**Diagnosebericht**\n_Der vollständige Bericht liegt in der Zwischenablage – bitte hier mit Strg + V einfügen._\n";
        var url = $"{UpdateService.RepoUrl}/issues/new?title={Uri.EscapeDataString("Problem: ")}&body={Uri.EscapeDataString(body)}";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}
