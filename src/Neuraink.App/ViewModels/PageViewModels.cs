using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Neuraink.App.Services;
using Neuraink.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Neuraink.App.ViewModels;

// ====================================================================== Dashboard
public sealed class CoverageRow
{
    public required string Letter { get; init; }
    public int Count { get; init; }
    public double Percent => Math.Min(100, Count * 100.0 / LetterLibrary.Target);
    public string Text => $"{Count:N0} / {LetterLibrary.Target:N0}";
}

public sealed class RoadmapStep
{
    public int Number { get; init; }
    public required string TitleKey { get; init; }
    public required string SubKey { get; init; }
    public bool Done { get; init; }
    public bool Now { get; init; }
    public string Title => Loc.T(TitleKey);
    public string Sub => Loc.T(SubKey);
    public string Mark => Done ? "" : "";
}

public sealed partial class DashboardViewModel(MainViewModel main) : ObservableObject
{
    [ObservableProperty] private string _usable = "0";
    [ObservableProperty] private string _texts = "0";
    [ObservableProperty] private string _segmented = "0";
    [ObservableProperty] private string _pieces = "0";
    [ObservableProperty] private string _hallOfFame = "0";
    [ObservableProperty] private bool _libraryMissing;
    [ObservableProperty] private bool _databaseMissing;
    [ObservableProperty] private bool _noLetters = true;
    public ObservableCollection<CoverageRow> Coverage { get; } = [];
    public ObservableCollection<RoadmapStep> Roadmap { get; } = [];
    public string Subtitle => Loc.F("Dash.Sub", main.Settings.Style);

    public void Refresh()
    {
        var lib = main.Library;
        DatabaseMissing = !main.Database.Exists;
        LibraryMissing = main.Database.Exists && !lib.Found;
        Usable = lib.Items.Count.ToString("N0");
        Texts = lib.Items.Select(i => i.Text).Distinct().Count().ToString("N0");
        Segmented = main.Annotations.CountSegmented.ToString("N0");
        HallOfFame = lib.Items.Count(i => i.Tier.Length > 0).ToString("N0");
        var groups = main.Letters.Groups();
        Pieces = groups.Sum(g => g.Count).ToString("N0");
        Coverage.Clear();
        foreach (var g in groups.Take(14)) Coverage.Add(new CoverageRow { Letter = g.Letter, Count = g.Count });
        NoLetters = Coverage.Count == 0;
        Roadmap.Clear();
        for (int i = 1; i <= 5; i++)
            Roadmap.Add(new RoadmapStep { Number = i, TitleKey = $"Road.{i}", SubKey = $"Road.{i}s", Done = i < 2, Now = i == 2 });
        OnPropertyChanged(nameof(Subtitle));
    }

    [RelayCommand] private void Continue() => main.Go(main.Annotate);

    [RelayCommand] private void ChooseDatabase() => main.ChooseDatabase();
}

// ====================================================================== Library
public sealed partial class LibraryTile : ObservableObject
{
    public required LibraryItem Item { get; init; }
    public required string State { get; init; }
    public string Text => Item.Text;
    public bool IsBest => Item.Tier.Length > 0;
    public bool IsArtboard => Item.IsArtboard;
    public string Badge => State switch
    {
        "done" => Loc.T("State.Done"),
        "partial" => Loc.T("State.Partial"),
        "rejected" => Loc.T("State.Rejected"),
        _ => Item.IsArtboard ? Loc.T("Board.Badge") : "×" + Item.Freq.ToString("N0"),
    };
    public bool IsDone => State is "done" or "partial";
    public bool IsRejected => State == "rejected";
    [ObservableProperty] private BitmapSource? _image;
}

public sealed partial class LibraryViewModel : ObservableObject
{
    private const int PageSize = 120;
    private readonly MainViewModel _main;
    private List<LibraryItem> _matches = [];
    private CancellationTokenSource? _loading;

    public ObservableCollection<LibraryTile> Tiles { get; } = [];
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _filter = "all";        // all | todo | done | rejected
    [ObservableProperty] private bool _singleWord;
    [ObservableProperty] private int _sortIndex;                 // 0 frequent, 1 best, 2 file order
    [ObservableProperty] private string _countText = "";
    [ObservableProperty] private bool _hasMore;
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private bool _libraryMissing;

    public LibraryViewModel(MainViewModel main)
    {
        _main = main;
        Reload();
    }

    partial void OnSearchChanged(string value) => Reload();
    partial void OnFilterChanged(string value) => Reload();
    partial void OnSingleWordChanged(bool value) => Reload();
    partial void OnSortIndexChanged(int value) => Reload();

