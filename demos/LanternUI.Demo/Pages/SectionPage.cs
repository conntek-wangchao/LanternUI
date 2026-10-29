using Lantern.UI;

namespace LanternUI.Demo.Pages;

/// <summary>A settings page used as one section of a container page.</summary>
public sealed class SectionPage : PageBase
{
    public SectionPage(IAppState app, ILocalizer loc, IShell shell, string title, string subtitle,
        (string Title, string[] Options)[] rows)
        : base(app, loc, title, subtitle, shell.Layout)
    {
        Group(loc.T("Sections"));
        foreach (var row in rows)
            ComboRow(row.Title, null, row.Options.Select(option => new Choice(option, option)).ToArray(),
                () => row.Options[0], _ => { }, glyph: Glyphs.Lightbulb);
    }
}
