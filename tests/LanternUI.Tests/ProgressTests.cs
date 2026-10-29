using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lantern.UI;
using Xunit;

namespace LanternUI.Tests;

/// <summary>The download tells the window how far it got, and the About page shows it.</summary>
public class ProgressTests
{
    private static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "lanternui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>A channel folder with one package that is big enough to arrive in several reads.</summary>
    private static (string Channel, long Size) NewChannel()
    {
        var channel = NewDirectory();
        var payload = Path.Combine(NewDirectory(), "payload.bin");
        File.WriteAllBytes(payload, new byte[3 * 1024 * 1024]);

        var staged = NewDirectory();
        File.Copy(payload, Path.Combine(staged, "app.bin"));
        var package = Path.Combine(channel, "app.zip");
        ZipFile.CreateFromDirectory(staged, package);

        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(package))).ToLowerInvariant();
        var size = new FileInfo(package).Length;
        File.WriteAllText(Path.Combine(channel, "release.json"),
            JsonSerializer.Serialize(new { version = "2.0.0", package = "app.zip", sha256 = hash, size }));

        return (channel, size);
    }

    [Fact]
    public async Task A_download_reports_its_bytes_from_zero_to_the_last_one()
    {
        var (channel, size) = NewChannel();
        var reported = new List<(long Received, long Total)>();

        var service = new UpdateService(new FolderUpdateSource(channel), Path.Combine(channel, "work"), new Version(1, 0, 0));
        service.DownloadProgress += (received, total) =>
        {
            lock (reported) reported.Add((received, total));
        };

        var release = await service.CheckAsync();
        Assert.NotNull(release);
        var result = await service.DownloadAsync(release!);
        Assert.True(result.Ok, result.Message);

        Assert.True(reported.Count >= 2, "the bar would never move with a single report");
        Assert.Equal((0L, size), reported[0]);
        Assert.Equal((size, size), reported[^1]);
        Assert.Equal(reported.OrderBy(item => item.Received).ToList(), reported);
        Assert.All(reported, item => Assert.InRange(item.Received, 0, size));
    }

    [Fact]
    public void The_about_page_has_a_progress_bar_that_waits_for_a_download()
    {
        Sta.Run(() =>
        {
            var state = Sta.NewState();
            var localizer = new Localizer("en", null);
            var shell = new ShellWindow { App = state, Localizer = localizer };
            var page = new Demo.Pages.AboutPage(state, shell, localizer);

            page.Measure(new Size(1020, 760));
            page.Arrange(new Rect(0, 0, 1020, 760));
            page.UpdateLayout();

            var bar = Visuals.Descendants<ProgressBar>(page).Single();
            Assert.Equal(Visibility.Collapsed, bar.Visibility);
            Assert.Same(shell.FindResource("Progress"), bar.Style);

            // The token style has to lay out like a bar, not collapse to nothing.
            bar.Visibility = Visibility.Visible;
            bar.Value = 42;
            page.Measure(new Size(1020, 760));
            page.Arrange(new Rect(0, 0, 1020, 760));
            page.UpdateLayout();

            Assert.True(bar.ActualWidth > 0, "the bar has no width");
            Assert.True(bar.ActualHeight > 0, "the bar has no height");
            Assert.Equal(42, bar.Value, 3);

            // Keep a picture of the row as it looks mid-download; the page has no background of its
            // own, so the shot gets one or the light text would be invisible.
            page.Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x20));
            var target = new RenderTargetBitmap(1020, 320, 96, 96, PixelFormats.Pbgra32);
            target.Render(page);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(target));
            var shot = Path.Combine(Path.GetTempPath(), "lanternui-tests", "progress-row.png");
            Directory.CreateDirectory(Path.GetDirectoryName(shot)!);
            using (var stream = File.Create(shot)) encoder.Save(stream);
            Assert.True(new FileInfo(shot).Length > 0);
        });
    }
}