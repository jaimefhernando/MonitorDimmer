using System.Drawing;
using System.Windows.Forms;
using Shadeplay.Interop;
using Shadeplay.Platform;

namespace Shadeplay.Core;

/// <summary>A short "Scene: Late night" note on the monitor with the mouse, after the shortcut.</summary>
internal sealed class SceneToast : Form
{
    static SceneToast? current;

    readonly string title;
    readonly string detail;
    readonly System.Windows.Forms.Timer timer = new() { Interval = 1800 };

    public static void ShowOn(string title, string detail)
    {
        current?.Close();
        current = new SceneToast(title, detail);
        current.Show();
    }

    SceneToast(string title, string detail)
    {
        this.title = title;
        this.detail = detail;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.FromArgb(17, 19, 23);
        Opacity = 0.96;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);

        var screen = Native.GetCursorPos(out var p) ? Screen.FromPoint(new Point(p.X, p.Y)) : Screen.PrimaryScreen!;
        int w = 380, h = 92;
        var area = screen.WorkingArea;
        Bounds = new Rectangle(area.Left + (area.Width - w) / 2, area.Bottom - h - 64, w, h);

        timer.Tick += (_, _) => Close();
        timer.Start();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
            return cp;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.Clear(BackColor);
        using var border = new Pen(Color.FromArgb(42, 47, 55));
        g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        using var accent = new SolidBrush(Color.FromArgb(242, 179, 61));
        g.FillRectangle(accent, 0, 0, 4, Height);
        using var kicker = WinFormsFonts.Create(12, FontStyle.Bold);
        using var big = WinFormsFonts.Create(24, FontStyle.Bold);
        using var main = new SolidBrush(Color.FromArgb(236, 238, 241));
        g.DrawString(detail, kicker, accent, 24, 18);
        g.DrawString(title, big, main, new RectangleF(22, 38, Width - 40, 40),
            new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        if (current == this) current = null;
        timer.Dispose();
        Dispose();
    }
}