    public void Reload()
    {
        var lib = _main.Library;
        LibraryMissing = !lib.Found;
        var q = (Search ?? "").Trim();
        // the artboards (handwritten pieces) first, then the library
        IEnumerable<LibraryItem> items = _main.Artboards.All().Select(_main.Artboards.ToItem).Concat(lib.Items);
        if (q.Length > 0) items = items.Where(i => i.Text.Contains(q, StringComparison.Ordinal) || i.File.Contains(q, StringComparison.OrdinalIgnoreCase));
        if (SingleWord) items = items.Where(i => i.Words == 1);
        items = Filter switch
        {
            "todo" => items.Where(i => _main.Annotations.State(i.Id) == ""),
            "done" => items.Where(i => _main.Annotations.IsSegmented(i.Id)),
            "rejected" => items.Where(i => _main.Annotations.State(i.Id) == "rejected"),
            _ => items,
        };
        items = SortIndex switch
        {
            1 => items.OrderBy(i => i.Tier.Length == 0).ThenByDescending(i => i.Freq).ThenBy(i => i.Id),
            2 => items.OrderBy(i => i.Id),
            _ => items.OrderBy(i => !i.IsArtboard).ThenByDescending(i => i.Freq).ThenBy(i => i.Text, StringComparer.Ordinal).ThenBy(i => i.Id),
        };
        _matches = items.ToList();
        CountText = Loc.F("Lib.Count", _matches.Count.ToString("N0"));
        IsEmpty = lib.Found && _matches.Count == 0;
        Tiles.Clear();
        _loading?.Cancel();
        _loading = new CancellationTokenSource();
        AddPage(_loading.Token);
    }

    [RelayCommand]
    private void LoadMore() => AddPage(_loading?.Token ?? CancellationToken.None);

    private void AddPage(CancellationToken ct)
    {
        var page = _matches.Skip(Tiles.Count).Take(PageSize).ToList();
        var lib = _main.Library;
        foreach (var it in page)
        {
            var tile = new LibraryTile { Item = it, State = _main.Annotations.State(it.Id) };
            Tiles.Add(tile);
            var path = lib.FullPath(it.Png);
            _ = Task.Run(() =>
            {
                if (ct.IsCancellationRequested) return;
                var img = Images.Load(path, 220);
                if (img != null && !ct.IsCancellationRequested)
                    Application.Current?.Dispatcher.BeginInvoke(() => tile.Image = img);
            }, ct);
        }
        HasMore = Tiles.Count < _matches.Count;
    }

    [RelayCommand] private void Open(LibraryTile? tile) { if (tile != null) _main.OpenAnnotate(tile.Item.Id); }
    [RelayCommand] private void SetFilter(string? f) => Filter = f ?? "all";
}

// ====================================================================== Letters
public sealed partial class LetterCard : ObservableObject
{
    public required LetterGroup Group { get; init; }
    public string Letter => Group.Letter;
    public string Count => Group.Count.ToString("N0");
    public double Percent => Math.Min(100, Group.Count * 100.0 / LetterLibrary.Target);
    public string TestText => Group.TestCount > 0 ? Loc.F("Let.TestCount", Group.TestCount.ToString("N0")) : "";
    public string Forms => string.Join("  ·  ", Group.Forms.OrderByDescending(f => f.Value).Select(f => $"{Loc.T("Form." + (f.Key.Length > 0 ? f.Key : "isolated"))} {f.Value:N0}"));
    public ObservableCollection<BitmapSource> Samples { get; } = [];
}

/// <summary>A cut-out in the letter's page: its picture, where it came from, and its set (train / test).</summary>
public sealed partial class LetterSampleVm : ObservableObject
{
    public required LetterSampleInfo Info { get; init; }
    [ObservableProperty] private BitmapSource? _image;
    public bool IsTest => Info.Meta?.Split == "test";
    public bool IsBrush => Info.Meta?.SourceKind == "brush";
    public string Caption => Info.Meta is { } m ? $"#{m.SourceId}  ·  {Loc.T("Form." + (m.Form.Length > 0 ? m.Form : "isolated"))}" : "";
    public string Tip => Info.Meta is { } m ? $"{m.SourceText}\n{m.SourceFile}\nv{m.Engine} · schema {m.Schema} · {m.SourceKind} · {m.Split}" : Info.Png;
}

public sealed partial class LettersViewModel(MainViewModel main) : ObservableObject
{
    public ObservableCollection<LetterCard> Cards { get; } = [];
    public ObservableCollection<LetterSampleVm> Detail { get; } = [];
    [ObservableProperty] private LetterCard? _selected;
    [ObservableProperty] private bool _isEmpty = true;
    [ObservableProperty] private string _outdatedText = "";
    [ObservableProperty] private bool _hasOutdated;

