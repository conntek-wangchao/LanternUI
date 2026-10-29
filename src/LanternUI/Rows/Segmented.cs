using System.Windows;
using System.Windows.Controls;

namespace Lantern.UI;

/// <summary>
/// Two to six short options as one pill row. It only reports the picked index; the page
/// keeps writing the value, so persistence stays in one place.
/// </summary>
public class Segmented : Border
{
    private readonly List<RadioButton> _items = new();
    private readonly string _group = "Segment_" + Guid.NewGuid().ToString("N");
    private bool _silent;

    public event Action<int>? Changed;

    public int SelectedIndex { get; private set; } = -1;

    public Segmented(IReadOnlyList<Choice> choices)
    {
        CornerRadius = new CornerRadius(15);
        Padding = new Thickness(3);
        BorderThickness = new Thickness(1);
        HorizontalAlignment = HorizontalAlignment.Left;
        SetResourceReference(BackgroundProperty, "PillTrackBrush");
        SetResourceReference(BorderBrushProperty, "DividerBrush");

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        for (var i = 0; i < choices.Count; i++)
        {
            var index = i;
            var button = new RadioButton { Content = choices[i].Label, GroupName = _group };
            button.SetResourceReference(StyleProperty, "Segment");
            button.Checked += (_, _) =>
            {
                if (_silent) return;
                SelectedIndex = index;
                Changed?.Invoke(index);
            };
            _items.Add(button);
            row.Children.Add(button);
        }

        Child = row;
    }

    /// <summary>Picks an index without raising Changed (notify: false) — use it to show the stored value.</summary>
    public void Select(int index, bool notify = false)
    {
        if (index < 0 || index >= _items.Count)
        {
            _silent = true;
            foreach (var item in _items) item.IsChecked = false;
            SelectedIndex = -1;
            _silent = false;
            return;
        }

        _silent = !notify;
        _items[index].IsChecked = true;
        _silent = false;
        SelectedIndex = index;
    }
}
