using System.Windows;
using Neuraink.App.Services;
using Neuraink.Core;
using CommunityToolkit.Mvvm.Input;

namespace Neuraink.App.ViewModels;

/// <summary>
/// Scissors (C) and Knife (K): the calligrapher writes the whole piece freely, then cuts it into letters. The pieces of a
/// stroke go to the letters in writing order by themselves, and every cut between two letters is where the first one leaves
/// and the next one enters (its exit and entry points). A right click on a piece shows the letters, to check or change it.
/// </summary>
public sealed partial class AnnotateViewModel
{
    public bool IsScissorsMode => EditMode == "scissors";
    public bool IsKnifeMode => EditMode == "knife";

    [RelayCommand] public void UseScissors() => EditMode = "scissors";
    [RelayCommand] public void UseKnife() => EditMode = "knife";

    /// <summary>The + of the studio: a folder of pieces added to a style folder of the library.</summary>
    [RelayCommand] private Task AddToLibrary() => _main.AddToLibrary(folder: true);

    /// <summary>The cut just made (a small mark shows there for a moment).</summary>
    public Point? LastCut { get; private set; }

    /// <summary>Scissors: a click on a stroke cuts it there (its shape does not change). False when no stroke is there.</summary>
    public bool CutAt(Point p, double tol)
    {
        var hit = NearestOnStrokes(p, tol);
        if (hit == null) return false;
        Checkpoint();
        Cut(hit.Value.Seg, hit.Value.Curve, hit.Value.T);
        AssignPieces();
        Refresh();
        return true;
    }

    /// <summary>Knife: a line drawn across the strokes cuts every one it crosses, where it crosses.</summary>
    public int KnifeCut(IReadOnlyList<Point> line)
    {
        if (line.Count < 2) return 0;
        var points = new List<Point>();
        foreach (var s in Ann.Segments.Where(IsStrokeKind).ToList())
        {
            if (s.Fit is not { Count: > 0 } fit) continue;
            var spine = StrokeGeometry.SampleFit(fit);
            for (int i = 1; i < spine.Count; i++)
                for (int j = 1; j < line.Count; j++)
                    if (Cross(new Point(spine[i - 1][0], spine[i - 1][1]), new Point(spine[i][0], spine[i][1]), line[j - 1], line[j]) is { } x)
                        points.Add(x);
        }
        if (points.Count == 0) return 0;
        Checkpoint();
        int n = 0;
        foreach (var x in points)
            if (NearestOnStrokes(x, 0.75) is { } h)
            {
                Cut(h.Seg, h.Curve, h.T);
                n++;
            }
        AssignPieces();
        Refresh();
        return n;
    }

