using System.Collections.ObjectModel;
using Neuraink.App.Services;
using Neuraink.Core;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Neuraink.App.ViewModels;

public sealed partial class NavItem : ObservableObject
{
    public required string Key { get; init; }       // localization key
    public required string Glyph { get; init; }     // Segoe Fluent Icons glyph
    /// <summary>A drawn icon instead of the glyph (a resource key, e.g. BrainGeometry).</summary>
    public string? IconKey { get; init; }
    public System.Windows.Media.Geometry? Icon => IconKey != null ? Application.Current?.TryFindResource(IconKey) as System.Windows.Media.Geometry : null;
    public bool HasIcon => IconKey != null;
    public required Func<object?> Page { get; init; }
    /// <summary>Planned page: shown with a "Soon" badge, not selectable.</summary>
    public bool IsSoon { get; init; }
    public bool IsEnabled => !IsSoon;
    /// <summary>The first row (Dashboard) carries the arrow that folds the menu.</summary>
    public bool IsFirst { get; init; }
    public string Title => Loc.T(Key);
    public void Refresh() => OnPropertyChanged(nameof(Title));
    [ObservableProperty] private string _count = "";
}

public sealed partial class MainViewModel : ObservableObject
{
    public static MainViewModel? Current { get; private set; }

    public AppSettings Settings { get; }
    public NeurainkDatabase Database { get; private set; } = null!;
    public Library Library { get; private set; } = null!;
    public AnnotationStore Annotations { get; private set; } = null!;
    public LetterLibrary Letters { get; private set; } = null!;
    public ArtboardStore Artboards { get; private set; } = null!;

    /// <summary>A piece by id: from the library, or an artboard (ids from 900001).</summary>
    public LibraryItem? GetItem(int id)
    {
        if (id >= ArtboardStore.FirstId) return Artboards.Get(id) is { } a ? Artboards.ToItem(a) : null;
        if (Library.Get(id) is { } here) return here;
        // a piece of another style folder (a tab opened before switching styles, a segmentation …)
        foreach (var lib in OtherLibraries) if (lib.Get(id) is { } other) return other;
        return null;
    }

    private List<Library>? _others;
    private List<Library> OtherLibraries => _others ??= Database.Exists
        ? Database.Styles().Select(st => Path.Combine(Database.LibraryRoot, st))
            .Where(f => !string.Equals(f, Library.Folder, StringComparison.OrdinalIgnoreCase)).Select(f => new Library(f)).ToList()
        : [];

    /// <summary>Full path of a piece's file (library paths are relative; artboard paths are already full).</summary>
    public string FullPath(string path) => Library.FullPath(path);
    public string FullPath(LibraryItem item, string path) => item.PathOf(path);

    /// <summary>
    /// A new white artboard to write on directly: its picture is a white page of the chosen size; its letters are
    /// handwritten training data (origin "artboard").
    /// </summary>
    public LibraryItem? NewArtboard(string text, int width, int height)
    {
        if (!Database.Exists) { Dialogs.Error(Loc.T("Db.NotSet")); return null; }
        var a = new Artboard { Id = Artboards.NextId(), Text = text.Trim(), Width = width, Height = height };
        Directory.CreateDirectory(Artboards.Folder);
        Services.PaperWriter.WhitePng(Artboards.PngPath(a.Id), width, height);
        Artboards.Save(a);
        LibraryPage.Reload();
        return Artboards.ToItem(a);
    }

    public DashboardViewModel Dashboard { get; }
    public LibraryViewModel LibraryPage { get; }
    public AnnotateViewModel Annotate { get; }
    public LettersViewModel LettersPage { get; }
    public SettingsViewModel SettingsPage { get; }

    public ObservableCollection<NavItem> Nav { get; } = [];

    /// <summary>The main menu folded to its icons (the names show as tooltips); remembered.</summary>
    public bool NavCollapsed
    {
        get => Settings.NavCollapsed;
        set { Settings.NavCollapsed = value; OnPropertyChanged(); SaveSettings(); }
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand] private void ToggleNav() => NavCollapsed = !NavCollapsed;
    [ObservableProperty] private NavItem? _selectedNav;
    [ObservableProperty] private object? _currentPage;

    public string VersionText
    {
        get
        {
            var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            return $"v{v?.Major}.{v?.Minor}.{v?.Build}";
        }
    }

