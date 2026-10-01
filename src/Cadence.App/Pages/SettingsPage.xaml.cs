using System.Diagnostics;
using Cadence.Core;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Win32;
using Windows.System;
using Windows.UI.Core;

namespace Cadence.App.Pages;

public sealed partial class SettingsPage : Page
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunName = "Cadence";
    private static string ProfileDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cadence");

    private static readonly (HotkeyAction Action, string Label)[] HotkeyLabels =
    [
        (HotkeyAction.TargetUp, "Ziel-FPS eine Stufe höher"),
        (HotkeyAction.TargetDown, "Ziel-FPS eine Stufe tiefer"),
        (HotkeyAction.ToggleRecording, "Aufnahme starten / stoppen"),
        (HotkeyAction.ToggleOverlay, "Overlay ein- / ausblenden"),
    ];

    private const string DefaultHotkeyHint = "Auf ein Kürzel klicken und die neue Tastenkombination drücken. Esc bricht ab, Entf löscht.";

    private bool _loading;
    private readonly Dictionary<HotkeyAction, Button> _hotkeyButtons = [];
    private HotkeyAction? _capturing;

    public SettingsPage()
    {
        InitializeComponent();
        _loading = true;
        using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
            AutostartSwitch.IsOn = key?.GetValue(RunName) is string;
        NotifySwitch.IsOn = App.Profiles.Settings.Notifications;
        _loading = false;

        ProfilePath.Text = ProfileDir;
        AboutText.Text = $"Version {MainWindow.AppVersion} · " + AboutText.Text;
        BuildHotkeyRows();

        PreviewKeyDown += OnPreviewKeyDown;
        Unloaded += (_, _) => StopCapture(save: false);
    }

    // ------------------------------------------------------------ Allgemein
    private void Autostart_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (AutostartSwitch.IsOn)
            key.SetValue(RunName, $"\"{Environment.ProcessPath}\" --minimized");
        else
            key.DeleteValue(RunName, throwOnMissingValue: false);
    }

    private void Notify_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.Profiles.Settings.Notifications = NotifySwitch.IsOn;
        App.Profiles.Save();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(ProfileDir);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{ProfileDir}\"") { UseShellExecute = true });
    }

    // ------------------------------------------------------------ Tastenkürzel
    private void BuildHotkeyRows()
    {
        HotkeyRows.Children.Clear();
        _hotkeyButtons.Clear();
        foreach (var (action, label) in HotkeyLabels)
        {
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });

            var button = new Button { MinWidth = 190, HorizontalContentAlignment = HorizontalAlignment.Center };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label);
            button.Click += (_, _) => StartCapture(action);
            Grid.SetColumn(button, 1);
            row.Children.Add(button);

            _hotkeyButtons[action] = button;
            HotkeyRows.Children.Add(row);
        }
        RefreshHotkeyTexts();
    }

    private void RefreshHotkeyTexts()
    {
        var conflicts = App.Controller.HotkeyConflicts;
        foreach (var (action, button) in _hotkeyButtons)
        {
            var binding = App.Profiles.Settings.Hotkey(action);
            button.Content = _capturing == action ? "Tasten drücken …" : binding.ToString();
            button.Style = _capturing == action ? (Style)Application.Current.Resources["AccentButtonStyle"] : null;
            if (conflicts.Contains(action) && _capturing is null)
                button.Content = binding + "  ·  belegt";
        }
        if (_capturing is null)
            HotkeyStatus.Text = conflicts.Count > 0
                ? "Mit „belegt“ markierte Kürzel nutzt bereits ein anderes Programm. Wähle eine andere Kombination."
                : DefaultHotkeyHint;
    }

    private void StartCapture(HotkeyAction action)
    {
        if (_capturing == action) { StopCapture(save: false); return; }
        if (_capturing is null) App.Controller.SuspendHotkeys(); // sonst faengt Windows bekannte Kombinationen ab
        _capturing = action;
        HotkeyStatus.Text = "Neue Kombination drücken, z. B. Strg + Alt + F. Esc bricht ab, Entf löscht das Kürzel.";
        RefreshHotkeyTexts();
        _hotkeyButtons[action].Focus(FocusState.Programmatic);
    }

    private void StopCapture(bool save, HotkeyBinding? binding = null)
    {
        if (_capturing is null) return;
        var action = _capturing.Value;
        _capturing = null;

        if (save && binding is not null)
        {
            var list = App.Profiles.Settings.Hotkeys.Select(h => h.Clone()).ToList();
            // Dieselbe Kombination darf nur eine Aktion haben – die alte Zuordnung wird geloescht.
            foreach (var other in list.Where(h => h.Action != action && h.SameKeys(binding)))
            {
                other.Modifiers = 0;
                other.Key = 0;
            }
            list.RemoveAll(h => h.Action == action);
            list.Add(new HotkeyBinding { Action = action, Modifiers = binding.Modifiers, Key = binding.Key });
            App.Controller.ApplyHotkeys(list); // speichert und registriert neu
        }
        else
        {
            App.Controller.ResumeHotkeys();
        }
        RefreshHotkeyTexts();
    }

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_capturing is null) return;
        e.Handled = true;
        var vk = (uint)e.Key;

        if (e.Key == VirtualKey.Escape) { StopCapture(save: false); return; }
        if (e.Key is VirtualKey.Delete or VirtualKey.Back)
        {
            StopCapture(save: true, new HotkeyBinding { Action = _capturing.Value });
            return;
        }
        if (!HotkeyBinding.IsAllowedKey(vk)) return; // nur Modifier gedrueckt – weiter warten

        uint mods = 0;
        if (IsDown(VirtualKey.Control)) mods |= HotkeyBinding.ModControl;
        if (IsDown(VirtualKey.Menu)) mods |= HotkeyBinding.ModAlt;
        if (IsDown(VirtualKey.Shift)) mods |= HotkeyBinding.ModShift;
        if (IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows)) mods |= HotkeyBinding.ModWin;

        var isFunctionKey = vk is >= 0x70 and <= 0x87;
        if (mods == 0 && !isFunctionKey)
        {
            HotkeyStatus.Text = "Bitte mit Strg, Alt, Umschalt oder Win kombinieren (F-Tasten gehen auch allein).";
            return;
        }

        StopCapture(save: true, new HotkeyBinding { Action = _capturing.Value, Modifiers = mods, Key = vk });
    }

    private void ResetHotkeys_Click(object sender, RoutedEventArgs e)
    {
        _capturing = null;
        App.Controller.ApplyHotkeys(HotkeyBinding.Defaults());
        RefreshHotkeyTexts();
    }
}
