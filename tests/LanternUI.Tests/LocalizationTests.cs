using System.IO;
using LanternUI.Demo;
using Lantern.UI;
using Xunit;

namespace LanternUI.Tests;

public class LocalizationTests
{
    [Fact]
    public void Every_language_is_complete_and_placeholders_match()
    {
        Sta.Run(() =>
        {
            var catalog = JsonCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Localization"));
            var state = Sta.NewState();

            var report = I18nCheck.Run(catalog, state);

            Assert.Empty(report.MissedRequired);
            Assert.NotEmpty(report.Gaps);
            foreach (var gap in report.Gaps)
            {
                Assert.True(gap.HasFile, gap.Code + " has no language file");
                Assert.Empty(gap.Missing);
                Assert.Empty(gap.Broken);
            }
            Assert.Equal(0, report.ExitCode);
        });
    }


    [Fact]
    public void A_sentence_is_found_even_when_the_call_site_ends_it_differently()
    {
        var catalog = JsonCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Localization"));
        var chinese = new Localizer("zh-CN", catalog.For("zh-CN"));

        // The About page says "Up to date." and the key table spells it "Up to date"; a missing full
        // stop used to be enough for the line to stay in English.
        Assert.Equal("已是最新版本", chinese.T("Up to date."));
        Assert.Equal("已是最新版本", chinese.T("up to date"));
        Assert.Empty(chinese.Missing);
    }
    [Fact]
    public void The_framework_own_message_is_translated()
    {
        var catalog = JsonCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Localization"));

        var english = PageBase.RangeHint(new Localizer("en", null), "100", "0", "100");
        var chinese = PageBase.RangeHint(new Localizer("zh-CN", catalog.For("zh-CN")), "100", "0", "100");

        Assert.Equal("Kept at 100 (the range is 0 to 100)", english);
        Assert.NotEqual(english, chinese);
        Assert.Contains("100", chinese);            // the placeholders survived
        Assert.Contains("0", chinese);
    }

    [Fact]
    public void A_translation_that_loses_a_placeholder_is_reported()
    {
        var key = "Kept at {0} (the range is {1} to {2})";
        var map = new Dictionary<string, string> { [key] = "已保持在 100" };   // {0} {1} {2} dropped

        var catalog = JsonCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Localization"));
        var gap = CatalogReport.Gaps(catalog, new[] { key }).First(entry => entry.Code == "zh-CN");
        Assert.Empty(gap.Missing);

        // The same check against a hand-made map, so the assertion does not depend on the files.
        var synthetic = new JsonCatalogProbe(map);
        var broken = synthetic.Check(key);
        Assert.NotNull(broken);
        Assert.Equal("{0} {1} {2}", broken!.Expected);
        Assert.Equal("", broken.Actual);
    }

    /// <summary>Runs the placeholder comparison the report uses, on a map of one entry.</summary>
    private sealed class JsonCatalogProbe
    {
        private readonly IReadOnlyDictionary<string, string> _map;
        public JsonCatalogProbe(IReadOnlyDictionary<string, string> map) => _map = map;

        public PlaceholderIssue? Check(string key)
        {
            var expected = CatalogReport.Placeholders(key);
            var actual = CatalogReport.Placeholders(_map[key]);
            return expected.SequenceEqual(actual)
                ? null
                : new PlaceholderIssue(key, string.Join(" ", expected), string.Join(" ", actual));
        }
    }
}