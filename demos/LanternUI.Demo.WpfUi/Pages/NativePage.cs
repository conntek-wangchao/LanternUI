using System.Windows;
using System.Windows.Controls;
using Lantern.UI;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace LanternUI.Demo.WpfUi.Pages;

/// <summary>
/// WPF-UI controls living inside a Lantern page: they are themed by the bridge dictionary, so a
/// third-party control set and the framework's own rows share one palette and one accent.
/// </summary>
public sealed class NativePage : PageBase
{
    public NativePage(IAppState app, IShell shell)
        : base(app, null, "WPF-UI controls",
            "A third-party set on the same tokens: the bridge dictionary re-points WPF-UI's brushes at the Lantern palette.",
            shell.Layout)
    {
        Group("WPF-UI");

        var button = new Button { Content = "WPF-UI button", Appearance = ControlAppearance.Primary, Padding = new Thickness(14, 5, 14, 5) };
        Add(Card("Button", "WPF-UI's own template, coloured by the bridge.", button, out _));

        var toggle = new ToggleSwitch { IsChecked = true };
        Add(Card("ToggleSwitch", "Its track takes AccentFillColorDefaultBrush from the bridge.", toggle, out _));

        var icon = new StackPanel { Orientation = Orientation.Horizontal };
        icon.Children.Add(new SymbolIcon(SymbolRegular.Settings24) { FontSize = 18, Margin = new Thickness(0, 0, 8, 0) });
        var caption = new TextBlock { Text = "SymbolIcon (Fluent System Icons)", VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5 };
        icon.Children.Add(caption);
        Add(Card("Icon", "The icon font the reference gets from WPF-UI, now available here.", icon, out _));

        var box = new Wpf.Ui.Controls.TextBox { PlaceholderText = "WPF-UI text box", Width = 220 };
        Add(Card("TextBox", "Placeholder, caret and selection from the same palette.", box, out _));

        Group("Lantern rows (for comparison)");
        ComboRow("Framework row", "The framework's own row, next to WPF-UI's controls.", new[]
        {
            new Choice("Alpha", "a"), new Choice("Beta", "b"), new Choice("Gamma", "c"),
        }, () => "a", _ => { });
        ToggleRow("Framework toggle", "Hand-built from tokens: same accent, same surfaces.", () => true, _ => { });
    }
}
