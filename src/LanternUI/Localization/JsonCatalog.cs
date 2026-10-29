using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Lantern.UI;

/// <summary>A language the UI can be shown in; English is the source and needs no file.</summary>
public sealed record LanguageOption(string Code, string Name);

/// <summary>
/// Localization/&lt;code&gt;.json beside the application: one flat map per language. The code calls
/// T("English source"), so the reading stays pleasant — but the files are keyed by a stable id from
/// keys.json, which means editing the English text (or renaming a key) no longer throws away the
/// translations. A language file keyed by English still works: the old form is the fallback.
/// </summary>
public sealed class JsonCatalog
{
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _byCode = new(StringComparer.OrdinalIgnoreCase);

    private JsonCatalog(IReadOnlyList<LanguageOption> languages) => Languages = languages;

    public IReadOnlyList<LanguageOption> Languages { get; }

    public string Directory { get; private set; } = "";

    /// <summary>Stable id per English source text (empty when keys.json is absent).</summary>
    public IReadOnlyDictionary<string, string> Ids { get; private set; } = new Dictionary<string, string>();

    public static IReadOnlyList<LanguageOption> DefaultLanguages => new[]
    {
        new LanguageOption("en", "English"),
        new LanguageOption("zh-CN", "简体中文"),
        new LanguageOption("zh-TW", "繁體中文"),
        new LanguageOption("ja", "日本語"),
        new LanguageOption("de", "Deutsch"),
    };

    /// <summary>Reads every language file it finds; a damaged file is skipped, not fatal.</summary>
    public static JsonCatalog Load(string directory, IReadOnlyList<LanguageOption>? languages = null)
    {
        var list = languages ?? DefaultLanguages;
        var catalog = new JsonCatalog(list) { Directory = directory };
        var keys = ReadKeys(directory);

        catalog.Ids = keys
            .SelectMany(pair => pair.Value.Select(text => (Text: text, Id: pair.Key)))
            .GroupBy(pair => pair.Text, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Id, StringComparer.Ordinal);

        foreach (var language in list)
        {
            if (string.Equals(language.Code, "en", StringComparison.OrdinalIgnoreCase)) continue;
            var file = Path.Combine(directory, language.Code + ".json");
            if (!File.Exists(file)) continue;
            try
            {
                if (JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file)) is { } map)
                    catalog._byCode[language.Code] = keys.Count == 0 ? map : Translate(map, keys);
            }
            catch (Exception e) when (e is IOException or JsonException)
            {
                // A broken translation must not stop the application: this language falls back to English.
            }
        }

        return catalog;
    }

    public IReadOnlyDictionary<string, string>? For(string code) =>
        _byCode.TryGetValue(code, out var map) ? map : null;

    /// <summary>keys.json: id -> [English source, ...former spellings]. An array keeps old call sites alive.</summary>
    private static Dictionary<string, string[]> ReadKeys(string directory)
    {
        var result = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var file = Path.Combine(directory, "keys.json");
        if (!File.Exists(file)) return result;

        try
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(file));
            if (raw == null) return result;

            foreach (var (id, element) in raw)
            {
                var texts = element.ValueKind switch
                {
                    JsonValueKind.String => new[] { element.GetString() ?? "" },
                    JsonValueKind.Array => element.EnumerateArray()
                        .Select(item => item.GetString() ?? "")
                        .Where(text => text.Length > 0)
                        .ToArray(),
                    _ => Array.Empty<string>(),
                };

                if (texts.Length > 0) result[id] = texts;
            }
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            // No usable key table: every file is read in the old English-keyed form.
        }

        return result;
    }

    /// <summary>id-keyed file -> the English-keyed map the rest of the framework speaks.</summary>
    private static Dictionary<string, string> Translate(Dictionary<string, string> file, Dictionary<string, string[]> keys)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, translation) in file)
        {
            // An id the table does not know is kept verbatim: it shows up as an unused entry, not as a crash.
            if (!keys.TryGetValue(id, out var texts))
            {
                map[id] = translation;
                continue;
            }

            foreach (var text in texts) map[text] = translation;
        }

        return map;
    }
}

/// <summary>One language's strings over the English source; records the keys it had to fall back on.</summary>
public sealed class Localizer : ILocalizer
{
    private readonly IReadOnlyDictionary<string, string>? _strings;
    private readonly Dictionary<string, string>? _loose;

