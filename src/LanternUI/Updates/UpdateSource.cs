using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace Lantern.UI;

/// <summary>One published build: what it is called, what to download, and the hash it must have.</summary>
public sealed record ReleaseInfo(
    Version Version,
    int Beta,
    string Notes,
    string PackageUrl,
    string Sha256,
    long Size,
    DateTime Published)
{
    /// <summary>The version with the beta suffix, as it is shown to a person.</summary>
    public string Label => Beta > 0 ? Version + "-beta." + Beta : Version.ToString();

    /// <summary>Reads one release.json. A missing or malformed field is an error, not a silent default.</summary>
    public static ReleaseInfo Parse(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new FormatException("release.json must be an object");

        var version = Text(element, "version") ?? throw new FormatException("release.json has no version");
        var package = Text(element, "package") ?? throw new FormatException("release.json has no package");

        // beta is written as a number by the packaging script and as a string by hand; both count.
        var beta = 0;
        if (element.TryGetProperty("beta", out var betaElement))
        {
            if (betaElement.ValueKind == JsonValueKind.Number && !betaElement.TryGetInt32(out beta)) beta = 0;
            else if (betaElement.ValueKind == JsonValueKind.String &&
                     !int.TryParse(betaElement.GetString(), out beta)) beta = 0;
        }

        long size = 0;
        if (element.TryGetProperty("size", out var sizeElement) && sizeElement.ValueKind == JsonValueKind.Number)
            size = sizeElement.GetInt64();

        var published = DateTime.MinValue;
        if (Text(element, "published") is { } publishedText) DateTime.TryParse(publishedText, out published);

        return new ReleaseInfo(
            UpdateVersion.Parse(version),
            beta,
            Text(element, "notes") ?? "",
            (string)package,
            (Text(element, "sha256") ?? "").ToLowerInvariant(),
            size,
            published);
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

/// <summary>Where a release comes from. A folder for an in-house channel and for tests, a URL for the public one.</summary>
public interface IUpdateSource
{
    /// <summary>The newest release, or null when there is nothing to offer.</summary>
    Task<ReleaseInfo?> CheckAsync(CancellationToken token = default);

    /// <summary>Opens the package for a release; the caller verifies and extracts it.</summary>
    Task<Stream> OpenPackageAsync(ReleaseInfo release, CancellationToken token = default);

    /// <summary>What this source is, for the log and the settings page.</summary>
    string Description { get; }
}

/// <summary>No update channel: the default, so nothing reaches the network by accident.</summary>
public sealed class NullUpdateSource : IUpdateSource
{
    public static readonly NullUpdateSource Instance = new();

    public string Description => "";

    public Task<ReleaseInfo?> CheckAsync(CancellationToken token = default) => Task.FromResult<ReleaseInfo?>(null);

    public Task<Stream> OpenPackageAsync(ReleaseInfo release, CancellationToken token = default) =>
        throw new InvalidOperationException("there is no update source");
}

/// <summary>A folder holding release.json and the package next to it (a share, a USB stick, a test).</summary>
public sealed class FolderUpdateSource : IUpdateSource
{
    private readonly string _manifest;

    /// <param name="manifest">Which file in the folder: release.json, or release-beta.json for the beta track.</param>
    public FolderUpdateSource(string directory, string manifest = "release.json")
    {
        Directory = Path.GetFullPath(directory);
        _manifest = manifest;
    }

    public string Directory { get; }

    public string Description => Directory;

    public Task<ReleaseInfo?> CheckAsync(CancellationToken token = default)
    {
        var manifest = Path.Combine(Directory, _manifest);
        if (!File.Exists(manifest)) return Task.FromResult<ReleaseInfo?>(null);

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifest));
            var release = ReleaseInfo.Parse(document.RootElement);
            return Task.FromResult<ReleaseInfo?>(File.Exists(Package(release)) ? release : null);
        }
        catch (Exception e) when (e is IOException or JsonException or FormatException)
        {
            return Task.FromResult<ReleaseInfo?>(null);
        }
    }

    public Task<Stream> OpenPackageAsync(ReleaseInfo release, CancellationToken token = default) =>
        Task.FromResult<Stream>(File.OpenRead(Package(release)));

    private string Package(ReleaseInfo release) => Path.Combine(Directory, release.PackageUrl);
}

/// <summary>A release.json behind an http(s) address; the package is fetched from the address it names.</summary>
public sealed class HttpUpdateSource : IUpdateSource
{
    private readonly Uri _manifest;
    private readonly HttpClient _client;

    public HttpUpdateSource(string manifestUrl, TimeSpan? timeout = null)
    {
        _manifest = new Uri(manifestUrl, UriKind.Absolute);

        // The plain client. The system proxy decides the route (WinINET on Windows, HTTP_PROXY on
        // Linux), and a failure comes back as an exception for the caller to report: no retry, no
        // fallback to a direct connection, no read timeout of our own.
        _client = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(60) };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("LanternUI");
    }

    public string Description => _manifest.ToString();

    public async Task<ReleaseInfo?> CheckAsync(CancellationToken token = default)
    {
        var json = await _client.GetStringAsync(_manifest, token).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        return ReleaseInfo.Parse(document.RootElement);
    }

    public async Task<Stream> OpenPackageAsync(ReleaseInfo release, CancellationToken token = default)
    {
        var address = new Uri(_manifest, release.PackageUrl);
        var response = await _client.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
    }
}