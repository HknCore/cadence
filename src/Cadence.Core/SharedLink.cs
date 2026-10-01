using System.IO.MemoryMappedFiles;
using System.Text;
using System.Threading;
using Cadence.Core.Interop;

namespace Cadence.Core;

/// <summary>
/// Verbindung zur Hook-DLL in einem Spielprozess ueber gemeinsamen Speicher.
/// Die App legt den Speicher vor der Injektion an; die DLL oeffnet ihn per Namen.
/// </summary>
public sealed class SharedLink : IDisposable
{
    private readonly MemoryMappedFile _file;
    private readonly MemoryMappedViewAccessor _view;
    private bool _disposed;

    public int ProcessId { get; }

    private SharedLink(int pid, MemoryMappedFile file)
    {
        ProcessId = pid;
        _file = file;
        _view = file.CreateViewAccessor(0, SharedLayout.TotalSize, MemoryMappedFileAccess.ReadWrite);
    }

    public static SharedLink Create(int pid, GameProfile profile, bool enabled)
    {
        var name = SharedLayout.NameFor(pid);
        MemoryMappedFile file;
        try
        {
            file = MemoryMappedFile.CreateNew(name, SharedLayout.TotalSize);
        }
        catch (IOException)
        {
            // existiert bereits (z. B. App neu gestartet, DLL laeuft noch) -> wiederverwenden
            file = MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.ReadWrite);
        }

        var link = new SharedLink(pid, file);
        if (link._view.ReadUInt32(SharedLayout.OffMagic) != SharedLayout.Magic)
        {
            link._view.Write(SharedLayout.OffVersion, SharedLayout.Version);
            link._view.Write(SharedLayout.OffMagic, SharedLayout.Magic);
        }
        link.Apply(profile, enabled);
        link.Heartbeat();
        return link;
    }

    public void Apply(GameProfile profile, bool enabled)
    {
        TargetFps = profile.TargetFps;
        Mode = profile.Mode;
        Enabled = enabled;
    }

    public bool Enabled
    {
        get => _view.ReadInt32(SharedLayout.OffEnabled) != 0;
        set => _view.Write(SharedLayout.OffEnabled, value ? 1 : 0);
    }

    public LimiterMode Mode
    {
        get => (LimiterMode)_view.ReadInt32(SharedLayout.OffMode);
        set => _view.Write(SharedLayout.OffMode, (int)value);
    }

    public double TargetFps
    {
        get => _view.ReadDouble(SharedLayout.OffTargetFps);
        set => _view.Write(SharedLayout.OffTargetFps, value);
    }

    /// <summary>
    /// Lebenszeichen fuer die DLL. Bleibt es ueber 2 s aus (App beendet oder abgestuerzt),
    /// laeuft das Spiel automatisch wieder unbegrenzt.
    /// </summary>
    public void Heartbeat()
    {
        var now = (uint)Environment.TickCount64;
        _view.Write(SharedLayout.OffHeartbeat, now == 0 ? 1u : now);
    }

    public HookState HookState => (HookState)_view.ReadInt32(SharedLayout.OffHookState);
    public GraphicsApi Api => (GraphicsApi)_view.ReadInt32(SharedLayout.OffApiMask);
    public long FrameCount => _view.ReadInt64(SharedLayout.OffFrameCount);
    public uint WriteIndex => _view.ReadUInt32(SharedLayout.OffWriteIndex);

    public string LastError
    {
        get
        {
            var bytes = new byte[SharedLayout.LastErrorSize];
            _view.ReadArray(SharedLayout.OffLastError, bytes, 0, bytes.Length);
            var len = Array.IndexOf(bytes, (byte)0);
            return Encoding.UTF8.GetString(bytes, 0, len < 0 ? bytes.Length : len);
        }
    }

    /// <summary>Liest die letzten <paramref name="count"/> Frametimes (aelteste zuerst).</summary>
    public float[] ReadLatest(int count)
    {
        var end = WriteIndex;
        Thread.MemoryBarrier();
        count = (int)Math.Min((uint)Math.Min(count, SharedLayout.RingSize), end);
        var result = new float[count];
        for (var i = 0; i < count; i++)
        {
            var idx = (end - (uint)count + (uint)i) % SharedLayout.RingSize;
            result[i] = _view.ReadSingle(SharedLayout.OffFrametimes + (int)idx * 4);
        }
        return result;
    }

    /// <summary>
    /// Liest alle Frametimes seit <paramref name="cursor"/> und setzt den Cursor weiter.
    /// Gehen Werte verloren (mehr als RingSize seit dem letzten Aufruf), werden nur die neuesten geliefert.
    /// </summary>
    public float[] ReadSince(ref uint cursor)
    {
        var end = WriteIndex;
        Thread.MemoryBarrier();
        var available = end - cursor;
        if (available == 0) return Array.Empty<float>();
        if (available > SharedLayout.RingSize) { cursor = end - SharedLayout.RingSize; available = SharedLayout.RingSize; }

        var result = new float[available];
        for (uint i = 0; i < available; i++)
        {
            var idx = (cursor + i) % SharedLayout.RingSize;
            result[i] = _view.ReadSingle(SharedLayout.OffFrametimes + (int)idx * 4);
        }
        cursor = end;
        return result;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _view.Dispose();
        _file.Dispose();
    }
}
