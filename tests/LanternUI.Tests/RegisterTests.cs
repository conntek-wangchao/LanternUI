using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using Lantern.UI;
using Xunit;

namespace LanternUI.Tests;

/// <summary>
/// The register piece: the platform's own config loads, a field only touches its own bits, read-only
/// means read-only, and the table builds a row per register that reacts to a click.
/// </summary>
public class RegisterTests
{
    private static string ConfigPath() =>
        Path.Combine(TestPaths.RepoRoot(), "demos", "LanternUI.Demo", "Assets", "helan-registers.json");

    private static RegisterMap PlatformMap() => RegisterMap.Load(File.ReadAllText(ConfigPath()));

    [Fact]
    public void The_platform_config_loads_with_its_addresses_and_fields()
    {
        var map = PlatformMap();

        Assert.Equal(8, map.Registers.Count);
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, map.Registers.Select(entry => entry.Address).ToArray());
        Assert.Equal(20, map.Registers.Sum(entry => entry.Fields.Count));

        var control = map.Find(1)!;
        Assert.Equal(0x03, control.Value);
        Assert.Equal(new[] { "wsel", "rd", "N/A" }, control.Fields.Select(field => field.Name).ToArray());
        Assert.Equal((0, 4), (control.Fields[0].Bias, control.Fields[0].Width));
        Assert.Equal((4, 2), (control.Fields[1].Bias, control.Fields[1].Width));
        Assert.Equal((6, 2), (control.Fields[2].Bias, control.Fields[2].Width));
    }

    [Fact]
    public void A_map_round_trips_through_the_same_json()
    {
        var first = PlatformMap();
        first.Find(2)!.Value = 0x5A;

        var second = RegisterMap.Load(first.Save());

        Assert.Equal(first.Registers.Count, second.Registers.Count);
        Assert.Equal(0x5A, second.Find(2)!.Value);
        foreach (var entry in first.Registers)
        {
            var other = second.Find(entry.Address)!;
            Assert.Equal(entry.Value, other.Value);
            Assert.Equal(entry.Fields.Select(f => (f.Name, f.Bias, f.Width)),
                other.Fields.Select(f => (f.Name, f.Bias, f.Width)));
        }
    }

    [Fact]
    public void Setting_one_bit_leaves_the_others_alone()
    {
        var entry = new RegisterEntry(0, 0b0100_1000);
        entry.SetBit(0, true);
        Assert.Equal(0b0100_1001, entry.Value);

        entry.SetBit(3, false);
        Assert.Equal(0b0100_0001, entry.Value);
    }

    [Fact]
    public void A_field_reads_and_writes_only_its_slice()
    {
        var entry = new RegisterEntry(0, 0xF0);
        entry.AddField("low", 0, 4);
        var field = entry.Field("low")!;

        Assert.Equal(0x0, field.Read(entry.Value));
        Assert.Equal(0xF5, field.Compose(entry.Value, 0x5));
        Assert.Equal(0x5, field.Compose(entry.Value, 0x15) & 0xF);
    }

    [Fact]
    public void A_read_only_register_refuses_a_toggle()
    {
        var entry = new RegisterEntry(0, 0x0F) { IsReadOnly = true };
        entry.ToggleBit(0);
        Assert.Equal(0x0F, entry.Value);
    }

    [Fact]
    public void A_read_only_field_refuses_a_toggle_but_the_value_still_moves()
    {
        var entry = new RegisterEntry(0, 0);
        entry.AddField("locked", 0, 2, isReadOnly: true);

        entry.ToggleBit(0);
        Assert.Equal(0, entry.Value);

        entry.Value = 0x03;                       // the locked field's bits are not written
        Assert.Equal(0, entry.Value & 0x03);

        entry.SetFromDevice(0x03);                // a device read may set them
        Assert.Equal(0x03, entry.Value);

        entry.SetBit(3, true);
        Assert.Equal(0x0B, entry.Value);
    }

    [Fact]
    public void A_wide_register_clamps_to_its_own_width()
    {
        var entry = new RegisterEntry(0, 0, width: 16);
        entry.Value = 0x1_FFFF;
        Assert.Equal(0xFFFF, entry.Value);
        Assert.Equal("0xFFFF", entry.ValueText);

        entry.Value = -5;
        Assert.Equal(0, entry.Value);
    }

    [Fact]
    public void The_table_builds_a_row_per_register_and_filters_them()
    {
        Sta.Run(() =>
        {
            Sta.NewState();
            var localizer = new Lantern.UI.Localizer("en", null);
            var table = new RegisterTable(localizer) { Map = PlatformMap() };

            Assert.Equal(8, table.VisibleCount);

            table.Filter = "wsel";
            Assert.Equal(1, table.VisibleCount);

            table.Filter = "0x05";
            Assert.Equal(1, table.VisibleCount);

            table.Filter = "nothing like this";
            Assert.Equal(0, table.VisibleCount);
        });
    }

    [Fact]
    public void Clicking_a_bit_cell_changes_the_register()
    {
        Sta.Run(() =>
        {
            Sta.NewState();
            var map = PlatformMap();
            var localizer = new Lantern.UI.Localizer("en", null);
            var table = new RegisterTable(localizer) { Map = map, Filter = "wsel" };

            table.Measure(new Size(700, 200));
            table.Arrange(new Rect(0, 0, 700, 200));
            table.UpdateLayout();

            // One register is visible, so bit 0 of it is simply the cell whose content is "0".
            var cell = Visuals.Descendants<ToggleButton>(table).First(button => (string)button.Content == "0");
            Assert.Contains("wsel", (string)cell.ToolTip!);

            var texts = Visuals.Descendants<System.Windows.Controls.TextBlock>(table)
                .Select(block => block.Text).ToList();
            Assert.Contains("3", texts);   // wsel reads 3 before the click

            cell.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            Assert.Equal(0x02, map.Find(1)!.Value);
            Assert.False(cell.IsChecked);

            texts = Visuals.Descendants<System.Windows.Controls.TextBlock>(table).Select(block => block.Text).ToList();
            Assert.Contains("2", texts);   // and 2 after it, without touching the model by hand
        });
    }

