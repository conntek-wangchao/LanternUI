using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Lantern.UI;
using Xunit;

namespace LanternUI.Tests;

/// <summary>
/// The update path, exercised offline: version ordering, a strict release.json, a folder channel, a
/// hash that must match, and an apply that keeps the player's files. Nothing here touches the network
/// or the real install folder.
/// </summary>
public class UpdateTests
{
    private static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "lanternui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [Theory]
    [InlineData("1.0.0", 0, "1.0.0", 0, 0)]
    [InlineData("1.0.0", 0, "1.0.1", 0, 1)]         // the release is newer
    [InlineData("1.2.0", 0, "1.1.9", 0, -1)]        // the release is older
    [InlineData("1.2", 0, "1.2.0", 0, 0)]           // 1.2 and 1.2.0 are the same release
    [InlineData("1.2.0", 1, "1.2.0", 2, 1)]         // a later beta is newer
    [InlineData("1.2.0", 3, "1.2.0", 0, 1)]         // a stable release beats its own betas
    [InlineData("1.2.0", 0, "1.2.0", 3, -1)]
    public void Versions_order_by_number_then_by_beta(string current, int currentBeta, string release, int releaseBeta, int sign)
    {
        var left = UpdateVersion.Parse(current);
        var right = UpdateVersion.Parse(release);
        Assert.Equal(sign, Math.Sign(UpdateVersion.Compare(right, releaseBeta, left, currentBeta)));
    }

    [Fact]
    public void The_versions_are_read_from_their_label()
    {
        Assert.Equal(new Version(1, 4, 2), UpdateVersion.Parse("v1.4.2"));
        Assert.Equal(3, UpdateVersion.BetaOf("v1.4.2-beta.3"));
        Assert.Equal(0, UpdateVersion.BetaOf("1.4.2"));
    }

    [Fact]
    public void A_release_needs_a_version_and_a_package()
    {
        using var good = JsonDocument.Parse("""{"version":"1.2.0","package":"app.zip","sha256":"abc","size":12}""");
        var release = ReleaseInfo.Parse(good.RootElement);
        Assert.Equal("1.2.0", release.Label);
        Assert.Equal("app.zip", release.PackageUrl);
        Assert.Equal(12, release.Size);

        using var noVersion = JsonDocument.Parse("""{"package":"app.zip"}""");
        Assert.Throws<FormatException>(() => ReleaseInfo.Parse(noVersion.RootElement));

        using var noPackage = JsonDocument.Parse("""{"version":"1.2.0"}""");
        Assert.Throws<FormatException>(() => ReleaseInfo.Parse(noPackage.RootElement));
    }

    [Fact]
    public async Task A_folder_channel_offers_only_a_package_it_has()
    {
        var directory = NewDirectory();
        var package = Path.Combine(directory, "app-1.2.0.zip");
        File.WriteAllBytes(package, new byte[] { 1, 2, 3 });
        File.WriteAllText(Path.Combine(directory, "release.json"),
            """{"version":"1.2.0","package":"app-1.2.0.zip","sha256":"x","size":3}""");

        var source = new FolderUpdateSource(directory);
        var release = await source.CheckAsync();
        Assert.NotNull(release);

        File.Delete(package);
        Assert.Null(await source.CheckAsync());
    }


    [Fact]
    public async Task A_channel_that_cannot_be_reached_says_so_instead_of_up_to_date()
    {
        // Nothing listens on port 1: the connection is refused at once, so the check is fast and real.
        var source = new HttpUpdateSource("http://127.0.0.1:1/release.json", TimeSpan.FromSeconds(2));

        var release = await source.CheckAsync();

        Assert.Null(release);
        Assert.False(string.IsNullOrWhiteSpace(source.LastError));
    }
    [Fact]
    public async Task Only_a_newer_release_is_offered()
    {
        var directory = NewDirectory();
        Directory.CreateDirectory(Path.Combine(directory, "work"));
        File.WriteAllBytes(Path.Combine(directory, "app.zip"), new byte[] { 1 });
        File.WriteAllText(Path.Combine(directory, "release.json"),
            """{"version":"1.0.0","package":"app.zip","sha256":"x","size":1}""");

        var service = new UpdateService(new FolderUpdateSource(directory), Path.Combine(directory, "work"), new Version(1, 0, 0));
        Assert.Null(await service.CheckAsync());
        Assert.Equal(UpdateStage.Nothing, service.Stage);
    }

