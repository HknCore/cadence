using System.Diagnostics;
using System.Text;
using Cadence.Core.Interop;

namespace Cadence.Core;

public sealed class InjectionException(string message) : Exception(message);

/// <summary>
/// Laedt die Hook-DLL per LoadLibraryW in einen Spielprozess.
/// Version 1 unterstuetzt nur 64-Bit-Spiele.
/// </summary>
public static class Injector
{
    public const string HookDllName = "CadenceHook.dll";

    public static string DefaultDllPath =>
        Path.Combine(AppContext.BaseDirectory, HookDllName);

    public static bool IsAlreadyLoaded(Process process)
    {
        try
        {
            foreach (ProcessModule m in process.Modules)
                if (string.Equals(m.ModuleName, HookDllName, StringComparison.OrdinalIgnoreCase))
                    return true;
        }
        catch
        {
            // Zugriff verweigert -> nicht pruefbar
        }
        return false;
    }

    public static void Inject(Process process, string? dllPath = null)
    {
        dllPath ??= DefaultDllPath;
        if (!File.Exists(dllPath))
            throw new InjectionException($"{HookDllName} wurde nicht gefunden: {dllPath}");

        const uint access = Native.PROCESS_CREATE_THREAD | Native.PROCESS_QUERY_INFORMATION |
                            Native.PROCESS_VM_OPERATION | Native.PROCESS_VM_WRITE | Native.PROCESS_VM_READ;

        var handle = Native.OpenProcess(access, false, (uint)process.Id);
        if (handle == IntPtr.Zero)
            throw new InjectionException("Kein Zugriff auf den Spielprozess. Läuft das Spiel als Administrator?");

        IntPtr remoteMem = IntPtr.Zero;
        try
        {
            if (Native.IsWow64Process2(handle, out var machine, out _) &&
                machine != Native.IMAGE_FILE_MACHINE_UNKNOWN)
                throw new InjectionException("32-Bit-Spiele werden in dieser Version noch nicht unterstützt.");

            var bytes = Encoding.Unicode.GetBytes(dllPath + "\0");
            remoteMem = Native.VirtualAllocEx(handle, IntPtr.Zero, (nuint)bytes.Length,
                Native.MEM_COMMIT | Native.MEM_RESERVE, Native.PAGE_READWRITE);
            if (remoteMem == IntPtr.Zero)
                throw new InjectionException("Speicher im Spielprozess konnte nicht reserviert werden.");

            if (!Native.WriteProcessMemory(handle, remoteMem, bytes, (nuint)bytes.Length, out _))
                throw new InjectionException("Pfad konnte nicht in den Spielprozess geschrieben werden.");

            // kernel32 liegt in allen Prozessen derselben Sitzung an derselben Adresse.
            var loadLibrary = Native.GetProcAddress(Native.GetModuleHandle("kernel32.dll"), "LoadLibraryW");
            var thread = Native.CreateRemoteThread(handle, IntPtr.Zero, 0, loadLibrary, remoteMem, 0, out _);
            if (thread == IntPtr.Zero)
                throw new InjectionException("Laden der DLL im Spiel fehlgeschlagen.");

            try
            {
                if (Native.WaitForSingleObject(thread, 10_000) != Native.WAIT_OBJECT_0)
                    throw new InjectionException("Zeitüberschreitung beim Laden der DLL.");
                // Exit-Code = untere 32 Bit des Modul-Handles; 0 bedeutet Fehler.
                if (Native.GetExitCodeThread(thread, out var code) && code == 0)
                    throw new InjectionException("Das Spiel hat das Laden der DLL abgelehnt.");
            }
            finally
            {
                Native.CloseHandle(thread);
            }
        }
        finally
        {
            if (remoteMem != IntPtr.Zero)
                Native.VirtualFreeEx(handle, remoteMem, 0, Native.MEM_RELEASE);
            Native.CloseHandle(handle);
        }
    }
}