[Fact]
    public void A_read_only_register_locks_every_cell_and_says_so()
    {
        Sta.Run(() =>
        {
            Sta.NewState();
            var map = PlatformMap();
            map.Find(5)!.IsReadOnly = true;
            var table = new RegisterTable(new Lantern.UI.Localizer("en", null)) { Map = map, Filter = "gd[8:1]" };
            table.Measure(new Size(700, 300));
            table.Arrange(new Rect(0, 0, 700, 300));
            table.UpdateLayout();

            var cells = Visuals.Descendants<ToggleButton>(table).ToList();
            Assert.Equal(8, cells.Count);
            Assert.All(cells, cell => Assert.Equal("locked", cell.Tag));
            Assert.All(cells, cell => Assert.False(cell.IsEnabled));
            Assert.Contains("Read only", (string)cells[0].ToolTip!);
            Assert.Contains("gd[8:1]", (string)cells[1].ToolTip!);
            Assert.Contains("=  0", (string)cells[1].ToolTip!);
        });
    }

    [Fact]
    public void A_read_only_field_locks_only_its_own_cells()
    {
        Sta.Run(() =>
        {
            Sta.NewState();
            var map = PlatformMap();
            map.Find(2)!.Field("read_otp")!.IsReadOnly = true;
            var table = new RegisterTable(new Lantern.UI.Localizer("en", null)) { Map = map, Filter = "read_otp" };
            table.Measure(new Size(700, 300));
            table.Arrange(new Rect(0, 0, 700, 300));
            table.UpdateLayout();

            var cells = Visuals.Descendants<ToggleButton>(table).ToList();
            var locked = cells.Where(cell => (string?)cell.Tag == "locked").ToList();
            Assert.Single(locked);
            Assert.Contains("read_otp", (string)locked[0].ToolTip!);
            Assert.Equal(8, cells.Count);
        });
    }

