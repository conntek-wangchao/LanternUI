using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Lantern.UI;

/// <summary>What the last read or write of a register did.</summary>
public enum RegisterStatus { Unknown, Ok, Failed }

/// <summary>
/// A typed number that was wider than the register or the field could hold: what was typed, the
/// largest value allowed, and the text that stayed on screen.
/// </summary>
public sealed record RegisterRejection(RegisterEntry Entry, int Attempted, int Limit, string Kept);

/// <summary>
/// Where a register's value lives. A plain register keeps it in itself; a 16- or 32-bit view of a
/// byte map composes it out of the bytes at the addresses below, little endian, so editing a word
/// still edits the bytes the device knows.
/// </summary>
public interface IRegisterBacking
{
    int Read();

    void Write(int value);
}

internal sealed class DirectBacking : IRegisterBacking
{
    public DirectBacking(int value) => Value = value;

    public int Value { get; set; }

    public int Read() => Value;

    public void Write(int value) => Value = value;
}

internal sealed class WordBacking : IRegisterBacking
{
    private readonly RegisterEntry?[] _bytes;

    public WordBacking(RegisterEntry?[] bytes) => _bytes = bytes;

    public int Read()
    {
        var value = 0;
        for (var index = 0; index < _bytes.Length; index++)
            if (_bytes[index] is { } entry) value |= (entry.Value & 0xFF) << (8 * index);
        return value;
    }

    public void Write(int value)
    {
        for (var index = 0; index < _bytes.Length; index++)
            if (_bytes[index] is { } entry) entry.Value = (value >> (8 * index)) & 0xFF;
    }
}

/// <summary>
/// One named slice of a register: the field starts at Bias and is Width bits wide. Two fields of the
/// same register never overlap, which is what makes the bit strip above them readable.
/// </summary>
public sealed class RegisterField
{
    public RegisterField(string name, int bias, int width, bool isReadOnly = false)
    {
        Name = name;
        Bias = bias;
        Width = width;
        IsReadOnly = isReadOnly;
    }

    public string Name { get; }

    public int Bias { get; }

    public int Width { get; }

    public bool IsReadOnly { get; set; }

    /// <summary>The field's bits, already shifted down to zero.</summary>
    public int Mask => Width >= 32 ? unchecked((int)0xFFFFFFFF) : (1 << Width) - 1;

    /// <summary>True when the field is really just one bit, which is the only case a click can toggle.</summary>
    public bool IsSingleBit => Width == 1;

    public int Read(int registerValue) => (int)((uint)registerValue >> Bias) & Mask;

    public int Compose(int registerValue, int fieldValue)
    {
        var shift = Mask << Bias;
        return (registerValue & ~shift) | ((fieldValue & Mask) << Bias);
    }

    public override string ToString() => Name + "[" + Bias + "+" + Width + "]";
}

/// <summary>
/// One register of the map: an address, a value, and the fields that live inside it. Reading and
/// writing the value keeps the fields in step, so a view never has to recompute anything.
/// </summary>
public sealed class RegisterEntry : INotifyPropertyChanged
{
    private int _value;
    private int _resetValue;
    private bool _isReadOnly;
    private RegisterStatus _status;
    private int _committed;

    private readonly IRegisterBacking _backing;

    public RegisterEntry(int address, int value, int width = 8, string name = "")
        : this(address, width, new DirectBacking(value), value, name)
    {
    }