    public MainViewModel(AppSettings settings)
    {
        Current = this;
        Settings = settings;
        LoadData();
        Dashboard = new DashboardViewModel(this);
        LibraryPage = new LibraryViewModel(this);
        Annotate = new AnnotateViewModel(this);
        LettersPage = new LettersViewModel(this);
        SettingsPage = new SettingsViewModel(this);

        Nav.Add(new NavItem { Key = "Nav.Dashboard", Glyph = "\uE9D2", Page = () => Dashboard, IsFirst = true });
        Nav.Add(new NavItem { Key = "Nav.Library", Glyph = "\uE8F1", Page = () => LibraryPage });
        Nav.Add(new NavItem { Key = "Nav.Annotate", Glyph = "\uE70F", IconKey = "BrainGeometry", Page = () => Annotate });
        Nav.Add(new NavItem { Key = "Nav.Letters", Glyph = "\uE8D2", Page = () => LettersPage });
        Nav.Add(new NavItem { Key = "Nav.Compose", Glyph = "\uE945", Page = () => null, IsSoon = true });
        Nav.Add(new NavItem { Key = "Nav.Train", Glyph = "\uE99A", Page = () => null, IsSoon = true });
        Nav.Add(new NavItem { Key = "Nav.Settings", Glyph = "\uE713", Page = () => SettingsPage });

        LanguageService.Changed += () => { foreach (var n in Nav) n.Refresh(); RefreshCounts(); };
        RefreshCounts();
        SelectedNav = Nav[0];
    }

    /// <summary>(Re)opens the library, the annotations and the letter library from the paths in Settings.</summary>
    public void LoadData()
    {
        Database = new NeurainkDatabase(Settings.DatabasePath);
        // a database of an older version gets the folders added since (e.g. Artboards); nothing is replaced
        if (Database.Exists && !Database.IsComplete) try { Database.CreateMissing(); } catch { }
        // older layout: a style folder next to Library\ (e.g. diwani) moves into Library\
        if (Database.Exists) Database.MoveLegacyStyles();
        _others = null;
        Artboards = new ArtboardStore(Database.IsSet ? Database.ArtboardsFolder : "");
        Library = new Library(Database.LibraryFolder(Settings.Style));
        Annotations = new AnnotationStore(Database.IsSet ? Database.AnnotationsFolder : "");
        Letters = new LetterLibrary(Database.IsSet ? Database.LettersFolder : "");
    }

    public void ReloadAll()
    {
        LoadData();
        RefreshCounts();
        LibraryPage.Reload();
        Dashboard.Refresh();
        LettersPage.Refresh();
        SettingsPage?.RefreshDatabase();
    }

    public void RefreshCounts()
    {
        foreach (var n in Nav)
            n.Count = n.Key switch
            {
                "Nav.Library" => Library.Items.Count > 0 ? Library.Items.Count.ToString("N0") : "",
                "Nav.Annotate" => Annotations.CountSegmented > 0 ? Annotations.CountSegmented.ToString("N0") : "",
                _ => "",
            };
    }

    partial void OnSelectedNavChanged(NavItem? value)
    {
        if (value == null) return;
        if (value.IsSoon) return;
        var page = value.Page();
        if (page == Dashboard) Dashboard.Refresh();
        if (page == LettersPage) LettersPage.Refresh();
        if (page == Annotate && Annotate.Item == null && Annotate.Tabs.Count == 0 && !Annotate.NoTabs) Annotate.RestoreTabs();
        CurrentPage = page;
    }

    public void Go(object page)
    {
        var nav = Nav.FirstOrDefault(n => n.Page() == page);
        if (nav != null) SelectedNav = nav;
    }

    public void OpenAnnotate(int id, bool newTab = false)
    {
        if (Annotate.Tabs.Count == 0 && !Annotate.NoTabs) Annotate.RestoreTabs();
        if (newTab) Annotate.OpenInNewTab(id); else Annotate.Open(id, remember: true);
        Go(Annotate);
    }

    // ------------------------------------------------------------------ adding and removing pieces of the library
    /// <summary>A long job in progress (adding a folder …), shown at the bottom of the window.</summary>
    [ObservableProperty] private string _busyText = "";

