using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace Lantern.UI;

/// <summary>Where an update run is, so a settings page can show it.</summary>
public enum UpdateStage { Idle, Checking, Nothing, Available, Downloading, Verifying, Ready, Failed }

/// <summary>The outcome of a download: ok, or the reason it was refused.</summary>
public sealed record UpdateResult(bool Ok, string Message, string? Staged = null, ReleaseInfo? Release = null)
{
    public static UpdateResult Fail(string message) => new(false, message);
}

/// <summary>A downloaded and extracted update that has not been applied yet.</summary>
public sealed record PendingUpdate(string Label, string Staged, DateTime Created);

/// <summary>
/// Checks a source, downloads the package into the work directory, verifies it against the release's
/// hash, extracts it and leaves a pending marker. It never touches the installed files: applying is
/// UpdateApplier's job, so a failed download cannot leave a half-updated application.
/// </summary>
public sealed class UpdateService
{
    private const string PendingFile = "pending.json";

    private readonly ILogSink _log;
    private readonly ILocalizer _text;

    public UpdateService(IUpdateSource source, string workDirectory, Version current, int currentBeta = 0,
        ILogSink? log = null, ILocalizer? localizer = null)
    {
        Source = source;
        WorkDirectory = Path.GetFullPath(workDirectory);
        CurrentVersion = UpdateVersion.Normalize(current);
        CurrentBeta = currentBeta;
        _log = log ?? NullLogSink.Instance;
        _text = localizer ?? NullLocalizer.Instance;
    }

    public IUpdateSource Source { get; }

    public string WorkDirectory { get; }

    public Version CurrentVersion { get; }

    public int CurrentBeta { get; }

    /// <summary>What the last check found to be newer, or null.</summary>
    public ReleaseInfo? Available { get; private set; }

    public UpdateStage Stage { get; private set; }

    /// <summary>Why the last check or download stopped, or empty when it did not.</summary>
    public string LastMessage { get; private set; } = "";

    public event Action<UpdateStage>? StageChanged;

    /// <summary>Asks the source; returns the release only when it is newer than this build.</summary>
    public async Task<ReleaseInfo?> CheckAsync(CancellationToken token = default)
    {
        Set(UpdateStage.Checking);
        Write(LogLevel.Info, "update: checking " + Source.Description);

        ReleaseInfo? release;
        try
        {
            release = await Source.CheckAsync(token).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or HttpRequestException)
        {
            return Nothing("check failed: " + e.Message);
        }

        if (release == null)
            return Source.LastError is { Length: > 0 } error
                ? Nothing(_text.T("check failed: ") + error, failed: true)
                : Nothing(_text.T("nothing published"));

        if (!UpdateVersion.IsNewer(release, CurrentVersion, CurrentBeta))
        {
            Write(LogLevel.Good, _text.F($"update: {release.Label} is not newer than {Label}"));
            return Nothing(_text.T("up to date"));
        }

        Available = release;
        Set(UpdateStage.Available);
        Write(LogLevel.Good, "update: " + release.Label + " is available");
        return release;
    }

    /// <summary>Downloads, verifies and extracts; the install folder is not touched.</summary>
    public async Task<UpdateResult> DownloadAsync(ReleaseInfo release, CancellationToken token = default)
    {
        if (release.Sha256.Length != 64)
            return Stop(_text.T("the release has no sha256: refusing to install it"));

        var download = Path.Combine(WorkDirectory, "download");
        var staging = Path.Combine(WorkDirectory, "staging", release.Label);
        var file = Path.Combine(download, Path.GetFileName(release.PackageUrl));

        try
        {
            Directory.CreateDirectory(download);
            Set(UpdateStage.Downloading);
            Write(LogLevel.Info, "update: downloading " + release.PackageUrl);

            await using (var input = await Source.OpenPackageAsync(release, token).ConfigureAwait(false))
            await using (var output = File.Create(file))
                await input.CopyToAsync(output, token).ConfigureAwait(false);

            Set(UpdateStage.Verifying);
            var size = new FileInfo(file).Length;
            if (release.Size > 0 && size != release.Size)
            {
                File.Delete(file);
                return Stop(string.Format(CultureInfo.InvariantCulture, "size mismatch: expected {0}, got {1}", release.Size, size));
            }

            var hash = await HashAsync(file, token).ConfigureAwait(false);
            if (!hash.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(file);
                return Stop("sha256 mismatch: expected " + release.Sha256 + ", got " + hash);
            }

            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            Directory.CreateDirectory(staging);
            ZipFile.ExtractToDirectory(file, staging, overwriteFiles: true);

            File.WriteAllText(Path.Combine(WorkDirectory, PendingFile),
                JsonSerializer.Serialize(new PendingUpdate(release.Label, staging, DateTime.UtcNow)));

            Set(UpdateStage.Ready);
            Write(LogLevel.Good, "update: " + release.Label + " is ready to install");
            return new UpdateResult(true, "ready: " + release.Label, staging, release);
        }
        catch (Exception e) when (e is IOException or HttpRequestException or InvalidDataException or UnauthorizedAccessException)
        {
            return Stop(e.Message);
        }
    }

    public PendingUpdate? Pending() => UpdateApplier.ReadPending(WorkDirectory);

    /// <summary>Forgets the pending update (after it was applied, or when it turned out to be unusable).</summary>
    public void ClearPending() => UpdateApplier.ClearPending(WorkDirectory);

    private string Label => CurrentBeta > 0 ? CurrentVersion + "-beta." + CurrentBeta : CurrentVersion.ToString();

    private ReleaseInfo? Nothing(string reason, bool failed = false)
    {
        Available = null;
        LastMessage = reason;
        Set(failed ? UpdateStage.Failed : UpdateStage.Nothing);
        Write(failed ? LogLevel.Bad : LogLevel.Info, "update: " + reason);
        return null;
    }

    private UpdateResult Stop(string reason)
    {
        LastMessage = reason;
        Set(UpdateStage.Failed);
        Write(LogLevel.Bad, "update: " + reason);
        return UpdateResult.Fail(reason);
    }

    private void Set(UpdateStage stage)
    {
        Stage = stage;
        StageChanged?.Invoke(stage);
    }

    private void Write(LogLevel level, string message) => _log.Write(DateTime.Now, level, message);

    private static async Task<string> HashAsync(string file, CancellationToken token)
    {
        await using var stream = File.OpenRead(file);
        var hash = await SHA256.HashDataAsync(stream, token).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}