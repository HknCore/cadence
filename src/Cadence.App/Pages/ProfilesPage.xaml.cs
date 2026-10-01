using System.Collections.ObjectModel;
using System.Diagnostics;
using Cadence.App.Services;
using Cadence.App.ViewModels;
using Cadence.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Cadence.App.Pages;

public sealed partial class ProfilesPage : Page
{
    public ObservableCollection<ProfileItem> Items { get; } = [];

    private readonly List<ProfileItem> _all = [];
    private ProfileItem? _selected;
    private bool _loading;

    public ProfilesPage()
    {
        InitializeComponent();
        Loaded += (_, _) => Reload();
    }

    private void Reload(string? selectExe = null)
    {
        _all.Clear();
        _all.Add(new ProfileItem(App.Profiles.Default));
        foreach (var g in App.Profiles.Games.OrderByDescending(g => g.LastPlayed ?? DateTimeOffset.MinValue))
            _all.Add(new ProfileItem(g));
        ApplyFilter();
        LoadIcons();

        var target = selectExe is null
            ? Items.FirstOrDefault()
            : Items.FirstOrDefault(i => string.Equals(i.Model.ExeName, selectExe, StringComparison.OrdinalIgnoreCase));
        ProfileList.SelectedItem = target;
    }

    /// <summary>
    /// Icons aus den EXE-Dateien nachladen. Profile von frueher kennen ihren Pfad noch nicht –
    /// laeuft das Spiel gerade, wird er nachgetragen.
    /// </summary>
    private async void LoadIcons()
    {
        var learned = false;
        foreach (var item in _all.Where(i => !i.IsDefault && string.IsNullOrEmpty(i.Model.ExePath)))
        {
            var name = Path.GetFileNameWithoutExtension(item.Model.ExeName);
            foreach (var proc in Process.GetProcessesByName(name))
            {
                using (proc)
                {
                    if (item.Model.ExePath is null && SessionController.TryGetExePath(proc) is { } path)
                    {
                        item.Model.ExePath = path;
                        learned = true;
                    }
                }
            }
        }
        if (learned) App.Profiles.Save();

        foreach (var item in _all.ToList())
        {
            if (item.IsDefault || item.Icon is not null) continue;
            item.Icon = await IconCache.GetAsync(item.Model.ExePath);
        }
    }

    private void ApplyFilter()
    {
        var q = Search.Text?.Trim() ?? "";
        Items.Clear();
        foreach (var item in _all)
            if (q.Length == 0 || item.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                item.Model.ExeName.Contains(q, StringComparison.OrdinalIgnoreCase))
                Items.Add(item);
    }

