using Lantern.UI;

namespace LanternUI.Demo.Pages;

/// <summary>
/// A container: one tab holding two settings pages. Each section is its own place in the search
/// ("More › Mods"), so a nested page is as findable as a top-level one.
/// </summary>
public sealed class MorePage : ContainerPage
{
    public MorePage(IAppState app, IShell shell, ILocalizer loc)
        : base(app, loc, loc.T("More"), loc.T("Sections of one page: each is its own place in the search."), shell.Layout)
    {
        AddSection("mods", loc.T("Mods"), () => new SectionPage(app, loc, shell, loc.T("Mods"),
            loc.T("Mods are folders of files that replace the game's own."),
            new[] { (loc.T("Install order"), new[] { loc.T("Top first"), loc.T("Bottom first") }) }));

        AddSection("cheats", loc.T("Cheats"), () => new SectionPage(app, loc, shell, loc.T("Cheats"),
            loc.T("Cheats change the running game and are not saved."),
            new[] { (loc.T("Infinite health"), new[] { loc.T("Off"), loc.T("On") }) }));
    }
}
