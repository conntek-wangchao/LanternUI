using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

namespace Lantern.UI;

/// <summary>One row of a list page; the template reads these three properties.</summary>
public sealed record ListItem(string Title, string? Detail = null, string? Badge = null)
{
    /// <summary>What a screen reader reads for the row: the title, not the record's own dump.</summary>
    public override string ToString() => Title;
}

/// <summary>
/// A long list with virtualized rows: thousands of items, and only the visible ones are built.
/// The template is parsed from XAML once per process and reads the palette by DynamicResource, so
/// the rows follow the accent like every other control.
/// </summary>
public class ListPage : PageBase
{
    private readonly ListBox _list;
    private static DataTemplate? _template;

    protected ListPage(IAppState app, ILocalizer? localizer, string? title, string? subtitle,
        UiLayout layout = UiLayout.Modern, double height = 420)
        : base(app, localizer, title, subtitle, layout)
    {
        _list = new ListBox
        {
            Height = height,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            ItemTemplate = RowTemplate(),
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        _list.Loaded += (_, _) =>
        {
            ResetScroll();
            _list.SelectedIndex = -1;   // nothing is chosen just because the page opened
        };
        _list.SetResourceReference(ForegroundProperty, "TextPrimaryBrush");
        VirtualizingPanel.SetIsVirtualizing(_list, true);
        VirtualizingPanel.SetVirtualizationMode(_list, VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(_list, true);
        Body.Children.Add(_list);
    }

    /// <summary>Fills the list; an empty collection shows the placeholder instead.</summary>
    public void SetItems(IReadOnlyList<ListItem> items, string? emptyText = null)
    {
        _list.ItemsSource = items;
        if (items.Count > 0 || string.IsNullOrEmpty(emptyText)) return;

        var empty = new TextBlock { Text = emptyText, FontSize = 12.5, Margin = new Thickness(4, 10, 4, 0) };
        empty.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");
        Body.Children.Add(empty);
    }

    /// <summary>Puts the list back at its first row (re-parenting can leave an offset behind).</summary>
    public void ResetScroll()
    {
        if (_list.Items.Count > 0) _list.ScrollIntoView(_list.Items[0]);
        if (Find(_list) is { } scroller) scroller.ScrollToTop();
    }

    private static ScrollViewer? Find(DependencyObject root)
    {
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (node is ScrollViewer scroller) return scroller;
            var children = VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < children; i++) queue.Enqueue(VisualTreeHelper.GetChild(node, i));
        }
        return null;
    }

    /// <summary>Items handed to the panel (all of them; they are not built).</summary>
    public int ItemCount => _list.Items.Count;

    /// <summary>Row containers realized right now — the proof that the list does not build them all.</summary>
    public int RealizedContainers => Count(_list);

    private static int Count(DependencyObject root)
    {
        var total = 0;
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (node is ListBoxItem) total++;
            var children = VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < children; i++) queue.Enqueue(VisualTreeHelper.GetChild(node, i));
        }
        return total;
    }

    private static DataTemplate RowTemplate()
    {
        if (_template != null) return _template;

        const string xaml = """
<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
            xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Border Padding='12,7' Background='Transparent'>
    <Grid>
      <Grid.ColumnDefinitions>
        <ColumnDefinition />
        <ColumnDefinition Width='Auto' />
      </Grid.ColumnDefinitions>
      <StackPanel>
        <TextBlock Text='{Binding Title}' FontSize='13' TextTrimming='CharacterEllipsis'
                   Foreground='{DynamicResource TextPrimaryBrush}' />
        <TextBlock Text='{Binding Detail}' FontSize='11.5' Margin='0,2,0,0' TextTrimming='CharacterEllipsis'
                   Foreground='{DynamicResource TextTertiaryBrush}' />
      </StackPanel>
      <Border Grid.Column='1' CornerRadius='4' Padding='6,1' VerticalAlignment='Center'
              Background='{DynamicResource PillTrackBrush}'>
        <Border.Style>
          <Style TargetType='Border'>
            <Style.Triggers>
              <DataTrigger Binding='{Binding Badge}' Value=''>
                <Setter Property='Visibility' Value='Collapsed' />
              </DataTrigger>
              <DataTrigger Binding='{Binding Badge}' Value='{x:Null}'>
                <Setter Property='Visibility' Value='Collapsed' />
              </DataTrigger>
            </Style.Triggers>
          </Style>
        </Border.Style>
        <TextBlock Text='{Binding Badge}' FontSize='10.5'
                   Foreground='{DynamicResource TextSecondaryBrush}' />
      </Border>
    </Grid>
  </Border>
</DataTemplate>
""";
        _template = (DataTemplate)XamlReader.Parse(xaml);
        return _template;
    }
}