    internal RegisterEntry(int address, int width, IRegisterBacking backing, int resetValue, string name)
    {
        Address = address;
        Width = Math.Clamp(width, 1, 32);
        _backing = backing;
        _value = Clamp(backing.Read());
        _resetValue = Clamp(resetValue);
        _committed = _value;
        Name = name;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Address { get; }

    /// <summary>Register width in bits: eight for a byte-wide map, sixteen or thirty-two for wider ones.</summary>
    public int Width { get; }

    /// <summary>Optional human name ("power control"); the reference map has none, so it may be empty.</summary>
    public string Name { get; set; }

    /// <summary>The group the register belongs to; empty means "group by address block".</summary>
    public string Group { get; set; } = "";

    public IReadOnlyList<RegisterField> Fields => _fields;

    private readonly List<RegisterField> _fields = new();

    /// <summary>
    /// The register's value. Writing it leaves the bits of read-only fields alone, so a locked field
    /// cannot be changed by typing the whole byte; a device read uses SetFromDevice instead.
    /// </summary>
    public int Value
    {
        get => Clamp(_backing.Read());
        set => Write(value, respectLocks: true);
    }

    public int ResetValue
    {
        get => _resetValue;
        set { _resetValue = Clamp(value); Changed(nameof(ResetValue)); }
    }

    public bool IsReadOnly
    {
        get => _isReadOnly;
        set { _isReadOnly = value; Changed(nameof(IsReadOnly)); }
    }

    public RegisterStatus Status
    {
        get => _status;
        set { _status = value; Changed(nameof(Status)); }
    }

    /// <summary>The value as the row shows it: hex, because a register is a number of bits, not a quantity.</summary>
    public string ValueText => "0x" + Value.ToString(Width <= 8 ? "X2" : Width <= 16 ? "X4" : "X8", CultureInfo.InvariantCulture);

    /// <summary>Value differs from the last value read from (or written to) the device.</summary>
    public bool IsDirty => Value != _committed;

    /// <summary>Bit i of the value, used by the strip; set through SetBit.</summary>
    public bool this[int bit] => bit >= 0 && bit < Width && ((Value >> bit) & 1) != 0;

    /// <summary>The bits in order, bit zero first, for a strip that rebuilds on a value change.</summary>
    public IReadOnlyList<bool> Bits
    {
        get
        {
            var bits = new bool[Width];
            for (var bit = 0; bit < Width; bit++) bits[bit] = this[bit];
            return bits;
        }
    }

    /// <summary>Raised whenever the value changes, so an undo log or a device write can follow it.</summary>
    public event Action<RegisterEntry>? ValueChanged;

    public RegisterEntry AddField(string name, int bias, int width, bool isReadOnly = false)
    {
        var field = new RegisterField(name, bias, width, isReadOnly);
        if (bias < 0 || width < 1 || bias + width > Width)
            throw new ArgumentOutOfRangeException(nameof(bias), "the field does not fit in the register");
        foreach (var existing in _fields)
            if (bias < existing.Bias + existing.Width && existing.Bias < bias + width)
                throw new ArgumentException("the field overlaps " + existing.Name, nameof(bias));

        _fields.Add(field);
        _fields.Sort((left, right) => left.Bias.CompareTo(right.Bias));
        Changed(nameof(Fields));
        Changed(field, nameof(RegisterField));
        return this;
    }

    public RegisterField? Field(string name) => _fields.FirstOrDefault(field => field.Name == name);

    public void SetBit(int bit, bool on)
    {
        if (IsReadOnly || bit < 0 || bit >= Width) return;

        var field = FieldAt(bit);
        if (field is { IsReadOnly: true }) return;

        var next = on ? Value | (1 << bit) : Value & ~(1 << bit);
        if (Clamp(next) == Value) return;

        _backing.Write(Clamp(next));
        NotifyValue();
    }

    public void ToggleBit(int bit) => SetBit(bit, !this[bit]);

    /// <summary>Takes a value the device reported: read-only fields are part of the news, so nothing is masked.</summary>
    public void SetFromDevice(int value) => Write(value, respectLocks: false);

    /// <summary>Bits that belong to a read-only field, already in place.</summary>
    public int LockedMask
    {
        get
        {
            var mask = 0;
            foreach (var slice in _fields)
                if (slice.IsReadOnly) mask |= slice.Mask << slice.Bias;
            return mask;
        }
    }

    private void Write(int value, bool respectLocks)
    {
        var next = Clamp(value);
        if (respectLocks)
        {
            if (IsReadOnly) return;
            var locked = LockedMask;
            next = (next & ~locked) | (Clamp(_backing.Read()) & locked);
        }

        if (next == Clamp(_backing.Read())) return;
        _backing.Write(next);
        NotifyValue();
    }

    /// <summary>The field that owns a bit, or null for the gaps a map leaves unnamed.</summary>
    public RegisterField? FieldAt(int bit) =>
        _fields.FirstOrDefault(field => bit >= field.Bias && bit < field.Bias + field.Width);

    /// <summary>Remembers the current value as the one the device has: clears IsDirty.</summary>
    public void Commit() => _committed = Value;

    /// <summary>One place raises the change for a value, so a backing that maps onto other
    /// registers does not have to know how the notifications work.</summary>
    private void NotifyValue()
    {
        _value = Value;
        Changed(nameof(Value));
        Changed(nameof(ValueText));
        Changed(nameof(IsDirty));
        Changed(nameof(Bits));
        foreach (var slice in _fields) Changed(slice, nameof(RegisterField));
        ValueChanged?.Invoke(this);
    }

    /// <summary>Called by a word view when a byte underneath it changed.</summary>
    internal void NotifyBackedChange() => NotifyValue();

    public void Reset() => Value = _resetValue;

    private int Clamp(int value)
    {
        if (Width >= 32) return value;
        var max = (1 << Width) - 1;
        return value < 0 ? 0 : value > max ? max : value;
    }

    private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private void Changed(RegisterField field, string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name + ":" + field.Bias));

