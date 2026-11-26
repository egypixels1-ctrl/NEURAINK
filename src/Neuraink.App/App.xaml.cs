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

        var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        DataVersion.Engine = $"{v?.Major}.{v?.Minor}.{v?.Build}";
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
            case "letters-detail":
                vm.Go(vm.LettersPage);
                Dispatcher.BeginInvoke(() => vm.LettersPage.Selected = vm.LettersPage.Cards.FirstOrDefault(), DispatcherPriority.ContextIdle);
                break;
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
            case "annotate-brush":
                // the brush: two letters traced with a 70° nib, the picture faded to 45 % (nothing is saved)
                vm.Go(vm.Annotate);
                Dispatcher.BeginInvoke(() => DemoBrush(vm.Annotate), DispatcherPriority.ContextIdle);
                break;
            case "artboard":
                // a new artboard «محمد» with a faded reference picture and two strokes fitted into smooth curves
                Dispatcher.BeginInvoke(() =>
                {
                    if (vm.NewArtboard("محمد", 1200, 1200) is not { } board) return;
                    vm.OpenAnnotate(board.Id);
                    var refPic = vm.Library.Items.Select(i => vm.FullPath(i.Png)).FirstOrDefault(File.Exists);
                    if (refPic != null) vm.Annotate.SetReference(refPic);
                    vm.Annotate.AdjustReference = false;
                    DemoBrush(vm.Annotate);
                }, DispatcherPriority.ContextIdle);
                break;
            case "tabs":
                // three pieces in tabs (Illustrator style), the one in front with unsaved strokes
                vm.Go(vm.Annotate);
                Dispatcher.BeginInvoke(() =>
                {
                    foreach (var it in vm.Library.Items.Skip(1).Take(2)) vm.Annotate.OpenInNewTab(it.Id);
                    DemoBrush(vm.Annotate);
                }, DispatcherPriority.ContextIdle);
                break;
            case "annotate-edit":
                // a stroke selected for editing (Ctrl + click): its anchors and handles
                vm.Go(vm.Annotate);
                Dispatcher.BeginInvoke(() =>
                {
                    DemoBrush(vm.Annotate);
                    vm.Annotate.SelectStroke(vm.Annotate.Ann.Segments.FirstOrDefault(x => x.Kind == "stroke"));
                }, DispatcherPriority.ContextIdle);
                break;
            case "select-tool":
                // the Selection tool: a stroke picked, its box with eight handles
                vm.Go(vm.Annotate);
                Dispatcher.BeginInvoke(() =>
                {
                    DemoBrush(vm.Annotate);
                    vm.Annotate.UseSelect();
                    if (vm.Annotate.Ann.Segments.FirstOrDefault(x => x.Kind == "stroke") is { } st)
                    {
                        var b = AnnotateViewModel.ShapeOf(st).Bounds;
                        vm.Annotate.SelectAt(new Point(b.X + b.Width / 2, b.Y + b.Height / 2), 40, false);
                        if (!vm.Annotate.HasSelection) vm.Annotate.SelectIn(b, false);
                    }
                }, DispatcherPriority.ContextIdle);
                break;
            case "curvature":
                // the Curvature tool: a stroke's points, and a new stroke built point by point
                vm.Go(vm.Annotate);
                Dispatcher.BeginInvoke(() =>
                {
                    DemoBrush(vm.Annotate);
                    var a = vm.Annotate;
                    if (a.Image is not { } img) return;
                    double w = img.PixelWidth, h = img.PixelHeight;
                    a.UseCurvature();
                    a.AddCurvPoint(new Point(w * 0.20, h * 0.80), false);
                    a.AddCurvPoint(new Point(w * 0.35, h * 0.62), false);
                    a.AddCurvPoint(new Point(w * 0.50, h * 0.85), true);
                    a.AddCurvPoint(new Point(w * 0.66, h * 0.70), false);
                    a.FinishCurvDraft();
                    a.CurvPoint = 1;
                }, DispatcherPriority.ContextIdle);
                break;
            case "compact":
                // the menu folded to its icons and the side panel folded: the picture takes the room
                vm.NavCollapsed = true;
                vm.Settings.SidePanelCollapsed = true;
                vm.Go(vm.Annotate);
                Dispatcher.BeginInvoke(() => DemoBrush(vm.Annotate), DispatcherPriority.ContextIdle);
                break;
            case "library-add":
                // a picture added to the library (cleaned, traced to SVG, thumbnail, report row), then two pieces ticked
                Dispatcher.BeginInvoke(async () =>
                {
                    var src = vm.Library.Items.Select(i => vm.FullPath(i.Png)).FirstOrDefault(File.Exists);
                    if (src == null) return;
                    var dir = Path.Combine(Path.GetTempPath(), "neuraink-add");
                    Directory.CreateDirectory(dir);
                    var pic = Path.Combine(dir, "سامي (2).png");
                    File.Copy(src, pic, true);
                    vm.Go(vm.LibraryPage);
                    await vm.LibraryPage.AddFiles([pic]);
                    foreach (var t in vm.LibraryPage.Tiles.Skip(1).Take(2)) t.IsSelected = true;
                }, DispatcherPriority.ContextIdle);
                break;
            case "brush-options":
                vm.Go(vm.Annotate);
                Dispatcher.BeginInvoke(() =>
                {
                    vm.Annotate.SetToolByKey(6);
                    new Views.BrushOptionsWindow(vm.Annotate.Tip.Copy(), false) { Owner = window }.Show();
                }, DispatcherPriority.ContextIdle);
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

    private static void DemoBrush(AnnotateViewModel a)
    {
        if (a.Image is not { } img) return;
        double w = img.PixelWidth, h = img.PixelHeight;
        a.SetToolByKey(6);
        a.PictureOpacity = 45;
        a.BrushSize = Math.Max(8, w / 22);
        void Stroke(params (double x, double y)[] pts)
        {
            a.BeginStroke(new Point(w * pts[0].x, h * pts[0].y), 0);
            for (int i = 1; i < pts.Length; i++)
                for (int k = 1; k <= 12; k++)
                {
                    double t = k / 12.0;
                    var x = pts[i - 1].x + (pts[i].x - pts[i - 1].x) * t + Math.Sin(k * 2.1 + i) * 0.004;
                    var y = pts[i - 1].y + (pts[i].y - pts[i - 1].y) * t + Math.Cos(k * 1.7 + i) * 0.004;
                    a.ExtendStroke(new Point(w * x, h * y), 0, 0.5);
                }
            a.EndStroke(stay: false, zoom: 0.5);
        }
        Stroke((0.80, 0.30), (0.88, 0.45), (0.82, 0.62), (0.72, 0.70));
        a.StepLetter(1);   // a right click: the next letter
        Stroke((0.62, 0.35), (0.58, 0.55), (0.50, 0.68), (0.40, 0.70));
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
