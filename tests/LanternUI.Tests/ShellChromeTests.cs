using Lantern.UI;
using Xunit;

namespace LanternUI.Tests;

/// <summary>
/// The title bar is placed by the side you can see: a mirrored window must not send the caption
/// buttons and the search box to the same edge, where the search box would sit on top of them.
/// </summary>
public class ShellChromeTests
{
    [Theory]
    [InlineData("windows", "right", false, true)]
    [InlineData("windows", "right", true, true)]     // the reported case: RTL + right
    [InlineData("windows", "left", true, false)]
    [InlineData("mac", "usual", true, false)]
    [InlineData("mac", "usual", false, false)]
    [InlineData("mac", "right", true, true)]
    public void Caption_buttons_stay_on_the_side_they_are_put_on(string family, string side, bool rightToLeft, bool expectRight)
    {
        Sta.Run(() =>
        {
            var state = Sta.NewState();
            state.Settings.Set("ui_window_buttons", family);
            state.Settings.Set("ui_window_buttons_side", side);

            var shell = new ShellWindow { App = state, Localizer = new Localizer("en", null) };
            shell.ApplyFlowDirection(rightToLeft);

            Assert.Equal(expectRight, shell.ButtonsOnTheRight);
            // The search box hugs the reading start: the left edge, or the right one in a mirrored window.
            Assert.Equal(rightToLeft, shell.SearchOnTheRight);
        });
    }

    [Fact]
    public void A_mirrored_window_places_them_the_same_way_round()
    {
        Sta.Run(() =>
        {
            var state = Sta.NewState();
            state.Settings.Set("ui_window_buttons", "windows");
            state.Settings.Set("ui_window_buttons_side", "right");

            var shell = new ShellWindow { App = state, Localizer = new Localizer("en", null) };

            shell.ApplyFlowDirection(false);
            Assert.True(shell.ButtonsOnTheRight);

            shell.ApplyFlowDirection(true);
            Assert.True(shell.ButtonsOnTheRight);
            Assert.True(shell.SearchOnTheRight);
            Assert.Equal(shell.ButtonsOnTheRight, shell.SearchOnTheRight);   // shared edge, so the box steps aside
        });
    }
}