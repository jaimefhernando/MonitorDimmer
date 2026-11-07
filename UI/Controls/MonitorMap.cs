using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Shadeplay.Interop;
using Shadeplay.Platform;

namespace Shadeplay.UI.Controls;

public enum MonitorTileState { Normal, Game, Dimmed, Selected }

/// <param name="Bounds">The monitor's desktop rectangle, for its size and position.</param>
/// <param name="Badge">Small label above the name, e.g. "GAME" or "DIMMED 80 %".</param>
/// <param name="Art">Picture filling the tile, under its text; the game's art on the game's monitor.</param>
public sealed record MonitorTile(RECT Bounds, string Name, string Badge, string Detail, MonitorTileState State, ImageSource? Art = null);

/// <summary>
/// The monitors drawn to scale, as Windows arranges them: the game's monitor outlined in the
/// accent color, dimmed ones striped.
/// </summary>
public class MonitorMap : FrameworkElement
{
    public static readonly DependencyProperty TilesProperty = DependencyProperty.Register(
        nameof(Tiles), typeof(IReadOnlyList<MonitorTile>), typeof(MonitorMap),
        new FrameworkPropertyMetadata(Array.Empty<MonitorTile>(), FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<MonitorTile> Tiles
    {
        get => (IReadOnlyList<MonitorTile>)GetValue(TilesProperty);
        set => SetValue(TilesProperty, value);
    }

    public MonitorMap()
    {
        // Colors come from the theme, which can change while the window is open.
        // Subscribe in Loaded (not the constructor) so an instance that is never
        // loaded does not leak a ThemeManager handler.
        Loaded += (_, _) => ThemeManager.Changed += InvalidateVisual;
        Unloaded += (_, _) => ThemeManager.Changed -= InvalidateVisual;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var tiles = Tiles;
        if (tiles == null || tiles.Count == 0 || ActualWidth <= 0 || ActualHeight <= 0) return;

        int left = tiles.Min(t => t.Bounds.Left), top = tiles.Min(t => t.Bounds.Top);
        int right = tiles.Max(t => t.Bounds.Right), bottom = tiles.Max(t => t.Bounds.Bottom);
        double scale = Math.Min(ActualWidth / (right - left), ActualHeight / (bottom - top));
        double offsetX = (ActualWidth - (right - left) * scale) / 2;
        double offsetY = (ActualHeight - (bottom - top) * scale) / 2;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var font = new Typeface((FontFamily)FindResource("AppFont"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var bold = new Typeface((FontFamily)FindResource("AppFont"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

        foreach (var t in tiles)
        {
            const double gap = 7; // space between monitors that touch on the desktop
            var r = new Rect(
                offsetX + (t.Bounds.Left - left) * scale + gap / 2,
                offsetY + (t.Bounds.Top - top) * scale + gap / 2,
                Math.Max(0, t.Bounds.Width * scale - gap),
                Math.Max(0, t.Bounds.Height * scale - gap));

            (Brush fill, Brush border, double thickness, Brush badge, Brush name) = t.State switch
            {
                MonitorTileState.Game or MonitorTileState.Selected =>
                    (Res("SelectedBg"), Res("Accent"), 2.0, Res("Accent"), t.Art != null ? Brushes.White : Res("TextMain")),
                MonitorTileState.Dimmed =>
                    (Res("DimmedStripes"), Res("ControlBorder"), 1.0, Res("TextMuted"), Res("TextSecondary")),
                _ => (Res("ControlBg"), Res("ControlBorder"), 1.0, Res("TextMuted"), Res("TextMain")),
            };
            var inner = new Rect(r.X + thickness / 2, r.Y + thickness / 2, Math.Max(0, r.Width - thickness), Math.Max(0, r.Height - thickness));
            dc.DrawRectangle(t.Art != null ? ArtFill(t.Art, inner) ?? fill : fill, null, inner);
            if (t.Art != null) dc.DrawRectangle(ArtScrim, null, inner);
            dc.DrawRectangle(null, new Pen(border, thickness), inner);

            // Badge, name and detail, centred; smaller monitors get smaller text
            double size = Math.Clamp(r.Height / 7, 10, 16);
            var lines = new List<FormattedText>
            {
                Text(Spread(t.Badge), font, size * 0.72, badge, dpi),
                Text(t.Name, bold, size, name, dpi),
            };
            if (t.Detail.Length > 0) lines.Add(Text(t.Detail, font, size * 0.78, t.Art != null ? Brushes.White : Res("TextMuted"), dpi));
            double total = lines.Sum(l => l.Height) + (lines.Count - 1) * 2;
            double y = r.Top + (r.Height - total) / 2;
            dc.PushClip(new RectangleGeometry(r));
            foreach (var line in lines)
            {
                line.MaxTextWidth = Math.Max(1, r.Width - 12);
                line.Trimming = TextTrimming.CharacterEllipsis;
                line.TextAlignment = TextAlignment.Center;
                dc.DrawText(line, new Point(r.Left + 6, y));
                y += line.Height + 2;
            }
            dc.Pop();
        }
    }

    Brush Res(string key) => TryFindResource(key) as Brush ?? Brushes.Gray;

    /// <summary>Keeps the white text readable over art in either theme.</summary>
    static readonly Brush ArtScrim = Freeze(new SolidColorBrush(Color.FromArgb(0x9C, 0, 0, 0)));

    static Brush Freeze(Brush b)
    {
        b.Freeze();
        return b;
    }

    /// <summary>The art cropped to fill a tile.</summary>
    static Brush? ArtFill(ImageSource art, Rect area)
    {
        if (area.Width <= 0 || area.Height <= 0) return null;
        var brush = new ImageBrush(art) { Stretch = Stretch.UniformToFill, AlignmentX = AlignmentX.Center, AlignmentY = AlignmentY.Center };
        brush.Freeze();
        return brush;
    }

    static FormattedText Text(string s, Typeface face, double size, Brush brush, double dpi) =>
        new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, size, brush, dpi);

    static string Spread(string s) => string.Join(' ', s.ToCharArray());
}
