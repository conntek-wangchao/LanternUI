using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Lantern.UI;

namespace LanternUI.Demo.Pages;

/// <summary>
/// One table, one file: the page opens a register map — the platform's own, a 16-bit sample, a 32-bit
/// sample, or any file the user picks — and the table shows it. There is no device in the demo, so
/// "read" and "write" only mark the map.
/// </summary>
public sealed class RegisterPage : PageBase
{
    private readonly RegisterTable _table;
    private TextBlock _mapHint = null!;
    private readonly TextBlock _file;
    private readonly Dictionary<int, int> _device = new();
    private RegisterMap _map = new();

    public RegisterPage(IAppState app, IShell shell, ILocalizer loc)
        : base(app, loc, loc.T("Registers"),
            loc.T("One row per register, one cell per bit, the field names under the cells they own."), shell.Layout)
    {
        _table = new RegisterTable(loc)
        {
            ValueBase = S.Str("register_base") == "hex" ? RegisterValueBase.Hexadecimal : RegisterValueBase.Decimal,
        };
        // The demo has no hardware, so it keeps the device's side itself: write copies a row into
        // this map, read copies it back. Reading used to invent a value, which made every read look
        // like the table had changed the register on its own.
        _table.ShowReadButtons = true;
        _table.ShowWriteButtons = true;
        _table.ReadRequested += ReadOne;
        _table.WriteRequested += WriteOne;

        _table.ValueEdited += entry =>
        {
            // Edited and not written yet: the status dot stays neutral until the device has it.
            entry.Status = RegisterStatus.Unknown;
            App.Log(loc.F($"Register {entry.Address} is now {entry.ValueText}, not written yet."), LogLevel.Info);
        };

        Group(loc.T("Map"));
        ComboRow(loc.T("Register width"), loc.T("A map of bytes can be regrouped; registers that are already 16 or 32 bits keep their width."),
            new[]
            {
                new Choice(loc.T("8 bits"), "8"),
                new Choice(loc.T("16 bits"), "16"),
                new Choice(loc.T("32 bits"), "32"),
            }, () => S.Str("register_width"), value =>
            {
                S.Set("register_width", value);
                _table.ViewWidth = int.Parse(value, CultureInfo.InvariantCulture);
            }, glyph: Glyphs.Settings);

        ComboRow(loc.T("Number base"), loc.T("One switch for every number in the table."),
            new[]
            {
                new Choice(loc.T("Decimal"), "dec"),
                new Choice(loc.T("Hexadecimal"), "hex"),
            }, () => S.Str("register_base"), value =>
            {
                S.Set("register_base", value);
                _table.ValueBase = value == "hex" ? RegisterValueBase.Hexadecimal : RegisterValueBase.Decimal;
            }, glyph: Glyphs.Edit);

        ToggleRow(loc.T("Group registers"), loc.T("One heading per block of sixteen addresses; click a heading to fold it."),
            () => S.Str("register_grouped") != "false", value =>
            {
                S.Set("register_grouped", value ? "true" : "false");
                _table.Grouped = value;
            }, glyph: Glyphs.Folder);
        _table.Grouped = S.Str("register_grouped") != "false";

        var filter = TextRow(loc.T("Filter"), loc.T("An address, a register name, a field name or a value."),
            loc.T("Type to narrow the map"), () => S.Str("register_filter"), value => _table.Filter = value,
            glyph: Glyphs.Search);
        filter.Text = S.Str("register_filter");

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(Button(loc.T("Browse..."), Browse));
        buttons.Children.Add(Button(loc.T("Platform sample"), () => Load(Bundled("helan-registers.json"), true)));
        buttons.Children.Add(Button(loc.T("16-bit sample"), () => Load(Bundled("registers-16.json"), false)));
        buttons.Children.Add(Button(loc.T("32-bit sample"), () => Load(Bundled("registers-32.json"), false)));

        Add(Card(loc.T("Map file"), loc.T("The file the table shows; open another one to switch maps."), buttons,
            out var hint, wide: true, glyph: Glyphs.Folder));
        _file = hint;
        SetHint(_file, "");

        RegisterTableRow(loc.T("Register map"), loc.T("Click a bit to toggle it; the value box takes hex or decimal."),
            _table, out var mapHint, Glyphs.Settings);
        _mapHint = mapHint;

        _table.ValueRejected += rejection =>
        {
            // The same line a clamped slider writes, and the same rule: a note, not a log entry.
            SetHint(_mapHint, RangeHint(loc, rejection.Kept, "0",
                rejection.Limit.ToString(CultureInfo.InvariantCulture)));
        };

        // The next commit takes the note away, which is what leaving the box does.
        _table.ValueAccepted += () => SetHint(_mapHint, null);

        Group(loc.T("Device"));
        var device = new StackPanel { Orientation = Orientation.Horizontal };
        device.Children.Add(Button(loc.T("Read all"), () => Visit(true)));
        device.Children.Add(Button(loc.T("Write all"), () => Visit(false)));
        device.Children.Add(Button(loc.T("Reset all"), Reset));
        Add(Card(loc.T("Simulated device"), loc.T("The demo has no hardware: these only mark the map."), device,
            out _, wide: true, glyph: Glyphs.Speed));

        var saved = S.Str("register_file");
        Load(saved.Length > 0 && File.Exists(saved) ? saved : Bundled("helan-registers.json"), saved.Length == 0);
    }

