using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Neuraink.App.ViewModels;
using Shapes = System.Windows.Shapes;

namespace Neuraink.App.Views;

/// <summary>The drawing surface of the letter segmentation: zoom, clicks, keys and the painted regions.</summary>
public partial class AnnotateView : UserControl
{
    private AnnotateViewModel? _vm;
    private double _zoom = 1;

    public AnnotateView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += (_, _) => Attach(null);
        DataContextChanged += (_, _) => { if (IsLoaded) Attach(DataContext as AnnotateViewModel); };
        PreviewKeyDown += OnKey;
        PreviewKeyUp += OnKeyUp;
        SizeChanged += (_, _) => { if (_vm?.Image != null && _zoom == _fitZoom) Fit(); };
    }

    private double _fitZoom = -1;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Attach(DataContext as AnnotateViewModel);
        Dispatcher.BeginInvoke(() => { Fit(); Focus(); }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void Attach(AnnotateViewModel? vm)
    {
        if (_vm != null) { _vm.Redraw -= Draw; _vm.Opened -= OnOpened; }
        _vm = vm;
        if (_vm != null) { _vm.Redraw += Draw; _vm.Opened += OnOpened; Draw(); }
    }

    private void OnOpened() => Dispatcher.BeginInvoke(() => { Fit(); Focus(); }, System.Windows.Threading.DispatcherPriority.Loaded);

    // ------------------------------------------------------------------ zoom
    private void SetZoom(double z)
    {
        _zoom = Math.Clamp(z, 0.1, 8);
        Zoom.ScaleX = Zoom.ScaleY = _zoom;
        Draw();
    }

    private void Fit()
    {
        if (_vm?.Image is not { } img) { Draw(); return; }
        Paper.Width = img.PixelWidth;
        Paper.Height = img.PixelHeight;
        double vw = Math.Max(100, Scroller.ActualWidth - 70), vh = Math.Max(100, Scroller.ActualHeight - 70);
        _fitZoom = Math.Min(Math.Min(vw / img.PixelWidth, vh / img.PixelHeight), 3);
        SetZoom(_fitZoom);
    }

    private void OnZoomIn(object sender, RoutedEventArgs e) => SetZoom(_zoom * 1.25);
    private void OnZoomOut(object sender, RoutedEventArgs e) => SetZoom(_zoom / 1.25);
    private void OnZoomFit(object sender, RoutedEventArgs e) => Fit();

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        e.Handled = true;
        SetZoom(_zoom * (e.Delta > 0 ? 1.15 : 1 / 1.15));
    }

    // ------------------------------------------------------------------ input: pen tool (Illustrator style)
    private PenNode? _dragNode;
    private Point _downAt;
    private Point? _mouse;

    private void OnOverlayDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
        if (_vm == null || _space) return;
        var p = e.GetPosition(Overlay);
        if (_vm.Tool is "entry" or "exit") { _vm.Click(p, 10 / _zoom); return; }
        if (_vm.IsOnFirstAnchor(p, 10 / _zoom)) { _vm.CloseShape(); return; }
        // a click adds a corner anchor; dragging before releasing pulls out smooth curve handles
        _dragNode = _vm.AddAnchor(p);
        _downAt = p;
        if (_dragNode != null) Overlay.CaptureMouse();
        e.Handled = true;
    }

    private void OnOverlayMove(object sender, MouseEventArgs e)
    {
        if (_vm == null) return;
        var p = e.GetPosition(Overlay);
        _mouse = p;
        if (_dragNode != null && e.LeftButton == MouseButtonState.Pressed)
        {
            if ((p - _downAt).Length * _zoom > 3) _vm.DragHandles(_dragNode, p);
            return;
        }
        if (_vm.Drawing.Count > 0) Draw();   // rubber band to the mouse
    }

    private void OnOverlayUp(object sender, MouseButtonEventArgs e)
    {
        _dragNode = null;
        if (Overlay.IsMouseCaptured) Overlay.ReleaseMouseCapture();
    }

    // ------------------------------------------------------------------ input: hand tool (hold Space, or the middle button)
    private bool _space, _panning;
    private Point _panFrom;
    private double _panH, _panV;

    private void SetHand(bool on)
    {
        Overlay.Cursor = on ? Cursors.Hand : Cursors.Pen;
        Scroller.Cursor = on ? Cursors.Hand : null;
    }

    private void OnPanDown(object sender, MouseButtonEventArgs e)
    {
        bool middle = e.ChangedButton == MouseButton.Middle;
        if (!(middle || (_space && e.ChangedButton == MouseButton.Left))) return;
        _panning = true;
        _panFrom = e.GetPosition(Scroller);
        _panH = Scroller.HorizontalOffset;
        _panV = Scroller.VerticalOffset;
        SetHand(true);
        Scroller.CaptureMouse();
        e.Handled = true;
    }

    private void OnPanMove(object sender, MouseEventArgs e)
    {
        if (!_panning) return;
        var d = e.GetPosition(Scroller) - _panFrom;
        Scroller.ScrollToHorizontalOffset(_panH - d.X);
        Scroller.ScrollToVerticalOffset(_panV - d.Y);
        e.Handled = true;
    }

    private void OnPanUp(object sender, MouseButtonEventArgs e)
    {
        if (!_panning) return;
        _panning = false;
        Scroller.ReleaseMouseCapture();
        SetHand(_space);
        e.Handled = true;
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (_vm == null || Keyboard.FocusedElement is TextBox) return;
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        switch (e.Key)
        {
            case Key.Space:
                if (!_space) { _space = true; SetHand(true); }
                break;
            case Key.Enter: _vm.CloseShape(); break;
            case Key.Back: _vm.BackspacePoint(); break;
            case Key.Escape: _vm.CancelDrawing(); break;
            case Key.Tab: _vm.SkipStep(); break;
            case Key.Z when ctrl: _vm.Undo(); break;
            case Key.OemPlus or Key.Add when ctrl: SetZoom(_zoom * 1.25); break;
            case Key.OemMinus or Key.Subtract when ctrl: SetZoom(_zoom / 1.25); break;
            case Key.D0 or Key.NumPad0 when ctrl: Fit(); break;
            case Key.S when !ctrl: _vm.Save(); break;
            case Key.D1 or Key.NumPad1: _vm.SetToolByKey(1); break;
            case Key.D2 or Key.NumPad2: _vm.SetToolByKey(2); break;
            case Key.D3 or Key.NumPad3: _vm.SetToolByKey(3); break;
            case Key.D4 or Key.NumPad4: _vm.SetToolByKey(4); break;
            case Key.D5 or Key.NumPad5: _vm.SetToolByKey(5); break;
            default: return;
        }
        e.Handled = true;
    }

    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space) return;
        _space = false;
        if (!_panning) SetHand(false);
        e.Handled = true;
    }

    // ------------------------------------------------------------------ painting
    private void Draw()
    {
        Overlay.Children.Clear();
        if (_vm?.Image is not { } img) return;
        Overlay.Width = img.PixelWidth;
        Overlay.Height = img.PixelHeight;
        double px = 1 / _zoom;   // one screen pixel in picture units

        foreach (var s in _vm.Ann.Segments)
        {
            var c = _vm.ColorOf(s);
            bool on = s.Kind != "exclude" && s.Chip == _vm.Active;
            var poly = new Shapes.Polygon
            {
                Points = new PointCollection(s.Points.Select(p => new Point(p[0], p[1]))),
                Fill = new SolidColorBrush(Color.FromArgb((byte)(s.Kind == "exclude" ? 46 : on ? 72 : 40), c.R, c.G, c.B)),
                Stroke = new SolidColorBrush(c),
                StrokeThickness = (on ? 2.4 : 1.6) * px,
                StrokeLineJoin = PenLineJoin.Round,
                IsHitTestVisible = false,
            };
            if (s.Kind == "dots") poly.StrokeDashArray = new DoubleCollection { 4, 3 };
            Overlay.Children.Add(poly);
            if (s.Kind == "letter")
            {
                if (s.Entry is { } en) Dot(en[0], en[1], 7 * px, AnnotateViewModel.EntryColor, px);
                if (s.Exit is { } ex) Dot(ex[0], ex[1], 7 * px, AnnotateViewModel.ExitColor, px);
            }
        }

        if (_vm.Drawing.Count > 0)
        {
            var c = _vm.Tool == "exclude" ? AnnotateViewModel.ExcludeColor : AnnotateViewModel.ChipColor(_vm.Active);
            var brush = new SolidColorBrush(c);
            // the path so far (curves where anchors have handles), then a thin line to the mouse
            var line = AnnotateViewModel.Flatten(_vm.Drawing, closed: false);
            Overlay.Children.Add(new Shapes.Polyline
            {
                Points = new PointCollection(line), Stroke = brush, StrokeThickness = 2 * px,
                StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false,
            });
            if (_mouse is { } m && _dragNode == null)
                Overlay.Children.Add(new Shapes.Line
                {
                    X1 = _vm.Drawing[^1].A.X, Y1 = _vm.Drawing[^1].A.Y, X2 = m.X, Y2 = m.Y,
                    Stroke = brush, StrokeThickness = 1 * px, StrokeDashArray = new DoubleCollection { 4, 3 }, IsHitTestVisible = false,
                });
            for (int i = 0; i < _vm.Drawing.Count; i++)
            {
                var n = _vm.Drawing[i];
                // handles of smooth anchors
                foreach (var h in new[] { n.In, n.Out })
                {
                    if (h is not { } hp) continue;
                    Overlay.Children.Add(new Shapes.Line { X1 = n.A.X, Y1 = n.A.Y, X2 = hp.X, Y2 = hp.Y, Stroke = brush, StrokeThickness = 1 * px, IsHitTestVisible = false });
                    Dot(hp.X, hp.Y, 3 * px, c, px, c);
                }
                Square(n.A.X, n.A.Y, (i == 0 ? 5 : 3.5) * px, i == 0 ? Colors.White : c, c, px);
            }
        }
    }

    /// <summary>An anchor: a small square, like Illustrator.</summary>
    private void Square(double x, double y, double r, Color fill, Color stroke, double px)
    {
        var e = new Shapes.Rectangle
        {
            Width = 2 * r, Height = 2 * r, Fill = new SolidColorBrush(fill), Stroke = new SolidColorBrush(stroke),
            StrokeThickness = 1.5 * px, IsHitTestVisible = false,
        };
        Canvas.SetLeft(e, x - r);
        Canvas.SetTop(e, y - r);
        Overlay.Children.Add(e);
    }

    private void Dot(double x, double y, double r, Color fill, double px, Color? stroke = null)
    {
        var e = new Shapes.Ellipse
        {
            Width = 2 * r, Height = 2 * r,
            Fill = new SolidColorBrush(fill),
            Stroke = new SolidColorBrush(stroke ?? Colors.White),
            StrokeThickness = 2 * px,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(e, x - r);
        Canvas.SetTop(e, y - r);
        Overlay.Children.Add(e);
    }
}
