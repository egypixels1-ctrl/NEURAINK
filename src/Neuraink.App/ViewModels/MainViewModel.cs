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
        return Library.Get(id);
    }

    /// <summary>Full path of a piece's file (library paths are relative; artboard paths are already full).</summary>
    public string FullPath(string path) => Library.FullPath(path);

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
        Nav.Add(new NavItem { Key = "Nav.Annotate", Glyph = "\uE70F", Page = () => Annotate });
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
    /// <summary>
    /// Adds pictures to the library of the current style: each is cleaned to pure black on white, saved in clean_png\,
    /// traced to svg\ and given a thumbnail and a row in report.csv. The text comes from the file name.
    /// </summary>
    public async Task<int> AddPieces(IReadOnlyList<string> files, IProgress<int>? progress = null)
    {
        if (!Database.Exists) { Dialogs.Error(Loc.T("Db.NotSet")); return 0; }
        var folder = Library.Found ? Library.Folder : Path.Combine(Database.LibraryRoot, Settings.Style);
        var used = Annotations.Ids.ToList();
        int added = 0;
        var failed = new List<string>();
        foreach (var f in files)
        {
            try
            {
                var r = await Task.Run(() => PieceImporter.Import(f, folder, used));
                used.Add(r);
                added++;
            }
            catch (Exception ex) { failed.Add($"{Path.GetFileName(f)} — {ex.Message}"); }
            progress?.Report(added + failed.Count);
        }
        ReloadAll();
        if (failed.Count > 0) Dialogs.Error(Loc.F("Lib.AddFailed", string.Join("\n", failed)));
        return added;
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
                    if (!string.IsNullOrWhiteSpace(rel)) files.Add(FullPath(rel));
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
            if (rows.Count > 0) LibraryEditor.RemoveRows(Library.Folder, rows);
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
            n += LetterExporter.Export(item, ann, FullPath(item.Png), Letters, Settings.Style);
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
                else LetterExporter.Export(item, ann, FullPath(item.Png), Letters, Settings.Style);
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
