using System.Runtime.InteropServices;

namespace MonitorDimmer.Interop;

/// <summary>
/// Reads the monitor's own name ("LG ULTRAGEAR") and a device path that stays the same
/// across reboots, which \\.\DISPLAYn does not.
/// </summary>
internal static class DisplayConfig
{
    public sealed record MonitorName(string FriendlyName, string DevicePath);

    const uint QDC_ONLY_ACTIVE_PATHS = 2;
    const uint DEVICE_INFO_GET_SOURCE_NAME = 1;
    const uint DEVICE_INFO_GET_TARGET_NAME = 2;

    [StructLayout(LayoutKind.Sequential)]
    struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    struct PATH_SOURCE_INFO { public LUID adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }

    [StructLayout(LayoutKind.Sequential)]
    struct PATH_TARGET_INFO
    {
        public LUID adapterId; public uint id; public uint modeInfoIdx; public uint outputTechnology;
        public uint rotation; public uint scaling; public uint refreshNumerator; public uint refreshDenominator;
        public uint scanLineOrdering; public int targetAvailable; public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PATH_INFO { public PATH_SOURCE_INFO source; public PATH_TARGET_INFO target; public uint flags; }

    // Only the size matters here; the mode data itself is not read
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    struct MODE_INFO { [FieldOffset(0)] public uint infoType; }

    [StructLayout(LayoutKind.Sequential)]
    struct DEVICE_INFO_HEADER { public uint type; public uint size; public LUID adapterId; public uint id; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SOURCE_DEVICE_NAME
    {
        public DEVICE_INFO_HEADER header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct TARGET_DEVICE_NAME
    {
        public DEVICE_INFO_HEADER header;
        public uint flags; public uint outputTechnology; public ushort edidManufactureId; public ushort edidProductCodeId;
        public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
    }

    [DllImport("user32.dll")]
    static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPaths, out uint numModes);

    [DllImport("user32.dll")]
    static extern int QueryDisplayConfig(uint flags, ref uint numPaths, [Out] PATH_INFO[] paths,
        ref uint numModes, [Out] MODE_INFO[] modes, IntPtr topology);

    [DllImport("user32.dll")]
    static extern int DisplayConfigGetDeviceInfo(ref SOURCE_DEVICE_NAME info);

    [DllImport("user32.dll")]
    static extern int DisplayConfigGetDeviceInfo(ref TARGET_DEVICE_NAME info);

    /// <summary>Maps GDI device names (\\.\DISPLAY1) to monitor names. Empty on failure.</summary>
    public static Dictionary<string, MonitorName> GetMonitorNames()
    {
        var result = new Dictionary<string, MonitorName>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint numPaths, out uint numModes) != 0)
                return result;
            var paths = new PATH_INFO[numPaths];
            var modes = new MODE_INFO[numModes];
            if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref numPaths, paths, ref numModes, modes, IntPtr.Zero) != 0)
                return result;

            for (int i = 0; i < numPaths; i++)
            {
                var source = new SOURCE_DEVICE_NAME
                {
                    header = new DEVICE_INFO_HEADER
                    {
                        type = DEVICE_INFO_GET_SOURCE_NAME,
                        size = (uint)Marshal.SizeOf<SOURCE_DEVICE_NAME>(),
                        adapterId = paths[i].source.adapterId,
                        id = paths[i].source.id,
                    },
                };
                if (DisplayConfigGetDeviceInfo(ref source) != 0) continue;

                var target = new TARGET_DEVICE_NAME
                {
                    header = new DEVICE_INFO_HEADER
                    {
                        type = DEVICE_INFO_GET_TARGET_NAME,
                        size = (uint)Marshal.SizeOf<TARGET_DEVICE_NAME>(),
                        adapterId = paths[i].target.adapterId,
                        id = paths[i].target.id,
                    },
                };
                if (DisplayConfigGetDeviceInfo(ref target) != 0) continue;

                result[source.viewGdiDeviceName] = new MonitorName(
                    target.monitorFriendlyDeviceName ?? "", target.monitorDevicePath ?? "");
            }
        }
        catch (Exception ex)
        {
            Log.Write("DisplayConfig failed: " + ex.Message);
        }
        return result;
    }
}
