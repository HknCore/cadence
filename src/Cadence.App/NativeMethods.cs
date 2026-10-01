using System.Runtime.InteropServices;

namespace Cadence.App;

internal static partial class NativeMethods
{
    [LibraryImport("user32.dll")]
    public static partial IntPtr GetForegroundWindow();
}
