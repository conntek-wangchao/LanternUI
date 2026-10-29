using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Lantern.UI;

/// <summary>The rows of an <see cref="AdvancedSection"/>: collapsed until the section opens.</summary>
public sealed class AdvancedRows : StackPanel
{
}

/// <summary>A folded group of rows: closed by default, opened by the player or by a search hit.</summary>
public class AdvancedSection : StackPanel
{
    private readonly TextBlock _label;
    private readonly Path _chevron;
    private readonly string _title;

    /// <summary>Its own type, so the settings search knows a folded section still counts as visible.</summary>
    public AdvancedRows Rows { get; } = new() { Visibility = Visibility.Collapsed };

    public AdvancedSection(string title)
    {
        _title = title;
        Margin = new Thickness(0, 2, 0, 0);

        _chevron = new Path
        {
            StrokeThickness = 1.4,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 1, 8, 0),
        };
        _chevron.SetResourceReference(Shape.StrokeProperty, "TextTertiaryBrush");

        _label = new TextBlock { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
        _label.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");

        var header = new Border
        {
            Padding = new Thickness(14, 9, 14, 9),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
        };
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(_chevron);
        line.Children.Add(_label);
        header.Child = line;
        header.MouseLeftButtonUp += (_, _) => IsOpen = !IsOpen;

        Children.Add(header);
        Children.Add(Rows);

        UpdateChevron();
        ShowCount();
    }

    public bool IsOpen
    {
        get => Rows.Visibility == Visibility.Visible;
        set
        {
            Rows.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            UpdateChevron();
            ShowCount();
        }
    }

    private void UpdateChevron()
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            if (IsOpen)
            {
                context.BeginFigure(new Point(1, 2), false, false);
                context.LineTo(new Point(4.5, 5.5), true, false);
                context.LineTo(new Point(8, 2), true, false);
            }
            else
            {
                context.BeginFigure(new Point(2, 1), false, false);
                context.LineTo(new Point(5.5, 4.5), true, false);
                context.LineTo(new Point(2, 8), true, false);
            }
        }
        geometry.Freeze();
        _chevron.Data = geometry;
    }

    private void ShowCount()
    {
        var count = Rows.Children.OfType<SettingRow>().Count(row => row.Visibility == Visibility.Visible);
        _label.Text = count > 0 ? _title + " (" + count + ")" : _title;
    }
}