    public Localizer(string code, IReadOnlyDictionary<string, string>? strings)
    {
        Code = code;
        _strings = strings;

        // A call site that adds a full stop to a sentence the table spells without one is not worth a
        // string that stays in English: the lookup retries on a form that ignores case, spacing and
        // trailing punctuation.
        if (strings is { Count: > 0 })
        {
            _loose = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (key, value) in strings) _loose.TryAdd(Loose(key), value);
        }
    }

    private static string Loose(string text) =>
        string.Join(' ', text.Trim().TrimEnd('.', '。', '!', '?', '…').Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant();

    public string Code { get; }

    /// <summary>True when this language has a catalog at all (English has none: it is the source).</summary>
    public bool Translated => _strings is { Count: > 0 };

    /// <summary>Keys this language lacks, in the order they were first asked for.</summary>
    public List<string> Missing { get; } = new();

    public string T(string text)
    {
        if (_strings is { Count: > 0 } strings)
        {
            if (strings.TryGetValue(text, out var translated)) return translated;
            if (_loose != null && _loose.TryGetValue(Loose(text), out var near)) return near;
        }

        if (Translated && !Missing.Contains(text)) Missing.Add(text);
        return text;
    }

    public string F(FormattableString text) =>
        string.Format(CultureInfo.CurrentCulture, T(text.Format), text.GetArguments());
}

/// <summary>Remembers every key it is asked for: the i18n self-check builds the UI with this one.</summary>
public sealed class RecordingLocalizer : ILocalizer
{
    private readonly ILocalizer _inner;

    public RecordingLocalizer(ILocalizer inner) => _inner = inner;

    public SortedSet<string> Keys { get; } = new(StringComparer.Ordinal);

    public string T(string text)
    {
        Keys.Add(text);
        return _inner.T(text);
    }

    public string F(FormattableString text)
    {
        Keys.Add(text.Format);
        return _inner.F(text);
    }
}

/// <summary>A key whose {placeholders} do not match the source: the translation would print a wrong value.</summary>
public sealed record PlaceholderIssue(string Key, string Expected, string Actual);

/// <summary>What one language is missing or broke, for the report and the gate.</summary>
public sealed record LanguageGap(string Code, string Name, bool HasFile, List<string> Missing, List<PlaceholderIssue> Broken);

/// <summary>The i18n gate's arithmetic, kept out of the demo so a test can call it too.</summary>
public static class CatalogReport
{
    private static readonly System.Text.RegularExpressions.Regex Token =
        new(@"\{\d+[^}]*\}", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>The {0} tokens of a string, in order.</summary>
    public static List<string> Placeholders(string text) => Token.Matches(text).Select(match => match.Value).ToList();

    public static List<LanguageGap> Gaps(JsonCatalog catalog, IEnumerable<string> used)
    {
        var keys = used.Distinct(StringComparer.Ordinal).ToList();
        var gaps = new List<LanguageGap>();

        foreach (var language in catalog.Languages)
        {
            if (string.Equals(language.Code, "en", StringComparison.OrdinalIgnoreCase)) continue;

            var map = catalog.For(language.Code);
            var missing = new List<string>();
            var broken = new List<PlaceholderIssue>();

            foreach (var key in keys)
            {
                if (map == null || !map.TryGetValue(key, out var text))
                {
                    missing.Add(key);
                    continue;
                }

                var expected = Placeholders(key);
                var actual = Placeholders(text);
                if (!expected.SequenceEqual(actual))
                    broken.Add(new PlaceholderIssue(key, string.Join(" ", expected), string.Join(" ", actual)));
            }

            gaps.Add(new LanguageGap(language.Code, language.Name, map != null, missing, broken));
        }

        return gaps;
    }

    /// <summary>True when every required language has a file with no gap.</summary>
    public static bool HasProblems(IEnumerable<LanguageGap> gaps, IEnumerable<string> required)
    {
        var must = required.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return gaps.Any(gap => must.Contains(gap.Code) &&
                               (!gap.HasFile || gap.Missing.Count > 0 || gap.Broken.Count > 0));
    }

    /// <summary>The machine-readable report the CI keeps.</summary>
    public static string ToJson(JsonCatalog catalog, IEnumerable<string> used)
    {
        var keys = used.Distinct(StringComparer.Ordinal).ToList();
        var gaps = Gaps(catalog, keys);

        var payload = new
        {
            keys,
            ids = keys.ToDictionary(key => key, key => catalog.Ids.TryGetValue(key, out var id) ? id : "", StringComparer.Ordinal),
            languages = gaps.Select(gap => new
            {
                code = gap.Code,
                name = gap.Name,
                file = gap.HasFile,
                keys = gap.Missing,
                placeholders = gap.Broken.Select(issue => new { key = issue.Key, expected = issue.Expected, actual = issue.Actual }),
            }),
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }
}