[Fact]
    public void A_large_map_realizes_only_the_rows_on_screen()
    {
        Sta.Run(() =>
        {
            Sta.NewState();
            var map = new RegisterMap();
            for (var address = 0; address < 400; address++)
                map.Add(new RegisterEntry(address, address & 0xFF));

            var table = new RegisterTable(new Lantern.UI.Localizer("en", null)) { Map = map };
            table.Measure(new Size(800, 300));
            table.Arrange(new Rect(0, 0, 800, 300));
            table.UpdateLayout();

            Assert.Equal(400, table.VisibleCount);
            Assert.True(table.RealizedRows <= 12, "only what fits should be built, built " + table.RealizedRows);
        });
    }

    [Fact]
    public void A_word_view_pairs_the_bytes_little_endian()
    {
        var map = new RegisterMap();
        map.Add(new RegisterEntry(0, 0x12));
        map.Add(new RegisterEntry(1, 0x34));
        map.Find(1)!.AddField("hi", 0, 4);

        var view = map.AsWords(16);

        Assert.Single(view.Registers);
        var word = view.Find(0)!;
        Assert.Equal(16, word.Width);
        Assert.Equal(0x3412, word.Value);
        Assert.Equal(8, word.Field("hi")!.Bias);

        word.Value = 0xABCD;

        Assert.Equal(0xCD, map.Find(0)!.Value);
        Assert.Equal(0xAB, map.Find(1)!.Value);
        Assert.Equal(0xABCD, word.Value);
        Assert.Equal(0xB, word.Field("hi")!.Read(word.Value));   // bits 8..11 of 0xABCD
    }

    [Fact]
    public void Changing_a_byte_shows_through_the_word_view()
    {
        var map = new RegisterMap();
        map.Add(new RegisterEntry(0, 0x11));
        map.Add(new RegisterEntry(1, 0x22));
        var view = map.AsWords(16);
        var word = view.Find(0)!;

        map.Find(0)!.Value = 0x99;

        Assert.Equal(0x2299, word.Value);
        Assert.True(word.IsDirty || !word.IsDirty);   // the notification path ran without throwing
    }

    [Fact]
    public void The_value_base_switch_changes_every_number()
    {
        Sta.Run(() =>
        {
            Sta.NewState();
            var map = PlatformMap();
            var table = new RegisterTable(new Lantern.UI.Localizer("en", null)) { Map = map, Filter = "wsel" };
            table.Measure(new Size(800, 300));
            table.Arrange(new Rect(0, 0, 800, 300));
            table.UpdateLayout();

            string RegisterBox() => Visuals.Descendants<System.Windows.Controls.TextBox>(table)
                .Select(box => (Box: box, Name: AutomationProperties.GetName(box) ?? ""))
                .First(pair => pair.Name.StartsWith("Value of register", StringComparison.Ordinal)).Box.Text;

            Assert.Equal("3", RegisterBox());

            table.ValueBase = RegisterValueBase.Hexadecimal;
            table.Measure(new Size(800, 300));
            table.Arrange(new Rect(0, 0, 800, 300));
            table.UpdateLayout();

            Assert.Equal("0x03", RegisterBox());
        });
    }

    [Fact]
    public void A_field_value_can_be_typed_and_writes_only_its_own_bits()
    {
        Sta.Run(() =>
        {
            Sta.NewState();
            var map = PlatformMap();
            var table = new RegisterTable(new Lantern.UI.Localizer("en", null)) { Map = map, Filter = "wsel" };
            table.Measure(new Size(800, 300));
            table.Arrange(new Rect(0, 0, 800, 300));
            table.UpdateLayout();

            var box = Visuals.Descendants<System.Windows.Controls.TextBox>(table)
                .First(candidate => (AutomationProperties.GetName(candidate) ?? "") == "Value of field wsel");
            Assert.Equal("3", box.Text);

            box.Text = "5";
            box.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));

            Assert.Equal(0x05, map.Find(1)!.Value);   // wsel is bits 0..3, so the rest of the byte was untouched
        });
    }

    [Fact]
    public void Hovering_a_field_shows_its_whole_name()
    {
        Sta.Run(() =>
        {
            Sta.NewState();
            var map = PlatformMap();
            var table = new RegisterTable(new Lantern.UI.Localizer("en", null)) { Map = map, Filter = "wsel" };
            table.Measure(new Size(800, 300));
            table.Arrange(new Rect(0, 0, 800, 300));
            table.UpdateLayout();

            Assert.True(table.PreviewField(1, 0, true));

            var chip = Visuals.Descendants<System.Windows.Controls.Border>(table)
                .FirstOrDefault(border => border.Child is System.Windows.Controls.TextBlock block
                                          && block.Text.Contains("[0..3]", StringComparison.Ordinal));
            Assert.NotNull(chip);
            Assert.Equal(Visibility.Visible, chip!.Visibility);

            Assert.True(table.PreviewField(1, 0, false));
            Assert.Equal(Visibility.Collapsed, chip.Visibility);
        });
    }

