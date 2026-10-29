namespace Lantern.UI;

/// <summary>
/// Segoe Fluent Icons code points for the few glyphs the rows use. Windows ships the font, so the
/// framework needs no icon package; a missing code point draws a box, which is easy to spot.
/// </summary>
public static class Glyphs
{
    public const string Font = "Segoe Fluent Icons";

    public const string Settings = "\uE713";
    public const string Color = "\uE790";
    public const string Globe = "\uE774";
    public const string Navigation = "\uE700";
    public const string Monitor = "\uE7F4";
    public const string Gamepad = "\uE7FC";
    public const string Volume = "\uE767";
    public const string Info = "\uE946";
    public const string Folder = "\uE8B7";
    public const string Photo = "\uE91B";
    public const string Lightbulb = "\uEA80";
    public const string Search = "\uE721";
    public const string Edit = "\uE70F";
    public const string Lab = "\uE9D9";
    public const string Keyboard = "\uE765";
    public const string Save = "\uE74E";
    public const string Speed = "\uEC4A";
    public const string Chart = "\uEB05";
    public const string Read = "\uE72C";

    /// <summary>The icon column of a row: a glyph from the Fluent icon font, not a package.</summary>
    public static System.Windows.Controls.TextBlock Icon(string glyph, double size) => new()
    {
        Text = glyph,
        FontFamily = new System.Windows.Media.FontFamily(Font),
        FontSize = size,
        VerticalAlignment = System.Windows.VerticalAlignment.Center,
        TextAlignment = System.Windows.TextAlignment.Center,
        Width = size + 2,
    };
}