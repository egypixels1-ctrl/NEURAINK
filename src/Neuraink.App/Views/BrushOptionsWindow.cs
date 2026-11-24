using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Neuraink.App.Services;
using Neuraink.App.ViewModels;
using Neuraink.Core;
using Shapes = System.Windows.Shapes;

namespace Neuraink.App.Views;

/// <summary>
/// Calligraphic Brush Options (like Illustrator): name, size, angle, roundness, with a live preview of the nib and a stroke.
/// </summary>
public sealed class BrushOptionsWindow : Window
{
    private readonly BrushTip _tip;
    private readonly TextBox _name = new() { Padding = new Thickness(8, 5, 8, 5) };
    private readonly Canvas _preview = new() { Width = 300, Height = 120, ClipToBounds = true };
    public bool Deleted { get; private set; }

    public BrushOptionsWindow(BrushTip tip, bool isNew)
    {
        _tip = tip;
        Title = Loc.T(isNew ? "Brushes.New" : "Brushes.Options");
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FlowDirection = LanguageService.CurrentFlow;
        SetResourceReference(BackgroundProperty, "SolidBackgroundFillColorBaseBrush");

        var root = new StackPanel { Margin = new Thickness(20), Width = 340 };
        root.Children.Add(Label("Brushes.Name"));
        _name.Text = tip.Name;
        root.Children.Add(_name);

        // preview: white paper, the nib, and an S stroke drawn with it
        var paper = new Border
        {
            Background = Brushes.White, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 14, 0, 6),
            Child = _preview, FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Center,
        };
        root.Children.Add(paper);

        root.Children.Add(Row("Brush.Size", 2, 160, tip.Size, v => tip.Size = v, "0"));
        root.Children.Add(Row("Brush.Angle", 0, 180, tip.Angle, v => tip.Angle = v, "0°"));
        root.Children.Add(Row("Brush.Roundness", 2, 100, tip.Roundness * 100, v => tip.Roundness = v / 100, "0'%'"));
        root.Children.Add(Row("Brush.Opacity", 5, 100, tip.Opacity * 100, v => tip.Opacity = v / 100, "0'%'"));

        var buttons = new DockPanel { Margin = new Thickness(0, 18, 0, 0), LastChildFill = false };
        var ok = new Button { Content = Loc.T("Common.OK"), MinWidth = 96, Padding = new Thickness(0, 7, 0, 7), IsDefault = true };
        ok.SetResourceReference(StyleProperty, "AccentButtonStyle");
        ok.Click += (_, _) => { _tip.Name = string.IsNullOrWhiteSpace(_name.Text) ? _tip.Name : _name.Text.Trim(); DialogResult = true; };
        var cancel = new Button { Content = Loc.T("Common.Cancel"), MinWidth = 96, Padding = new Thickness(0, 7, 0, 7), IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
        DockPanel.SetDock(ok, Dock.Right); DockPanel.SetDock(cancel, Dock.Right);
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        if (!isNew)
        {
            var del = new Button { Content = Loc.T("Brushes.Delete"), Padding = new Thickness(12, 7, 12, 7) };
            del.Click += (_, _) => { Deleted = true; DialogResult = true; };
            DockPanel.SetDock(del, Dock.Left);
            buttons.Children.Add(del);
        }
        root.Children.Add(buttons);
        Content = root;
        Loaded += (_, _) =>
        {
            AccentService.Apply(ThemeService.Accent, ThemeService.Current == AppTheme.Light);
            _name.Focus();
            _name.SelectAll();
            Paint();
        };
    }

    private TextBlock Label(string key) => new() { Text = Loc.T(key), Margin = new Thickness(0, 0, 0, 4) };

    private Grid Row(string key, double min, double max, double value, Action<double> set, string format)
    {
        var g = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
        var label = new TextBlock { Text = Loc.T(key), VerticalAlignment = VerticalAlignment.Center };
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, VerticalAlignment = VerticalAlignment.Center, IsMoveToPointEnabled = true };
        var box = new TextBox { Text = value.ToString("0", CultureInfo.InvariantCulture), Margin = new Thickness(8, 0, 0, 0), FlowDirection = FlowDirection.LeftToRight, Padding = new Thickness(4, 3, 4, 3) };
        Grid.SetColumn(slider, 1); Grid.SetColumn(box, 2);
        g.Children.Add(label); g.Children.Add(slider); g.Children.Add(box);
        bool sync = false;
        slider.ValueChanged += (_, e) =>
        {
            if (sync) return;
            var v = Math.Round(e.NewValue);
            set(v);
            sync = true; box.Text = v.ToString("0", CultureInfo.InvariantCulture); sync = false;
            Paint();
        };
        box.TextChanged += (_, _) =>
        {
            if (sync || !double.TryParse(box.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) return;
            v = Math.Clamp(v, min, max);
            set(v);
            sync = true; slider.Value = v; sync = false;
            Paint();
        };
        return g;
    }

    private void Paint()
    {
        _preview.Children.Clear();
        var tip = _tip.Copy();
        tip.Size = Math.Min(tip.Size, 46);
        var accent = ThemeService.AccentColor;
        // an S-shaped stroke across the preview
        var pts = new List<double[]>();
        for (int i = 0; i <= 120; i++)
        {
            double t = i / 120.0;
            pts.Add([40 + t * 200, 62 - Math.Sin(t * Math.PI * 2) * 30, 0]);
        }
        _preview.Children.Add(new Shapes.Path { Data = StrokeGeometry.Build(pts, tip, 4), Fill = Brushes.Black, Opacity = Math.Clamp(tip.Opacity, 0.05, 1) });
        // the nib alone, at the right
        _preview.Children.Add(new Shapes.Path { Data = StrokeGeometry.Build([[272, 60, 0]], tip), Fill = new SolidColorBrush(accent) });
    }

    /// <summary>Shows the window; null when cancelled.</summary>
    public static BrushEdit? Show(BrushTip tip, bool isNew)
    {
        var w = new BrushOptionsWindow(tip, isNew);
        if (Application.Current?.MainWindow is { IsLoaded: true } owner) w.Owner = owner;
        if (w.ShowDialog() != true) return null;
        return new BrushEdit(tip, w.Deleted);
    }
}
