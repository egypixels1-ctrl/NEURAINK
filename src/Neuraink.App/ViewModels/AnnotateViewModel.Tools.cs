using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Neuraink.App.Services;
using Neuraink.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Neuraink.App.ViewModels;

/// <summary>
/// The Illustrator tools of the tool bar besides the Pen and the Brush: the Selection tool (V) — pick, move, scale and turn
/// strokes and pen shapes — and the Curvature tool (Shift+~) — edit a stroke through its points, or build a new one point by
/// point. Any drawing picked makes its letter the active one.
/// </summary>
public sealed partial class AnnotateViewModel
{
    /// <summary>"" = draw (the Pen or the Brush, by the tool) · "select" · "curvature".</summary>
    [ObservableProperty] private string _editMode = "";
    public bool IsSelectMode => EditMode == "select";
    public bool IsCurvatureMode => EditMode == "curvature";
    /// <summary>The Pen / the Brush button lit: drawing with it (not selecting or bending).</summary>
    public bool IsDrawPen => EditMode == "" && IsPenTool;
    public bool IsDrawBrush => EditMode == "" && IsBrushTool;

    partial void OnEditModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsSelectMode)); OnPropertyChanged(nameof(IsCurvatureMode));
        OnPropertyChanged(nameof(IsDrawPen)); OnPropertyChanged(nameof(IsDrawBrush));
        OnPropertyChanged(nameof(IsScissorsMode)); OnPropertyChanged(nameof(IsKnifeMode));
        LastCut = null;
        if (value != "select") ReferenceSelected = false;
        CurvDraft.Clear();
        CurvPoint = -1;
        if (value != "") Drawing.Clear();
        if (value != "select") Selection.Clear();
        Redraw?.Invoke();
    }

    /// <summary>The Pen / the Brush buttons light by tool as well as by mode.</summary>
    private void NotifyModeButtons() { OnPropertyChanged(nameof(IsDrawPen)); OnPropertyChanged(nameof(IsDrawBrush)); }

    [RelayCommand]
    public void UseSelect()
    {
        // a stroke being bent stays picked
        var keep = SelectedStroke;
        EditMode = "select";
        if (keep != null) { Selection.Add(keep); SelectedOrder = 0; }
        Redraw?.Invoke();
    }

    [RelayCommand]
    public void UseCurvature()
    {
        var one = Selection.FirstOrDefault(IsStrokeKind);
        EditMode = "curvature";
        if (one != null) SelectedOrder = one.Order;
        Redraw?.Invoke();
    }

    // ================================================================== Selection tool (V)
    public List<Segment> Selection { get; } = [];
    public bool HasSelection => Selection.Count > 0 || ReferenceSelected;

    /// <summary>The reference picture of an artboard, taken in hand with the Selection tool (move, scale, Backspace).</summary>
    public bool ReferenceSelected { get; set; }

    public Rect ReferenceRect => Reference is { } r && _board != null
        ? new Rect(_board.RefX, _board.RefY, r.PixelWidth * _board.RefScale, r.PixelHeight * _board.RefScale) : Rect.Empty;

    /// <summary>A click on the reference picture (drawings above it are taken first).</summary>
    public bool HitReference(Point p) => IsArtboard && Reference != null && ReferenceRect.Contains(p);

    public void SelectReference(bool add)
    {
        if (!add) { Selection.Clear(); SelectedOrder = 0; }
        ReferenceSelected = true;
        Redraw?.Invoke();
    }

    private static bool IsShape(Segment s) => s.Brush == null && s.Points.Count >= 3;

    /// <summary>The ink of a stroke, or the area of a pen shape.</summary>
    public static Geometry ShapeOf(Segment s)
    {
        if (IsStrokeKind(s)) return StrokeGeometry.ForSegment(s);
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(s.Points[0][0], s.Points[0][1]), true, true);
            c.PolyLineTo(s.Points.Skip(1).Select(p => new Point(p[0], p[1])).ToList(), true, false);
        }
        g.Freeze();
        return g;
    }

    /// <summary>What lies under a point: strokes first (on top), the last drawn first.</summary>
    public Segment? HitAny(Point p, double tol)
    {
        var stroke = HitStroke(p, tol);
        if (stroke != null) return stroke;
        foreach (var s in Ann.Segments.Where(IsShape).Reverse())
        {
            var g = ShapeOf(s);
            if (g.FillContains(p) || g.StrokeContains(new Pen(System.Windows.Media.Brushes.Black, 2 * tol), p)) return s;
        }
        return null;
    }

    public Rect SelectionBounds()
    {
        var r = Rect.Empty;
        foreach (var s in Selection) r.Union(ShapeOf(s).Bounds);
        if (ReferenceSelected && !ReferenceRect.IsEmpty) r.Union(ReferenceRect);
        return r;
    }

    /// <summary>A click with the Selection tool: picks what is under it (Shift: adds or removes it); on nothing, none.</summary>
    public Segment? SelectAt(Point p, double tol, bool add)
    {
        var hit = HitAny(p, tol);
        if (!add) { if (hit == null || !Selection.Contains(hit)) { Selection.Clear(); ReferenceSelected = false; } }
        if (hit != null)
        {
            if (add && Selection.Contains(hit)) Selection.Remove(hit);
            else if (!Selection.Contains(hit)) Selection.Add(hit);
            FollowLetter(hit);
        }
        Redraw?.Invoke();
        return hit;
    }

    /// <summary>A marquee: everything it touches is picked.</summary>
    public void SelectIn(Rect r, bool add)
    {
        if (!add) { Selection.Clear(); ReferenceSelected = false; }
        foreach (var s in Ann.Segments.Where(x => IsStrokeKind(x) || IsShape(x)))
            if (!Selection.Contains(s) && ShapeOf(s).Bounds.IntersectsWith(r)) Selection.Add(s);
        if (!ReferenceRect.IsEmpty && r.Contains(ReferenceRect)) ReferenceSelected = true;   // the picture only when wholly inside
        if (Selection.FirstOrDefault() is { } first) FollowLetter(first);
        Redraw?.Invoke();
    }

    public void ClearSelection() { Selection.Clear(); ReferenceSelected = false; SelectedOrder = 0; CurvPoint = -1; Redraw?.Invoke(); }

    /// <summary>A drawing picked: its letter becomes the active one (what is drawn next goes to it).</summary>
    private void FollowLetter(Segment s)
    {
        if (s.Brush != null) AdoptBrushOf(s);
        if (s.Chip < 0 || s.Chip >= Chips.Count || s.Kind is "ornament" or "exclude" || s.Chip == Active) return;
        Active = s.Chip;
        Refresh();
    }

    private List<Segment>? _base;

    /// <summary>Start of a move / scale / turn: the picked drawings as they are now (undo comes back here).</summary>
    private (double X, double Y, double S)? _refBase;

    public void BeginTransform()
    {
        if (!HasSelection) return;
        Checkpoint();
        _base = Selection.Select(Clone).ToList();
        _refBase = ReferenceSelected && _board != null ? (_board.RefX, _board.RefY, _board.RefScale) : null;
    }

    /// <summary>The picked drawings, as they were at the start, moved by a matrix (move, scale, turn).</summary>
    public void ApplyTransform(Matrix m)
    {
        if (_base == null) return;
        for (int i = 0; i < Selection.Count && i < _base.Count; i++) CopyTransformed(_base[i], Selection[i], m);
        if (_refBase is { } rb && _board != null)
        {
            // the picture moves and scales with the rest (it does not turn)
            var tl = m.Transform(new Point(rb.X, rb.Y));
            _board.RefScale = Math.Clamp(rb.S * Math.Sqrt(Math.Abs(m.M11 * m.M22 - m.M12 * m.M21)), 0.02, 40);
            _board.RefX = tl.X; _board.RefY = tl.Y;
            NotifyReference();
        }
        Redraw?.Invoke();
    }

    public void EndTransform()
    {
        if (_base == null) return;
        _base = null;
        if (_refBase != null) { _refBase = null; SaveBoard(now: true); }
        foreach (var s in Selection.Where(IsStrokeKind)) s.Edited = true;
        Refresh();
    }

    /// <summary>Arrow keys: a step of 1 pixel (Shift: 10).</summary>
    public void Nudge(double dx, double dy)
    {
        if (!HasSelection) return;
        BeginTransform();
        ApplyTransform(new Matrix(1, 0, 0, 1, dx, dy));
        EndTransform();
    }

    public bool DeleteSelection()
    {
        if (!HasSelection) return false;
        Checkpoint();
        foreach (var s in Selection) Ann.Segments.Remove(s);
        Selection.Clear();
        if (ReferenceSelected && _board != null)
        {
            _board.Reference = null;
            Reference = null;
            ReferenceSelected = false;
            SaveBoard(now: true);
            NotifyReference();
        }
        Refresh();
        return true;
    }

    private static Segment Clone(Segment s) => JsonSerializer.Deserialize<Segment>(JsonSerializer.Serialize(s))!;

    /// <summary>
    /// Every point of a drawing through a matrix. A brush stroke keeps its look: the nib turns with it and grows with it
    /// (like "Scale Strokes" in Illustrator).
    /// </summary>
    private static void CopyTransformed(Segment src, Segment dst, Matrix m)
    {
        double[] Pt(double[] v, int pairs)
        {
            var r = (double[])v.Clone();
            for (int k = 0; k < pairs && 2 * k + 1 < r.Length; k++)
            {
                if (double.IsNaN(r[2 * k])) continue;
                var p = m.Transform(new Point(r[2 * k], r[2 * k + 1]));
                r[2 * k] = p.X; r[2 * k + 1] = p.Y;
            }
            return r;
        }
        dst.Points = src.Points.Select(v => Pt(v, 1)).ToList();
        dst.Bezier = src.Bezier?.Select(v => Pt(v, 3)).ToList();
        dst.Entry = src.Entry == null ? null : Pt(src.Entry, 1);
        dst.Exit = src.Exit == null ? null : Pt(src.Exit, 1);
        dst.Stroke = src.Stroke?.Select(v => Pt(v, 1)).ToList();
        dst.Raw = src.Raw?.Select(v => Pt(v, 1)).ToList();
        dst.Fit = src.Fit?.Select(v => Pt(v, 4)).ToList();
        if (src.Brush is { } b)
        {
            var t = b.Copy();
            double det = m.M11 * m.M22 - m.M12 * m.M21;
            t.Size = Math.Max(0.5, b.Size * Math.Sqrt(Math.Abs(det)));
            double turn = Math.Atan2(m.M12, m.M11) * 180 / Math.PI;   // clockwise on screen
            t.Angle = ((b.Angle - turn) % 360 + 360) % 360;
            dst.Brush = t;
        }
    }

    // ================================================================== Curvature tool (Shift+~)
    /// <summary>The point of the bent stroke in hand (−1: none).</summary>
    [ObservableProperty] private int _curvPoint = -1;
    partial void OnCurvPointChanged(int value) => Redraw?.Invoke();

    /// <summary>A new stroke being built point by point: each point and whether it is a corner.</summary>
    public List<(Point P, bool Corner)> CurvDraft { get; } = [];

    public static List<Point> Anchors(List<double[]> fit)
    {
        var a = new List<Point>(fit.Count + 1);
        for (int k = 0; k <= fit.Count; k++) a.Add(AnchorOf(fit, k));
        return a;
    }

    /// <summary>A corner: a point whose two handles do not make one straight line (or have no length).</summary>
    public static bool IsCorner(List<double[]> fit, int k)
    {
        var a = AnchorOf(fit, k);
        Point? hin = k > 0 ? new Point(fit[k - 1][4], fit[k - 1][5]) : null;
        Point? hout = k < fit.Count ? new Point(fit[k][2], fit[k][3]) : null;
        bool hasIn = hin is { } i && (i - a).Length > 1e-3, hasOut = hout is { } o && (o - a).Length > 1e-3;
        if (k == 0) return !hasOut;
        if (k == fit.Count) return !hasIn;
        if (!hasIn || !hasOut) return true;
        var u = a - hin!.Value; var v = hout!.Value - a;
        u.Normalize(); v.Normalize();
        return Vector.AngleBetween(u, v) is var g && Math.Abs(g) > 12;
    }

    /// <summary>The handles of point j as the Curvature tool sets them: the curve runs smoothly through its neighbours.</summary>
    private static void CurvHandles(List<double[]> fit, int j, bool corner)
    {
        int n = fit.Count;
        var A = Anchors(fit);
        var aj = A[j];
        if (corner)
        {
            if (j > 0) { fit[j - 1][4] = aj.X; fit[j - 1][5] = aj.Y; }
            if (j < n) { fit[j][2] = aj.X; fit[j][3] = aj.Y; }
            return;
        }
        if (n == 0) return;
        Vector dir;
        if (j == 0)
        {
            var t = A[1] - A[0];
            if (n >= 2) t = 2 * (A[1] - A[0]) - (A[2] - A[0]) / 2;
            dir = t;
        }
        else if (j == n)
        {
            var t = A[n] - A[n - 1];
            if (n >= 2) t = 2 * (A[n] - A[n - 1]) - (A[n] - A[n - 2]) / 2;
            dir = t;
        }
        else dir = A[j + 1] - A[j - 1];
        if (dir.Length < 1e-9) return;
        dir.Normalize();
        if (j > 0)
        {
            var h = aj - dir * ((aj - A[j - 1]).Length / 3);
            fit[j - 1][4] = h.X; fit[j - 1][5] = h.Y;
        }
        if (j < n)
        {
            var h = aj + dir * ((A[j + 1] - aj).Length / 3);
            fit[j][2] = h.X; fit[j][3] = h.Y;
        }
    }

    /// <summary>Curves through points, like the Curvature tool draws them.</summary>
    public static List<double[]> CurvFit(IReadOnlyList<(Point P, bool Corner)> pts)
    {
        var fit = new List<double[]>();
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            Point a = pts[i].P, b = pts[i + 1].P;
            fit.Add([a.X, a.Y, a.X, a.Y, b.X, b.Y, b.X, b.Y, 0, 0]);
        }
        if (fit.Count == 0) return fit;
        for (int j = 0; j < pts.Count; j++) CurvHandles(fit, j, pts[j].Corner);
        return fit;
    }

    private bool[] _curvCorners = [];

    /// <summary>
    /// A click with the Curvature tool on the bent stroke: its point (k), or a place on its path (a new point is put there).
    /// </summary>
    public (string Part, int K)? HitCurv(Point p, double r, double pathTol)
    {
        if (SelectedStroke?.Fit is not { Count: > 0 } fit) return null;
        var A = Anchors(fit);
        for (int k = 0; k < A.Count; k++) if ((A[k] - p).Length <= r) return ("anchor", k);
        int bestSeg = -1; double bestT = 0, bestD = pathTol;
        for (int i = 0; i < fit.Count; i++)
        {
            var c = fit[i];
            for (int s = 1; s < 40; s++)
            {
                double t = s / 40.0, u = 1 - t;
                double x = u * u * u * c[0] + 3 * u * u * t * c[2] + 3 * u * t * t * c[4] + t * t * t * c[6];
                double y = u * u * u * c[1] + 3 * u * u * t * c[3] + 3 * u * t * t * c[5] + t * t * t * c[7];
                double d = Math.Sqrt((x - p.X) * (x - p.X) + (y - p.Y) * (y - p.Y));
                if (d < bestD) { bestD = d; bestSeg = i; bestT = t; }
            }
        }
        if (bestSeg < 0) return null;
        return ("path", InsertPoint(fit, bestSeg, bestT));
    }

    /// <summary>A new point on the path: the curve is cut there, its shape unchanged (de Casteljau). Returns its index.</summary>
    private int InsertPoint(List<double[]> fit, int seg, double t)
    {
        Checkpoint();
        var c = fit[seg];
        Point p0 = new(c[0], c[1]), p1 = new(c[2], c[3]), p2 = new(c[4], c[5]), p3 = new(c[6], c[7]);
        Point L(Point a, Point b) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
        Point q0 = L(p0, p1), q1 = L(p1, p2), q2 = L(p2, p3), r0 = L(q0, q1), r1 = L(q1, q2), m = L(r0, r1);
        double pm = c[8] + (c[9] - c[8]) * t;
        fit[seg] = [p0.X, p0.Y, q0.X, q0.Y, r0.X, r0.Y, m.X, m.Y, c[8], pm];
        fit.Insert(seg + 1, [m.X, m.Y, r1.X, r1.Y, q2.X, q2.Y, p3.X, p3.Y, pm, c[9]]);
        _curvEdited = true;
        Redraw?.Invoke();
        return seg + 1;
    }

    private bool _curvEdited;

    /// <summary>A point taken in hand: which points are corners is kept while it moves.</summary>
    public void BeginCurvEdit(int k)
    {
        if (SelectedStroke?.Fit is not { Count: > 0 } fit) return;
        if (!_curvEdited) Checkpoint();
        _curvEdited = true;
        _curvCorners = Enumerable.Range(0, fit.Count + 1).Select(j => IsCorner(fit, j)).ToArray();
        CurvPoint = k;
    }

    /// <summary>The point follows the mouse; the curve bends smoothly through it and its neighbours.</summary>
    public void MoveCurvPoint(int k, Point to)
    {
        if (SelectedStroke?.Fit is not { Count: > 0 } fit || k < 0 || k > fit.Count) return;
        var a = AnchorOf(fit, k);
        var d = to - a;
        if (k < fit.Count) { fit[k][0] += d.X; fit[k][1] += d.Y; fit[k][2] += d.X; fit[k][3] += d.Y; }
        if (k > 0) { fit[k - 1][6] += d.X; fit[k - 1][7] += d.Y; fit[k - 1][4] += d.X; fit[k - 1][5] += d.Y; }
        int n = fit.Count;
        var near = new SortedSet<int> { k - 1, k, k + 1 };
        if (k <= 2) near.Add(0);
        if (k >= n - 2) near.Add(n);
        foreach (var j in near)
            if (j >= 0 && j <= n) CurvHandles(fit, j, j < _curvCorners.Length && _curvCorners[j]);
        Redraw?.Invoke();
    }

    /// <summary>Double click on a point: a smooth point becomes a corner, a corner becomes smooth.</summary>
    public void ToggleCorner(int k)
    {
        if (SelectedStroke?.Fit is not { Count: > 0 } fit || k < 0 || k > fit.Count) return;
        Checkpoint();
        CurvHandles(fit, k, !IsCorner(fit, k));
        CurvPoint = k;
        EndFitEdit();
    }

    /// <summary>Delete on a point: it goes, and the curve runs smoothly without it (two points left at least).</summary>
    public bool DeleteCurvPoint()
    {
        if (CurvPoint < 0 || SelectedStroke is not { Fit: { Count: > 0 } fit } s) return false;
        Checkpoint();
        int k = CurvPoint, n = fit.Count;
        if (n <= 1) { Ann.Segments.Remove(s); SelectedOrder = 0; CurvPoint = -1; Refresh(); return true; }
        var corners = Enumerable.Range(0, n + 1).Select(j => IsCorner(fit, j)).ToList();
        if (k == 0) fit.RemoveAt(0);
        else if (k == n) fit.RemoveAt(n - 1);
        else
        {
            var a = fit[k - 1]; var b = fit[k];
            fit[k - 1] = [a[0], a[1], a[2], a[3], b[4], b[5], b[6], b[7], a[8], b[9]];
            fit.RemoveAt(k);
        }
        corners.RemoveAt(k);
        int m = fit.Count;
        var redo = new List<int> { k - 1, k };
        if (k <= 2) redo.Add(0);
        if (k >= m - 2) redo.Add(m);
        foreach (var j in redo.Distinct())
            if (j >= 0 && j <= m) CurvHandles(fit, j, corners[j]);
        CurvPoint = -1;
        EndFitEdit();
        return true;
    }

    /// <summary>End of a bend: the stroke's points follow its curves (as after dragging anchors).</summary>
    public void EndCurvEdit()
    {
        if (!_curvEdited) return;
        _curvEdited = false;
        EndFitEdit();
    }

    // ---- building a new stroke point by point
    /// <summary>A click with the Curvature tool on nothing: a point of a new stroke (double click: a corner).</summary>
    public void AddCurvPoint(Point p, bool corner)
    {
        if (Item == null) return;
        if (CurvDraft.Count == 0) SelectedOrder = 0;
        CurvDraft.Add((p, corner));
        Redraw?.Invoke();
    }

    /// <summary>The last point made a corner (the second click of a double click).</summary>
    public void CornerLastCurvPoint()
    {
        if (CurvDraft.Count == 0) return;
        CurvDraft[^1] = (CurvDraft[^1].P, true);
        Redraw?.Invoke();
    }

    public bool IsOnFirstCurvPoint(Point p, double r) => CurvDraft.Count >= 3 && (CurvDraft[0].P - p).Length <= r;

    /// <summary>Enter / Esc, or a click on the first point (closes it): the new stroke is kept for the active letter.</summary>
    public void FinishCurvDraft(bool close = false)
    {
        if (CurvDraft.Count == 0) return;
        if (close && CurvDraft.Count >= 3) CurvDraft.Add(CurvDraft[0]);
        if (CurvDraft.Count < 2) { CurvDraft.Clear(); Redraw?.Invoke(); return; }
        Checkpoint();
        var fit = CurvFit(CurvDraft);
        CurvDraft.Clear();
        var made = NewStroke();
        if (made == null) { Redraw?.Invoke(); return; }
        made.Fit = fit;
        made.Fidelity = Fidelity;
        made.Built = true;
        made.Stroke = StrokeGeometry.SampleFit(fit).Select(q => new[] { Math.Round(q[0], 2), Math.Round(q[1], 2), 0, 0 }).ToList();
        Ann.Segments.Add(made);
        SelectedOrder = made.Order;   // like Illustrator: the new path stays selected
        Refresh();
    }
}