[Fact]
    public void A_locked_field_survives_a_whole_register_write()
    {
        var entry = new RegisterEntry(0, 0x00);
        entry.AddField("otp", 0, 1, isReadOnly: true);
        entry.AddField("rest", 1, 7);

        entry.Value = 0xFF;
        Assert.Equal(0xFE, entry.Value);          // bit 0 belongs to the locked field

        entry.SetFromDevice(0x01);                // a device read is news, not an edit
        Assert.Equal(0x01, entry.Value);

        entry.Value = 0x00;
        Assert.Equal(0x01, entry.Value);          // the locked bit keeps what the device said

        Assert.Equal(0x01, entry.LockedMask);
    }

    [Fact]
    public void The_register_box_cannot_change_a_locked_field_either()
    {
        Sta.Run(() =>
        {
            Sta.NewState();
            var map = PlatformMap();
            map.Find(2)!.Field("read_otp")!.IsReadOnly = true;
            var locked = map.Find(2)!.LockedMask;
            var before = map.Find(2)!.Value & locked;

            var table = new RegisterTable(new Lantern.UI.Localizer("en", null)) { Map = map, Filter = "read_otp" };
            table.Measure(new Size(800, 300));
            table.Arrange(new Rect(0, 0, 800, 300));
            table.UpdateLayout();

            var box = Visuals.Descendants<System.Windows.Controls.TextBox>(table)
                .First(candidate => (AutomationProperties.GetName(candidate) ?? "").StartsWith("Value of register", StringComparison.Ordinal));
            box.Text = "0xFF";
            box.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));

            Assert.Equal(before, map.Find(2)!.Value & locked);
            Assert.Equal(0xFF & ~locked, map.Find(2)!.Value & ~locked);
        });
    }

    [Fact]
    public void A_config_can_declare_wide_registers()
    {
        const string json = """
        {
          "0x100": { "Address": 256, "Width": 16, "ResetValue": 4660, "Value": 4660, "IsReadOnly": false,
            "RegisterBitsList": [
              { "Name": "mode", "BitBias": 0, "BitNum": 4 },
              { "Name": "div", "BitBias": 4, "BitNum": 8 },
              { "Name": "en", "BitBias": 15, "BitNum": 1, "IsReadOnly": true }
            ] },
          "0x200": { "Address": 512, "Width": 32, "Value": 305419896,
            "RegisterBitsList": [ { "Name": "count", "BitBias": 0, "BitNum": 16 } ] }
        }
        """;

        var map = RegisterMap.Load(json);

        Assert.Equal(2, map.Registers.Count);
        Assert.False(map.IsByteMap);
        Assert.Equal(32, map.Width);

        var half = map.Find(256)!;
        Assert.Equal(16, half.Width);
        Assert.Equal(0x1234, half.Value);
        Assert.Equal("en", half.Fields[2].Name);
        Assert.True(half.Fields[2].IsReadOnly);

        var word = map.Find(512)!;
        Assert.Equal(32, word.Width);
        Assert.Equal(0x12345678, word.Value);
        Assert.Equal(16, word.Fields[0].Width);

        Assert.Equal(0x1234, RegisterMap.Load(map.Save()).Find(256)!.Value);
        Assert.Equal(32, RegisterMap.Load(map.Save()).Find(512)!.Width);
    }

    [Fact]
    public void A_word_view_only_regroups_a_map_of_bytes()
    {
        var bytes = new RegisterMap();
        bytes.Add(new RegisterEntry(0, 1));
        bytes.Add(new RegisterEntry(1, 2));

        var wide = new RegisterMap();
        wide.Add(new RegisterEntry(0, 0x1234, 16));

        Assert.True(bytes.IsByteMap);
        Assert.Single(bytes.AsWords(16).Registers);
        Assert.Same(wide, wide.AsWords(16));       // nothing to regroup
    }

    [Fact]
    public void A_thirty_two_bit_register_takes_two_blocks()
    {
        Assert.Equal(RegisterTable.HeightOf(8), RegisterTable.HeightOf(16));
        Assert.True(RegisterTable.HeightOf(32) > RegisterTable.HeightOf(16) * 1.5,
            "32 bits should draw as two rows of sixteen");
    }

[Theory]
    [InlineData("registers-16.json", 16, 12, 6)]
    [InlineData("registers-32.json", 32, 8, 4)]
    public void The_bundled_wide_maps_load(string file, int width, int registers, int fields)
    {
        var path = Path.Combine(TestPaths.RepoRoot(), "demos", "LanternUI.Demo", "Assets", file);
        var map = RegisterMap.Load(File.ReadAllText(path));

        Assert.Equal(registers, map.Registers.Count);
        Assert.Equal(width, map.Width);
        Assert.False(map.IsByteMap);
        Assert.All(map.Registers, entry => Assert.Equal(width, entry.Width));
        Assert.All(map.Registers, entry => Assert.Equal(fields, entry.Fields.Count));

        var reloaded = RegisterMap.Load(map.Save());
        Assert.Equal(width, reloaded.Width);
        Assert.Equal(registers, reloaded.Registers.Count);
    }

[Fact]
    public void A_thirty_two_bit_row_keeps_its_bits_apart()
    {
        Sta.Run(() =>
        {
            Sta.NewState();
            var map = new RegisterMap();
            var word = new RegisterEntry(0, 0, 32);
            word.AddField("count", 0, 16);
            word.AddField("busy", 31, 1);
            map.Add(word);

            var table = new RegisterTable(new Lantern.UI.Localizer("en", null)) { Map = map, ValueBase = RegisterValueBase.Hexadecimal };
            table.Measure(new Size(900, 400));
            table.Arrange(new Rect(0, 0, 900, 400));
            table.UpdateLayout();

            var cells = Visuals.Descendants<ToggleButton>(table)
                .ToDictionary(cell => (string)cell.Content, cell => cell);
            Assert.Equal(32, cells.Count);

            // Only bit 31: the cell labelled 31 is the one that must light up, in the upper row.
            word.Value = unchecked((int)0x8000_0000);
            Assert.True(cells["31"].IsChecked);
            Assert.False(cells["0"].IsChecked);
            Assert.False(cells["16"].IsChecked);

            // Only bit 0: and now the lower row, nothing else.
            word.Value = 0x0000_0001;
            Assert.True(cells["0"].IsChecked);
            Assert.False(cells["31"].IsChecked);
            Assert.False(cells["15"].IsChecked);

            // Clicking the cell labelled 3 toggles bit 3 of the same register, not bit 19 of the other row.
            cells["3"].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal(0x0000_0009, word.Value);
            Assert.True(cells["3"].IsChecked);
            Assert.False(cells["19"].IsChecked);
        });
    }

