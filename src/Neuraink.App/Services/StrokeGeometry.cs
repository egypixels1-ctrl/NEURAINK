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
    /// <summary>Gaussian width (picture pixels) of each Fidelity step, Accurate … Smooth.</summary>
    private static readonly double[] Sigma = [0, 1.5, 3, 5, 8];

    /// <summary>
    /// Like the Illustrator Paintbrush Fidelity: the hand's small shakes are smoothed out of the path before the nib is swept
    /// along it. The raw points are not changed (they stay in the data); only the drawn shape is smooth.
    /// </summary>
    public static List<double[]> Smooth(IReadOnlyList<double[]> pts, int fidelity)
    {
        if (fidelity <= 0 || pts.Count < 3) return pts.ToList();
        double sigma = Sigma[Math.Clamp(fidelity, 0, Sigma.Length - 1)];
        // 1. even spacing (1 px), so the smoothing does not depend on the mouse speed
        var even = new List<double[]> { new[] { pts[0][0], pts[0][1], Pr(pts[0]) } };
        double carry = 0;
        for (int i = 1; i < pts.Count; i++)
        {
            double x0 = pts[i - 1][0], y0 = pts[i - 1][1], x1 = pts[i][0], y1 = pts[i][1];
            double len = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
            if (len < 1e-9) continue;
            double t = 1 - carry;
            while (t <= len)
            {
                double f = t / len;
                even.Add([x0 + (x1 - x0) * f, y0 + (y1 - y0) * f, Pr(pts[i - 1]) + (Pr(pts[i]) - Pr(pts[i - 1])) * f]);
                t += 1;
            }
            carry = len - (t - 1);
        }
        var last = pts[^1];
        even.Add([last[0], last[1], Pr(last)]);
        int n = even.Count;
        if (n < 3) return even;
        // 2. Gaussian smoothing; the two ends stay where they were drawn
        int rad = (int)Math.Ceiling(3 * sigma);
        var kernel = Enumerable.Range(-rad, 2 * rad + 1).Select(k => Math.Exp(-k * k / (2 * sigma * sigma))).ToArray();
        var outp = new List<double[]>(n / 2 + 2);
        for (int i = 0; i < n; i += (i == 0 || i >= n - 3) ? 1 : 2)
        {
            double sx = 0, sy = 0, sp = 0, sw = 0;
            for (int k = -rad; k <= rad; k++)
            {
                int j = i + k;
                if (j < 0 || j >= n) continue;
                double w = kernel[k + rad];
                sx += even[j][0] * w; sy += even[j][1] * w; sp += even[j][2] * w; sw += w;
            }
            // near an end, blend back to the drawn point (no shrinking of the stroke)
            double edge = Math.Min(1, Math.Min(i, n - 1 - i) / Math.Max(1.0, rad));
            outp.Add([
                even[i][0] + (sx / sw - even[i][0]) * edge,
                even[i][1] + (sy / sw - even[i][1]) * edge,
                sp / sw,
            ]);
        }
        return outp;

        static double Pr(double[] p) => p.Length > 2 ? p[2] : 0;
    }

    public static Geometry Build(IReadOnlyList<double[]> raw, BrushTip tip, int fidelity = 0)
    {
        var pts = fidelity > 0 ? Smooth(raw, fidelity) : raw;
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


    // ======================================================================================================
    // Curve fitting, like the Illustrator Paintbrush: when the stroke ends, the hand's path is replaced by a few
    // smooth cubic Bézier curves that stay within a tolerance of it (Schneider, "An Algorithm for Automatically
    // Fitting Digitized Curves", Graphics Gems 1990). Sharp turns (corners of the letters) are kept as corners.
    // ======================================================================================================

    /// <summary>Tolerance of each Fidelity step, in screen pixels (Accurate … Smooth).</summary>
    public static readonly double[] FitTolerance = [1, 2, 4, 7, 11];

    /// <summary>The stroke as Bézier curves: per curve x0, y0, x1, y1, x2, y2, x3, y3, pressure start, pressure end.</summary>
    public static List<double[]> Fit(IReadOnlyList<double[]> raw, double tolerance)
    {
        var res = new List<double[]>();
        var pts = Resample(raw, 1.5);
        if (pts.Count < 2) return res;
        var P = pts.Select(p => new Point(p[0], p[1])).ToArray();
        var pr = pts.Select(p => p[2]).ToArray();
        double err2 = tolerance * tolerance;

        // corners: where the direction turns sharply
        var cuts = new List<int> { 0 };
        const int k = 4;
        int lastCut = 0;
        for (int i = k; i < P.Length - k; i++)
        {
            var a = P[i] - P[i - k]; var b = P[i + k] - P[i];
            if (a.Length < 1e-6 || b.Length < 1e-6) continue;
            double ang = Math.Abs(Vector.AngleBetween(a, b));
            if (ang < 75 || i - lastCut < 2 * k) continue;
            // the sharpest point around here
            int best = i; double bestAng = ang;
            for (int j = i + 1; j < Math.Min(P.Length - k, i + k); j++)
            {
                var a2 = P[j] - P[j - k]; var b2 = P[j + k] - P[j];
                double g = Math.Abs(Vector.AngleBetween(a2, b2));
                if (g > bestAng) { bestAng = g; best = j; }
            }
            cuts.Add(best);
            lastCut = best;
            i = best + k;
        }
        cuts.Add(P.Length - 1);

        for (int c = 0; c + 1 < cuts.Count; c++)
        {
            int first = cuts[c], last = cuts[c + 1];
            if (last <= first) continue;
            var t1 = LeftTangent(P, first, last);
            var t2 = RightTangent(P, first, last);
            FitCubic(P, pr, first, last, t1, t2, err2, res, 0);
        }
        return res;
    }

    private static List<double[]> Resample(IReadOnlyList<double[]> raw, double step)
    {
        var outp = new List<double[]>();
        if (raw.Count == 0) return outp;
        static double Pr(double[] p) => p.Length > 2 ? p[2] : 0;
        outp.Add([raw[0][0], raw[0][1], Pr(raw[0])]);
        double carry = 0;
        for (int i = 1; i < raw.Count; i++)
        {
            double x0 = raw[i - 1][0], y0 = raw[i - 1][1], x1 = raw[i][0], y1 = raw[i][1];
            double len = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
            if (len < 1e-9) continue;
            double t = step - carry;
            while (t <= len)
            {
                double f = t / len;
                outp.Add([x0 + (x1 - x0) * f, y0 + (y1 - y0) * f, Pr(raw[i - 1]) + (Pr(raw[i]) - Pr(raw[i - 1])) * f]);
                t += step;
            }
            carry = len - (t - step);
        }
        var l = raw[^1];
        if (Math.Abs(outp[^1][0] - l[0]) + Math.Abs(outp[^1][1] - l[1]) > 0.3) outp.Add([l[0], l[1], Pr(l)]);
        return outp;
    }

    private static Vector LeftTangent(Point[] d, int first, int last)
    {
        var v = d[Math.Min(first + 3, last)] - d[first];
        if (v.Length < 1e-9) v = d[last] - d[first];
        if (v.Length > 1e-9) v.Normalize();
        return v;
    }

    private static Vector RightTangent(Point[] d, int first, int last)
    {
        var v = d[Math.Max(last - 3, first)] - d[last];
        if (v.Length < 1e-9) v = d[first] - d[last];
        if (v.Length > 1e-9) v.Normalize();
        return v;
    }

    private static void FitCubic(Point[] d, double[] pr, int first, int last, Vector t1, Vector t2, double err2, List<double[]> outp, int depth)
    {
        int n = last - first + 1;
        if (n == 2 || depth > 40)
        {
            double dist = (d[last] - d[first]).Length / 3;
            Add(outp, d[first], d[first] + t1 * dist, d[last] + t2 * dist, d[last], pr[first], pr[last]);
            return;
        }
        var u = ChordLength(d, first, last);
        var bez = Generate(d, first, last, u, t1, t2);
        double maxErr = MaxError(d, first, last, bez, u, out int split);
        if (maxErr < err2) { Add(outp, bez[0], bez[1], bez[2], bez[3], pr[first], pr[last]); return; }
        if (maxErr < err2 * 4)
        {
            for (int i = 0; i < 4; i++)
            {
                u = Reparameterize(d, first, last, u, bez);
                bez = Generate(d, first, last, u, t1, t2);
                maxErr = MaxError(d, first, last, bez, u, out split);
                if (maxErr < err2) { Add(outp, bez[0], bez[1], bez[2], bez[3], pr[first], pr[last]); return; }
            }
        }
        split = Math.Clamp(split, first + 1, last - 1);
        var center = d[split - 1] - d[split + 1];
        if (center.Length < 1e-9) center = d[first] - d[last];
        center.Normalize();
        FitCubic(d, pr, first, split, t1, center, err2, outp, depth + 1);
        FitCubic(d, pr, split, last, -center, t2, err2, outp, depth + 1);
    }

    private static void Add(List<double[]> outp, Point a, Point b, Point c, Point e, double p0, double p1) =>
        outp.Add([a.X, a.Y, b.X, b.Y, c.X, c.Y, e.X, e.Y, p0, p1]);

    private static double[] ChordLength(Point[] d, int first, int last)
    {
        var u = new double[last - first + 1];
        for (int i = first + 1; i <= last; i++) u[i - first] = u[i - first - 1] + (d[i] - d[i - 1]).Length;
        double total = u[^1];
        if (total > 0) for (int i = 1; i < u.Length; i++) u[i] /= total;
        return u;
    }

    private static Point[] Generate(Point[] d, int first, int last, double[] u, Vector t1, Vector t2)
    {
        double c00 = 0, c01 = 0, c11 = 0, x0 = 0, x1 = 0;
        Point p0 = d[first], p3 = d[last];
        for (int i = 0; i < u.Length; i++)
        {
            double t = u[i], mt = 1 - t;
            double b0 = mt * mt * mt, b1 = 3 * t * mt * mt, b2 = 3 * t * t * mt, b3 = t * t * t;
            var a0 = t1 * b1; var a1 = t2 * b2;
            c00 += a0 * a0; c01 += a0 * a1; c11 += a1 * a1;
            var tmp = d[first + i] - new Point(p0.X * (b0 + b1) + p3.X * (b2 + b3), p0.Y * (b0 + b1) + p3.Y * (b2 + b3));
            x0 += a0 * tmp; x1 += a1 * tmp;
        }
        double det = c00 * c11 - c01 * c01;
        double al = det == 0 ? 0 : (x0 * c11 - x1 * c01) / det;
        double ar = det == 0 ? 0 : (c00 * x1 - c01 * x0) / det;
        double seg = (p3 - p0).Length, eps = 1e-6 * seg;
        if (al < eps || ar < eps) al = ar = seg / 3;
        return [p0, p0 + t1 * al, p3 + t2 * ar, p3];
    }

    private static Point Q(Point[] b, double t)
    {
        double mt = 1 - t;
        double b0 = mt * mt * mt, b1 = 3 * t * mt * mt, b2 = 3 * t * t * mt, b3 = t * t * t;
        return new Point(b[0].X * b0 + b[1].X * b1 + b[2].X * b2 + b[3].X * b3, b[0].Y * b0 + b[1].Y * b1 + b[2].Y * b2 + b[3].Y * b3);
    }

    private static double MaxError(Point[] d, int first, int last, Point[] bez, double[] u, out int split)
    {
        double max = 0;
        split = (first + last) / 2;
        for (int i = first + 1; i < last; i++)
        {
            double e = (Q(bez, u[i - first]) - d[i]).LengthSquared;
            if (e >= max) { max = e; split = i; }
        }
        return max;
    }

    private static double[] Reparameterize(Point[] d, int first, int last, double[] u, Point[] b)
    {
        var q1 = new[] { new Point(3 * (b[1].X - b[0].X), 3 * (b[1].Y - b[0].Y)), new Point(3 * (b[2].X - b[1].X), 3 * (b[2].Y - b[1].Y)), new Point(3 * (b[3].X - b[2].X), 3 * (b[3].Y - b[2].Y)) };
        var q2 = new[] { new Point(2 * (q1[1].X - q1[0].X), 2 * (q1[1].Y - q1[0].Y)), new Point(2 * (q1[2].X - q1[1].X), 2 * (q1[2].Y - q1[1].Y)) };
        var outp = new double[u.Length];
        for (int i = 0; i < u.Length; i++)
        {
            double t = u[i], mt = 1 - t;
            var qt = Q(b, t);
            var q1t = new Point(q1[0].X * mt * mt + q1[1].X * 2 * t * mt + q1[2].X * t * t, q1[0].Y * mt * mt + q1[1].Y * 2 * t * mt + q1[2].Y * t * t);
            var q2t = new Point(q2[0].X * mt + q2[1].X * t, q2[0].Y * mt + q2[1].Y * t);
            var diff = qt - d[first + i];
            double num = diff.X * q1t.X + diff.Y * q1t.Y;
            double den = q1t.X * q1t.X + q1t.Y * q1t.Y + diff.X * q2t.X + diff.Y * q2t.Y;
            outp[i] = Math.Abs(den) < 1e-12 ? t : Math.Clamp(t - num / den, 0, 1);
        }
        return outp;
    }

    /// <summary>The fitted curves sampled densely (x, y, pressure), ready for the nib sweep.</summary>
    public static List<double[]> SampleFit(IReadOnlyList<double[]> fit)
    {
        var outp = new List<double[]>();
        foreach (var c in fit)
        {
            Point[] b = [new(c[0], c[1]), new(c[2], c[3]), new(c[4], c[5]), new(c[6], c[7])];
            double len = (b[1] - b[0]).Length + (b[2] - b[1]).Length + (b[3] - b[2]).Length;
            int n = Math.Max(3, (int)Math.Ceiling(len / 1.5));
            for (int i = outp.Count == 0 ? 0 : 1; i <= n; i++)
            {
                double t = i / (double)n;
                var p = Q(b, t);
                outp.Add([p.X, p.Y, c[8] + (c[9] - c[8]) * t]);
            }
        }
        return outp;
    }

    /// <summary>The shape of a saved stroke: its fitted curves when it has them, else its raw points smoothed.</summary>
    public static Geometry ForSegment(Segment s) =>
        s.Fit is { Count: > 0 } f ? Build(SampleFit(f), s.Brush!, 0) : Build(s.Stroke!, s.Brush!, s.Fidelity);

    /// <summary>All the strokes of a letter as one filled outline (overlaps merged).</summary>
    public static Geometry Outline(IEnumerable<Segment> strokes)
    {
        var all = new GeometryGroup { FillRule = FillRule.Nonzero };
        foreach (var s in strokes)
            if (s.Stroke is { Count: > 0 } && s.Brush != null) all.Children.Add(ForSegment(s));
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
