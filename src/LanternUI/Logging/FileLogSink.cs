using System.Globalization;
using System.IO;
using System.Text;

namespace Lantern.UI;

/// <summary>
/// The activity log on disk: one file per day, a size cap that rolls into a numbered file, and old
/// files dropped on start. Lines are "HH:mm:ss.fff  LEVEL  message" with a fixed-width level so a
/// file can be grepped and read without a parser. Every write is flushed — a log that is lost when
/// the process dies is exactly the log that was needed.
/// </summary>
public sealed class FileLogSink : ILogSink, IDisposable
{
    private readonly object _gate = new();
    private readonly string _prefix;
    private readonly int _keepDays;
    private readonly long _maxBytes;
    private readonly bool _alsoConsole;
    private StreamWriter? _writer;
    private DateOnly _day;
    private int _part;
    private long _written;

    public FileLogSink(string directory, string prefix = "lantern", int keepDays = 7,
        long maxBytes = 2 * 1024 * 1024, bool alsoConsole = false)
    {
        Directory = Path.GetFullPath(directory);
        _prefix = prefix;
        _keepDays = Math.Max(1, keepDays);
        _maxBytes = Math.Max(64 * 1024, maxBytes);
        _alsoConsole = alsoConsole;

        System.IO.Directory.CreateDirectory(Directory);
        Prune();
    }

    /// <summary>Where the current file lives (the crash handler and the status bar show it).</summary>
    public string Directory { get; }

    public string CurrentFile { get; private set; } = "";

    public void Write(DateTime at, LogLevel level, string message)
    {
        var line = string.Format(CultureInfo.InvariantCulture, "{0:HH:mm:ss.fff}  {1,-5}  {2}",
            at, Text(level), message);

        if (_alsoConsole) Console.Error.WriteLine(line);

        lock (_gate)
        {
            Open(at);
            _writer!.WriteLine(line);
            _writer.Flush();
            _written += line.Length + Environment.NewLine.Length;

            // Size cap: today's file rolls into "<name>.2.log", "<name>.3.log" and so on.
            if (_written >= _maxBytes) Close();
        }
    }

    public void Flush()
    {
        lock (_gate) _writer?.Flush();
    }

    public void Dispose()
    {
        lock (_gate) Close();
    }

    private void Open(DateTime at)
    {
        var day = DateOnly.FromDateTime(at);
        if (_writer != null && day == _day) return;

        Close();
        _day = day;
        _part = 0;
        _written = 0;
        OpenWriter();
    }

    private void OpenWriter()
    {
        while (true)
        {
            var name = _part == 0
                ? string.Format(CultureInfo.InvariantCulture, "{0}-{1:yyyyMMdd}.log", _prefix, _day.ToDateTime(TimeOnly.MinValue))
                : string.Format(CultureInfo.InvariantCulture, "{0}-{1:yyyyMMdd}.{2}.log", _prefix, _day.ToDateTime(TimeOnly.MinValue), _part);

            CurrentFile = Path.Combine(Directory, name);
            if (!File.Exists(CurrentFile)) break;

            var size = new FileInfo(CurrentFile).Length;
            if (size >= _maxBytes)
            {
                _part++;
                continue;
            }

            _written = size;
            break;
        }

        // FileShare.ReadWrite: a running launcher must not lock a log that someone is reading.
        _writer = new StreamWriter(new FileStream(CurrentFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite),
            new UTF8Encoding(false));
    }

    private void Close()
    {
        _writer?.Dispose();
        _writer = null;
    }

    /// <summary>Keeps the folder bounded: files older than the retention window are deleted on start.</summary>
    private void Prune()
    {
        var oldest = DateTime.Now.Date.AddDays(-_keepDays);
        foreach (var file in System.IO.Directory.EnumerateFiles(Directory, _prefix + "-*.log"))
        {
            try
            {
                if (File.GetLastWriteTime(file) < oldest) File.Delete(file);
            }
            catch (IOException) { /* another process is reading it; try again next start */ }
            catch (UnauthorizedAccessException) { /* not ours to delete */ }
        }
    }

    private static string Text(LogLevel level) => level switch
    {
        LogLevel.Good => "good",
        LogLevel.Bad => "bad",
        LogLevel.Warn => "warn",
        _ => "info",
    };
}
