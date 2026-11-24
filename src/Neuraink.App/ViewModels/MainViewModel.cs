using System.Collections.ObjectModel;
using Neuraink.App.Services;
using Neuraink.Core;
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

    public DashboardViewModel Dashboard { get; }
    public LibraryViewModel LibraryPage { get; }
    public AnnotateViewModel Annotate { get; }
    public LettersViewModel LettersPage { get; }
    public SettingsViewModel SettingsPage { get; }

    public ObservableCollection<NavItem> Nav { get; } = [];
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

        Nav.Add(new NavItem { Key = "Nav.Dashboard", Glyph = "\uE9D2", Page = () => Dashboard });
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
        if (page == Annotate && Annotate.Item == null) Annotate.OpenNext(0);
        CurrentPage = page;
    }

    public void Go(object page)
    {
        var nav = Nav.FirstOrDefault(n => n.Page() == page);
        if (nav != null) SelectedNav = nav;
    }

    public void OpenAnnotate(int id)
    {
        Annotate.Open(id, remember: true);
        Go(Annotate);
    }

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

    public void SaveSettings()
    {
        try { SettingsStore.Save(Settings); } catch { }
    }

    /// <summary>Order of the work: single words first, most frequent texts first.</summary>
    public int? NextToSegment(int after)
    {
        var order = Library.Items
            .OrderBy(i => i.Words != 1).ThenByDescending(i => i.Freq).ThenBy(i => i.Text, StringComparer.Ordinal).ThenBy(i => i.Id)
            .ToList();
        int start = after == 0 ? 0 : order.FindIndex(i => i.Id == after) + 1;
        for (int k = 0; k < order.Count; k++)
        {
            var it = order[(start + k) % order.Count];
            if (it.Id != after && Annotations.State(it.Id) == "") return it.Id;
        }
        return null;
    }
}
