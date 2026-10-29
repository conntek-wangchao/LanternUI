using System.IO;
using Lantern.UI;

namespace LanternUI.Demo;

/// <summary>What the localization self-check found.</summary>
public sealed record I18nReport(
    int SourceKeys,
    IReadOnlyList<LanguageGap> Gaps,
    IReadOnlyList<string> MissedRequired,
    string Directory)
{
    public bool Problems => Gaps.Any(gap => gap.Broken.Count > 0) || MissedRequired.Count > 0 ||
                            Gaps.Any(gap => gap.Missing.Count > 0);

    public int ExitCode => Problems ? 1 : 0;
}

/// <summary>
/// Builds the whole interface off screen with a recording localizer and diffs the keys it asked for
/// against every language file. It also drives the paths that only run on an event (the saved label,
/// an empty search result, the restart question, the slider range note), and fails when one of the
/// framework's own messages was not reached at all — otherwise a message can silently drop out of
/// the harvest and the gate goes green over an incomplete key list.
/// </summary>
public static class I18nCheck
{
    /// <summary>Framework and app messages the harvest must always reach.</summary>
    public static readonly IReadOnlyList<string> RequiredKeys = new[]
    {
        "Search settings  (Ctrl+F)",
        "Activity log",
        "Clear",
        "Log",
        "Ready",
        "Saved",
        "Nothing found for \u201c{0}\u201d",
        "OK",
        "Cancel",
        "Change language",
        "Restart the launcher to apply the language.",
        "Restart now",
        "Later",
        "Kept at {0} (the range is {1} to {2})",
        "Options: ",
        "Notification",
        "A notification card, shown in the corner.",
    };

    public static I18nReport Run(JsonCatalog catalog, DemoState state, string? required = null)
    {
        var recorder = new RecordingLocalizer(NullLocalizer.Instance);

        var shell = new ShellWindow { App = state, Localizer = recorder };
        shell.ApplyLanguage();
        shell.SetPages(new[]
        {
            new PageEntry("appearance", "Appearance", () => new Pages.AppearancePage(state, shell, recorder, catalog.Languages)),
            new PageEntry("about", "About", () => new Pages.AboutPage(state, shell, recorder)),
            new PageEntry("shortcuts", "Shortcuts", () => new Pages.ShortcutsPage(state, shell, recorder)),
            new PageEntry("more", "More", () => new Pages.MorePage(state, shell, recorder)),
            new PageEntry("list", "List", () => new Pages.ListDemoPage(state, shell, recorder)),
        });

        foreach (var key in new[] { "appearance", "about", "shortcuts", "more", "list" }) shell.ShowPage(key);

        var directory = Path.GetDirectoryName(Path.GetFullPath(state.FilePath))!;
        Directory.CreateDirectory(directory);

        // Event-only text: nothing here is reached by building the pages.
        shell.ShowSaved();
        shell.RunSearch("zzz\u0000nohit");

        var beforePath = Prompt.SnapshotPath;
        var beforeLocalizer = Prompt.Localizer;
        Prompt.SnapshotPath = Path.Combine(directory, "prompt-check.png");
        Prompt.Localizer = recorder;
        Pages.AppearancePage.AskRestart(recorder);
        // A snippet built from a choice's options reaches "Options: ".
        var probe = new Pages.AppearancePage(state, shell, recorder, catalog.Languages);
        SearchScorer.Find(new[] { new Place("Appearance", probe) }, "crimson", recorder);

        // Confirm with no explicit labels: this is what reaches the localized "OK" and "Cancel".
        Prompt.Confirm(recorder.T("Notification"), recorder.T("A notification card, shown in the corner."));
        PageBase.RangeHint(recorder, "100", "0", "100");
        Prompt.Localizer = beforeLocalizer;
        Prompt.SnapshotPath = beforePath;

        var keys = recorder.Keys.ToList();
        var missed = RequiredKeys.Where(key => !recorder.Keys.Contains(key)).ToList();

        var gaps = CatalogReport.Gaps(catalog, keys);
        File.WriteAllText(Path.Combine(directory, "i18n-report.json"),
            CatalogReport.ToJson(catalog, keys), new System.Text.UTF8Encoding(true));

        var must = (required ?? string.Join(",", catalog.Languages
                .Where(language => !string.Equals(language.Code, "en", StringComparison.OrdinalIgnoreCase))
                .Select(language => language.Code)))
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var failed = CatalogReport.HasProblems(gaps, must) || missed.Count > 0;

        var summary = new List<string>
        {
            "source keys: " + keys.Count,
            "required:    " + string.Join(", ", must),
            "",
            string.Format("{0,-6} {1,-10} {2,8} {3,8}", "lang", "file", "missing", "broken"),
        };
        foreach (var gap in gaps)
            summary.Add(string.Format("{0,-6} {1,-10} {2,8} {3,8}", gap.Code, gap.HasFile ? "yes" : "no", gap.Missing.Count, gap.Broken.Count));

        var broken = gaps.SelectMany(gap => gap.Broken.Select(issue => (gap.Code, issue))).ToList();
        if (broken.Count > 0)
        {
            summary.Add("");
            summary.Add("placeholder problems (a key's tokens must be kept exactly):");
            foreach (var (code, issue) in broken)
                summary.Add(string.Format("  [{0}] \"{1}\"  expected [{2}]  got [{3}]", code, Trim(issue.Key), issue.Expected, issue.Actual));
        }

        if (missed.Count > 0)
        {
            summary.Add("");
            summary.Add("messages the harvest never reached (add the path to I18nCheck.Run):");
            foreach (var key in missed) summary.Add("  " + key);
        }

        summary.Add("");
        summary.Add(failed ? "FAILED: the i18n gate is not met." : "OK: the i18n gate is met.");

        File.WriteAllLines(Path.Combine(directory, "i18n-report.txt"), summary, new System.Text.UTF8Encoding(true));
        return new I18nReport(keys.Count, gaps, missed, directory);
    }

    private static string Trim(string text) => text.Length <= 60 ? text : text[..57] + "...";
}
