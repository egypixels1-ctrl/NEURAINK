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

/// <summary>A row of the Brushes panel.</summary>
public sealed partial class BrushItem : ObservableObject
{
    public required BrushTip Tip { get; init; }
    public required int Index { get; init; }
    public string Name => Tip.Name;
    /// <summary>The angle, from the brush's settings (not from its name).</summary>
    public string AngleText => Tip.Roundness >= 0.99 ? "●" : $"{Tip.Angle:0}°";
    [ObservableProperty] private bool _isRenaming;
    [ObservableProperty] private string _editName = "";
    public void Refresh() { OnPropertyChanged(nameof(Name)); OnPropertyChanged(nameof(AngleText)); OnPropertyChanged(nameof(Angle)); OnPropertyChanged(nameof(Details)); }
    public double Angle => Tip.Angle;
    public double Roundness => Tip.Roundness;
    public string Details => $"{Tip.Angle:0}° · {Tip.Roundness * 100:0}% · {Tip.Size:0} · {Tip.Opacity * 100:0}%";
    [ObservableProperty] private bool _isSelected;
}

/// <summary>Result of the Brush Options window.</summary>
public sealed record BrushEdit(BrushTip Tip, bool Delete);

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
    public static readonly Color OrnamentColor = Color.FromRgb(0xE8, 0xB9, 0x3A);
    public static readonly Color DiacriticColor = Color.FromRgb(0x4F, 0xC3, 0xF7);

    /// <summary>Calligraphic brush presets (angle / roundness), like the user's Illustrator brushes.</summary>
    public static readonly BrushTip[] Presets =
    [
        new() { Name = "●", Angle = 0, Roundness = 1 },
        new() { Name = "45°", Angle = 45, Roundness = 0.2 },
        new() { Name = "50°", Angle = 50, Roundness = 0.2 },
        new() { Name = "55°", Angle = 55, Roundness = 0.2 },
        new() { Name = "60°", Angle = 60, Roundness = 0.2 },
        new() { Name = "65°", Angle = 65, Roundness = 0.2 },
        new() { Name = "70°", Angle = 70, Roundness = 0.2 },
        new() { Name = "75°", Angle = 75, Roundness = 0.2 },
        new() { Name = "80°", Angle = 80, Roundness = 0.2 },
        new() { Name = "85°", Angle = 85, Roundness = 0.2 },
        new() { Name = "90°", Angle = 90, Roundness = 0.2 },
        new() { Name = "136°", Angle = 136, Roundness = 0.2 },
    ];

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
            new ToolItem { Key = "brush", Shortcut = "6", Brush = Frozen(ThemeService.AccentColor) },
            new ToolItem { Key = "diacritic", Shortcut = "7", Brush = Frozen(DiacriticColor) },
            new ToolItem { Key = "ornament", Shortcut = "8", Brush = Frozen(OrnamentColor) },
        ];
        Tools[0].IsActive = true;
        LoadBrushes();
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
    /// <summary>This piece belongs to the test set: never trained on, used to measure the model.</summary>
    public bool IsTestSet
    {
        get => (string.IsNullOrEmpty(Ann.Split) ? DataVersion.SplitFor(Ann.Text) : Ann.Split) == "test";
        set { Ann.Split = value ? "test" : "train"; OnPropertyChanged(); }
    }
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

    public Color ColorOf(Segment s) => s.Kind switch { "exclude" => ExcludeColor, "ornament" => OrnamentColor, "diacritic" => DiacriticColor, _ => ChipColor(s.Chip) };

    // ------------------------------------------------------------------ open
    public void OpenNext(int after)
    {
        var id = _main.NextToSegment(after);
        if (id is { } i) Open(i, remember: after != 0);
        else
        {
            Item = null; HasItem = false; NothingLeft = true; Image = null; Original = null; _board = null; Reference = null; NotifyReference();
            Chips.Clear(); Segments.Clear(); Drawing.Clear();
            Redraw?.Invoke();
        }
    }

    public void Open(int id, bool remember)
    {
        var item = _main.GetItem(id);
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
        _undo.Clear(); _redo.Clear();
        Image = Images.Load(_main.FullPath(item.Png));
        _board = item.IsArtboard ? _main.Artboards.Get(item.Id) : null;
        Reference = _board?.Reference is { } rp ? Images.Load(rp) : null;
        AdjustReference = false;
        NotifyReference();
        Original = _main.Settings.ShowOriginal ? Images.Load(_main.FullPath(item.Thumb), 320) : null;
        OnPropertyChanged(nameof(ShowOriginal));
        OnPropertyChanged(nameof(IsTestSet));
        _settingText = true;
        Text = Ann.Text;
        _settingText = false;
        RebuildChips();
        _strokeOrder = Ann.Segments.Count == 0 ? 0 : Ann.Segments.Max(x => x.Order);
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
        Checkpoint();
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

    private bool HasStrokes(int chip) => Ann.Segments.Any(s => s.Kind == "stroke" && s.Chip == chip);

    public bool IsDone(int i)
    {
        if (HasStrokes(i)) return true;   // drawn with the brush: its joins are the stroke ends
        var b = Body(i);
        var c = ChipAt(i);
        if (b == null || c == null) return false;
        return (!c.NeedsEntry || b.Entry != null || b.EntrySkipped) && (!c.NeedsExit || b.Exit != null || b.ExitSkipped);
    }

    /// <summary>The next step for a letter: body, then entry, then exit.</summary>
    private string StepFor(int i)
    {
        if (Tool == "brush") return "brush";
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
                Label = s.Kind is "exclude" or "ornament" ? Loc.T("Tool." + s.Kind)
                    : s.Kind == "diacritic" ? $"{s.Letter}  ·  {Loc.T("Tool.diacritic")}"
                    : $"{s.Letter}  ·  {Loc.T("Tool." + s.Kind)}" + (s.Kind == "letter" ? (s.Entry != null ? "  ↘" : "") + (s.Exit != null ? "  ↗" : "") : ""),
            });
        UpdateHint();
        Redraw?.Invoke();
    }

    private void UpdateHint()
    {
        var c = ChipAt(Active);
        if (Tool == "exclude") Hint = Loc.T("Hint.exclude");
        else if (Tool == "ornament") Hint = Loc.T("Hint.ornament");
        else if (Tool == "diacritic" && c != null) Hint = Loc.F("Hint.diacritic", c.Ch);
        else if (c == null) Hint = "";
        else if (Chips.Count > 0 && Chips.All(x => IsDone(x.Chip.Index))) Hint = Loc.T("Hint.done");
        else Hint = Loc.F("Hint." + Tool, c.Ch, Loc.T("Form." + Arabic.FormKey(c.Form)));
    }

    partial void OnToolChanged(string value)
    {
        foreach (var t in Tools) t.IsActive = t.Key == value;
        if (_restoring) { OnPropertyChanged(nameof(IsBrushTool)); OnPropertyChanged(nameof(IsPenTool)); UpdateHint(); return; }
        CurrentStroke.Clear();
        OnPropertyChanged(nameof(IsBrushTool));
        OnPropertyChanged(nameof(IsPenTool));
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
            Checkpoint();
            var pt = new[] { Math.Round(p.X, 1), Math.Round(p.Y, 1) };
            if (Tool == "entry") { b.Entry = pt; b.EntrySkipped = false; } else { b.Exit = pt; b.ExitSkipped = false; }
            Advance();
            return;
        }
        if (Drawing.Count >= 3 && (Drawing[0].A - p).Length < closeRadius) { CloseShape(); return; }
        Checkpoint();
        Drawing.Add(new PenNode { A = p });
        Redraw?.Invoke();
    }

    /// <summary>True when a click at <paramref name="p"/> closes the shape (on the first anchor).</summary>
    public bool IsOnFirstAnchor(Point p, double radius) => Drawing.Count >= 3 && (Drawing[0].A - p).Length < radius;

    /// <summary>Pen tool: a new anchor (its handles are pulled out by <see cref="DragHandles"/>).</summary>
    public PenNode? AddAnchor(Point p)
    {
        if (Item == null || Tool is "entry" or "exit") return null;
        Checkpoint();
        var n = new PenNode { A = p };
        Drawing.Add(n);
        Redraw?.Invoke();
        return n;
    }

    /// <summary>
    /// Pen tool: dragging from an anchor makes it smooth — the out handle follows the mouse and the in handle mirrors it;
    /// with Alt only the out handle moves (the in side keeps its shape).
    /// </summary>
    public void DragHandles(PenNode n, Point to, bool alt = false)
    {
        n.Out = to;
        if (!alt) n.In = new Point(2 * n.A.X - to.X, 2 * n.A.Y - to.Y);
        Redraw?.Invoke();
    }

    /// <summary>What a click lands on in the pen path: an anchor, its in handle or its out handle.</summary>
    public (PenNode Node, string Part)? HitPen(Point p, double radius)
    {
        for (int i = Drawing.Count - 1; i >= 0; i--)
        {
            var n = Drawing[i];
            if (n.Out is { } o && (o - p).Length <= radius) return (n, "out");
            if (n.In is { } q && (q - p).Length <= radius) return (n, "in");
        }
        for (int i = Drawing.Count - 1; i >= 0; i--)
            if ((Drawing[i].A - p).Length <= radius) return (Drawing[i], "anchor");
        return null;
    }

    public bool IsLastAnchor(PenNode n) => Drawing.Count > 0 && ReferenceEquals(Drawing[^1], n);

    /// <summary>Moves an anchor with its handles.</summary>
    public void MoveAnchor(PenNode n, Point to)
    {
        var d = to - n.A;
        n.A = to;
        if (n.In is { } i) n.In = i + d;
        if (n.Out is { } o) n.Out = o + d;
        Redraw?.Invoke();
    }

    /// <summary>Moves a handle; the opposite handle mirrors its direction (keeping its own length) unless Alt breaks them apart.</summary>
    public void MoveHandle(PenNode n, bool outHandle, Point to, bool alt)
    {
        if (outHandle) n.Out = to; else n.In = to;
        var other = outHandle ? n.In : n.Out;
        if (!alt && other is { } ot)
        {
            var dir = n.A - to;
            if (dir.Length > 1e-6)
            {
                dir.Normalize();
                var p = n.A + dir * (ot - n.A).Length;
                if (outHandle) n.In = p; else n.Out = p;
            }
        }
        Redraw?.Invoke();
    }

    /// <summary>Alt + click on the last anchor: its out handle is removed (the next part starts straight or anew).</summary>
    public void CutOutHandle(PenNode n)
    {
        if (n.Out == null) return;
        Checkpoint();
        n.Out = null;
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
        Checkpoint();
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

    // ------------------------------------------------------------------ brush (calligraphic strokes traced over the letter)
    /// <summary>The stroke being drawn: raw points x, y, pressure, milliseconds since the stroke began (speed and rhythm).</summary>
    public List<double[]> CurrentStroke { get; } = [];
    private readonly System.Diagnostics.Stopwatch _strokeClock = new();
    private int _strokeOrder;
    // ---- Brushes panel (like Illustrator): a list of named brushes; double-click edits, + adds
    public ObservableCollection<BrushItem> BrushList { get; } = [];
    public BrushTip Tip
    {
        get
        {
            var b = _main.Settings.Brushes;
            if (b.Count == 0) b.AddRange(BrushTip.Defaults());
            return b[Math.Clamp(_main.Settings.BrushIndex, 0, b.Count - 1)];
        }
    }
    public double BrushSize { get => Tip.Size; set { Tip.Size = Math.Clamp(Math.Round(value, 1), 1, 400); OnPropertyChanged(); SaveBrush(); } }
    public int Fidelity { get => _main.Settings.Fidelity; set { _main.Settings.Fidelity = Math.Clamp(value, 0, 4); OnPropertyChanged(); _main.SaveSettings(); Redraw?.Invoke(); } }
    public bool AutoNextLetter { get => _main.Settings.AutoNextLetter; set { _main.Settings.AutoNextLetter = value; OnPropertyChanged(); _main.SaveSettings(); } }
    public double PictureOpacity
    {
        get => Math.Round((_board != null ? _board.RefOpacity : _main.Settings.PictureOpacity) * 100);
        set
        {
            var v = Math.Clamp(value / 100, 0.05, 1);
            if (_board != null) { _board.RefOpacity = v; SaveBoard(); }
            else { _main.Settings.PictureOpacity = v; _main.SaveSettings(); }
            OnPropertyChanged(); OnPropertyChanged(nameof(PictureOpacityValue)); OnPropertyChanged(nameof(RefOpacity));
        }
    }
    /// <summary>Opacity of the piece's picture (an artboard's white page always shows fully).</summary>
    public double PictureOpacityValue => _board != null ? 1 : _main.Settings.PictureOpacity;

    // ------------------------------------------------------------------ artboard (written directly, over an optional reference picture)
    private Artboard? _board;
    public bool IsArtboard => _board != null;
    [ObservableProperty] private BitmapSource? _reference;
    [ObservableProperty] private bool _adjustReference;
    public double RefX => _board?.RefX ?? 0;
    public double RefY => _board?.RefY ?? 0;
    public double RefScale => _board?.RefScale ?? 1;
    public double RefOpacity => _board?.RefOpacity ?? 0.35;
    public bool HasReference => Reference != null;
    partial void OnReferenceChanged(BitmapSource? value) => OnPropertyChanged(nameof(HasReference));

    private DateTime _lastBoardSave;
    private void SaveBoard(bool now = false)
    {
        if (_board == null) return;
        if (!now && (DateTime.Now - _lastBoardSave).TotalMilliseconds < 300) return;
        _lastBoardSave = DateTime.Now;
        try { _main.Artboards.Save(_board); } catch { }
    }

    private void NotifyReference()
    {
        OnPropertyChanged(nameof(RefX)); OnPropertyChanged(nameof(RefY)); OnPropertyChanged(nameof(RefScale));
        OnPropertyChanged(nameof(RefOpacity)); OnPropertyChanged(nameof(HasReference)); OnPropertyChanged(nameof(IsArtboard));
        OnPropertyChanged(nameof(PictureOpacity)); OnPropertyChanged(nameof(PictureOpacityValue));
    }

    /// <summary>Adds (or replaces) the reference picture of the artboard; the window opens on the Desktop.</summary>
    [RelayCommand]
    private void AddReference()
    {
        if (_board == null) return;
        var d = new Microsoft.Win32.OpenFileDialog
        {
            Filter = Loc.T("Board.Pictures") + "|*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.tif;*.tiff",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        };
        if (d.ShowDialog(Application.Current?.MainWindow) != true) return;
        SetReference(d.FileName);
    }

    public void SetReference(string file)
    {
        if (_board == null) return;
        var img = Images.Load(file);
        if (img == null) { Dialogs.Error(Loc.T("Board.BadPicture")); return; }
        _board.Reference = file;
        // fitted in the page, centred
        double sc = Math.Min(_board.Width * 0.9 / img.PixelWidth, _board.Height * 0.9 / img.PixelHeight);
        _board.RefScale = sc;
        _board.RefX = (_board.Width - img.PixelWidth * sc) / 2;
        _board.RefY = (_board.Height - img.PixelHeight * sc) / 2;
        Reference = img;
        AdjustReference = true;
        SaveBoard(now: true);
        NotifyReference();
    }

    [RelayCommand] private void ToggleAdjustReference() => AdjustReference = HasReference && !AdjustReference;

    public void MoveReference(Vector by)
    {
        if (_board == null) return;
        _board.RefX += by.X; _board.RefY += by.Y;
        SaveBoard();
        NotifyReference();
    }

    /// <summary>Scales the reference around a point of the page (mouse wheel while adjusting it).</summary>
    public void ScaleReference(double factor, Point around)
    {
        if (_board == null) return;
        double s0 = _board.RefScale, s1 = Math.Clamp(s0 * factor, 0.02, 40);
        _board.RefX = around.X - (around.X - _board.RefX) * s1 / s0;
        _board.RefY = around.Y - (around.Y - _board.RefY) * s1 / s0;
        _board.RefScale = s1;
        SaveBoard();
        NotifyReference();
    }

    /// <summary>Saves the strokes of this piece as a vector PDF (pure black on white); the window opens on the Desktop.</summary>
    [RelayCommand]
    private void ExportPdf()
    {
        if (Item == null || Image == null) return;
        var strokes = Ann.Segments.Where(x => x.Kind is "stroke" or "diacritic" or "ornament" && x.Stroke is { Count: > 0 } && x.Brush != null).ToList();
        if (strokes.Count == 0) { ShowToast(Loc.T("Pdf.Nothing")); return; }
        var name = string.Join("_", (Ann.Text.Length > 0 ? Ann.Text : $"NEURAINK {Item.Id}").Split(System.IO.Path.GetInvalidFileNameChars()));
        var d = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PDF|*.pdf", FileName = name + ".pdf", DefaultExt = ".pdf",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        };
        if (d.ShowDialog(Application.Current?.MainWindow) != true) return;
        try
        {
            var outline = StrokeGeometry.Outline(strokes);
            PaperWriter.Pdf(d.FileName, [outline], Image.PixelWidth, Image.PixelHeight);
            ShowToast(Loc.F("Pdf.Saved", System.IO.Path.GetFileName(d.FileName)));
        }
        catch (Exception ex) { Dialogs.Error(ex.Message); }
    }
    public bool IsBrushTool => Tool is "brush" or "ornament" or "diacritic";
    public bool IsPenTool => !IsBrushTool && Tool is not ("entry" or "exit");
    public Color BrushColor => Tool switch { "ornament" => OrnamentColor, "diacritic" => DiacriticColor, _ => ChipColor(Active) };

    public void LoadBrushes()
    {
        BrushList.Clear();
        var list = _main.Settings.Brushes;
        if (list.Count == 0) list.AddRange(BrushTip.Defaults());
        for (int i = 0; i < list.Count; i++) BrushList.Add(new BrushItem { Tip = list[i], Index = i, IsSelected = i == _main.Settings.BrushIndex });
        OnPropertyChanged(nameof(BrushSize));
    }

    private DateTime _lastBrushSave;
    private void SaveBrush()
    {
        Redraw?.Invoke();
        if ((DateTime.Now - _lastBrushSave).TotalMilliseconds < 400) return;
        _lastBrushSave = DateTime.Now;
        _main.SaveSettings();
    }

    [RelayCommand]
    private void SelectBrush(BrushItem? b)
    {
        if (b == null) return;
        _main.Settings.BrushIndex = b.Index;
        foreach (var x in BrushList) x.IsSelected = x == b;
        OnPropertyChanged(nameof(BrushSize));
        _main.SaveSettings();
        if (!IsBrushTool) Tool = "brush";
        Redraw?.Invoke();
    }

    /// <summary>Brush Options window (double-click or a new brush). Returns false when cancelled.</summary>
    public Func<BrushTip, bool, BrushEdit?>? EditBrushDialog { get; set; }

    [RelayCommand]
    private void EditBrush(BrushItem? b)
    {
        if (b == null || EditBrushDialog == null) return;
        var r = EditBrushDialog(b.Tip.Copy(), false);
        if (r == null) return;
        var list = _main.Settings.Brushes;
        if (r.Delete && list.Count > 1)
        {
            list.RemoveAt(b.Index);
            _main.Settings.BrushIndex = Math.Clamp(_main.Settings.BrushIndex >= b.Index ? _main.Settings.BrushIndex - 1 : _main.Settings.BrushIndex, 0, list.Count - 1);
        }
        else if (!r.Delete)
        {
            list[b.Index] = r.Tip;
            _main.Settings.BrushIndex = b.Index;
        }
        _main.SaveSettings();
        LoadBrushes();
        Redraw?.Invoke();
    }

    [RelayCommand]
    private void AddBrush()
    {
        if (EditBrushDialog == null) return;
        var n = _main.Settings.Brushes.Count + 1;
        var fresh = new BrushTip { Name = Loc.F("Brushes.NewName", n), Size = Tip.Size, Angle = 45, Roundness = BrushTip.DefaultRoundness };
        var r = EditBrushDialog(fresh, true);
        if (r == null || r.Delete) return;
        _main.Settings.Brushes.Add(r.Tip);
        _main.Settings.BrushIndex = _main.Settings.Brushes.Count - 1;
        _main.SaveSettings();
        LoadBrushes();
        if (!IsBrushTool) Tool = "brush";
        Redraw?.Invoke();
    }

    [RelayCommand]
    private void DuplicateBrush(BrushItem? b)
    {
        if (b == null) return;
        var c = b.Tip.Copy();
        c.Name += " 2";
        _main.Settings.Brushes.Insert(b.Index + 1, c);
        _main.Settings.BrushIndex = b.Index + 1;
        _main.SaveSettings();
        LoadBrushes();
    }

    /// <summary>Alt + wheel: turns the brush (5° a notch, 1° with Shift); returns the new angle.</summary>
    public double RotateBrush(double degrees)
    {
        var a = (Tip.Angle + degrees) % 180;
        if (a < 0) a += 180;
        Tip.Angle = Math.Round(a);
        foreach (var b in BrushList) if (b.IsSelected) b.Refresh();
        SaveBrush();
        return Tip.Angle;
    }

    // ---- rename a brush in its row (right-click › Rename, or F2)
    [RelayCommand]
    private void RenameBrush(BrushItem? b)
    {
        b ??= BrushList.FirstOrDefault(x => x.IsSelected);
        if (b == null) return;
        foreach (var x in BrushList) x.IsRenaming = false;
        b.EditName = b.Tip.Name;
        b.IsRenaming = true;
    }

    public void CommitRename(BrushItem b, bool keep)
    {
        if (!b.IsRenaming) return;
        b.IsRenaming = false;
        if (keep && !string.IsNullOrWhiteSpace(b.EditName))
        {
            b.Tip.Name = b.EditName.Trim();
            b.Refresh();
            _main.SaveSettings();
        }
    }

    [RelayCommand] private void UsePen() => Tool = "letter";
    [RelayCommand] private void UseBrush() => Tool = "brush";

    [RelayCommand]
    private void ToggleOriginal()
    {
        _main.Settings.ShowOriginal = !_main.Settings.ShowOriginal;
        _main.SaveSettings();
        if (_main.Settings.ShowOriginal && Original == null && Item != null) Original = Images.Load(_main.FullPath(Item.Thumb), 320);
        OnPropertyChanged(nameof(ShowOriginal));
    }

    // ------------------------------------------------------------------ undo / redo (one step at a time)
    private bool _restoring;
    private sealed record Snapshot(string Segments, List<PenNode> Drawing, int Active, string Tool, string Text);
    private readonly Stack<Snapshot> _undo = new(), _redo = new();
    private static readonly System.Text.Json.JsonSerializerOptions SnapJson = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    private Snapshot Take() => new(
        System.Text.Json.JsonSerializer.Serialize(Ann.Segments, SnapJson),
        Drawing.Select(d => new PenNode { A = d.A, In = d.In, Out = d.Out }).ToList(), Active, Tool, Ann.Text);

    /// <summary>Remembers the state before a change (Ctrl+Z goes back to it).</summary>
    public void Checkpoint()
    {
        _undo.Push(Take());
        if (_undo.Count > 300) { var keep = _undo.Take(200).Reverse().ToList(); _undo.Clear(); foreach (var k in keep) _undo.Push(k); }
        _redo.Clear();
    }

    private void Restore(Snapshot s)
    {
        Ann.Segments = System.Text.Json.JsonSerializer.Deserialize<List<Segment>>(s.Segments, SnapJson) ?? [];
        Drawing.Clear();
        Drawing.AddRange(s.Drawing.Select(d => new PenNode { A = d.A, In = d.In, Out = d.Out }));
        if (s.Text != Ann.Text) { Ann.Text = s.Text; _settingText = true; Text = s.Text; _settingText = false; RebuildChips(); }
        Active = Math.Min(s.Active, Math.Max(0, Chips.Count - 1));
        _restoring = true;
        Tool = s.Tool;
        _restoring = false;
        Refresh();
    }

    public void UndoStep()
    {
        if (_undo.Count == 0) return;
        _redo.Push(Take());
        Restore(_undo.Pop());
    }

    public void RedoStep()
    {
        if (_redo.Count == 0) return;
        _undo.Push(Take());
        Restore(_redo.Pop());
    }

    public void BeginStroke(Point p, double pressure)
    {
        if (Item == null || !IsBrushTool) return;
        Checkpoint();
        CurrentStroke.Clear();
        _strokeClock.Restart();
        CurrentStroke.Add([Math.Round(p.X, 2), Math.Round(p.Y, 2), Math.Round(pressure, 3), 0]);
        Redraw?.Invoke();
    }

    public void ExtendStroke(Point p, double pressure, double minStep)
    {
        if (CurrentStroke.Count == 0) return;
        var last = CurrentStroke[^1];
        if (Math.Abs(p.X - last[0]) + Math.Abs(p.Y - last[1]) < minStep) return;
        CurrentStroke.Add([Math.Round(p.X, 2), Math.Round(p.Y, 2), Math.Round(pressure, 3), _strokeClock.ElapsedMilliseconds]);
        Redraw?.Invoke();
    }

    /// <summary>The stroke is kept for the active letter (or as an ornament); then, unless Shift is held, the next letter.</summary>
    public void EndStroke(bool stay, double zoom = 1)
    {
        if (CurrentStroke.Count == 0) return;
        var pts = CurrentStroke.ToList();
        CurrentStroke.Clear();
        // like the Illustrator Paintbrush: the stroke is rebuilt from a few smooth Bézier curves (tolerance in screen pixels)
        var tol = StrokeGeometry.FitTolerance[Math.Clamp(Fidelity, 0, StrokeGeometry.FitTolerance.Length - 1)] / Math.Max(0.05, zoom);
        List<double[]>? fit = null;
        try { fit = pts.Count >= 2 ? StrokeGeometry.Fit(pts, tol) : null; } catch { fit = null; }
        var c = ChipAt(Active);
        if (Tool == "ornament")
            Ann.Segments.Add(new Segment { Kind = "ornament", Order = ++_strokeOrder, Brush = Tip.Copy(), Fidelity = Fidelity, Stroke = pts, Fit = fit });
        else if (Tool == "diacritic" && c != null)   // linguistic tashkeel belongs to its letter
            Ann.Segments.Add(new Segment { Kind = "diacritic", Order = ++_strokeOrder, Chip = Active, Letter = c.Ch, Form = Arabic.FormKey(c.Form), Brush = Tip.Copy(), Fidelity = Fidelity, Stroke = pts, Fit = fit });
        else if (c != null)
            Ann.Segments.Add(new Segment { Kind = "stroke", Order = ++_strokeOrder, Chip = Active, Letter = c.Ch, Form = Arabic.FormKey(c.Form), Brush = Tip.Copy(), Fidelity = Fidelity, Stroke = pts, Fit = fit });
        if (Tool == "brush" && !stay && AutoNextLetter && Chips.Count > 0)
        {
            for (int k = 1; k <= Chips.Count; k++)
            {
                int i = (Active + k) % Chips.Count;
                if (!IsDone(i)) { Active = i; break; }
            }
        }
        Refresh();
    }

    public void BackspacePoint()
    {
        if (Drawing.Count > 0) { Checkpoint(); Drawing.RemoveAt(Drawing.Count - 1); Redraw?.Invoke(); }
    }

    public void CancelDrawing() { Drawing.Clear(); Redraw?.Invoke(); }

    public void Undo() => UndoStep();

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
        Checkpoint();
        Ann.Segments.Remove(row.Segment);
        Refresh();
    }

    [RelayCommand] private void ClearAll()
    {
        Checkpoint();
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
        bool all = Chips.Count > 0 && Chips.All(c => Body(c.Chip.Index) != null || HasStrokes(c.Chip.Index));
        Ann.State = all ? "done" : "partial";
        Persist();
        int n = LetterExporter.Export(Item, Ann, _main.FullPath(Item.Png), _main.Letters, _main.Settings.Style);
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

    /// <summary>Asks the text and size of a new artboard, then opens it.</summary>
    public Func<(string Text, int W, int H)?>? NewArtboardDialog { get; set; }

    [RelayCommand]
    private void NewArtboard()
    {
        if (NewArtboardDialog?.Invoke() is not { } r) return;
        if (_main.NewArtboard(r.Text, r.W, r.H) is { } item) Open(item.Id, remember: true);
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