    public void Refresh()
    {
        var keep = Selected?.Letter;
        Cards.Clear();
        Selected = null;
        foreach (var g in main.Letters.Groups())
        {
            var card = new LetterCard { Group = g };
            Cards.Add(card);
            var paths = g.Samples.Take(6).ToList();
            _ = Task.Run(() =>
            {
                var imgs = paths.Select(p => Images.Load(p, 120)).Where(i => i != null).ToList();
                Application.Current?.Dispatcher.BeginInvoke(() => { foreach (var i in imgs) card.Samples.Add(i!); });
            });
        }
        IsEmpty = Cards.Count == 0;
        int old = main.Letters.CountOutdated(DataVersion.Engine);
        HasOutdated = old > 0;
        OutdatedText = Loc.F("Let.Outdated", old.ToString("N0"));
        if (keep != null) Selected = Cards.FirstOrDefault(c => c.Letter == keep);
    }

    partial void OnSelectedChanged(LetterCard? value)
    {
        Detail.Clear();
        if (value == null) return;
        foreach (var p in value.Group.Samples.Take(400))
        {
            var vm = new LetterSampleVm { Info = LetterLibrary.Info(p) };
            Detail.Add(vm);
            _ = Task.Run(() =>
            {
                var img = Images.Load(p, 200);
                if (img != null) Application.Current?.Dispatcher.BeginInvoke(() => vm.Image = img);
            });
        }
    }

    [RelayCommand] private void Show(LetterCard? card) => Selected = card;
    [RelayCommand] private void Back() => Selected = null;
    [RelayCommand] private void Start() => main.Go(main.Annotate);

    /// <summary>Opens the piece of this cut-out in Letter Segmentation (to redo the letter).</summary>
    [RelayCommand] private void OpenSource(LetterSampleVm? s) { if (s?.Info.Meta is { } m) main.OpenAnnotate(m.SourceId); }

    [RelayCommand]
    private void Delete(LetterSampleVm? s)
    {
        if (s == null || !Dialogs.Confirm(Loc.T("Let.DeleteAsk"))) return;
        main.DeleteLetter(s.Info);
        Refresh();
    }

    [RelayCommand]
    private void Rebuild()
    {
        if (!Dialogs.Confirm(Loc.T("Let.RebuildAsk"))) return;
        var n = main.RebuildLetters();
        Refresh();
        Dialogs.Info(Loc.F("Let.Rebuilt", n.ToString("N0")));
    }
}

