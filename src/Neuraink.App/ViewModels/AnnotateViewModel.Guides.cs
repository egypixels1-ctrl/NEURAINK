using System.Windows;
using CommunityToolkit.Mvvm.Input;

namespace Neuraink.App.ViewModels;

/// <summary>
/// Rulers and guides, like Illustrator: Ctrl+R shows the rulers; a guide is dragged out of a ruler, moved or dragged off the
/// picture with the Selection tool; Ctrl+; hides them, Ctrl+Alt+; locks them. Drawings, pen anchors and Curvature points snap
/// to them. They are kept with the piece, and never go into the drawing, the PDF or the training data.
/// </summary>
public sealed partial class AnnotateViewModel
{
    public bool ShowRulers { get => _main.Settings.ShowRulers; set { _main.Settings.ShowRulers = value; OnPropertyChanged(); _main.SaveSettings(); } }
    public bool ShowGuides { get => _main.Settings.ShowGuides; set { _main.Settings.ShowGuides = value; OnPropertyChanged(); _main.SaveSettings(); Redraw?.Invoke(); } }
    public bool GuidesLocked { get => _main.Settings.GuidesLocked; set { _main.Settings.GuidesLocked = value; OnPropertyChanged(); _main.SaveSettings(); } }
    public bool SnapToGuides { get => _main.Settings.SnapToGuides; set { _main.Settings.SnapToGuides = value; OnPropertyChanged(); _main.SaveSettings(); } }

    [RelayCommand] private void ToggleRulers() => ShowRulers = !ShowRulers;
    [RelayCommand] private void ToggleGuides() => ShowGuides = !ShowGuides;
    [RelayCommand] private void ToggleGuidesLock() => GuidesLocked = !GuidesLocked;
    [RelayCommand] private void ToggleSnap() => SnapToGuides = !SnapToGuides;

    public IReadOnlyList<double> GuidesH => Ann.GuidesH;
    public IReadOnlyList<double> GuidesV => Ann.GuidesV;

    /// <summary>The guide picked with the Selection tool (Delete / Backspace removes it): ('h' | 'v', index).</summary>
    public (char Dir, int Index)? SelectedGuide { get; set; }

    public void AddGuide(char dir, double pos)
    {
        if (Item == null) return;
        Checkpoint();
        (dir == 'h' ? Ann.GuidesH : Ann.GuidesV).Add(Math.Round(pos, 1));
        SelectedGuide = (dir, (dir == 'h' ? Ann.GuidesH : Ann.GuidesV).Count - 1);
        Redraw?.Invoke();
    }

    /// <summary>A guide near a point (within <paramref name="tol"/>), when the guides are shown and not locked.</summary>
    public (char Dir, int Index)? HitGuide(Point p, double tol)
    {
        if (!ShowGuides || GuidesLocked || Item == null) return null;
        for (int i = 0; i < Ann.GuidesH.Count; i++) if (Math.Abs(Ann.GuidesH[i] - p.Y) <= tol) return ('h', i);
        for (int i = 0; i < Ann.GuidesV.Count; i++) if (Math.Abs(Ann.GuidesV[i] - p.X) <= tol) return ('v', i);
        return null;
    }

    public void BeginGuideDrag() => Checkpoint();

    public void MoveGuide((char Dir, int Index) g, double pos)
    {
        var list = g.Dir == 'h' ? Ann.GuidesH : Ann.GuidesV;
        if (g.Index < 0 || g.Index >= list.Count) return;
        list[g.Index] = Math.Round(pos, 1);
        Redraw?.Invoke();
    }

    public void RemoveGuide((char Dir, int Index) g, bool checkpoint = true)
    {
        var list = g.Dir == 'h' ? Ann.GuidesH : Ann.GuidesV;
        if (g.Index < 0 || g.Index >= list.Count) return;
        if (checkpoint) Checkpoint();
        list.RemoveAt(g.Index);
        SelectedGuide = null;
        Redraw?.Invoke();
    }

    /// <summary>A point pulled onto the guides it is near (snapping), when snapping is on.</summary>
    public Point Snap(Point p, double tol)
    {
        if (!SnapToGuides || !ShowGuides || Item == null) return p;
        double x = p.X, y = p.Y, bx = tol, by = tol;
        foreach (var g in Ann.GuidesV) if (Math.Abs(g - p.X) < bx) { bx = Math.Abs(g - p.X); x = g; }
        foreach (var g in Ann.GuidesH) if (Math.Abs(g - p.Y) < by) { by = Math.Abs(g - p.Y); y = g; }
        return new Point(x, y);
    }

    /// <summary>A move of the selection pulled so that its edges or middle land on a guide nearby.</summary>
    public Vector SnapMove(Rect box, Vector d, double tol)
    {
        if (!SnapToGuides || !ShowGuides || box.IsEmpty) return d;
        double dx = d.X, dy = d.Y, bestX = tol, bestY = tol;
        foreach (var g in Ann.GuidesV)
            foreach (var edge in new[] { box.Left, box.Left + box.Width / 2, box.Right })
                if (Math.Abs(g - (edge + d.X)) < bestX) { bestX = Math.Abs(g - (edge + d.X)); dx = g - edge; }
        foreach (var g in Ann.GuidesH)
            foreach (var edge in new[] { box.Top, box.Top + box.Height / 2, box.Bottom })
                if (Math.Abs(g - (edge + d.Y)) < bestY) { bestY = Math.Abs(g - (edge + d.Y)); dy = g - edge; }
        return new Vector(dx, dy);
    }
}
