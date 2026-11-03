using System.Runtime.InteropServices;
using Shadeplay.Interop;

namespace Shadeplay.Platform;

/// <summary>
/// Keeps the cursor on the game's monitor so a stray movement can't click another screen and
/// minimize a borderless game. Never overrides a cursor lock the game set itself.
/// </summary>
internal sealed class MouseLock : IDisposable
{
    const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;

    [DllImport("user32.dll")]
    static extern bool ClipCursor(ref RECT rect);

    [DllImport("user32.dll", EntryPoint = "ClipCursor")]
    static extern bool ReleaseClip(IntPtr none);

    [DllImport("user32.dll")]
    static extern bool GetClipCursor(out RECT rect);

    [DllImport("user32.dll")]
    static extern int GetSystemMetrics(int index);

    RECT? locked;

    public bool IsLocked => locked != null;

    /// <summary>Confines the cursor to <paramref name="area"/>, or frees it when null.</summary>
    public void Apply(RECT? area)
    {
        if (area is not RECT target)
        {
            Release();
            return;
        }

        GetClipCursor(out var current);
        if (locked is RECT ours && current.Equals(ours) && ours.Equals(target)) return;

        // Only take over when the cursor is free (clip = whole desktop) or still held by us.
        // A game that confines the cursor itself (to its window or its centre) keeps its own lock.
        bool free = current.Equals(VirtualScreen());
        bool stillOurs = locked is RECT previous && current.Equals(previous);
        if (!free && !stillOurs)
        {
            locked = null;
            return;
        }

        if (ClipCursor(ref target))
        {
            if (locked == null) Log.Write($"Mouse lock: cursor kept on {target}");
            locked = target;
        }
    }

    public void Release()
    {
        if (locked is not RECT ours) return;
        locked = null;
        // Leave the clip alone if someone else (the game) has replaced ours meanwhile
        GetClipCursor(out var current);
        if (!current.Equals(ours)) return;
        ReleaseClip(IntPtr.Zero);
        Log.Write("Mouse lock: cursor free");
    }

    static RECT VirtualScreen()
    {
        int x = GetSystemMetrics(SM_XVIRTUALSCREEN), y = GetSystemMetrics(SM_YVIRTUALSCREEN);
        return new RECT(x, y, x + GetSystemMetrics(SM_CXVIRTUALSCREEN), y + GetSystemMetrics(SM_CYVIRTUALSCREEN));
    }

    public void Dispose() => Release();
}
