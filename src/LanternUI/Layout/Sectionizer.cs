using System.Windows;
using System.Windows.Controls;

namespace Lantern.UI;

/// <summary>
/// Turns declarations into looks: every group header the page wrote starts a card, and the rows
/// that follow it are moved into that card. The page keeps writing one flat list of rows, and
/// "anything else" (a paragraph, a table, an info bar) ends the card on its own.
/// </summary>
public static class Sectionizer
{
    public static void Apply(Panel body)
    {
        var items = body.Children.Cast<UIElement>().ToList();
        body.Children.Clear();

        StackPanel? rows = null;
        foreach (var item in items)
        {
            if (item is FrameworkElement { Tag: "GroupHeader" })
            {
                body.Children.Add(item);

                var card = new Border
                {
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Margin = new Thickness(0, 2, 0, 12),
                };
                card.SetResourceReference(Border.BackgroundProperty, "CardBackgroundBrush");
                card.SetResourceReference(Border.BorderBrushProperty, "CardStrokeBrush");

                rows = new StackPanel();
                card.Child = rows;
                body.Children.Add(card);
                continue;
            }

            if (rows != null && item is SettingRow or AdvancedSection)
            {
                rows.Children.Add(item);
                continue;
            }

            body.Children.Add(item);
            rows = null;
        }
    }
}
