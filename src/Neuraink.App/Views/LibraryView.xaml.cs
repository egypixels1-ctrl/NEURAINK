using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Neuraink.App.ViewModels;

namespace Neuraink.App.Views;

public partial class LibraryView : UserControl
{
    public LibraryView()
    {
        InitializeComponent();
        PreviewKeyDown += OnKey;
        Drop += OnDrop;
        DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
    }

    private LibraryViewModel? Vm => DataContext as LibraryViewModel;
    private static LibraryTile? TileOf(object sender) => (sender as FrameworkElement)?.DataContext as LibraryTile;

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (Vm == null || Keyboard.FocusedElement is TextBox) return;
        if (e.Key == Key.Delete && Vm.HasSelection) { Vm.DeleteCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.Escape && Vm.HasSelection) { Vm.ClearSelectionCommand.Execute(null); e.Handled = true; }
    }

    /// <summary>Pictures dropped from Explorer are added to the library.</summary>
    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (Vm == null || e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        var pics = files.Where(f => new[] { ".png", ".jpg", ".jpeg", ".bmp", ".webp", ".tif", ".tiff" }.Contains(Path.GetExtension(f).ToLowerInvariant())).ToList();
        if (pics.Count > 0) await Vm.AddFiles(pics);
    }

    /// <summary>The middle button opens the piece in a new tab (like a browser).</summary>
    private void OnTileMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && TileOf(sender) is { } t) { Vm?.OpenNewTabCommand.Execute(t); e.Handled = true; }
    }

    private void OnMenuOpen(object sender, RoutedEventArgs e)
    {
        if (TileOf(sender) is { } t && MainViewModel.Current is { } m) m.OpenAnnotate(t.Item.Id);
    }

    private void OnMenuOpenNewTab(object sender, RoutedEventArgs e) { if (TileOf(sender) is { } t) Vm?.OpenNewTabCommand.Execute(t); }
    private void OnMenuSelect(object sender, RoutedEventArgs e) { if (TileOf(sender) is { } t) t.IsSelected = !t.IsSelected; }
    private void OnMenuDelete(object sender, RoutedEventArgs e) { if (TileOf(sender) is { } t) Vm?.DeleteCommand.Execute(t); }
}
