using System.IO;
using System.Text;
using Lantern.UI;
using Xunit;

namespace LanternUI.Tests;

/// <summary>
/// Keys are stable ids now: the code still says T("Interface language") — readable — but the files are
/// keyed by keys.json, so editing the English (or renaming the key) no longer throws the translations
/// away. The former spellings stay alive through the array form.
/// </summary>
public class StableKeyTests
{
    private static string NewCatalog(string? keys, string language)
    {
        var directory = Path.Combine(Path.GetTempPath(), "lanternui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        if (keys != null) File.WriteAllText(Path.Combine(directory, "keys.json"), keys, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(directory, "zh-CN.json"), language, new UTF8Encoding(false));
        return directory;
    }

    [Fact]
    public void Editing_the_english_keeps_the_translation()
    {
        var directory = NewCatalog("""{"appearance.language": "Interface language"}""", """{"appearance.language": "界面语言"}""");
        var catalog = JsonCatalog.Load(directory);

        Assert.Equal("appearance.language", catalog.Ids["Interface language"]);

        var localizer = new Localizer("zh-CN", catalog.For("zh-CN"));
        Assert.Equal("界面语言", localizer.T("Interface language"));
        Assert.Empty(localizer.Missing);
    }

    [Fact]
    public void A_former_spelling_still_resolves()
    {
        var directory = NewCatalog("""{"appearance.language": ["Interface language", "Language"]}""",
            """{"appearance.language": "界面语言"}""");
        var catalog = JsonCatalog.Load(directory);
        var localizer = new Localizer("zh-CN", catalog.For("zh-CN"));

        Assert.Equal("界面语言", localizer.T("Interface language"));
        Assert.Equal("界面语言", localizer.T("Language"));   // the old call site keeps working
        Assert.Empty(localizer.Missing);
    }

    [Fact]
    public void A_file_without_a_key_table_is_read_the_old_way()
    {
        var directory = NewCatalog(null, """{"Language": "界面语言"}""");
        var catalog = JsonCatalog.Load(directory);
        var localizer = new Localizer("zh-CN", catalog.For("zh-CN"));

        Assert.Equal("界面语言", localizer.T("Language"));
        Assert.Empty(catalog.Ids);
    }

    [Fact]
    public void The_shipped_catalog_covers_every_used_key_with_an_id()
    {
        var catalog = JsonCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Localization"));
        var translation = catalog.For("de");
        Assert.NotNull(translation);

        // Every translation resolves back to an English source, and every one of those has an id.
        foreach (var entry in translation!)
            Assert.True(catalog.Ids.ContainsKey(entry.Key), "no id for: " + entry.Key);
    }
}
