using Cadence.Core;
using Microsoft.Win32;
using Windows.ApplicationModel;

namespace Cadence.App.Services;

/// <summary>
/// „Mit Windows starten“: normal über den Run-Schlüssel, in der Store-Version über die Startaufgabe des Pakets
/// (der Run-Schlüssel wirkt dort nicht).
/// </summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunName = "Cadence";
    public const string TaskId = "CadenceStartup";

    public sealed record State(bool Enabled, bool Locked, string? Hint);

    public static async Task<State> GetAsync()
    {
        if (!AppEnvironment.IsPackaged)
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return new State(key?.GetValue(RunName) is string, false, null);
        }
        var task = await StartupTask.GetAsync(TaskId);
        return Describe(task.State);
    }

    public static async Task<State> SetAsync(bool enabled)
    {
        if (!AppEnvironment.IsPackaged)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled) key.SetValue(RunName, $"\"{Environment.ProcessPath}\" --minimized");
            else key.DeleteValue(RunName, throwOnMissingValue: false);
            return new State(enabled, false, null);
        }
        var task = await StartupTask.GetAsync(TaskId);
        if (enabled) return Describe(await task.RequestEnableAsync());
        task.Disable();
        return Describe(task.State);
    }

    private static State Describe(StartupTaskState s) => s switch
    {
        StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy => new State(true, s == StartupTaskState.EnabledByPolicy, null),
        StartupTaskState.DisabledByUser => new State(false, true, "Im Task-Manager unter „Autostart-Apps“ deaktiviert. Dort wieder einschalten."),
        StartupTaskState.DisabledByPolicy => new State(false, true, "Von einer Richtlinie deines Systems deaktiviert."),
        _ => new State(false, false, null),
    };

    /// <summary>true, wenn Windows Cadence beim Anmelden gestartet hat (dann direkt in den Tray).</summary>
    public static bool LaunchedAtStartup()
    {
        if (Environment.GetCommandLineArgs().Contains("--minimized")) return true;
        if (!AppEnvironment.IsPackaged) return false;
        try
        {
            return Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs().Kind
                   == Microsoft.Windows.AppLifecycle.ExtendedActivationKind.StartupTask;
        }
        catch
        {
            return false;
        }
    }
}
