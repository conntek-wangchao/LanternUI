using System.Diagnostics;
using System.IO;
using System.Windows;
using Lantern.UI;

namespace LanternUI.Demo.Pages;

public sealed class AppearancePage : PageBase
{
    private static readonly Choice[] Accents =
    {
        new("Default", "default"),
        new("Crimson", "crimson"),
        new("Moonlight", "moon"),
        new("Amber", "amber"),
    };

    private static readonly Choice[] ButtonFamilies =
    {
        new("macOS", "mac"),
        new("Windows", "windows"),
    };

    private static readonly Choice[] Sides =
    {
        new("Usual", "usual"),
        new("Left", "left"),
        new("Right", "right"),
    };

    private static readonly Choice[] Backgrounds =
    {
        new("Picture", "custom"),
        new("None", "none"),
    };

    private static readonly Choice[] Blurs =
    {
        new("Strong", "strong"),
        new("Light", "light"),
        new("Off", "off"),
    };

    private static readonly Choice[] Resolutions =
    {
        new("1280x720", "1280x720"),
        new("1920x1080", "1920x1080"),
        new("2560x1440", "2560x1440"),
        new("3840x2160", "3840x2160"),
    };

    private static readonly Choice[] FrameLimits =
    {
        new("30", "30"),
        new("60", "60"),
        new("120", "120"),
        new("144", "144"),
        new("Unlimited", "0"),
    };

