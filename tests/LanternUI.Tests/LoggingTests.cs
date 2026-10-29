using System.IO;
using Lantern.UI;
using Xunit;

namespace LanternUI.Tests;

/// <summary>
/// The activity log has to survive the process: a bug report without the file is not actionable.
/// These cover the sink itself (one line per entry, the size cap, the retention window) and the
/// fan-out from the state, which is what the window calls.
/// </summary>
public class LoggingTests
{
    private static string NewDirectory() =>
        Path.Combine(Path.GetTempPath(), "lanternui-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void A_sink_writes_the_time_the_level_and_the_message()
    {
        var directory = NewDirectory();
        using var sink = new FileLogSink(directory, "test");

        sink.Write(new DateTime(2026, 3, 4, 10, 11, 12, 345), LogLevel.Bad, "boom");
        sink.Flush();

        var file = Assert.Single(Directory.GetFiles(directory, "test-*.log"));

        // Read while the sink still holds it: that is what "look at the log of a running app" is.
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd();
        Assert.Contains("10:11:12.345", text);
        Assert.Contains("bad", text);
        Assert.Contains("boom", text);
    }

    [Fact]
    public void The_file_rolls_over_at_the_size_cap()
    {
        var directory = NewDirectory();
        using var sink = new FileLogSink(directory, "test", maxBytes: 64 * 1024);

        for (var index = 0; index < 2000; index++) sink.Write(DateTime.Now, LogLevel.Info, new string('x', 100));
        sink.Flush();

        Assert.True(Directory.GetFiles(directory, "test-*.log").Length >= 2,
            "files: " + string.Join(", ", Directory.GetFiles(directory, "test-*.log").Select(Path.GetFileName)));
    }

    [Fact]
    public void Files_older_than_the_retention_window_are_deleted_on_start()
    {
        var directory = NewDirectory();
        Directory.CreateDirectory(directory);
        var old = Path.Combine(directory, "test-20200101.log");
        File.WriteAllText(old, "old");
        File.SetLastWriteTime(old, DateTime.Now.AddDays(-30));

        using var sink = new FileLogSink(directory, "test", keepDays: 7);
        sink.Flush();

        Assert.False(File.Exists(old));
    }

    [Fact]
    public void The_state_writes_to_its_sink_before_the_pane()
    {
        var state = new Demo.DemoState(Path.Combine(NewDirectory() + ".json"));
        var sink = new RecordingSink();
        state.Sink = sink;

        var pane = new List<string>();
        state.Logged += (message, _) => pane.Add(message);

        state.Log("hello", LogLevel.Warn);

        Assert.Single(sink.Lines);
        Assert.Contains("hello", sink.Lines[0]);
        Assert.Contains("Warn", sink.Lines[0]);
        Assert.Single(pane);
    }

    private sealed class RecordingSink : ILogSink
    {
        public List<string> Lines { get; } = new();

        public void Write(DateTime at, LogLevel level, string message) => Lines.Add(level + ": " + message);

        public void Flush() { }
    }
}
