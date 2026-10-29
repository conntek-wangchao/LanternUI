using System.IO;
using System.Windows;
using Lantern.UI;
using Xunit;

namespace LanternUI.Tests;

/// <summary>The structural pieces: a container page's sections, the virtualized list, the layout switch.</summary>
public class StructuralTests
{
    private static (Demo.DemoState State, JsonCatalog Catalog, Localizer Loc) Context()
    {
        var state = Sta.NewState();
        var catalog = JsonCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Localization"));
        return (state, catalog, new Localizer("en", null));
    }

    [Fact]
    public void A_container_page_exposes_each_section_as_its_own_place()
    {
        Sta.Run(() =>
        {
            var (state, catalog, loc) = Context();
            var shell = new ShellWindow { App = state, Localizer = loc };
            var more = new Demo.Pages.MorePage(state, shell, loc);

            // The switch and the section on screen must agree — the switch is rebuilt per section,
            // and it used to come back with nothing selected while the page already showed Mods.
            Assert.Equal("mods", more.CurrentSection);
            Assert.Equal(0, more.SelectedSectionIndex);

            more.ShowSection("cheats");
            Assert.Equal("cheats", more.CurrentSection);
            Assert.Equal(1, more.SelectedSectionIndex);

            var places = more.Places().ToList();
            Assert.Equal(new[] { "Mods", "Cheats" }, places.Select(place => place.Name));
            Assert.All(places, place => Assert.NotNull(place.Show));

            var hits = SearchScorer.Find(places, "install", loc);
            Assert.Contains(hits, hit => hit.Place.Name == "Mods" && hit.Row.Title == "Install order");
        });
    }

    [Fact]
    public void A_long_list_builds_only_the_rows_on_screen()
    {
        Sta.Run(() =>
        {
            var (state, _, loc) = Context();
            var shell = new ShellWindow { App = state, Localizer = loc };
            var page = new Demo.Pages.ListDemoPage(state, shell, loc);

            page.Measure(new Size(900, 700));
            page.Arrange(new Rect(0, 0, 900, 700));
            page.UpdateLayout();

            Assert.Equal(Demo.Pages.ListDemoPage.Rows, page.ItemCount);
            Assert.True(page.RealizedContainers < 500,
                "realized " + page.RealizedContainers + " of " + page.ItemCount + " rows");
        });
    }

    [Fact]
    public void Switching_the_layout_rebuilds_the_page_without_a_restart()
    {
        Sta.Run(() =>
        {
            var (state, catalog, loc) = Context();
            var shell = new ShellWindow { App = state, Localizer = loc };
            shell.SetPages(new[]
            {
                new PageEntry("appearance", "Appearance",
                    () => new Demo.Pages.AppearancePage(state, shell, loc, catalog.Languages)),
            });
            shell.ShowPage("appearance");

            var modern = shell.CurrentPage;
            Assert.NotNull(modern);
            Assert.Equal(UiLayout.Modern, shell.Layout);

            shell.ApplyLayout(UiLayout.Classic);

            Assert.Equal(UiLayout.Classic, shell.Layout);
            Assert.NotSame(modern, shell.CurrentPage);      // rebuilt with the new layout
            Assert.NotNull(shell.CurrentPage);
        });
    }
}
