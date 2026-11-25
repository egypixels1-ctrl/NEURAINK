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

/// <summary>The work saved for undo: the regions and strokes, the open pen path, the letter, the tool and the text.</summary>
public sealed record Snapshot(string Segments, List<PenNode> Drawing, int Active, string Tool, string Text);

/// <summary>
/// A piece open in a tab (like Illustrator documents): each tab keeps its own work, undo history, zoom and scroll while
/// another one is in front.
/// </summary>
public sealed partial class DocTab : ObservableObject
{
    [ObservableProperty] private int _id;
    [ObservableProperty] private string _text = "";
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private bool _isActive;
    /// <summary>Zoom of the picture (1 = 100 %); 0 = fit it when shown.</summary>
    [ObservableProperty] private double _zoom;
    [ObservableProperty] private string _fileInfo = "";
    public double ScrollH { get; set; }
    public double ScrollV { get; set; }
    public string IdText => $"#{Id}";
    public string ZoomText => Zoom > 0 ? $"@ {Zoom * 100:0}%" : "";
    partial void OnIdChanged(int value) => OnPropertyChanged(nameof(IdText));
    partial void OnZoomChanged(double value) => OnPropertyChanged(nameof(ZoomText));

    // the work kept while another tab is in front (null: not opened yet — read from the disk)
    internal Annotation? Ann { get; set; }
    internal List<PenNode> Drawing { get; set; } = [];
    internal Stack<Snapshot> Undo { get; } = new();
    internal Stack<Snapshot> Redo { get; } = new();
    internal Stack<int> History { get; } = new();
    internal int Active { get; set; }
    internal string Tool { get; set; } = "letter";
    internal int StrokeOrder { get; set; }
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
    private Stack<int> _history = new();
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

    // ------------------------------------------------------------------ tabs (like Illustrator documents)
    public ObservableCollection<DocTab> Tabs { get; } = [];
    [ObservableProperty] private DocTab? _currentTab;
    /// <summary>Every tab is closed.</summary>
    [ObservableProperty] private bool _noTabs;
    /// <summary>The view writes its zoom and scroll into the tab before another one comes to the front.</summary>
    public event Action<DocTab>? SavingView;

    partial void OnCurrentTabChanged(DocTab? value)
    {
        foreach (var t in Tabs) t.IsActive = t == value;
    }

    private Stack<Snapshot> _undo = new(), _redo = new();

    private DocTab NewTab(int id) => new() { Id = id, Text = _main.GetItem(id)?.Text ?? "" };

    /// <summary>The work of the tab in front is kept in it (another tab comes to the front).</summary>
    private void Stash()
    {
        if (CurrentTab is not { } t) return;
        SavingView?.Invoke(t);
        if (Item == null) return;
        t.Ann = Ann;
        t.Drawing = Drawing.Select(d => new PenNode { A = d.A, In = d.In, Out = d.Out }).ToList();
        t.Active = Active;
        t.Tool = Tool;
        t.StrokeOrder = _strokeOrder;
        CurrentStroke.Clear();
        SelectedOrder = 0;
    }

    /// <summary>Brings a tab to the front, with its work as it was left.</summary>
    [RelayCommand]
    public void Activate(DocTab? tab)
    {
        if (tab == null || tab == CurrentTab) return;
        Stash();
        CurrentTab = tab;
        Load(tab.Id, remember: false, keep: true);
        SaveTabs();
    }

    /// <summary>Opens a piece in a new tab (or brings its tab to the front).</summary>
    public void OpenInNewTab(int id)
    {
        if (Tabs.FirstOrDefault(t => t.Id == id) is { } open) { Activate(open); return; }
        if (_main.GetItem(id) == null) return;
        Stash();
        var tab = NewTab(id);
        int at = CurrentTab != null ? Tabs.IndexOf(CurrentTab) + 1 : Tabs.Count;
        Tabs.Insert(Math.Clamp(at, 0, Tabs.Count), tab);
        CurrentTab = tab;
        NoTabs = false;
        Load(id, remember: false, keep: false);
        SaveTabs();
    }

    /// <summary>Asks Save / Don't save / Cancel for a tab with unsaved changes (true, false, null).</summary>
    public Func<string, bool?>? AskSaveDialog { get; set; }