    [Fact]
    public async Task A_download_is_refused_when_the_hash_does_not_match()
    {
        var directory = NewDirectory();
        var work = Path.Combine(directory, "work");
        var package = Path.Combine(directory, "app.zip");
        BuildPackage(package, ("app.dll", "new"));

        var release = new ReleaseInfo(new Version(2, 0, 0), 0, "", "app.zip", new string('0', 64),
            new FileInfo(package).Length, DateTime.UtcNow);

        var service = new UpdateService(new FolderUpdateSource(directory), work, new Version(1, 0, 0));
        var result = await service.DownloadAsync(release);

        Assert.False(result.Ok);
        Assert.Contains("sha256", result.Message);
        Assert.Null(UpdateApplier.ReadPending(work));
        Assert.False(Directory.Exists(Path.Combine(work, "staging")));
    }

    [Fact]
    public async Task A_release_without_a_hash_is_not_installed()
    {
        var directory = NewDirectory();
        var package = Path.Combine(directory, "app.zip");
        BuildPackage(package, ("app.dll", "new"));

        var service = new UpdateService(new FolderUpdateSource(directory), Path.Combine(directory, "work"), new Version(1, 0, 0));
        var release = new ReleaseInfo(new Version(2, 0, 0), 0, "", "app.zip", "", 0, DateTime.UtcNow);
        var result = await service.DownloadAsync(release);

        Assert.False(result.Ok);
        Assert.Contains("sha256", result.Message);
    }


