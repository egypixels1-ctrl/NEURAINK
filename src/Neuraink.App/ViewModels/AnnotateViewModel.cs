using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Neuraink.App.Services;
using Neuraink.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Neuraink.App.ViewModels;

public sealed partial class ChipVm : ObservableObject
{
    public required LetterChip Chip { get; init; }
    public required Color Color { get; init; }
    public string Letter => Chip.Ch;
    public string FormText => Loc.T("Form." + Arabic.FormKey(Chip.Form));
    public Brush ColorBrush => new SolidColorBrush(Color);
    /// <summary>First letter of a new word: a small gap before it.</summary>
    public Thickness Gap => Chip.Index > 0 && Chip.Word > 0 && IsFirstOfWord ? new Thickness(14, 0, 0, 0) : new Thickness(0);
    public bool IsFirstOfWord { get; init; }
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _isDone;
}

/// <summary>An anchor of the pen tool (Illustrator style): a click makes a corner, a drag pulls out its two handles.</summary>
public sealed class PenNode
{
    public Point A { get; set; }
    public Point? In { get; set; }
    public Point? Out { get; set; }
}

public sealed class SegmentRow
{
    public required Segment Segment { get; init; }
    public required Brush Brush { get; init; }
    public required string Label { get; init; }
}

public sealed partial class ToolItem : ObservableObject
{
    [ObservableProperty] private bool _isActive;
    public required string Key { get; init; }
    public required string Shortcut { get; init; }
    public required Brush Brush { get; init; }
    public string Title => Loc.T("Tool." + Key);
}

/// <summary>
/// Letter segmentation of one piece: the text is split into letters with their forms; for each letter the user
/// outlines the body (and dots), then clicks the entry and exit joins; ornaments / frames / signatures can be excluded.
/// </summary>
public sealed partial class AnnotateViewModel : ObservableObject
{
    public static readonly Color ExcludeColor = Color.FromRgb(0xFF, 0x5D, 0x5D);
    public static readonly Color EntryColor = Color.FromRgb(0x3D, 0xD6, 0x8C);
    public static readonly Color ExitColor = Color.FromRgb(0xFF, 0xA5, 0x3D);

    private readonly MainViewModel _main;
    private readonly Stack<int> _history = new();
    private bool _settingText;

    public AnnotateViewModel(MainViewModel main)
    {
        _main = main;
        Tools =
        [
            new ToolItem { Key = "letter", Shortcut = "1", Brush = Frozen(ThemeService.AccentColor) },
            new ToolItem { Key = "dots", Shortcut = "2", Brush = Frozen(ThemeService.AccentColor, 0.55) },
            new ToolItem { Key = "entry", Shortcut = "3", Brush = Frozen(EntryColor) },
            new ToolItem { Key = "exit", Shortcut = "4", Brush = Frozen(ExitColor) },
            new ToolItem { Key = "exclude", Shortcut = "5", Brush = Frozen(ExcludeColor) },
        ];
        Tools[0].IsActive = true;
        LanguageService.Changed += () => { if (Item != null) { RebuildChips(); UpdateHint(); } };
    }

    // ------------------------------------------------------------------ state
    public LibraryItem? Item { get; private set; }
    public Annotation Ann { get; private set; } = new();
    public List<PenNode> Drawing { get; } = [];
    public List<ToolItem> Tools { get; }
    public ObservableCollection<ChipVm> Chips { get; } = [];
    public ObservableCollection<SegmentRow> Segments { get; } = [];

    [ObservableProperty] private BitmapSource? _image;
    [ObservableProperty] private BitmapSource? _original;
    [ObservableProperty] private string _text = "";
    [ObservableProperty] private string _tool = "letter";
    [ObservableProperty] private int _active;
    [ObservableProperty] private string _hint = "";
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _fileInfo = "";
    [ObservableProperty] private bool _hasItem;
    [ObservableProperty] private bool _nothingLeft;
    [ObservableProperty] private string _toast = "";
    public bool ShowOriginal => _main.Settings.ShowOriginal && Original != null;

    /// <summary>Raised when the drawing must be repainted.</summary>
    public event Action? Redraw;
    /// <summary>Raised when a new picture is opened (zoom to fit).</summary>
    public event Action? Opened;

    private static Brush Frozen(Color c, double opacity = 1)
    {
        var b = new SolidColorBrush(c) { Opacity = opacity };
        b.Freeze();
        return b;
    }

    public static Color ChipColor(int i)
    {
        double h = (i * 67 + 200) % 360 / 60.0, s = 0.85, l = 0.58;
        double c = (1 - Math.Abs(2 * l - 1)) * s, x = c * (1 - Math.Abs(h % 2 - 1)), m = l - c / 2;
        (double r, double g, double b) = h switch
        {
            < 1 => (c, x, 0d), < 2 => (x, c, 0d), < 3 => (0d, c, x), < 4 => (0d, x, c), < 5 => (x, 0d, c), _ => (c, 0d, x),
        };
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }

