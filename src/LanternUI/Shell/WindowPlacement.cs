using System.Globalization;
using System.Windows;

namespace Lantern.UI;

/// <summary>
/// Remembers where the window was: size, position and whether it was maximised. A restored
/// position is kept only if it still lands on a screen (a monitor can be unplugged between runs).
/// </summary>
public static class WindowPlacement
{
    public static void Restore(Window window, ISettingsStore settings, string prefix = "window")
    {
        var width = Number(settings, prefix + "_width");
        var height = Number(settings, prefix + "_height");
        if (width is > 320 and < 20000) window.Width = width.Value;
        if (height is > 240 and < 20000) window.Height = height.Value;

        var left = Number(settings, prefix + "_left");
        var top = Number(settings, prefix + "_top");
        if (left is { } l && top is { } t)
        {
            var screenLeft = SystemParameters.VirtualScreenLeft;
            var screenTop = SystemParameters.VirtualScreenTop;
            var screenRight = screenLeft + SystemParameters.VirtualScreenWidth;
            var screenBottom = screenTop + SystemParameters.VirtualScreenHeight;

            // Some part of the window must stay reachable; otherwise the default centring wins.
            if (l + 120 > screenLeft && l < screenRight - 80 && t + 60 > screenTop && t < screenBottom - 40)
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = l;
                window.Top = t;
            }
        }

        if (settings.Bool(prefix + "_maximized")) window.WindowState = WindowState.Maximized;
    }

    /// <summary>Writes the placement back on every move/resize; <paramref name="save"/> is the caller's debounce.</summary>
    public static void Track(Window window, ISettingsStore settings, Action save, string prefix = "window")
    {
        void Store()
        {
            if (window.WindowState == WindowState.Minimized) return;
            settings.SetBool(prefix + "_maximized", window.WindowState == WindowState.Maximized);
            if (window.WindowState != WindowState.Normal) return;

            settings.Set(prefix + "_width", Text(window.Width));
            settings.Set(prefix + "_height", Text(window.Height));
            settings.Set(prefix + "_left", Text(window.Left));
            settings.Set(prefix + "_top", Text(window.Top));
            save();
        }

        window.LocationChanged += (_, _) => Store();
        window.SizeChanged += (_, _) => Store();
        window.StateChanged += (_, _) => Store();
    }

    private static double? Number(ISettingsStore settings, string key) =>
        double.TryParse(settings.Str(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value != 0
            ? value
            : null;

    private static string Text(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
