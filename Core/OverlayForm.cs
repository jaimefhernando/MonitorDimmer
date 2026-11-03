using System.Drawing;
using System.Windows.Forms;
using Shadeplay.Interop;

namespace Shadeplay.Core;

/// <summary>
/// Black, click-through, always-on-top window covering one monitor. Its opacity is the dim
/// strength; exception windows are cut out of it so they stay fully visible.
/// </summary>
internal sealed class OverlayForm : Form
{
    // Opacity 1.0 makes WinForms drop the layered style, which hides the window
    const double MaxOpacity = 0.99;

    readonly System.Windows.Forms.Timer fadeTimer = new() { Interval = 15 };
    RECT bounds;
    bool captureExcluded;
    string holesKey = "";

    double fadeFrom, fadeTo = -1;
    DateTime fadeStart;
    int fadeMs;
    Action? fadeDone;

    public OverlayForm(RECT monitorBounds, bool hideFromCapture)
    {
        bounds = monitorBounds;
        captureExcluded = hideFromCapture;
        FormBorderStyle = FormBorderStyle.None;
        BackColor = Color.Black;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        TopMost = true;
        Opacity = 0;
        Bounds = new Rectangle(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
        Text = "Shadeplay overlay";
        fadeTimer.Tick += (_, _) => StepFade();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW
                | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyCaptureAffinity();
        ApplyBounds();
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case Native.WM_MOUSEACTIVATE:
                m.Result = new IntPtr(Native.MA_NOACTIVATE);
                return;
            case Native.WM_DPICHANGED:
                // Windows suggests a rescaled size when crossing monitors; the overlay always
                // matches the monitor in pixels instead
                ApplyBounds();
                return;
        }
        base.WndProc(ref m);
    }

    public void Place(RECT monitorBounds)
    {
        if (monitorBounds.Equals(bounds)) return;
        bounds = monitorBounds;
        holesKey = "";
        ApplyBounds();
    }

    public void SetCaptureExcluded(bool excluded)
    {
        if (excluded == captureExcluded) return;
        captureExcluded = excluded;
        ApplyCaptureAffinity();
    }

    /// <summary>Keeps above windows that became topmost after the overlay.</summary>
    public void KeepOnTop()
    {
        if (IsHandleCreated)
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
    }

    /// <param name="holes">Screen rectangles to leave undimmed.</param>
    public void SetHoles(IReadOnlyList<RECT> holes)
    {
        // A region set before the window exists is not applied to it
        if (!IsHandleCreated) return;
        string key = string.Join(";", holes);
        if (key == holesKey) return;
        holesKey = key;
        var old = Region;
        if (holes.Count == 0)
        {
            Region = null;
        }
        else
        {
            var region = new Region(new Rectangle(0, 0, bounds.Width, bounds.Height));
            foreach (var h in holes)
                region.Exclude(new Rectangle(h.Left - bounds.Left, h.Top - bounds.Top, h.Width, h.Height));
            Region = region;
        }
        old?.Dispose();
    }

    /// <summary>
    /// Fades to <paramref name="opacity"/> (0..1). Asking again for the target already being
    /// faded to does nothing, so this can be called on every update.
    /// </summary>
    public void FadeTo(double opacity, int durationMs, Action? done = null)
    {
        opacity = Math.Clamp(opacity, 0, MaxOpacity);
        if (Math.Abs(opacity - fadeTo) < 0.001) return;

        fadeFrom = Opacity;
        fadeTo = opacity;
        fadeMs = durationMs;
        fadeStart = DateTime.UtcNow;
        fadeDone = done;
        if (!Visible) Show();
        StepFade();
        if (fadeDone != null || Math.Abs(Opacity - fadeTo) > 0.001) fadeTimer.Start();
    }

    void StepFade()
    {
        double t = fadeMs <= 0 ? 1 : Math.Min(1, (DateTime.UtcNow - fadeStart).TotalMilliseconds / fadeMs);
        Opacity = fadeFrom + (fadeTo - fadeFrom) * t;
        if (t < 1) return;

        fadeTimer.Stop();
        var done = fadeDone;
        fadeDone = null;
        done?.Invoke();
    }

    void ApplyBounds()
    {
        if (IsHandleCreated)
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, bounds.Left, bounds.Top, bounds.Width, bounds.Height,
                Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
    }

    void ApplyCaptureAffinity()
    {
        if (IsHandleCreated)
            Native.SetWindowDisplayAffinity(Handle, captureExcluded ? Native.WDA_EXCLUDEFROMCAPTURE : Native.WDA_NONE);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            fadeTimer.Dispose();
            Region?.Dispose();
        }
        base.Dispose(disposing);
    }
}