    /// <summary>The style folders of Library\ (diwani, thuluth …).</summary>
    public List<string> StyleFolders => Database.Exists ? Database.Styles() : [];
    /// <summary>The style folder shown in the library and segmented in Knowledge Studio.</summary>
    public string CurrentStyle => Library.Found ? Path.GetFileName(Library.Folder) : Settings.Style;
    /// <summary>The style written into the letters (its Arabic name, as before: ديواني).</summary>
    public string StyleLabelOf(LibraryItem item) => StyleNames.Arabic(item.IsArtboard || item.Folder.Length == 0 ? CurrentStyle : item.Style);

    /// <summary>Shows another style folder in the library (and in Knowledge Studio's "next piece").</summary>
    public void SwitchStyle(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || folder == CurrentStyle) return;
        Settings.Style = folder;
        SaveSettings();
        ReloadAll();
    }

    /// <summary>
    /// Add to library (Knowledge Studio's + and the library's "Add pieces"): a folder of pictures (or some files) → a style
    /// folder of Library\ → move or copy → each picture cleaned, traced to SVG, given a thumbnail and a row → the count, with
    /// OK to keep them or Cancel to take everything back (moved pictures included). The text comes from the file name.
    /// </summary>
    public async Task AddToLibrary(bool folder, IReadOnlyList<string>? given = null, string? into = null, bool? moveFiles = null, bool confirm = true)
    {
        if (!Database.Exists) { Dialogs.Error(Loc.T("Db.NotSet")); return; }
        if (BusyText.Length > 0) return;
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        List<string> files;
        if (given != null) files = given.ToList();
        else if (folder)
        {
            if (Dialogs.PickFolder(desktop, Loc.T("Lib.PickSource")) is not { } src) return;
            files = Directory.EnumerateFiles(src).Where(PieceImporter.IsPicture).OrderBy(f => f, StringComparer.Ordinal).ToList();
            if (files.Count == 0) { Dialogs.Error(Loc.T("Lib.NoPictures")); return; }
        }
        else
        {
            var d = new Microsoft.Win32.OpenFileDialog
            {
                Filter = Loc.T("Board.Pictures") + "|*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.tif;*.tiff",
                Multiselect = true, InitialDirectory = desktop,
            };
            if (d.ShowDialog(Application.Current?.MainWindow) != true || d.FileNames.Length == 0) return;
            files = d.FileNames.ToList();
        }
        // the style folder: a folder of Library\ (a new one can be made in the window)
        Directory.CreateDirectory(Database.LibraryRoot);
        var start = Database.LibraryRoot;
        var target = into ?? Dialogs.PickFolder(start, Loc.T("Lib.PickStyle"));
        if (target == null) return;
        if (!string.Equals(Path.GetFullPath(Path.GetDirectoryName(target.TrimEnd('\\', '/')) ?? ""), Path.GetFullPath(Database.LibraryRoot).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
        {
            Dialogs.Error(Loc.F("Lib.NotAStyle", Database.LibraryRoot));
            return;
        }
        var style = Path.GetFileName(target.TrimEnd('\\', '/'));
        int how = moveFiles is { } mf ? (mf ? 0 : 1) : Views.ChoiceWindow.Ask(Loc.F("Lib.MoveOrCopy", files.Count, StyleDisplay(style)),
            [Loc.T("Lib.Move"), Loc.T("Lib.Copy"), Loc.T("Common.Cancel")], accent: 1, cancel: 2);
        if (how is < 0 or 2) return;
        bool move = how == 0;

        var used = Database.UsedIds();
        var hashes = new HashSet<string>(Database.Styles().SelectMany(st => LibraryEditor.Column(Path.Combine(Database.LibraryRoot, st), "hash")));
        var added = new List<PieceImporter.Added>();
        var done = new List<string>();
        var noName = new List<string>();
        var failed = new List<string>();
        int dupes = 0, n = 0;
        try
        {
            foreach (var f in files)
            {
                BusyText = Loc.F("Lib.Adding", ++n, files.Count);
                if (LibraryEditor.CleanName(Path.GetFileNameWithoutExtension(f)).Length == 0) { noName.Add(Path.GetFileName(f)); continue; }
                try
                {
                    var r = await Task.Run(() => PieceImporter.Import(f, target, used, hashes));
                    used.Add(r.Id);
                    added.Add(r);
                    done.Add(f);
                }
                catch (PieceImporter.DuplicateException) { dupes++; }
                catch (Exception ex) { failed.Add($"{Path.GetFileName(f)} — {ex.Message}"); }
            }
        }
        finally { BusyText = ""; }

        var msg = Loc.F("Lib.AddedN", added.Count, StyleDisplay(style));
        if (dupes > 0) msg += "\n" + Loc.F("Lib.Dupes", dupes);
        if (noName.Count > 0) msg += "\n\n" + Loc.F("Lib.Skipped", noName.Count) + "\n" + string.Join("\n", noName.Take(15)) + (noName.Count > 15 ? "\n…" : "");
        if (failed.Count > 0) msg += "\n\n" + Loc.F("Lib.AddFailed", string.Join("\n", failed.Take(10)));
        bool keep = added.Count == 0 || !confirm || Views.ChoiceWindow.Ask(msg, [Loc.T("Common.OK"), Loc.T("Common.Cancel")], accent: 0, cancel: 1) == 0;
        if (added.Count == 0 && confirm) Dialogs.Info(msg);
        if (!keep)
        {
            // everything taken back: the files made and the rows (the pictures were not moved yet)
            foreach (var f in added.SelectMany(a => a.Files)) try { if (File.Exists(f)) File.Delete(f); } catch { }
            LibraryEditor.RemoveRows(target, added.Select(a => a.Id).ToList());
            ReloadAll();
            return;
        }
        if (move && done.Count > 0) { try { RecycleBin.Send(done); } catch (Exception ex) { Dialogs.Error(ex.Message); } }
        Settings.Style = style;
        SaveSettings();
        ReloadAll();
        LibraryPage.ShowAddedLast();
    }

    /// <summary>
    /// Move to › / Copy to › (right click in the library): pieces go to another style folder. A move keeps the id, the
    /// segmentation and the letters (their style is written again); a copy is a new piece (new id, to do). Undo takes it back.
    /// </summary>
    public void MovePieces(IReadOnlyList<LibraryItem> items, string targetStyle, bool copy)
    {
        var target = Path.Combine(Database.LibraryRoot, targetStyle);
        var list = items.Where(i => !i.IsArtboard && !string.Equals(i.Folder.TrimEnd('\\', '/'), target, StringComparison.OrdinalIgnoreCase)).ToList();
        if (list.Count == 0) return;
        Directory.CreateDirectory(target);
        var used = Database.UsedIds();
        var undo = new List<Action>();
        int count = 0;
        foreach (var it in list)
        {
            try
            {
                var row = LibraryEditor.GetRow(it.Folder, it.Id) ?? new Dictionary<string, string>
                {
                    ["id"] = it.Id.ToString(), ["text"] = it.Text, ["file"] = it.File, ["status"] = it.Status, ["tier"] = it.Tier,
                    ["clean_png"] = it.Png, ["svg"] = it.Svg, ["thumb"] = it.Thumb,
                };
                var orig = new Dictionary<string, string>(row);
                int id = copy ? NextFree(used) : it.Id;
                used.Add(id);
                var name = LibraryEditor.BaseName(id, it.Text);
                var made = new List<(string From, string To)>();
                foreach (var (col, rel, sub, ext) in new[] { ("clean_png", it.Png, "clean_png", ".png"), ("svg", it.Svg, "svg", ".svg"), ("thumb", it.Thumb, "thumbs", Path.GetExtension(it.Thumb)) })
                {
                    if (string.IsNullOrWhiteSpace(rel)) { row[col] = ""; continue; }
                    var from = it.PathOf(rel);
                    if (!File.Exists(from)) continue;
                    Directory.CreateDirectory(Path.Combine(target, sub));
                    var to = Path.Combine(target, sub, name + (string.IsNullOrEmpty(ext) ? Path.GetExtension(from) : ext));
                    if (copy) File.Copy(from, to, overwrite: true); else File.Move(from, to, overwrite: true);
                    made.Add((from, to));
                    row[col] = $"{sub}/{Path.GetFileName(to)}";
                }
                row["id"] = id.ToString();
                if (copy) { row["note"] = $"copied from #{it.Id} ({it.Style})"; row.Remove("hash"); }
                LibraryEditor.AddRow(target, row);
                if (!copy) LibraryEditor.RemoveRows(it.Folder, [it.Id]);
                var src = it;
                undo.Add(() =>
                {
                    LibraryEditor.RemoveRows(target, [id]);
                    foreach (var (from, to) in made)
                        try { if (copy) File.Delete(to); else File.Move(to, from, overwrite: true); } catch { }
                    if (!copy) LibraryEditor.AddRow(src.Folder, orig);
                });
                // a moved piece that is segmented: its letters are written again under the new style
                if (!copy && Annotations.IsSegmented(id) && Annotations.Load(id) is { } ann)
                {
                    var moved = new Library(target).Get(id);
                    if (moved != null) LetterExporter.Export(moved, ann, moved.PathOf(moved.Png), Letters, StyleNames.Arabic(targetStyle));
                }
                count++;
            }
            catch (Exception ex) { Dialogs.Error($"#{it.Id} — {ex.Message}"); }
        }
        ReloadAll();
        var text = Loc.F(copy ? "Lib.CopiedN" : "Lib.MovedN", count, StyleDisplay(targetStyle));
        LibraryPage.OfferUndo(text, () =>
        {
            for (int i = undo.Count - 1; i >= 0; i--) try { undo[i](); } catch { }
            ReloadAll();
        });
    }

    /// <summary>A style folder as it reads in the language of the program (ديواني / Diwani).</summary>
    public static string StyleDisplay(string folder) =>
        LanguageService.CurrentFlow == FlowDirection.RightToLeft ? StyleNames.Arabic(folder) : StyleNames.English(folder);

    private static int NextFree(HashSet<int> used)
    {
        int id = used.Where(i => i < ArtboardStore.FirstId).DefaultIfEmpty(0).Max() + 1;
        return id;
    }

    /// <summary>Makes a new style folder in Library\ (its name in Latin letters, e.g. thuluth); null if cancelled.</summary>
    public string? NewStyleFolder()
    {
        var name = Views.TextPromptWindow.Ask(Loc.T("Lib.NewStyle"), Loc.T("Lib.NewStyleHint"), "");
        if (string.IsNullOrWhiteSpace(name)) return null;
        name = string.Join("_", name.Trim().Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        if (name.Length == 0) return null;
        Directory.CreateDirectory(Path.Combine(Database.LibraryRoot, name));
        return name;
    }

    /// <summary>
    /// Removes pieces: their files (picture, SVG, thumbnail; an artboard's page) go to the Recycle Bin and their rows leave
    /// report.csv. When some are segmented, the user chooses whether their segmentation and letters go too.
    /// </summary>
    public bool DeletePieces(IReadOnlyList<LibraryItem> items)
    {
        if (items.Count == 0) return false;
        var name = items.Count == 1 ? $"«{items[0].Text}» #{items[0].Id}" : Loc.F("Lib.NPieces", items.Count);
        if (!Dialogs.Confirm(Loc.F("Lib.DeleteAsk", name))) return false;
        bool dropWork = false;
        int worked = items.Count(i => Annotations.State(i.Id) != "");
        if (worked > 0)
        {
            var r = MessageBox.Show(Application.Current?.MainWindow!, Loc.F("Lib.DeleteWork", worked), "NEURAINK",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel) return false;
            dropWork = r == MessageBoxResult.Yes;
        }
        var files = new List<string>();
        var rows = new List<int>();
        var work = new List<int>();
        foreach (var it in items)
        {
            if (it.IsArtboard) { files.Add(Artboards.JsonPath(it.Id)); files.Add(Artboards.PngPath(it.Id)); }
            else
            {
                rows.Add(it.Id);
                foreach (var rel in new[] { it.Png, it.Svg, it.Thumb })
                    if (!string.IsNullOrWhiteSpace(rel)) files.Add(it.PathOf(rel));
            }
            if (dropWork && Annotations.State(it.Id) != "")
            {
                Letters.RemoveSource(it.Id);
                var ann = Path.Combine(Annotations.Folder, $"{it.Id:00000}.json");
                if (File.Exists(ann)) files.Add(ann);
                work.Add(it.Id);
            }
        }
        Annotate.Forget(items.Select(i => i.Id).ToList());
        try
        {
            foreach (var g in items.Where(i => !i.IsArtboard).GroupBy(i => i.Folder))
                LibraryEditor.RemoveRows(g.Key, g.Select(i => i.Id).ToList());
            RecycleBin.Send(files.Where(File.Exists).ToList());
            foreach (var id in work) Annotations.Delete(id);   // its file is in the Recycle Bin already
        }
        catch (Exception ex) { Dialogs.Error(ex.Message); }
        ReloadAll();
        return true;
    }

    /// <summary>The window is closing: unsaved tabs are asked about. False = stay open.</summary>
    public bool ConfirmClose() => Annotate.ConfirmClose();

    /// <summary>
    /// Settings / Dashboard: pick the NEURAINK Database folder. A folder that is not a complete database is completed
    /// (only the missing folders and files are created, like the Database folder of DEEPMATH).
    /// </summary>
    public bool ChooseDatabase()
    {
        if (Dialogs.PickFolder(Database.Exists ? Database.Root : null) is not { } folder) return false;
        var db = new NeurainkDatabase(folder);
        var missing = db.Missing();
        if (missing.Count > 0)
        {
            if (!Dialogs.Confirm(Loc.F("Db.Missing", string.Join("\n", missing)))) return false;
            try { db.CreateMissing(); }
            catch (Exception ex) { Dialogs.Error(ex.Message); return false; }
        }
        Settings.DatabasePath = db.Root;
        SaveSettings();
        ReloadAll();
        return true;
    }

    /// <summary>
    /// Re-makes every cut-out from the saved segmentations with the current engine (after a fix in the brush, the pressure
    /// or the export): the drawing work is kept, only the letter library is rebuilt.
    /// </summary>
    public int RebuildLetters()
    {
        int n = 0;
        foreach (var id in Annotations.Ids.ToList())
        {
            if (!Annotations.IsSegmented(id) || GetItem(id) is not { } item || Annotations.Load(id) is not { } ann) continue;
            n += LetterExporter.Export(item, ann, item.PathOf(item.Png), Letters, StyleLabelOf(item));
        }
        RefreshCounts();
        return n;
    }

    /// <summary>
    /// Deletes a cut-out for good: its regions / strokes leave the segmentation (so a rebuild does not bring it back)
    /// and the piece is exported again; the letter shows as not done in Letter Segmentation.
    /// </summary>
    public void DeleteLetter(LetterSampleInfo info)
    {
        if (info.Meta is not { } m) { try { File.Delete(info.Png); } catch { } return; }
        var ann = Annotations.Load(m.SourceId);
        bool rewritten = false;
        if (ann != null)
        {
            if (m.Form == "ornament" || m.Chip < 0)
            {
                var orders = (m.Strokes ?? new List<Segment>()).Select(x => x.Order).ToHashSet();
                ann.Segments.RemoveAll(x => x.Kind == "ornament" && orders.Contains(x.Order));
            }
            else
                ann.Segments.RemoveAll(x => x.Chip == m.Chip && x.Kind is "letter" or "dots" or "stroke" or "diacritic");
            ann.State = ann.Segments.Any(x => x.Kind is "letter" or "stroke") ? "partial" : "";
            if (ann.State == "") Annotations.Delete(ann.Id);
            else Annotations.Save(ann);
            if (GetItem(m.SourceId) is { } item)
            {
                if (ann.State == "") Letters.RemoveSource(item.Id);
                else LetterExporter.Export(item, ann, item.PathOf(item.Png), Letters, StyleLabelOf(item));
                rewritten = true;
            }
        }
        if (!rewritten)
            foreach (var ext in new[] { ".png", ".json", ".svg" })
                try { var f = Path.ChangeExtension(info.Png, ext); if (File.Exists(f)) File.Delete(f); } catch { }
        RefreshCounts();
    }

    public void SaveSettings()
    {
        try { SettingsStore.Save(Settings); } catch { }
    }

    /// <summary>Order of the work: single words first, most frequent texts first.</summary>
    public int? NextToSegment(int after, IReadOnlySet<int>? skip = null)
    {
        var order = Library.Items
            .OrderBy(i => i.Words != 1).ThenByDescending(i => i.Freq).ThenBy(i => i.Text, StringComparer.Ordinal).ThenBy(i => i.Id)
            .ToList();
        int start = after == 0 ? 0 : order.FindIndex(i => i.Id == after) + 1;
        for (int k = 0; k < order.Count; k++)
        {
            var it = order[(start + k) % order.Count];
            if (it.Id != after && Annotations.State(it.Id) == "" && skip?.Contains(it.Id) != true) return it.Id;
        }
        return null;
    }
}
