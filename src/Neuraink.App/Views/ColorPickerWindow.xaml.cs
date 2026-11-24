using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Neuraink.App.Services;
using Neuraink.App.ViewModels;

namespace Neuraink.App.Views;

/// <summary>
/// The program's colour picker: saturation / value square, hue bar, HEX and R/G/B boxes, new / current preview,
/// a palette and the last picked colours. Follows the theme and the accent.
/// </summary>
public partial class ColorPickerWindow : Window
{
    public sealed record SwatchItem(string Hex, Brush Brush);

    /// <summary>Dark document colours first (invoice headers), then the Windows accent palette.</summary>
    private static readonly string[] DocumentColors =
        ["#002060", "#1F3864", "#1F4E79", "#203864", "#385723", "#7F6000", "#C00000", "#000000"];

    private double _h, _s = 1, _v = 1;
    private Color _initial;
    private bool _sync;
    private bool _dragSv, _dragHue;

    public Color Selected { get; private set; }

    public ColorPickerWindow(Color? initial)
    {
        InitializeComponent();
        _initial = initial ?? Color.FromRgb(0x00, 0x20, 0x60);
        OldPreview.Background = new SolidColorBrush(_initial);
        PaletteList.ItemsSource = DocumentColors.Concat(AccentService.Palette).Distinct(StringComparer.OrdinalIgnoreCase).Select(Item).ToList();
        var recent = MainViewModel.Current?.Settings.RecentColors ?? [];
        RecentList.ItemsSource = recent.Select(Item).ToList();
        RecentPanel.Visibility = recent.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SetColor(_initial, null);
        Loaded += (_, _) =>
        {
            // the accent of the program also on this window (OK button, focus)
            AccentService.Apply(ThemeService.Accent, ThemeService.Current == Neuraink.Core.AppTheme.Light);
            HexBox.Focus();
            HexBox.SelectAll();
        };
    }

    private static SwatchItem Item(string hex)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        var b = new SolidColorBrush(c);
        b.Freeze();
        return new SwatchItem(hex.ToUpperInvariant(), b);
    }

    // ------------------------------------------------------------------ state
    /// <summary>Shows a colour; <paramref name="from"/> is the box being typed in (left untouched).</summary>
    private void SetColor(Color c, object? from, bool keepHue = false)
    {
        var (h, s, v) = ToHsv(c);
        if (!keepHue && s > 0 && v > 0) _h = h;
        _s = s;
        _v = v;
        Show(c, from);
    }

    private void Show(Color c, object? from)
    {
        Selected = c;
        _sync = true;
        try
        {
            if (!ReferenceEquals(from, HexBox)) HexBox.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            if (!ReferenceEquals(from, RBox)) RBox.Text = c.R.ToString(CultureInfo.InvariantCulture);
            if (!ReferenceEquals(from, GBox)) GBox.Text = c.G.ToString(CultureInfo.InvariantCulture);
            if (!ReferenceEquals(from, BBox)) BBox.Text = c.B.ToString(CultureInfo.InvariantCulture);
        }
        finally { _sync = false; }
        NewPreview.Background = new SolidColorBrush(c);
        HueFill.Background = new SolidColorBrush(FromHsv(_h, 1, 1));
        Canvas.SetLeft(SvThumb, _s * SvArea.Width - SvThumb.Width / 2);
        Canvas.SetTop(SvThumb, (1 - _v) * SvArea.Height - SvThumb.Height / 2);
        Canvas.SetTop(HueThumb, _h / 360 * HueArea.Height - HueThumb.Height / 2);
        SvThumb.Stroke = _v > 0.6 && _s < 0.4 ? Brushes.Black : Brushes.White;
    }

    // ------------------------------------------------------------------ square & hue bar
    private void OnSvDown(object sender, MouseButtonEventArgs e) { _dragSv = SvArea.CaptureMouse(); PickSv(e.GetPosition(SvArea)); }
    private void OnSvMove(object sender, MouseEventArgs e) { if (_dragSv) PickSv(e.GetPosition(SvArea)); }
    private void OnSvUp(object sender, MouseButtonEventArgs e) { _dragSv = false; SvArea.ReleaseMouseCapture(); }

    private void PickSv(Point p)
    {
        _s = Math.Clamp(p.X / SvArea.Width, 0, 1);
        _v = 1 - Math.Clamp(p.Y / SvArea.Height, 0, 1);
        Show(FromHsv(_h, _s, _v), null);
    }

    private void OnHueDown(object sender, MouseButtonEventArgs e) { _dragHue = HueArea.CaptureMouse(); PickHue(e.GetPosition(HueArea)); }
    private void OnHueMove(object sender, MouseEventArgs e) { if (_dragHue) PickHue(e.GetPosition(HueArea)); }
    private void OnHueUp(object sender, MouseButtonEventArgs e) { _dragHue = false; HueArea.ReleaseMouseCapture(); }

    private void PickHue(Point p)
    {
        _h = Math.Clamp(p.Y / HueArea.Height, 0, 1) * 360;
        if (_h >= 360) _h = 359.9;
        Show(FromHsv(_h, _s, _v), null);
    }

    // ------------------------------------------------------------------ boxes & swatches
    private void OnHexChanged(object sender, TextChangedEventArgs e)
    {
        if (_sync) return;
        if (TryParseHex(HexBox.Text, out var c)) SetColor(c, HexBox);
    }

    private void OnHexLostFocus(object sender, RoutedEventArgs e) => Show(Selected, null);

    private void OnRgbChanged(object sender, TextChangedEventArgs e)
    {
        if (_sync || RBox == null || GBox == null || BBox == null) return;
        if (Byte(RBox.Text) is { } r && Byte(GBox.Text) is { } g && Byte(BBox.Text) is { } b)
            SetColor(Color.FromRgb(r, g, b), sender);
    }

    private static byte? Byte(string s) =>
        int.TryParse(s.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var v) && v is >= 0 and <= 255 ? (byte)v : null;

    private void OnSwatchClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SwatchItem s && TryParseHex(s.Hex, out var c)) SetColor(c, null);
    }

    private void OnOldClick(object sender, MouseButtonEventArgs e) => SetColor(_initial, null);

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (TryParseHex(HexBox.Text, out var typed)) Selected = typed;
        Remember(Selected);
        DialogResult = true;
    }

    private static void Remember(Color c)
    {
        if (MainViewModel.Current is not { } main) return;
        var hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        var list = main.Settings.RecentColors;
        list.RemoveAll(x => string.Equals(x, hex, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, hex);
        if (list.Count > 10) list.RemoveRange(10, list.Count - 10);
        main.SaveSettings();
    }

    // ------------------------------------------------------------------ colour maths
    /// <summary>#RGB, #RRGGBB or RRGGBB (also with spaces).</summary>
    public static bool TryParseHex(string? text, out Color c)
    {
        c = default;
        var s = (text ?? "").Trim().TrimStart('#').Replace(" ", "");
        if (s.Length == 3) s = string.Concat(s.Select(ch => $"{ch}{ch}"));
        if (s.Length != 6 || !int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return false;
        c = Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    private static (double H, double S, double V) ToHsv(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double h = 0;
        if (d > 0)
        {
            if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
        }
        if (h < 0) h += 360;
        return (h, max == 0 ? 0 : d / max, max);
    }

    private static Color FromHsv(double h, double s, double v)
    {
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        var (r, g, b) = h switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        static byte B(double d) => (byte)Math.Round(Math.Clamp(d, 0, 1) * 255);
        return Color.FromRgb(B(r + m), B(g + m), B(b + m));
    }
}
