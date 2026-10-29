using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Lantern.UI;

/// <summary>
/// One setting: its title (and description) on the left, its control on the right.
/// The row is also the settings search's index entry (Title/Info/Hint/Choices), so a new
/// setting becomes searchable the moment a page adds it — no second registration anywhere.
/// </summary>
public class SettingRow : Border
{
    private readonly Border _flash;

    public string Title { get; }
    public string? Info { get; }
    public TextBlock Hint { get; }

    /// <summary>Set for rows a page adds through PageBase.Add (not for list entries).</summary>
    public bool Searchable { get; set; }
    public string? Section { get; set; }
    /// <summary>A choice row's options ("Crimson · Moonlight"), so searching an option finds the row.</summary>
    public string Choices { get; set; } = "";

    public SettingRow(string title, string? info, object? control, UiLayout layout = UiLayout.Modern,
        bool wide = false, string? glyph = null)
    {
        Title = title;
        Info = info;
        var classic = layout == UiLayout.Classic;

        Padding = classic ? new Thickness(14, 9, 14, 9) : new Thickness(14, 7, 14, 7);
        MinHeight = classic ? 52 : 38;
        BorderThickness = classic ? new Thickness(1) : new Thickness(0, 1, 0, 0);
        CornerRadius = classic ? new CornerRadius(6) : new CornerRadius(0);
        Background = Brushes.Transparent;
        SetResourceReference(BorderBrushProperty, classic ? "CardStrokeBrush" : "DividerBrush");

        var titleBlock = new TextBlock
        {
            Text = title,
            FontSize = 13.5,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(titleBlock);

        if (!string.IsNullOrWhiteSpace(info))
        {
            var infoBlock = new TextBlock
            {
                Text = info,
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0),
            };
            infoBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");
            text.Children.Add(infoBlock);
        }

        Hint = new TextBlock
        {
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0),
            Visibility = Visibility.Collapsed,
        };
        Hint.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextBrush");
        text.Children.Add(Hint);

        FrameworkElement content = text;
        if (!string.IsNullOrEmpty(glyph))
        {
            var icon = Glyphs.Icon(glyph, classic ? 18 : 16);
            icon.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            icon.VerticalAlignment = VerticalAlignment.Center;

            // A grid, not a margin: the columns mirror with the reading direction, a physical margin
            // does not. This is what lets the icon move to the other side in a right-to-left window.
            var line = new Grid { VerticalAlignment = VerticalAlignment.Center };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(classic ? 14 : 10) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(icon, 0);
            Grid.SetColumn(text, 2);
            line.Children.Add(icon);
            line.Children.Add(text);
            content = line;
        }

        FrameworkElement body;
        if (wide && control is FrameworkElement wideControl)
        {
            var stack = new StackPanel();
            stack.Children.Add(content);
            // The control keeps the alignment it chose: a button row stays as wide as its buttons,
            // a chart asks for the row's width and stretches into it.
            wideControl.Margin = new Thickness(0, 8, 0, 0);
            stack.Children.Add(wideControl);
            body = stack;
        }
        else
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(content);
            if (control is UIElement element)
            {
                if (element is FrameworkElement field)
                {
                    field.VerticalAlignment = VerticalAlignment.Center;
                    field.Margin = new Thickness(12, 0, 0, 0);
                }
                Grid.SetColumn(element, 1);
                grid.Children.Add(element);
            }
            body = grid;
        }

        _flash = new Border
        {
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(6),
            IsHitTestVisible = false,
            Opacity = 0,
        };

        var layer = new Grid();
        layer.Children.Add(body);
        layer.Children.Add(_flash);
        Child = layer;
    }

    /// <summary>A short accent wash: the settings search uses it to show where a hit landed.</summary>
    public void Flash()
    {
        var accent = TryFindResource("AccentBrush") as SolidColorBrush;
        var colour = accent?.Color ?? AccentTheme.Default;
        _flash.Background = new SolidColorBrush(Color.FromArgb(0x3A, colour.R, colour.G, colour.B));
        _flash.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(900))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        });
    }

    /// <summary>A row is one unit for a screen reader: its title, its description, and its control inside.</summary>
    protected override AutomationPeer OnCreateAutomationPeer() => new SettingRowAutomationPeer(this);
}

/// <summary>
/// Exposes a row as a UIA group named after its setting, so "Show the log at start" is read once
/// and its check box belongs to it, instead of a loose title and a nameless toggle.
/// </summary>
public sealed class SettingRowAutomationPeer : FrameworkElementAutomationPeer
{
    public SettingRowAutomationPeer(SettingRow owner) : base(owner) { }

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

    protected override string GetClassNameCore() => nameof(SettingRow);

    protected override string GetNameCore() => ((SettingRow)Owner).Title;

    protected override string GetHelpTextCore() => ((SettingRow)Owner).Info ?? "";

    protected override bool IsControlElementCore() => true;

    protected override bool IsContentElementCore() => true;
}
