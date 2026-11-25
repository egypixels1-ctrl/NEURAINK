using System.Globalization;
using System.Text;

namespace Neuraink.Core;

/// <summary>
/// Bitmap → vector (like vtracer / potrace, for black ink on white): the outlines of the ink are followed along the pixel
/// edges, smoothed, split at real corners, and fitted with cubic Bézier curves (Schneider's algorithm) within a fraction of
/// a pixel. Holes are kept (even-odd fill). The result is an SVG the size of the picture, pure black on nothing.
/// </summary>
public static class Tracer
{
    public sealed record Options(double MaxError = 1.0, double CornerAngle = 75, int CornerWindow = 5, double Sigma = 2.0, double MinArea = 4);

    /// <summary>The ink of a picture as SVG (ink[y * width + x] = true for black).</summary>
    public static string ToSvg(bool[] ink, int width, int height, Options? o = null)
    {
        var d = PathData(ink, width, height, o);
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\">\n");
        sb.Append("<path fill=\"#000000\" fill-rule=\"evenodd\" d=\"").Append(d).Append("\"/>\n</svg>\n");
        return sb.ToString();
    }

    /// <summary>The ink as path data: one closed sub-path per outline (outer edges and holes).</summary>
    public static string PathData(bool[] ink, int width, int height, Options? o = null)
    {
        o ??= new Options();
        var sb = new StringBuilder();
        foreach (var curves in Trace(ink, width, height, o))
        {
            if (curves.Count == 0) continue;
            sb.Append('M').Append(F(curves[0][0])).Append(' ').Append(F(curves[0][1]));
            foreach (var c in curves)
                sb.Append('C').Append(F(c[2])).Append(' ').Append(F(c[3])).Append(' ').Append(F(c[4])).Append(' ').Append(F(c[5]))
                  .Append(' ').Append(F(c[6])).Append(' ').Append(F(c[7]));
            sb.Append('Z');
        }
        return sb.ToString();
    }

    private static string F(double v) => Math.Round(v, 2).ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Every outline as Bézier curves: x0, y0, x1, y1, x2, y2, x3, y3.</summary>
    public static List<List<double[]>> Trace(bool[] ink, int width, int height, Options? o = null)
    {
        o ??= new Options();
        var res = new List<List<double[]>>();
        foreach (var loop in Loops(ink, width, height))
        {
            if (Math.Abs(Area(loop)) < o.MinArea) continue;
            int n = loop.Count;
            // the middles of the pixel edges: a staircase becomes a slope
            var m = new List<double[]>(n);
            for (int i = 0; i < n; i++)
            {
                var a = loop[i]; var b = loop[(i + 1) % n];
                m.Add([(a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0]);
            }
            var curves = new List<double[]>();
            var cs = Corners(m, o.CornerWindow, o.CornerAngle);
            if (cs.Count == 0)
            {
                var s = Smooth(m, closed: true, o.Sigma);
                int half = n / 2;
                double[] Tan(int i) => Unit(Sub(s[(i + 1) % n], s[(i - 1 + n) % n]));
                FitCubic(s.GetRange(0, half + 1), Tan(0), Neg(Tan(half)), o.MaxError, curves);
                var rest = s.GetRange(half, n - half); rest.Add(s[0]);
                FitCubic(rest, Tan(half), Neg(Tan(0)), o.MaxError, curves);
            }
            else
            {
                for (int j = 0; j < cs.Count; j++)
                {
                    int a = cs[j], b = cs[(j + 1) % cs.Count];
                    int len = (b - a + n) % n;
                    if (len == 0) len = n;
                    var piece = new List<double[]>(len + 1);
                    for (int k = 0; k <= len; k++) piece.Add(m[(a + k) % n]);
                    var p = Smooth(piece, closed: false, o.Sigma);
                    if (p.Count < 2) continue;
                    var t1 = Unit(Sub(p[Math.Min(2, p.Count - 1)], p[0]));
                    var t2 = Unit(Sub(p[Math.Max(0, p.Count - 3)], p[^1]));
                    FitCubic(p, t1, t2, o.MaxError, curves);
                }
            }
            if (curves.Count > 0) res.Add(curves);
        }
        return res;
    }

    // ---------------------------------------------------------------- outlines along the pixel edges
    private readonly record struct V(int X, int Y);

    /// <summary>Closed outlines; each edge has the ink on the same side (outer outlines and holes turn opposite ways).</summary>
    private static List<List<V>> Loops(bool[] ink, int w, int h)
    {
        bool At(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && ink[y * w + x];
        var outs = new Dictionary<V, List<V>>();
        void Add(V a, V b)
        {
            if (!outs.TryGetValue(a, out var l)) outs[a] = l = new List<V>(2);
            l.Add(b);
        }
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (!ink[y * w + x]) continue;
                if (!At(x, y - 1)) Add(new V(x, y), new V(x + 1, y));
                if (!At(x + 1, y)) Add(new V(x + 1, y), new V(x + 1, y + 1));
                if (!At(x, y + 1)) Add(new V(x + 1, y + 1), new V(x, y + 1));
                if (!At(x - 1, y)) Add(new V(x, y + 1), new V(x, y));
            }
        var loops = new List<List<V>>();
        // a stable start order (top-left first)
        var starts = outs.Keys.OrderBy(v => v.Y).ThenBy(v => v.X).ToList();
        foreach (var start in starts)
        {
            while (outs.ContainsKey(start))
            {
                var loop = new List<V> { start };
                var cur = start;
                int dx = 0, dy = 0;
                bool first = true;
                while (true)
                {
                    var l = outs[cur];
                    V next = l[0];
                    if (l.Count > 1 && !first)
                    {
                        // two ways on (pixels touching at a corner): turn towards the ink, so diagonal pixels stay apart
                        var right = new V(cur.X - dy, cur.Y + dx);
                        foreach (var c in l) if (c == right) { next = c; break; }
                    }
                    l.Remove(next);
                    if (l.Count == 0) outs.Remove(cur);
                    dx = next.X - cur.X; dy = next.Y - cur.Y;
                    first = false;
                    cur = next;
                    if (cur == start) break;
                    loop.Add(cur);
                    if (!outs.ContainsKey(cur)) break;   // should not happen: outlines are closed
                }
                if (loop.Count >= 4) loops.Add(loop);
            }
        }
        return loops;
    }

    private static double Area(List<V> p)
    {
        double s = 0;
        for (int i = 0; i < p.Count; i++) { var a = p[i]; var b = p[(i + 1) % p.Count]; s += (double)a.X * b.Y - (double)b.X * a.Y; }
        return s / 2;
    }

    /// <summary>Where the outline really turns (more than <paramref name="limit"/>° over ±k points): kept sharp.</summary>
    private static List<int> Corners(List<double[]> p, int k, double limit)
    {
        int n = p.Count;
        var ang = new double[n];
        for (int i = 0; i < n; i++)
        {
            var a = Sub(p[i], p[(i - k + n * 4) % n]);
            var b = Sub(p[(i + k) % n], p[i]);
            double na = Len(a), nb = Len(b);
            ang[i] = na < 1e-9 || nb < 1e-9 ? 0 : Math.Acos(Math.Clamp((a[0] * b[0] + a[1] * b[1]) / (na * nb), -1, 1)) * 180 / Math.PI;
        }
        var idx = new List<int>();
        if (n < 2 * k + 3) return idx;
        for (int i = 0; i < n; i++)
        {
            if (ang[i] <= limit) continue;
            bool max = true;
            for (int j = -k; j <= k && max; j++) if (ang[(i + j + n) % n] > ang[i]) max = false;
            if (!max) continue;
            if (idx.Count > 0 && i - idx[^1] <= k) continue;
            idx.Add(i);
        }
        if (idx.Count > 1 && idx[0] + n - idx[^1] <= k) idx.RemoveAt(idx.Count - 1);
        return idx;
    }

    /// <summary>Gaussian smoothing (the ends of an open piece stay where they are).</summary>
    private static List<double[]> Smooth(List<double[]> p, bool closed, double sigma)
    {
        int n = p.Count, r = Math.Max(1, (int)Math.Ceiling(3 * sigma));
        var w = new double[2 * r + 1];
        for (int j = -r; j <= r; j++) w[j + r] = Math.Exp(-0.5 * j * j / (sigma * sigma));
        var q = new List<double[]>(n);
        for (int i = 0; i < n; i++)
        {
            if (!closed && (i == 0 || i == n - 1)) { q.Add([p[i][0], p[i][1]]); continue; }
            int rr = closed ? r : Math.Min(r, Math.Min(i, n - 1 - i));
            double sx = 0, sy = 0, sw = 0;
            for (int j = -rr; j <= rr; j++)
            {
                var v = p[closed ? ((i + j) % n + n) % n : i + j];
                sx += w[j + r] * v[0]; sy += w[j + r] * v[1]; sw += w[j + r];
            }
            q.Add([sx / sw, sy / sw]);
        }
        return q;
    }

    // ---------------------------------------------------------------- Schneider curve fitting
    private static double[] Sub(double[] a, double[] b) => [a[0] - b[0], a[1] - b[1]];
    private static double[] Neg(double[] a) => [-a[0], -a[1]];
    private static double Len(double[] a) => Math.Sqrt(a[0] * a[0] + a[1] * a[1]);
    private static double[] Unit(double[] a) { var l = Len(a); return l < 1e-12 ? new double[] { 1, 0 } : new double[] { a[0] / l, a[1] / l }; }

    private static double[] Q(double[][] c, double t)
    {
        double mt = 1 - t, b0 = mt * mt * mt, b1 = 3 * mt * mt * t, b2 = 3 * mt * t * t, b3 = t * t * t;
        return [b0 * c[0][0] + b1 * c[1][0] + b2 * c[2][0] + b3 * c[3][0], b0 * c[0][1] + b1 * c[1][1] + b2 * c[2][1] + b3 * c[3][1]];
    }

    private static void FitCubic(List<double[]> p, double[] t1, double[] t2, double err, List<double[]> outp, int depth = 0)
    {
        if (p.Count < 2) return;
        if (p.Count == 2 || depth > 40)
        {
            double d = Len(Sub(p[^1], p[0])) / 3;
            Emit([p[0], [p[0][0] + t1[0] * d, p[0][1] + t1[1] * d], [p[^1][0] + t2[0] * d, p[^1][1] + t2[1] * d], p[^1]], outp);
            return;
        }
        var u = Chord(p);
        var c = Generate(p, u, t1, t2);
        int split = p.Count / 2;
        for (int it = 0; it < 5; it++)
        {
            double max = 0;
            for (int i = 0; i < p.Count; i++)
            {
                var q = Q(c, u[i]);
                double e = Len(Sub(q, p[i]));
                if (e > max) { max = e; split = i; }
            }
            if (max < err) { Emit(c, outp); return; }
            if (max > err * 4) break;
            u = Reparameterize(c, p, u);
            c = Generate(p, u, t1, t2);
        }
        split = Math.Clamp(split, 1, p.Count - 2);
        var tc = Unit(Sub(p[split - 1], p[split + 1]));
        FitCubic(p.GetRange(0, split + 1), t1, tc, err, outp, depth + 1);
        FitCubic(p.GetRange(split, p.Count - split), Neg(tc), t2, err, outp, depth + 1);
    }

    private static void Emit(double[][] c, List<double[]> outp) =>
        outp.Add([c[0][0], c[0][1], c[1][0], c[1][1], c[2][0], c[2][1], c[3][0], c[3][1]]);

    private static double[] Chord(List<double[]> p)
    {
        var u = new double[p.Count];
        for (int i = 1; i < p.Count; i++) u[i] = u[i - 1] + Len(Sub(p[i], p[i - 1]));
        double total = u[^1];
        for (int i = 1; i < p.Count; i++) u[i] = total > 0 ? u[i] / total : i / (double)(p.Count - 1);
        return u;
    }

    private static double[][] Generate(List<double[]> p, double[] u, double[] t1, double[] t2)
    {
        double c00 = 0, c01 = 0, c11 = 0, x0 = 0, x1 = 0;
        var first = p[0]; var last = p[^1];
        for (int i = 0; i < p.Count; i++)
        {
            double t = u[i], mt = 1 - t;
            double b0 = mt * mt * mt, b1 = 3 * mt * mt * t, b2 = 3 * mt * t * t, b3 = t * t * t;
            double[] a1 = [t1[0] * b1, t1[1] * b1], a2 = [t2[0] * b2, t2[1] * b2];
            c00 += a1[0] * a1[0] + a1[1] * a1[1];
            c01 += a1[0] * a2[0] + a1[1] * a2[1];
            c11 += a2[0] * a2[0] + a2[1] * a2[1];
            double tx = p[i][0] - (first[0] * (b0 + b1) + last[0] * (b2 + b3));
            double ty = p[i][1] - (first[1] * (b0 + b1) + last[1] * (b2 + b3));
            x0 += a1[0] * tx + a1[1] * ty;
            x1 += a2[0] * tx + a2[1] * ty;
        }
        double det = c00 * c11 - c01 * c01;
        double seg = Len(Sub(last, first)), eps = 1e-6 * seg;
        double al = 0, ar = 0;
        if (Math.Abs(det) > 1e-12) { al = (x0 * c11 - x1 * c01) / det; ar = (c00 * x1 - c01 * x0) / det; }
        if (al < eps || ar < eps) al = ar = seg / 3;
        return [first, [first[0] + t1[0] * al, first[1] + t1[1] * al], [last[0] + t2[0] * ar, last[1] + t2[1] * ar], last];
    }

    private static double[] Reparameterize(double[][] c, List<double[]> p, double[] u)
    {
        var r = new double[u.Length];
        for (int i = 0; i < u.Length; i++)
        {
            double t = u[i], mt = 1 - t;
            var q = Q(c, t);
            double d1x = 3 * (mt * mt * (c[1][0] - c[0][0]) + 2 * mt * t * (c[2][0] - c[1][0]) + t * t * (c[3][0] - c[2][0]));
            double d1y = 3 * (mt * mt * (c[1][1] - c[0][1]) + 2 * mt * t * (c[2][1] - c[1][1]) + t * t * (c[3][1] - c[2][1]));
            double d2x = 6 * (mt * (c[2][0] - 2 * c[1][0] + c[0][0]) + t * (c[3][0] - 2 * c[2][0] + c[1][0]));
            double d2y = 6 * (mt * (c[2][1] - 2 * c[1][1] + c[0][1]) + t * (c[3][1] - 2 * c[2][1] + c[1][1]));
            double ex = q[0] - p[i][0], ey = q[1] - p[i][1];
            double num = ex * d1x + ey * d1y, den = d1x * d1x + d1y * d1y + ex * d2x + ey * d2y;
            r[i] = Math.Abs(den) > 1e-12 ? Math.Clamp(t - num / den, 0, 1) : t;
        }
        return r;
    }
}
