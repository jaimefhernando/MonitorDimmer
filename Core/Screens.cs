using System.Runtime.InteropServices;
using Shadeplay.Interop;

namespace Shadeplay.Core;

/// <param name="Id">Stable id used to key per-monitor settings.</param>
/// <param name="Number">The number Windows shows in Display settings.</param>
public sealed record MonitorInfo(
    IntPtr Handle, string Id, string DeviceName, RECT Bounds, bool IsPrimary, int Number, string FriendlyName)
{
    public string Label =>
        $"Display {Number}" + (FriendlyName.Length > 0 ? $" ({FriendlyName})" : "");

    public string Description =>
        $"{Label}, {Bounds.Width}×{Bounds.Height}" + (IsPrimary ? ", primary" : "");
}

public static class Screens
{
    static string lastSignature = "";
    static Dictionary<string, DisplayConfig.MonitorName> names = new();

    public static List<MonitorInfo> GetAll()
    {
        var raw = new List<MONITORINFOEX>();
        var handles = new List<IntPtr>();
        Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr hdc, ref RECT r, IntPtr data) =>
        {
            var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (Native.GetMonitorInfo(h, ref mi))
            {
                raw.Add(mi);
                handles.Add(h);
            }
            return true;
        }, IntPtr.Zero);

        // Names only change when monitors are added, removed or rearranged
        string signature = string.Join("|", raw.Select(m => m.szDevice + m.rcMonitor));
        if (signature != lastSignature)
        {
            names = DisplayConfig.GetMonitorNames();
            lastSignature = signature;
        }

        var result = new List<MonitorInfo>(raw.Count);
        for (int i = 0; i < raw.Count; i++)
        {
            var mi = raw[i];
            names.TryGetValue(mi.szDevice, out var name);
            string friendly = name?.FriendlyName ?? "";
            if (friendly.Length == 0) friendly = AdapterDescription(mi.szDevice);
            string id = string.IsNullOrEmpty(name?.DevicePath) ? mi.szDevice : name.DevicePath;

            result.Add(new MonitorInfo(handles[i], id, mi.szDevice, mi.rcMonitor,
                (mi.dwFlags & Native.MONITORINFOF_PRIMARY) != 0, DisplayNumber(mi.szDevice, i), friendly));
        }
        return result.OrderBy(m => m.Number).ToList();
    }

    static int DisplayNumber(string deviceName, int fallbackIndex)
    {
        // "\\.\DISPLAY3" -> 3
        string digits = new(deviceName.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
        return int.TryParse(digits, out int n) ? n : fallbackIndex + 1;
    }

    static string AdapterDescription(string deviceName)
    {
        var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
        return Native.EnumDisplayDevices(deviceName, 0, ref dd, 0) ? dd.DeviceString : "";
    }
}
