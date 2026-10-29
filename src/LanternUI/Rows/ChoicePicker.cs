using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Lantern.UI;

/// <summary>One entry as the list box sees it: the label, and where it came from.</summary>
internal readonly record struct ChoiceRow(int Index, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// A drop-down for long lists: the closed control is a bordered button, the open one a real list box
/// (plus a filter from FilterFrom entries on). Typing while it is closed opens it and starts the
/// filter, Enter takes the selected entry, Escape closes. It only reports the picked index.
/// The list is a ListBox on purpose: entries should be items a screen reader can read, a client can
/// select and a finger can reach — a drawn list is none of those.
/// </summary>
public class ChoicePicker : Border
{
    public const int FilterFrom = 8;

    private static DataTemplate? _rowTemplate;

    private readonly IReadOnlyList<Choice> _choices;
    private readonly TextBlock _label;
    private readonly Path _chevron;
    private readonly Popup _popup;
    private readonly TextBox _filter;
    private readonly ListBox _list;
    private readonly TextBlock _empty;
    private readonly Border _panel;

    private bool _silent;
    private bool _rebuilding;   // a filter rebuild must not look like the user picking something
    private string _typed = "";

    public event Action<int>? Changed;

    public int SelectedIndex { get; private set; } = -1;

    /// <summary>Entries the filter currently leaves in the list (the self-check uses it).</summary>
    public int VisibleCount => _list.Items.Count;

    /// <summary>The label of the picked entry, for a screen reader (empty when nothing is picked).</summary>
    public string SelectedLabel => SelectedIndex >= 0 && SelectedIndex < _choices.Count
        ? _choices[SelectedIndex].Label
        : "";

    protected override AutomationPeer OnCreateAutomationPeer() => new ChoicePickerAutomationPeer(this);

    public ChoicePicker(IReadOnlyList<Choice> choices)
    {
        _choices = choices;
        Width = 220;
        Height = 30;
        CornerRadius = new CornerRadius(6);
        BorderThickness = new Thickness(1);
        Background = Brushes.Transparent;
        SetResourceReference(BorderBrushProperty, "ControlStrokeBrush");
        Cursor = Cursors.Hand;
        Focusable = true;

        _label = new TextBlock { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 26, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        _label.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");

        _chevron = new Path { Data = Geometry.Parse("M 0,0 L 4,4 L 8,0"), StrokeThickness = 1.3, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        _chevron.SetResourceReference(Shape.StrokeProperty, "TextSecondaryBrush");

        var face = new Grid();
        face.Children.Add(new Border { Background = Brushes.Transparent });   // whole face is hit-testable
        face.Children.Add(_label);
        face.Children.Add(_chevron);
        Child = face;

        _filter = new TextBox
        {
            Margin = new Thickness(6, 6, 6, 4),
            Padding = new Thickness(6, 4, 6, 4),
            FontSize = 12,
            Visibility = choices.Count >= FilterFrom ? Visibility.Visible : Visibility.Collapsed,
        };
        _filter.SetResourceReference(StyleProperty, "Field");
        _filter.TextChanged += (_, _) => Rebuild();
        _filter.PreviewKeyDown += FilterKeyDown;

        _list = new ListBox
        {
            MaxHeight = 260,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            ItemTemplate = RowTemplate(),
            Margin = new Thickness(3),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        _list.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
        ScrollViewer.SetVerticalScrollBarVisibility(_list, ScrollBarVisibility.Auto);
        _list.SelectionChanged += (_, _) => LiveSelection();
        _list.PreviewKeyDown += ListKeyDown;
        _list.MouseLeftButtonUp += (_, _) => Commit();

        _empty = new TextBlock { Text = "\u2014", FontSize = 12, Margin = new Thickness(9, 6, 9, 6), Visibility = Visibility.Collapsed };
        _empty.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");

        var stack = new StackPanel();
        stack.Children.Add(_filter);
        stack.Children.Add(_list);
        stack.Children.Add(_empty);

        _panel = new Border { Child = stack, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), MinWidth = 220, Margin = new Thickness(0, 4, 0, 0) };
        _panel.SetResourceReference(BackgroundProperty, "ContentAreaBrush");
        _panel.SetResourceReference(BorderBrushProperty, "CardStrokeBrush");

        _popup = new Popup
        {
            PlacementTarget = this,
            Placement = PlacementMode.Bottom,
            AllowsTransparency = true,
            StaysOpen = false,
            PopupAnimation = PopupAnimation.Fade,
            Child = _panel,
        };
        _popup.Closed += (_, _) =>
        {
            _typed = "";
            NotifyExpanded(false);
        };
        _popup.Opened += (_, _) =>
        {
            // The list is a window of its own; giving it the picker's name makes it findable.
            AutomationProperties.SetName(_list, AutomationProperties.GetName(this));
            Rebuild();
            if (_filter.Visibility == Visibility.Visible) _filter.Focus();
            else _list.Focus();
            NotifyExpanded(true);
        };

        PreviewMouseLeftButtonUp += (_, _) => Toggle();
        PreviewKeyDown += ClosedKeyDown;
        Rebuild();
    }

    public void Toggle() => _popup.IsOpen = !_popup.IsOpen;

    public void Open()
    {
        if (!_popup.IsOpen) _popup.IsOpen = true;
    }

    public void Close() => _popup.IsOpen = false;

    /// <summary>Whether the list is showing (the automation peer reports it as expand/collapse).</summary>
    public bool IsOpen => _popup.IsOpen;

    /// <summary>Shows a stored value without reporting it as a change.</summary>
    public void Select(int index, bool notify = false)
    {
        _silent = !notify;
        Apply(index);
        _silent = false;
    }

    private void Apply(int index)
    {
        if (index < 0 || index >= _choices.Count)
        {
            SelectedIndex = -1;
            _label.Text = "";
            return;
        }

        SelectedIndex = index;
        _label.Text = _choices[index].Label;
        if (_silent) return;
        Close();
        Changed?.Invoke(index);
        NotifyValueChanged();
    }

    /// <summary>
    /// Picking in the list takes effect at once — that is what a combo box does, and it is also what
    /// makes SelectionItemPattern.Select() from a screen reader or a script mean something.
    /// </summary>
    private void LiveSelection()
    {
        if (_rebuilding || _silent) return;
        if (_list.SelectedItem is not ChoiceRow row) return;

        SelectedIndex = row.Index;
        _label.Text = row.Label;
        Changed?.Invoke(row.Index);
        NotifyValueChanged();
    }

    private void Rebuild()
    {
        _rebuilding = true;
        var query = _filter.Text.Trim();
        var rows = new List<ChoiceRow>(_choices.Count);

        for (var index = 0; index < _choices.Count; index++)
            if (query.Length == 0 || _choices[index].Label.Contains(query, StringComparison.OrdinalIgnoreCase))
                rows.Add(new ChoiceRow(index, _choices[index].Label));

        _list.ItemsSource = rows;
        _list.SelectedIndex = rows.Count == 0 ? -1 : Math.Max(0, rows.FindIndex(row => row.Index == SelectedIndex));
        _empty.Visibility = rows.Count == 0 && query.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_list.SelectedItem != null) _list.ScrollIntoView(_list.SelectedItem);
        _rebuilding = false;
    }

    private void Commit()
    {
        if (_list.SelectedItem is ChoiceRow row) Apply(row.Index);
    }

    private void ListKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter: Commit(); e.Handled = true; break;
            case Key.Escape: Close(); Focus(); e.Handled = true; break;
        }
    }

    private void FilterKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down: MoveSelection(1); e.Handled = true; break;
            case Key.Up: MoveSelection(-1); e.Handled = true; break;
            case Key.Enter: Commit(); e.Handled = true; break;
            case Key.Escape: Close(); Focus(); e.Handled = true; break;
        }
    }

    private void MoveSelection(int delta)
    {
        if (_list.Items.Count == 0) return;
        var at = _list.SelectedIndex < 0 ? 0 : _list.SelectedIndex + delta;
        _list.SelectedIndex = Math.Clamp(at, 0, _list.Items.Count - 1);
        if (_list.SelectedItem != null) _list.ScrollIntoView(_list.SelectedItem);
    }

    /// <summary>Typing while closed opens the list and feeds the filter (the reference's jump).</summary>
    private void ClosedKeyDown(object sender, KeyEventArgs e)
    {
        if (_popup.IsOpen) return;
        if (e.Key is Key.Enter or Key.Space or Key.Down) { Open(); e.Handled = true; return; }
        if (e.Key is >= Key.A and <= Key.Z)
        {
            _typed += (char)('a' + (e.Key - Key.A));
            _filter.Text = _typed;
            Open();
            e.Handled = true;
        }
    }

    private void NotifyExpanded(bool expanded)
    {
        if (UIElementAutomationPeer.FromElement(this) is ChoicePickerAutomationPeer peer) peer.NotifyExpanded(expanded);
        else if (UIElementAutomationPeer.CreatePeerForElement(this) is ChoicePickerAutomationPeer created) created.NotifyExpanded(expanded);
    }

    private void NotifyValueChanged()
    {
        if (UIElementAutomationPeer.FromElement(this) is ChoicePickerAutomationPeer peer) peer.NotifyValueChanged();
    }

    /// <summary>For the screenshot path: the popup's panel, so it can be rendered on its own.</summary>
    public FrameworkElement Panel => _panel;

    public void SetFilter(string text) => _filter.Text = text;

    private static DataTemplate RowTemplate()
    {
        if (_rowTemplate != null) return _rowTemplate;

        const string xaml = """
<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
  <TextBlock Text='{Binding Label}' FontSize='12.5' Margin='8,5,8,5' TextTrimming='CharacterEllipsis'
             Foreground='{DynamicResource TextPrimaryBrush}' />
</DataTemplate>
""";
        _rowTemplate = (DataTemplate)XamlReader.Parse(xaml);
        return _rowTemplate;
    }
}