    public Color ColorOf(Segment s) => s.Kind == "exclude" ? ExcludeColor : ChipColor(s.Chip);

    // ------------------------------------------------------------------ open
    public void OpenNext(int after)
    {
        var id = _main.NextToSegment(after);
        if (id is { } i) Open(i, remember: after != 0);
        else
        {
            Item = null; HasItem = false; NothingLeft = true; Image = null; Original = null;
            Chips.Clear(); Segments.Clear(); Drawing.Clear();
            Redraw?.Invoke();
        }
    }

    public void Open(int id, bool remember)
    {
        var item = _main.Library.Get(id);
        if (item == null) return;
        if (remember && Item != null && Item.Id != id) _history.Push(Item.Id);
        Item = item;
        HasItem = true;
        NothingLeft = false;
        Ann = _main.Annotations.Load(id) ?? new Annotation { Id = id, File = item.File, Text = item.Text };
        Ann.Id = id;
        Ann.File = item.File;
        if (string.IsNullOrWhiteSpace(Ann.Text)) Ann.Text = item.Text;
        Title = $"#{item.Id}";
        FileInfo = item.File;
        Drawing.Clear();
        Image = Images.Load(_main.Library.FullPath(item.Png));
        Original = _main.Settings.ShowOriginal ? Images.Load(_main.Library.FullPath(item.Thumb), 320) : null;
        OnPropertyChanged(nameof(ShowOriginal));
        _settingText = true;
        Text = Ann.Text;
        _settingText = false;
        RebuildChips();
        var first = Chips.FirstOrDefault(c => !IsDone(c.Chip.Index));
        Active = first?.Chip.Index ?? 0;
        Tool = first == null ? "letter" : StepFor(first.Chip.Index);
        Refresh();
        Opened?.Invoke();
    }

    private void RebuildChips()
    {
        var letters = Arabic.Letters(Ann.Text);
        Chips.Clear();
        int lastWord = 0;
        foreach (var l in letters)
        {
            Chips.Add(new ChipVm { Chip = l, Color = ChipColor(l.Index), IsFirstOfWord = l.Word != lastWord });
            lastWord = l.Word;
        }
    }

    partial void OnTextChanged(string value)
    {
        if (_settingText || Item == null) return;
        Ann.Text = (value ?? "").Trim();
        var letters = Arabic.Letters(Ann.Text);
        // keep the regions that still match the same letter at the same place
        Ann.Segments = Ann.Segments.Where(s => s.Kind == "exclude" || (s.Chip >= 0 && s.Chip < letters.Count && letters[s.Chip].Ch == s.Letter)).ToList();
        foreach (var s in Ann.Segments.Where(s => s.Kind != "exclude")) s.Form = Arabic.FormKey(letters[s.Chip].Form);
        RebuildChips();
        Active = Math.Min(Active, Math.Max(0, Chips.Count - 1));
        Refresh();
    }

    // ------------------------------------------------------------------ helpers
    private Segment? Body(int chip) => Ann.Segments.FirstOrDefault(s => s.Kind == "letter" && s.Chip == chip);
    private LetterChip? ChipAt(int i) => i >= 0 && i < Chips.Count ? Chips[i].Chip : null;

    public bool IsDone(int i)
    {
        var b = Body(i);
        var c = ChipAt(i);
        if (b == null || c == null) return false;
        return (!c.NeedsEntry || b.Entry != null || b.EntrySkipped) && (!c.NeedsExit || b.Exit != null || b.ExitSkipped);
    }

    /// <summary>The next step for a letter: body, then entry, then exit.</summary>
    private string StepFor(int i)
    {
        var b = Body(i);
        var c = ChipAt(i);
        if (b == null || c == null) return "letter";
        if (c.NeedsEntry && b.Entry == null && !b.EntrySkipped) return "entry";
        if (c.NeedsExit && b.Exit == null && !b.ExitSkipped) return "exit";
        return "letter";
    }

    public void Refresh()
    {
        foreach (var c in Chips)
        {
            c.IsActive = c.Chip.Index == Active;
            c.IsDone = IsDone(c.Chip.Index);
        }
        Segments.Clear();
        foreach (var s in Ann.Segments)
            Segments.Add(new SegmentRow
            {
                Segment = s,
                Brush = Frozen(ColorOf(s)),
                Label = s.Kind == "exclude" ? Loc.T("Tool.exclude")
                    : $"{s.Letter}  ·  {Loc.T("Tool." + s.Kind)}" + (s.Kind == "letter" ? (s.Entry != null ? "  ↘" : "") + (s.Exit != null ? "  ↗" : "") : ""),
            });
        UpdateHint();
        Redraw?.Invoke();
    }

