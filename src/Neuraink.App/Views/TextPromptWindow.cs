using System.Windows;
using System.Windows.Controls;
using Neuraink.App.Services;

namespace Neuraink.App.Views;

/// <summary>A one-line question (e.g. the name of a new style folder).</summary>
public sealed class TextPromptWindow : Window
{
    private readonly TextBox _box = new() { FontSize = 15, Padding = new Thickness(8, 5, 8, 5), FlowDirection = FlowDirection.LeftToRight };

    public TextPromptWindow(string title, string hint, string value)
    {
        Title = title;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FlowDirection = LanguageService.CurrentFlow;
        SetResourceReference(BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        var root = new StackPanel { Margin = new Thickness(20), Width = 340 };
        root.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        _box.Text = value;
        root.Children.Add(_box);
        root.Children.Add(new TextBlock { Text = hint, FontSize = 11.5, Opacity = 0.7, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap });
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var ok = new Button { Content = Loc.T("Common.OK"), MinWidth = 96, Padding = new Thickness(0, 7, 0, 7), IsDefault = true };
        ok.SetResourceReference(StyleProperty, "AccentButtonStyle");
        ok.Click += (_, _) => DialogResult = true;
        row.Children.Add(ok);
        row.Children.Add(new Button { Content = Loc.T("Common.Cancel"), MinWidth = 96, Padding = new Thickness(0, 7, 0, 7), IsCancel = true, Margin = new Thickness(8, 0, 0, 0) });
        root.Children.Add(row);
        Content = root;
        Loaded += (_, _) => { AccentService.Apply(ThemeService.Accent, ThemeService.Current == AppTheme.Light); _box.Focus(); _box.SelectAll(); };
    }

    public static string? Ask(string title, string hint, string value)
    {
        var w = new TextPromptWindow(title, hint, value);
        if (Application.Current?.MainWindow is { IsLoaded: true } owner) w.Owner = owner;
        return w.ShowDialog() == true ? w._box.Text.Trim() : null;
    }
}
