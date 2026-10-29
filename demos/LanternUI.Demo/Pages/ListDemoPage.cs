using System.Windows;
using System.Windows.Controls;
using Lantern.UI;

namespace LanternUI.Demo.Pages;

/// <summary>Five thousand rows behind a virtualizing panel: only the visible ones are built.</summary>
public sealed class ListDemoPage : ListPage
{
    public const int Rows = 5000;

    public ListDemoPage(IAppState app, IShell shell, ILocalizer loc)
        : base(app, loc, loc.T("List"), loc.T("A long list: only the rows on screen are built."), shell.Layout)
    {
        var note = new TextBlock { Text = loc.T("Five thousand rows — scrolling stays smooth."), FontSize = 12, Margin = new Thickness(4, 0, 4, 10) };
        note.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");
        Body.Children.Insert(0, note);

        SetItems(Enumerable.Range(0, Rows)
            .Select(index => new ListItem("Row " + index, "Detail line for row " + index, index % 7 == 0 ? "new" : ""))
            .ToArray(), loc.T("Nothing here yet."));
    }
}
