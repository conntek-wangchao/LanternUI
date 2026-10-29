using System.IO;
using System.Windows.Threading;
using Lantern.UI;

namespace LanternUI.Demo;

/// <summary>The real thing now: settings.json on disk, with the 350 ms debounce the reference uses.</summary>
public sealed class DemoState : IAppState
{
    private static readonly Dictionary<string, string> Defaults = new()
    {
        ["ui_theme"] = "default",
        ["ui_theme_colour"] = "#4C9DFF",
        ["ui_window_buttons"] = "mac",
        ["ui_window_buttons_side"] = "usual",
        ["ui_navigation"] = "tabs",
        ["ui_flow"] = "ltr",
        ["ui_language"] = "en",
        ["data_dir"] = "",
        ["ui_background"] = "custom",
        ["ui_background_image"] = "",
        ["ui_background_blur"] = "strong",
        ["overlay_opacity"] = "80",
        ["volume"] = "70",
        ["resolution"] = "1920x1080",
        ["frame_limit"] = "60",
        ["shader_cache"] = "true",
        ["verbose_log"] = "false",
        ["log_to_file"] = "true",
        ["log_keep_days"] = "7",
        ["ui_update_source"] = "",
        ["update_beta"] = "false",
        ["chart_stream"] = "true",
        ["chart_smooth"] = "true",
        ["register_filter"] = "",
        ["register_width"] = "8",
        ["register_base"] = "dec",
        ["register_file"] = "",
        ["log_on_start"] = "true",
    };

    private readonly JsonSettingsStore _store;
    private DispatcherTimer? _timer;

    public DemoState(string path) => _store = new JsonSettingsStore(path, Defaults);

    public ISettingsStore Settings => _store;

    public string FilePath => _store.Path;

    public event Action? Saved;
    public event Action<string, LogLevel>? Logged;

    public void SaveSoon()
    {
        if (_timer == null)
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            _timer.Tick += (_, _) =>
            {
                _timer!.Stop();
                SaveNow();
            };
        }
        _timer.Stop();
        _timer.Start();
    }

    public void SaveNow()
    {
        _timer?.Stop();
        try
        {
            _store.Save();
            Saved?.Invoke();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log("Could not save settings: " + e.Message, LogLevel.Bad);
        }
    }

    /// <summary>Where lines also go besides the pane (a file sink in the app, a recorder in a test).</summary>
    public ILogSink Sink { get; set; } = NullLogSink.Instance;

    /// <summary>One line: the sink first (so it survives a crash in the pane), then the pane's listeners.</summary>
    public void Log(string message, LogLevel level = LogLevel.Info)
    {
        Sink.Write(DateTime.Now, level, message);
        Logged?.Invoke(message, level);
    }
}
