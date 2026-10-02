using System.Runtime.InteropServices;
using System.Text;
using Cadence.Core;

namespace Cadence.App;

/// <summary>
/// Schreibt unerwartete Fehler nach %LOCALAPPDATA%\Cadence\crash.log und zeigt beim Start
/// eine Meldung, statt dass Cadence kommentarlos verschwindet.
/// </summary>
internal static partial class CrashReporter
{
    public static string LogPath { get; } = Path.Combine(AppEnvironment.DataDirectory, "crash.log");

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBox(IntPtr hwnd, string text, string caption, uint type);

    private const uint MB_ICONERROR = 0x10;

    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) Log("Unbehandelter Fehler", ex);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log("Unbeobachteter Task-Fehler", e.Exception);
            e.SetObserved();
        };
    }

    public static void Log(string context, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            var sb = new StringBuilder()
                .AppendLine($"==== {DateTime.Now:yyyy-MM-dd HH:mm:ss} · {context} ====")
                .AppendLine($"Cadence {MainWindow.AppVersion} · {Environment.OSVersion} · {AppContext.BaseDirectory}")
                .AppendLine(ex.ToString())
                .AppendLine();
            File.AppendAllText(LogPath, sb.ToString());
        }
        catch
        {
            // Protokollieren darf selbst nie fehlschlagen.
        }
    }

    /// <summary>Fehler beim Start: protokollieren, sichtbar melden, beenden.</summary>
    public static void FailStartup(Exception ex)
    {
        Log("Fehler beim Start", ex);
        MessageBox(IntPtr.Zero,
            $"Cadence konnte nicht gestartet werden.\n\n{ex.Message}\n\nDetails stehen in:\n{LogPath}",
            "Cadence", MB_ICONERROR);
        Environment.Exit(1);
    }
}