    [Fact]
    public void Finishing_a_pending_update_replaces_files_and_leaves_nothing_behind()
    {
        var root = Path.Combine(Path.GetTempPath(), "lantern-apply-" + Guid.NewGuid().ToString("N"));
        var install = Path.Combine(root, "install");
        var work = Path.Combine(root, "work");
        var staged = Path.Combine(work, "staging", "1.0.1");
        Directory.CreateDirectory(install);
        Directory.CreateDirectory(staged);

        File.WriteAllText(Path.Combine(install, "app.exe"), "old");
        File.WriteAllText(Path.Combine(install, "settings.json"), "keep me");
        File.WriteAllText(Path.Combine(staged, "app.exe"), "new");
        File.WriteAllText(Path.Combine(work, "pending.json"),
            System.Text.Json.JsonSerializer.Serialize(new PendingUpdate("1.0.1", staged, DateTime.UtcNow)));

        try
        {
            var logged = new List<string>();
            var outcome = UpdateApplier.ApplyPending(work, install, logged.Add);

            Assert.True(outcome.Ok, outcome.Message);
            Assert.Equal("1.0.1", outcome.Label);
            Assert.Equal("new", File.ReadAllText(Path.Combine(install, "app.exe")));
            Assert.Equal("keep me", File.ReadAllText(Path.Combine(install, "settings.json")));   // the player's own
            Assert.False(File.Exists(Path.Combine(work, "pending.json")));
            Assert.False(Directory.Exists(staged));       // the copy of the package went with it
            Assert.NotEmpty(logged);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
    [Fact]
    public void Applying_replaces_the_app_keeps_the_player_and_moves_the_running_exe()
    {
        var staged = NewDirectory();
        var install = NewDirectory();
        Directory.CreateDirectory(Path.Combine(staged, "logs"));
        File.WriteAllText(Path.Combine(staged, "app.dll"), "new");
        File.WriteAllText(Path.Combine(staged, "LanternUI.exe"), "new exe");
        File.WriteAllText(Path.Combine(staged, "settings.json"), "shipped");
        File.WriteAllText(Path.Combine(staged, "logs", "today.log"), "shipped");

        File.WriteAllText(Path.Combine(install, "app.dll"), "old");
        File.WriteAllText(Path.Combine(install, "LanternUI.exe"), "old exe");
        File.WriteAllText(Path.Combine(install, "settings.json"), "mine");
        Directory.CreateDirectory(Path.Combine(install, "logs"));
        File.WriteAllText(Path.Combine(install, "logs", "today.log"), "mine");

        var running = Path.Combine(install, "LanternUI.exe");
        var inUse = new[] { running, Path.Combine(install, "app.dll") };   // the process holds its own dll too
        var plan = UpdateApplier.BuildPlan(staged, install, UpdateApplier.DefaultKept, inUse);

        Assert.Equal(2, plan.Skipped.Count);
        Assert.Contains(plan.Skipped, path => path.Equals("settings.json", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.Skipped, path => path.EndsWith("today.log", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.Steps, step => step.MoveAside && step.Destination.EndsWith("LanternUI.exe", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.Steps, step => step.MoveAside && step.Destination.EndsWith("app.dll", StringComparison.OrdinalIgnoreCase));

        var outcome = UpdateApplier.Apply(plan);
        Assert.True(outcome.Ok, outcome.Message);

        Assert.Equal("new", File.ReadAllText(Path.Combine(install, "app.dll")));
        Assert.Equal("mine", File.ReadAllText(Path.Combine(install, "settings.json")));
        Assert.Equal("mine", File.ReadAllText(Path.Combine(install, "logs", "today.log")));
        Assert.Equal("new exe", File.ReadAllText(running));
        Assert.True(File.Exists(running + ".old"));

        UpdateApplier.Cleanup(install);
        Assert.False(File.Exists(running + ".old"));
    }

    [Fact]
    public async Task Check_download_and_apply_work_end_to_end_from_a_folder()
    {
        var channel = NewDirectory();
        var work = Path.Combine(channel, "work");
        var install = NewDirectory();

        var package = Path.Combine(channel, "lanternui-2.0.0.zip");
        BuildPackage(package, ("app.dll", "v2"), ("settings.json", "shipped"));
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(package))).ToLowerInvariant();

        File.WriteAllText(Path.Combine(channel, "release.json"),
            JsonSerializer.Serialize(new
            {
                version = "2.0.0",
                package = Path.GetFileName(package),
                sha256 = hash,
                size = new FileInfo(package).Length,
                notes = "second release",
            }));

        File.WriteAllText(Path.Combine(install, "app.dll"), "v1");
        File.WriteAllText(Path.Combine(install, "settings.json"), "mine");

        var service = new UpdateService(new FolderUpdateSource(channel), work, new Version(1, 0, 0));
        var release = await service.CheckAsync();
        Assert.NotNull(release);
        Assert.Equal(UpdateStage.Available, service.Stage);

        var result = await service.DownloadAsync(release!);
        Assert.True(result.Ok, result.Message);
        Assert.Equal(UpdateStage.Ready, service.Stage);

        var pending = UpdateApplier.ReadPending(work);
        Assert.NotNull(pending);
        Assert.Equal("2.0.0", pending!.Label);
        Assert.True(Directory.Exists(pending.Staged));

        var outcome = UpdateApplier.Apply(UpdateApplier.BuildPlan(pending.Staged, install));
        Assert.True(outcome.Ok, outcome.Message);

        Assert.Equal("v2", File.ReadAllText(Path.Combine(install, "app.dll")));
        Assert.Equal("mine", File.ReadAllText(Path.Combine(install, "settings.json")));

        service.ClearPending();
        Assert.Null(UpdateApplier.ReadPending(work));
    }

    /// <summary>A package is a zip of files at its root, which is what the release publishes.</summary>
    private static void BuildPackage(string path, params (string Name, string Text)[] files)
    {
        var folder = NewDirectory();
        foreach (var (name, text) in files)
        {
            var target = Path.Combine(folder, name);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, text);
        }

        if (File.Exists(path)) File.Delete(path);
        ZipFile.CreateFromDirectory(folder, path);
    }
}