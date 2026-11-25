using System.Windows;

namespace Neuraink.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // small screens: the window never opens larger than the work area
        var area = SystemParameters.WorkArea;
        if (Width > area.Width || Height > area.Height)
        {
            Width = Math.Min(Width, area.Width);
            Height = Math.Min(Height, area.Height);
            MinWidth = Math.Min(MinWidth, area.Width);
            MinHeight = Math.Min(MinHeight, area.Height);
            WindowState = WindowState.Maximized;
        }
        // unsaved tabs of Letter Segmentation: save them, leave them, or stay
        Closing += (_, e) => { if (DataContext is ViewModels.MainViewModel vm && !vm.ConfirmClose()) e.Cancel = true; };
    }
}
