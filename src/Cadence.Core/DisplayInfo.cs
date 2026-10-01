using System.Runtime.InteropServices;

namespace Cadence.Core;

/// <summary>Informationen zum Hauptbildschirm.</summary>
public static class DisplayInfo
{
    private const int ENUM_CURRENT_SETTINGS = -1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODEW
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettingsW(string? deviceName, int modeNum, ref DEVMODEW devMode);

    /// <summary>
    /// Aktuelle Bildwiederholrate des Hauptbildschirms in Hz (z. B. 144).
    /// Liefert 60, wenn Windows keinen Wert meldet.
    /// </summary>
    public static int PrimaryRefreshRate()
    {
        try
        {
            var mode = new DEVMODEW { dmSize = (ushort)Marshal.SizeOf<DEVMODEW>() };
            // null = Hauptbildschirm
            if (EnumDisplaySettingsW(null, ENUM_CURRENT_SETTINGS, ref mode) && mode.dmDisplayFrequency > 1)
                return (int)mode.dmDisplayFrequency;
        }
        catch
        {
            // ausserhalb von Windows oder Treiberproblem
        }
        return 60;
    }
}
