using System.Drawing;
using System.Windows.Forms;

namespace MonitorDimmer.Core;

/// <summary>Shows each monitor's number and name on that monitor for a few seconds.</summary>
internal sealed class IdentifyForm : Form
{
    public static void ShowOnAll()
    {
        foreach (var monitor in Screens.GetAll())
            new IdentifyForm(monitor).Show();
    }

    IdentifyForm(MonitorInfo monitor)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.FromArgb(32, 36, 44);
        Opacity = 0.92;

        int w = 420, h = 260;
        Bounds = new Rectangle(
            monitor.Bounds.Left + (monitor.Bounds.Width - w) / 2,
            monitor.Bounds.Top + (monitor.Bounds.Height - h) / 2, w, h);

        Controls.Add(new Label
        {
            Text = monitor.Number.ToString(),
            ForeColor = Color.FromArgb(255, 196, 64),
            Font = new Font("Segoe UI", 96, FontStyle.Bold, GraphicsUnit.Pixel),
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Fill,
        });
        Controls.Add(new Label
        {
            Text = monitor.Description,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 18, FontStyle.Regular, GraphicsUnit.Pixel),
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Bottom,
            Height = 64,
        });

        var timer = new System.Windows.Forms.Timer { Interval = 2500 };
        timer.Tick += (_, _) =>
        {
            timer.Dispose();
            Close();
        };
        timer.Start();
    }

    protected override bool ShowWithoutActivation => true;

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        Dispose();
    }
}