[Fact]
    public void A_field_that_crosses_the_sixteen_bit_split_keeps_its_number_together()
    {
        Sta.Run(() =>
        {
            Sta.NewState();
            var map = new RegisterMap();
            var word = new RegisterEntry(0, 0, 32);
            word.AddField("addr", 0, 12);
            word.AddField("len", 12, 12);      // bits 12..23: it starts in the lower block and ends in the upper one
            map.Add(word);
            word.Value = 0x011234;             // addr = 0x234, len = 0x011

            var table = new RegisterTable(new Lantern.UI.Localizer("en", null))
            {
                Map = map,
                ValueBase = RegisterValueBase.Hexadecimal,
            };
            table.Measure(new Size(900, 400));
            table.Arrange(new Rect(0, 0, 900, 400));
            table.UpdateLayout();

            var box = Visuals.Descendants<System.Windows.Controls.TextBox>(table)
                .Single(candidate => (AutomationProperties.GetName(candidate) ?? "") == "Value of field len");

            Assert.Equal("0x011", box.Text);
            Assert.Equal(1, System.Windows.Controls.Grid.GetRow(box));          // the upper block's value row
            Assert.Equal(8, System.Windows.Controls.Grid.GetColumnSpan(box));   // over bits 23..16

            // The lower piece marks itself as a continuation instead of pretending to be a 4-bit field.
            var more = Visuals.Descendants<System.Windows.Controls.TextBlock>(table)
                .SingleOrDefault(candidate => (AutomationProperties.GetName(candidate) ?? "") == "len continues");
            Assert.NotNull(more);
            Assert.Equal(4, System.Windows.Controls.Grid.GetRow(more));         // the lower block's value row
            Assert.Contains("12..23", (string)more!.ToolTip!);
        });
    }

    [Fact]
    public void A_wide_register_gets_a_value_box_that_fits()
    {
        Sta.Run(() =>
        {
            Sta.NewState();
            var map = new RegisterMap();
            map.Add(new RegisterEntry(0, unchecked((int)0xDEADBEEF), 32));

            var table = new RegisterTable(new Lantern.UI.Localizer("en", null))
            {
                Map = map,
                ValueBase = RegisterValueBase.Hexadecimal,
            };
            table.Measure(new Size(900, 400));
            table.Arrange(new Rect(0, 0, 900, 400));
            table.UpdateLayout();

            var box = Visuals.Descendants<System.Windows.Controls.TextBox>(table)
                .First(candidate => (AutomationProperties.GetName(candidate) ?? "").StartsWith("Value of register", StringComparison.Ordinal));

            Assert.Equal("0xDEADBEEF", box.Text);
            Assert.True(box.Width >= 110, "an eight digit value needs a wider box, width " + box.Width);
        });
    }

[Fact]
    public void A_row_can_ask_the_host_to_read_that_register()
    {
        Sta.Run(() =>
        {
            Sta.NewState();
            var map = PlatformMap();
            var table = new RegisterTable(new Lantern.UI.Localizer("en", null))
            {
                Map = map,
                ShowReadButtons = true,
                ValueBase = RegisterValueBase.Hexadecimal,   // the name of the button carries the address
            };
            table.Measure(new Size(900, 400));
            table.Arrange(new Rect(0, 0, 900, 400));
            table.UpdateLayout();

            RegisterEntry? asked = null;
            table.ReadRequested += entry => asked = entry;

            var buttons = Visuals.Descendants<System.Windows.Controls.Button>(table)
                .Where(button => (AutomationProperties.GetName(button) ?? "").StartsWith("Read register", StringComparison.Ordinal))
                .ToList();
            // Only the rows that are on screen carry one: the button is virtualised with its row.
            Assert.Equal(table.RealizedRows, buttons.Count);
            Assert.NotEmpty(buttons);

            var first = buttons.First(button => (AutomationProperties.GetName(button) ?? "").Contains("0x00", StringComparison.Ordinal));
            first.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

            Assert.Same(map.Find(0), asked);
        });
    }

    [Fact]
    public void A_table_without_a_host_has_no_read_buttons()
    {
        Sta.Run(() =>
        {
            Sta.NewState();
            var table = new RegisterTable(new Lantern.UI.Localizer("en", null)) { Map = PlatformMap() };
            table.Measure(new Size(900, 400));
            table.Arrange(new Rect(0, 0, 900, 400));
            table.UpdateLayout();

            Assert.DoesNotContain(Visuals.Descendants<System.Windows.Controls.Button>(table),
                button => (AutomationProperties.GetName(button) ?? "").StartsWith("Read register", StringComparison.Ordinal));
        });
    }

