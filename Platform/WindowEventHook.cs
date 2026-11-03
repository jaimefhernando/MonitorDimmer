using System.Runtime.InteropServices;

namespace Shadeplay.Platform;

/// <summary>
/// Tells the app when top-level windows change, so dimming reacts right away instead of waiting
/// for the next check. Windows calls back on the thread that created the hook (the UI thread).
/// </summary>
internal sealed class WindowEventHook : IDisposable
{
    const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016;
    const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;
    const uint EVENT_OBJECT_DESTROY = 0x8001;
    const uint EVENT_OBJECT_HIDE = 0x8003; // DESTROY, SHOW and HIDE are consecutive
    const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    const uint EVENT_OBJECT_CLOAKED = 0x8017;
    const uint EVENT_OBJECT_UNCLOAKED = 0x8018;

    const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    // Our own overlays moving or fading must not trigger another update
    const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

    const int OBJID_WINDOW = 0;
    const int CHILDID_SELF = 0;
    const uint GA_ROOT = 2;

    delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild,
        uint eventThread, uint eventTime);

    [DllImport("user32.dll")]
    static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmod, WinEventProc callback,
        uint idProcess, uint idThread, uint flags);

    [DllImport("user32.dll")]
    static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll")]
    static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    // Windows holds only a raw pointer to the callback; this field keeps the delegate alive
    readonly WinEventProc callback;
    readonly List<IntPtr> hooks = new();

    public WindowEventHook()
    {
        callback = OnEvent;
        (uint Min, uint Max)[] ranges =
        {
            (EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND),
            (EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZEEND),
            (EVENT_OBJECT_DESTROY, EVENT_OBJECT_HIDE),
            (EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE),
            (EVENT_OBJECT_CLOAKED, EVENT_OBJECT_UNCLOAKED),
        };
        foreach (var (min, max) in ranges)
        {
            IntPtr hook = SetWinEventHook(min, max, IntPtr.Zero, callback, 0, 0,
                WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
            if (hook != IntPtr.Zero) hooks.Add(hook);
            else Log.Write($"Window event hook 0x{min:X}-0x{max:X} could not be set; the regular check still runs");
        }
    }

    /// <summary>A top-level window appeared, closed, moved, resized, minimized or got focus.</summary>
    public event Action? WindowChanged;

    void OnEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        // Location changes also fire for the mouse cursor, carets and controls inside windows
        if (idObject != OBJID_WINDOW || idChild != CHILDID_SELF || hwnd == IntPtr.Zero) return;
        // A destroyed window has no ancestor any more, so only check the others
        if (eventType != EVENT_OBJECT_DESTROY && GetAncestor(hwnd, GA_ROOT) != hwnd) return;
        WindowChanged?.Invoke();
    }

    public void Dispose()
    {
        foreach (IntPtr hook in hooks) UnhookWinEvent(hook);
        hooks.Clear();
    }
}
