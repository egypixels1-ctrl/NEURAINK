using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Neuraink.App.Services;
using Neuraink.Core;

namespace Neuraink.App.Views;

/// <summary>New Artboard: the text to write (its letters guide the work) and the size of the white page.</summary>
public sealed class NewArtboardWindow : Window
{
    private readonly TextBox _text = new() { FontSize = 18, Padding = new Thickness(8, 5, 8, 5), FlowDirection = FlowDirection.RightToLeft };
    private readonly TextBox _w = new() { Text = "2000", Padding = new Thickness(6, 4, 6, 4), FlowDirection = FlowDirection.LeftToRight };
    private readonly TextBox _h = new() { Text = "2000", Padding = new Thickness(6, 4, 6, 4), FlowDirection = FlowDirection.LeftToRight };

    public string ResultText => _text.Text.Trim();
    public int ResultW => int.TryParse(_w.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? Math.Clamp(v, 200, 8000) : 2000;
    public int ResultH => int.TryParse(_h.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? Math.Clamp(v, 200, 8000) : 2000;

    public NewArtboardWindow()
    {
        Title = Loc.T("Board.New");
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FlowDirection = LanguageService.CurrentFlow;
        SetResourceReference(BackgroundProperty, "SolidBackgroundFillColorBaseBrush");

        var root = new StackPanel { Margin = new Thickness(20), Width = 340 };
        root.Children.Add(new TextBlock { Text = Loc.T("Board.Text"), Margin = new Thickness(0, 0, 0, 4) });
        root.Children.Add(_text);
        root.Children.Add(new TextBlock { Text = Loc.T("Board.TextHint"), FontSize = 11.5, Opacity = 0.7, Margin = new Thickness(0, 4, 0, 14), TextWrapping = TextWrapping.Wrap });

        root.Children.Add(new TextBlock { Text = Loc.T("Board.Size"), Margin = new Thickness(0, 0, 0, 4) });
        var size = new StackPanel { Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.LeftToRight };
        _w.Width = 90; _h.Width = 90;
        size.Children.Add(_w);
        size.Children.Add(new TextBlock { Text = "×", Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
        size.Children.Add(_h);
        size.Children.Add(new TextBlock { Text = "px", Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 });
        root.Children.Add(size);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        var ok = new Button { Content = Loc.T("Board.Create"), MinWidth = 110, Padding = new Thickness(0, 7, 0, 7), IsDefault = true };
        ok.SetResourceReference(StyleProperty, "AccentButtonStyle");
        ok.Click += (_, _) =>
        {
            if (Arabic.Letters(ResultText).Count == 0) { _text.Focus(); return; }
            DialogResult = true;
        };
        var cancel = new Button { Content = Loc.T("Common.Cancel"), MinWidth = 96, Padding = new Thickness(0, 7, 0, 7), IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);
        Content = root;
        Loaded += (_, _) => { AccentService.Apply(ThemeService.Accent, ThemeService.Current == AppTheme.Light); _text.Focus(); };
    }

    public static (string Text, int W, int H)? Ask()
    {
        var w = new NewArtboardWindow();
        if (Application.Current?.MainWindow is { IsLoaded: true } owner) w.Owner = owner;
        return w.ShowDialog() == true ? (w.ResultText, w.ResultW, w.ResultH) : null;
    }
}