    public AppearancePage(IAppState app, IShell shell, ILocalizer loc, IReadOnlyList<LanguageOption> languages)
        : base(app, loc, loc.T("Appearance"),
            loc.T("A settings page built from declarations: groups become cards, and each row is one call."),
            shell.Layout)
    {
        Group(loc.T("Theme"));

        var names = languages.Select(language => new Choice(language.Name, language.Code)).ToArray();
        ComboRow(loc.T("Language"), loc.T("The interface language. The launcher restarts to apply it."), names,
            () => S.Str("ui_language"), value => S.Set("ui_language", value),
            changed: () =>
            {
                App.SaveNow();
                if (AskRestart(loc)) Restart();
            },
            glyph: Glyphs.Globe);

        ComboRow(loc.T("Layout"), loc.T("Rows read the layout when a page is built; switching rebuilds it."), new[]
        {
            new Choice(loc.T("Modern"), "modern"),
            new Choice(loc.T("Classic"), "classic"),
        }, () => S.Str("ui_layout") == "classic" ? "classic" : "modern",
            value => S.Set("ui_layout", value),
            changed: () => shell.ApplyLayout(S.Str("ui_layout") == "classic" ? UiLayout.Classic : UiLayout.Modern),
            glyph: Glyphs.Monitor);

        ComboRow(loc.T("Accent colour"), loc.T("One colour in; the whole surface scale follows it."),
            Accents.Select(choice => new Choice(loc.T(choice.Label), choice.Value)).ToArray(),
            () => S.Str("ui_theme"), value =>
            {
                S.Set("ui_theme", value);
                S.Set("ui_theme_colour", value switch
                {
                    "crimson" => "#C41E3A",
                    "moon" => "#8FB8E8",
                    "amber" => "#D99A2B",
                    _ => "#4C9DFF",
                });
            },
            changed: () => shell.ApplyTheme(),
            glyph: Glyphs.Color);

        ToggleRow(loc.T("High contrast"), loc.T("Windows sets this; the switch previews it here."),
            () => ThemeBoot.HighContrast, value => ThemeBoot.ApplyPalette(value), glyph: Glyphs.Monitor);

        ComboRow(loc.T("Background"), loc.T("A picture behind the page (the Modern layout only)."),
            Backgrounds.Select(choice => new Choice(loc.T(choice.Label), choice.Value)).ToArray(),
            () => S.Str("ui_background"), value => S.Set("ui_background", value),
            changed: () => shell.ApplyBackdrop(),
            glyph: Glyphs.Photo);

        ComboRow(loc.T("Blur"), loc.T("How much the picture is softened under the panes."),
            Blurs.Select(choice => new Choice(loc.T(choice.Label), choice.Value)).ToArray(),
            () => S.Str("ui_background_blur"), value => S.Set("ui_background_blur", value),
            changed: () => shell.ApplyBackdrop(),
            glyph: Glyphs.Lightbulb);

        Group(loc.T("Window"));
        ComboRow(loc.T("Reading direction"), loc.T("Mirrors the whole window; a row that ignores it is a bug."), new[]
        {
            new Choice("LTR", "ltr"),
            new Choice("RTL", "rtl"),
        }, () => S.Str("ui_flow") == "rtl" ? "rtl" : "ltr", value => S.Set("ui_flow", value),
            changed: () => shell.ApplyFlowDirection(S.Str("ui_flow") == "rtl"),
            glyph: Glyphs.Navigation);

        ComboRow(loc.T("Navigation"), loc.T("Tabs across the top, or a sidebar with the pages in groups."), new[]
        {
            new Choice(loc.T("Tabs"), "tabs"),
            new Choice(loc.T("Sidebar"), "sidebar"),
        }, () => S.Str("ui_navigation"), value => S.Set("ui_navigation", value),
            changed: () => shell.ApplyNavigation(),
            glyph: Glyphs.Navigation);

        ComboRow(loc.T("Window buttons"), loc.T("macOS traffic lights, or Windows caption buttons."),
            ButtonFamilies.Select(choice => new Choice(loc.T(choice.Label), choice.Value)).ToArray(),
            () => S.Str("ui_window_buttons"), value => S.Set("ui_window_buttons", value),
            changed: () => shell.ApplyWindowButtons(),
            glyph: Glyphs.Monitor);

        ComboRow(loc.T("Buttons side"), loc.T("Their usual side (macOS left, Windows right), or the other."),
            Sides.Select(choice => new Choice(loc.T(choice.Label), choice.Value)).ToArray(),
            () => S.Str("ui_window_buttons_side"), value => S.Set("ui_window_buttons_side", value),
            changed: () => shell.ApplyWindowButtons(),
            glyph: Glyphs.Navigation);

        ToggleRow(loc.T("Show the log at start"), loc.T("The activity log folds out under the page."),
            () => S.Bool("log_on_start"), value => S.SetBool("log_on_start", value),
            glyph: Glyphs.Info);

        Group(loc.T("Home"));
        TextRow(loc.T("Data folder"), loc.T("Where saves, caches and logs live. Empty: beside the application."),
            "D:\\Lantern\\data", () => S.Str("data_dir"), value => S.Set("data_dir", value),
            glyph: Glyphs.Folder);

        Group(loc.T("Log"));
        ToggleRow(loc.T("Write the log to a file"),
            loc.T("One file per day beside the settings; the crash handler writes there too."),
            () => S.Bool("log_to_file"), value => S.SetBool("log_to_file", value),
            glyph: Glyphs.Save);
        ComboRow(loc.T("Keep the log for"), loc.T("Older files are deleted when the launcher starts."),
            new[] { new Choice("3", "3"), new Choice("7", "7"), new Choice("14", "14"), new Choice("30", "30") },
            () => S.Str("log_keep_days"), value => S.Set("log_keep_days", value),
            glyph: Glyphs.Folder);

        Advanced(loc.T("Advanced rows"));
        ToggleRow(loc.T("Shader cache"), loc.T("Compiled pipelines are kept between runs."),
            () => S.Bool("shader_cache"), value => S.SetBool("shader_cache", value),
            glyph: Glyphs.Lightbulb);
        ToggleRow(loc.T("Verbose log"), loc.T("Every draw call goes into the session log."),
            () => S.Bool("verbose_log"), value => S.SetBool("verbose_log", value),
            glyph: Glyphs.Edit);

        Group(loc.T("Engine"));
        ComboRow(loc.T("Resolution"), loc.T("The window's size on the first start."), Resolutions,
            () => S.Str("resolution"), value => S.Set("resolution", value),
            glyph: Glyphs.Monitor);
        ComboRow(loc.T("Frame limit"), loc.T("The cap the engine paces to."),
            FrameLimits.Select(choice => new Choice(choice.Value == "0" ? loc.T("Unlimited") : choice.Label, choice.Value)).ToArray(),
            () => S.Str("frame_limit"), value => S.Set("frame_limit", value),
            glyph: Glyphs.Speed);

        SliderRow(loc.T("Overlay opacity"), loc.T("How visible the in-game overlay is."), 0, 100, 5,
            () => double.TryParse(S.Str("overlay_opacity"), out var value) ? value : 80,
            value => S.Set("overlay_opacity", value.ToString("0")), glyph: Glyphs.Lightbulb);

        SliderRow(loc.T("Volume"), loc.T("The interface sounds' loudness."), 0, 100, 5,
            () => double.TryParse(S.Str("volume"), out var value) ? value : 70,
            value => S.Set("volume", value.ToString("0")), glyph: Glyphs.Volume);
    }

    /// <summary>The one place that asks for the restart, so the page and the i18n harvest agree.</summary>
    public static bool AskRestart(ILocalizer loc) =>
        Prompt.Confirm(loc.T("Change language"), loc.T("Restart the launcher to apply the language."),
            loc.T("Restart now"), loc.T("Later"));

    /// <summary>Restart to re-read the language: strings are written into the controls when pages are built.</summary>
    private static void Restart()
    {
        if (Environment.ProcessPath is { } self)
        {
            Process.Start(new ProcessStartInfo(self)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(self)!,
            });
        }
        Application.Current.Shutdown();
    }
}
