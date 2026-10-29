using System.IO;
using System.Text.Json;
using Lantern.UI;
using Xunit;

namespace LanternUI.Tests;

/// <summary>A download that stops half way has to fail and start over, not wait forever.</summary>
public class StallTests
{
    /// <summary>Hands out a little data and then blocks, the way a dropped proxy tunnel does.</summary>
    private sealed class Stalls : Stream
    {
        private int _reads;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            if (_reads++ == 0)
            {
                buffer.Span[..4].Fill(1);
                return 4;
            }

            await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
            return 0;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set { } }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public async Task A_stream_that_stops_sending_fails_instead_of_hanging()
    {
        await using var stalled = new StalledStream(new Stalls(), TimeSpan.FromMilliseconds(200));

        var buffer = new byte[64];
        Assert.Equal(4, await stalled.ReadAsync(buffer));

        var error = await Assert.ThrowsAsync<IOException>(async () => await stalled.ReadAsync(buffer));
        Assert.Contains("stopped", error.Message);
    }

    private sealed class StallingSource : IUpdateSource
    {
        public string Description => "a source that stalls";
        public Task<ReleaseInfo?> CheckAsync(CancellationToken token = default) => Task.FromResult<ReleaseInfo?>(null);
        public Task<Stream> OpenPackageAsync(ReleaseInfo release, CancellationToken token = default) =>
            Task.FromResult<Stream>(new StalledStream(new Stalls(), TimeSpan.FromMilliseconds(200)));
    }

    [Fact]
    public async Task A_download_that_stalls_is_reported_as_failed_and_tried_again()
    {
        var work = Path.Combine(Path.GetTempPath(), "lanternui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var service = new UpdateService(new StallingSource(), work, new Version(1, 0, 0));

        var release = new ReleaseInfo(new Version(2, 0, 0), 0, "", "app.zip", new string('a', 64), 1024, DateTime.UtcNow);
        var opened = 0;
        service.DownloadProgress += (_, _) => opened++;

        var result = await service.DownloadAsync(release);

        Assert.False(result.Ok);
        Assert.Equal(UpdateStage.Failed, service.Stage);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
        // Every attempt reports the first byte again, so a restart is visible on the bar.
        Assert.True(opened >= 2, "the progress was never restarted");
    }
}