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
        var sweep = new NibSweep(tip);
        foreach (var p in pts) sweep.Add(p);
        sweep.Group.Freeze();
        return sweep.Group;
    }

    /// <summary>
    /// The nib swept point by point. While drawing, only the new points are added (the stroke so far is not built again),
    /// so the brush keeps up with the hand however long the stroke is.
    /// </summary>
    public sealed class NibSweep
    {
        private readonly BrushTip _tip;
        private readonly double _cos, _sin, _r;
        private Point? _last;
        private double _lastScale;
        public GeometryGroup Group { get; } = new() { FillRule = FillRule.Nonzero };
        public int Count { get; private set; }

        public NibSweep(BrushTip tip)
        {
            _tip = tip;
            double a = tip.Angle * Math.PI / 180;
            _cos = Math.Cos(a); _sin = Math.Sin(a);
            _r = Math.Clamp(tip.Roundness, 0.02, 1);
        }

        public void Add(double[] p)
        {
            Count++;
            var c = new Point(p[0], p[1]);
            double s = p.Length > 2 && p[2] > 0 ? Math.Clamp(p[2] * 2, 0.25, 2) : 1;
            if (_last is { } p0)
            {
                var d = c - p0;
                if (d.Length < 1e-6) return;
                var n = new Vector(-d.Y, d.X);
                n.Normalize();
                Vector o0 = Support(n, _lastScale), o1 = Support(n, s);
                // every piece must turn the same way as the ellipses: with the non-zero rule, opposite turns cancel out (holes)
                Point[] q = [p0 + o0, c + o1, c - o1, p0 - o0];
                if (Math.Sign(SignedArea(q)) != EllipseTurn) Array.Reverse(q);
                var quad = new StreamGeometry();
                using (var g = quad.Open())
                {
                    g.BeginFigure(q[0], true, true);
                    g.PolyLineTo(new[] { q[1], q[2], q[3] }, true, false);
                }
                quad.Freeze();
                Group.Children.Add(quad);
            }
            // nib ellipse at the point (Y axis of the picture points down: the angle turns counter-clockwise on screen)
            var e = new EllipseGeometry(c, _tip.Size * s / 2, _tip.Size * s * _r / 2) { Transform = new RotateTransform(-_tip.Angle, c.X, c.Y) };
            e.Freeze();
            Group.Children.Add(e);
            _last = c;
            _lastScale = s;
        }

        // the point of the nib ellipse farthest along the normal n (support point), relative to the centre
        private Vector Support(Vector n, double s)
        {
            double A = _tip.Size * s / 2, B = _tip.Size * s * _r / 2;
            double nx = n.X * _cos - n.Y * _sin, ny = n.X * _sin + n.Y * _cos;
            double ux = A * nx, uy = B * ny, len = Math.Sqrt(ux * ux + uy * uy);
            if (len < 1e-9) return new Vector(0, 0);
            double lx = A * ux / len, ly = B * uy / len;
            return new Vector(lx * _cos + ly * _sin, -lx * _sin + ly * _cos);
        }
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
    // Stroke fitting, like the Illustrator Paintbrush (and better): when the stroke ends, the hand's path is replaced by
    // a smoothing cubic B-spline fitted by least squares — few control points, and continuous curvature everywhere
    // (no kinks where two curves meet) — then written as Bézier curves. Real cusps (where the pen turns back) stay sharp.
    // Fidelity sets the distance between knots (in screen pixels) and how far the curve may stray from the hand.
    // Tuned on real strokes of the user (piece 2133, «ح»).
    // ======================================================================================================

    /// <summary>Screen pixels between knots, Accurate … Smooth.</summary>
    private static readonly double[] KnotSpacing = [30, 45, 60, 75, 90];
    /// <summary>Largest distance from the hand's path allowed, screen pixels, Accurate … Smooth.</summary>
    private static readonly double[] MaxDeviation = [3, 5, 8, 10, 12];

    /// <summary>The stroke as Bézier curves: per curve x0, y0, x1, y1, x2, y2, x3, y3, pressure start, pressure end.</summary>
    /// <param name="smoothing">
    /// The smoothing applied when the hand leaves the paper, 0 … 1 (like the Illustrator Paintbrush): the path is evened out
    /// (a Gaussian up to 10 screen pixels wide, cusps kept) and fitted with fewer, longer curves. Nothing lags while drawing.
    /// </param>
    public static List<double[]> Fit(IReadOnlyList<double[]> raw, double zoom, int fidelity, double smoothing = 0)
    {
        var res = new List<double[]>();
        var q = Resample(raw, 1.0);
        if (q.Count < 3) return res;
        zoom = Math.Max(0.05, zoom);
        int f = Math.Clamp(fidelity, 0, KnotSpacing.Length - 1);
        smoothing = Math.Clamp(smoothing, 0, 1);
        double spacing = KnotSpacing[f] / zoom * (1 + smoothing), tol = MaxDeviation[f] / zoom * (1 + 1.5 * smoothing);
        double sigma = smoothing * 10 / zoom;
        int w = Math.Max(3, (int)Math.Round(6 / zoom));

        var cuts = new List<int> { 0 };
        cuts.AddRange(Cusps(q, w));
        cuts.Add(q.Count - 1);
        for (int c = 0; c + 1 < cuts.Count; c++)
        {
            int a = cuts[c], b = cuts[c + 1];
            if (b - a < 2) continue;
            res.AddRange(FitPiece(Even(q.GetRange(a, b - a + 1), sigma), spacing, tol));
        }
        return res;
    }

    /// <summary>Where the pen turns back sharply (more than 110°): the stroke is fitted in separate pieces there.</summary>
    private static List<int> Cusps(List<double[]> q, int w)
    {
        var idx = new List<int>();
        double Ang(int j)
        {
            double ax = q[j][0] - q[j - w][0], ay = q[j][1] - q[j - w][1], bx = q[j + w][0] - q[j][0], by = q[j + w][1] - q[j][1];
            double na = Math.Sqrt(ax * ax + ay * ay), nb = Math.Sqrt(bx * bx + by * by);
            if (na < 1e-9 || nb < 1e-9) return 0;
            return Math.Acos(Math.Clamp((ax * bx + ay * by) / (na * nb), -1, 1)) * 180 / Math.PI;
        }
        int last = int.MinValue / 2;
        for (int i = w; i < q.Count - w; i++)
        {
            double g = Ang(i);
            if (g <= 110 || i - last <= 3 * w) continue;
            int best = i; double bestG = g;
            for (int j = i + 1; j < Math.Min(q.Count - w, i + w); j++) { var h = Ang(j); if (h > bestG) { bestG = h; best = j; } }
            idx.Add(best);
            last = best;
            i = best + w;
        }
        return idx;
    }

    /// <summary>One piece: a clamped cubic B-spline (least squares + a light bending penalty), knots made closer until it stays within the tolerance.</summary>
    private static IEnumerable<double[]> FitPiece(List<double[]> q, double spacing, double tol)
    {
        int m = q.Count;
        double len = 0;
        for (int i = 1; i < m; i++) len += Math.Sqrt(Sq(q[i][0] - q[i - 1][0]) + Sq(q[i][1] - q[i - 1][1]));
        var t = new double[m];
        for (int i = 0; i < m; i++) t[i] = m == 1 ? 0 : i / (double)(m - 1);

        double[] knots = [];
        double[][] ctrl = [];
        for (int it = 0; it < 6; it++)
        {
            int nseg = Math.Max(1, (int)Math.Round(len / spacing));
            knots = Knots(nseg);
            ctrl = Solve(q, t, knots, 0.5);
            double err = 0;
            for (int i = 0; i < m; i++)
            {
                var p = Eval(knots, ctrl, t[i]);
                err = Math.Max(err, Math.Sqrt(Sq(p[0] - q[i][0]) + Sq(p[1] - q[i][1])));
            }
            if (err <= tol || nseg > m / 3) break;
            spacing *= 0.75;
        }
        foreach (var b in ToBeziers(knots, ctrl))
            yield return [b[0][0], b[0][1], b[1][0], b[1][1], b[2][0], b[2][1], b[3][0], b[3][1], b[0][2], b[3][2]];
    }

    private static double Sq(double v) => v * v;

    /// <summary>Gaussian smoothing of a piece (1 px spacing); its two ends stay where they were drawn.</summary>
    private static List<double[]> Even(List<double[]> q, double sigma)
    {
        int n = q.Count;
        if (sigma <= 0.3 || n < 5) return q;
        int r = (int)Math.Ceiling(3 * sigma);
        var w = new double[2 * r + 1];
        for (int j = -r; j <= r; j++) w[j + r] = Math.Exp(-0.5 * j * j / (sigma * sigma));
        var outp = new List<double[]>(n) { q[0] };
        for (int i = 1; i < n - 1; i++)
        {
            int rr = Math.Min(r, Math.Min(i, n - 1 - i));
            double sx = 0, sy = 0, sp = 0, sw = 0;
            for (int j = -rr; j <= rr; j++)
            {
                var v = q[i + j]; double ww = w[j + r];
                sx += ww * v[0]; sy += ww * v[1]; sp += ww * v[2]; sw += ww;
            }
            outp.Add([sx / sw, sy / sw, sp / sw]);
        }
        outp.Add(q[^1]);
        return outp;
    }

    private static double[] Knots(int nseg)
    {
        var k = new List<double> { 0, 0, 0, 0 };
        for (int i = 1; i < nseg; i++) k.Add(i / (double)nseg);
        k.AddRange([1, 1, 1, 1]);
        return [.. k];
    }

    private static int Span(double[] k, int n, double u)
    {
        if (u >= k[n]) return n - 1;
        int lo = 3, hi = n;
        while (hi - lo > 1) { int mid = (lo + hi) / 2; if (u < k[mid]) hi = mid; else lo = mid; }
        return lo;
    }

    /// <summary>The four non-zero cubic basis functions at u (span s), "The NURBS Book" A2.2.</summary>
    private static double[] Basis(double[] k, int s, double u)
    {
        var N = new double[4]; var left = new double[4]; var right = new double[4];
        N[0] = 1;
        for (int j = 1; j <= 3; j++)
        {
            left[j] = u - k[s + 1 - j]; right[j] = k[s + j] - u;
            double saved = 0;
            for (int r = 0; r < j; r++)
            {
                double den = right[r + 1] + left[j - r];
                double tmp = den == 0 ? 0 : N[r] / den;
                N[r] = saved + right[r + 1] * tmp;
                saved = left[j - r] * tmp;
            }
            N[j] = saved;
        }
        return N;
    }

    private static double[] Eval(double[] k, double[][] c, double u)
    {
        int n = c.Length, s = Span(k, n, u);
        var N = Basis(k, s, u);
        var p = new double[3];
        for (int i = 0; i < 4; i++) for (int d = 0; d < 3; d++) p[d] += N[i] * c[s - 3 + i][d];
        return p;
    }

    /// <summary>Least squares with the ends held, plus a small penalty on the control polygon's bending.</summary>
    private static double[][] Solve(List<double[]> q, double[] t, double[] k, double lambda)
    {
        int n = k.Length - 4, m = q.Count;
        var A = new double[n, n];
        var rhs = new double[n, 3];
        for (int i = 0; i < m; i++)
        {
            double w = i == 0 || i == m - 1 ? 1e4 : 1;
            int s = Span(k, n, t[i]);
            var N = Basis(k, s, t[i]);
            for (int a = 0; a < 4; a++)
            {
                int ra = s - 3 + a;
                for (int b = 0; b < 4; b++) A[ra, s - 3 + b] += w * N[a] * N[b];
                for (int d = 0; d < 3; d++) rhs[ra, d] += w * N[a] * q[i][d];
            }
        }
        for (int i = 0; i + 2 < n; i++)   // second differences of the control points
        {
            double[] dv = [1, -2, 1];
            for (int a = 0; a < 3; a++) for (int b = 0; b < 3; b++) A[i + a, i + b] += lambda * dv[a] * dv[b];
        }
        // Gaussian elimination with partial pivoting
        var x = new double[n][];
        for (int col = 0; col < n; col++)
        {
            int piv = col;
            for (int r = col + 1; r < n; r++) if (Math.Abs(A[r, col]) > Math.Abs(A[piv, col])) piv = r;
            if (piv != col)
            {
                for (int cc = 0; cc < n; cc++) (A[col, cc], A[piv, cc]) = (A[piv, cc], A[col, cc]);
                for (int d = 0; d < 3; d++) (rhs[col, d], rhs[piv, d]) = (rhs[piv, d], rhs[col, d]);
            }
            double diag = A[col, col];
            if (Math.Abs(diag) < 1e-12) diag = 1e-12;
            for (int r = col + 1; r < n; r++)
            {
                double fct = A[r, col] / diag;
                if (fct == 0) continue;
                for (int cc = col; cc < n; cc++) A[r, cc] -= fct * A[col, cc];
                for (int d = 0; d < 3; d++) rhs[r, d] -= fct * rhs[col, d];
            }
        }
        for (int r = n - 1; r >= 0; r--)
        {
            x[r] = new double[3];
            for (int d = 0; d < 3; d++)
            {
                double sum = rhs[r, d];
                for (int cc = r + 1; cc < n; cc++) sum -= A[r, cc] * x[cc][d];
                x[r][d] = sum / (Math.Abs(A[r, r]) < 1e-12 ? 1e-12 : A[r, r]);
            }
        }
        return x;
    }

    /// <summary>The B-spline as Bézier curves (knot insertion until every interior knot has multiplicity 3).</summary>
    private static List<double[][]> ToBeziers(double[] knots, double[][] ctrl)
    {
        var k = knots.ToList();
        var P = ctrl.Select(c => (double[])c.Clone()).ToList();
        foreach (var u in knots.Skip(4).Take(knots.Length - 8).Distinct().ToList())
        {
            int mult = k.Count(x => x == u);
            for (int r = mult; r < 3; r++)
            {
                int s = -1;
                for (int i = 0; i < k.Count - 1; i++) if (k[i] <= u && u < k[i + 1]) s = i;
                var Q = new List<double[]>();
                for (int i = 0; i <= P.Count; i++)
                {
                    if (i <= s - 3) Q.Add(P[i]);
                    else if (i > s) Q.Add(P[i - 1]);
                    else
                    {
                        double a = (u - k[i]) / (k[i + 3] - k[i]);
                        Q.Add([(1 - a) * P[i - 1][0] + a * P[i][0], (1 - a) * P[i - 1][1] + a * P[i][1], (1 - a) * P[i - 1][2] + a * P[i][2]]);
                    }
                }
                k.Insert(s + 1, u);
                P = Q;
            }
        }
        var outp = new List<double[][]>();
        for (int i = 0; i + 3 < P.Count; i += 3) outp.Add([P[i], P[i + 1], P[i + 2], P[i + 3]]);
        return outp;
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
                double mt = 1 - t, b0 = mt * mt * mt, b1 = 3 * t * mt * mt, b2 = 3 * t * t * mt, b3 = t * t * t;
                outp.Add([b[0].X * b0 + b[1].X * b1 + b[2].X * b2 + b[3].X * b3, b[0].Y * b0 + b[1].Y * b1 + b[2].Y * b2 + b[3].Y * b3, c[8] + (c[9] - c[8]) * t]);
            }
        }
        return outp;
    }

    /// <summary>The shape of a saved stroke: its fitted curves when it has them, else its raw points smoothed.</summary>
    /// <summary>
    /// The shape of a stroke, kept until the stroke changes (its curves, points or brush): the picture is drawn again at every
    /// move of the mouse, and building every stroke again each time made the brush lag.
    /// </summary>
    public static Geometry ForSegment(Segment s)
    {
        var key = KeyOf(s);
        if (Cache.TryGetValue(s, out var hit) && hit.Key == key) return hit.Geometry;
        var g = s.Fit is { Count: > 0 } f ? Build(SampleFit(f), s.Brush!, 0) : Build(s.Stroke!, s.Brush!, s.Fidelity);
        Cache.AddOrUpdate(s, new Cached(key, g));
        return g;
    }

    private sealed record Cached(string Key, Geometry Geometry);
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Segment, Cached> Cache = new();

    private static string KeyOf(Segment s)
    {
        double sum = 0, wsum = 0;
        int n = 0, i = 0;
        foreach (var list in new[] { s.Fit, s.Fit is { Count: > 0 } ? null : s.Stroke })
        {
            if (list == null) continue;
            foreach (var v in list) { foreach (var x in v) { sum += x; wsum += x * (++i % 7 + 1); } n++; }
        }
        var t = s.Brush;
        return string.Create(CultureInfo.InvariantCulture, $"{n}|{sum:R}|{wsum:R}|{t?.Size}|{t?.Angle}|{t?.Roundness}|{s.Fidelity}");
    }

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
