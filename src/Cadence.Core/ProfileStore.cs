using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cadence.Core;

/// <summary>
/// Speichert Profile als JSON unter %LOCALAPPDATA%\Cadence\profiles.json.
/// </summary>
public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private sealed class FileModel
    {
        public GameProfile Default { get; set; } = new() { DisplayName = "Alle anderen Spiele", TargetFps = 120 };
        public List<GameProfile> Games { get; set; } = [];
        public OverlaySettings Overlay { get; set; } = new();
        public AppSettings Settings { get; set; } = new();
    }

    private readonly string _path;
    private FileModel _model = new();
    private readonly object _lock = new();

    public event EventHandler? Changed;

    public ProfileStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cadence", "profiles.json");
    }

    public GameProfile Default { get { lock (_lock) return _model.Default; } }

    public IReadOnlyList<GameProfile> Games { get { lock (_lock) return _model.Games.ToList(); } }

    /// <summary>Overlay-Einstellungen (global). Nach Aenderungen <see cref="Save"/> aufrufen.</summary>
    public OverlaySettings Overlay { get { lock (_lock) return _model.Overlay; } }

    /// <summary>App-Einstellungen (global). Nach Aenderungen <see cref="Save"/> aufrufen.</summary>
    public AppSettings Settings { get { lock (_lock) return _model.Settings; } }

    public void Load()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_path))
                    _model = JsonSerializer.Deserialize<FileModel>(File.ReadAllText(_path), JsonOptions) ?? new();
                _model.Settings ??= new();
                _model.Overlay ??= new();
                _model.Settings.Normalize();
                _model.Default.ApplyRefreshRate();
            }
            catch (JsonException)
            {
                // defekte Datei sichern statt zu ueberschreiben
                File.Copy(_path, _path + ".bak", overwrite: true);
                _model = new();
            }
        }
    }

    public void Save()
    {
        lock (_lock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_model, JsonOptions));
            File.Move(tmp, _path, overwrite: true);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public GameProfile? Find(string exeName)
    {
        lock (_lock)
            return _model.Games.FirstOrDefault(g => string.Equals(g.ExeName, exeName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Profil fuer die EXE oder das Standardprofil.</summary>
    public GameProfile Resolve(string exeName) => Find(exeName) ?? Default;

    public void Upsert(GameProfile profile)
    {
        lock (_lock)
        {
            if (profile.IsDefault) { _model.Default = profile; }
            else
            {
                var i = _model.Games.FindIndex(g => string.Equals(g.ExeName, profile.ExeName, StringComparison.OrdinalIgnoreCase));
                if (i >= 0) _model.Games[i] = profile; else _model.Games.Add(profile);
            }
        }
        Save();
    }

    public void Remove(string exeName)
    {
        lock (_lock)
            _model.Games.RemoveAll(g => string.Equals(g.ExeName, exeName, StringComparison.OrdinalIgnoreCase));
        Save();
    }
}
