using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Neuraink.Core;

namespace Neuraink.App.Services;

/// <summary>
/// The shape a calligraphic nib leaves along a path, like an Illustrator calligraphic brush: an ellipse (size × size·roundness,
/// turned by the angle) swept from point to point. Between two points the sweep is the hull of the two ellipses, so the
/// stroke is exact for any angle and roundness; pressure (pen tablets) scales the nib.
/// </summary>
public static class StrokeGeometry
{
    public static Geometry Build(IReadOnlyList<double[]> pts, BrushTip tip)
    {
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        if (pts.Count == 0) { group.Freeze(); return group; }
        double a = tip.Angle * Math.PI / 180, cos = Math.Cos(a), sin = Math.Sin(a);
        double r = Math.Clamp(tip.Roundness, 0.02, 1);

        Point P(int i) => new(pts[i][0], pts[i][1]);
        double Scale(int i) => pts[i].Length > 2 && pts[i][2] > 0 ? Math.Clamp(pts[i][2] * 2, 0.25, 2) : 1;

        // nib ellipse at a point (Y axis of the picture points down: the angle turns counter-clockwise on screen)
        void Stamp(Point c, double s)
        {
            var e = new EllipseGeometry(c, tip.Size * s / 2, tip.Size * s * r / 2);
            e.Transform = new RotateTransform(-tip.Angle, c.X, c.Y);
            group.Children.Add(e);
        }

        // the point of the nib ellipse farthest along the normal n (support point), relative to the centre
        Vector Support(Vector n, double s)
        {
            double A = tip.Size * s / 2, B = tip.Size * s * r / 2;
            // into the ellipse's own axes
            double nx = n.X * cos - n.Y * sin, ny = n.X * sin + n.Y * cos;
            double ux = A * nx, uy = B * ny, len = Math.Sqrt(ux * ux + uy * uy);
            if (len < 1e-9) return new Vector(0, 0);
            double lx = A * ux / len, ly = B * uy / len;
            // back to the picture
            return new Vector(lx * cos + ly * sin, -lx * sin + ly * cos);
        }

        Stamp(P(0), Scale(0));
        for (int i = 1; i < pts.Count; i++)
        {
            Point p0 = P(i - 1), p1 = P(i);
            var d = p1 - p0;
            if (d.Length < 1e-6) continue;
            var n = new Vector(-d.Y, d.X);
            n.Normalize();
            Vector o0 = Support(n, Scale(i - 1)), o1 = Support(n, Scale(i));
            // every piece must turn the same way as the ellipses: with the non-zero rule, opposite turns cancel out (holes)
            Point[] q = [p0 + o0, p1 + o1, p1 - o1, p0 - o0];
            if (Math.Sign(SignedArea(q)) != EllipseTurn) Array.Reverse(q);
            var quad = new StreamGeometry();
            using (var g = quad.Open())
            {
                g.BeginFigure(q[0], true, true);
                g.PolyLineTo(new[] { q[1], q[2], q[3] }, true, false);
            }
            group.Children.Add(quad);
            Stamp(p1, Scale(i));
        }
        group.Freeze();
        return group;
    }

    private static double SignedArea(IReadOnlyList<Point> p)
    {
        double a = 0;
        for (int i = 0; i < p.Count; i++)
        {
            var u = p[i]; var v = p[(i + 1) % p.Count];
            a += u.X * v.Y - v.X * u.Y;
        }
        return a;
    }

    /// <summary>Which way WPF draws an ellipse (+1 / -1), measured once.</summary>
    private static readonly int EllipseTurn = MeasureEllipseTurn();

    private static int MeasureEllipseTurn()
    {
        var flat = new EllipseGeometry(new Point(0, 0), 2, 1).GetFlattenedPathGeometry();
        var pts = new List<Point>();
        foreach (var f in flat.Figures)
        {
            pts.Add(f.StartPoint);
            foreach (var seg in f.Segments)
                if (seg is PolyLineSegment pl) pts.AddRange(pl.Points);
                else if (seg is LineSegment l) pts.Add(l.Point);
            break;
        }
        var sign = Math.Sign(SignedArea(pts));
        return sign == 0 ? 1 : sign;
    }

    /// <summary>All the strokes of a letter as one filled outline (overlaps merged).</summary>
    public static Geometry Outline(IEnumerable<Segment> strokes)
    {
        var all = new GeometryGroup { FillRule = FillRule.Nonzero };
        foreach (var s in strokes)
            if (s.Stroke is { Count: > 0 } pts && s.Brush != null) all.Children.Add(Build(pts, s.Brush));
        return all.GetOutlinedPathGeometry();
    }

    /// <summary>An SVG file of the outline (pure black on transparent), in picture pixels.</summary>
    public static string ToSvg(Geometry outline, Rect box)
    {
        var path = outline is PathGeometry pg ? pg : PathGeometry.CreateFromGeometry(outline);
        var d = path.ToString(CultureInfo.InvariantCulture);
        if (d.StartsWith("F0", StringComparison.Ordinal) || d.StartsWith("F1", StringComparison.Ordinal)) d = d[2..];
        string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        return $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"{F(box.X)} {F(box.Y)} {F(box.Width)} {F(box.Height)}\" " +
               $"width=\"{F(box.Width)}\" height=\"{F(box.Height)}\"><path fill=\"#000\" fill-rule=\"nonzero\" d=\"{d.Trim()}\"/></svg>\n";
    }
}
