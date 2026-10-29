using System.IO;
using Lantern.UI;
using Xunit;

namespace LanternUI.Tests;

/// <summary>Every sentence the updater puts on screen comes from the catalogue, not from the code.</summary>
public class UpdateWordingTests
{
    private static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "lanternui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public async Task A_channel_failure_is_reported_in_the_readers_language()
    {
        var catalog = JsonCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Localization"));
        var localizer = new Localizer("zh-CN", catalog.For("zh-CN"));

        // Nothing listens on port 1: the check fails at once.
        var service = new UpdateService(
            new HttpUpdateSource("http://127.0.0.1:1/release.json", TimeSpan.FromSeconds(2)),
            NewDirectory(), new Version(1, 0, 0), 0, null, localizer);

        Assert.Null(await service.CheckAsync());
        Assert.Equal(UpdateStage.Failed, service.Stage);

        // The sentence is ours and translated; only the transport detail may stay in the system's words.
        Assert.StartsWith("检查失败。", service.LastMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("check failed", service.LastMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(service.LastMessage.Contains("An error occurred", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_package_of_the_wrong_size_is_reported_in_the_readers_language()
    {
        var catalog = JsonCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Localization"));
        var localizer = new Localizer("zh-CN", catalog.For("zh-CN"));

        var channel = NewDirectory();
        var staged = NewDirectory();
        File.WriteAllText(Path.Combine(staged, "app.dll"), "v2");
        System.IO.Compression.ZipFile.CreateFromDirectory(staged, Path.Combine(channel, "app.zip"));
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            File.ReadAllBytes(Path.Combine(channel, "app.zip")))).ToLowerInvariant();
        // A size that is one byte off, which is what a truncated upload produces.
        File.WriteAllText(Path.Combine(channel, "release.json"),
            System.Text.Json.JsonSerializer.Serialize(new
            {
                version = "2.0.0",
                package = "app.zip",
                sha256 = hash,
                size = new FileInfo(Path.Combine(channel, "app.zip")).Length + 1,
            }));

        var service = new UpdateService(new FolderUpdateSource(channel), NewDirectory(), new Version(1, 0, 0), 0, null, localizer);
        var release = await service.CheckAsync();
        Assert.NotNull(release);

        var result = await service.DownloadAsync(release!);

        Assert.False(result.Ok);
        Assert.StartsWith("包的大小是", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("size mismatch", result.Message, StringComparison.Ordinal);
    }
}
