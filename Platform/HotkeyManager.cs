using System.Windows.Input;
using System.Windows.Interop;
using MonitorDimmer.Interop;

namespace MonitorDimmer.Platform;

/// <summary>One system-wide hotkey, received on a hidden message window.</summary>
internal sealed class HotkeyManager : IDisposable
{
    const int HotkeyId = 1;
    static readonly IntPtr HWND_MESSAGE = new(-3);

    readonly HwndSource source;
    bool registered;

    public HotkeyManager()
    {
        source = new HwndSource(new HwndSourceParameters("MonitorDimmerHotkey") { ParentWindow = HWND_MESSAGE });
        source.AddHook(WndProc);
    }

    public event Action? Pressed;

    /// <returns>False when another app already uses this combination.</returns>
    public bool Register(uint modifiers, int virtualKey)
    {
        Unregister();
        registered = Native.RegisterHotKey(source.Handle, HotkeyId, modifiers | Native.MOD_NOREPEAT, (uint)virtualKey);
        return registered;
    }

    public void Unregister()
    {
        if (!registered) return;
        Native.UnregisterHotKey(source.Handle, HotkeyId);
        registered = false;
    }

    public static string Describe(uint modifiers, int virtualKey)
    {
        var parts = new List<string>();
        if ((modifiers & AppSettings.ModControl) != 0) parts.Add("Ctrl");
        if ((modifiers & AppSettings.ModAlt) != 0) parts.Add("Alt");
        if ((modifiers & AppSettings.ModShift) != 0) parts.Add("Shift");
        if ((modifiers & AppSettings.ModWin) != 0) parts.Add("Win");
        var key = KeyInterop.KeyFromVirtualKey(virtualKey);
        parts.Add(key switch
        {
            >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
            Key.OemPlus => "+",
            Key.OemMinus => "-",
            _ => key.ToString(),
        });
        return string.Join(" + ", parts);
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        source.RemoveHook(WndProc);
        source.Dispose();
    }
}
