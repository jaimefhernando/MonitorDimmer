using System.Drawing;
using System.Windows.Forms;
using MonitorDimmer.Core;

namespace MonitorDimmer.Platform;

internal sealed class TrayIcon : IDisposable
{
    readonly NotifyIcon icon;
    readonly ContextMenuStrip menu;
    readonly DimController controller;
    readonly ToolStripMenuItem dimNow;
    readonly ToolStripMenuItem pause;

    public TrayIcon(DimController controller, Action openSettings, Action exit)
    {
        this.controller = controller;

        dimNow = new ToolStripMenuItem("Dim other monitors now", null, (_, _) => controller.ToggleManual());
        pause = new ToolStripMenuItem("Pause automatic dimming", null, (_, _) => controller.SetAutoPaused(!controller.AutoPaused));
        var settings = new ToolStripMenuItem("Settings…", null, (_, _) => openSettings()) { Font = BoldMenuFont() };

        menu = new ContextMenuStrip();
        menu.Items.AddRange(new ToolStripItem[]
        {
            settings, new ToolStripSeparator(), dimNow, pause, new ToolStripSeparator(),
            new ToolStripMenuItem("Exit", null, (_, _) => exit()),
        });
        menu.Opening += (_, _) => Refresh();

        icon = new NotifyIcon
        {
            Icon = AppIcon.Icon,
            ContextMenuStrip = menu,
            Visible = true,
        };
        icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) openSettings();
        };

        controller.StateChanged += Refresh;
        ThemeManager.Changed += ApplyTheme;
        ApplyTheme();
        Refresh();
    }

    public void Refresh()
    {
        dimNow.Checked = controller.ManualActive;
        pause.Checked = controller.AutoPaused;
        string text = "Monitor Dimmer: " + controller.Status;
        icon.Text = text.Length > 63 ? text[..62] + "…" : text;
    }

    void ApplyTheme()
    {
        menu.Renderer = ThemeManager.IsDark ? new DarkMenuRenderer() : new ToolStripProfessionalRenderer();
        menu.ForeColor = ThemeManager.IsDark ? DarkMenuColors.Text : SystemColors.MenuText;
    }

    static Font BoldMenuFont() =>
        new(SystemFonts.MenuFont ?? Control.DefaultFont, FontStyle.Bold);

    public void Dispose()
    {
        controller.StateChanged -= Refresh;
        ThemeManager.Changed -= ApplyTheme;
        icon.Visible = false;
        menu.Dispose();
        icon.Dispose();
    }

    sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkMenuColors()) => RoundedEdges = false;

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = DarkMenuColors.Text;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            // Accent check mark instead of the default dark-on-light one
            var r = e.ImageRectangle;
            using var pen = new Pen(DarkMenuColors.Accent, 2f);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.DrawLines(pen, new[]
            {
                new PointF(r.Left + r.Width * 0.2f, r.Top + r.Height * 0.52f),
                new PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.74f),
                new PointF(r.Left + r.Width * 0.8f, r.Top + r.Height * 0.28f),
            });
        }
    }

    sealed class DarkMenuColors : ProfessionalColorTable
    {
        public static readonly Color Text = Color.FromArgb(231, 233, 236);
        public static readonly Color Accent = Color.FromArgb(242, 179, 61);
        static readonly Color Back = Color.FromArgb(30, 33, 38);
        static readonly Color Hover = Color.FromArgb(52, 57, 67);
        static readonly Color Line = Color.FromArgb(58, 63, 73);

        public override Color ToolStripDropDownBackground => Back;
        public override Color ImageMarginGradientBegin => Back;
        public override Color ImageMarginGradientMiddle => Back;
        public override Color ImageMarginGradientEnd => Back;
        public override Color MenuBorder => Line;
        public override Color MenuItemBorder => Hover;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color SeparatorDark => Line;
        public override Color SeparatorLight => Line;
        public override Color CheckBackground => Back;
        public override Color CheckSelectedBackground => Hover;
        public override Color CheckPressedBackground => Hover;
    }
}
