using Lantern.UI;

namespace LanternUI.Demo.Pages;

public sealed class ShortcutsPage : PageBase
{
    public ShortcutsPage(IAppState app, IShell shell, ILocalizer loc)
        : base(app, loc, loc.T("Shortcuts"),
            loc.T("A third page, so the sidebar and the search have more to look through."), shell.Layout)
    {
        Group(loc.T("Input"));
        ComboRow(loc.T("Pointer speed"), loc.T("How fast the camera turns with the mouse."), new[]
        {
            new Choice(loc.T("Slow"), "slow"),
            new Choice(loc.T("Normal"), "normal"),
            new Choice(loc.T("Fast"), "fast"),
        }, () => S.Str("pointer_speed"), value => S.Set("pointer_speed", value));

        ToggleRow(loc.T("Invert the vertical axis"), loc.T("Mouse look, flipped the way a pad's right stick is."),
            () => S.Bool("invert_y"), value => S.SetBool("invert_y", value));

        Group(loc.T("Overlay"));
        ToggleRow(loc.T("Show the frame counter"), loc.T("The engine's own statistics, in the corner."),
            () => S.Bool("show_fps"), value => S.SetBool("show_fps", value));
        TextRow(loc.T("Screenshot folder"), loc.T("Where F12 writes its pictures."), "D:\\Shots",
            () => S.Str("shot_dir"), value => S.Set("shot_dir", value));
    }
}
