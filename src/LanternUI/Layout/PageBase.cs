using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Lantern.UI;

/// <summary>
/// A settings page: groups of compact rows, each group one rounded card (Sectionizer builds the
/// cards once the page is ready). The builders bind a control straight to a settings key and save
/// on change, so a page is a flat list of declarations.
/// </summary>
public abstract class PageBase : UserControl
{
    private readonly List<SettingRow> _searchRows = new();
    private Panel _target;
    private string? _section;
    private bool _sectioned;

    protected IAppState App { get; }
    protected ILocalizer L { get; }
    protected UiLayout Layout { get; }
    protected ISettingsStore S => App.Settings;

    protected StackPanel Body { get; }
    protected ScrollViewer Scroller { get; }

    protected PageBase(IAppState app, ILocalizer? localizer, string? title, string? subtitle,
        UiLayout layout = UiLayout.Modern)
    {
        App = app;
        L = localizer ?? NullLocalizer.Instance;
        Layout = layout;

        Background = Brushes.Transparent;
        // A Frame (and a ContentControl template) does not pass inherited text options on to its
        // page: without these the page draws WPF's default "Ideal" text and looks soft at 11-13 px.
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
        TextOptions.SetTextHintingMode(this, TextHintingMode.Fixed);
        RenderOptions.SetClearTypeHint(this, ClearTypeHint.Enabled);
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        SetResourceReference(FontFamilyProperty, "UiFont");
        SetResourceReference(ForegroundProperty, "TextPrimaryBrush");

        Body = new StackPanel { Margin = new Thickness(20, 6, 20, 24), MaxWidth = 880 };
        Scroller = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = Body,
        };
        Content = Scroller;
        PageScroll.Attach(Scroller);
        _target = Body;

        if (layout == UiLayout.Classic) Body.MaxWidth = double.PositiveInfinity;

        if (!string.IsNullOrEmpty(title))
        {
            var head = new TextBlock { Text = title };
            head.SetResourceReference(StyleProperty, "PageTitle");
            Body.Children.Add(head);
        }
        if (!string.IsNullOrEmpty(subtitle))
        {
            var sub = new TextBlock { Text = subtitle };
            sub.SetResourceReference(StyleProperty, "PageSubtitle");
            Body.Children.Add(sub);
        }