    private static Point? Cross(Point a, Point b, Point c, Point d)
    {
        double den = (b.X - a.X) * (d.Y - c.Y) - (b.Y - a.Y) * (d.X - c.X);
        if (Math.Abs(den) < 1e-12) return null;
        double t = ((c.X - a.X) * (d.Y - c.Y) - (c.Y - a.Y) * (d.X - c.X)) / den;
        double u = ((c.X - a.X) * (b.Y - a.Y) - (c.Y - a.Y) * (b.X - a.X)) / den;
        return t is >= 0 and <= 1 && u is >= 0 and <= 1 ? new Point(a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y)) : null;
    }

    /// <summary>The place on a stroke's path nearest to a point: the stroke, its curve and the parameter on it.</summary>
    private (Segment Seg, int Curve, double T)? NearestOnStrokes(Point p, double tol)
    {
        (Segment, int, double)? best = null;
        double bestD = double.MaxValue;
        foreach (var s in Ann.Segments.Where(IsStrokeKind))
        {
            if (s.Fit is not { Count: > 0 } fit) continue;
            double reach = Math.Max(tol, (s.Brush?.Size ?? 0) / 2);
            for (int i = 0; i < fit.Count; i++)
            {
                var c = fit[i];
                for (int k = 0; k <= 60; k++)
                {
                    double t = k / 60.0, u = 1 - t;
                    double x = u * u * u * c[0] + 3 * u * u * t * c[2] + 3 * u * t * t * c[4] + t * t * t * c[6];
                    double y = u * u * u * c[1] + 3 * u * u * t * c[3] + 3 * u * t * t * c[5] + t * t * t * c[7];
                    double d = Math.Sqrt((x - p.X) * (x - p.X) + (y - p.Y) * (y - p.Y));
                    if (d <= reach && d < bestD) { bestD = d; best = (s, i, t); }
                }
            }
        }
        // the ends of a stroke are not cut (nothing would be left on one side)
        if (best is { } b && b.Item1.Fit is { } f && ((b.Item2 == 0 && b.Item3 < 0.01) || (b.Item2 == f.Count - 1 && b.Item3 > 0.99))) return null;
        return best;
    }

    /// <summary>Cuts a stroke into two pieces at curve i, parameter t (de Casteljau: the shape stays exactly the same).</summary>
    private void Cut(Segment s, int i, double t)
    {
        var fit = s.Fit!;
        var c = fit[i];
        Point L(Point a, Point b) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
        Point p0 = new(c[0], c[1]), p1 = new(c[2], c[3]), p2 = new(c[4], c[5]), p3 = new(c[6], c[7]);
        Point q0 = L(p0, p1), q1 = L(p1, p2), q2 = L(p2, p3), r0 = L(q0, q1), r1 = L(q1, q2), m = L(r0, r1);
        double pm = c[8] + (c[9] - c[8]) * t;
        var fa = fit.Take(i).Select(x => (double[])x.Clone()).ToList();
        if (t > 1e-4) fa.Add([p0.X, p0.Y, q0.X, q0.Y, r0.X, r0.Y, m.X, m.Y, c[8], pm]);
        var fb = new List<double[]>();
        if (t < 1 - 1e-4) fb.Add([m.X, m.Y, r1.X, r1.Y, q2.X, q2.Y, p3.X, p3.Y, pm, c[9]]);
        fb.AddRange(fit.Skip(i + 1).Select(x => (double[])x.Clone()));
        if (fa.Count == 0 || fb.Count == 0) return;

        // where along the first, whole stroke this cut is (to keep the pieces in writing order)
        double lenA = PathLength(fa);
        int root = s.CutFrom != 0 ? s.CutFrom : s.Order;
        var a = Clone(s); var b = Clone(s);
        a.Fit = fa; b.Fit = fb;
        a.CutFrom = b.CutFrom = root;
        a.CutPos = s.CutPos;
        b.CutPos = s.CutPos + lenA;
        a.Stroke = StrokeGeometry.SampleFit(fa).Select(q => new[] { Math.Round(q[0], 2), Math.Round(q[1], 2), Math.Round(q[2], 3), 0 }).ToList();
        b.Stroke = StrokeGeometry.SampleFit(fb).Select(q => new[] { Math.Round(q[0], 2), Math.Round(q[1], 2), Math.Round(q[2], 3), 0 }).ToList();
        a.Raw = s.Raw ?? s.Stroke;   // the hand as it moved stays with both pieces
        b.Raw = null;
        a.Exit = [Math.Round(m.X, 2), Math.Round(m.Y, 2)];
        b.Entry = [Math.Round(m.X, 2), Math.Round(m.Y, 2)];
        b.Exit = s.Exit;
        b.Manual = false;
        // the two pieces take the stroke's place; the orders follow the writing order again
        int at = Ann.Segments.IndexOf(s);
        Ann.Segments[at] = a;
        Ann.Segments.Insert(at + 1, b);
        Renumber(a, b);
        LastCut = m;
        SelectedOrder = 0;
        Selection.Clear();
    }

    private static double PathLength(List<double[]> fit)
    {
        var pts = StrokeGeometry.SampleFit(fit);
        double len = 0;
        for (int k = 1; k < pts.Count; k++) len += Math.Sqrt((pts[k][0] - pts[k - 1][0]) * (pts[k][0] - pts[k - 1][0]) + (pts[k][1] - pts[k - 1][1]) * (pts[k][1] - pts[k - 1][1]));
        return len;
    }

    /// <summary>The strokes numbered again in writing order, the new piece b right after a.</summary>
    private void Renumber(Segment a, Segment b)
    {
        var strokes = Ann.Segments.Where(IsStrokeKind).Where(x => x != b).OrderBy(x => x.Order).ToList();
        strokes.Insert(strokes.IndexOf(a) + 1, b);
        for (int k = 0; k < strokes.Count; k++) strokes[k].Order = k + 1;
        _strokeOrder = strokes.Count;
    }

    /// <summary>
    /// The pieces of each cut stroke go to the letters in writing order: the first keeps the stroke's letter, the next ones
    /// the letters after it. Pieces whose letter the user chose stay as they are; the joins between letters are kept.
    /// </summary>
    private void AssignPieces()
    {
        foreach (var family in Ann.Segments.Where(x => x.Kind == "stroke" && x.CutFrom != 0).GroupBy(x => x.CutFrom))
        {
            var list = family.OrderBy(x => x.CutPos).ToList();
            int last = Math.Max(0, Chips.Count - 1);
            int chip = Math.Clamp(list[0].Chip, 0, last);
            for (int k = 0; k < list.Count; k++)
            {
                var piece = list[k];
                if (piece.Manual) { chip = piece.Chip; continue; }
                if (k > 0) chip = Math.Min(chip + 1, last);
                SetLetter(piece, chip, "stroke");
            }
        }
    }

    private void SetLetter(Segment s, int chip, string kind)
    {
        var c = ChipAt(chip);
        s.Kind = kind;
        if (kind == "ornament" || c == null) { s.Chip = kind == "ornament" ? -1 : s.Chip; s.Letter = ""; s.Form = ""; return; }
        s.Chip = chip;
        s.Letter = c.Ch;
        s.Form = Arabic.FormKey(c.Form);
    }

    // ------------------------------------------------------------------ right click on a drawing: its letter
    /// <summary>What a right click lands on (a stroke or a pen shape), with the drawings it applies to (the selection, if it is in it).</summary>
    public List<Segment> TargetsAt(Point p, double tol)
    {
        var hit = HitAny(p, tol);
        if (hit == null) return [];
        if (Selection.Contains(hit)) return [.. Selection];
        return [hit];
    }

    /// <summary>The letter, tashkeel or ornament of these drawings, chosen in the letters menu (it overrides the automatic one).</summary>
    public void AssignLetter(IReadOnlyList<Segment> targets, int chip, string kind)
    {
        if (targets.Count == 0) return;
        Checkpoint();
        foreach (var s in targets)
        {
            if (IsStrokeKind(s))
            {
                SetLetter(s, chip, kind);
                s.Manual = true;
            }
            else if (s.Kind is "letter" or "dots" && chip >= 0)
            {
                var c = ChipAt(chip);
                if (c == null) continue;
                s.Chip = chip; s.Letter = c.Ch; s.Form = Arabic.FormKey(c.Form);
            }
        }
        AssignPieces();
        if (kind != "ornament" && chip >= 0 && chip < Chips.Count) Active = chip;
        Refresh();
    }

    /// <summary>The letter a drawing belongs to now (the menu ticks it): its chip and kind.</summary>
    public static (int Chip, string Kind) LetterOf(Segment s) => (s.Chip, s.Kind);
}
