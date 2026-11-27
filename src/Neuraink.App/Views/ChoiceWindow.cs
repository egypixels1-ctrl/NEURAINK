using System.Windows;
using System.Windows.Controls;
using Neuraink.App.Services;

namespace Neuraink.App.Views;

/// <summary>A small question with buttons of its own (e.g. Move · Copy · Cancel). Returns the index of the button, −1 if closed.</summary>
public sealed class ChoiceWindow : Window
{
    public int Result { get; private set; } = -1;

    public ChoiceWindow(string title, string message, IReadOnlyList<string> buttons, int accent = 0, int cancel = -1)
    {
        Title = title;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FlowDirection = LanguageService.CurrentFlow;
        SetResourceReference(BackgroundProperty, "SolidBackgroundFillColorBaseBrush");

        var root = new StackPanel { Margin = new Thickness(22, 20, 22, 18), MaxWidth = 520, MinWidth = 340 };
        root.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 13.5, LineHeight = 21 });
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
        for (int i = 0; i < buttons.Count; i++)
        {
            int k = i;
            var b = new Button { Content = buttons[i], MinWidth = 100, Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0) };
            if (i == accent) { b.SetResourceReference(StyleProperty, "AccentButtonStyle"); b.IsDefault = true; }
            if (i == cancel) b.IsCancel = true;
            b.Click += (_, _) => { Result = k; DialogResult = true; };
            row.Children.Add(b);
        }
        root.Children.Add(row);
        Content = root;
        Loaded += (_, _) => AccentService.Apply(ThemeService.Accent, ThemeService.Current == AppTheme.Light);
    }

    public static int Ask(string message, IReadOnlyList<string> buttons, int accent = 0, int cancel = -1, string title = "NEURAINK")
    {
        var w = new ChoiceWindow(title, message, buttons, accent, cancel);
        if (Application.Current?.MainWindow is { IsLoaded: true } owner) w.Owner = owner;
        w.ShowDialog();
        return w.Result;
    }
}
