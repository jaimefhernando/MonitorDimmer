using System.Diagnostics;
using System.IO;
using Shadeplay.Interop;

namespace Shadeplay.Core;

public sealed record WindowInfo(
    IntPtr Handle, int ProcessId, string ProcessName, string ExePath, string Title, RECT Bounds, IntPtr Monitor,
    bool IsMaximized, bool HasCaption)
{
    /// <summary>
    /// Covers its whole monitor. A maximized window with a title bar is not fullscreen,
    /// even when an auto-hiding taskbar lets it reach the screen edges.
    /// </summary>
    public bool IsFullscreenOn(MonitorInfo monitor) =>
        Bounds.Covers(monitor.Bounds) && !(IsMaximized && HasCaption);
}

public static class WindowScanner
{
    // Desktop, wallpaper and taskbar windows that cover a monitor without being an app
    static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
    };

    /// <summary>Visible, non-minimized top-level windows of other processes, topmost first.</summary>
    public static List<WindowInfo> Scan()
    {
        int ownPid = Environment.ProcessId;
        var result = new List<WindowInfo>();
        var processes = new Dictionary<uint, (string Name, string Path)>();

        Native.EnumWindows((h, _) =>
        {
            if (!Native.IsWindowVisible(h) || Native.IsIconic(h)) return true;

            Native.GetWindowThreadProcessId(h, out uint pid);
            if (pid == ownPid) return true;

            // Click-through and tool windows are overlays (NVIDIA, Steam, Xbox Game Bar...), not apps
            long exStyle = Native.GetWindowLongPtr(h, Native.GWL_EXSTYLE).ToInt64();
            if ((exStyle & (Native.WS_EX_TOOLWINDOW | Native.WS_EX_TRANSPARENT)) != 0) return true;

            if (Native.IsCloaked(h)) return true;
            if (ShellClasses.Contains(Native.GetClassName(h))) return true;
            if (!Native.TryGetVisibleBounds(h, out var bounds) || bounds.Width <= 0 || bounds.Height <= 0) return true;

            if (!processes.TryGetValue(pid, out var process))
            {
                string path = Native.GetProcessPath(pid);
                string name = path.Length > 0 ? Path.GetFileName(path).ToLowerInvariant() : ProcessName((int)pid);
                process = (name, path);
                processes[pid] = process;
            }
            if (process.Name.Length == 0) return true;

            long style = Native.GetWindowLongPtr(h, Native.GWL_STYLE).ToInt64();
            result.Add(new WindowInfo(h, (int)pid, process.Name, process.Path, Native.GetWindowText(h), bounds,
                Native.MonitorFromWindow(h, Native.MONITOR_DEFAULTTONEAREST),
                Native.IsZoomed(h), (style & Native.WS_CAPTION) == Native.WS_CAPTION));
            return true;
        }, IntPtr.Zero);

        return result;
    }

    static string ProcessName(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.ProcessName.ToLowerInvariant() + ".exe";
        }
        catch (Exception)
        {
            return ""; // exited between enumeration and lookup
        }
    }

    /// <summary>"VLC", "vlc.exe" and " Vlc.EXE " all become "vlc.exe".</summary>
    public static string NormalizeExe(string name)
    {
        name = name.Trim().ToLowerInvariant();
        return name.Length == 0 || name.EndsWith(".exe") ? name : name + ".exe";
    }
}