    private void Search_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) => ApplyFilter();

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selected = ProfileList.SelectedItem as ProfileItem;
        EmptyHint.Visibility = _selected is null ? Visibility.Visible : Visibility.Collapsed;
        Details.Visibility = _selected is null ? Visibility.Collapsed : Visibility.Visible;
        if (_selected is null) return;

        _loading = true;
        DetailTitle.Text = _selected.Title;
        DetailSubtitle.Text = _selected.Subtitle;
        NameBox.Text = _selected.DisplayName;
        NameBox.Visibility = _selected.IsEditableName ? Visibility.Visible : Visibility.Collapsed;
        RefreshSwitch.Header = $"An Bildwiederholrate anpassen ({DisplayInfo.PrimaryRefreshRate()} Hz)";
        RefreshSwitch.IsOn = _selected.MatchRefreshRate;
        FpsBox.Value = _selected.TargetFps;
        ModeBox.SelectedIndex = _selected.ModeIndex;
        AutoSwitch.IsOn = _selected.AutoApply;
        AutoSwitch.Visibility = _selected.IsDefault ? Visibility.Collapsed : Visibility.Visible;
        HotkeySwitch.IsOn = _selected.HotkeysEnabled;
        OverlaySwitch.IsOn = _selected.OverlayEnabled;
        DeleteButton.Visibility = _selected.IsDefault ? Visibility.Collapsed : Visibility.Visible;
        _loading = false;
    }

    private void NameBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading || _selected is null || string.IsNullOrWhiteSpace(NameBox.Text)) return;
        _selected.DisplayName = NameBox.Text.Trim();
        DetailTitle.Text = _selected.Title;
    }

    private void FpsBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading || _selected is null) return;
        _selected.TargetFps = args.NewValue;
        _loading = true;
        RefreshSwitch.IsOn = _selected.MatchRefreshRate; // Handeingabe schaltet die Automatik ab
        _loading = false;
    }

    private void ModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && _selected is not null) _selected.ModeIndex = ModeBox.SelectedIndex;
    }

    private void Switch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading || _selected is null) return;
        if (ReferenceEquals(sender, AutoSwitch)) _selected.AutoApply = AutoSwitch.IsOn;
        else if (ReferenceEquals(sender, HotkeySwitch)) _selected.HotkeysEnabled = HotkeySwitch.IsOn;
        else if (ReferenceEquals(sender, RefreshSwitch))
        {
            _selected.MatchRefreshRate = RefreshSwitch.IsOn;
            _loading = true;
            FpsBox.Value = _selected.TargetFps;
            _loading = false;
        }
        else if (ReferenceEquals(sender, OverlaySwitch)) _selected.OverlayEnabled = OverlaySwitch.IsOn;
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _selected.IsDefault) return;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Profil löschen?",
            Content = $"Das Profil für {_selected.Title} wird entfernt. Das Spiel nutzt danach das Standardprofil.",
            PrimaryButtonText = "Löschen",
            CloseButtonText = "Abbrechen",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        App.Profiles.Remove(_selected.Model.ExeName);
        Reload();
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        // Laufende Programme mit Fenster anbieten (Spiele muessen dafuer gestartet sein).
        var own = Environment.ProcessId;
        var candidates = Process.GetProcesses()
            .Where(p => p.Id != own && p.MainWindowHandle != IntPtr.Zero && p.MainWindowTitle.Length > 0)
            .OrderBy(p => p.MainWindowTitle)
            .ToList();

        var list = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            MaxHeight = 360,
            ItemsSource = candidates.Select(p => $"{GameProfile.CleanName(p.MainWindowTitle)} · {p.ProcessName}.exe").ToList(),
        };
        var panel = new StackPanel { Spacing = 12, MinWidth = 420 };
        panel.Children.Add(new TextBlock
        {
            Text = "Starte das Spiel und wähle es hier aus. Cadence merkt sich die EXE und begrenzt sie künftig automatisch.",
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(list);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Profil hinzufügen",
            Content = panel,
            PrimaryButtonText = "Hinzufügen",
            CloseButtonText = "Abbrechen",
            DefaultButton = ContentDialogButton.Primary,
        };
        list.SelectionChanged += (_, _) => dialog.IsPrimaryButtonEnabled = list.SelectedIndex >= 0;
        dialog.IsPrimaryButtonEnabled = false;

        if (await dialog.ShowAsync() != ContentDialogResult.Primary || list.SelectedIndex < 0) return;

        var proc = candidates[list.SelectedIndex];
        var exe = proc.ProcessName + ".exe";
        var profile = App.Profiles.Find(exe) ?? new GameProfile
        {
            ExeName = exe,
            DisplayName = GameProfile.CleanName(proc.MainWindowTitle),
            ExePath = SessionController.TryGetExePath(proc),
            TargetFps = App.Profiles.Default.TargetFps,
            Mode = App.Profiles.Default.Mode,
        };
        profile.ExePath ??= SessionController.TryGetExePath(proc);
        App.Profiles.Upsert(profile);

        // Blockiert/Fehler meldet das Hauptfenster selbst ueber die Controller-Events.
        App.Controller.Attach(proc, profile);

        foreach (var p in candidates.Where(p => p != proc)) p.Dispose();
        Reload(exe);
    }
}
