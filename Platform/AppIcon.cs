using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Shadeplay.Platform;

/// <summary>Draws the app icon: one bright monitor next to a dimmed one.</summary>
internal static class AppIcon
{
    static Icon? icon;

    public static Icon Icon => icon ??= Create(32);

    public static ImageSource ImageSource =>
        Imaging.CreateBitmapSourceFromHIcon(Icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

    static Icon Create(int size)
    {
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(System.Drawing.Color.Transparent);
            float s = size / 32f;

            // Dimmed monitor behind
            using (var dim = new SolidBrush(System.Drawing.Color.FromArgb(255, 70, 74, 84)))
                FillRounded(g, dim, new RectangleF(13 * s, 5 * s, 18 * s, 13 * s), 2.5f * s);

            // Amber crescent arcing over both screens
            using (var arc = new System.Drawing.Pen(System.Drawing.Color.FromArgb(255, 255, 214, 110), 2f * s)
                   { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawArc(arc, 3 * s, 2 * s, 26 * s, 22 * s, 195, 150);

            // Bright monitor in front
            using (var frame = new SolidBrush(System.Drawing.Color.FromArgb(255, 32, 36, 44)))
                FillRounded(g, frame, new RectangleF(1 * s, 9 * s, 20 * s, 15 * s), 2.5f * s);
            using (var screen = new SolidBrush(System.Drawing.Color.FromArgb(255, 255, 196, 64)))
                FillRounded(g, screen, new RectangleF(3 * s, 11 * s, 16 * s, 11 * s), 1.5f * s);
            using (var stand = new SolidBrush(System.Drawing.Color.FromArgb(255, 32, 36, 44)))
                g.FillRectangle(stand, 7 * s, 24 * s, 8 * s, 3 * s);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    static void FillRounded(Graphics g, System.Drawing.Brush brush, RectangleF r, float radius)
    {
        using var path = new GraphicsPath();
        float d = radius * 2;
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        g.FillPath(brush, path);
    }
}
