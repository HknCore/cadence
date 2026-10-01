using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;

namespace Cadence.App.Pages;

public sealed partial class SettingsPage : Page
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunName = "Cadence";
    private static string ProfileDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cadence");

    private bool _loading;

    public SettingsPage()
    {
        InitializeComponent();
        _loading = true;
        using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
            AutostartSwitch.IsOn = key?.GetValue(RunName) is string;
        _loading = false;

        HotkeyStatus.Text = (App.Controller.HotkeysAvailable
            ? ""
            : "Achtung: Einige Tastenkürzel sind bereits von einem anderen Programm belegt.\n") +
            "Strg + Alt + ↑ / ↓  Ziel-FPS in Stufen ändern (z. B. 45 → 60 → 72)\n" +
            "Strg + Alt + R  Aufnahme starten / stoppen\n" +
            "Strg + Alt + O  Overlay ein- / ausblenden";
        ProfilePath.Text = ProfileDir;
        AboutText.Text = $"Version {MainWindow.AppVersion} · " + AboutText.Text;
    }

    private void Autostart_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (AutostartSwitch.IsOn)
            key.SetValue(RunName, $"\"{Environment.ProcessPath}\" --minimized");
        else
            key.DeleteValue(RunName, throwOnMissingValue: false);
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(ProfileDir);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{ProfileDir}\"") { UseShellExecute = true });
    }
}
