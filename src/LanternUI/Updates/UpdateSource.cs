using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
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

        var beta = 0;
        if (Text(element, "beta") is { } betaText && !int.TryParse(betaText, out beta)) beta = 0;

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

    /// <summary>
    /// Why the last check failed, or null when it did not. A source that quietly returns "nothing" for
    /// a timeout would leave the window saying "up to date" while the machine is offline.
    /// </summary>
    string? LastError => null;
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
    private const string Manifest = "release.json";

    public FolderUpdateSource(string directory) => Directory = Path.GetFullPath(directory);

    public string Directory { get; }

    public string Description => Directory;

    public Task<ReleaseInfo?> CheckAsync(CancellationToken token = default)
    {
        var manifest = Path.Combine(Directory, Manifest);
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
    private readonly TimeSpan _timeout;
    private HttpClient _client;

    public HttpUpdateSource(string manifestUrl, TimeSpan? timeout = null)
    {
        _manifest = new Uri(manifestUrl, UriKind.Absolute);
        _timeout = timeout ?? TimeSpan.FromSeconds(60);
        _client = NewClient();
    }

    private HttpClient NewClient()
    {
        var client = new HttpClient(Handler()) { Timeout = _timeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LanternUI");
        return client;
    }

    /// <summary>The message plus whatever the transport said inside it.</summary>
    private static string Describe(Exception e)
    {
        var text = e.Message;
        for (var inner = e.InnerException; inner != null; inner = inner.InnerException)
            text += " (" + inner.Message + ")";
        return text;
    }

    public string Description => _manifest.ToString();

    public string? LastError { get; private set; }

    private static SocketsHttpHandler Handler() => new()
    {
        ConnectTimeout = TimeSpan.FromSeconds(10),
        ConnectCallback = async (context, token) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, token).ConfigureAwait(false);
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                // IPv4 first: a host that advertises AAAA while the machine has no IPv6 route would
                // otherwise sit in connect until the timeout expires.
                foreach (var address in addresses.OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1))
                {
                    try
                    {
                        await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), token).ConfigureAwait(false);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch (SocketException)
                    {
                        // try the next address
                    }
                }

                throw new HttpRequestException("no address of " + context.DnsEndPoint.Host + " could be reached");
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };

    public async Task<ReleaseInfo?> CheckAsync(CancellationToken token = default)
    {
        LastError = null;

        // Two attempts: a channel on the far side of a flaky route fails once and answers the next
        // time often enough that one retry turns a red line into a normal check.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var json = await _client.GetStringAsync(_manifest, token).ConfigureAwait(false);
                using var document = JsonDocument.Parse(json);
                return ReleaseInfo.Parse(document.RootElement);
            }
            catch (Exception e) when (e is HttpRequestException or JsonException or FormatException or TaskCanceledException)
            {
                // Remembered, not swallowed: "could not reach the channel" and "nothing published"
                // are different answers and the window must not confuse them.
                LastError = Describe(e);
                if (attempt == 0 && e is not JsonException and not FormatException)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(600), token).ConfigureAwait(false);
                    continue;
                }
            }

            break;
        }

        return null;
    }

    public async Task<Stream> OpenPackageAsync(ReleaseInfo release, CancellationToken token = default)
    {
        var address = new Uri(_manifest, release.PackageUrl);
        Exception? last = null;

        // Three attempts, and each one after a failure gets a fresh pool: the usual cause is a socket
        // the far end or the proxy closed between two requests, which shows up as an SSL handshake
        // error on the *next* request and then works when the connection is new.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, address);
                request.Headers.Accept.ParseAdd("application/octet-stream");
                var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            }
            catch (Exception e) when ((e is HttpRequestException or IOException or TaskCanceledException) && attempt < 2)
            {
                last = e;
                _client.Dispose();
                _client = NewClient();
                await Task.Delay(TimeSpan.FromMilliseconds(500 * (attempt + 1)), token).ConfigureAwait(false);
            }
        }

        throw new HttpRequestException(Describe(last!), last);
    }
}