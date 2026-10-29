using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Lantern.UI;

/// <summary>
/// A notification card: title, body and an accent bar, drawn inside the shell (the reference puts a
/// trophy toast in a separate click-through window over the game; here it belongs to the window).
/// </summary>
public class Toast : Border
{
    public Toast(string title, string body)
    {
        CornerRadius = new CornerRadius(8);
        BorderThickness = new Thickness(1);
        Padding = new Thickness(0);
        Width = 320;
        Margin = new Thickness(0, 6, 0, 0);
        HorizontalAlignment = HorizontalAlignment.Right;
        SetResourceReference(BackgroundProperty, "CardBackgroundBrush");
        SetResourceReference(BorderBrushProperty, "CardStrokeBrush");

        var bar = new Border { Width = 4, CornerRadius = new CornerRadius(8, 0, 0, 8) };
        bar.SetResourceReference(BackgroundProperty, "AccentBrush");

        var heading = new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");

        var text = new TextBlock { Text = body, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");

        var stack = new StackPanel { Margin = new Thickness(12, 10, 12, 10) };
        stack.Children.Add(heading);
        stack.Children.Add(text);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(bar);
        Grid.SetColumn(stack, 1);
        grid.Children.Add(stack);

        Child = grid;
    }
}