    [RelayCommand]
    public void CloseTab(DocTab? tab)
    {
        tab ??= CurrentTab;
        if (tab == null) return;
        if (tab.IsDirty)
        {
            if (tab != CurrentTab) Activate(tab);
            var answer = AskSaveDialog?.Invoke(Loc.F("Tabs.AskSave", tab.Text.Length > 0 ? tab.Text : tab.IdText)) ?? false;
            if (answer == null) return;
            if (answer == true) SaveInPlace();
        }
        int idx = Tabs.IndexOf(tab);
        bool front = tab == CurrentTab;
        if (front) { SavingView?.Invoke(tab); CurrentTab = null; }
        Tabs.Remove(tab);
        if (front)
        {
            if (Tabs.Count > 0) { CurrentTab = Tabs[Math.Clamp(idx, 0, Tabs.Count - 1)]; Load(CurrentTab.Id, remember: false, keep: true); }
            else ClearView(nothingLeft: false);
        }
        NoTabs = Tabs.Count == 0;
        SaveTabs();
    }

    /// <summary>Ctrl+Tab / Ctrl+Shift+Tab.</summary>
    public void CycleTab(int step)
    {
        if (Tabs.Count < 2 || CurrentTab == null) return;
        int i = (Tabs.IndexOf(CurrentTab) + step + Tabs.Count) % Tabs.Count;
        Activate(Tabs[i]);
    }

    /// <summary>The open tabs are remembered (they open again with the program).</summary>
    private void SaveTabs()
    {
        _main.Settings.OpenTabs = Tabs.Select(t => t.Id).ToList();
        _main.Settings.ActiveTab = CurrentTab?.Id ?? 0;
        _main.SaveSettings();
    }

    /// <summary>First visit of the page: the tabs of the last session, else the next piece to segment.</summary>
    public void RestoreTabs()
    {
        if (Tabs.Count > 0) return;
        foreach (var id in _main.Settings.OpenTabs.Where(i => _main.GetItem(i) != null).Distinct())
            Tabs.Add(NewTab(id));
        if (Tabs.Count == 0) { OpenNext(0); return; }
        var front = Tabs.FirstOrDefault(t => t.Id == _main.Settings.ActiveTab) ?? Tabs[0];
        CurrentTab = front;
        NoTabs = false;
        Load(front.Id, remember: false, keep: false);
    }

    /// <summary>Pieces removed from the library: their tabs close (nothing to save — they are gone).</summary>
    public void Forget(IReadOnlyCollection<int> ids)
    {
        foreach (var t in Tabs.Where(t => ids.Contains(t.Id)).ToList()) { t.IsDirty = false; CloseTab(t); }
    }