        Loaded += (_, _) =>
        {
            if (_sectioned || Layout == UiLayout.Classic) return;
            _sectioned = true;
            Sectionizer.Apply(Body);
        };
    }

    /// <summary>A container page swaps the scroll shell for its own layout (the switch and the section).</summary>
    protected void ReplaceBody(UIElement content) => Content = content;

    /// <summary>Called every time the shell shows the page: re-read anything that lives on disk.</summary>
    public virtual void OnShown() { }

    // ---------------------------------------------------------------- building

    protected TextBlock Group(string text, Panel? into = null)
    {
        var header = new TextBlock { Text = text, Tag = "GroupHeader" };
        header.SetResourceReference(StyleProperty, "GroupHeader");
        (into ?? Body).Children.Add(header);
        _section = text;
        _target = Body;
        return header;
    }

    /// <summary>A folded group of rows; rows added afterwards go into it until the next Group.</summary>
    protected AdvancedSection Advanced(string title = "Advanced", Panel? into = null)
    {
        var section = new AdvancedSection(title);
        _target = Body;
        (into ?? Body).Children.Add(section);
        _target = section.Rows;
        return section;
    }

    protected SettingRow Add(SettingRow row, Panel? into = null)
    {
        row.Section = _section;
        row.Searchable = true;
        _searchRows.Add(row);
        (into ?? _target).Children.Add(row);
        return row;
    }

    protected SettingRow Card(string title, string? info, object? control, out TextBlock hint,
        bool wide = false, string? glyph = null)
    {
        // The row is one group named after the setting; the control carries the name too, because a
        // screen reader reaches the control directly and would otherwise announce only "check box".
        if (control is UIElement element)
        {
            System.Windows.Automation.AutomationProperties.SetName(element, title);
            if (!string.IsNullOrEmpty(info)) System.Windows.Automation.AutomationProperties.SetHelpText(element, info);
        }

        var row = new SettingRow(title, info, control, Layout, wide, glyph);
        hint = row.Hint;
        return row;
    }

    protected static void SetHint(TextBlock hint, string? text)
    {
        hint.Text = text ?? "";
        hint.Visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    protected static string? Short(string? text) =>
        text is { Length: > 90 } ? text[..87] + "..." : text;

    // ---------------------------------------------------------------- rows

    /// <summary>A register table row: the table stretches across the card and keeps its own scrolling.</summary>
    protected SettingRow RegisterTableRow(string title, string? info, RegisterTable table, out TextBlock hint,
        string? glyph = null)
    {
        System.Windows.Automation.AutomationProperties.SetName(table, title);
        return Add(Card(title, info, table, out hint, wide: true, glyph: glyph ?? Glyphs.Settings));
    }

    /// <summary>A chart row: the control stretches across the card, and its title is its automation name.</summary>
    protected SettingRow ChartRow(string title, string? info, Chart chart, out TextBlock hint, string? glyph = null)
    {
        if (chart.MinHeight < 110) chart.MinHeight = 110;
        System.Windows.Automation.AutomationProperties.SetName(chart, title);
        return Add(Card(title, info, chart, out hint, wide: true, glyph: glyph ?? Glyphs.Chart));
    }

    protected FrameworkElement SettingCombo(string title, string? info, IReadOnlyList<Choice> choices,
        string key, Panel? into = null, Action? changed = null) =>
        ComboRow(title, info, choices, () => S.Str(key), value => S.Set(key, value), into, changed: changed);

    /// <summary>
    /// One row for a choice. Two to six short options become pills, anything else a drop-down:
    /// the page never decides which control to use.
    /// </summary>
    protected FrameworkElement ComboRow(string title, string? info, IReadOnlyList<Choice> choices,
        Func<string> get, Action<string> set, Panel? into = null, double width = 220, Action? changed = null,
        string? glyph = null)
    {
        var options = string.Join(" · ", choices.Select(choice => choice.Label));
        var index = IndexOf(choices, get());

        if (Layout != UiLayout.Classic && choices.Count is >= 2 and <= 6)
        {
            var segmented = new Segmented(choices);
            var row = Add(Card(title, info, segmented, out var note, wide: true, glyph: glyph), into);
            row.Choices = options;
            row.Tag = note;
            segmented.Select(index, notify: false);
            SetHint(note, Short(index >= 0 ? choices[index].Note : null));
            segmented.Changed += picked =>
            {
                set(choices[picked].Value);
                App.SaveSoon();
                SetHint(note, Short(choices[picked].Note));
                changed?.Invoke();
            };
            return segmented;
        }

        var picker = new ChoicePicker(choices) { Width = width, VerticalAlignment = VerticalAlignment.Center };
        var card = Add(Card(title, info, picker, out var hint, glyph: glyph), into);
        card.Choices = options;
        card.Tag = hint;
        picker.Select(index, notify: false);
        SetHint(hint, Short(index >= 0 ? choices[index].Note : null));
        picker.Changed += picked =>
        {
            set(choices[picked].Value);
            App.SaveSoon();
            SetHint(hint, Short(choices[picked].Note));
            changed?.Invoke();
        };
        return picker;
    }

    protected CheckBox ToggleRow(string title, string? info, Func<bool> get, Action<bool> set,
        Panel? into = null, Action? changed = null, string? hintText = null, string? glyph = null)
    {
        var box = new CheckBox { IsChecked = get(), VerticalAlignment = VerticalAlignment.Center };
        box.SetResourceReference(StyleProperty, "Toggle");
        box.Focusable = true;
        var row = Add(Card(title, info, box, out var hint, glyph: glyph), into);
        SetHint(hint, hintText);
        box.Checked += (_, _) => { set(true); App.SaveSoon(); changed?.Invoke(); };
        box.Unchecked += (_, _) => { set(false); App.SaveSoon(); changed?.Invoke(); };
        return box;
    }

    protected CheckBox SettingToggle(string title, string? info, string key, Panel? into = null,
        Action? changed = null, string? glyph = null) =>
        ToggleRow(title, info, () => S.Bool(key), value => S.SetBool(key, value), into, changed, glyph: glyph);

    protected TextBox TextRow(string title, string? info, string placeholder, Func<string> get, Action<string> set,
        Panel? into = null, Action? changed = null, double width = 220, string? glyph = null)
    {
        var box = new TextBox
        {
            Text = get() ?? "",
            Width = width,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = placeholder,
        };
        box.SetResourceReference(StyleProperty, "Field");
        Add(Card(title, info, box, out _, glyph: glyph), into);

        void Commit()
        {
            set(box.Text.Trim());
            App.SaveSoon();
            changed?.Invoke();
        }

        box.LostKeyboardFocus += (_, _) => Commit();
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            Commit();
            e.Handled = true;
        };
        return box;
    }

    /// <summary>
    /// The line a slider shows when a typed value was outside its range. It goes through the
    /// localizer, so the one message the framework writes itself is translated like the rest.
    /// </summary>
    public static string RangeHint(ILocalizer localizer, string value, string min, string max) =>
        string.Format(System.Globalization.CultureInfo.CurrentCulture,
            localizer.T("Kept at {0} (the range is {1} to {2})"), value, min, max);

    /// <summary>A slider with a small value box: drag it, or type a number and press Enter.</summary>
    protected Slider SliderRow(string title, string? info, double min, double max, double step,
        Func<double> get, Action<double> set, string format = "0", Panel? into = null,
        Action? changed = null, string? glyph = null)
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        var stored = get();
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            SmallChange = step,
            LargeChange = step * 4,
            TickFrequency = step,
            IsSnapToTickEnabled = true,
            Width = 180,
            VerticalAlignment = VerticalAlignment.Center,
            Value = Math.Clamp(stored, min, max),
        };

        var box = new TextBox
        {
            Width = 58,
            TextAlignment = TextAlignment.Right,
            Text = slider.Value.ToString(format, culture),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        box.SetResourceReference(StyleProperty, "Field");

        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(slider);
        line.Children.Add(box);

        Add(Card(title, info, line, out var hint, glyph: glyph), into);

        // A stored value outside the range says so straight away, in the same words a typed one does.
        if (Math.Abs(Math.Clamp(stored, min, max) - stored) > double.Epsilon)
            SetHint(hint, RangeHint(L, Math.Clamp(stored, min, max).ToString(format, culture),
                min.ToString(format, culture), max.ToString(format, culture)));

        void Commit()
        {
            var typed = double.TryParse(box.Text, System.Globalization.NumberStyles.Float, culture, out var parsed)
                ? parsed
                : slider.Value;
            var kept = Math.Clamp(typed, min, max);
            slider.Value = kept;
            box.Text = kept.ToString(format, culture);
            set(kept);
            App.SaveSoon();
            changed?.Invoke();

            // A typed value outside the range is reported instead of changed silently. The note is a
            // note: it is not logged, and the next commit — which is what leaving the box does — takes
            // it away again.
            SetHint(hint, Math.Abs(kept - typed) > double.Epsilon
                ? RangeHint(L, kept.ToString(format, culture), min.ToString(format, culture), max.ToString(format, culture))
                : null);
        }

        slider.ValueChanged += (_, e) =>
        {
            box.Text = e.NewValue.ToString(format, culture);
            SetHint(hint, null);   // dragging it back into range clears the note
            set(e.NewValue);
            App.SaveSoon();
            changed?.Invoke();
        };
        box.LostKeyboardFocus += (_, _) => Commit();
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            Commit();
            e.Handled = true;
        };

        return slider;
    }

    // ---------------------------------------------------------------- search

    /// <summary>This page's index for the settings search: the rows added with Add, as the page is built.</summary>
    public IEnumerable<SettingRow> SearchRows() => _searchRows.Where(row => row.Searchable && Shown(row));

    private bool Shown(DependencyObject? at)
    {
        for (; at != null && !ReferenceEquals(at, this); at = LogicalTreeHelper.GetParent(at))
            if (at is UIElement { Visibility: not Visibility.Visible } && at is not AdvancedRows)
                return false; // a folded Advanced section still counts: the search opens it
        return at != null;
    }

    /// <summary>Opens the page's folds, centres the row, flashes it and puts the keyboard on its control.</summary>
    public bool Reveal(SettingRow row)
    {
        for (DependencyObject? at = row; at != null; at = LogicalTreeHelper.GetParent(at))
            if (at is AdvancedSection section) section.IsOpen = true;

        UpdateLayout();
        if (!row.IsVisible) return false;

        var focused = Descendants(row).OfType<Control>()
            .FirstOrDefault(c => c is { Focusable: true, IsTabStop: true, IsEnabled: true, IsVisible: true })
            ?.Focus() == true;

        try
        {
            var top = row.TransformToAncestor(Scroller).Transform(new Point(0, 0)).Y + Scroller.VerticalOffset;
            Scroller.ScrollToVerticalOffset(Math.Max(0, top - Math.Max(0, Scroller.ViewportHeight - row.ActualHeight) / 2));
        }
        catch (InvalidOperationException)
        {
            row.BringIntoView();
        }

        row.Flash();
        return focused;
    }

    public static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            if (child is SettingRow) continue;
            foreach (var below in Descendants(child)) yield return below;
        }
    }

    private static int IndexOf(IReadOnlyList<Choice> choices, string value)
    {
        for (var i = 0; i < choices.Count; i++)
            if (choices[i].Value == value) return i;
        return choices.Count > 0 ? 0 : -1;
    }
}