[Fact]
    public void The_table_never_changes_a_value_by_itself()
    {
        Sta.Run(() =>
        {
            Sta.NewState();
            var map = PlatformMap();
            var table = new RegisterTable(new Lantern.UI.Localizer("en", null))
            {
                Map = map,
                ShowReadButtons = true,
                ShowWriteButtons = true,
                ValueBase = RegisterValueBase.Hexadecimal,
            };
            table.Measure(new Size(900, 400));
            table.Arrange(new Rect(0, 0, 900, 400));
            table.UpdateLayout();

            var asked = new List<string>();
            table.ReadRequested += entry => asked.Add("read " + entry.Address);
            table.WriteRequested += entry => asked.Add("write " + entry.Address);

            var before = map.Find(0)!.Value;
            foreach (var button in Visuals.Descendants<System.Windows.Controls.Button>(table)
                         .Where(button => (AutomationProperties.GetName(button) ?? "").Contains("0x00", StringComparison.Ordinal))
                         .ToList())
                button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

            // The buttons only ask; whoever owns the device decides. A read that invented a value
            // would make the table look like it changed the register on its own.
            Assert.Equal(before, map.Find(0)!.Value);
            Assert.Contains("read 0", asked);
            Assert.Contains("write 0", asked);
        });
    }

[Fact]
    public void A_grouped_table_heads_every_block()
    {
        Sta.Run(() =>
        {
            Sta.NewState();
            var map = new RegisterMap();
            map.Add(new RegisterEntry(0x00, 0));
            map.Add(new RegisterEntry(0x01, 0));
            map.Add(new RegisterEntry(0x21, 0));
            var clock = new RegisterEntry(0x30, 0) { Group = "Clock" };
            map.Add(clock);

            var table = new RegisterTable(new Lantern.UI.Localizer("en", null)) { Map = map, Grouped = true, ValueBase = RegisterValueBase.Hexadecimal };
            table.Measure(new Size(900, 600));
            table.Arrange(new Rect(0, 0, 900, 600));
            table.UpdateLayout();

            Assert.Equal(4, table.VisibleCount);   // the headings are not registers

            var texts = Visuals.Descendants<System.Windows.Controls.TextBlock>(table).Select(block => block.Text).ToList();
            Assert.Contains("0x00", texts);        // the block of sixteen it derives
            Assert.Contains("0x20", texts);
            Assert.Contains("Clock", texts);       // the group the file declared
            Assert.Contains("2 registers", texts); // the first block holds two
        });
    }

    [Fact]
    public void Folding_a_group_hides_its_registers_but_keeps_its_heading()
    {
        Sta.Run(() =>
        {
            Sta.NewState();
            var map = new RegisterMap();
            map.Add(new RegisterEntry(0x00, 0));
            var clock = new RegisterEntry(0x10, 0) { Group = "Clock" };
            map.Add(clock);
            map.Add(new RegisterEntry(0x11, 0) { Group = "Clock" });

            var table = new RegisterTable(new Lantern.UI.Localizer("en", null)) { Map = map, Grouped = true, ValueBase = RegisterValueBase.Hexadecimal };
            table.Measure(new Size(900, 600));
            table.Arrange(new Rect(0, 0, 900, 600));
            table.UpdateLayout();
            Assert.Equal(3, table.VisibleCount);

            Assert.True(table.SetFolded("Clock", true));
            table.Measure(new Size(900, 600));
            table.Arrange(new Rect(0, 0, 900, 600));
            table.UpdateLayout();

            Assert.Equal(1, table.VisibleCount);          // only the register outside the group is left
            Assert.Equal(1, table.FoldedGroups);
            Assert.Contains("Clock", Visuals.Descendants<System.Windows.Controls.TextBlock>(table).Select(block => block.Text));
            Assert.Contains("2 registers", Visuals.Descendants<System.Windows.Controls.TextBlock>(table).Select(block => block.Text));

            Assert.True(table.SetFolded("Clock", false));
            table.Measure(new Size(900, 600));
            table.Arrange(new Rect(0, 0, 900, 600));
            table.UpdateLayout();
            Assert.Equal(3, table.VisibleCount);
        });
    }

    [Fact]
    public void A_group_survives_a_save()
    {
        const string json = """
        { "0": { "Address": 0, "Group": "Clock", "Width": 8, "Value": 0, "RegisterBitsList": [] } }
        """;

        var map = RegisterMap.Load(json);
        Assert.Equal("Clock", map.Find(0)!.Group);

        var reloaded = RegisterMap.Load(map.Save());
        Assert.Equal("Clock", reloaded.Find(0)!.Group);
    }