// ====================================================================== Settings
/// <summary>A colour square of the accent palette (Settings → Appearance).</summary>
public sealed partial class AccentSwatch : ObservableObject
{
    public required string Hex { get; init; }
    public Brush Brush => new SolidColorBrush((Color)ColorConverter.ConvertFromString(Hex));
    [ObservableProperty] private bool _isSelected;
}

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private bool _loading;

    /// <summary>The NEURAINK colours first, then the Windows accent palette.</summary>
    public static readonly string[] BrandColors = ["#7C5CFF", "#5B8CFF", "#00B7C3", "#10B981", "#C9A227", "#F59E0B", "#F43F5E", "#E3008C"];

    public List<AccentSwatch> AccentPalette { get; } =
        BrandColors.Concat(AccentService.Palette).Distinct(StringComparer.OrdinalIgnoreCase).Select(h => new AccentSwatch { Hex = h }).ToList();
    public ObservableCollection<AccentSwatch> RecentAccents { get; } = [];
    public bool HasRecentAccents => RecentAccents.Count > 0;

    [ObservableProperty] private bool _isArabic;
    [ObservableProperty] private bool _isLight;
    [ObservableProperty] private bool _useWindowsAccent;
    [ObservableProperty] private bool _appearanceOpen;
    [ObservableProperty] private string _databasePath = "";
    [ObservableProperty] private bool _databaseOk;
    [ObservableProperty] private bool _databaseIncomplete;
    [ObservableProperty] private string _databaseStatus = "";
    [ObservableProperty] private string _libraryStatus = "";
    [ObservableProperty] private string _style = "";
    [ObservableProperty] private bool _showOriginal;
    public string AccentHex { get; private set; } = "";
    public string Version => _main.VersionText;

    public SettingsViewModel(MainViewModel main)
    {
        _main = main;
        _loading = true;
        var s = main.Settings;
        IsArabic = s.Language == AppLanguage.Arabic;
        IsLight = s.Theme == AppTheme.Light;
        AppearanceOpen = s.AppearanceOpen;
        RefreshDatabase();
        Style = s.Style;
        ShowOriginal = s.ShowOriginal;
        LoadAccent();
        _loading = false;
    }

    private void LoadAccent()
    {
        var a = _main.Settings.Accent ?? "auto";
        _loading = true;
        UseWindowsAccent = a.Equals("auto", StringComparison.OrdinalIgnoreCase);
        _loading = false;
        AccentHex = UseWindowsAccent ? "" : a.ToUpperInvariant();
        RecentAccents.Clear();
        foreach (var h in _main.Settings.RecentAccents.Take(6)) RecentAccents.Add(new AccentSwatch { Hex = h });
        OnPropertyChanged(nameof(HasRecentAccents));
        foreach (var sw in AccentPalette.Concat(RecentAccents))
            sw.IsSelected = !UseWindowsAccent && string.Equals(sw.Hex, AccentHex, StringComparison.OrdinalIgnoreCase);
    }

    private void SetAccent(string accent)
    {
        _main.Settings.Accent = accent;
        if (!accent.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            var list = _main.Settings.RecentAccents;
            list.RemoveAll(x => string.Equals(x, accent, StringComparison.OrdinalIgnoreCase));
            list.Insert(0, accent.ToUpperInvariant());
            if (list.Count > 6) list.RemoveRange(6, list.Count - 6);
        }
        _main.SaveSettings();
        ThemeService.ApplyAccent(accent);
        LoadAccent();
    }

    partial void OnUseWindowsAccentChanged(bool value)
    {
        if (_loading) return;
        SetAccent(value ? "auto" : (_main.Settings.RecentAccents.FirstOrDefault() ?? BrandColors[0]));
    }

    [RelayCommand] private void PickAccent(AccentSwatch? swatch) { if (swatch != null) SetAccent(swatch.Hex); }

    [RelayCommand]
    private void CustomAccent()
    {
        Color? initial = null;
        try { if (AccentHex.Length > 0) initial = (Color)ColorConverter.ConvertFromString(AccentHex); } catch { }
        if (ColorPicker.Pick(initial) is { } c) SetAccent($"#{c.R:X2}{c.G:X2}{c.B:X2}");
    }

    [RelayCommand]
    private void ToggleAppearance()
    {
        AppearanceOpen = !AppearanceOpen;
        _main.Settings.AppearanceOpen = AppearanceOpen;
        _main.SaveSettings();
    }

    partial void OnIsArabicChanged(bool value)
    {
        if (_loading) return;
        _main.Settings.Language = value ? AppLanguage.Arabic : AppLanguage.English;
        LanguageService.Apply(_main.Settings.Language);
        _main.SaveSettings();
        _main.Dashboard.Refresh();
        _main.LibraryPage.Reload();
    }

    partial void OnIsLightChanged(bool value)
    {
        if (_loading) return;
        _main.Settings.Theme = value ? AppTheme.Light : AppTheme.Dark;
        ThemeService.Apply(_main.Settings.Theme);
        _main.SaveSettings();
    }

    partial void OnShowOriginalChanged(bool value)
    {
        if (_loading) return;
        _main.Settings.ShowOriginal = value;
        _main.SaveSettings();
    }

    /// <summary>Path, completeness and library of the database (shown in Settings › Database).</summary>
    public void RefreshDatabase()
    {
        var db = _main.Database;
        DatabasePath = db.IsSet ? db.Root : Loc.T("Db.NotSet");
        DatabaseOk = db.IsComplete;
        DatabaseIncomplete = db.Exists && !db.IsComplete;
        DatabaseStatus = !db.IsSet ? Loc.T("Db.NotSet") : !db.Exists ? Loc.T("Db.NotFound")
            : db.IsComplete ? Loc.T("Db.Ok") : Loc.F("Db.Incomplete", string.Join("  ", db.Missing()));
        var lib = _main.Library;
        LibraryStatus = lib.Found ? Loc.F("Db.LibraryAt", lib.Items.Count.ToString("N0"), lib.Folder) : Loc.T("Db.NoLibrary");
    }

    [RelayCommand]
    private void ChangeDatabase()
    {
        _main.ChooseDatabase();
        RefreshDatabase();
    }

    [RelayCommand]
    private void CreateMissing()
    {
        try { _main.Database.CreateMissing(); }
        catch (Exception ex) { Dialogs.Error(ex.Message); }
        _main.ReloadAll();
        RefreshDatabase();
    }

    [RelayCommand] private void OpenDatabase() { if (_main.Database.Exists) Shell.ShowInFolder(_main.Database.Root); }
    [RelayCommand] private void OpenLibrary() { if (_main.Library.Found) Shell.ShowInFolder(_main.Library.Folder); }

    [RelayCommand]
    private void SaveStyle()
    {
        _main.Settings.Style = (Style ?? "").Trim();
        _main.SaveSettings();
        _main.ReloadAll();
        RefreshDatabase();
    }
}
