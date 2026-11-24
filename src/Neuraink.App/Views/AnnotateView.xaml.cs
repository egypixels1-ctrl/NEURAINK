using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Neuraink.App.Services;
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
        if (_vm != null) { _vm.Redraw -= Draw; _vm.Opened -= OnOpened; _vm.PropertyChanged -= OnVmChanged; }
        _vm = vm;
        if (_vm != null)
        {
            _vm.Redraw += Draw; _vm.Opened += OnOpened;
            _vm.EditBrushDialog = BrushOptionsWindow.Show;
            _vm.NewArtboardDialog = NewArtboardWindow.Ask;
            _vm.PropertyChanged += OnVmChanged;
            UpdateReference();
            Draw();
        }
    }

    private string _lastHint = "";

    private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AnnotateViewModel.Hint):
                if (_vm != null && _vm.Hint != _lastHint && _vm.Hint.Length > 0) { _lastHint = _vm.Hint; PulseHint(); }
                break;
            case nameof(AnnotateViewModel.RefX) or nameof(AnnotateViewModel.RefY) or nameof(AnnotateViewModel.RefScale)
                or nameof(AnnotateViewModel.Reference) or nameof(AnnotateViewModel.AdjustReference):
                UpdateReference();
                break;
        }
    }

    /// <summary>A soft pulse of the "!" when the guidance changes.</summary>
    private void PulseHint()
    {
        Dispatcher.BeginInvoke(() =>
        {
            HintToggle.ApplyTemplate();
            if (HintToggle.Template?.FindName("HintScale", HintToggle) is not ScaleTransform sc) return;
            var anim = new System.Windows.Media.Animation.DoubleAnimation(1, 1.35, TimeSpan.FromMilliseconds(260))
            {
                AutoReverse = true, RepeatBehavior = new System.Windows.Media.Animation.RepeatBehavior(2),
                EasingFunction = new System.Windows.Media.Animation.SineEase(),
            };
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>Places the artboard's reference picture (position, scale) and its frame while adjusting.</summary>
    private void UpdateReference()
    {
        if (_vm?.Reference is not { } r) { RefPic.Visibility = Visibility.Collapsed; RefFrame.Visibility = Visibility.Collapsed; return; }
        RefPic.Visibility = Visibility.Visible;
        double w = r.PixelWidth * _vm.RefScale, h = r.PixelHeight * _vm.RefScale;
        RefPic.Width = w; RefPic.Height = h;
        Canvas.SetLeft(RefPic, _vm.RefX); Canvas.SetTop(RefPic, _vm.RefY);
        RefFrame.Width = w; RefFrame.Height = h;
        RefFrame.BorderThickness = new Thickness(2 / _zoom);
        Canvas.SetLeft(RefFrame, _vm.RefX); Canvas.SetTop(RefFrame, _vm.RefY);
        RefFrame.Visibility = _vm.AdjustReference ? Visibility.Visible : Visibility.Collapsed;
        if (_vm.Image is { } img) { RefLayer.Width = img.PixelWidth; RefLayer.Height = img.PixelHeight; }
    }

    private void OnOpened() => Dispatcher.BeginInvoke(() => { Fit(); Focus(); }, System.Windows.Threading.DispatcherPriority.Loaded);

    // ------------------------------------------------------------------ zoom
    private void SetZoom(double z)
    {
        _zoom = Math.Clamp(z, 0.1, 8);
        Zoom.ScaleX = Zoom.ScaleY = _zoom;
        UpdateReference();
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

    private string? _angleFlash;
    private DateTime _angleFlashUntil;

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        if ((mods & ModifierKeys.Alt) != 0 && _vm?.IsBrushTool == true)
        {
            // Alt + wheel turns the brush: 5° a notch, 1° with Shift
            double step = (mods & ModifierKeys.Shift) != 0 ? 1 : 5;
            var a = _vm.RotateBrush(e.Delta > 0 ? step : -step);
            _angleFlash = $"{a:0}°";
            _angleFlashUntil = DateTime.Now.AddMilliseconds(900);
            _mouse = e.GetPosition(Overlay);
            Draw();
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(950) };
            t.Tick += (_, _) => { t.Stop(); Draw(); };
            t.Start();
            e.Handled = true;
            return;
        }
        if ((mods & ModifierKeys.Control) != 0)
        {
            e.Handled = true;
            SetZoom(_zoom * (e.Delta > 0 ? 1.15 : 1 / 1.15));
            return;
        }
        if (_vm is { AdjustReference: true, IsArtboard: true })
        {
            _vm.ScaleReference(e.Delta > 0 ? 1.06 : 1 / 1.06, e.GetPosition(Overlay));
            e.Handled = true;
        }
    }

    // ------------------------------------------------------------------ input: pen tool (Illustrator style)
    private PenNode? _dragNode;
    private Point _downAt;
    private Point? _mouse;
    private bool _stroking;
    private (PenNode Node, string Part)? _edit;
    private Point? _refDrag;

    /// <summary>Pen pressure of a tablet (0–1); 0 for a mouse (the nib keeps its size).</summary>
    private double Pressure(MouseEventArgs e)
    {
        if (e.StylusDevice == null) return 0;
        var pts = e.StylusDevice.GetStylusPoints(Overlay);
        return pts.Count > 0 ? pts[^1].PressureFactor : 0;
    }

    private void OnOverlayDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
        if (_vm == null || _space) return;
        var p = e.GetPosition(Overlay);
        if (_vm.Tool is "entry" or "exit") { _vm.Click(p, 10 / _zoom); return; }
        if (_vm is { AdjustReference: true, IsArtboard: true })
        {
            _refDrag = p;
            Overlay.CaptureMouse();
            e.Handled = true;
            return;
        }
        if (_vm.IsBrushTool)
        {
            _vm.BeginStroke(p, Pressure(e));
            Overlay.CaptureMouse();
            _stroking = true;
            e.Handled = true;
            return;
        }
        bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
        if (_vm.IsOnFirstAnchor(p, 10 / _zoom) && !alt) { _vm.CloseShape(); return; }
        // editing the path: drag an anchor or a handle (Alt breaks the handles apart); Alt + click on the last anchor cuts its out handle
        if (_vm.HitPen(p, 7 / _zoom) is { } hit)
        {
            if (alt && hit.Part == "anchor" && _vm.IsLastAnchor(hit.Node)) { _vm.CutOutHandle(hit.Node); e.Handled = true; return; }
            _vm.Checkpoint();
            _edit = hit;
            Overlay.CaptureMouse();
            e.Handled = true;
            return;
        }
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
        if (_refDrag is { } rd && e.LeftButton == MouseButtonState.Pressed) { _vm.MoveReference(p - rd); _refDrag = p; return; }
        if (_edit is { } ed && e.LeftButton == MouseButtonState.Pressed)
        {
            bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
            if (ed.Part == "anchor") _vm.MoveAnchor(ed.Node, p);
            else _vm.MoveHandle(ed.Node, ed.Part == "out", p, alt);
            return;
        }
        if (_stroking && e.LeftButton == MouseButtonState.Pressed) { _vm.ExtendStroke(p, Pressure(e), 1.2 / _zoom); return; }
        if (_vm.IsBrushTool) { Draw(); return; }   // nib preview follows the mouse
        if (_dragNode != null && e.LeftButton == MouseButtonState.Pressed)
        {
            if ((p - _downAt).Length * _zoom > 3) _vm.DragHandles(_dragNode, p, (Keyboard.Modifiers & ModifierKeys.Alt) != 0);
            return;
        }
        Draw();   // rubber band and pen cursor follow the mouse
    }

    private void OnOverlayUp(object sender, MouseButtonEventArgs e)
    {
        if (_stroking)
        {
            _stroking = false;
            _vm?.EndStroke(stay: (Keyboard.Modifiers & ModifierKeys.Shift) != 0, zoom: _zoom);
        }
        _dragNode = null;
        _edit = null;
        _refDrag = null;
        if (Overlay.IsMouseCaptured) Overlay.ReleaseMouseCapture();
    }

    private void OnOverlayLeave(object sender, MouseEventArgs e)
    {
        if (_stroking || _dragNode != null) return;
        _mouse = null;
        Draw();
    }

    // ------------------------------------------------------------------ Brushes panel
    private void OnBrushClick(object sender, MouseButtonEventArgs e)
    {
        if (_vm == null || (sender as FrameworkElement)?.DataContext is not BrushItem b) return;
        if (e.ClickCount >= 2) _vm.EditBrushCommand.Execute(b);
        else _vm.SelectBrushCommand.Execute(b);
        e.Handled = true;
    }

    private void OnBrushRenameMenu(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is BrushItem b) _vm?.RenameBrushCommand.Execute(b);
    }

    private void OnRenameShown(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox t && t.IsVisible) Dispatcher.BeginInvoke(() => { t.Focus(); t.SelectAll(); }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void OnRenameKey(object sender, KeyEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not BrushItem b || _vm == null) return;
        if (e.Key == Key.Enter) { _vm.CommitRename(b, keep: true); Focus(); e.Handled = true; }
        else if (e.Key == Key.Escape) { _vm.CommitRename(b, keep: false); Focus(); e.Handled = true; }
    }

    private void OnRenameLost(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is BrushItem b) _vm?.CommitRename(b, keep: true);
    }

    private void OnBrushEditMenu(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is BrushItem b) _vm?.EditBrushCommand.Execute(b);
    }

    private void OnBrushDuplicateMenu(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is BrushItem b) _vm?.DuplicateBrushCommand.Execute(b);
    }

    // ------------------------------------------------------------------ input: hand tool (hold Space, or the middle button)
    private bool _space, _panning;
    private Point _panFrom;
    private double _panH, _panV;

    private void SetHand(bool on)
    {
        Overlay.Cursor = on ? Cursors.Hand : _vm?.Tool is "entry" or "exit" ? Cursors.Cross : Cursors.None;
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
            case Key.F2 when _vm.IsBrushTool: _vm.RenameBrushCommand.Execute(null); break;
            case Key.System when e.SystemKey is Key.LeftAlt or Key.RightAlt: break;   // Alt alone: no menu focus
            case Key.Space:
                if (!_space) { _space = true; SetHand(true); }
                break;
            case Key.Enter: _vm.CloseShape(); break;
            case Key.Back: _vm.BackspacePoint(); break;
            case Key.Escape: _vm.CancelDrawing(); break;
            case Key.Tab: _vm.SkipStep(); break;
            case Key.Z when ctrl && (Keyboard.Modifiers & ModifierKeys.Shift) != 0: _vm.RedoStep(); break;
            case Key.Z when ctrl: _vm.UndoStep(); break;
            case Key.Y when ctrl: _vm.RedoStep(); break;
            case Key.OemOpenBrackets: _vm.BrushSize = Math.Max(2, _vm.BrushSize - (_vm.BrushSize > 20 ? 4 : 1)); break;
            case Key.OemCloseBrackets: _vm.BrushSize = _vm.BrushSize + (_vm.BrushSize >= 20 ? 4 : 1); break;
            case Key.OemPlus or Key.Add when ctrl: SetZoom(_zoom * 1.25); break;
            case Key.OemMinus or Key.Subtract when ctrl: SetZoom(_zoom / 1.25); break;
            case Key.D0 or Key.NumPad0 when ctrl: Fit(); break;
            case Key.S when !ctrl: _vm.Save(); break;
            case Key.D1 or Key.NumPad1: _vm.SetToolByKey(1); break;
            case Key.D2 or Key.NumPad2: _vm.SetToolByKey(2); break;
            case Key.D3 or Key.NumPad3: _vm.SetToolByKey(3); break;
            case Key.D4 or Key.NumPad4: _vm.SetToolByKey(4); break;
            case Key.D5 or Key.NumPad5: _vm.SetToolByKey(5); break;
            case Key.D6 or Key.NumPad6: _vm.SetToolByKey(6); break;
            case Key.D7 or Key.NumPad7: _vm.SetToolByKey(7); break;
            case Key.D8 or Key.NumPad8: _vm.SetToolByKey(8); break;
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
        if (!_space && !_panning) Overlay.Cursor = _vm.AdjustReference && _vm.IsArtboard ? Cursors.SizeAll : _vm.Tool is "entry" or "exit" ? Cursors.Cross : Cursors.None;

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

        // brush strokes: the letter traced with the calligraphic nib, in its colour
        foreach (var s in _vm.Ann.Segments.Where(x => x.Stroke is { Count: > 0 } && x.Brush != null))
        {
            var c = _vm.ColorOf(s);
            Overlay.Children.Add(new Shapes.Path
            {
                Data = StrokeGeometry.ForSegment(s),
                Fill = new SolidColorBrush(Color.FromArgb((byte)(255 * Math.Clamp(s.Brush!.Opacity, 0.05, 1)), c.R, c.G, c.B)),
                IsHitTestVisible = false,
            });
        }
        if (_vm.CurrentStroke.Count > 0)
        {
            var c = _vm.BrushColor;
            Overlay.Children.Add(new Shapes.Path
            {
                Data = StrokeGeometry.Build(_vm.CurrentStroke, _vm.Tip, _vm.Fidelity),
                Fill = new SolidColorBrush(Color.FromArgb((byte)(255 * Math.Clamp(_vm.Tip.Opacity, 0.05, 1)), c.R, c.G, c.B)),
                IsHitTestVisible = false,
            });
        }
        if (_vm.IsBrushTool && _mouse is { } nib && !_stroking && !_space && !_vm.AdjustReference)
        {
            // the nib under the mouse: its size, angle and roundness before drawing
            var c = _vm.BrushColor;
            Overlay.Children.Add(new Shapes.Path
            {
                Data = StrokeGeometry.Build([[nib.X, nib.Y, 0]], _vm.Tip),
                Stroke = new SolidColorBrush(c), StrokeThickness = 1.2 * px,
                Fill = new SolidColorBrush(Color.FromArgb(60, c.R, c.G, c.B)),
                IsHitTestVisible = false,
            });
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
        if (_vm.IsPenTool && _mouse is { } pm && !_space && !_panning && _dragNode == null && !_vm.AdjustReference) PenCursor(pm, px);
        if (_angleFlash != null && DateTime.Now < _angleFlashUntil && _mouse is { } am)
        {
            var label = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(235, 28, 28, 30)), CornerRadius = new CornerRadius(4 * px),
                Padding = new Thickness(6 * px, 2 * px, 6 * px, 2 * px), IsHitTestVisible = false,
                Child = new TextBlock { Text = _angleFlash, Foreground = Brushes.White, FontSize = 12 * px, FontWeight = FontWeights.SemiBold },
            };
            Canvas.SetLeft(label, am.X + 16 * px);
            Canvas.SetTop(label, am.Y + 12 * px);
            Overlay.Children.Add(label);
        }
    }

    /// <summary>The pen tool cursor: the fountain-pen nib of Illustrator, its tip on the mouse.</summary>
    private void PenCursor(Point m, double px)
    {
        if (TryFindResource("PenNibGeometry") is not Geometry nib) return;
        var g = nib.Clone();
        var t = new TransformGroup();
        t.Children.Add(new TranslateTransform(-12, -1.5));          // the tip of the nib on the hot spot
        t.Children.Add(new RotateTransform(-45));                   // pointing up-left, like Illustrator
        t.Children.Add(new ScaleTransform(px * 0.95, px * 0.95));   // about 23 screen pixels at any zoom
        t.Children.Add(new TranslateTransform(m.X, m.Y));
        g.Transform = t;
        Overlay.Children.Add(new Shapes.Path { Data = g, Fill = Brushes.Black, Stroke = Brushes.White, StrokeThickness = 1.1 * px, IsHitTestVisible = false });
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
