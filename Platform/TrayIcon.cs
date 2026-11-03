using System.Drawing;
using System.Windows.Forms;
using Shadeplay.Core;

namespace Shadeplay.Platform;

internal sealed class TrayIcon : IDisposable
{
    readonly NotifyIcon icon;
    readonly ContextMenuStrip menu;
    readonly DimController controller;
    readonly ToolStripMenuItem dimNow;
    readonly ToolStripMenuItem pause;
    readonly ToolStripMenuItem resume;
    readonly ToolStripMenuItem night;
    readonly ToolStripLabel header;
    readonly ToolStripLabel headerDetail;

    public TrayIcon(DimController controller, Action openSettings, Action exit)
    {
        this.controller = controller;

        dimNow = new ToolStripMenuItem("Dim other monitors now", null, (_, _) => controller.ToggleManual());
        night = new ToolStripMenuItem("Night mode", null, (_, _) => controller.ToggleNight());
        pause = new ToolStripMenuItem("Pause");
        pause.DropDownItems.AddRange(new ToolStripItem[]
        {
            new ToolStripMenuItem("For 30 minutes", null, (_, _) => controller.PauseFor(TimeSpan.FromMinutes(30))),
            new ToolStripMenuItem("For 1 hour", null, (_, _) => controller.PauseFor(TimeSpan.FromHours(1))),
            new ToolStripMenuItem("For 2 hours", null, (_, _) => controller.PauseFor(TimeSpan.FromHours(2))),
            new ToolStripMenuItem("Until I resume", null, (_, _) => controller.PauseFor(null)),
        });
        resume = new ToolStripMenuItem("Resume", null, (_, _) => controller.Resume());
        var settings = new ToolStripMenuItem("Open Shadeplay", null, (_, _) => openSettings()) { Font = BoldMenuFont() };
        // What is happening right now, as in the app's Home page
        header = new ToolStripLabel { Font = BoldMenuFont(), Padding = new Padding(4, 6, 4, 2) };
        headerDetail = new ToolStripLabel { Padding = new Padding(4, 0, 4, 6) };

        menu = new ContextMenuStrip();
        menu.Items.AddRange(new ToolStripItem[]
        {
            header, headerDetail, new ToolStripSeparator(),
            dimNow, night, pause, resume, new ToolStripSeparator(),
            settings, new ToolStripMenuItem("Exit", null, (_, _) => exit()),
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
        icon.BalloonTipClicked += (_, _) => openSettings();

        controller.StateChanged += Refresh;
        ThemeManager.Changed += ApplyTheme;
        ApplyTheme();
        Refresh();
    }

    public void Refresh()
    {
        dimNow.Checked = controller.ManualActive;
        night.Checked = controller.NightOn;
        bool paused = controller.AutoPaused;
        pause.Visible = !paused;
        resume.Visible = paused;
        resume.Text = controller.PausedUntil is DateTime until ? $"Resume (paused until {until:HH:mm})" : "Resume";

        var dimmed = controller.Monitors.Where(m => m.Kind == MonitorDimKind.Dimmed).ToList();
        if (controller.SessionName != null)
        {
            header.Text = "Dimming for " + controller.SessionName;
            string time = controller.SessionStartedAt is DateTime start ? " · " + SessionTracker.Format(DateTime.Now - start) : "";
            headerDetail.Text = (dimmed.Count > 0
                ? string.Join(", ", dimmed.Select(d => $"Display {d.Monitor.Number}")) + $" at {dimmed[0].Strength} %"
                : "Dimming") + time;
        }
        else
        {
            header.Text = controller.ManualActive ? "Dimmed by hand" : paused ? "Paused" : "Shadeplay";
            headerDetail.Text = controller.ManualActive || paused ? controller.Status : "Waiting for a game";
        }
        header.ForeColor = controller.IsDimming ? DarkMenuColors.Accent : MenuText;
        headerDetail.ForeColor = MutedText;
        string text = "Shadeplay: " + controller.Status;
        icon.Text = text.Length > 63 ? text[..62] + "…" : text;
    }

    /// <summary>
    /// A Windows notification from the tray icon. Windows decides how it shows: as a toast, or
    /// straight into the notification centre while a fullscreen game has Do Not Disturb on.
    /// </summary>
    public void Notify(string title, string text)
    {
        icon.ShowBalloonTip(5000, title, text, ToolTipIcon.None);
        Log.Write("Notification: " + title);
    }

    static Color MenuText => ThemeManager.IsDark ? DarkMenuColors.Text : SystemColors.MenuText;
    static Color MutedText => ThemeManager.IsDark ? Color.FromArgb(140, 146, 156) : SystemColors.GrayText;

    void ApplyTheme()
    {
        menu.Renderer = ThemeManager.IsDark ? new DarkMenuRenderer() : new ToolStripProfessionalRenderer();
        menu.ForeColor = ThemeManager.IsDark ? DarkMenuColors.Text : SystemColors.MenuText;
        // The Pause submenu is its own drop-down and needs the same look
        pause.DropDown.Renderer = menu.Renderer;
        pause.DropDown.ForeColor = menu.ForeColor;
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
        // The app's graphite palette (Themes\Dark.xaml)
        public static readonly Color Text = Color.FromArgb(236, 238, 241);
        public static readonly Color Accent = Color.FromArgb(242, 179, 61);
        static readonly Color Back = Color.FromArgb(17, 19, 23);
        static readonly Color Hover = Color.FromArgb(30, 34, 40);
        static readonly Color Line = Color.FromArgb(42, 47, 55);

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