    private void UpdateHint()
    {
        var c = ChipAt(Active);
        if (Tool == "exclude") Hint = Loc.T("Hint.exclude");
        else if (c == null) Hint = "";
        else if (Chips.Count > 0 && Chips.All(x => IsDone(x.Chip.Index))) Hint = Loc.T("Hint.done");
        else Hint = Loc.F("Hint." + Tool, c.Ch, Loc.T("Form." + Arabic.FormKey(c.Form)));
    }

    partial void OnToolChanged(string value)
    {
        foreach (var t in Tools) t.IsActive = t.Key == value;
        Drawing.Clear();
        UpdateHint();
        Redraw?.Invoke();
    }

    // ------------------------------------------------------------------ input from the view
    /// <summary>A click on the picture (pixel coordinates). <paramref name="closeRadius"/> = 10 screen pixels in picture units.</summary>
    public void Click(Point p, double closeRadius)
    {
        if (Item == null) return;
        if (Tool is "entry" or "exit")
        {
            var b = Body(Active);
            if (b == null) { ShowToast(Loc.T("Ann.NeedBody")); return; }
            var pt = new[] { Math.Round(p.X, 1), Math.Round(p.Y, 1) };
            if (Tool == "entry") { b.Entry = pt; b.EntrySkipped = false; } else { b.Exit = pt; b.ExitSkipped = false; }
            Advance();
            return;
        }
        if (Drawing.Count >= 3 && (Drawing[0].A - p).Length < closeRadius) { CloseShape(); return; }
        Drawing.Add(new PenNode { A = p });
        Redraw?.Invoke();
    }

    /// <summary>True when a click at <paramref name="p"/> closes the shape (on the first anchor).</summary>
    public bool IsOnFirstAnchor(Point p, double radius) => Drawing.Count >= 3 && (Drawing[0].A - p).Length < radius;

    /// <summary>Pen tool: a new anchor (its handles are pulled out by <see cref="DragHandles"/>).</summary>
    public PenNode? AddAnchor(Point p)
    {
        if (Item == null || Tool is "entry" or "exit") return null;
        var n = new PenNode { A = p };
        Drawing.Add(n);
        Redraw?.Invoke();
        return n;
    }

    /// <summary>Pen tool: dragging from an anchor makes it smooth — the out handle follows the mouse, the in handle mirrors it.</summary>
    public void DragHandles(PenNode n, Point to)
    {
        n.Out = to;
        n.In = new Point(2 * n.A.X - to.X, 2 * n.A.Y - to.Y);
        Redraw?.Invoke();
    }

    /// <summary>The shape as straight lines (Bézier curves sampled), in picture pixels.</summary>
    public static List<Point> Flatten(IReadOnlyList<PenNode> nodes, bool closed)
    {
        var pts = new List<Point>();
        int n = nodes.Count;
        if (n == 0) return pts;
        int segs = closed ? n : n - 1;
        pts.Add(nodes[0].A);
        for (int i = 0; i < segs; i++)
        {
            var a = nodes[i]; var b = nodes[(i + 1) % n];
            if (a.Out == null && b.In == null) { pts.Add(b.A); continue; }
            Point p0 = a.A, p1 = a.Out ?? a.A, p2 = b.In ?? b.A, p3 = b.A;
            for (int k = 1; k <= 16; k++)
            {
                double t = k / 16.0, u = 1 - t;
                pts.Add(new Point(
                    u * u * u * p0.X + 3 * u * u * t * p1.X + 3 * u * t * t * p2.X + t * t * t * p3.X,
                    u * u * u * p0.Y + 3 * u * u * t * p1.Y + 3 * u * t * t * p2.Y + t * t * t * p3.Y));
            }
        }
        if (closed && pts.Count > 1 && (pts[^1] - pts[0]).Length < 0.01) pts.RemoveAt(pts.Count - 1);
        return pts;
    }

