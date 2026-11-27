using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Neuraink.App.Services;
using Neuraink.App.ViewModels;
using Neuraink.Core;
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
        Unloaded += (_, _) => { if (_vm?.CurrentTab is { } t) OnSavingView(t); Attach(null); };
        DataContextChanged += (_, _) => { if (IsLoaded) Attach(DataContext as AnnotateViewModel); };
        // the keys are taken from the whole window while the studio is shown: the tool shortcuts (V, P, B, C, K …) work
        // wherever the focus is (a button of the panel, the menu), except while typing in a text box
        Loaded += (_, _) =>
        {
            if (Window.GetWindow(this) is { } w && w != _keysFrom)
            {
                if (_keysFrom != null) { _keysFrom.PreviewKeyDown -= OnKey; _keysFrom.PreviewKeyUp -= OnKeyUp; }
                _keysFrom = w;
                w.PreviewKeyDown += OnKey;
                w.PreviewKeyUp += OnKeyUp;
            }
        };
        Unloaded += (_, _) =>
        {
            if (_keysFrom != null) { _keysFrom.PreviewKeyDown -= OnKey; _keysFrom.PreviewKeyUp -= OnKeyUp; _keysFrom = null; }
        };
        SizeChanged += (_, _) => { if (_vm?.Image != null && _zoom == _fitZoom) Fit(); };
    }

    private double _fitZoom = -1;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Attach(DataContext as AnnotateViewModel);
        ApplyPanel();
        Dispatcher.BeginInvoke(() => { ShowTabView(); Focus(); }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void Attach(AnnotateViewModel? vm)
    {
        if (_vm != null) { _vm.Redraw -= Draw; _vm.Opened -= OnOpened; _vm.LiveStroke -= OnLiveStroke; _vm.BrushReverted -= OnBrushReverted; _vm.BrushFollowed -= OnBrushFollowed; _vm.PropertyChanged -= OnVmChanged; _vm.SavingView -= OnSavingView; }
        _vm = vm;
        if (_vm != null)
        {
            _vm.Redraw += Draw; _vm.Opened += OnOpened; _vm.LiveStroke += OnLiveStroke;
            _vm.BrushReverted += OnBrushReverted; _vm.BrushFollowed += OnBrushFollowed;
            _vm.EditBrushDialog = BrushOptionsWindow.Show;
            _vm.NewArtboardDialog = NewArtboardWindow.Ask;
            _vm.AskSaveDialog = AskSave;
            _vm.SavingView += OnSavingView;
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

    /// <summary>A soft, calm pulse of the yellow "!" when the guidance changes (its light fades a little and comes back).</summary>
    private void PulseHint()
    {
        Dispatcher.BeginInvoke(() =>
        {
            HintToggle.ApplyTemplate();
            if (HintToggle.Template?.FindName("Mark", HintToggle) is not UIElement mark) return;
            var anim = new System.Windows.Media.Animation.DoubleAnimation(1, 0.4, TimeSpan.FromMilliseconds(700))
            {
                AutoReverse = true, RepeatBehavior = new System.Windows.Media.Animation.RepeatBehavior(2),
                EasingFunction = new System.Windows.Media.Animation.SineEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut },
            };
            mark.BeginAnimation(OpacityProperty, anim);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // ------------------------------------------------------------------ tabs
    private static bool? AskSave(string message)
    {
        var r = MessageBox.Show(Application.Current?.MainWindow!, message, "NEURAINK", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return r switch { MessageBoxResult.Yes => true, MessageBoxResult.No => false, _ => null };
    }

    /// <summary>Another tab comes to the front: this one keeps its zoom and scroll.</summary>
    private void OnSavingView(DocTab tab)
    {
        if (_vm?.Image == null) return;
        tab.Zoom = _zoom;
        tab.ScrollH = Scroller.HorizontalOffset;
        tab.ScrollV = Scroller.VerticalOffset;
    }

    /// <summary>The tab in front shows as it was left (zoom, scroll), or fitted when it is new.</summary>
    private void ShowTabView()
    {
        if (_vm?.CurrentTab is not { Zoom: > 0 } t || _vm.Image is not { } img) { Fit(); return; }
        Paper.Width = img.PixelWidth;
        Paper.Height = img.PixelHeight;
        double vw = Math.Max(100, Scroller.ActualWidth - 70), vh = Math.Max(100, Scroller.ActualHeight - 70);
        _fitZoom = Math.Min(Math.Min(vw / img.PixelWidth, vh / img.PixelHeight), 3);
        SetZoom(t.Zoom);
        double h = t.ScrollH, v = t.ScrollV;
        Dispatcher.BeginInvoke(() => { Scroller.ScrollToHorizontalOffset(h); Scroller.ScrollToVerticalOffset(v); }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void OnTabMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_vm == null || (sender as FrameworkElement)?.DataContext is not DocTab tab) return;
        if (e.ChangedButton == MouseButton.Middle) _vm.CloseTab(tab);
        else if (e.ChangedButton == MouseButton.Left) _vm.Activate(tab);
        Focus();
        e.Handled = true;
    }

    private void OnTabClose(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is DocTab tab) _vm?.CloseTab(tab);
        Focus();
    }

    private void OnTabsWheel(object sender, MouseWheelEventArgs e)
    {
        TabScroller.ScrollToHorizontalOffset(TabScroller.HorizontalOffset - e.Delta / 3.0);
        e.Handled = true;
    }

    // ------------------------------------------------------------------ the side panel: its width is dragged on its edge; folded to a slim bar
    private const double PanelNormal = 340, PanelMin = 260, PanelMax = 620, PanelFoldAt = 200, PanelSlim = 40;
    private double _dragWidth;

    private Neuraink.Core.AppSettings? Settings => MainViewModel.Current?.Settings;

    private void ApplyPanel()
    {
        if (Settings is not { } st) return;
        bool folded = st.SidePanelCollapsed;
        PanelCol.Width = new GridLength(folded ? PanelSlim : Math.Clamp(st.SidePanelWidth, PanelMin, PanelMax));
        PanelScroll.Visibility = folded ? Visibility.Collapsed : Visibility.Visible;
        PanelFolded.Visibility = folded ? Visibility.Visible : Visibility.Collapsed;
        _dragWidth = PanelCol.Width.Value;
    }

    private void OnFoldPanel(object sender, RoutedEventArgs e)
    {
        if (Settings is not { } st) return;
        st.SidePanelCollapsed = !st.SidePanelCollapsed;
        MainViewModel.Current?.SaveSettings();
        ApplyPanel();
        Focus();
    }

    private void OnPanelDrag(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        if (Settings is not { } st) return;
        // the panel is on the far side of its edge: dragging away from it makes it narrower (mirrored in Arabic as well)
        _dragWidth -= e.HorizontalChange;
        if (_dragWidth < PanelFoldAt)
        {
            if (!st.SidePanelCollapsed) { st.SidePanelCollapsed = true; ApplyPanel(); _dragWidth = PanelFoldAt - 1; }
            return;
        }
        if (st.SidePanelCollapsed) { st.SidePanelCollapsed = false; st.SidePanelWidth = PanelMin; ApplyPanel(); }
        double w = Math.Clamp(_dragWidth, PanelMin, PanelMax);
        PanelCol.Width = new GridLength(w);
        st.SidePanelWidth = w;
    }

    private void OnPanelDragDone(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        _dragWidth = PanelCol.Width.Value;
        MainViewModel.Current?.SaveSettings();
    }

    private void OnPanelReset(object sender, MouseButtonEventArgs e)
    {
        if (Settings is not { } st) return;
        st.SidePanelCollapsed = false;
        st.SidePanelWidth = PanelNormal;
        MainViewModel.Current?.SaveSettings();
        ApplyPanel();
        e.Handled = true;
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

    private void OnOpened() => Dispatcher.BeginInvoke(() => { ShowTabView(); Focus(); }, System.Windows.Threading.DispatcherPriority.Loaded);

    // ------------------------------------------------------------------ zoom
    private void SetZoom(double z)
    {
        _zoom = Math.Clamp(z, 0.1, 8);
        Zoom.ScaleX = Zoom.ScaleY = _zoom;
        if (_vm?.CurrentTab is { } tab && _vm.Image != null) tab.Zoom = _zoom;
        UpdateReference();
        Draw();
        Dispatcher.BeginInvoke(DrawRulers, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Zoom that keeps one point still (like Illustrator and Photoshop): the point under the mouse stays under the mouse while
    /// the picture grows or shrinks around it. <paramref name="at"/>: a point of the view (null: the middle of the view).
    /// </summary>
    private void ZoomAt(double z, Point? at)
    {
        if (_vm?.Image == null) { SetZoom(z); return; }
        var vp = at ?? new Point(Scroller.ViewportWidth / 2, Scroller.ViewportHeight / 2);
        var pic = Scroller.TranslatePoint(vp, Overlay);
        SetZoom(z);
        Scroller.UpdateLayout();
        var now = Overlay.TranslatePoint(pic, Scroller);
        Scroller.ScrollToHorizontalOffset(Scroller.HorizontalOffset + (now.X - vp.X));
        Scroller.ScrollToVerticalOffset(Scroller.VerticalOffset + (now.Y - vp.Y));
    }

    /// <summary>The mouse over the view, if it is there (for the zoom keys).</summary>
    private Point? MouseInView()
    {
        var p = Mouse.GetPosition(Scroller);
        return p.X >= 0 && p.Y >= 0 && p.X <= Scroller.ViewportWidth && p.Y <= Scroller.ViewportHeight ? p : null;
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

    private void OnZoomIn(object sender, RoutedEventArgs e) => ZoomAt(_zoom * 1.25, null);
    private void OnZoomOut(object sender, RoutedEventArgs e) => ZoomAt(_zoom / 1.25, null);
    private void OnZoomFit(object sender, RoutedEventArgs e) => Fit();

    private string? _angleFlash;
    private DateTime _angleFlashUntil;

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        if (_xDown && _vm != null)
        {
            // X + wheel: the brush size (bigger steps on big brushes)
            double sz = _vm.BrushSize, step = sz >= 40 ? 4 : sz >= 20 ? 2 : 1;
            _vm.BrushSize = Math.Clamp(sz + (e.Delta > 0 ? step : -step), 1, 400);
            Flash($"{_vm.BrushSize:0} px", e.GetPosition(Overlay));
            e.Handled = true;
            return;
        }
        if ((mods & ModifierKeys.Alt) != 0 && _vm?.IsBrushTool == true)
        {
            // Alt + wheel turns the brush: 5° a notch, 1° with Shift
            double step = (mods & ModifierKeys.Shift) != 0 ? 1 : 5;
            var a = _vm.RotateBrush(e.Delta > 0 ? step : -step);
            Flash($"{a:0}°", e.GetPosition(Overlay));
            e.Handled = true;
            return;
        }
        if ((mods & ModifierKeys.Control) != 0)
        {
            e.Handled = true;
            ZoomAt(_zoom * (e.Delta > 0 ? 1.15 : 1 / 1.15), e.GetPosition(Scroller));
            return;
        }
        if (_vm is { AdjustReference: true, IsArtboard: true })
        {
            _vm.ScaleReference(e.Delta > 0 ? 1.06 : 1 / 1.06, e.GetPosition(Overlay));
            e.Handled = true;
        }
    }

    /// <summary>A small label by the mouse for a moment (the brush angle or size).</summary>
    private void Flash(string text, Point at)
    {
        _angleFlash = text;
        _angleFlashUntil = DateTime.Now.AddMilliseconds(900);
        _mouse = at;
        DrawLive();
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(950) };
        t.Tick += (_, _) => { t.Stop(); DrawLive(); };
        t.Start();
    }

    private bool _xDown;

    /// <summary>Ctrl+Z gave the brush back as it was: what changed shows by the mouse.</summary>
    private void OnBrushReverted(string what)
    {
        var at = _mouse ?? (Overlay.IsMouseOver ? Mouse.GetPosition(Overlay) : new Point(Overlay.ActualWidth / 2, Overlay.ActualHeight / 2));
        Flash(what, at);
    }

    /// <summary>A stroke taken in hand: its brush is shown in the Brushes list.</summary>
    private void OnBrushFollowed(BrushItem? b)
    {
        if (b == null) return;
        Dispatcher.BeginInvoke(() => BrushListBox.ScrollIntoView(b), System.Windows.Threading.DispatcherPriority.Loaded);
        if (_mouse is { } m) Flash($"{b.Tip.Name} · {b.Tip.Angle:0}° · {b.Tip.Size:0} px", m);
    }

    // ------------------------------------------------------------------ input: pen tool (Illustrator style)
    private PenNode? _dragNode;
    private Point _downAt;
    private Point? _mouse;
    private bool _stroking;
    private (PenNode Node, string Part)? _edit;
    private (int K, string Part)? _fitEdit;
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
        if (_vm.IsDrawPen || _vm.IsCurvatureMode) p = _vm.Snap(p, 6 / _zoom);   // pen anchors and Curvature points snap to the guides
        if (_vm.Tool is "entry" or "exit") { _vm.Click(p, 10 / _zoom); return; }
        if (_vm is { AdjustReference: true, IsArtboard: true })
        {
            _refDrag = p;
            Overlay.CaptureMouse();
            e.Handled = true;
            return;
        }
        if (_vm.IsSelectMode) { SelectDown(p, e); return; }
        if (_vm.IsScissorsMode) { _vm.CutAt(p, 6 / _zoom); e.Handled = true; return; }
        if (_vm.IsKnifeMode) { _knife = [p]; Overlay.CaptureMouse(); e.Handled = true; return; }
        if (_vm.IsCurvatureMode) { CurvatureDown(p, e); return; }
        if (_vm.IsBrushTool)
        {
            // Ctrl + click: select a stroke to edit it (like the selection tool under Ctrl in Illustrator); on nothing: none
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                _vm.SelectStroke(_vm.HitStroke(p, 6 / _zoom));
                e.Handled = true;
                return;
            }
            // an anchor or a handle of the selected stroke: dragged
            if (_vm.HitFit(p, 7 / _zoom) is { } fh)
            {
                _vm.Checkpoint();
                _fitEdit = fh;
                Overlay.CaptureMouse();
                e.Handled = true;
                return;
            }
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
        if (_vm.IsSelectMode) { SelectMove(p, e); return; }
        if (_vm.IsKnifeMode || _vm.IsScissorsMode)
        {
            if (_knife != null && e.LeftButton == MouseButtonState.Pressed && (p - _knife[^1]).Length * _zoom > 2) _knife.Add(p);
            DrawLive();
            return;
        }
        if (_vm.IsCurvatureMode)
        {
            if (_curvDrag >= 0 && e.LeftButton == MouseButtonState.Pressed) _vm.MoveCurvPoint(_curvDrag, _vm.Snap(p, 6 / _zoom));
            else DrawLive();
            return;
        }
        if (_edit is { } ed && e.LeftButton == MouseButtonState.Pressed)
        {
            bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
            if (ed.Part == "anchor") _vm.MoveAnchor(ed.Node, p);
            else _vm.MoveHandle(ed.Node, ed.Part == "out", p, alt);
            return;
        }
        if (_fitEdit is { } fe && e.LeftButton == MouseButtonState.Pressed)
        {
            if (fe.Part == "anchor") _vm.MoveFitAnchor(fe.K, p);
            else _vm.MoveFitHandle(fe.K, fe.Part == "out", p, (Keyboard.Modifiers & ModifierKeys.Alt) != 0);
            return;
        }
        if (_stroking && e.LeftButton == MouseButtonState.Pressed) { _vm.ExtendStroke(p, Pressure(e), 0.6 / _zoom); return; }
        if (_vm.IsBrushTool) { DrawLive(); return; }   // nib preview follows the mouse
        if (_dragNode != null && e.LeftButton == MouseButtonState.Pressed)
        {
            if ((p - _downAt).Length * _zoom > 3) _vm.DragHandles(_dragNode, p, (Keyboard.Modifiers & ModifierKeys.Alt) != 0);
            return;
        }
        DrawLive();   // rubber band and pen cursor follow the mouse
    }

    private void OnOverlayUp(object sender, MouseButtonEventArgs e)
    {
        if (_stroking)
        {
            _stroking = false;
            _vm?.EndStroke(stay: (Keyboard.Modifiers & ModifierKeys.Shift) != 0, zoom: _zoom);
        }
        if (_fitEdit != null) { _fitEdit = null; _vm?.EndFitEdit(); }
        if (_curvDrag >= 0) { _curvDrag = -1; _vm?.EndCurvEdit(); }
        if (_vm?.IsSelectMode == true) SelectUp(e.GetPosition(Overlay));
        if (_knife != null)
        {
            var line = _knife;
            _knife = null;
            if (line.Count >= 2) _vm?.KnifeCut(line);
            DrawLive();
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
        DrawLive();
    }

    /// <summary>Right click: the next letter (Shift: the one before) — when a letter is finished, like turning a page.</summary>
    private void OnOverlayRight(object sender, MouseButtonEventArgs e)
    {
        Focus();
        if (_vm == null || _stroking) return;
        e.Handled = true;
        // on a drawing: its letter (to check what the cut gave, or change it); on nothing: the next letter
        var targets = _vm.TargetsAt(e.GetPosition(Overlay), 4 / _zoom);
        if (targets.Count > 0) { ShowLetterMenu(targets); return; }
        _vm.StepLetter((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? -1 : 1);
    }

    private List<Point>? _knife;

    /// <summary>
    /// The letters menu: the letters of the text as tiles (like the Letters panel), the one the drawing belongs to ticked;
    /// a row to make it the tashkeel of a letter, and an ornament button.
    /// </summary>
    private void ShowLetterMenu(List<Segment> targets)
    {
        if (_vm == null) return;
        var first = targets[0];
        string mode = first.Kind == "diacritic" ? "diacritic" : "stroke";
        bool strokes = targets.Any(t => t.Brush != null);
        var popup = new System.Windows.Controls.Primitives.Popup
        {
            Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint, StaysOpen = false, AllowsTransparency = true,
            PopupAnimation = System.Windows.Controls.Primitives.PopupAnimation.Fade,
        };
        var root = new StackPanel { Margin = new Thickness(12) };
        var card = new Border
        {
            Child = root, CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(Color.FromArgb(248, 32, 32, 36)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.5 },
            FlowDirection = FlowDirection,
        };
        popup.Child = card;
        root.Children.Add(new TextBlock
        {
            Text = targets.Count > 1 ? Loc.F("Menu.LetterOfN", targets.Count) : Loc.T("Menu.LetterOf"), Foreground = Brushes.White,
            Opacity = 0.75, FontSize = 12, Margin = new Thickness(2, 0, 2, 8),
        });
        var tiles = new WrapPanel { FlowDirection = FlowDirection.RightToLeft, MaxWidth = 380 };
        void Fill()
        {
            tiles.Children.Clear();
            foreach (var c in _vm.Chips)
            {
                int idx = c.Chip.Index;
                bool tick = first.Chip == idx && first.Kind == mode;
                var col = c.Color;
                var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                stack.Children.Add(new TextBlock { Text = c.Letter, FontSize = 22, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, FlowDirection = FlowDirection.RightToLeft });
                stack.Children.Add(new TextBlock { Text = c.FormText, FontSize = 10, Foreground = Brushes.White, Opacity = 0.75, HorizontalAlignment = HorizontalAlignment.Center });
                var grid = new Grid();
                grid.Children.Add(stack);
                if (tick) grid.Children.Add(new TextBlock { Text = "✓", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(col), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -4, -2, 0) });
                var b = new Button
                {
                    Content = grid, Width = 58, Height = 62, Margin = new Thickness(3), Padding = new Thickness(0), Cursor = Cursors.Hand,
                    Background = new SolidColorBrush(tick ? Color.FromArgb(80, col.R, col.G, col.B) : Color.FromArgb(28, 255, 255, 255)),
                    BorderBrush = new SolidColorBrush(col), BorderThickness = new Thickness(tick ? 2.5 : 1.5), Focusable = false,
                };
                b.Click += (_, _) => { popup.IsOpen = false; _vm.AssignLetter(targets, idx, mode); };
                tiles.Children.Add(b);
            }
        }
        if (strokes)
        {
            // letter or tashkeel of the letter
            var modes = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            foreach (var (key, kind) in new[] { ("Menu.AsLetter", "stroke"), ("Menu.AsTashkeel", "diacritic") })
            {
                var rb = new RadioButton { Content = Loc.T(key), IsChecked = mode == kind, Foreground = Brushes.White, Margin = new Thickness(2, 0, 14, 0), GroupName = "lettermode", Focusable = false };
                var k = kind;
                rb.Checked += (_, _) => { mode = k; Fill(); };
                modes.Children.Add(rb);
            }
            root.Children.Add(modes);
        }
        Fill();
        root.Children.Add(tiles);
        if (strokes)
        {
            var orn = new Button { Content = Loc.T("Menu.AsOrnament"), Margin = new Thickness(3, 10, 3, 0), Padding = new Thickness(12, 6, 12, 6), HorizontalAlignment = HorizontalAlignment.Stretch, Focusable = false };
            orn.Click += (_, _) => { popup.IsOpen = false; _vm.AssignLetter(targets, -1, "ornament"); };
            root.Children.Add(orn);
        }
        popup.PlacementTarget = Overlay;
        popup.IsOpen = true;
    }

    // ------------------------------------------------------------------ Selection tool (V): pick, move, scale, turn (Illustrator)
    private string? _tMode;          // move | scale | rotate | marquee
    private Point _tFrom;
    private Rect _tBox;
    private (double X, double Y) _tHandle;
    private bool _tMoved;

    /// <summary>The handle of the box under the mouse: (0|0.5|1, 0|0.5|1) — or a rotation zone just outside a corner.</summary>
    private (string Kind, double X, double Y)? BoxPart(Point p)
    {
        if (_vm == null || !_vm.HasSelection) return null;
        var b = _vm.SelectionBounds();
        if (b.IsEmpty) return null;
        double r = 6 / _zoom;
        foreach (var hx in new[] { 0, 0.5, 1 })
            foreach (var hy in new[] { 0, 0.5, 1 })
            {
                if (hx == 0.5 && hy == 0.5) continue;
                var h = new Point(b.X + b.Width * hx, b.Y + b.Height * hy);
                if ((h - p).Length <= r) return ("scale", hx, hy);
            }
        foreach (var hx in new[] { 0.0, 1 })
            foreach (var hy in new[] { 0.0, 1 })
            {
                var h = new Point(b.X + b.Width * hx, b.Y + b.Height * hy);
                double d = (h - p).Length * _zoom;
                if (d > 6 && d < 22 && !b.Contains(p)) return ("rotate", hx, hy);
            }
        return null;
    }

    private void SelectDown(Point p, MouseButtonEventArgs e)
    {
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        _tFrom = p;
        _tMoved = false;
        if (BoxPart(p) is { } part)
        {
            _tMode = part.Kind;
            _tHandle = (part.X, part.Y);
            _tBox = _vm!.SelectionBounds();
            _vm.BeginTransform();
        }
        else
        {
            var hit = _vm!.HitAny(p, 4 / _zoom);
            _vm.SelectedGuide = null;
            if (hit == null && _vm.HitGuide(p, 4 / _zoom) is { } g)
            {
                // a guide: moved; dragged off the picture it goes
                _vm.BeginGuideDrag();
                _vm.SelectedGuide = g;
                _guideDrag = g;
                _tMode = "guide";
                Overlay.CaptureMouse();
                e.Handled = true;
                Draw();
                return;
            }
            if (hit == null && _vm.HitReference(p))
            {
                // the reference picture (under the drawings): taken and moved like a drawing
                if (!_vm.ReferenceSelected || shift) _vm.SelectReference(shift);
                _tMode = "move"; _tBox = _vm.SelectionBounds(); _vm.BeginTransform();
            }
            else if (hit == null) { if (!shift) _vm.ClearSelection(); _tMode = "marquee"; }
            else
            {
                bool was = _vm.Selection.Contains(hit);
                if (!was || shift) _vm.SelectAt(p, 4 / _zoom, shift);
                if (_vm.Selection.Contains(hit)) { _tMode = "move"; _tBox = _vm.SelectionBounds(); _vm.BeginTransform(); }
                else _tMode = null;
            }
        }
        Overlay.CaptureMouse();
        e.Handled = true;
    }

    private void SelectMove(Point p, MouseEventArgs e)
    {
        if (_vm == null) return;
        if (e.LeftButton != MouseButtonState.Pressed || _tMode == null) { UpdateSelectCursor(p); DrawLive(); return; }
        if (!_tMoved && (p - _tFrom).Length * _zoom < 2) return;
        _tMoved = true;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0, alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
        var m = Matrix.Identity;
        switch (_tMode)
        {
            case "guide" when _guideDrag is { } gd: _vm.MoveGuide(gd, gd.Dir == 'h' ? p.Y : p.X); return;
            case "marquee": DrawLive(); return;
            case "move":
            {
                var d = p - _tFrom;
                if (shift) { if (Math.Abs(d.X) > Math.Abs(d.Y)) d.Y = 0; else d.X = 0; }
                d = _vm.SnapMove(_tBox, d, 6 / _zoom);
                m.Translate(d.X, d.Y);
                break;
            }
            case "scale":
            {
                var (hx, hy) = _tHandle;
                var anchor = alt ? new Point(_tBox.X + _tBox.Width / 2, _tBox.Y + _tBox.Height / 2)
                                 : new Point(_tBox.X + _tBox.Width * (1 - hx), _tBox.Y + _tBox.Height * (1 - hy));
                double sx = 1, sy = 1;
                if (hx != 0.5 && Math.Abs(_tFrom.X - anchor.X) > 1e-6) sx = (p.X - anchor.X) / (_tFrom.X - anchor.X);
                if (hy != 0.5 && Math.Abs(_tFrom.Y - anchor.Y) > 1e-6) sy = (p.Y - anchor.Y) / (_tFrom.Y - anchor.Y);
                if (shift)
                {
                    if (hx == 0.5) sx = sy;
                    else if (hy == 0.5) sy = sx;
                    else { var u = Math.Abs(sx) > Math.Abs(sy) ? sx : sy; sx = sy = u; }
                }
                sx = Math.Abs(sx) < 0.01 ? 0.01 * Math.Sign(sx == 0 ? 1 : sx) : sx;
                sy = Math.Abs(sy) < 0.01 ? 0.01 * Math.Sign(sy == 0 ? 1 : sy) : sy;
                m.Translate(-anchor.X, -anchor.Y);
                m.Scale(sx, sy);
                m.Translate(anchor.X, anchor.Y);
                break;
            }
            case "rotate":
            {
                var c = new Point(_tBox.X + _tBox.Width / 2, _tBox.Y + _tBox.Height / 2);
                double a = (Math.Atan2(p.Y - c.Y, p.X - c.X) - Math.Atan2(_tFrom.Y - c.Y, _tFrom.X - c.X)) * 180 / Math.PI;
                if (shift) a = Math.Round(a / 45) * 45;
                m.RotateAt(a, c.X, c.Y);
                Flash($"{(a % 360 + 360) % 360:0}°", p);
                break;
            }
        }
        _vm.ApplyTransform(m);
    }

    private void SelectUp(Point p)
    {
        if (_vm == null) return;
        if (_tMode == "guide" && _guideDrag is { } gd)
        {
            double pos = gd.Dir == 'h' ? p.Y : p.X, max = gd.Dir == 'h' ? Overlay.Height : Overlay.Width;
            if (pos < 0 || pos > max) _vm.RemoveGuide(gd, checkpoint: false);
            _guideDrag = null;
        }
        else if (_tMode == "marquee" && _tMoved)
            _vm.SelectIn(new Rect(_tFrom, p), (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
        else if (_tMode is "move" or "scale" or "rotate") _vm.EndTransform();
        _tMode = null;
        DrawLive();
    }

    private void UpdateSelectCursor(Point p)
    {
        if (_space || _panning) return;
        var part = BoxPart(p);
        Overlay.Cursor = part switch
        {
            { Kind: "rotate" } => Cursors.None,   // a turning arrow is drawn by the mouse
            { Kind: "scale", X: 0.5 } => Cursors.SizeNS,
            { Kind: "scale", Y: 0.5 } => Cursors.SizeWE,
            { Kind: "scale" } k => (k.X == k.Y) ? Cursors.SizeNWSE : Cursors.SizeNESW,
            _ => Cursors.Arrow,
        };
        _rotateCursor = part is { Kind: "rotate" };
    }

    private bool _rotateCursor;
    private (char Dir, int Index)? _guideDrag;

    // ------------------------------------------------------------------ rulers and guides (Ctrl+R, like Illustrator)
    private char? _guideNew;
    private double _guideNewPos;

    private void OnScrolled(object sender, ScrollChangedEventArgs e) => DrawRulers();

    /// <summary>A guide pulled out of a ruler: the top one gives a horizontal guide, the left one a vertical guide.</summary>
    private void OnRulerDown(object sender, MouseButtonEventArgs e)
    {
        if (_vm?.Image == null || sender is not Canvas c) return;
        _guideNew = (c.Tag as string) == "h" ? 'h' : 'v';
        if (!_vm.ShowGuides) _vm.ShowGuides = true;
        c.CaptureMouse();
        e.Handled = true;
    }

    private void OnRulerMove(object sender, MouseEventArgs e)
    {
        if (_guideNew is not { } dir || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(Overlay);
        _guideNewPos = dir == 'h' ? p.Y : p.X;
        _mouse = p;
        Flash($"{_guideNewPos:0} px", p);
    }

    private void OnRulerUp(object sender, MouseButtonEventArgs e)
    {
        if (_guideNew is not { } dir || sender is not Canvas c) return;
        c.ReleaseMouseCapture();
        _guideNew = null;
        // dropped on the picture's view (not back on the ruler): the guide stays
        var v = e.GetPosition(Scroller);
        bool onView = v.X > RulerLeft.ActualWidth && v.Y > RulerTop.ActualHeight && v.X < Scroller.ActualWidth && v.Y < Scroller.ActualHeight;
        if (onView && _vm != null) _vm.AddGuide(dir, _guideNewPos);
        DrawLive();
    }

    /// <summary>The rulers: ticks in picture pixels, following the zoom and the scroll, and a mark where the mouse is.</summary>
    private void DrawRulers()
    {
        if (_vm?.Image == null || !_vm.ShowRulers || RulerTop.ActualWidth <= 0) { RulerTop.Children.Clear(); RulerLeft.Children.Clear(); return; }
        RulerTop.Children.Clear();
        RulerLeft.Children.Clear();
        // a tick every 1, 2 or 5 × 10ⁿ pixels, at least 60 screen pixels apart for the numbered ones
        double major = 1;
        foreach (var k in new[] { 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 2000, 5000 }) { major = k; if (k * _zoom >= 60) break; }
        double minor = major / 10;
        var tick = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255));
        void Axis(Canvas c, bool horizontal)
        {
            double len = horizontal ? c.ActualWidth : c.ActualHeight;
            var a = c.TranslatePoint(new Point(0, 0), Overlay);
            var b = c.TranslatePoint(horizontal ? new Point(len, 0) : new Point(0, len), Overlay);
            double from = horizontal ? a.X : a.Y, to = horizontal ? b.X : b.Y;
            for (double v = Math.Floor(from / minor) * minor; v <= to; v += minor)
            {
                var sp = Overlay.TranslatePoint(horizontal ? new Point(v, 0) : new Point(0, v), c);
                double at = horizontal ? sp.X : sp.Y;
                bool big = Math.Abs(v / major - Math.Round(v / major)) < 1e-6;
                bool mid = !big && Math.Abs(v / (major / 2) - Math.Round(v / (major / 2))) < 1e-6;
                double h = big ? 18 : mid ? 8 : 4;
                var line = horizontal
                    ? new Shapes.Line { X1 = at, X2 = at, Y1 = 18 - h, Y2 = 18, Stroke = tick, StrokeThickness = 1 }
                    : new Shapes.Line { Y1 = at, Y2 = at, X1 = 18 - h, X2 = 18, Stroke = tick, StrokeThickness = 1 };
                c.Children.Add(line);
                if (big)
                {
                    var t = new TextBlock { Text = v.ToString("0"), FontSize = 9, Foreground = tick };
                    if (horizontal) { Canvas.SetLeft(t, at + 2); Canvas.SetTop(t, 0); }
                    else { t.LayoutTransform = new RotateTransform(-90); Canvas.SetLeft(t, 0); Canvas.SetTop(t, at - 2 - t.Text.Length * 5.5); }
                    c.Children.Add(t);
                }
            }
            if (_mouse is { } m)
            {
                var sp = Overlay.TranslatePoint(m, c);
                double at = horizontal ? sp.X : sp.Y;
                c.Children.Add(horizontal
                    ? new Shapes.Line { X1 = at, X2 = at, Y1 = 0, Y2 = 18, Stroke = new SolidColorBrush(Accent), StrokeThickness = 1 }
                    : new Shapes.Line { Y1 = at, Y2 = at, X1 = 0, X2 = 18, Stroke = new SolidColorBrush(Accent), StrokeThickness = 1 });
            }
        }
        Axis(RulerTop, true);
        Axis(RulerLeft, false);
    }

    // ------------------------------------------------------------------ Curvature tool (Shift+~)
    private int _curvDrag = -1;

    private void CurvatureDown(Point p, MouseButtonEventArgs e)
    {
        var vm = _vm!;
        e.Handled = true;
        if (vm.CurvDraft.Count > 0)
        {
            if (vm.IsOnFirstCurvPoint(p, 8 / _zoom)) { vm.FinishCurvDraft(close: true); return; }
            if (e.ClickCount >= 2) vm.CornerLastCurvPoint(); else vm.AddCurvPoint(p, false);
            return;
        }
        if (vm.SelectedStroke is { } sel)
        {
            double pathTol = Math.Max(6 / _zoom, (sel.Brush?.Size ?? 0) / 2);
            if (vm.HitCurv(p, 7 / _zoom, pathTol) is { } h)
            {
                if (h.Part == "anchor" && e.ClickCount >= 2) { vm.ToggleCorner(h.K); return; }
                vm.BeginCurvEdit(h.K);
                _curvDrag = h.K;
                Overlay.CaptureMouse();
                return;
            }
        }
        if (vm.HitStroke(p, 6 / _zoom) is { } other) { vm.SelectStroke(other); vm.CurvPoint = -1; return; }
        vm.AddCurvPoint(p, false);
    }

    // ------------------------------------------------------------------ the stroke being drawn: only its new part is drawn
    private StrokeGeometry.NibSweep? _sweep;
    private Shapes.Path? _livePath;

    private void OnLiveStroke()
    {
        if (_vm == null || _sweep == null || _livePath == null) { DrawLive(); return; }
        for (int i = _sweep.Count; i < _vm.CurrentStroke.Count; i++) _sweep.Add(_vm.CurrentStroke[i]);
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

    private Window? _keysFrom;

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (_vm == null || !IsVisible || Keyboard.FocusedElement is TextBox || Keyboard.FocusedElement is ComboBox) return;
        if (Keyboard.FocusedElement is DependencyObject fe && Window.GetWindow(fe) is { } fw && fw != _keysFrom) return;   // a dialog
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        // Ctrl+; guides shown / hidden · Ctrl+Alt+; locked (with Alt the key comes as a "system" key)
        if (ctrl && (e.Key == Key.Oem1 || e.Key == Key.System && e.SystemKey == Key.Oem1))
        {
            if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) _vm.GuidesLocked = !_vm.GuidesLocked; else _vm.ShowGuides = !_vm.ShowGuides;
            e.Handled = true;
            return;
        }
        switch (e.Key)
        {
            case Key.R when ctrl: _vm.ShowRulers = !_vm.ShowRulers; Dispatcher.BeginInvoke(DrawRulers, System.Windows.Threading.DispatcherPriority.Loaded); break;
            case Key.F2 when _vm.IsBrushTool: _vm.RenameBrushCommand.Execute(null); break;
            case Key.System when e.SystemKey is Key.LeftAlt or Key.RightAlt: break;   // Alt alone: no menu focus
            case Key.Space:
                if (!_space) { _space = true; SetHand(true); }
                break;
            case Key.X: _xDown = true; break;
            case Key.V when !ctrl: _vm.UseSelect(); break;
            case Key.C when !ctrl: _vm.UseScissors(); break;
            case Key.K when !ctrl: _vm.UseKnife(); break;
            case Key.P when !ctrl: _vm.UsePenCommand.Execute(null); break;
            case Key.B when !ctrl: _vm.UseBrushCommand.Execute(null); break;
            case Key.Oem3 when (Keyboard.Modifiers & ModifierKeys.Shift) != 0: _vm.UseCurvature(); break;
            case Key.Left or Key.Right or Key.Up or Key.Down when _vm.IsSelectMode && _vm.HasSelection:
            {
                double step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 10 : 1;
                _vm.Nudge(e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0, e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0);
                break;
            }
            case Key.Enter when _vm.CurvDraft.Count > 0: _vm.FinishCurvDraft(); break;
            case Key.Enter: _vm.CloseShape(); break;
            case Key.Back or Key.Delete when _vm.IsSelectMode && _vm.SelectedGuide is { } sg: _vm.RemoveGuide(sg); break;
            case Key.Back when _vm.IsSelectMode: _vm.DeleteSelection(); break;
            case Key.Back when _vm.IsCurvatureMode && _vm.CurvPoint >= 0: _vm.DeleteCurvPoint(); break;
            case Key.Back: _vm.BackspacePoint(); break;
            case Key.Escape when _vm.CurvDraft.Count > 0: _vm.FinishCurvDraft(); break;
            case Key.Escape when _vm.IsSelectMode || _vm.IsCurvatureMode: _vm.ClearSelection(); break;
            case Key.Escape: _vm.CancelDrawing(); break;
            case Key.Tab when ctrl: _vm.CycleTab((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? -1 : 1); break;
            case Key.W when ctrl: _vm.CloseTab(null); break;
            case Key.Tab: _vm.SkipStep(); break;
            case Key.Delete:
                if (!(_vm.IsSelectMode ? _vm.DeleteSelection() : _vm.IsCurvatureMode && _vm.CurvPoint >= 0 ? _vm.DeleteCurvPoint() : _vm.DeleteSelectedStroke())) return;
                break;
            case Key.Z when ctrl && (Keyboard.Modifiers & ModifierKeys.Shift) != 0: _vm.RedoStep(); break;
            case Key.Z when ctrl: _vm.UndoStep(); break;
            case Key.Y when ctrl: _vm.RedoStep(); break;
            case Key.OemOpenBrackets: _vm.BrushSize = Math.Max(2, _vm.BrushSize - (_vm.BrushSize > 20 ? 4 : 1)); break;
            case Key.OemCloseBrackets: _vm.BrushSize = _vm.BrushSize + (_vm.BrushSize >= 20 ? 4 : 1); break;
            case Key.OemPlus or Key.Add when ctrl: ZoomAt(_zoom * 1.25, MouseInView()); break;
            case Key.OemMinus or Key.Subtract when ctrl: ZoomAt(_zoom / 1.25, MouseInView()); break;
            case Key.D0 or Key.NumPad0 when ctrl: Fit(); break;
            case Key.S when ctrl: _vm.SaveInPlace(); break;
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
        if (e.Key == Key.X) { _xDown = false; e.Handled = true; return; }
        if (e.Key != Key.Space) return;
        _space = false;
        if (!_panning) SetHand(false);
        e.Handled = true;
    }

    // ------------------------------------------------------------------ painting
    /// <summary>The whole picture: the regions and strokes (only when they change), then what follows the mouse.</summary>
    private void Draw()
    {
        DrawScene();
        DrawLive();
    }

    private static Color Accent => ThemeService.AccentColor;

    private void DrawScene()
    {
        Overlay.Children.Clear();
        if (_vm?.Image is not { } img) { Live.Children.Clear(); return; }
        Overlay.Width = Live.Width = img.PixelWidth;
        Overlay.Height = Live.Height = img.PixelHeight;
        double px = 1 / _zoom;   // one screen pixel in picture units
        if (!_space && !_panning)
            Overlay.Cursor = _vm.AdjustReference && _vm.IsArtboard ? Cursors.SizeAll
                : _vm.IsSelectMode ? Cursors.Arrow
                : _vm.IsCurvatureMode ? Cursors.Pen
                : _vm.IsScissorsMode || _vm.IsKnifeMode ? Cursors.Cross
                : _vm.Tool is "entry" or "exit" ? Cursors.Cross : Cursors.None;

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
        var accent = ThemeService.AccentColor;
        var ab = new SolidColorBrush(accent);
        // the stroke picked for editing (Ctrl + click, or the Curvature tool): its outline and points
        if (_vm.SelectedStroke is { } sel)
        {
            Overlay.Children.Add(new Shapes.Path { Data = StrokeGeometry.ForSegment(sel), Stroke = ab, StrokeThickness = 1.5 * px, IsHitTestVisible = false });
            if (sel.Fit is { Count: > 0 } fit)
            {
                Overlay.Children.Add(new Shapes.Path { Data = Spine(fit), Stroke = ab, StrokeThickness = 1 * px, IsHitTestVisible = false });
                bool curv = _vm.IsCurvatureMode;
                for (int k = 0; k <= fit.Count; k++)
                {
                    var a = AnnotateViewModel.AnchorOf(fit, k);
                    if (!curv)
                    {
                        if (k < fit.Count) Handle(a, new Point(fit[k][2], fit[k][3]));
                        if (k > 0) Handle(a, new Point(fit[k - 1][4], fit[k - 1][5]));
                        Square(a.X, a.Y, 3.5 * px, Colors.White, accent, px);
                    }
                    else
                    {
                        // Curvature: smooth points round, corners square; the point in hand filled
                        var fill = k == _vm.CurvPoint ? accent : Colors.White;
                        if (AnnotateViewModel.IsCorner(fit, k)) Square(a.X, a.Y, 3.5 * px, fill, accent, px);
                        else Dot(a.X, a.Y, 3.8 * px, fill, px * 0.75, accent);
                    }
                }
                void Handle(Point a, Point h)
                {
                    if ((h - a).Length < 0.5 * px) return;
                    Overlay.Children.Add(new Shapes.Line { X1 = a.X, Y1 = a.Y, X2 = h.X, Y2 = h.Y, Stroke = ab, StrokeThickness = 1 * px, IsHitTestVisible = false });
                    Dot(h.X, h.Y, 3 * px, accent, px, accent);
                }
            }
        }
        // guides (cyan, like Illustrator), longer than the picture
        if (_vm.ShowGuides)
        {
            var gb = new SolidColorBrush(Color.FromRgb(0x29, 0xB6, 0xF6));
            var gs = new SolidColorBrush(accent);
            double w = img.PixelWidth, h = img.PixelHeight;
            for (int i = 0; i < _vm.GuidesH.Count; i++)
            {
                bool gsel = _vm.SelectedGuide is { Dir: 'h' } sg && sg.Index == i;
                Overlay.Children.Add(new Shapes.Line { X1 = -w, X2 = 2 * w, Y1 = _vm.GuidesH[i], Y2 = _vm.GuidesH[i], Stroke = gsel ? gs : gb, StrokeThickness = (gsel ? 1.6 : 1) * px, IsHitTestVisible = false });
            }
            for (int i = 0; i < _vm.GuidesV.Count; i++)
            {
                bool gsel = _vm.SelectedGuide is { Dir: 'v' } sg && sg.Index == i;
                Overlay.Children.Add(new Shapes.Line { Y1 = -h, Y2 = 2 * h, X1 = _vm.GuidesV[i], X2 = _vm.GuidesV[i], Stroke = gsel ? gs : gb, StrokeThickness = (gsel ? 1.6 : 1) * px, IsHitTestVisible = false });
            }
        }
        // the last cut (Scissors / Knife): a small mark
        if (_vm.LastCut is { } lc)
        {
            double r = 5 * px;
            Overlay.Children.Add(new Shapes.Line { X1 = lc.X - r, Y1 = lc.Y - r, X2 = lc.X + r, Y2 = lc.Y + r, Stroke = ab, StrokeThickness = 2 * px, IsHitTestVisible = false });
            Overlay.Children.Add(new Shapes.Line { X1 = lc.X - r, Y1 = lc.Y + r, X2 = lc.X + r, Y2 = lc.Y - r, Stroke = ab, StrokeThickness = 2 * px, IsHitTestVisible = false });
        }
        // the Selection tool: each picked drawing outlined, and one box with its eight handles
        if (_vm.IsSelectMode && _vm.HasSelection)
        {
            foreach (var s in _vm.Selection)
                Overlay.Children.Add(new Shapes.Path { Data = AnnotateViewModel.ShapeOf(s), Stroke = ab, StrokeThickness = 1.2 * px, IsHitTestVisible = false });
            var b = _vm.SelectionBounds();
            if (!b.IsEmpty)
            {
                var box = new Shapes.Rectangle { Width = b.Width, Height = b.Height, Stroke = ab, StrokeThickness = 1 * px, IsHitTestVisible = false };
                Canvas.SetLeft(box, b.X); Canvas.SetTop(box, b.Y);
                Overlay.Children.Add(box);
                foreach (var hx in new[] { 0, 0.5, 1 })
                    foreach (var hy in new[] { 0, 0.5, 1 })
                        if (!(hx == 0.5 && hy == 0.5)) Square(b.X + b.Width * hx, b.Y + b.Height * hy, 3.5 * px, Colors.White, accent, px);
            }
        }
    }

    private static PathGeometry Spine(List<double[]> fit)
    {
        var g = new PathGeometry();
        var fig = new PathFigure { StartPoint = new Point(fit[0][0], fit[0][1]) };
        foreach (var c0 in fit) fig.Segments.Add(new BezierSegment(new Point(c0[2], c0[3]), new Point(c0[4], c0[5]), new Point(c0[6], c0[7]), true));
        g.Figures.Add(fig);
        return g;
    }

    /// <summary>What follows the mouse, drawn alone on its own layer (cheap at every move).</summary>
    private void DrawLive()
    {
        if (_vm?.ShowRulers == true) DrawRulers();
        Live.Children.Clear();
        _livePath = null;
        _sweep = null;
        if (_vm?.Image == null) return;
        double px = 1 / _zoom;
        var target = Live.Children;

        // the stroke being drawn: exactly where the hand goes; new points are added to it as they come (OnLiveStroke)
        if (_vm.CurrentStroke.Count > 0)
        {
            var c = _vm.BrushColor;
            _sweep = new StrokeGeometry.NibSweep(_vm.Tip);
            foreach (var q in _vm.CurrentStroke) _sweep.Add(q);
            _livePath = new Shapes.Path
            {
                Data = _sweep.Group,
                Fill = new SolidColorBrush(Color.FromArgb((byte)(255 * Math.Clamp(_vm.Tip.Opacity, 0.05, 1)), c.R, c.G, c.B)),
                IsHitTestVisible = false,
            };
            target.Add(_livePath);
        }
        if (_vm.IsDrawBrush && _mouse is { } nib && !_stroking && !_space && !_vm.AdjustReference)
        {
            // the nib under the mouse: its size, angle and roundness before drawing
            var c = _vm.BrushColor;
            target.Add(new Shapes.Path
            {
                Data = StrokeGeometry.Build([[nib.X, nib.Y, 0]], _vm.Tip),
                Stroke = new SolidColorBrush(c), StrokeThickness = 1.2 * px,
                Fill = new SolidColorBrush(Color.FromArgb(60, c.R, c.G, c.B)),
                IsHitTestVisible = false,
            });
        }

        // Curvature: the new stroke so far, bending to the mouse
        if (_vm.IsCurvatureMode && _vm.CurvDraft.Count > 0)
        {
            var pts = _vm.CurvDraft.ToList();
            if (_mouse is { } mm && !_vm.IsOnFirstCurvPoint(mm, 8 * px)) pts.Add((mm, false));
            var fit = AnnotateViewModel.CurvFit(pts);
            var c = _vm.BrushColor;
            if (fit.Count > 0)
            {
                target.Add(new Shapes.Path
                {
                    Data = StrokeGeometry.Build(StrokeGeometry.SampleFit(fit), _vm.Tip),
                    Fill = new SolidColorBrush(Color.FromArgb(150, c.R, c.G, c.B)), IsHitTestVisible = false,
                });
                target.Add(new Shapes.Path { Data = Spine(fit), Stroke = new SolidColorBrush(Accent), StrokeThickness = 1 * px, IsHitTestVisible = false });
            }
            for (int i = 0; i < _vm.CurvDraft.Count; i++)
            {
                var (pt, corner) = _vm.CurvDraft[i];
                if (corner) Square(pt.X, pt.Y, 3.5 * px, Colors.White, Accent, px, target);
                else Dot(pt.X, pt.Y, 3.8 * px, i == 0 ? Accent : Colors.White, px * 0.75, Accent, target);
            }
        }

        // a guide being pulled out of a ruler
        if (_guideNew is { } gn)
        {
            double w = Live.Width, h = Live.Height;
            target.Add(gn == 'h'
                ? new Shapes.Line { X1 = -w, X2 = 2 * w, Y1 = _guideNewPos, Y2 = _guideNewPos, Stroke = new SolidColorBrush(Color.FromRgb(0x29, 0xB6, 0xF6)), StrokeThickness = px, IsHitTestVisible = false }
                : new Shapes.Line { Y1 = -h, Y2 = 2 * h, X1 = _guideNewPos, X2 = _guideNewPos, Stroke = new SolidColorBrush(Color.FromRgb(0x29, 0xB6, 0xF6)), StrokeThickness = px, IsHitTestVisible = false });
        }
        // Knife: the line being drawn
        if (_knife is { Count: > 1 } kl)
        {
            var pl = new Shapes.Polyline { Stroke = new SolidColorBrush(Accent), StrokeThickness = 1.5 * px, StrokeDashArray = new DoubleCollection { 3, 2 }, IsHitTestVisible = false };
            foreach (var q in kl) pl.Points.Add(q);
            target.Add(pl);
        }
        // Selection tool: the marquee, and the turning arrow outside a corner
        if (_vm.IsSelectMode && _tMode == "marquee" && _tMoved && _mouse is { } mq)
        {
            var r = new Rect(_tFrom, mq);
            var box = new Shapes.Rectangle
            {
                Width = r.Width, Height = r.Height, Stroke = new SolidColorBrush(Accent), StrokeThickness = 1 * px,
                StrokeDashArray = new DoubleCollection { 4, 3 }, Fill = new SolidColorBrush(Color.FromArgb(24, Accent.R, Accent.G, Accent.B)), IsHitTestVisible = false,
            };
            Canvas.SetLeft(box, r.X); Canvas.SetTop(box, r.Y);
            target.Add(box);
        }
        if (_vm.IsSelectMode && _rotateCursor && _mouse is { } rc && _tMode == null)
        {
            var t = new TextBlock { Text = "\u21BB", FontSize = 20 * px, Foreground = Brushes.White, IsHitTestVisible = false,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 3, ShadowDepth = 0, Opacity = 1, Color = Colors.Black } };
            Canvas.SetLeft(t, rc.X - 8 * px); Canvas.SetTop(t, rc.Y - 13 * px);
            target.Add(t);
        }

        if (_vm.Drawing.Count > 0)
        {
            var c = _vm.Tool == "exclude" ? AnnotateViewModel.ExcludeColor : AnnotateViewModel.ChipColor(_vm.Active);
            var brush = new SolidColorBrush(c);
            // the path so far (curves where anchors have handles), then a thin line to the mouse
            var line = AnnotateViewModel.Flatten(_vm.Drawing, closed: false);
            target.Add(new Shapes.Polyline
            {
                Points = new PointCollection(line), Stroke = brush, StrokeThickness = 2 * px,
                StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false,
            });
            if (_mouse is { } m && _dragNode == null)
                target.Add(new Shapes.Line
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
                    target.Add(new Shapes.Line { X1 = n.A.X, Y1 = n.A.Y, X2 = hp.X, Y2 = hp.Y, Stroke = brush, StrokeThickness = 1 * px, IsHitTestVisible = false });
                    Dot(hp.X, hp.Y, 3 * px, c, px, c, target);
                }
                Square(n.A.X, n.A.Y, (i == 0 ? 5 : 3.5) * px, i == 0 ? Colors.White : c, c, px, target);
            }
        }
        if (_vm.IsDrawPen && _mouse is { } pm && !_space && !_panning && _dragNode == null && !_vm.AdjustReference) PenCursor(pm, px);
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
            target.Add(label);
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
        Live.Children.Add(new Shapes.Path { Data = g, Fill = Brushes.Black, Stroke = Brushes.White, StrokeThickness = 1.1 * px, IsHitTestVisible = false });
    }

    /// <summary>An anchor: a small square, like Illustrator.</summary>
    private void Square(double x, double y, double r, Color fill, Color stroke, double px, UIElementCollection? into = null)
    {
        var e = new Shapes.Rectangle
        {
            Width = 2 * r, Height = 2 * r, Fill = new SolidColorBrush(fill), Stroke = new SolidColorBrush(stroke),
            StrokeThickness = 1.5 * px, IsHitTestVisible = false,
        };
        Canvas.SetLeft(e, x - r);
        Canvas.SetTop(e, y - r);
        (into ?? Overlay.Children).Add(e);
    }

    private void Dot(double x, double y, double r, Color fill, double px, Color? stroke = null, UIElementCollection? into = null)
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
        (into ?? Overlay.Children).Add(e);
    }
}
