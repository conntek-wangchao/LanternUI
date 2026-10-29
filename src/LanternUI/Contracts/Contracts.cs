namespace Lantern.UI;

public enum LogLevel
{
    Info,
    Step,
    Good,
    Warn,
    Bad
}

public enum UiLayout
{
    Modern,
    Classic
}

/// <summary>One option of a combo/segmented row.</summary>
public sealed record Choice(string Label, string Value, string? Note = null);

/// <summary>A page the shell can show.</summary>
public sealed record PageEntry(string Key, string Title, Func<PageBase> Create);

public interface ISettingsStore
{
    string Str(string key);
    void Set(string key, string value);
    bool Bool(string key);
    void SetBool(string key, bool value);
}

/// <summary>The state a page reads and writes: settings, saving, the activity log.</summary>
public interface IAppState
{
    ISettingsStore Settings { get; }
    event Action? Saved;
    event Action<string, LogLevel>? Logged;
    void SaveSoon();
    void SaveNow();
    void Log(string message, LogLevel level = LogLevel.Info);
}

/// <summary>Presentation-text translation, keyed by the English source string.</summary>
public interface ILocalizer
{
    string T(string text);
    string F(FormattableString text);
}

public sealed class NullLocalizer : ILocalizer
{
    public static readonly NullLocalizer Instance = new();
    public string T(string text) => text;
    public string F(FormattableString text) =>
        string.Format(System.Globalization.CultureInfo.CurrentCulture, text.Format, text.GetArguments());
}

/// <summary>The window a page can ask to restyle itself (the page never touches the shell directly).</summary>
public interface IShell
{
    UiLayout Layout { get; }
    void ApplyTheme();
    void ApplyBackdrop();
    void ApplyWindowButtons();
    void ApplyNavigation();
    void ApplyLayout(UiLayout layout);
    void ApplyFlowDirection(bool rightToLeft);
    void ShowPage(string key);

    /// <summary>A notification card in the corner of the window.</summary>
    void Notify(string title, string body);
}

/// <summary>
/// Somewhere a log line can go. The shell keeps the last few hundred lines in memory for the pane;
/// a sink is what makes them survive the process — a bug report is worth nothing without the file.
/// </summary>
public interface ILogSink
{
    void Write(DateTime at, LogLevel level, string message);

    void Flush();
}

/// <summary>A sink that drops everything (the default, so nothing logs by accident).</summary>
public sealed class NullLogSink : ILogSink
{
    public static readonly NullLogSink Instance = new();

    private NullLogSink() { }

    public void Write(DateTime at, LogLevel level, string message) { }

    public void Flush() { }
}

/// <summary>Several sinks as one: the pane and the file both get the line.</summary>
public sealed class MultiLogSink : ILogSink
{
    private readonly ILogSink[] _sinks;

    public MultiLogSink(params ILogSink[] sinks) => _sinks = sinks;

    public void Write(DateTime at, LogLevel level, string message)
    {
        foreach (var sink in _sinks) sink.Write(at, level, message);
    }

    public void Flush()
    {
        foreach (var sink in _sinks) sink.Flush();
    }
}