    public void CloseShape()
    {
        if (Drawing.Count < 3) return;
        var pts = Flatten(Drawing, closed: true).Select(d => new[] { Math.Round(d.X, 1), Math.Round(d.Y, 1) }).ToList();
        // the pen path itself: anchor x, y, in-handle x, y, out-handle x, y (the vector outline for later)
        var bez = Drawing.Select(d => new[]
        {
            Math.Round(d.A.X, 2), Math.Round(d.A.Y, 2),
            Math.Round((d.In ?? d.A).X, 2), Math.Round((d.In ?? d.A).Y, 2),
            Math.Round((d.Out ?? d.A).X, 2), Math.Round((d.Out ?? d.A).Y, 2),
        }).ToList();
        var c = ChipAt(Active);
        if (Tool == "exclude") Ann.Segments.Add(new Segment { Kind = "exclude", Points = pts, Bezier = bez });
        else if (c != null && Tool == "letter")
        {
            var old = Body(Active);
            if (old != null) Ann.Segments.Remove(old);
            Ann.Segments.Add(new Segment
            {
                Kind = "letter", Chip = Active, Letter = c.Ch, Form = Arabic.FormKey(c.Form), Points = pts, Bezier = bez,
                Entry = old?.Entry, Exit = old?.Exit, EntrySkipped = old?.EntrySkipped ?? false, ExitSkipped = old?.ExitSkipped ?? false,
            });
        }
        else if (c != null && Tool == "dots")
            Ann.Segments.Add(new Segment { Kind = "dots", Chip = Active, Letter = c.Ch, Form = Arabic.FormKey(c.Form), Points = pts, Bezier = bez });
        Drawing.Clear();
        if (Tool == "letter") Advance(); else Refresh();
    }

    /// <summary>After a step: entry → exit → the next unfinished letter.</summary>
    private void Advance()
    {
        var step = StepFor(Active);
        if (Body(Active) != null && step != "letter") { Tool = step; Refresh(); return; }
        // this letter is complete: the next unfinished one
        int n = Chips.Count;
        for (int k = 1; k <= n; k++)
        {
            int i = (Active + k) % Math.Max(1, n);
            if (!IsDone(i)) { Active = i; Tool = StepFor(i); Refresh(); return; }
        }
        Tool = "letter";
        Refresh();
    }

    public void BackspacePoint()
    {
        if (Drawing.Count > 0) { Drawing.RemoveAt(Drawing.Count - 1); Redraw?.Invoke(); }
    }

    public void CancelDrawing() { Drawing.Clear(); Redraw?.Invoke(); }

    public void Undo()
    {
        if (Drawing.Count > 0) Drawing.Clear();
        else if (Ann.Segments.Count > 0) Ann.Segments.RemoveAt(Ann.Segments.Count - 1);
        Refresh();
    }

    /// <summary>Tab: skip the entry / exit of this letter, or move to the next letter.</summary>
    public void SkipStep()
    {
        var b = Body(Active);
        if (b != null && Tool == "entry") b.EntrySkipped = true;
        if (b != null && Tool == "exit") b.ExitSkipped = true;
        if (b == null && Tool == "letter" && Chips.Count > 0) { Active = (Active + 1) % Chips.Count; Refresh(); return; }
        Advance();
    }

    public void SetToolByKey(int n)
    {
        if (n >= 1 && n <= Tools.Count) Tool = Tools[n - 1].Key;
    }

    private async void ShowToast(string text)
    {
        Toast = text;
        await Task.Delay(2600);
        if (Toast == text) Toast = "";
    }

    // ------------------------------------------------------------------ commands
    [RelayCommand] private void SelectChip(ChipVm? chip)
    {
        if (chip == null) return;
        Active = chip.Chip.Index;
        Tool = StepFor(Active);
        Refresh();
    }

    [RelayCommand] private void SetTool(string? key) { if (key != null) Tool = key; }

    [RelayCommand] private void DeleteSegment(SegmentRow? row)
    {
        if (row == null) return;
        Ann.Segments.Remove(row.Segment);
        Refresh();
    }

    [RelayCommand] private void ClearAll()
    {
        Ann.Segments.Clear();
        Drawing.Clear();
        Active = 0;
        Tool = "letter";
        Refresh();
    }

    [RelayCommand]
    public void Save()
    {
        if (Item == null) return;
        if (Ann.Segments.Count == 0) { ShowToast(Loc.T("Ann.NeedBody")); return; }
        bool all = Chips.Count > 0 && Chips.All(c => Body(c.Chip.Index) != null);
        Ann.State = all ? "done" : "partial";
        Persist();
        int n = LetterExporter.Export(Item, Ann, _main.Library.FullPath(Item.Png), _main.Letters, _main.Settings.Style);
        ShowToast(all ? Loc.F("Ann.Saved", n) : Loc.T("Ann.Partial"));
        _main.RefreshCounts();
        OpenNext(Item.Id);
    }

    [RelayCommand]
    private void Reject()
    {
        if (Item == null) return;
        Ann.State = "rejected";
        Persist();
        _main.Letters.RemoveSource(Item.Id);
        ShowToast(Loc.T("Ann.Rejected"));
        _main.RefreshCounts();
        OpenNext(Item.Id);
    }

    [RelayCommand] private void Skip() { if (Item != null) OpenNext(Item.Id); }

    [RelayCommand]
    private void Previous()
    {
        if (_history.Count == 0) return;
        Open(_history.Pop(), remember: false);
    }

    private void Persist()
    {
        try { _main.Annotations.Save(Ann); }
        catch (Exception ex) { Dialogs.Error(ex.Message); }
    }
}
