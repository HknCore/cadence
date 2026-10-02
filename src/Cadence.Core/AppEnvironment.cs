using System.Runtime.InteropServices;

namespace Cadence.Core;

/// <summary>
/// Unterschiede zwischen der normalen Installation (Setup von GitHub) und der Version aus dem Microsoft Store.
/// Die Store-Version läuft als MSIX-Paket: Updates kommen vom Store, Daten liegen im Paketordner.
/// </summary>
public static partial class AppEnvironment
{
    private const int APPMODEL_ERROR_NO_PACKAGE = 15700;

    [LibraryImport("kernel32.dll")]
    private static partial int GetCurrentPackageFullName(ref uint packageFullNameLength, IntPtr packageFullName);

    [LibraryImport("kernel32.dll")]
    private static unsafe partial int GetCurrentPackageFamilyName(ref uint packageFamilyNameLength, char* packageFamilyName);

    /// <summary>true, wenn Cadence aus dem Microsoft Store (als Paket) läuft.</summary>
    public static bool IsPackaged { get; } = DetectPackaged();

    private static bool DetectPackaged()
    {
        try
        {
            uint length = 0;
            return GetCurrentPackageFullName(ref length, IntPtr.Zero) != APPMODEL_ERROR_NO_PACKAGE;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Ordner für Profile, Einstellungen und Protokoll.
    /// Im Paket der echte Pfad im Paket-Cache – Windows würde %LOCALAPPDATA% sonst unsichtbar dorthin umleiten,
    /// und „Ordner öffnen“ zeigte einen leeren Ordner.
    /// </summary>
    public static string DataDirectory { get; } = ResolveDataDirectory();

    private static string ResolveDataDirectory()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (IsPackaged && PackageFamilyName() is { } family)
            return Path.Combine(local, "Packages", family, "LocalCache", "Cadence");
        return Path.Combine(local, "Cadence");
    }

    private static unsafe string? PackageFamilyName()
    {
        try
        {
            uint length = 0;
            GetCurrentPackageFamilyName(ref length, null);
            if (length == 0) return null;
            var buffer = new char[length];
            fixed (char* p = buffer)
                if (GetCurrentPackageFamilyName(ref length, p) != 0) return null;
            return new string(buffer, 0, (int)length - 1);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Pfad der Hook-DLL, den das Spiel laden soll. Im Paket wird sie in den Datenordner kopiert,
    /// weil Spiele den geschützten Store-Ordner (WindowsApps) nicht in jedem Fall lesen dürfen.
    /// Pro Version ein eigener Ordner: eine geladene DLL lässt sich nicht überschreiben.
    /// </summary>
    public static string HookDllPath(string version)
    {
        var source = Path.Combine(AppContext.BaseDirectory, Injector.HookDllName);
        if (!IsPackaged) return source;
        try
        {
            var dir = Path.Combine(DataDirectory, "hook", version);
            var target = Path.Combine(dir, Injector.HookDllName);
            if (!File.Exists(target) || new FileInfo(target).Length != new FileInfo(source).Length)
            {
                Directory.CreateDirectory(dir);
                File.Copy(source, target, overwrite: true);
            }
            return target;
        }
        catch
        {
            return source;
        }
    }
}