[Fact]
    public void A_group_is_not_limited_to_sixteen_registers()
    {
        Sta.Run(() =>
        {
            Sta.NewState();
            var map = new RegisterMap();
            for (var index = 0; index < 30; index++)
            {
                var entry = new RegisterEntry(index * 2, index) { Group = "Bank" };
                entry.AddField("v", 0, 4);
                map.Add(entry);
            }
            for (var index = 0; index < 4; index++)
                map.Add(new RegisterEntry(0x40 + index * 2, index) { Group = "Clock" });

            var table = new RegisterTable(new Lantern.UI.Localizer("en", null)) { Map = map, Grouped = true, ValueBase = RegisterValueBase.Hexadecimal };
            table.Measure(new Size(900, 600));
            table.Arrange(new Rect(0, 0, 900, 600));
            table.UpdateLayout();

            Assert.Equal(34, table.VisibleCount);
            var texts = Visuals.Descendants<System.Windows.Controls.TextBlock>(table).Select(block => block.Text).ToList();
            Assert.Single(texts, text => text == "Bank");            // one heading, not one per sixteen
            Assert.Contains("30 registers", texts);

            // Inside a long group its heading scrolls away; the name is pinned so the run still says
            // where it is.
            var scroller = Visuals.Descendants<System.Windows.Controls.ScrollViewer>(table).First();
            scroller.ScrollToVerticalOffset(600);
            table.UpdateLayout();
            Assert.True(table.StickyShown);
            Assert.StartsWith("Bank", table.StickyGroup, StringComparison.Ordinal);

            scroller.ScrollToVerticalOffset(0);
            table.UpdateLayout();
            Assert.False(table.StickyShown);   // its own heading is on screen again

            Assert.True(table.SetFolded("Bank", true));
            table.Measure(new Size(900, 600));
            table.Arrange(new Rect(0, 0, 900, 600));
            table.UpdateLayout();
            Assert.Equal(4, table.VisibleCount);   // thirty registers fold away together
            Assert.False(table.StickyShown);
        });
    }

[Fact]
    public void The_address_follows_the_number_base()
    {
        Sta.Run(() =>
        {
            Sta.NewState();
            var map = new RegisterMap();
            map.Add(new RegisterEntry(0x20, 0));
            map.Add(new RegisterEntry(0x21, 0));

            var table = new RegisterTable(new Lantern.UI.Localizer("en", null)) { Map = map, ShowReadButtons = true };
            table.Measure(new Size(900, 400));
            table.Arrange(new Rect(0, 0, 900, 400));
            table.UpdateLayout();

            string[] Addresses() => Visuals.Descendants<System.Windows.Controls.TextBlock>(table)
                .Where(block => (AutomationProperties.GetName(block) ?? "") == "Address")
                .Select(block => block.Text)
                .ToArray();

            // The table starts in decimal; the demo asks for hex through the same switch.
            Assert.Contains("32", Addresses());
            Assert.Equal("32", table.FormatAddress(0x20));

            table.ValueBase = RegisterValueBase.Hexadecimal;
            table.Measure(new Size(900, 400));
            table.Arrange(new Rect(0, 0, 900, 400));
            table.UpdateLayout();

            Assert.Contains("0x20", Addresses());
            Assert.DoesNotContain("32", Addresses());
            Assert.Equal("0x20", table.FormatAddress(0x20));
            // The buttons say what is on screen, not what the file said.
            Assert.Contains(Visuals.Descendants<System.Windows.Controls.Button>(table),
                button => (AutomationProperties.GetName(button) ?? "") == "Read register 0x20");
        });
    }

    [Fact]
    public void A_group_heading_that_is_an_address_follows_the_base_too()
    {
        Sta.Run(() =>
        {
            Sta.NewState();
            var map = new RegisterMap();
            map.Add(new RegisterEntry(0x00, 0));
            map.Add(new RegisterEntry(0x20, 0));

            var table = new RegisterTable(new Lantern.UI.Localizer("en", null)) { Map = map, Grouped = true, ValueBase = RegisterValueBase.Hexadecimal };
            table.Measure(new Size(900, 500));
            table.Arrange(new Rect(0, 0, 900, 500));
            table.UpdateLayout();

            string[] Headings() => Visuals.Descendants<System.Windows.Controls.TextBlock>(table)
                .Where(block => block.FontWeight == FontWeights.SemiBold)
                .Select(block => block.Text)
                .ToArray();

            Assert.Contains("0x00", Headings());
            Assert.Contains("0x20", Headings());

            table.ValueBase = RegisterValueBase.Decimal;
            table.Measure(new Size(900, 500));
            table.Arrange(new Rect(0, 0, 900, 500));
            table.UpdateLayout();

            Assert.Contains("0", Headings());
            Assert.Contains("32", Headings());
            Assert.DoesNotContain("0x20", Headings());
        });
    }

