using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Neuraink.Core;

namespace Neuraink.App.Services;

/// <summary>Localized strings from the active Strings.xx.xaml dictionary.</summary>
public static class Loc
{
    public static string T(string key)
    {
        var app = Application.Current;
        if (app == null) return key;
        if (!app.Dispatcher.CheckAccess()) return app.Dispatcher.Invoke(() => T(key));   // from a background task
        return app.TryFindResource(key) as string ?? key;
    }
    public static string F(string key, params object[] args) => string.Format(T(key), args);
}

public static class LanguageService
{
    public static event Action? Changed;

    public static void Apply(AppLanguage lang)
    {
        var app = Application.Current;
        var dicts = app.Resources.MergedDictionaries;
        var old = dicts.FirstOrDefault(d => d.Source?.OriginalString.Contains("Localization/Strings.") == true);
        var file = lang == AppLanguage.Arabic ? "Strings.ar.xaml" : "Strings.en.xaml";
        var fresh = new ResourceDictionary { Source = new Uri($"pack://application:,,,/NEURAINK;component/Localization/{file}") };
        if (old != null) dicts[dicts.IndexOf(old)] = fresh; else dicts.Add(fresh);
        var dir = lang == AppLanguage.Arabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        foreach (Window w in app.Windows) w.FlowDirection = dir;
        CurrentFlow = dir;
        Current = lang;
        Changed?.Invoke();
    }

    public static AppLanguage Current { get; private set; } = AppLanguage.Arabic;
    public static FlowDirection CurrentFlow { get; private set; } = FlowDirection.RightToLeft;
}

public static class ThemeService
{
    /// <summary>Accent colour re-applied after every theme change ("auto" or #RRGGBB).</summary>
    public static string Accent { get; set; } = "auto";
    public static AppTheme Current { get; private set; } = AppTheme.Dark;
    public static event Action? Changed;

    public static void Apply(AppTheme theme)
    {
#pragma warning disable WPF0001
        Application.Current.ThemeMode = theme == AppTheme.Light ? ThemeMode.Light : ThemeMode.Dark;
#pragma warning restore WPF0001
        Current = theme;
        AccentService.Apply(Accent, theme == AppTheme.Light);
        Changed?.Invoke();
    }

    /// <summary>
    /// Changes the accent while the program runs. The Fluent theme creates its control brushes once from the accent,
    /// so its dictionaries are reloaded after the new accent is set - every open control then picks up the new colour.
    /// </summary>
    public static void ApplyAccent(string accent)
    {
        Accent = accent;
        bool light = Current == AppTheme.Light;
        AccentService.Apply(accent, light);
        var app = Application.Current;
        if (app == null) return;
        int reloaded = ReloadFluent(app.Resources);
        foreach (Window w in app.Windows) reloaded += ReloadFluent(w.Resources);
        if (reloaded == 0)
        {
#pragma warning disable WPF0001
            app.ThemeMode = light ? ThemeMode.Dark : ThemeMode.Light;
            app.ThemeMode = light ? ThemeMode.Light : ThemeMode.Dark;
#pragma warning restore WPF0001
        }
        AccentService.Apply(accent, light);
        Changed?.Invoke();
    }

    private static int ReloadFluent(ResourceDictionary res)
    {
        int n = 0;
        var dicts = res.MergedDictionaries;
        for (int i = 0; i < dicts.Count; i++)
        {
            var src = dicts[i].Source;
            if (src == null || !src.OriginalString.Contains("Fluent", StringComparison.OrdinalIgnoreCase)) continue;
            dicts[i] = new ResourceDictionary { Source = src };
            n++;
        }
        return n;
    }

    /// <summary>The accent colour in use (for drawings that need a Color, not a brush).</summary>
    public static Color AccentColor =>
        Application.Current?.TryFindResource("BrandBrush") is SolidColorBrush b ? b.Color : Color.FromRgb(0x7C, 0x5C, 0xFF);
}

public static class Shell
{
    public static void Open(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { Dialogs.Error(ex.Message); }
    }

    public static void ShowInFolder(string path)
    {
        try
        {
            if (File.Exists(path)) Process.Start("explorer.exe", $"/select,\"{path}\"");
            else if (Directory.Exists(path)) Process.Start("explorer.exe", $"\"{path}\"");
        }
        catch (Exception ex) { Dialogs.Error(ex.Message); }
    }
}

public static class Dialogs
{
    private static Window? Owner => Application.Current?.MainWindow;

    public static void Info(string msg) =>
        MessageBox.Show(Owner!, msg, "NEURAINK", MessageBoxButton.OK, MessageBoxImage.Information);

    public static void Error(string msg) =>
        MessageBox.Show(Owner!, msg, "NEURAINK", MessageBoxButton.OK, MessageBoxImage.Warning);

    public static bool Confirm(string msg) =>
        MessageBox.Show(Owner!, msg, "NEURAINK", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public static string? PickFolder(string? initial = null)
    {
        var d = new Microsoft.Win32.OpenFolderDialog();
        if (!string.IsNullOrEmpty(initial) && Directory.Exists(initial)) d.InitialDirectory = initial;
        return d.ShowDialog(Owner) == true ? d.FolderName : null;
    }
}

/// <summary>Pictures loaded once, decoded at the size they are shown, frozen (usable from any thread).</summary>
public static class Images
{
    public static BitmapSource? Load(string path, int decodeWidth = 0)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            using var fs = File.OpenRead(path);
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            if (decodeWidth > 0) bi.DecodePixelWidth = decodeWidth;
            bi.StreamSource = fs;
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch { return null; }
    }

    public static Task<BitmapSource?> LoadAsync(string path, int decodeWidth = 0) => Task.Run(() => Load(path, decodeWidth));
}

/// <summary>The program's colour picker window.</summary>
public static class ColorPicker
{
    public static Color? Pick(Color? initial)
    {
        var w = new Views.ColorPickerWindow(initial);
        if (Application.Current?.MainWindow is { IsLoaded: true } owner && owner != w) w.Owner = owner;
        else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return w.ShowDialog() == true ? w.Selected : null;
    }
}
