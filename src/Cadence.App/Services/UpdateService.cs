using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Cadence.App.Services;

public sealed record UpdateInfo(Version Version, string Tag, string? InstallerUrl, string PageUrl);

public sealed record UpdateCheck(bool Success, UpdateInfo? Newer, string? Error);

/// <summary>Fragt den neuesten Release auf GitHub ab und installiert ihn auf Wunsch.</summary>
public static class UpdateService
{
    public const string RepoUrl = "https://github.com/HknCore/cadence";
    private const string LatestApi = "https://api.github.com/repos/HknCore/cadence/releases/latest";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Cadence", MainWindow.AppVersion is { Length: > 0 } v ? v : "0"));
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return c;
    }

    public static Version Current
    {
        get
        {
            var v = typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    public static async Task<UpdateCheck> CheckAsync()
    {
        try
        {
            using var resp = await Http.GetAsync(LatestApi).ConfigureAwait(false);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                return new(false, null, "Keine Releases gefunden (oder das Repository ist privat).");
            if (!resp.IsSuccessStatusCode)
                return new(false, null, $"GitHub antwortet mit {(int)resp.StatusCode}.");

            await using var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
            var root = doc.RootElement;
            var tag = root.GetProperty("tag_name").GetString() ?? "";
            var page = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? RepoUrl : RepoUrl;

            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest))
                return new(false, null, $"Unbekanntes Versionsformat: {tag}");
            latest = new Version(latest.Major, latest.Minor, Math.Max(0, latest.Build));

            string? installer = null;
            if (root.TryGetProperty("assets", out var assets))
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.GetProperty("name").GetString() ?? "";
                    if (name.StartsWith("Cadence-Setup", StringComparison.OrdinalIgnoreCase) &&
                        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        installer = a.GetProperty("browser_download_url").GetString();
                }

            return latest > Current
                ? new(true, new UpdateInfo(latest, tag, installer, page), null)
                : new(true, null, null);
        }
        catch (Exception ex)
        {
            CrashReporter.Log("Update-Prüfung", ex);
            return new(false, null, "Keine Verbindung zu GitHub.");
        }
    }

    /// <summary>Lädt den Installer herunter und startet ihn. Danach beendet sich Cadence.</summary>
    public static async Task InstallAsync(UpdateInfo info, IProgress<double>? progress = null)
    {
        if (info.InstallerUrl is null)
        {
            Process.Start(new ProcessStartInfo(info.PageUrl) { UseShellExecute = true });
            return;
        }

        var target = Path.Combine(Path.GetTempPath(), $"Cadence-Setup-{info.Version}.exe");
        using (var resp = await Http.GetAsync(info.InstallerUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? 0;
            await using var src = await resp.Content.ReadAsStreamAsync();
            await using var dst = File.Create(target);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await src.ReadAsync(buffer)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read));
                done += read;
                if (total > 0) progress?.Report((double)done / total);
            }
        }

        // /SP- ueberspringt die Rueckfrage "Soll ... installiert werden?". Der Assistent bleibt sichtbar
        // und startet Cadence am Ende wieder.
        Process.Start(new ProcessStartInfo(target, "/SP-") { UseShellExecute = true });
    }
}
