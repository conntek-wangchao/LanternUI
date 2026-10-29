using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Lantern.UI;

/// <summary>How values are printed and typed: decimal everywhere, or hexadecimal everywhere.</summary>
public enum RegisterValueBase { Decimal, Hexadecimal }

/// <summary>
/// A register map as rows: address, value, and the bits drawn the way a datasheet draws them — bit
/// seven on the left, one cell per bit, a field's name, value and cells inside a band of its own.
/// Bit cells toggle; a field's value can be typed; hovering a field shows its whole name.
///
/// Rows are realized only while they are on screen: a map of four hundred registers costs forty rows
/// of visuals, not four hundred.
/// </summary>
public sealed class RegisterTable : Border
{
    private const double NameRow = 15;
    private const double ValueRow = 20;
    private const double CellsRow = 24;
    private const double RowPadding = 6;
    private const double Column = 24;
    private const double Overscan = 2;
    private const double BlockHeight = NameRow + ValueRow + CellsRow;

    /// <summary>Height of a group heading: a line of text, not a row of bits.</summary>
    public const double HeadingHeight = 26;

    /// <summary>Bits drawn side by side; a 32-bit register is two rows of sixteen.</summary>
    private const int BitsPerRow = 16;

    private readonly ILocalizer _text;
    private readonly Grid _root = new();
    private readonly Grid _header = new();
    private readonly ScrollViewer _scroller = new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Focusable = false,
    };

    private readonly Canvas _canvas = new();
    private readonly TextBlock _empty;
    private readonly Dictionary<int, RegisterRow> _realized = new();
    private readonly Dictionary<int, FrameworkElement> _headings = new();
    private Border _sticky = null!;
    private TextBlock _stickyText = null!;
    private double[] _tops = Array.Empty<double>();
    private double _stripWidth = BitsPerRow * Column;
    private double _valueWidth = 88;
    private bool _showReadButtons;
    private bool _showWriteButtons;
    private double[] _heights = Array.Empty<double>();

    private List<Row> _visible = new();
    private readonly HashSet<string> _folded = new(StringComparer.Ordinal);
    private bool _grouped;
    private RegisterMap? _source;
    private RegisterMap? _view;
    private string _filter = "";
    private int _width = 8;
    private RegisterValueBase _base = RegisterValueBase.Decimal;

    public RegisterTable(ILocalizer localizer)
    {
        _text = localizer;
        BorderThickness = new Thickness(0);
        Background = Brushes.Transparent;
        // A table is its own scroll area: without a ceiling the page would be as tall as the map and
        // every row would have to be built. A host can raise it.
        MaxHeight = 520;

        _empty = new TextBlock { FontSize = 12, Margin = new Thickness(2, 8, 0, 6) };
        _empty.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");

        _scroller.Content = _canvas;
        _scroller.ScrollChanged += (_, _) => Realize();
        _scroller.SizeChanged += (_, _) =>
        {
            foreach (var row in _realized.Values)
                row.Element.Width = Math.Max(320, _scroller.ViewportWidth);
            Realize();
        };
        PageScroll.Attach(_scroller);

        _stickyText = new TextBlock
        {
            FontSize = 10.5,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(18, 0, 8, 0),
            IsHitTestVisible = false,
        };
        _stickyText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        _sticky = new Border
        {
            Child = _stickyText,
            Height = 22,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
        _sticky.SetResourceReference(Border.BackgroundProperty, "ContentAreaBrush");
        _sticky.SetResourceReference(Border.BorderBrushProperty, "DividerBrush");

        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_header, 0);
        Grid.SetRow(_scroller, 1);
        _root.Children.Add(_header);
        _root.Children.Add(_scroller);
        Grid.SetRow(_sticky, 1);
        _root.Children.Add(_sticky);   // after the scroller, so it is drawn over the rows
        Child = _root;

        Loaded += (_, _) => Realize();
    }

    /// <summary>
    /// Raised when a typed number was wider than the register or the field could hold. A host shows
    /// it the way it shows a slider that was clamped: "kept at 18 (the range is 0 to 255)".
    /// </summary>
    public event Action<RegisterRejection>? ValueRejected;

    /// <summary>
    /// Raised when a commit had nothing to complain about — the same number, a reparsed one, or a
    /// value that fits. A host uses it to take the note away, the way the slider's note goes away on
    /// the next commit instead of living in the log.
    /// </summary>
    public event Action? ValueAccepted;

    /// <summary>Raised after a bit click or a value edit, so the host can write the register.</summary>
    public event Action<RegisterEntry>? ValueEdited;

    /// <summary>The map as it is stored; the 16/32-bit view is derived from it.</summary>
    public RegisterMap? Map
    {
        get => _source;
        set
        {
            _source = value;
            BuildView();
        }
    }

    /// <summary>Eight, sixteen or thirty-two bits per register: a word view pairs the bytes at even addresses.</summary>
    public int ViewWidth
    {
        get => _width;
        set
        {
            var next = value is 8 or 16 or 32 ? value : 8;
            if (next == _width) return;
            _width = next;
            BuildView();
        }
    }

    /// <summary>The master switch for numbers: decimal, or hexadecimal everywhere.</summary>
    public RegisterValueBase ValueBase
    {
        get => _base;
        set
        {
            if (value == _base) return;
            _base = value;
            Refresh();
        }
    }

    /// <summary>Matches an address, a name, a field name or the value; empty shows everything.</summary>
    public string Filter
    {
        get => _filter;
        set
        {
            value ??= "";
            if (value == _filter) return;
            _filter = value;
            Refresh();
        }
    }

    public bool ShowHeader { get; set; } = true;

    /// <summary>How many registers the filter let through.</summary>
    public int VisibleCount => _visible.Count(row => !row.IsHeading);

    /// <summary>
    /// True to draw a heading over each group of registers. The group is the one the file declared,
    /// or the block of sixteen addresses the register lives in; a heading folds its group.
    /// </summary>
    public bool Grouped
    {
        get => _grouped;
        set
        {
            if (_grouped == value) return;
            _grouped = value;
            Refresh();
        }
    }

    /// <summary>How many groups are folded right now.</summary>
    public int FoldedGroups => _folded.Count;

    /// <summary>Folds or unfolds one group by the key a heading carries.</summary>
    public bool SetFolded(string group, bool folded)
    {
        var changed = folded ? _folded.Add(group) : _folded.Remove(group);
        if (changed) Refresh();
        return changed;
    }

    /// <summary>The key a register is grouped under: what it declared, or its block of sixteen.</summary>
    public static string GroupOf(RegisterEntry entry) =>
        string.IsNullOrWhiteSpace(entry.Group)
            ? "0x" + (entry.Address - entry.Address % 16).ToString("X2", CultureInfo.InvariantCulture)
            : entry.Group!;

    /// <summary>
    /// Raised when a row's read button is pressed. The table does not talk to a device, so a host
    /// that has one turns the buttons on and reads the register itself.
    /// </summary>
    public event Action<RegisterEntry>? ReadRequested;

    /// <summary>Raised when a row's write button is pressed: the host is asked to store that register.</summary>
    public event Action<RegisterEntry>? WriteRequested;

    /// <summary>True to give every row a button that asks the host to write that one register.</summary>
    public bool ShowWriteButtons
    {
        get => _showWriteButtons;
        set
        {
            if (_showWriteButtons == value) return;
            _showWriteButtons = value;
            Refresh();
        }
    }

    /// <summary>True to give every row a button that asks the host to read that one register.</summary>
    public bool ShowReadButtons
    {
        get => _showReadButtons;
        set
        {
            if (_showReadButtons == value) return;
            _showReadButtons = value;
            // Rows may already be built; the button is part of the row, so they are built again.
            Refresh();
        }
    }

    /// <summary>How many rows have visuals right now — the number that has to stay small.</summary>
    public int RealizedRows => _realized.Count;

    /// <summary>True while the name of the group being scrolled through is pinned at the top.</summary>
    public bool StickyShown => _sticky.Visibility == Visibility.Visible;

    /// <summary>The pinned group's name, or an empty string when nothing is pinned.</summary>
    public string StickyGroup => _stickyText.Text;

    /// <summary>Height of one register's row: one block for eight or sixteen bits, two for thirty-two.</summary>
    public static double HeightOf(int width) => Blocks(width) * BlockHeight + RowPadding * 2 + 1;

    private static int Blocks(int width) => width <= BitsPerRow ? 1 : (width + BitsPerRow - 1) / BitsPerRow;

    /// <summary>
    /// Shows the whole name of one field, the way hovering does. Kept public so the hover behaviour
    /// can be asserted without a mouse.
    /// </summary>
    public bool PreviewField(int address, int bias, bool show)
    {
        for (var index = 0; index < _visible.Count; index++)
        {
            if (_visible[index].Entry is not { Address: var rowAddress } || rowAddress != address) continue;
            if (!_realized.TryGetValue(index, out var row)) return false;
            return show ? row.Preview(bias) : row.Hide();
        }
        return false;
    }

    private void BuildView()
    {
        _view = _source == null ? null : _width == 8 ? _source : _source.AsWords(_width);
        var offset = _scroller.VerticalOffset;
        Refresh();
        if (offset > 0 && _scroller.CanContentScroll == false) _scroller.ScrollToVerticalOffset(offset);
    }

    private void Refresh()
    {
        foreach (var row in _realized.Values) row.Detach();
        _realized.Clear();
        _headings.Clear();
        _canvas.Children.Clear();

        var matching = _view == null
            ? new List<RegisterEntry>()
            : _view.Registers.Where(entry => Matches(entry, _filter)).ToList();
        _visible = Rows(matching);

        _stripWidth = Math.Min(BitsPerRow, Math.Max(8, _view?.Width ?? 8)) * Column;
        // Eight hex digits do not fit in a byte-wide box: the column follows the widest register.
        var digits = (_view?.Width ?? 8) <= 8 ? 2 : (_view?.Width ?? 8) <= 16 ? 4 : 8;
        _valueWidth = Math.Max(88, 54 + digits * 9);
        _heights = _visible.Select(row => row.IsHeading ? HeadingHeight : HeightOf(row.Entry!.Width)).ToArray();
        _tops = new double[_heights.Length];
        var top = 0d;
        for (var index = 0; index < _heights.Length; index++)
        {
            _tops[index] = top;
            top += _heights[index];
        }
        _canvas.Height = Math.Max(1, top + 4);
        _header.Children.Clear();
        if (ShowHeader && _visible.Count > 0) _header.Children.Add(BuildHeader());

        if (_visible.Count == 0)
        {
            _empty.Text = _view == null || _view.Registers.Count == 0
                ? _text.T("No registers.")
                : _text.T("Nothing matches the filter.");
            Canvas.SetTop(_empty, 4);
            _canvas.Children.Add(_empty);
            return;
        }

        Realize();
    }

    private void Realize()
    {
        if (_visible.Count == 0) return;

        var height = _scroller.ViewportHeight;
        if (double.IsNaN(height) || height < BlockHeight) height = BlockHeight * 8;

        var offset = _scroller.VerticalOffset;
        var first = Math.Max(0, IndexAt(offset) - (int)Overscan);
        var last = Math.Min(_visible.Count - 1, IndexAt(offset + height) + (int)Overscan);

        // A slot can change kind while scrolling: the index that held a heading now holds a register,
        // or the other way round. Either way the element in that slot is the wrong one and has to go,
        // or it is drawn underneath the new one.
        foreach (var index in _realized.Keys.ToList())
        {
            if (index >= first && index <= last && !_visible[index].IsHeading) continue;
            _realized[index].Detach();
            _canvas.Children.Remove(_realized[index].Element);
            _realized.Remove(index);
        }

        foreach (var index in _headings.Keys.ToList())
        {
            if (index >= first && index <= last && _visible[index].IsHeading) continue;
            _canvas.Children.Remove(_headings[index]);
            _headings.Remove(index);
        }

        for (var index = first; index <= last; index++)
        {
            if (_realized.ContainsKey(index) || _headings.ContainsKey(index)) continue;
            var item = _visible[index];
            var width = Math.Max(320, _scroller.ViewportWidth > 0 ? _scroller.ViewportWidth : ActualWidth);
            if (item.IsHeading)
            {
                var heading = BuildHeading(item);
                heading.Width = width;
                Canvas.SetTop(heading, _tops[index]);
                _canvas.Children.Add(heading);
                _headings[index] = heading;
                continue;
            }

            var row = new RegisterRow(this, item.Entry!, _text, _base);
            row.Element.Width = width;
            Canvas.SetTop(row.Element, _tops[index]);
            _canvas.Children.Add(row.Element);
            _realized[index] = row;
        }

        UpdateSticky();
    }

    /// <summary>
    /// A group has no size limit: thirty registers under one heading are thirty rows. Once its heading
    /// has scrolled out of sight the name is pinned at the top, so a long run still says where it is.
    /// </summary>
    private void UpdateSticky()
    {
        if (!_grouped || _visible.Count == 0)
        {
            _sticky.Visibility = Visibility.Collapsed;
            return;
        }

        var index = IndexAt(_scroller.VerticalOffset);
        if (index < 0 || index >= _visible.Count || _visible[index].IsHeading)
        {
            _sticky.Visibility = Visibility.Collapsed;
            return;
        }

        var group = GroupOf(_visible[index].Entry!);
        var heading = index;
        while (heading > 0 && !_visible[heading].IsHeading) heading--;
        if (_visible[heading].IsHeading && heading == index)
        {
            _sticky.Visibility = Visibility.Collapsed;
            return;
        }

        var count = _visible.Count(row => !row.IsHeading && GroupOf(row.Entry!) == group);
        _stickyText.Text = Label(group) + "   ·   " + _text.F($"{count} registers");
        _sticky.Visibility = Visibility.Visible;
    }

    /// <summary>The largest number a register of this width can hold.</summary>
    public static int MaxValue(int width) => width >= 32 ? int.MaxValue : (1 << width) - 1;

    /// <summary>How long a typed number may get: the digits of the widest value, plus a 0x prefix.</summary>
    public static int MaxTextLength(int width) => Math.Max(4, (width + 3) / 4 + 2);

    /// <summary>An address in the base the whole table uses: 0x20, or 32.</summary>
    public string FormatAddress(int address) => _base == RegisterValueBase.Hexadecimal
        ? "0x" + address.ToString("X2", CultureInfo.InvariantCulture)
        : address.ToString(CultureInfo.InvariantCulture);

    /// <summary>The row that covers a vertical offset (the rows are not equally tall).</summary>
    private int IndexAt(double offset)
    {
        var low = 0;
        var high = _tops.Length - 1;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (_tops[middle] <= offset) low = middle;
            else high = middle - 1;
        }
        return low < 0 ? 0 : low;
    }

    private static bool Matches(RegisterEntry entry, string filter)
    {
        if (filter.Length == 0) return true;

        var needle = filter.Trim();
        var hex = "0x" + entry.Address.ToString("X2", CultureInfo.InvariantCulture);
        if (hex.Contains(needle, StringComparison.OrdinalIgnoreCase)) return true;
        if (entry.Address.ToString(CultureInfo.InvariantCulture).Contains(needle, StringComparison.OrdinalIgnoreCase)) return true;
        if (entry.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)) return true;
        if (entry.ValueText.Contains(needle, StringComparison.OrdinalIgnoreCase)) return true;
        if (entry.Value.ToString(CultureInfo.InvariantCulture).Contains(needle, StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var field in entry.Fields)
            if (field.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>One visible slot: a register, or the heading that introduces a group.</summary>
    private sealed class Row
    {
        public RegisterEntry? Entry { get; init; }
        public string Heading { get; init; } = "";
        public int Count { get; init; }
        public bool Folded { get; init; }
        public bool IsHeading => Entry == null;
    }

    /// <summary>
    /// A group heading that is an address block ("0x20") is a number and follows the base switch;
    /// a group the file named ("Clock") is a name and is left alone.
    /// </summary>
    private string Label(string group)
    {
        if (_base == RegisterValueBase.Hexadecimal) return group;
        if (group.Length < 3 || !group.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return group;
        return int.TryParse(group[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var address)
            ? address.ToString(CultureInfo.InvariantCulture)
            : group;
    }

    private List<Row> Rows(List<RegisterEntry> matching)
    {
        if (!_grouped) return matching.Select(entry => new Row { Entry = entry }).ToList();

        var rows = new List<Row>();
        foreach (var group in matching.GroupBy(GroupOf))
        {
            var folded = _folded.Contains(group.Key);
            rows.Add(new Row { Heading = group.Key, Count = group.Count(), Folded = folded });
            if (folded) continue;
            rows.AddRange(group.Select(entry => new Row { Entry = entry }));
        }
        return rows;
    }

    private FrameworkElement BuildHeading(Row row)
    {
        var line = new Grid { Margin = new Thickness(0, 8, 0, 2) };
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var chevron = new TextBlock
        {
            Text = row.Folded ? "\uE76C" : "\uE70D",
            FontFamily = new FontFamily(Glyphs.Font),
            FontSize = 9,
            VerticalAlignment = VerticalAlignment.Center,
        };
        chevron.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");
        line.Children.Add(chevron);

        var title = new TextBlock
        {
            Text = Label(row.Heading),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        Grid.SetColumn(title, 1);
        line.Children.Add(title);

        var count = new TextBlock
        {
            Text = _text.F($"{row.Count} registers"),
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };
        count.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");
        Grid.SetColumn(count, 2);
        line.Children.Add(count);

        var button = new Button
        {
            Content = line,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Height = HeadingHeight,
            ToolTip = row.Folded ? _text.T("Unfold group") : _text.T("Fold group"),
        };
        button.SetResourceReference(StyleProperty, "GhostButton");
        AutomationProperties.SetName(button, _text.F($"Group {Label(row.Heading)}"));
        button.Click += (_, _) => SetFolded(row.Heading, !row.Folded);

        var border = new Border { Child = button, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 2, 0, 0) };
        border.SetResourceReference(Border.BorderBrushProperty, "DividerBrush");
        return border;
    }

    private FrameworkElement BuildHeader()
    {
        var grid = Columns();
        Add(grid, Head(_text.T("Address")), 0);
        Add(grid, Head(_text.T("Value")), 2);
        Add(grid, Head(_text.T("Bits")), 3);
        return new Border { Child = grid, Padding = new Thickness(0, 0, 0, 4), Margin = new Thickness(0, 0, 0, 2) };
    }

    private static TextBlock Head(string text)
    {
        var block = new TextBlock { Text = text, FontSize = 10.5 };
        block.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");
        return block;
    }

    internal static void Add(Grid grid, UIElement element, int column)
    {
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    /// <summary>
    /// The columns every row shares: the strip gets a fixed width so a bit of row 3 stands over the
    /// same bit of row 300 and over the header, which no auto-sized column would guarantee.
    /// </summary>
    internal Grid Columns()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        // Fixed, not Auto: the header has no value box, and an Auto column would put its label
        // somewhere else than the boxes below it.
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_valueWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_stripWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        return grid;
    }

    /// <summary>One register: address, name, value box, band strip and status, kept in step with the model.</summary>
    private sealed class RegisterRow
    {
        private readonly RegisterTable _table;
        private readonly RegisterEntry _entry;
        private readonly RegisterValueBase _base;
        private readonly TextBox _value;
        private readonly TextBlock _status;
        // Visual order is not bit order: a 32-bit register draws bits 31..16 above bits 15..0, so
        // every cell has to remember which bit it belongs to.
        private readonly List<(int Bit, ToggleButton Cell)> _cells = new();
        private readonly List<(RegisterField Field, TextBox Box)> _fieldBoxes = new();
        private readonly Canvas _hover = new() { IsHitTestVisible = false, ClipToBounds = false };
        private readonly Border _chip = new()
        {
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4, 1, 4, 1),
            Visibility = Visibility.Collapsed,
        };
        private readonly TextBlock _chipText = new()
        {
            FontSize = 10,
            TextWrapping = TextWrapping.NoWrap,
        };
        private bool _writing;

        public RegisterRow(RegisterTable table, RegisterEntry entry, ILocalizer text, RegisterValueBase valueBase)
        {
            _table = table;
            _entry = entry;
            _base = valueBase;

            var grid = _table.Columns();

            var address = new TextBlock
            {
                Text = table.FormatAddress(entry.Address),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
            };
            address.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            AutomationProperties.SetName(address, text.T("Address"));
            Add(grid, address, 0);

            var name = new TextBlock
            {
                Text = entry.Name,
                FontSize = 12,
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            name.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            Add(grid, name, 1);

            _value = new TextBox
            {
                Text = table.FormatRegister(entry),
                MaxLength = MaxTextLength(entry.Width),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Width = _table._valueWidth - 12,
                Padding = new Thickness(6, 2, 6, 2),
                TextAlignment = TextAlignment.Right,
                IsReadOnly = entry.IsReadOnly,
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            _value.SetResourceReference(StyleProperty, "Field");
            AutomationProperties.SetName(_value, text.F($"Value of register {entry.Address}"));
            _value.KeyDown += OnValueKey;
            _value.LostFocus += (_, _) => Commit();
            Add(grid, _value, 2);

            Add(grid, BuildStrip(text), 3);

            _status = new TextBlock
            {
                Text = "●",
                FontSize = 9,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            ApplyStatus(text);
            Add(grid, _status, 6);

            if (_table.ShowReadButtons)
            {
                var read = new Button
                {
                    Content = Glyphs.Read,
                    FontFamily = new FontFamily(Glyphs.Font),
                    FontSize = 11,
                    Padding = new Thickness(0),
                    Width = 22,
                    Height = 22,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    // A disabled looking glyph would be a lie: the table cannot know whether the host
                    // can read, so it only draws the button when the host asked for it.
                    ToolTip = text.T("Read register"),
                };
                read.SetResourceReference(StyleProperty, "GhostButton");
                AutomationProperties.SetName(read, text.F($"Read register {_table.FormatAddress(entry.Address)}"));
                read.Click += (_, _) => _table.ReadRequested?.Invoke(_entry);
                Add(grid, read, 4);
            }

            if (_table.ShowWriteButtons)
            {
                var write = new Button
                {
                    Content = Glyphs.Save,
                    FontFamily = new FontFamily(Glyphs.Font),
                    FontSize = 11,
                    Padding = new Thickness(0),
                    Width = 22,
                    Height = 22,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    ToolTip = text.T("Write register"),
                };
                write.SetResourceReference(StyleProperty, "GhostButton");
                AutomationProperties.SetName(write, text.F($"Write register {_table.FormatAddress(entry.Address)}"));
                write.Click += (_, _) => _table.WriteRequested?.Invoke(_entry);
                Add(grid, write, 5);
            }

            var border = new Border
            {
                Child = grid,
                Height = HeightOf(entry.Width),
                Padding = new Thickness(0, RowPadding, 0, RowPadding),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Background = Brushes.Transparent,
            };
            border.SetResourceReference(BorderBrushProperty, "DividerBrush");
            Element = border;

            entry.PropertyChanged += OnEntryChanged;
        }

        public FrameworkElement Element { get; }

        public void Detach() => _entry.PropertyChanged -= OnEntryChanged;

        /// <summary>Shows the whole name of the field a bit belongs to, in the band the field covers.</summary>
        public bool Preview(int bias)
        {
            var field = _entry.FieldAt(bias);
            if (field == null) return false;

            var blocks = Blocks(_entry.Width);
            var perRow = Math.Min(BitsPerRow, _entry.Width);
            var block = blocks - 1 - field.Bias / BitsPerRow;
            var lowBit = (blocks - 1 - block) * BitsPerRow;
            var to = Math.Min(field.Bias + field.Width - 1, lowBit + perRow - 1);

            _chipText.Text = field.Name + "   [" + field.Bias + ".." + (field.Bias + field.Width - 1) + "]";
            _chip.SetResourceReference(Border.BackgroundProperty, "WindowBaseBrush");
            _chip.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeBrush");
            _chip.MinWidth = (to - Math.Max(field.Bias, lowBit) + 1) * Column;
            if (_chip.Child == null) _chip.Child = _chipText;
            Canvas.SetLeft(_chip, (perRow - 1 - (to - lowBit)) * Column);
            Canvas.SetTop(_chip, block * BlockHeight);
            _chip.Visibility = Visibility.Visible;

            if (!_hover.Children.Contains(_chip)) _hover.Children.Add(_chip);
            return true;
        }

        public bool Hide()
        {
            _chip.Visibility = Visibility.Collapsed;
            return true;
        }

        private FrameworkElement BuildStrip(ILocalizer text)
        {
            // Bit order is a number, not a sentence: bit seven stays on the left even when the window
            // reads right to left. Only the columns around the strip mirror.
            var blocks = Blocks(_entry.Width);
            var perRow = Math.Min(BitsPerRow, _entry.Width);

            var strip = new Grid
            {
                VerticalAlignment = VerticalAlignment.Center,
                FlowDirection = FlowDirection.LeftToRight,
            };
            for (var block = 0; block < blocks; block++)
            {
                strip.RowDefinitions.Add(new RowDefinition { Height = new GridLength(NameRow) });
                strip.RowDefinitions.Add(new RowDefinition { Height = new GridLength(ValueRow) });
                strip.RowDefinitions.Add(new RowDefinition { Height = new GridLength(CellsRow) });
            }
            for (var column = 0; column < perRow; column++)
                strip.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Column) });

            for (var block = 0; block < blocks; block++)
            {
                var lowBit = (blocks - 1 - block) * BitsPerRow;
                var highBit = Math.Min(lowBit + perRow - 1, _entry.Width - 1);
                var baseRow = block * 3;

                foreach (var field in _entry.Fields)
                {
                    var from = Math.Max(field.Bias, lowBit);
                    var to = Math.Min(field.Bias + field.Width - 1, highBit);
                    if (from > to) continue;

                    var band = new Border
                    {
                        CornerRadius = new CornerRadius(4),
                        BorderThickness = new Thickness(1),
                        Margin = new Thickness(1, 0, 1, 0),
                        IsHitTestVisible = false,
                    };
                    band.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeBrush");
                    band.SetResourceReference(Border.BackgroundProperty, "ControlFillBrush");
                    Grid.SetColumn(band, perRow - 1 - (to - lowBit));
                    Grid.SetColumnSpan(band, to - from + 1);
                    Grid.SetRow(band, baseRow);
                    // A field that starts in this block owns the name and value rows above it; a field
                    // that only crosses into this block is drawn as a band around the bits alone.
                    Grid.SetRowSpan(band, field.Bias >= lowBit ? 3 : 1);
                    strip.Children.Add(band);
                }

                foreach (var field in _entry.Fields)
                {
                    if (field.Bias > highBit || field.Bias + field.Width - 1 < lowBit) continue;

                    var to = Math.Min(field.Bias + field.Width - 1, highBit);
                    var from = Math.Max(field.Bias, lowBit);
                    var start = perRow - 1 - (to - lowBit);
                    var span = to - from + 1;

                    // A field wider than one block (bits 12..23 of a 32-bit register) is drawn in both
                    // pieces; its name and value go on the piece that holds its top bits, so the number
                    // sits above all of it — otherwise it reads as if the field were only four bits.
                    if (to != field.Bias + field.Width - 1)
                    {
                        var more = new TextBlock
                        {
                            Text = "⋯",
                            FontSize = 10,
                            TextAlignment = TextAlignment.Center,
                            ToolTip = field.Name + "  [" + field.Bias + ".." + (field.Bias + field.Width - 1) + "]",
                            IsHitTestVisible = false,
                        };
                        more.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");
                        AutomationProperties.SetName(more, text.F($"{field.Name} continues"));
                        Grid.SetColumn(more, start);
                        Grid.SetColumnSpan(more, span);
                        Grid.SetRow(more, baseRow + 1);
                        strip.Children.Add(more);
                        continue;
                    }

                    var label = new TextBlock
                    {
                        Text = field.Name,
                        FontSize = span >= 3 ? 9.5 : 8.5,
                        TextAlignment = TextAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        Margin = new Thickness(1, 1, 1, 0),
                        IsHitTestVisible = false,
                    };
                    label.SetResourceReference(TextBlock.ForegroundProperty,
                        field.IsReadOnly ? "TextTertiaryBrush" : "TextSecondaryBrush");
                    Grid.SetColumn(label, start);
                    Grid.SetColumnSpan(label, span);
                    Grid.SetRow(label, baseRow);
                    strip.Children.Add(label);

                    // The number this field holds, editable: typing into a four-bit field writes the
                    // whole slice at once instead of asking someone to add up cells.
                    var locked = _entry.IsReadOnly || field.IsReadOnly;
                    var box = new TextBox
                    {
                        Text = _table.FormatField(field, _entry.Value),
                        MaxLength = MaxTextLength(field.Width),
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = 10,
                        Width = Math.Max(20, span * Column - 4),
                        Height = 17,
                        Padding = new Thickness(1, 0, 1, 0),
                        Margin = new Thickness(2, 1, 2, 1),
                        TextAlignment = TextAlignment.Center,
                        VerticalContentAlignment = VerticalAlignment.Center,
                        IsReadOnly = locked,
                    };
                    box.SetResourceReference(StyleProperty, "Field");
                    AutomationProperties.SetName(box, text.F($"Value of field {field.Name}"));
                    var captured = field;
                    box.KeyDown += (_, e) =>
                    {
                        if (e.Key == Key.Enter)
                        {
                            CommitField(captured, box);
                            e.Handled = true;
                        }
                        else if (e.Key == Key.Escape)
                        {
                            box.Text = _table.FormatField(captured, _entry.Value);
                            e.Handled = true;
                        }
                    };
                    box.LostFocus += (_, _) => CommitField(captured, box);
                    Grid.SetColumn(box, start);
                    Grid.SetColumnSpan(box, span);
                    Grid.SetRow(box, baseRow + 1);
                    strip.Children.Add(box);
                    _fieldBoxes.Add((field, box));
                }

                for (var bit = lowBit; bit <= highBit; bit++)
                {
                    var index = bit;
                    var owner = _entry.FieldAt(bit);
                    var locked = _entry.IsReadOnly || owner is { IsReadOnly: true };
                    var cell = new ToggleButton
                    {
                        Content = bit.ToString(CultureInfo.InvariantCulture),
                        IsChecked = _entry[bit],
                        Focusable = true,
                        IsEnabled = !locked,
                        Margin = new Thickness(2, 2, 2, 2),
                    };
                    cell.SetResourceReference(StyleProperty, "BitCell");
                    if (locked) cell.Tag = "locked";

                    var hover = Hover(owner, bit);
                    AutomationProperties.SetName(cell, text.F($"Register {_entry.Address} {hover}"));
                    cell.ToolTip = hover;
                    cell.Click += (_, _) =>
                    {
                        _entry.ToggleBit(index);
                        // A read-only field inside a writable register is drawn enabled by mistake
                        // easily; the model refuses the change, so put the cell back where it was.
                        cell.IsChecked = _entry[index];
                    };
                    Grid.SetColumn(cell, perRow - 1 - (bit - lowBit));
                    Grid.SetRow(cell, baseRow + 2);
                    strip.Children.Add(cell);
                    _cells.Add((bit, cell));
                }
            }

            Grid.SetRowSpan(_hover, blocks * 3);
            Grid.SetColumnSpan(_hover, perRow);
            strip.Children.Add(_hover);

            strip.MouseMove += (_, e) =>
            {
                var position = e.GetPosition(strip);
                var block = (int)(position.Y / BlockHeight);
                var column = (int)(position.X / Column);
                if (block < 0 || block >= blocks || column < 0 || column >= perRow) return;

                var lowBit = (blocks - 1 - block) * BitsPerRow;
                var bit = lowBit + (perRow - 1 - column);
                if (bit >= 0 && bit < _entry.Width) Preview(bit);
            };
            strip.MouseLeave += (_, _) => Hide();

            return strip;
        }

        private string Hover(RegisterField? field, int bit)
        {
            var head = field == null
                ? "bit " + bit
                : field.Name + "  [" + field.Bias + ".." + (field.Bias + field.Width - 1) + "]";
            var value = field == null
                ? (_entry[bit] ? "1" : "0")
                : _table.FormatField(field, _entry.Value);
            var locked = _entry.IsReadOnly || field is { IsReadOnly: true };
            return head + "  =  " + value + (locked ? "  ·  " + _table._text.T("Read only") : "");
        }

        private void OnValueKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Commit();
                // The number is applied; the caret stays where the number was typed. Moving focus on
                // Enter dropped it into the last field of the strip, which is never what was meant.
                _value.SelectAll();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                _value.Text = _table.FormatRegister(_entry);
                e.Handled = true;
            }
        }

        private void Commit()
        {
            if (_entry.IsReadOnly) return;

            var text = _value.Text.Trim();
            var hex = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
            if (hex) text = text[2..];
            var parsed = int.TryParse(text, hex || _base == RegisterValueBase.Hexadecimal ? NumberStyles.HexNumber : NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var value);
            if (!parsed || value < 0 || value > MaxValue(_entry.Width) || value == _entry.Value)
            {
                // A number wider than the register is dropped, not quietly masked: the box goes back
                // to what is stored so it never shows something the bits cannot hold.
                if (parsed && (value < 0 || value > MaxValue(_entry.Width)))
                    _table.ValueRejected?.Invoke(new RegisterRejection(_entry, value, MaxValue(_entry.Width), _table.FormatRegister(_entry)));
                else
                    _table.ValueAccepted?.Invoke();
                _value.Text = _table.FormatRegister(_entry);
                _value.SelectAll();
                return;
            }

            _entry.Value = value;
            _value.Text = _table.FormatRegister(_entry);   // the model may keep bits of read-only fields
            _table.ValueAccepted?.Invoke();
            _table.ValueEdited?.Invoke(_entry);
        }

        private void CommitField(RegisterField field, TextBox box)
        {
            if (_entry.IsReadOnly || field.IsReadOnly)
            {
                box.Text = _table.FormatField(field, _entry.Value);
                return;
            }

            var text = box.Text.Trim();
            var hex = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || _base == RegisterValueBase.Hexadecimal;
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
            if (!int.TryParse(text, hex ? NumberStyles.HexNumber : NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                || value < 0 || value > field.Mask)
            {
                // Wider than the field: put back the stored value instead of masking the extra bits away.
                if (int.TryParse(text, hex ? NumberStyles.HexNumber : NumberStyles.Integer, CultureInfo.InvariantCulture, out var refused)
                    && refused >= 0 && refused > field.Mask)
                    _table.ValueRejected?.Invoke(new RegisterRejection(_entry, refused, field.Mask, _table.FormatField(field, _entry.Value)));
                else
                    _table.ValueAccepted?.Invoke();
                box.Text = _table.FormatField(field, _entry.Value);
                return;
            }

            if (value == field.Read(_entry.Value))
            {
                box.Text = _table.FormatField(field, _entry.Value);
                return;
            }

            _entry.Value = field.Compose(_entry.Value, value);
            box.Text = _table.FormatField(field, _entry.Value);
            _table.ValueAccepted?.Invoke();
            _table.ValueEdited?.Invoke(_entry);
        }

        private void OnEntryChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (_writing) return;
            _writing = true;
            try
            {
                if (e.PropertyName is nameof(RegisterEntry.Value) or nameof(RegisterEntry.ValueText) or null)
                {
                    if (!_value.IsKeyboardFocused) _value.Text = _table.FormatRegister(_entry);
                    foreach (var (field, box) in _fieldBoxes)
                        if (!box.IsKeyboardFocused) box.Text = _table.FormatField(field, _entry.Value);
                }

                if (e.PropertyName is nameof(RegisterEntry.Value) or nameof(RegisterEntry.Bits) or null)
                    foreach (var (bit, cell) in _cells) cell.IsChecked = _entry[bit];

                if (e.PropertyName is nameof(RegisterEntry.Status) or null) ApplyStatus(_table._text);
            }
            finally
            {
                _writing = false;
            }
        }

        private void ApplyStatus(ILocalizer text)
        {
            var key = _entry.Status switch
            {
                RegisterStatus.Ok => "StatusOkBrush",
                RegisterStatus.Failed => "StatusBadBrush",
                _ => "TextTertiaryBrush",
            };
            _status.SetResourceReference(TextBlock.ForegroundProperty, key);
            AutomationProperties.SetName(_status, _entry.Status switch
            {
                RegisterStatus.Ok => text.T("Read ok"),
                RegisterStatus.Failed => text.T("Read failed"),
                _ => text.T("Not read yet"),
            });
        }
    }

    /// <summary>The register's value as the box shows it, in the base the table is set to.</summary>
    public string FormatRegister(RegisterEntry entry) => _base == RegisterValueBase.Hexadecimal
        ? "0x" + entry.Value.ToString(entry.Width <= 8 ? "X2" : entry.Width <= 16 ? "X4" : "X8", CultureInfo.InvariantCulture)
        : entry.Value.ToString(CultureInfo.InvariantCulture);

    private string FormatField(RegisterField field, int registerValue)
    {
        var value = field.Read(registerValue);
        if (_base == RegisterValueBase.Decimal) return value.ToString(CultureInfo.InvariantCulture);
        var digits = Math.Max(1, (field.Width + 3) / 4);
        return "0x" + value.ToString("X" + digits, CultureInfo.InvariantCulture);
    }
}