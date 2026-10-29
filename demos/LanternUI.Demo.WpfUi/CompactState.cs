using System.IO;
using Lantern.UI;

namespace LanternUI.Demo.WpfUi;

/// <summary>The smallest IAppState that still saves like the real one.</summary>
public sealed class CompactState : IAppState
{
    private readonly JsonSettingsStore _store = new(
        Path.Combine(Path.GetTempPath(), "lanternui-wpfui", "settings.json"),
        new Dictionary<string, string> { ["demo"] = "true" });

    public ISettingsStore Settings => _store;
    public event Action? Saved;
    public event Action<string, LogLevel>? Logged;

    public void SaveSoon() => SaveNow();

    public void SaveNow()
    {
        _store.Save();
        Saved?.Invoke();
    }

    public void Log(string message, LogLevel level = LogLevel.Info) => Logged?.Invoke(message, level);
}
