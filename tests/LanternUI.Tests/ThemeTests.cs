using System.IO;
using System.Windows;
using System.Windows.Media;
using Lantern.UI;
using Xunit;

namespace LanternUI.Tests;

public class ThemeTests
{
    [Fact]
    public void High_contrast_swaps_the_palette_and_drops_the_accent()
    {
        Sta.Run(() =>
        {
            try
            {
                ThemeBoot.ApplyPalette(false);
                var dark = Brush("WindowBaseBrush");
                AccentTheme.Apply(Colors.Crimson);
                Assert.Equal(Colors.Crimson, Brush("AccentBrush"));   // the overlay wins, as designed

                ThemeBoot.ApplyPalette(true);
                Assert.True(ThemeBoot.HighContrast);
                Assert.NotEqual(dark, Brush("WindowBaseBrush"));

                // In high contrast the system's highlight colour is the accent: an app accent is ignored.
                AccentTheme.Apply(Colors.Crimson);
                Assert.NotEqual(Colors.Crimson, Brush("AccentBrush"));
            }
            finally
            {
                ThemeBoot.ApplyPalette(false);
                AccentTheme.Apply(null);
            }
        });
    }

    private static Color Brush(string key) =>
        ((SolidColorBrush)Application.Current.Resources[key]).Color;

    [Theory]
    [InlineData("TextPrimaryBrush", "WindowTextColorKey")]
    [InlineData("TextSecondaryBrush", "WindowTextColorKey")]
    [InlineData("TextTertiaryBrush", "GrayTextColorKey")]
    [InlineData("AccentTextBrush", "WindowTextColorKey")]   // a hint sits on the page, not on the accent
    public void High_contrast_text_tokens_are_readable_system_text_colours(string token, string expected)
    {
        var path = Path.Combine(TestPaths.RepoRoot(), "src", "LanternUI", "Theming", "Palette.HighContrast.xaml");
        var text = File.ReadAllText(path);
        var match = System.Text.RegularExpressions.Regex.Match(text,
            "x:Key=\"" + token + "\"[^>]*SystemColors\\.(\\w+)");

        Assert.True(match.Success, token + " is missing from the high-contrast palette");
        Assert.Equal(expected, match.Groups[1].Value);
    }

}