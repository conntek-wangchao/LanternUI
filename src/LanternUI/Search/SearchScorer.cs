namespace Lantern.UI;

/// <summary>One page the search looks through: its name in the results and how to bring it up.</summary>
public sealed record Place(string Name, PageBase Page, Action? Show = null);

/// <summary>One setting a query found.</summary>
public sealed record SearchHit(SettingRow Row, Place Place, string? Snippet, int Score);

/// <summary>
/// The search itself: no list of settings to keep, because every page's rows are already the index
/// (PageBase.SearchRows). Ranking: title prefix, then a title word start, then anywhere in the
/// title, then every word in the title, then text found only in the description, the status line,
/// a choice's options, the group or the page name.
/// </summary>
public static class SearchScorer
{
    public const int MaxHits = 8;

    public static List<SearchHit> Find(IEnumerable<Place> places, string query, ILocalizer localizer)
    {
        query = query.Trim();
        var hits = new List<SearchHit>();
        if (query.Length == 0) return hits;

        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var place in places)
            foreach (var row in place.Page.SearchRows())
                if (Match(row, place, query, words, localizer) is { } hit)
                    hits.Add(hit);

        return hits.OrderByDescending(hit => hit.Score).Take(MaxHits).ToList();
    }

    private static SearchHit? Match(SettingRow row, Place place, string query, string[] words, ILocalizer localizer)
    {
        var title = row.Title;
        var at = title.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (at == 0) return new SearchHit(row, place, null, 100);
        if (at > 0)
            return new SearchHit(row, place, null, char.IsLetterOrDigit(title[at - 1]) ? 60 : 80);

        var info = row.Info ?? "";
        var hint = row.Hint.Visibility == System.Windows.Visibility.Visible ? row.Hint.Text : "";
        var section = row.Section ?? "";

        if (!words.All(word => Has(title, word) || Has(info, word) || Has(hint, word) ||
                               Has(row.Choices, word) || Has(section, word) || Has(place.Name, word)))
            return null;

        var inTitle = words.Count(word => Has(title, word));
        if (inTitle == words.Length) return new SearchHit(row, place, null, 50);

        bool Shows(string text) => words.Any(word => !Has(title, word) && Has(text, word));
        var snippet = Shows(info) ? info
            : Shows(hint) ? hint
            : Shows(row.Choices) ? localizer.T("Options: ") + row.Choices
            : null;

        return new SearchHit(row, place, snippet, 10 + 10 * inTitle);
    }

    private static bool Has(string text, string word) => text.Contains(word, StringComparison.OrdinalIgnoreCase);
}
