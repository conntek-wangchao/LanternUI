using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Lantern.UI;
using Xunit;

namespace LanternUI.Tests;

/// <summary>
/// The portable half of visual regression: no pixels, just structure and theme. It answers "is a row
/// missing" and "did the accent stop reaching the cards" on any machine, while the pixel baselines
/// stay authoritative only where they were made. Set LANTERN_UPDATE_SNAPSHOTS=1 to rewrite it.
/// </summary>
public class StructureSnapshotTests
{
    private static string Source => Path.Combine(TestPaths.SnapshotSource, "structure.json");

    [Fact]
    public void Every_page_lists_the_same_rows_and_the_tokens_resolve()
    {
        var json = Sta.Run(Describe);
        if (Environment.GetEnvironmentVariable("LANTERN_UPDATE_SNAPSHOTS") == "1" || !File.Exists(Source))
        {
            Directory.CreateDirectory(TestPaths.SnapshotSource);
            File.WriteAllText(Source, json);
            Assert.Fail("structure snapshot written to " + Source + " — look at it, commit it, run again.");
        }

        Assert.Equal(Normalise(File.ReadAllText(Source)), Normalise(json));
    }

    private static string Normalise(string text) => text.Replace("\r\n", "\n").TrimEnd();

    private static string Describe()
    {
        var config = Path.Combine(Path.GetTempPath(), "lanternui-structure", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(config)!);
        if (File.Exists(config)) File.Delete(config);

        var state = new Demo.DemoState(config);
        state.Settings.Set("ui_background", "none");
        state.Settings.SetBool("log_to_file", false);

        var catalog = JsonCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Localization"));
        var localizer = new Localizer("en", null);
        var shell = new ShellWindow
        {
            App = state, Localizer = localizer, Width = 1020, Height = 760,
            Left = -8000, Top = -8000, ShowInTaskbar = false,
        };
        shell.ApplyLanguage();
        shell.SetPages(new[]
        {
            new PageEntry("appearance", "Appearance", () => new Demo.Pages.AppearancePage(state, shell, localizer, catalog.Languages)),
            new PageEntry("about", "About", () => new Demo.Pages.AboutPage(state, shell, localizer)),
            new PageEntry("shortcuts", "Shortcuts", () => new Demo.Pages.ShortcutsPage(state, shell, localizer)),
            new PageEntry("more", "More", () => new Demo.Pages.MorePage(state, shell, localizer)),
            new PageEntry("list", "List", () => new Demo.Pages.ListDemoPage(state, shell, localizer)),
        });
        shell.Show();

        var pages = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var key in new[] { "appearance", "about", "shortcuts", "more", "list" })
        {
            shell.ShowPage(key);
            shell.UpdateLayout();
            pages[key] = shell.CurrentPage!.SearchRows().Select(row => row.Title).ToArray();
        }
        shell.Close();

        var tokens = new[] { "WindowBaseBrush", "CardBackgroundBrush", "CardStrokeBrush", "AccentBrush", "TextPrimaryBrush", "TextTertiaryBrush" };
        var colors = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var token in tokens)
            colors[token] = ((SolidColorBrush)Application.Current.Resources[token]).Color.ToString();

        var payload = new SortedDictionary<string, object>(StringComparer.Ordinal)
        {
            ["pages"] = pages,
            ["tokens"] = colors,
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }
}
