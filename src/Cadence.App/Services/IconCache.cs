using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace Cadence.App.Services;

/// <summary>Lädt Programm-Icons aus EXE-Dateien (über die Windows-Vorschau) und merkt sie sich.</summary>
public static class IconCache
{
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Muss auf dem UI-Thread aufgerufen werden. Liefert null, wenn kein Icon verfügbar ist.</summary>
    public static async Task<ImageSource?> GetAsync(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath)) return null;
        if (Cache.TryGetValue(exePath, out var cached)) return cached;

        ImageSource? result = null;
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(exePath);
            using var thumb = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, 64, ThumbnailOptions.UseCurrentScale);
            if (thumb is { Size: > 0 })
            {
                var bmp = new BitmapImage();
                await bmp.SetSourceAsync(thumb);
                result = bmp;
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log("Icon laden", ex);
        }
        Cache[exePath] = result;
        return result;
    }
}