    private static string Bundled(string name) => Path.Combine(AppContext.BaseDirectory, "Assets", name);

    private Button Button(string text, Action click)
    {
        var button = new Button { Content = text, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 8, 8, 0) };
        button.SetResourceReference(StyleProperty, "GhostButton");
        button.Click += (_, _) => click();
        return button;
    }

    private void Browse()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "JSON (*.json)|*.json|All files (*.*)|*.*",
            Title = L.T("Open a register map"),
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() == true) Load(dialog.FileName, false);
    }

    /// <summary>Reads a map and puts it in the table; a file that cannot be read leaves the old one alone.</summary>
    private void Load(string path, bool withGeneratedBlock)
    {
        try
        {
            var map = RegisterMap.Load(File.ReadAllText(path));
            if (withGeneratedBlock) Generate(map);

            _map = map;
            _table.Map = map;
            _table.ViewWidth = map.IsByteMap
                ? (int.TryParse(S.Str("register_width"), out var bits) ? bits : 8)
                : 8;

            _device.Clear();
            foreach (var entry in map.Registers) _device[entry.Address] = entry.Value;

            S.Set("register_file", Same(path, Bundled("helan-registers.json")) ? "" : path);
            App.SaveSoon();
            SetHint(_file, L.F($"{map.Registers.Count} registers, {map.Width} bits") + "   ·   " + Path.GetFileName(path));
            App.Log(L.F($"Map {Path.GetFileName(path)}: {map.Registers.Count} registers."), LogLevel.Good);
        }
        catch (Exception e) when (e is IOException or FormatException or JsonException or UnauthorizedAccessException)
        {
            SetHint(_file, L.F($"Could not read this map: {e.Message}"));
            App.Log(L.F($"Could not read this map: {e.Message}"), LogLevel.Bad);
        }
    }

    private static bool Same(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    /// <summary>The platform file is small, so the demo pads it out to show that a table can be long.</summary>
    private static void Generate(RegisterMap map)
    {
        for (var address = 0x08; address < 0x200; address++)
        {
            var entry = new RegisterEntry(address, (address * 37) & 0xFF, 8, "Generated block " + address / 8 + "");
            entry.AddField("en", 0, 1);
            entry.AddField("mode", 1, 3);
            entry.AddField("gain", 4, 2);
            entry.AddField("irq", 6, 2);
            map.Add(entry);
        }
    }

    /// <summary>Takes the value the device holds into the row.</summary>
    private void ReadOne(RegisterEntry entry)
    {
        entry.SetFromDevice(_device.TryGetValue(entry.Address, out var held) ? held : entry.ResetValue);
        entry.Status = RegisterStatus.Ok;
        entry.Commit();
        App.Log(L.F($"Read register {entry.Address}: {entry.ValueText}."), LogLevel.Good);
    }

    /// <summary>Stores what the row shows into the device.</summary>
    private void WriteOne(RegisterEntry entry)
    {
        _device[entry.Address] = entry.Value;
        entry.Status = RegisterStatus.Ok;
        entry.Commit();
        App.Log(L.F($"Write register {entry.Address}: {entry.ValueText}."), LogLevel.Good);
    }

    private void Visit(bool read)
    {
        foreach (var entry in _map.Registers)
        {
            if (read) ReadOne(entry);
            else WriteOne(entry);
        }

        App.Log(read
            ? L.F($"Read {_map.Registers.Count} registers.")
            : L.F($"Wrote {_map.Registers.Count} registers."), LogLevel.Good);
    }

    private void Reset()
    {
        foreach (var entry in _map.Registers)
        {
            _device[entry.Address] = entry.ResetValue;
            ReadOne(entry);
        }

        App.Log(L.F($"Reset {_map.Registers.Count} registers."), LogLevel.Info);
    }
}