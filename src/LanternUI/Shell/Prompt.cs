using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Lantern.UI;

/// <summary>
/// A small modal dialog in the framework's own style: a title, a message, optional text fields and
/// one to three buttons. It is a borderless window of its own (a real modal, with its own focus),
/// centred on the owner. Set <see cref="SnapshotPath"/> to render it off-screen to a PNG instead of
/// waiting for an answer — the screenshot path the reference uses for its dialogs.
/// </summary>
public sealed class Prompt : Window
{
    public sealed record Field(string Label, string Value = "", string Placeholder = "", bool Multiline = false);

    /// <summary>When set, the dialog is drawn to this PNG, off screen, and closed at once.</summary>
    public static string? SnapshotPath;

    /// <summary>The default button labels ("OK" / "Cancel") come from here; the app sets it once.</summary>
    public static ILocalizer Localizer { get; set; } = NullLocalizer.Instance;

    private readonly List<TextBox> _boxes = new();
    private int _chosen = -1;

    private Prompt(string title, string? message, IReadOnlyList<Field> fields,
        string ok, string? cancel, IReadOnlyList<string>? choices)   // "" means the localized default
    {
        var owner = Application.Current.MainWindow is { IsLoaded: true } main && main.WindowState != WindowState.Minimized
            ? main
            : null;
        Owner = owner;

        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        TextOptions.SetTextHintingMode(this, TextHintingMode.Fixed);
        UseLayoutRounding = true;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        MaxWidth = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var stack = new StackPanel { Width = 420 };
        var heading = new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        stack.Children.Add(heading);

        if (!string.IsNullOrEmpty(message))
        {
            var text = new TextBlock
            {
                Text = message,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0),
            };
            text.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            stack.Children.Add(new ScrollViewer
            {
                Content = text,
                MaxHeight = 280,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            });
        }

        foreach (var field in fields)
        {
            var label = new TextBlock { Text = field.Label, FontSize = 12, Margin = new Thickness(0, 12, 0, 4) };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");
            stack.Children.Add(label);

            var box = new TextBox
            {
                Text = field.Value,
                ToolTip = field.Placeholder,
                AcceptsReturn = field.Multiline,
                TextWrapping = field.Multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
                MinHeight = field.Multiline ? 96 : 0,
                VerticalScrollBarVisibility = field.Multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden,
            };
            box.SetResourceReference(StyleProperty, "Field");
            _boxes.Add(box);
            stack.Children.Add(box);
        }

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 20, 0, 0),
        };

        if (choices is { Count: > 0 })
        {
            for (var i = 0; i < choices.Count; i++)
            {
                var index = i;
                var button = MakeButton(choices[i], i == 0 ? "AccentButton" : "GhostButton");
                button.Click += (_, _) => Answer(index);
                button.Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0);
                buttons.Children.Add(button);
            }
        }
        else
        {
            if (cancel != null)
            {
                var no = MakeButton(cancel.Length == 0 ? Localizer.T("Cancel") : cancel, "GhostButton");
                no.Click += (_, _) => Answer(-1);
                no.Margin = new Thickness(0, 0, 8, 0);
                buttons.Children.Add(no);
            }
            var yes = MakeButton(ok.Length == 0 ? Localizer.T("OK") : ok, "AccentButton");
            yes.Click += (_, _) => Answer(0);
            buttons.Children.Add(yes);
        }

        stack.Children.Add(buttons);

        var card = new Border
        {
            Child = stack,
            Padding = new Thickness(22, 20, 22, 20),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 0, Opacity = 0.5 },
        };
        card.SetResourceReference(BackgroundProperty, "CardBackgroundBrush");
        card.SetResourceReference(BorderBrushProperty, "CardStrokeBrush");

        Content = card;
        PreviewKeyDown += OnKey;
    }

    private static Button MakeButton(string text, string style)
    {
        var button = new Button { Content = text, Padding = new Thickness(16, 5, 16, 5), FontSize = 12.5, Focusable = true };
        button.SetResourceReference(StyleProperty, style);
        return button;
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Answer(-1);
                e.Handled = true;
                break;
            case Key.Enter when !(_boxes.Any(box => box.IsKeyboardFocusWithin && box.AcceptsReturn)):
                Answer(0);
                e.Handled = true;
                break;
        }
    }

    private void Answer(int index)
    {
        _chosen = index;
        Hide();
    }

    // ---------------------------------------------------------------- API

    public static void Tell(string title, string message) => Run(title, message, null, "", null, null);

    public static bool Confirm(string title, string message, string ok = "", string cancel = "") =>
        Run(title, message, null, ok, cancel ?? "", null) == 0;

    /// <summary>One or more text fields; null when cancelled.</summary>
    public static string[]? Ask(string title, string? message, params Field[] fields)
    {
        var result = Run(title, message, fields, "", "", null);
        if (result != 0) return null;
        return _last!.Select(box => box.Text).ToArray();
    }

    /// <summary>Up to three buttons: the index pressed, -1 when closed with Escape.</summary>
    public static int Choose(string title, string? message, params string[] choices) =>
        Run(title, message, null, choices.FirstOrDefault() ?? "", "", choices);

    private static List<TextBox>? _last;

    private static int Run(string title, string? message, IReadOnlyList<Field>? fields,
        string ok, string? cancel, IReadOnlyList<string>? choices)
    {
        var prompt = new Prompt(title, message, fields ?? Array.Empty<Field>(), ok, cancel, choices);
        _last = prompt._boxes;

        if (SnapshotPath is { Length: > 0 } path)
        {
            prompt.WindowStartupLocation = WindowStartupLocation.Manual;
            prompt.ShowActivated = false;
            prompt.ShowInTaskbar = false;
            prompt.Left = -32000;
            prompt.Top = -32000;
            prompt.Show();
            prompt.UpdateLayout();
            Shot(prompt, path);
            prompt.Close();
            return -1;
        }

        prompt.ShowDialog();
        return prompt._chosen;
    }

    private static void Shot(Window window, string path)
    {
        var width = (int)Math.Ceiling(window.ActualWidth);
        var height = (int)Math.Ceiling(window.ActualHeight);
        if (width <= 0 || height <= 0) return;

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        using var stream = File.Create(full);
        encoder.Save(stream);
    }
}
