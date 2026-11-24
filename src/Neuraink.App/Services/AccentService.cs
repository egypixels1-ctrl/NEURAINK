using System.Windows;
using System.Windows.Media;

namespace Neuraink.App.Services;

/// <summary>
/// Accent colour of the program (Settings → Appearance), like Windows "Choose your accent color".
/// "auto" follows the Windows accent; any #RRGGBB overrides the Fluent accent resources for both Dark and Light.
/// </summary>
public static class AccentService
{
    /// <summary>Windows accent palette (6 rows × 8).</summary>
    public static readonly string[] Palette =
    [
        "#FFB900", "#FF8C00", "#F7630C", "#CA5010", "#DA3B01", "#EF6950", "#D13438", "#FF4343",
        "#E74856", "#E81123", "#EA005E", "#C30052", "#E3008C", "#BF0077", "#C239B3", "#9A0089",
        "#0078D7", "#0063B1", "#8E8CD8", "#6B69D6", "#8764B8", "#744DA9", "#B146C2", "#881798",
        "#0099BC", "#2D7D9A", "#00B7C3", "#038387", "#00B294", "#018574", "#00CC6A", "#10893E",
        "#7A7574", "#5D5A58", "#68768A", "#515C6B", "#567C73", "#486860", "#498205", "#107C10",
        "#767676", "#4C4A48", "#69797E", "#4A5459", "#647C64", "#525E54", "#847545", "#7E735F",
    ];

    private static readonly string[] ColorKeys =
    [
        "SystemAccentColor", "SystemAccentColorLight1", "SystemAccentColorLight2", "SystemAccentColorLight3",
        "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorDark3",
    ];

    private static readonly string[] BrushKeys =
    [
        "AccentFillColorDefaultBrush", "AccentFillColorSecondaryBrush", "AccentFillColorTertiaryBrush",
        "AccentTextFillColorPrimaryBrush", "AccentTextFillColorSecondaryBrush", "AccentTextFillColorTertiaryBrush",
        "TextOnAccentFillColorPrimaryBrush", "TextOnAccentFillColorSecondaryBrush", "SystemFillColorAttentionBrush",
        "AccentFillColorSelectedTextBackgroundBrush",
    ];

    // The Fluent theme takes every accent shade from these system keys (DynamicResource), so overriding them
    // recolours all controls: checkboxes, radio buttons, sliders, selection, accent buttons ...
    private static readonly (object Key, int Shade)[] SystemColorKeys =
    [
        (SystemColors.AccentColorKey, 0),
        (SystemColors.AccentColorLight1Key, 1), (SystemColors.AccentColorLight2Key, 2), (SystemColors.AccentColorLight3Key, 3),
        (SystemColors.AccentColorDark1Key, -1), (SystemColors.AccentColorDark2Key, -2), (SystemColors.AccentColorDark3Key, -3),
    ];
    private static readonly (object Key, int Shade)[] SystemBrushKeys =
    [
        (SystemColors.AccentColorBrushKey, 0),
        (SystemColors.AccentColorLight1BrushKey, 1), (SystemColors.AccentColorLight2BrushKey, 2), (SystemColors.AccentColorLight3BrushKey, 3),
        (SystemColors.AccentColorDark1BrushKey, -1), (SystemColors.AccentColorDark2BrushKey, -2), (SystemColors.AccentColorDark3BrushKey, -3),
    ];

    private static readonly Color DefaultBrand = (Color)ColorConverter.ConvertFromString("#7C5CFF");

    /// <summary>Applies "auto" or a #RRGGBB accent for the current theme (call again after the theme changes).</summary>
    public static void Apply(string? accent, bool light)
    {
        var app = Application.Current;
        if (app == null) return;
        // the Fluent theme is also merged into each window's resources, which are searched before the
        // application's: the accent is set on the application and on every open window
        ApplyTo(app.Resources, accent, light);
        foreach (Window w in app.Windows) ApplyTo(w.Resources, accent, light);
    }

    private static void ApplyTo(ResourceDictionary res, string? accent, bool light)
    {
        Color? custom = null;
        if (!string.IsNullOrWhiteSpace(accent) && !accent.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            try { custom = (Color)ColorConverter.ConvertFromString(accent.Trim()); } catch { }
        }

        if (custom is not { } c)
        {
            // Windows accent: drop the overrides so the Fluent theme's own (system) values are used again
            foreach (var k in ColorKeys.Concat(BrushKeys)) res.Remove(k);
            foreach (var (k, _) in SystemColorKeys.Concat(SystemBrushKeys)) res.Remove(k);
            var sys = Application.Current!.TryFindResource("SystemAccentColor") is Color sc ? sc : DefaultBrand;
            SetBrand(res, sys, light);
            return;
        }

        var l1 = Mix(c, Colors.White, 0.18); var l2 = Mix(c, Colors.White, 0.36); var l3 = Mix(c, Colors.White, 0.54);
        var d1 = Mix(c, Colors.Black, 0.18); var d2 = Mix(c, Colors.Black, 0.36); var d3 = Mix(c, Colors.Black, 0.54);
        Color Shade(int n) => n switch { 1 => l1, 2 => l2, 3 => l3, -1 => d1, -2 => d2, -3 => d3, _ => c };
        foreach (var (k, n) in SystemColorKeys) res[k] = Shade(n);
        foreach (var (k, n) in SystemBrushKeys) res[k] = Brush(Shade(n));
        res["SystemAccentColor"] = c;
        res["SystemAccentColorLight1"] = l1; res["SystemAccentColorLight2"] = l2; res["SystemAccentColorLight3"] = l3;
        res["SystemAccentColorDark1"] = d1; res["SystemAccentColorDark2"] = d2; res["SystemAccentColorDark3"] = d3;

        // same mapping as the Fluent theme: light accents on Dark, dark accents on Light
        var fill = light ? d1 : l2;
        res["AccentFillColorDefaultBrush"] = Brush(fill);
        res["AccentFillColorSecondaryBrush"] = Brush(fill, 0.9);
        res["AccentFillColorTertiaryBrush"] = Brush(fill, 0.8);
        res["AccentTextFillColorPrimaryBrush"] = Brush(light ? d2 : l3);
        res["AccentTextFillColorSecondaryBrush"] = Brush(light ? d3 : l3);
        res["AccentTextFillColorTertiaryBrush"] = Brush(light ? d1 : l2);
        res["SystemFillColorAttentionBrush"] = Brush(light ? d1 : l2);
        res["AccentFillColorSelectedTextBackgroundBrush"] = Brush(c);
        // text on the accent: black or white, whichever reads better
        var onAccent = Luminance(fill) > 0.55 ? Colors.Black : Colors.White;
        res["TextOnAccentFillColorPrimaryBrush"] = Brush(onAccent);
        res["TextOnAccentFillColorSecondaryBrush"] = Brush(onAccent, 0.7);
        SetBrand(res, c, light);
    }

    private static void SetBrand(ResourceDictionary res, Color c, bool light)
    {
        var brand = light ? c : Mix(c, Colors.White, 0.1);
        res["BrandBrush"] = Brush(brand);
        res["BrandSoftBrush"] = Brush(brand, 0.16);
    }

    private static SolidColorBrush Brush(Color c, double opacity = 1)
    {
        var b = new SolidColorBrush(c) { Opacity = opacity };
        b.Freeze();
        return b;
    }

    private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    private static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;
}