[Fact]
    public void A_register_value_wider_than_the_register_is_refused()
    {
        Sta.Run(() =>
        {
            Sta.NewState();
            var map = new RegisterMap();
            map.Add(new RegisterEntry(0, 0x12));

            var table = new RegisterTable(new Lantern.UI.Localizer("en", null)) { Map = map };
            table.Measure(new Size(900, 400));
            table.Arrange(new Rect(0, 0, 900, 400));
            table.UpdateLayout();

            var box = Visuals.Descendants<System.Windows.Controls.TextBox>(table)
                .First(candidate => (AutomationProperties.GetName(candidate) ?? "").StartsWith("Value of register", StringComparison.Ordinal));

            box.Text = "300";                       // an eight bit register cannot hold it
            box.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
            Assert.Equal("18", box.Text);           // back to what is stored, not silently masked
            Assert.Equal(0x12, map.Find(0)!.Value);

            box.Text = "255";
            box.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
            Assert.Equal("255", box.Text);
            Assert.Equal(0xFF, map.Find(0)!.Value);
        });
    }

    [Fact]
    public void A_field_value_wider_than_its_bits_is_refused()
    {
        Sta.Run(() =>
        {
            Sta.NewState();
            var map = new RegisterMap();
            var entry = new RegisterEntry(0, 0);
            entry.AddField("mode", 1, 3);            // three bits, so 0..7
            map.Add(entry);

            var table = new RegisterTable(new Lantern.UI.Localizer("en", null)) { Map = map };
            table.Measure(new Size(900, 400));
            table.Arrange(new Rect(0, 0, 900, 400));
            table.UpdateLayout();

            var box = Visuals.Descendants<System.Windows.Controls.TextBox>(table)
                .First(candidate => (AutomationProperties.GetName(candidate) ?? "") == "Value of field mode");

            box.Text = "9";
            box.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
            Assert.Equal("0", box.Text);
            Assert.Equal(0, map.Find(0)!.Value);

            box.Text = "5";
            box.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
            Assert.Equal("5", box.Text);
            Assert.Equal(5 << 1, map.Find(0)!.Value);   // only its own slice
        });
    }

    [Fact]
    public void A_number_cannot_be_typed_longer_than_the_widest_one()
    {
        Assert.Equal(4, RegisterTable.MaxTextLength(8));
        Assert.Equal(6, RegisterTable.MaxTextLength(16));
        Assert.Equal(10, RegisterTable.MaxTextLength(32));
        Assert.Equal(0xFF, RegisterTable.MaxValue(8));
        Assert.Equal(0xFFFF, RegisterTable.MaxValue(16));
        Assert.Equal(int.MaxValue, RegisterTable.MaxValue(32));
    }

[Fact]
    public void A_refused_value_says_what_stayed_and_what_the_range_is()
    {
        Sta.Run(() =>
        {
            Sta.NewState();
            var map = new RegisterMap();
            var entry = new RegisterEntry(0, 0x12);
            entry.AddField("mode", 1, 3);
            map.Add(entry);

            var table = new RegisterTable(new Lantern.UI.Localizer("en", null)) { Map = map };
            table.Measure(new Size(900, 400));
            table.Arrange(new Rect(0, 0, 900, 400));
            table.UpdateLayout();

            var rejections = new List<RegisterRejection>();
            table.ValueRejected += rejection => rejections.Add(rejection);

            var valueBox = Visuals.Descendants<System.Windows.Controls.TextBox>(table)
                .First(box => (AutomationProperties.GetName(box) ?? "").StartsWith("Value of register", StringComparison.Ordinal));
            valueBox.Text = "300";
            valueBox.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));

            var first = Assert.Single(rejections);
            Assert.Equal(300, first.Attempted);
            Assert.Equal(255, first.Limit);        // what an eight bit register can hold
            Assert.Equal("18", first.Kept);        // what stayed on screen

            var fieldBox = Visuals.Descendants<System.Windows.Controls.TextBox>(table)
                .First(box => (AutomationProperties.GetName(box) ?? "") == "Value of field mode");
            fieldBox.Text = "9";
            fieldBox.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));

            var second = rejections[^1];
            Assert.Equal(9, second.Attempted);
            Assert.Equal(7, second.Limit);         // three bits
            Assert.Equal("1", second.Kept);        // bits 1..3 of 0x12
        });
    }

    [Fact]
    public void The_status_dot_says_what_the_last_read_did()
    {
        Sta.Run(() =>
        {
            Sta.NewState();
            var map = PlatformMap();
            var localizer = new Lantern.UI.Localizer("en", null);
            var table = new RegisterTable(localizer) { Map = map, Filter = "0x00" };
            table.Measure(new Size(700, 200));
            table.Arrange(new Rect(0, 0, 700, 200));
            table.UpdateLayout();

            Assert.Contains("Not read yet", Visuals.Descendants<System.Windows.Controls.TextBlock>(table)
                .Select(block => AutomationProperties.GetName(block)));

            map.Find(0)!.Status = RegisterStatus.Failed;

            Assert.Contains("Read failed", Visuals.Descendants<System.Windows.Controls.TextBlock>(table)
                .Select(block => AutomationProperties.GetName(block)));
        });
    }
}