using System.IO;
using System.Windows;
using System.Windows.Controls;
using Lantern.UI;

namespace LanternUI.Demo.Pages;

public sealed class AboutPage : PageBase
{
    /// <summary>
    /// The update rows: the version this build is, the channel it follows, and the buttons. Checking
    /// and downloading are the framework's UpdateService; the installed files are only touched by the
    /// confirmed "install" step, which restarts.
    /// </summary>
    private void BuildUpdates(ILocalizer loc, IShell shell)
    {
        // The demo's own state carries the settings path and the log sink; a host with a different
        // state just does not get the rows.
        if (App is not DemoState state) return;

        var version = GetType().Assembly.GetName().Version ?? new Version(1, 0, 0);
        Add(Card(loc.T("Version"), null, Label(version.ToString(3)), out _, glyph: Glyphs.Info));

        // Editable: without a row here the only way to point the build at a channel was a command
        // line switch or hand-editing settings.json, and the page then said "no channel configured".
        var channel = TextRow(loc.T("Update channel"), loc.T("A folder or address holding release.json; empty for none."),
            loc.T(@"For example D:\releases"), () => S.Str("ui_update_source"),
            value => S.Set("ui_update_source", value), glyph: Glyphs.Folder, width: 420);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var check = new Button { Content = loc.T("Check now"), Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0) };
        check.SetResourceReference(StyleProperty, "GhostButton");
        var install = new Button { Content = loc.T("Install"), Padding = new Thickness(12, 4, 12, 4), Visibility = Visibility.Collapsed };
        install.SetResourceReference(StyleProperty, "AccentButton");
        buttons.Children.Add(check);
        buttons.Children.Add(install);

        var work = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(state.FilePath))!, "updates");
        UpdateService? service = null;

        // Built per click from what the row holds now, so a channel typed a moment ago is used.
        UpdateService? Service()
        {
            var source = S.Str("ui_update_source").Trim();
            return source.Length == 0 ? null : new UpdateService(Channel(source), work, version, 0, state.Sink, loc);
        }

        Add(Card(loc.T("Updates"), loc.T("Checks the channel and stages a package that matches its sha256."),
            buttons, out var status, wide: true, glyph: Glyphs.Save));
        SetHint(status, Service() == null ? loc.T("No update channel is configured.") : "");

        check.Click += async (_, _) =>
        {
            service = Service();
            if (service == null)
            {
                SetHint(status, loc.T("No update channel is configured."));
                install.Visibility = Visibility.Collapsed;
                channel.Focus();
                return;
            }

            // A click must never take the process down: whatever the channel does, the worst case is
            // a line in the hint.
            check.IsEnabled = false;
            try
            {
                SetHint(status, loc.T("Checking..."));
                var release = await service.CheckAsync();
                if (release == null)
                {
                    SetHint(status, service.Stage == UpdateStage.Failed
                        ? loc.T("The check failed.") + " " + service.LastMessage
                        : loc.T("Up to date."));
                    install.Visibility = Visibility.Collapsed;
                    return;
                }

                SetHint(status, loc.F($"Update {release.Label} is available."));
                install.Content = loc.F($"Install {release.Label}");
                install.Visibility = Visibility.Visible;
            }
            catch (Exception e)
            {
                SetHint(status, loc.T("The check failed.") + " " + e.Message);
            }
            finally
            {
                check.IsEnabled = true;
            }
        };

        install.Click += async (_, _) =>
        {
            if (service?.Available is not { } release) return;

            install.IsEnabled = false;
            try
            {
                SetHint(status, loc.F($"Downloading {release.Label}..."));
                var result = await service.DownloadAsync(release);
                if (!result.Ok)
                {
                    SetHint(status, loc.F($"Download refused: {result.Message}"));
                    return;
                }

                SetHint(status, loc.F($"Ready to install {release.Label}."));
                if (!Prompt.Confirm(loc.T("Update ready"), loc.F($"Install {release.Label} now? The application restarts."),
                        loc.T("Restart now"), loc.T("Later"))) return;

                // ApplyPending is the same path a start takes: it replaces the files where the
                // executable is, keeps the player's own, and clears the marker and the staged copy.
                var applied = UpdateApplier.ApplyPending(work, UpdateApplier.InstallRoot,
                    message => state.Log(message, LogLevel.Good));
                if (!applied.Ok)
                {
                    SetHint(status, loc.T("The update could not be applied:") + " " + applied.Message);
                    return;
                }

                Restart();
            }
            catch (Exception e)
            {
                SetHint(status, loc.T("The update could not be applied:") + " " + e.Message);
            }
            finally
            {
                install.IsEnabled = true;
            }
        };
    }

    /// <summary>Starts a fresh copy of this build; the current one closes.</summary>
    private static void Restart()
    {
        if (Environment.ProcessPath is { } self)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(self)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(self)!,
            });
        }
        Application.Current.Shutdown();
    }

    private static TextBlock Label(string text)
    {
        var block = new TextBlock { Text = text, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
        block.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        return block;
    }

    private static IUpdateSource Channel(string source) =>
        source.Contains("://", StringComparison.Ordinal)
            ? new HttpUpdateSource(source)
            : new FolderUpdateSource(source);

    public AboutPage(IAppState app, IShell shell, ILocalizer loc)
        : base(app, loc, loc.T("About"), loc.T("What this build is: the first slice of the framework, P1."), shell.Layout)
    {
        Group(loc.T("Updates"));
        BuildUpdates(loc, shell);

        Group(loc.T("Rows"));
        ToggleRow(loc.T("Toggle row"), loc.T("A switch: the whole control is a token-driven template."),
            () => S.Bool("shader_cache"), value => S.SetBool("shader_cache", value));
        ComboRow(loc.T("Choice row (drop-down)"), loc.T("More than six options becomes a drop-down."), new[]
        {
            new Choice("Alpha", "a"), new Choice("Beta", "b"), new Choice("Gamma", "c"),
            new Choice("Delta", "d"), new Choice("Epsilon", "e"), new Choice("Zeta", "f"),
            new Choice("Eta", "g"), new Choice("Theta", "h"), new Choice("Iota", "i"),
            new Choice("Kappa", "j"), new Choice("Lambda", "k"), new Choice("Mu", "l"),
        }, () => "a", _ => { }, glyph: Glyphs.Lab);

        // Anything that is not a row ends the card, exactly like the reference's Sectionizer.
        var note = new TextBlock
        {
            Text = loc.T("This paragraph sits outside a card: Sectionizer ends the group at the first element that is not a row."),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4, 2, 4, 12),
        };
        note.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");
        Body.Children.Add(note);

        Group(loc.T("Framework"));
        Add(Card(loc.T("Sections"), loc.T("Groups are cards, built once when the page loads."), null, out _));
        Add(Card(loc.T("Search index"), loc.T("Every row added here is searchable — no second list to keep."), null, out _));
    }
}