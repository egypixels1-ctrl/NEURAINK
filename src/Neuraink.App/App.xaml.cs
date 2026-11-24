using System.Windows;
using System.Windows.Threading;
using Neuraink.App.Services;
using Neuraink.App.ViewModels;
using Neuraink.Core;

namespace Neuraink.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;

        var settings = SettingsStore.Load();
        ThemeService.Accent = string.IsNullOrWhiteSpace(settings.Accent) ? "auto" : settings.Accent;
        ThemeService.Apply(settings.Theme);
        LanguageService.Apply(settings.Language);

        var window = new MainWindow();
        MainWindow = window;
        AccentService.Apply(ThemeService.Accent, ThemeService.Current == AppTheme.Light);
        window.FlowDirection = LanguageService.CurrentFlow;
        var vm = new MainViewModel(settings);
        window.DataContext = vm;
        window.Show();

        // --page <dashboard|library|annotate|annotate-demo|letters|settings|colorpicker>  (screenshot smoke test)
        var i = Array.IndexOf(e.Args, "--page");
        if (i >= 0 && i + 1 < e.Args.Length) ScreenshotPage(vm, window, e.Args[i + 1].ToLowerInvariant());
    }

    private void ScreenshotPage(MainViewModel vm, Window window, string arg)
    {
        switch (arg)
        {
            case "library": vm.Go(vm.LibraryPage); break;
            case "letters": vm.Go(vm.LettersPage); break;
            case "settings": vm.Go(vm.SettingsPage); break;
            case "annotate": vm.Go(vm.Annotate); break;
            case "annotate-demo":
                vm.Go(vm.Annotate);
                // outlines two letters and a join, like a user at work (nothing is saved)
                Dispatcher.BeginInvoke(() => DemoSegments(vm.Annotate), DispatcherPriority.ContextIdle);
                break;
            case "annotate-save":
                // the same, then Save: the letters are cut into the letter library (checks the exporter)
                vm.Go(vm.Annotate);
                Dispatcher.BeginInvoke(() => { DemoSegments(vm.Annotate); vm.Annotate.CancelDrawing(); vm.Annotate.Save(); }, DispatcherPriority.ContextIdle);
                break;
            case "colorpicker":
                vm.Go(vm.SettingsPage);
                Dispatcher.BeginInvoke(() => new Views.ColorPickerWindow(System.Windows.Media.Color.FromRgb(0x7C, 0x5C, 0xFF)) { Owner = window }.Show(),
                    DispatcherPriority.ContextIdle);
                break;
        }
    }

    private static void DemoSegments(AnnotateViewModel a)
    {
        if (a.Image is not { } img) return;
        double w = img.PixelWidth, h = img.PixelHeight;
        void Box(double x0, double y0, double x1, double y1)
        {
            a.Click(new Point(w * x0, h * y0), 1); a.Click(new Point(w * x1, h * y0), 1);
            a.Click(new Point(w * x1, h * y1), 1); a.Click(new Point(w * x0, h * y1), 1);
            a.CloseShape();
        }
        Box(0.70, 0.20, 0.95, 0.80);
        if (a.Tool == "entry") a.Click(new Point(w * 0.72, h * 0.6), 1);
        if (a.Tool == "exit") a.Click(new Point(w * 0.71, h * 0.62), 1);
        Box(0.45, 0.25, 0.70, 0.85);
        a.Click(new Point(w * 0.68, h * 0.62), 1);
        a.Click(new Point(w * 0.30, h * 0.30), 1);
        a.Click(new Point(w * 0.40, h * 0.30), 1);
        a.Click(new Point(w * 0.40, h * 0.50), 1);
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.Folder);
            File.AppendAllText(Path.Combine(SettingsStore.Folder, "error.log"), $"[{DateTime.Now:u}] {e.Exception}\n\n");
        }
        catch { }
        MessageBox.Show(e.Exception.Message, "NEURAINK", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