/// <summary>
/// The picker as a UIA combo box: a name, a value, expand/collapse, and — because the entries are a
/// real ListBox — a list of items a screen reader can read and a client can select.
/// </summary>
public sealed class ChoicePickerAutomationPeer : FrameworkElementAutomationPeer, IExpandCollapseProvider, IValueProvider
{
    public ChoicePickerAutomationPeer(ChoicePicker owner) : base(owner) { }

    private ChoicePicker Picker => (ChoicePicker)Owner;

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ComboBox;

    protected override string GetClassNameCore() => nameof(ChoicePicker);

    protected override string GetNameCore()
    {
        var name = base.GetNameCore();
        var picked = Picker.SelectedLabel;
        return picked.Length == 0 ? name : name + ": " + picked;
    }

    protected override bool IsControlElementCore() => true;

    public ExpandCollapseState ExpandCollapseState =>
        Picker.IsOpen ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

    public void Expand() => Picker.Open();

    public void Collapse() => Picker.Close();

    public bool IsReadOnly => true;

    public string Value => Picker.SelectedLabel;

    public void SetValue(string value) => throw new InvalidOperationException("the picker is read-only for automation");

    /// <summary>Says out loud that the list opened or closed.</summary>
    internal void NotifyExpanded(bool expanded)
    {
        RaisePropertyChangedEvent(ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty,
            expanded ? ExpandCollapseState.Collapsed : ExpandCollapseState.Expanded,
            expanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed);
    }

    /// <summary>Says out loud that the picked value changed.</summary>
    internal void NotifyValueChanged() =>
        RaisePropertyChangedEvent(ValuePatternIdentifiers.ValueProperty, "", Picker.SelectedLabel);
}