    /// <summary>Unsaved tabs when the program closes: save them all, leave them, or stay. False = stay.</summary>
    public bool ConfirmClose()
    {
        Stash();
        var dirty = Tabs.Where(t => t.IsDirty && t.Ann != null).ToList();
        if (dirty.Count == 0) return true;
        var r = System.Windows.MessageBox.Show(Application.Current?.MainWindow!, Loc.F("Tabs.AskSaveAll", dirty.Count), "NEURAINK",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (r == MessageBoxResult.Cancel) return false;
        if (r == MessageBoxResult.Yes)
            foreach (var t in dirty)
                if (_main.GetItem(t.Id) is { } item && t.Ann is { Segments.Count: > 0 } ann) SaveWork(item, ann);
        return true;
    }

    private void ClearView(bool nothingLeft)
    {
        Item = null; HasItem = false; NothingLeft = nothingLeft; Image = null; Original = null; _board = null; Reference = null; NotifyReference();
        Chips.Clear(); Segments.Clear(); Drawing.Clear(); CurrentStroke.Clear();
        SelectedOrder = 0;
        Hint = "";
        Redraw?.Invoke();
    }

    // ------------------------------------------------------------------ open
    public void OpenNext(int after)
    {
        var others = Tabs.Where(t => t != CurrentTab).Select(t => t.Id).ToHashSet();
        var id = _main.NextToSegment(after, others);
        if (id is { } i) Open(i, remember: after != 0);
        else
        {
            if (CurrentTab != null) { Tabs.Remove(CurrentTab); CurrentTab = null; }
            if (Tabs.Count > 0) { CurrentTab = Tabs[0]; Load(CurrentTab.Id, remember: false, keep: true); ShowToast(Loc.T("Ann.None")); }
            else ClearView(nothingLeft: true);
            NoTabs = false;
            SaveTabs();
        }
    }

    /// <summary>Opens a piece in the tab in front (its tab comes to the front if it is open already).</summary>
    public void Open(int id, bool remember)
    {
        if (_main.GetItem(id) == null) return;
        if (Tabs.FirstOrDefault(t => t.Id == id) is { } open && open != CurrentTab) { Activate(open); return; }
        if (CurrentTab?.Id == id && Item?.Id == id) return;   // in front already (its unsaved work stays)
        if (CurrentTab == null)
        {
            CurrentTab = NewTab(id);
            Tabs.Add(CurrentTab);
        }
        else if (CurrentTab.Id != id && CurrentTab.IsDirty) { OpenInNewTab(id); return; }   // unsaved work stays in its tab
        NoTabs = false;
        Load(id, remember, keep: false);
        SaveTabs();
    }

    /// <summary>
    /// Shows a piece in the tab in front. <paramref name="keep"/>: the tab's own work (undo, pen path, letter, tool) as it
    /// was left; otherwise the piece is read from the disk.
    /// </summary>
    private void Load(int id, bool remember, bool keep)
    {
        var item = _main.GetItem(id);
        var tab = CurrentTab!;
        if (item == null) return;
        if (remember && Item != null && Item.Id != id) tab.History.Push(Item.Id);
        bool fresh = !keep || tab.Ann == null;
        if (!keep)
        {
            if (tab.Id != id) tab.Zoom = 0;
            tab.Undo.Clear(); tab.Redo.Clear();
            tab.IsDirty = false;
        }
        tab.Id = id;
        _history = tab.History;
        _undo = tab.Undo;
        _redo = tab.Redo;
        Item = item;
        HasItem = true;
        NothingLeft = false;
        SelectedOrder = 0;
        CurrentStroke.Clear();
        if (fresh)
        {
            Ann = _main.Annotations.Load(id) ?? new Annotation { Id = id, File = item.File, Text = item.Text };
            Ann.Id = id;
            Ann.File = item.File;
            if (string.IsNullOrWhiteSpace(Ann.Text)) Ann.Text = item.Text;
        }
        else Ann = tab.Ann!;
        tab.Ann = Ann;
        tab.Text = Ann.Text;
        tab.FileInfo = item.File;
        Title = $"#{item.Id}";
        FileInfo = item.File;
        Drawing.Clear();
        if (!fresh) Drawing.AddRange(tab.Drawing.Select(d => new PenNode { A = d.A, In = d.In, Out = d.Out }));
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
        if (fresh)
        {
            var first = Chips.FirstOrDefault(c => !IsDone(c.Chip.Index));
            Active = first?.Chip.Index ?? 0;
            Tool = first == null ? "letter" : StepFor(first.Chip.Index);
        }
        else
        {
            Active = Math.Min(tab.Active, Math.Max(0, Chips.Count - 1));
            _restoring = true;
            Tool = tab.Tool;
            _restoring = false;
            OnPropertyChanged(nameof(IsBrushTool)); OnPropertyChanged(nameof(IsPenTool));
        }
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
        if (CurrentTab != null) CurrentTab.Text = Ann.Text;
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
        SelectedOrder = 0;
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
    /// <summary>StreamLine, like Procreate: the stroke follows the hand a little behind, without its shakes (0 … 100 %).</summary>
    public double StreamLine { get => _main.Settings.StreamLine; set { _main.Settings.StreamLine = (int)Math.Clamp(Math.Round(value), 0, 100); OnPropertyChanged(); SaveBrush(); } }
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
    private static readonly System.Text.Json.JsonSerializerOptions SnapJson = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    private Snapshot Take() => new(
        System.Text.Json.JsonSerializer.Serialize(Ann.Segments, SnapJson),
        Drawing.Select(d => new PenNode { A = d.A, In = d.In, Out = d.Out }).ToList(), Active, Tool, Ann.Text);

    /// <summary>Remembers the state before a change (Ctrl+Z goes back to it).</summary>
    public void Checkpoint()
    {
        if (CurrentTab != null) CurrentTab.IsDirty = true;
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
        if (CurrentTab != null) { CurrentTab.IsDirty = true; CurrentTab.Text = Ann.Text; }
        if (SelectedStroke == null) SelectedOrder = 0;
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

    // ---- StreamLine (live smoothing while drawing): the drawn point follows the hand like a weight on a short string
    private readonly List<double[]> _rawStroke = [];
    private double _sx, _sy;

    public void BeginStroke(Point p, double pressure)
    {
        if (Item == null || !IsBrushTool) return;
        Checkpoint();
        CurrentStroke.Clear();
        _rawStroke.Clear();
        _strokeClock.Restart();
        _sx = p.X; _sy = p.Y;
        double[] first = [Math.Round(p.X, 2), Math.Round(p.Y, 2), Math.Round(pressure, 3), 0];
        CurrentStroke.Add(first);
        _rawStroke.Add(first);
        Redraw?.Invoke();
    }

    /// <summary>How much of the way to the hand the drawn point moves at each step (1 = no StreamLine).</summary>
    private double Follow => 1 - 0.9 * Math.Clamp(StreamLine / 100.0, 0, 1);

    public void ExtendStroke(Point p, double pressure, double minStep)
    {
        if (CurrentStroke.Count == 0) return;
        long ms = _strokeClock.ElapsedMilliseconds;
        var lastRaw = _rawStroke[^1];
        if (Math.Abs(p.X - lastRaw[0]) + Math.Abs(p.Y - lastRaw[1]) >= minStep * 0.5)
            _rawStroke.Add([Math.Round(p.X, 2), Math.Round(p.Y, 2), Math.Round(pressure, 3), ms]);
        double f = Follow;
        _sx += (p.X - _sx) * f;
        _sy += (p.Y - _sy) * f;
        var last = CurrentStroke[^1];
        if (Math.Abs(_sx - last[0]) + Math.Abs(_sy - last[1]) < minStep) return;
        CurrentStroke.Add([Math.Round(_sx, 2), Math.Round(_sy, 2), Math.Round(pressure, 3), ms]);
        Redraw?.Invoke();
    }

    /// <summary>The stroke is kept for the active letter (or as an ornament); then, unless Shift is held, the next letter.</summary>
    public void EndStroke(bool stay, double zoom = 1)
    {
        if (CurrentStroke.Count == 0) return;
        // StreamLine catches up with the hand where it left the paper (like Procreate)
        var end = _rawStroke[^1];
        double gap = Math.Sqrt((end[0] - _sx) * (end[0] - _sx) + (end[1] - _sy) * (end[1] - _sy));
        if (StreamLine > 0 && gap > 0.5)
        {
            int steps = Math.Clamp((int)(gap / 2), 1, 12);
            for (int k = 1; k <= steps; k++)
            {
                double t = k / (double)steps;
                CurrentStroke.Add([Math.Round(_sx + (end[0] - _sx) * t, 2), Math.Round(_sy + (end[1] - _sy) * t, 2), end[2], end[3]]);
            }
        }
        var pts = CurrentStroke.ToList();
        var raw = StreamLine > 0 ? _rawStroke.ToList() : null;
        CurrentStroke.Clear();
        _rawStroke.Clear();
        // a part of the selected stroke drawn again (like Illustrator "Edit Selected Paths")
        if (SelectedStroke is { } sel && RedrawPart(sel, pts, 12 / Math.Max(0.05, zoom), zoom)) { Refresh(); return; }
        // the stroke is rebuilt from a smooth spline (see StrokeGeometry.Fit)
        List<double[]>? fit = null;
        try { fit = pts.Count >= 2 ? StrokeGeometry.Fit(pts, zoom, Fidelity) : null; } catch { fit = null; }
        var c = ChipAt(Active);
        Segment? made = null;
        if (Tool == "ornament")
            made = new Segment { Kind = "ornament" };
        else if (Tool == "diacritic" && c != null)   // linguistic tashkeel belongs to its letter
            made = new Segment { Kind = "diacritic", Chip = Active, Letter = c.Ch, Form = Arabic.FormKey(c.Form) };
        else if (c != null)
            made = new Segment { Kind = "stroke", Chip = Active, Letter = c.Ch, Form = Arabic.FormKey(c.Form) };
        if (made != null)
        {
            made.Order = ++_strokeOrder;
            made.Brush = Tip.Copy();
            made.Fidelity = Fidelity;
            made.StreamLine = StreamLine > 0 ? (int)StreamLine : 0;
            made.Stroke = pts;
            made.Raw = raw;
            made.Fit = fit;
            Ann.Segments.Add(made);
        }
        SelectedOrder = 0;
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

    // ------------------------------------------------------------------ editing a stroke after drawing (like Illustrator)
    /// <summary>The stroke selected for editing (Ctrl + click): its anchors and handles show and can be dragged.</summary>
    [ObservableProperty] private int _selectedOrder;
    partial void OnSelectedOrderChanged(int value) => Redraw?.Invoke();

    private static bool IsStrokeKind(Segment s) => s.Kind is "stroke" or "diacritic" or "ornament" && s.Brush != null && s.Stroke is { Count: > 0 };

    public Segment? SelectedStroke => SelectedOrder == 0 ? null : Ann.Segments.FirstOrDefault(s => s.Order == SelectedOrder && IsStrokeKind(s));

    /// <summary>The stroke under a point (the top one): inside its ink, or within <paramref name="tol"/> of its path.</summary>
    public Segment? HitStroke(Point p, double tol)
    {
        Segment? best = null;
        double bestD = tol;
        foreach (var s in Ann.Segments.Where(IsStrokeKind).OrderByDescending(x => x.Order))
        {
            try { if (StrokeGeometry.ForSegment(s).FillContains(p)) return s; } catch { }
            foreach (var q in PathOf(s))
            {
                double d = Math.Sqrt((q[0] - p.X) * (q[0] - p.X) + (q[1] - p.Y) * (q[1] - p.Y));
                if (d < bestD) { bestD = d; best = s; }
            }
        }
        return best;
    }

    public void SelectStroke(Segment? s)
    {
        SelectedOrder = s?.Order ?? 0;
        if (s != null && s.Chip >= 0 && s.Chip < Chips.Count && s.Kind != "ornament") { Active = s.Chip; Refresh(); }
    }

    /// <summary>The path a stroke follows: its fitted curves sampled, else its points.</summary>
    private static List<double[]> PathOf(Segment s) =>
        s.Fit is { Count: > 0 } f ? StrokeGeometry.SampleFit(f) : s.Stroke!.Select(p => new[] { p[0], p[1], p.Length > 2 ? p[2] : 0 }).ToList();

    /// <summary>Anchors of the selected stroke: the ends of its curves.</summary>
    public static Point AnchorOf(List<double[]> fit, int k) => k < fit.Count ? new Point(fit[k][0], fit[k][1]) : new Point(fit[^1][6], fit[^1][7]);

    /// <summary>What a click lands on in the selected stroke: anchor k, its in handle or its out handle.</summary>
    public (int K, string Part)? HitFit(Point p, double r)
    {
        if (SelectedStroke?.Fit is not { Count: > 0 } fit) return null;
        for (int k = 0; k <= fit.Count; k++)
        {
            if (k < fit.Count && Near(fit[k][2], fit[k][3])) return (k, "out");
            if (k > 0 && Near(fit[k - 1][4], fit[k - 1][5])) return (k, "in");
        }
        for (int k = 0; k <= fit.Count; k++)
        {
            var a = AnchorOf(fit, k);
            if (Near(a.X, a.Y)) return (k, "anchor");
        }
        return null;
        bool Near(double x, double y) => (x - p.X) * (x - p.X) + (y - p.Y) * (y - p.Y) <= r * r;
    }

    /// <summary>Moves anchor k of the selected stroke with its handles.</summary>
    public void MoveFitAnchor(int k, Point to)
    {
        if (SelectedStroke?.Fit is not { Count: > 0 } fit) return;
        var a = AnchorOf(fit, k);
        double dx = to.X - a.X, dy = to.Y - a.Y;
        if (k < fit.Count) { fit[k][0] += dx; fit[k][1] += dy; fit[k][2] += dx; fit[k][3] += dy; }
        if (k > 0) { fit[k - 1][6] += dx; fit[k - 1][7] += dy; fit[k - 1][4] += dx; fit[k - 1][5] += dy; }
        Redraw?.Invoke();
    }

    /// <summary>Moves a handle; the opposite one turns with it (a smooth anchor stays smooth) unless Alt breaks them apart.</summary>
    public void MoveFitHandle(int k, bool outHandle, Point to, bool alt)
    {
        if (SelectedStroke?.Fit is not { Count: > 0 } fit) return;
        var a = AnchorOf(fit, k);
        if (outHandle && k < fit.Count) { fit[k][2] = to.X; fit[k][3] = to.Y; }
        if (!outHandle && k > 0) { fit[k - 1][4] = to.X; fit[k - 1][5] = to.Y; }
        if (!alt)
        {
            var dir = a - to;
            if (dir.Length > 1e-6)
            {
                dir.Normalize();
                if (outHandle && k > 0)
                {
                    double len = new Vector(fit[k - 1][4] - a.X, fit[k - 1][5] - a.Y).Length;
                    fit[k - 1][4] = a.X + dir.X * len; fit[k - 1][5] = a.Y + dir.Y * len;
                }
                else if (!outHandle && k < fit.Count)
                {
                    double len = new Vector(fit[k][2] - a.X, fit[k][3] - a.Y).Length;
                    fit[k][2] = a.X + dir.X * len; fit[k][3] = a.Y + dir.Y * len;
                }
            }
        }
        Redraw?.Invoke();
    }

    /// <summary>After dragging anchors or handles: the stroke's points follow its new curves (the hand stays in Raw).</summary>
    public void EndFitEdit()
    {
        if (SelectedStroke is not { Fit: { Count: > 0 } fit } s) return;
        s.Raw ??= s.Stroke;
        s.Stroke = StrokeGeometry.SampleFit(fit).Select(q => new[] { Math.Round(q[0], 2), Math.Round(q[1], 2), Math.Round(q[2], 3), 0 }).ToList();
        s.Edited = true;
        Refresh();
    }

    /// <summary>
    /// A new stroke that starts on the selected one redraws that part of it: between where it starts and where it ends on
    /// the stroke, or from where it starts to the end it heads for. Returns false when it does not start on the stroke.
    /// </summary>
    private bool RedrawPart(Segment sel, List<double[]> pts, double tol, double zoom)
    {
        if (pts.Count < 2) return false;
        var path = PathOf(sel).Select(q => new[] { q[0], q[1], q.Length > 2 ? q[2] : 0, 0 }).ToList();
        if (path.Count < 2) return false;
        (int I, double D) Nearest(double[] p)
        {
            int bi = 0; double bd = double.MaxValue;
            for (int i = 0; i < path.Count; i++)
            {
                double d = (path[i][0] - p[0]) * (path[i][0] - p[0]) + (path[i][1] - p[1]) * (path[i][1] - p[1]);
                if (d < bd) { bd = d; bi = i; }
            }
            return (bi, Math.Sqrt(bd));
        }
        var (ia, da) = Nearest(pts[0]);
        if (da > tol) return false;
        var (ib, db) = Nearest(pts[^1]);
        List<double[]> joined;
        if (db <= tol && Math.Abs(ib - ia) > 2)
        {
            if (ia <= ib) joined = [.. path.Take(ia), .. pts, .. path.Skip(ib + 1)];
            else { var rev = Enumerable.Reverse(pts).ToList(); joined = [.. path.Take(ib), .. rev, .. path.Skip(ia + 1)]; }
        }
        else
        {
            // heading along the stroke: its tail is replaced; heading back: its head
            int j = Math.Min(ia + 1, path.Count - 1), i0 = Math.Max(0, j - 1);
            double tx = path[j][0] - path[i0][0], ty = path[j][1] - path[i0][1];
            var far = pts[Math.Min(pts.Count - 1, 8)];
            bool forward = (far[0] - pts[0][0]) * tx + (far[1] - pts[0][1]) * ty >= 0;
            if (forward) joined = [.. path.Take(ia), .. pts];
            else { var rev = Enumerable.Reverse(pts).ToList(); joined = [.. rev, .. path.Skip(ia + 1)]; }
        }
        if (joined.Count < 2) return false;
        sel.Raw ??= sel.Stroke;
        sel.Stroke = joined;
        try { sel.Fit = StrokeGeometry.Fit(joined, zoom, Fidelity); } catch { sel.Fit = null; }
        sel.Edited = true;
        return true;
    }

    /// <summary>Delete: the selected stroke goes.</summary>
    public bool DeleteSelectedStroke()
    {
        if (SelectedStroke is not { } s) return false;
        Checkpoint();
        Ann.Segments.Remove(s);
        SelectedOrder = 0;
        Refresh();
        return true;
    }

    public void BackspacePoint()
    {
        if (Drawing.Count > 0) { Checkpoint(); Drawing.RemoveAt(Drawing.Count - 1); Redraw?.Invoke(); }
    }

    public void CancelDrawing() { Drawing.Clear(); SelectedOrder = 0; Redraw?.Invoke(); }

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
        var (all, n) = SaveWork(Item, Ann);
        ShowToast(all ? Loc.F("Ann.Saved", n) : Loc.T("Ann.Partial"));
        if (CurrentTab != null) CurrentTab.IsDirty = false;
        OpenNext(Item.Id);
    }

    /// <summary>Saves the tab in front without moving on (closing a tab: "Save").</summary>
    public void SaveInPlace()
    {
        if (Item == null || Ann.Segments.Count == 0) { if (CurrentTab != null) CurrentTab.IsDirty = false; return; }
        SaveWork(Item, Ann);
        if (CurrentTab != null) CurrentTab.IsDirty = false;
    }

    /// <summary>Writes a segmentation and cuts its letters into the letter library: (every letter done, letters cut).</summary>
    private (bool All, int Letters) SaveWork(LibraryItem item, Annotation ann)
    {
        var letters = Arabic.Letters(ann.Text);
        bool all = letters.Count > 0 && letters.All(c => ann.Segments.Any(x => x.Chip == c.Index && x.Kind is "letter" or "stroke"));
        ann.State = all ? "done" : "partial";
        try { _main.Annotations.Save(ann); }
        catch (Exception ex) { Dialogs.Error(ex.Message); }
        int n = LetterExporter.Export(item, ann, _main.FullPath(item.Png), _main.Letters, _main.Settings.Style);
        _main.RefreshCounts();
        return (all, n);
    }

    [RelayCommand]
    private void Reject()
    {
        if (Item == null) return;
        Ann.State = "rejected";
        Persist();
        if (CurrentTab != null) CurrentTab.IsDirty = false;
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
        if (_main.NewArtboard(r.Text, r.W, r.H) is { } item) OpenInNewTab(item.Id);
    }

    /// <summary>Every tab closed: the next piece to segment opens.</summary>
    [RelayCommand] private void OpenNextPiece() { NoTabs = false; OpenNext(0); }

    [RelayCommand] private void Skip() { if (Item != null && LeaveCurrent()) OpenNext(Item.Id); }

    [RelayCommand]
    private void Previous()
    {
        if (_history.Count == 0 || !LeaveCurrent()) return;
        Open(_history.Pop(), remember: false);
    }

    /// <summary>Before the tab in front shows another piece: its unsaved work is saved, dropped, or the move is cancelled.</summary>
    private bool LeaveCurrent()
    {
        if (CurrentTab is not { IsDirty: true } t) return true;
        var answer = AskSaveDialog?.Invoke(Loc.F("Tabs.AskSave", t.Text.Length > 0 ? t.Text : t.IdText)) ?? false;
        if (answer == null) return false;
        if (answer == true) SaveInPlace();
        t.IsDirty = false;
        return true;
    }

    private void Persist()
    {
        try { _main.Annotations.Save(Ann); }
        catch (Exception ex) { Dialogs.Error(ex.Message); }
    }
}
