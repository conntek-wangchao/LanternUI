using System.IO;
using Lantern.UI;
using Xunit;

namespace LanternUI.Tests;

/// <summary>Two tracks, two manifests: the address in the settings names one of them or the folder.</summary>
public class ChannelTests
{
    [Fact]
    public void A_folder_gets_the_manifest_of_the_track()
    {
        Assert.Equal(Path.Combine("D:\\releases", "release.json"), UpdateChannel.Address("D:\\releases", beta: false));
        Assert.Equal(Path.Combine("D:\\releases", "release-beta.json"), UpdateChannel.Address("D:\\releases", beta: true));
        Assert.Equal(Path.Combine("D:\\releases", "release-beta.json"), UpdateChannel.Address("  D:\\releases  ", beta: true));
    }

    [Fact]
    public void A_manifest_address_keeps_its_folder()
    {
        Assert.Equal("https://host/x/release-beta.json", UpdateChannel.Address("https://host/x/release.json", beta: true));
        Assert.Equal("https://host/x/release.json", UpdateChannel.Address("https://host/x/release-beta.json", beta: false));
        Assert.Equal("https://host/x/release-beta.json", UpdateChannel.Address("https://host/x", beta: true));
        Assert.Equal("https://host/x/release.json", UpdateChannel.Address("https://host/x/", beta: false));
    }

    [Fact]
    public void An_empty_channel_stays_empty()
    {
        Assert.Equal("", UpdateChannel.Address("", beta: true));
        Assert.Equal("", UpdateChannel.Address("   ", beta: true));
    }

    [Fact]
    public void The_track_setting_is_read_leniently()
    {
        Assert.True(UpdateChannel.IsBeta("beta"));
        Assert.True(UpdateChannel.IsBeta(" Beta "));
        Assert.False(UpdateChannel.IsBeta("stable"));
        Assert.False(UpdateChannel.IsBeta(""));
        Assert.False(UpdateChannel.IsBeta(null));
    }

    [Fact]
    public async Task A_beta_release_is_not_offered_on_the_stable_track()
    {
        var channel = Path.Combine(Path.GetTempPath(), "lanternui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(channel);
        File.WriteAllBytes(Path.Combine(channel, "app.zip"), new byte[] { 1, 2, 3 });
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(channel, "app.zip")))).ToLowerInvariant();
        File.WriteAllText(Path.Combine(channel, "release-beta.json"),
            System.Text.Json.JsonSerializer.Serialize(new
            {
                version = "2.0.0",
                beta = 3,
                package = "app.zip",
                sha256 = hash,
                size = 3,
            }));

        var stable = new UpdateService(UpdateChannel.Source(channel, beta: false), Path.Combine(channel, "w1"), new Version(1, 0, 0)) { AllowBeta = false };
        Assert.Null(await stable.CheckAsync());
        Assert.Equal(UpdateStage.Nothing, stable.Stage);

        var beta = new UpdateService(UpdateChannel.Source(channel, beta: true), Path.Combine(channel, "w2"), new Version(1, 0, 0)) { AllowBeta = true };
        var release = await beta.CheckAsync();
        Assert.NotNull(release);
        Assert.Equal("2.0.0-beta.3", release!.Label);

        // The same manifest read on the stable track is refused, not installed.
        File.Copy(Path.Combine(channel, "release-beta.json"), Path.Combine(channel, "release.json"), overwrite: true);
        var catalog = JsonCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Localization"));
        var stableAgain = new UpdateService(UpdateChannel.Source(channel, beta: false), Path.Combine(channel, "w3"),
            new Version(1, 0, 0), 0, null, new Localizer("zh-CN", catalog.For("zh-CN")));
        Assert.Null(await stableAgain.CheckAsync());
        Assert.Contains("测试版", stableAgain.LastMessage, StringComparison.Ordinal);
    }
}