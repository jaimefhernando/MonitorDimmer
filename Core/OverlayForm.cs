using System.Drawing;
using System.Windows.Forms;
using Shadeplay.Interop;

namespace Shadeplay.Core;

/// <summary>What the quiet side display shows.</summary>
/// <param name="Level">How bright the text looks on screen, in percent of white.</param>
internal sealed record OverlayInfo(string Clock, string Line, string Media, int Level);

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
    Rectangle? onlyRegion;

    // Burn-in protection: a faint lighter band that creeps across the overlay
    readonly System.Windows.Forms.Timer driftTimer = new() { Interval = 30_000 };
    bool drift;
    double driftPos;

    OverlayInfo? info;

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
        driftTimer.Tick += (_, _) =>
        {
            driftPos = (driftPos + 0.03) % 1.4;
            Invalidate();
        };
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
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

    public void SetDrift(bool on)
    {
        if (on == drift) return;
        drift = on;
        if (on) driftTimer.Start();
        else driftTimer.Stop();
        Invalidate();
    }

    /// <summary>Shows a faint clock and session line on this overlay, or nothing.</summary>
    public void SetInfo(OverlayInfo? value)
    {
        if (Equals(value, info)) return;
        info = value;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        PaintDrift(e.Graphics);
        if (info != null) PaintInfo(e.Graphics, info);
    }

    /// <summary>
    /// The window is see-through at its opacity, so the text is drawn brighter by the same
    /// amount to look as bright as asked. It wanders slowly so no pixel stays lit for hours.
    /// </summary>
    void PaintInfo(Graphics g, OverlayInfo show)
    {
        int w = ClientSize.Width, h = ClientSize.Height;
        if (w <= 0 || h <= 0) return;
        double opacity = Math.Max(0.2, fadeTo > 0 ? fadeTo : Opacity);
        int level = (int)Math.Clamp(show.Level / 100.0 * 255 / opacity, 0, 255);
        using var main = new SolidBrush(Color.FromArgb(level, level, level));
        using var soft = new SolidBrush(Color.FromArgb(level * 3 / 4, level * 3 / 4, level * 3 / 4));
        float scale = Math.Clamp(h / 1080f, 0.6f, 2.5f);
        using var clockFont = Platform.WinFormsFonts.Create(110 * scale, FontStyle.Bold);
        using var lineFont = Platform.WinFormsFonts.Create(26 * scale);
        using var mediaFont = Platform.WinFormsFonts.Create(22 * scale);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

        var lines = new List<(string Text, Font Font, Brush Brush)> { (show.Clock, clockFont, main) };
        if (show.Line.Length > 0) lines.Add((show.Line, lineFont, soft));
        if (show.Media.Length > 0) lines.Add(("♪  " + show.Media, mediaFont, soft));
        float maxWidth = w * 0.6f;
        var sizes = lines.Select(l => g.MeasureString(l.Text, l.Font, (int)maxWidth)).ToList();
        float blockW = sizes.Max(s => s.Width), blockH = sizes.Sum(s => s.Height);

        double minutes = DateTime.Now.Ticks / (double)TimeSpan.TicksPerMinute;
        float margin = 60 * scale;
        float x = margin + (float)((w - blockW - 2 * margin) * (0.5 + 0.5 * Math.Sin(minutes / 9.0)));
        float y = margin + (float)((h - blockH - 2 * margin) * (0.5 + 0.5 * Math.Cos(minutes / 13.0)));
        using var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        for (int i = 0; i < lines.Count; i++)
        {
            g.DrawString(lines[i].Text, lines[i].Font, lines[i].Brush, new RectangleF(x, y, maxWidth, sizes[i].Height), format);
            y += sizes[i].Height;
        }
    }

    void PaintDrift(Graphics g)
    {
        if (!drift || ClientSize.Width <= 0) return;
        int w = ClientSize.Width, band = Math.Max(1, w * 2 / 5);
        int x = (int)(driftPos * w) - band;
        using var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
            new Rectangle(x, 0, band, 1), Color.FromArgb(0, 40, 40, 40), Color.FromArgb(0, 40, 40, 40), 0f)
        {
            InterpolationColors = new System.Drawing.Drawing2D.ColorBlend(3)
            {
                Colors = new[] { Color.Black, Color.FromArgb(46, 46, 46), Color.Black },
                Positions = new[] { 0f, 0.5f, 1f },
            },
        };
        g.FillRectangle(brush, new Rectangle(x, 0, band, ClientSize.Height));
    }

    /// <summary>Restricts this overlay to one screen-space rectangle, for a notification peek.</summary>
    public void SetOnlyRegion(RECT? rect)
    {
        var next = rect is RECT r
            ? new Rectangle(r.Left - bounds.Left, r.Top - bounds.Top, r.Width, r.Height)
            : (Rectangle?)null;
        if (next == onlyRegion) return;
        onlyRegion = next;
        Region = next is Rectangle area ? new Region(area) : null;
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
        if (onlyRegion != null) return;
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
        // The text is drawn for the target opacity; on the way out it would flash brighter
        if (opacity <= 0.001) info = null;
        if (info != null || opacity <= 0.001) Invalidate();

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
            driftTimer.Dispose();
            Region?.Dispose();
        }
        base.Dispose(disposing);
    }
}