    public override string ToString() => "0x" + Address.ToString("X2", CultureInfo.InvariantCulture) + " " + ValueText;
}

/// <summary>
/// A register map: loaded from the JSON the test platform already uses, edited in the table, saved
/// back in the same shape. Nothing here talks to hardware — a host subscribes to ValueChanged.
/// </summary>
public sealed class RegisterMap
{
    private readonly List<RegisterEntry> _registers = new();

    public IReadOnlyList<RegisterEntry> Registers => _registers;

    /// <summary>True when every register is eight bits wide, which is what the word view can group.</summary>
    public bool IsByteMap => _registers.Count > 0 && _registers.All(entry => entry.Width == 8);

    /// <summary>The widest register in the map: 8, 16 or 32.</summary>
    public int Width => _registers.Count == 0 ? 8 : _registers.Max(entry => entry.Width);

    public event Action<RegisterEntry>? Changed;

    /// <summary>Built once, then kept in address order so the table never shuffles on a reload.</summary>
    public RegisterMap Add(RegisterEntry entry)
    {
        _registers.Add(entry);
        _registers.Sort((left, right) => left.Address.CompareTo(right.Address));
        entry.ValueChanged += OnEntryChanged;
        return this;
    }

    private void OnEntryChanged(RegisterEntry entry) => Changed?.Invoke(entry);

    public RegisterEntry? Find(int address) => _registers.FirstOrDefault(entry => entry.Address == address);

    /// <summary>
    /// Reads the platform's config: an object keyed by address, each with Address, ResetValue, Value,
    /// IsReadOnly and RegisterBitsList of {Name, BitBias, BitNum, IsReadOnly}. Unknown members are ignored.
    /// </summary>
    public static RegisterMap Load(string json)
    {
        var map = new RegisterMap();
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new FormatException("a register map is an object keyed by address");

        foreach (var property in document.RootElement.EnumerateObject())
        {
            var register = property.Value;
            if (register.ValueKind != JsonValueKind.Object) continue;

            var address = Int(register, "Address") ?? (int.TryParse(property.Name, out var parsed) ? parsed : -1);
            if (address < 0) continue;

            var width = Int(register, "Width") switch { 16 => 16, 32 => 32, _ => 8 };
            var entry = new RegisterEntry(address, Int(register, "Value") ?? Int(register, "ResetValue") ?? 0, width,
                Text(register, "Name") ?? "")
            {
                Group = Text(register, "Group") ?? "",
            ResetValue = Int(register, "ResetValue") ?? 0,
                IsReadOnly = Bool(register, "IsReadOnly") ?? false,
            };

            if (register.TryGetProperty("RegisterBitsList", out var bits) && bits.ValueKind == JsonValueKind.Array)
            {
                foreach (var bit in bits.EnumerateArray())
                {
                    var name = Text(bit, "Name") ?? "";
                    var bias = Int(bit, "BitBias") ?? 0;
                    var count = Int(bit, "BitNum") ?? 1;
                    if (bias + count > width) continue;
                    try
                    {
                        entry.AddField(name, bias, count, Bool(bit, "IsReadOnly") ?? false);
                    }
                    catch (ArgumentException)
                    {
                        // Overlapping fields in a hand-written file: keep the first, drop the rest.
                    }
                }
            }

            map.Add(entry);
        }

        return map;
    }

