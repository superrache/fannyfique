using System.Windows;
using System.Windows.Media;
using Fannyfique.App.Services;
using Fannyfique.App.ViewModels;

namespace Fannyfique.App.Controls;

/// <summary>Anneau de répartition de l'espace récupérable par type de source (CLAUDE.md 7.5).</summary>
public sealed class DonutChart : FrameworkElement
{
    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.Register(
        nameof(Segments),
        typeof(IEnumerable<DonutSegment>),
        typeof(DonutChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness),
        typeof(double),
        typeof(DonutChart),
        new FrameworkPropertyMetadata(14.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public DonutChart()
    {
        Loaded += (_, _) => ThemeManager.ThemeChanged += OnThemeChanged;
        Unloaded += (_, _) => ThemeManager.ThemeChanged -= OnThemeChanged;
    }

    public IEnumerable<DonutSegment>? Segments
    {
        get => (IEnumerable<DonutSegment>?)GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    private void OnThemeChanged(object? sender, EventArgs e) => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= Thickness)
        {
            return;
        }

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = (size - Thickness) / 2;
        var segments = Segments?.Where(s => s.Value > 0).ToList() ?? [];
        var total = segments.Sum(s => s.Value);
        if (total <= 0)
        {
            dc.DrawEllipse(null, new Pen(Brush("Stroke"), Thickness), center, radius, radius);
            return;
        }

        const double gap = 2; // degrés entre deux segments
        var angle = -90.0;
        foreach (var segment in segments)
        {
            var sweep = segment.Value / total * 360;
            var pen = new Pen(Brush($"Type{segment.Type}"), Thickness);
            if (segments.Count == 1)
            {
                dc.DrawEllipse(null, pen, center, radius, radius);
                break;
            }

            var visible = Math.Max(0.5, sweep - gap);
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(PointAt(center, radius, angle), isFilled: false, isClosed: false);
                ctx.ArcTo(PointAt(center, radius, angle + visible), new Size(radius, radius), 0, visible > 180, SweepDirection.Clockwise, true, false);
            }

            geometry.Freeze();
            dc.DrawGeometry(null, pen, geometry);
            angle += sweep;
        }
    }

    private Brush Brush(string key) => TryFindResource(key) as Brush ?? Brushes.Gray;

    private static Point PointAt(Point center, double radius, double degrees)
    {
        var radians = degrees * Math.PI / 180;
        return new Point(center.X + (radius * Math.Cos(radians)), center.Y + (radius * Math.Sin(radians)));
    }
}
