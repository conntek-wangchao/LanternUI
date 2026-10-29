using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lantern.UI;
using Xunit;

namespace LanternUI.Tests;

/// <summary>
/// Visual regression: the same window, rendered to a bitmap and compared with a committed baseline.
/// This is the one dimension the other checks cannot see — a build and a green i18n gate say nothing
/// about a card that lost its padding or a theme that stopped following the accent.
///
/// The rendering is deterministic on purpose: fixed size, no backdrop image (no blurred blur), no
/// hover, English, and a settings file that is deleted before the run so the status bar is stable.
/// Allowance is a small share of pixels, because anti-aliasing is allowed to move by a hair.
///
/// Set LANTERN_UPDATE_SNAPSHOTS=1 to (re)write a baseline; a missing baseline is written and fails
/// once, so a new scenario cannot pass silently.
/// </summary>
public class SnapshotTests
{
    // 0.05%: a re-render on the machine that made the baseline is byte-identical, so anything above
    // that is a real change. The old 0.5% let a 55x30 px highlighted pill slip through unnoticed.
    private const double Tolerance = 0.0005;

    private static string Source => TestPaths.SnapshotSource;

    [Theory]
    [InlineData("appearance-modern", UiLayout.Modern)]
    [InlineData("appearance-classic", UiLayout.Classic)]
    [InlineData("container-modern", UiLayout.Modern)]
    [InlineData("appearance-rtl", UiLayout.Modern)]
    [InlineData("appearance-backdrop", UiLayout.Modern)]
    public void The_window_still_looks_the_same(string name, UiLayout layout)
    {
        // The pixel baselines are authoritative on the machine that produced them (fonts, ClearType,
        // WPF build). A foreign CI sets this to 0 and relies on StructureSnapshotTests instead.
        if (Environment.GetEnvironmentVariable("LANTERN_PIXEL_SNAPSHOTS") == "0") return;

        var rendered = Sta.Run(() => Render(name, layout));
        var source = Path.Combine(Source, name + ".png");

        if (Environment.GetEnvironmentVariable("LANTERN_UPDATE_SNAPSHOTS") == "1" || !File.Exists(source))
        {
            Directory.CreateDirectory(Source);
            File.WriteAllBytes(source, rendered);
            Assert.Fail("baseline written to " + source + " — look at it, commit it, and run again.");
        }

        var differing = Compare(rendered, File.ReadAllBytes(source), out var ratio);
        Assert.True(ratio <= Tolerance,
            string.Format("{0}: {1:P2} of the pixels differ ({2} px). Run with LANTERN_UPDATE_SNAPSHOTS=1 " +
                          "after looking at the new rendering.", name, ratio, differing));
    }

    private static byte[] Render(string name, UiLayout layout)
    {
        var config = Path.Combine(Path.GetTempPath(), "lanternui-snapshots", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(config)!);
        if (File.Exists(config)) File.Delete(config);

        var state = new Demo.DemoState(config);
        var catalog = JsonCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Localization"));
        var localizer = new Localizer("en", null);

        // The backdrop is off by default (a blurred picture is not portable), but one scenario turns
        // it on: that is the case where the picture used to paint over the navigation.
        var backdrop = name.EndsWith("-backdrop", StringComparison.Ordinal);
        state.Settings.Set("ui_background", backdrop ? "custom" : "none");
        if (backdrop)
            state.Settings.Set("ui_background_image", Path.Combine(AppContext.BaseDirectory, "Assets", "backdrop-sample.png"));
        state.Settings.SetBool("log_to_file", false);
        state.Settings.SetBool("log_on_start", false);

        var shell = new ShellWindow
        {
            App = state,
            Localizer = localizer,
            Width = 1020,
            Height = 760,
            Left = -8000,
            Top = -8000,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
        };
        shell.SetLayout(layout);
        shell.ApplyLanguage();
        shell.SetPages(new[]
        {
            new PageEntry("appearance", "Appearance",
                () => new Demo.Pages.AppearancePage(state, shell, localizer, catalog.Languages)),
            new PageEntry("more", "More", () => new Demo.Pages.MorePage(state, shell, localizer)),
            new PageEntry("list", "List", () => new Demo.Pages.ListDemoPage(state, shell, localizer)),
        });
        if (name.EndsWith("-rtl", StringComparison.Ordinal)) shell.ApplyFlowDirection(true);
        shell.ShowPage(name == "container-modern" ? "more" : "appearance");
        shell.Show();
        shell.UpdateLayout();

        if (backdrop && !shell.BackdropVisible)
            throw new InvalidOperationException("the backdrop was requested but is not on screen: " + name);

        var width = (int)Math.Max(1, shell.ActualWidth);
        var height = (int)Math.Max(1, shell.ActualHeight);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(shell);
        shell.Close();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static int Compare(byte[] left, byte[] right, out double ratio)
    {
        var a = Pixels(left, out var width, out var height);
        var b = Pixels(right, out var otherWidth, out var otherHeight);
        Assert.Equal(width, otherWidth);
        Assert.Equal(height, otherHeight);

        var differing = 0;
        for (var index = 0; index < a.Length; index += 4)
        {
            // Any channel off by more than a rounding step counts as a changed pixel.
            if (Math.Abs(a[index] - b[index]) > 8 || Math.Abs(a[index + 1] - b[index + 1]) > 8 ||
                Math.Abs(a[index + 2] - b[index + 2]) > 8) differing++;
        }

        ratio = differing / (double)(width * height);
        return differing;
    }

    private static byte[] Pixels(byte[] png, out int width, out int height)
    {
        using var stream = new MemoryStream(png);
        var frame = BitmapFrame.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0);

        width = converted.PixelWidth;
        height = converted.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        converted.CopyPixels(pixels, stride, 0);
        return pixels;
    }
}
