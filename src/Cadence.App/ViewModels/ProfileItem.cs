using Cadence.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Cadence.App.ViewModels;

/// <summary>Bearbeitbare Ansicht eines Profils; jede Aenderung wird sofort gespeichert und angewendet.</summary>
public sealed class ProfileItem : ObservableObject
{
    public GameProfile Model { get; }

    public ProfileItem(GameProfile model) => Model = model;

    public bool IsDefault => Model.IsDefault;
    public bool IsEditableName => !Model.IsDefault;

    public string Title => Model.IsDefault ? "Alle anderen Spiele" : Model.DisplayName;
    public string Subtitle => Model.IsDefault ? "Standard, wenn ein Spiel kein eigenes Profil hat" : Model.ExeName;
    /// <summary>Programm-Icon aus der EXE; wird nachgeladen, bis dahin stehen die Initialen.</summary>
    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            if (ReferenceEquals(_icon, value)) return;
            _icon = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(InitialsVisibility));
        }
    }
    private ImageSource? _icon;

    public string Initials
    {
        get
        {
            if (Model.IsDefault) return "";
            var words = (Model.DisplayName.Length > 0 ? Model.DisplayName : Path.GetFileNameWithoutExtension(Model.ExeName))
                .Split([' ', '-', '_', ':', '.'], StringSplitOptions.RemoveEmptyEntries)
                .Where(w => char.IsLetterOrDigit(w[0]))
                .ToArray();
            return words.Length switch
            {
                0 => "?",
                1 => words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant(),
                _ => $"{char.ToUpperInvariant(words[0][0])}{char.ToUpperInvariant(words[1][0])}",
            };
        }
    }

    public Visibility InitialsVisibility => _icon is null && !IsDefault ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DefaultGlyphVisibility => IsDefault ? Visibility.Visible : Visibility.Collapsed;

    public string Summary => $"{Model.TargetFps:0} FPS{(Model.FollowsRefreshRate ? " (Monitor)" : "")} · {ModeName(Model.Mode)}";

    public string DisplayName
    {
        get => Model.DisplayName;
        set { if (Model.DisplayName == value || IsDefault) return; Model.DisplayName = value; OnPropertyChanged(nameof(Initials)); Changed(nameof(Title)); }
    }

    public double TargetFps
    {
        get => Model.TargetFps;
        set
        {
            if (double.IsNaN(value)) return;
            value = Math.Round(Math.Clamp(value, 10, 1000));
            if (Model.TargetFps == value) return;
            Model.TargetFps = value;
            if (Model.FollowsRefreshRate) { Model.MatchRefreshRate = false; OnPropertyChanged(nameof(MatchRefreshRate)); }
            Changed(nameof(Summary));
        }
    }

    public int ModeIndex
    {
        get => (int)Model.Mode;
        set { if (value < 0 || (int)Model.Mode == value) return; Model.Mode = (LimiterMode)value; Changed(nameof(Summary)); }
    }

    public bool MatchRefreshRate
    {
        get => Model.FollowsRefreshRate;
        set
        {
            if (Model.FollowsRefreshRate == value) return;
            Model.MatchRefreshRate = value;
            Model.ApplyRefreshRate();
            OnPropertyChanged(nameof(TargetFps));
            Changed(nameof(Summary));
        }
    }

    public bool AutoApply
    {
        get => Model.AutoApply;
        set { if (Model.AutoApply == value) return; Model.AutoApply = value; Changed(); }
    }

    public bool OverlayEnabled
    {
        get => Model.OverlayEnabled;
        set { if (Model.OverlayEnabled == value) return; Model.OverlayEnabled = value; Changed(); }
    }

    public bool HotkeysEnabled
    {
        get => Model.HotkeysEnabled;
        set { if (Model.HotkeysEnabled == value) return; Model.HotkeysEnabled = value; Changed(); }
    }

    private void Changed(string? extra = null, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        OnPropertyChanged(name);
        if (extra is not null) OnPropertyChanged(extra);
        App.Controller.ProfileEdited(Model);
    }

    public static string ModeName(LimiterMode m) => m switch
    {
        LimiterMode.LowLatency => "Niedrige Latenz",
        LimiterMode.Smooth => "Max. Glätte",
        _ => "Ausgewogen",
    };
}
