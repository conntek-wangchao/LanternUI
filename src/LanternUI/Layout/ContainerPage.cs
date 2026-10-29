using System.Windows;
using System.Windows.Controls;

namespace Lantern.UI;

/// <summary>A page that hosts others: the shell asks it for the places the search should look through.</summary>
public interface IPlaceHost
{
    IEnumerable<Place> Places();
}

/// <summary>
/// A container page: a switch at the top and one section shown under it. Each section is a page of
/// its own and a place of its own in the settings search ("More › Mods"), which is how one tab can
/// hold several settings pages without flattening them into one list.
/// </summary>
public abstract class ContainerPage : PageBase, IPlaceHost
{
    private readonly List<(string Key, string Title, Func<PageBase> Create)> _sections = new();
    private readonly Dictionary<string, PageBase> _made = new(StringComparer.Ordinal);
    private readonly ContentControl _switch = new() { Focusable = false, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly ContentControl _host = new() { Focusable = false };
    private Segmented? _selector;
    private string? _current;

    protected ContainerPage(IAppState app, ILocalizer? localizer, string? title, string? subtitle,
        UiLayout layout = UiLayout.Modern)
        : base(app, localizer, title, subtitle, layout)
    {
        var stack = new StackPanel { Margin = new Thickness(20, 8, 20, 12) };
        stack.Children.Add(_switch);
        // The hosted page brings its own 20 px margin; cancel it so both edges line up.
        _host.Margin = new Thickness(-20, 0, -20, 0);
        stack.Children.Add(_host);
        ReplaceBody(stack);
    }

    public IReadOnlyList<string> SectionKeys => _sections.Select(section => section.Key).ToList();

    /// <summary>The section on screen, or null before the first one.</summary>
    public string? CurrentSection => _current;

    /// <summary>Which pill the switch shows as selected (-1 when nothing is).</summary>
    public int SelectedSectionIndex => _selector?.SelectedIndex ?? -1;

    protected void AddSection(string key, string title, Func<PageBase> create)
    {
        _sections.Add((key, title, create));

        // The switch is rebuilt for the new list of sections, so it comes back with nothing checked:
        // whatever is already on screen has to be marked again, or the page shows one section while
        // the switch shows none.
        _selector = null;
        EnsureSelector();
        if (_current == null) ShowSection(key);
        else SelectCurrent();
    }

    private void EnsureSelector()
    {
        if (_selector != null || _sections.Count == 0) return;

        var choices = _sections.Select(section => new Choice(section.Title, section.Key)).ToArray();
        var segmented = new Segmented(choices);
        segmented.Changed += index => ShowSection(_sections[index].Key);
        _switch.Content = segmented;
        _selector = segmented;
        SelectCurrent();
    }

    private void SelectCurrent()
    {
        var at = _sections.FindIndex(section => section.Key == _current);
        if (at >= 0) _selector?.Select(at, notify: false);
    }

    /// <summary>Showing the page again puts the switch back in step with the section on screen.</summary>
    public override void OnShown()
    {
        base.OnShown();
        SelectCurrent();
    }

    public void ShowSection(string key)
    {
        var at = _sections.FindIndex(section => section.Key == key);
        if (at < 0) return;

        if (!_made.TryGetValue(key, out var page))
        {
            page = _sections[at].Create();
            _made[key] = page;
        }

        _host.Content = page;
        page.OnShown();
        _current = key;
        _selector?.Select(at, notify: false);
    }

    public IEnumerable<Place> Places()
    {
        foreach (var section in _sections)
        {
            if (!_made.TryGetValue(section.Key, out var page))
            {
                page = section.Create();
                _made[section.Key] = page;
            }

            var key = section.Key;
            yield return new Place(section.Title, page, () => ShowSection(key));
        }
    }
}
