namespace Cadence.Core.Interop;

/// <summary>
/// Feste Offsets im gemeinsamen Speicher. MUSS mit
/// src/Cadence.Hook/include/cadence_shared.h uebereinstimmen.
/// </summary>
internal static class SharedLayout
{
    public const uint Magic = 0x434E4443; // "CDNC"
    public const uint Version = 1;
    public const int RingSize = 4096;

    public const int OffMagic = 0;
    public const int OffVersion = 4;
    public const int OffEnabled = 8;
    public const int OffMode = 12;
    public const int OffTargetFps = 16;
    public const int OffHeartbeat = 24;
    public const int OffHookState = 32;
    public const int OffApiMask = 36;
    public const int OffFrameCount = 40;
    public const int OffWriteIndex = 48;
    public const int OffFrametimes = 56;
    public const int OffLastError = 16440;
    public const int LastErrorSize = 256;
    public const int TotalSize = 16696;

    public static string NameFor(int pid) => $"Local\\Cadence_{pid}";
}
