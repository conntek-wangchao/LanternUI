using System.Windows;
using System.Windows.Media;

namespace Lantern.UI;

/// <summary>
/// Merges the framework dictionaries into the application once, in order, and keeps the palette
/// swappable: the dark one by default, the high-contrast one when Windows asks for it — followed
/// live, because a user who turns on high contrast should not have to restart the launcher.
/// </summary>
public static class ThemeBoot
{
    private static bool _installed;
    private static ResourceDictionary? _palette;

    /// <summary>True while the high-contrast palette is the one in use.</summary>
    public static bool HighContrast { get; private set; }

    public static void Install()
    {
        if (_installed) return;
        _installed = true;

        ApplyPalette(SystemParameters.HighContrast);

        SystemParameters.StaticPropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SystemParameters.HighContrast)) ApplyPalette(SystemParameters.HighContrast);
        };

        Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/LanternUI;component/Theming/Styles.xaml"),
        });
    }

    /// <summary>
    /// Replaces the palette dictionary where it sits: at the bottom of the merged list, so the styles
    /// and the accent overlay above it always win. High contrast clears the accent — the system's
    /// highlight colour is the accent.
    /// </summary>
    public static void ApplyPalette(bool highContrast)
    {
        var resources = Application.Current.Resources;
        if (_palette != null) resources.MergedDictionaries.Remove(_palette);

        HighContrast = highContrast;
        _palette = new ResourceDictionary
        {
            Source = new Uri(highContrast
                ? "pack://application:,,,/LanternUI;component/Theming/Palette.HighContrast.xaml"
                : "pack://application:,,,/LanternUI;component/Theming/Palette.Dark.xaml"),
        };
        // The font comes from the system too: a reader who set a large message font gets it.
        if (highContrast) _palette["UiFont"] = SystemFonts.MessageFontFamily;

        resources.MergedDictionaries.Insert(0, _palette);

        if (highContrast) AccentTheme.Clear();
    }
}
public static class AccentTheme
{
    private static ResourceDictionary? _overlay;

    /// <summary>The accent the palette ships with.</summary>
    public static readonly Color Default = Color.FromRgb(0x4C, 0x9D, 0xFF);

    /// <summary>Removes the overlay, leaving the palette's own colours (also the high-contrast case).</summary>
    public static void Clear()
    {
        foreach (var dictionary in Application.Current.Resources.MergedDictionaries.ToList())
            if (dictionary == _overlay) Application.Current.Resources.MergedDictionaries.Remove(dictionary);
        _overlay = null;
    }

    /// <summary>Applies an accent colour; null (or Reset) returns to the palette's own.</summary>
    public static void Apply(Color? accent)
    {
        // In high contrast the system owns the colours: an accent would fight the user's theme.
        if (ThemeBoot.HighContrast)
        {
            Clear();
            return;
        }

        var resources = Application.Current.Resources;
        if (_overlay != null)
        {
            resources.MergedDictionaries.Remove(_overlay);
            _overlay = null;
        }
        if (accent is not { } a) return;

        var d = new ResourceDictionary();
        void Solid(string key, Color c) => d[key] = Frozen(new SolidColorBrush(c));

        Solid("AccentBrush", a);
        Solid("AccentTextBrush", Mix(a, Colors.White, 0.55));
        Solid("SelectedBrush", Mix(Color.FromRgb(0x37, 0x37, 0x3B), a, 0.30));
        Solid("HoverBrush", Mix(Color.FromRgb(0x2E, 0x2E, 0x31), a, 0.10));
        Solid("WindowBaseBrush", Mix(Color.FromRgb(0x24, 0x24, 0x26), a, 0.06));
        Solid("ContentAreaBrush", Mix(Color.FromRgb(0x29, 0x29, 0x2B), a, 0.06));
        Solid("CardBackgroundBrush", Mix(Color.FromRgb(0x2E, 0x2E, 0x31), a, 0.07));
        Solid("CardStrokeBrush", Mix(Color.FromRgb(0x3A, 0x3A, 0x3E), a, 0.12));
        Solid("PillTrackBrush", Mix(Color.FromRgb(0x1E, 0x1E, 0x21), a, 0.05));
        Solid("DividerBrush", Mix(Color.FromRgb(0x34, 0x34, 0x3A), a, 0.10));

        resources.MergedDictionaries.Add(d);
        _overlay = d;
    }

    public static void Reset() => Apply(null);

    /// <summary>Parses "#RRGGBB" (the "#" optional); null when it is not a colour.</summary>
    public static Color? Parse(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return null;
        if (text.Length is 6 or 8 && !text.StartsWith('#')) text = "#" + text;
        try
        {
            return ColorConverter.ConvertFromString(text) is Color c ? Color.FromRgb(c.R, c.G, c.B) : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>The dark wash under a backdrop picture, tinted by the accent.</summary>
    public static Color ShadeColour(Color accent) => Mix(Color.FromRgb(0x0A, 0x0B, 0x0E), accent, 0.12);

    public static Color WithAlpha(Color colour, byte alpha) => Color.FromArgb(alpha, colour.R, colour.G, colour.B);

    public static Color Mix(Color from, Color to, double amount) => Color.FromRgb(
        (byte)Math.Round(from.R + (to.R - from.R) * amount),
        (byte)Math.Round(from.G + (to.G - from.G) * amount),
        (byte)Math.Round(from.B + (to.B - from.B) * amount));

    private static SolidColorBrush Frozen(SolidColorBrush brush)
    {
        if (brush.CanFreeze) brush.Freeze();
        return brush;
    }

    /// <summary>
    /// A plain brush of the same colour, frozen when it can be. Cloning is not enough: a brush whose
    /// colour is a DynamicResource — every brush of the high-contrast palette is one — cannot be
    /// frozen, and its clone keeps the resource reference. Building a new brush drops the reference.
    /// </summary>
    public static SolidColorBrush Copy(Brush brush, double opacity = 1)
    {
        var colour = brush is SolidColorBrush solid ? solid.Color : Colors.Gray;
        var copy = new SolidColorBrush(colour) { Opacity = opacity };
        if (copy.CanFreeze) copy.Freeze();
        return copy;
    }
}