    /// <summary>Writes the same shape the platform reads, so a round trip loses nothing it understands.</summary>
    public string Save()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            foreach (var entry in _registers)
            {
                writer.WriteStartObject(entry.Address.ToString(CultureInfo.InvariantCulture));
                writer.WriteNumber("Address", entry.Address);
                if (entry.Name.Length > 0) writer.WriteString("Name", entry.Name);
            if (entry.Group.Length > 0) writer.WriteString("Group", entry.Group);
                writer.WriteNumber("Width", entry.Width);
                writer.WriteNumber("ResetValue", entry.ResetValue);
                writer.WriteNumber("Value", entry.Value);
                writer.WriteBoolean("IsReadOnly", entry.IsReadOnly);
                writer.WriteStartArray("RegisterBitsList");
                foreach (var field in entry.Fields)
                {
                    writer.WriteStartObject();
                    writer.WriteString("Name", field.Name);
                    writer.WriteNumber("Address", entry.Address);
                    writer.WriteNumber("BitBias", field.Bias);
                    writer.WriteNumber("BitNum", field.Width);
                    writer.WriteNumber("Value", field.Read(entry.Value));
                    writer.WriteBoolean("IsReadOnly", field.IsReadOnly);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// The same map seen as wider registers: 16-bit words pair the bytes at even addresses, 32-bit
    /// words quad them, little endian — the low byte sits at the lowest address. The bytes stay the
    /// source of truth: a value written here is written back to them, and a field keeps its position
    /// inside the word (byte 1 starts at bit 8).
    /// </summary>
    public RegisterMap AsWords(int width)
    {
        if (width is not (16 or 32) || !IsByteMap) return this;

        var bytes = width / 8;
        var highest = _registers.Count == 0 ? -1 : _registers.Max(entry => entry.Address);
        var view = new RegisterMap();

        for (var start = 0; start <= highest; start += bytes)
        {
            var group = new RegisterEntry?[bytes];
            for (var index = 0; index < bytes; index++) group[index] = Find(start + index);
            if (group.All(entry => entry == null)) continue;

            var word = new RegisterEntry(start, width, new WordBacking(group),
                ResetOf(group), NameOf(group))
            {
                IsReadOnly = group.Where(entry => entry != null).All(entry => entry!.IsReadOnly),
            };

            for (var index = 0; index < bytes; index++)
            {
                if (group[index] is not { } byteEntry) continue;
                foreach (var field in byteEntry.Fields)
                {
                    try
                    {
                        word.AddField(field.Name, field.Bias + 8 * index, field.Width, field.IsReadOnly);
                    }
                    catch (ArgumentException)
                    {
                        // A field of one byte that would overlap a field of the next: keep the first.
                    }
                }
            }

            var captured = word;
            foreach (var byteEntry in group)
                if (byteEntry != null)
                    byteEntry.ValueChanged += _ => captured.NotifyBackedChange();

            view.Add(word);
        }

        view.Changed += entry => Changed?.Invoke(entry);
        return view;
    }

    private static int ResetOf(IReadOnlyList<RegisterEntry?> group)
    {
        var value = 0;
        for (var index = 0; index < group.Count; index++)
            if (group[index] is { } entry) value |= (entry.ResetValue & 0xFF) << (8 * index);
        return value;
    }

    private static string NameOf(IReadOnlyList<RegisterEntry?> group)
    {
        var names = group.Where(entry => entry != null && entry.Name.Length > 0).Select(entry => entry!.Name).Distinct().ToList();
        return names.Count == 1 ? names[0] : "";
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var parsed) ? parsed : null;

    private static bool? Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            }
            : null;
}