using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Shadeplay.UI.Controls;

/// <summary>
/// A border with the top-left and bottom-right corners cut at 45°, the app's signature shape.
/// Clips its content to the same outline.
/// </summary>
public class ChamferBorder : Decorator
{
    public static readonly DependencyProperty BackgroundProperty = DependencyProperty.Register(
        nameof(Background), typeof(Brush), typeof(ChamferBorder),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BorderBrushProperty = DependencyProperty.Register(
        nameof(BorderBrush), typeof(Brush), typeof(ChamferBorder),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BorderThicknessProperty = DependencyProperty.Register(
        nameof(BorderThickness), typeof(double), typeof(ChamferBorder),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ChamferProperty = DependencyProperty.Register(
        nameof(Chamfer), typeof(double), typeof(ChamferBorder),
        new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty PaddingProperty = DependencyProperty.Register(
        nameof(Padding), typeof(Thickness), typeof(ChamferBorder),
        new FrameworkPropertyMetadata(new Thickness(0), FrameworkPropertyMetadataOptions.AffectsMeasure));

    public Brush? Background { get => (Brush?)GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }
    public Brush? BorderBrush { get => (Brush?)GetValue(BorderBrushProperty); set => SetValue(BorderBrushProperty, value); }
    public double BorderThickness { get => (double)GetValue(BorderThicknessProperty); set => SetValue(BorderThicknessProperty, value); }
    /// <summary>Length of the cut corner's sides, in pixels.</summary>
    public double Chamfer { get => (double)GetValue(ChamferProperty); set => SetValue(ChamferProperty, value); }
    public Thickness Padding { get => (Thickness)GetValue(PaddingProperty); set => SetValue(PaddingProperty, value); }

    Thickness Inner => new(
        Padding.Left + BorderThickness, Padding.Top + BorderThickness,
        Padding.Right + BorderThickness, Padding.Bottom + BorderThickness);

    protected override Size MeasureOverride(Size constraint)
    {
        var inner = Inner;
        double w = inner.Left + inner.Right, h = inner.Top + inner.Bottom;
        if (Child == null) return new Size(w, h);
        Child.Measure(new Size(Math.Max(0, constraint.Width - w), Math.Max(0, constraint.Height - h)));
        return new Size(Child.DesiredSize.Width + w, Child.DesiredSize.Height + h);
    }

    protected override Size ArrangeOverride(Size size)
    {
        var inner = Inner;
        Child?.Arrange(new Rect(inner.Left, inner.Top,
            Math.Max(0, size.Width - inner.Left - inner.Right), Math.Max(0, size.Height - inner.Top - inner.Bottom)));
        Clip = Outline(size, 0);
        return size;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = new Size(ActualWidth, ActualHeight);
        double t = BorderThickness;
        Pen? pen = t > 0 && BorderBrush != null ? new Pen(BorderBrush, t) : null;
        // The pen is centred on the outline, so draw it half a stroke inside
        dc.DrawGeometry(Background, pen, Outline(size, pen != null ? t / 2 : 0));
    }

    Geometry Outline(Size size, double inset)
    {
        double w = size.Width, h = size.Height;
        double c = Math.Min(Chamfer, Math.Min(w, h) / 2);
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(c + inset * 0.4, inset), true, true);
            ctx.LineTo(new Point(w - inset, inset), true, false);
            ctx.LineTo(new Point(w - inset, h - c - inset * 0.4), true, false);
            ctx.LineTo(new Point(w - c - inset * 0.4, h - inset), true, false);
            ctx.LineTo(new Point(inset, h - inset), true, false);
            ctx.LineTo(new Point(inset, c + inset * 0.4), true, false);
        }
        g.Freeze();
        return g;
    }
}
