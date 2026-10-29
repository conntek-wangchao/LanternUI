using System.Globalization;
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

        var version = BuildStamp.Version(GetType().Assembly);
        var current = BuildStamp.Beta(GetType().Assembly);
        Add(Card(loc.T("Version"), null, Label(UpdateVersion.Label(version, current)), out _, glyph: Glyphs.Info));

        // Editable: without a row here the only way to point the build at a channel was a command
        // line switch or hand-editing settings.json, and the page then said "no channel configured".
        var channel = TextRow(loc.T("Channel address"), loc.T("A folder or address holding release.json; empty for none."),
            loc.T(@"For example D:\releases"), () => S.Str("ui_update_source"),
            value => S.Set("ui_update_source", value), glyph: Glyphs.Folder, width: 420);

        ComboRow(loc.T("Update channel"), loc.T("Stable follows finished releases; beta also takes pre-releases."),
            new[]
            {
                new Choice(loc.T("Stable"), UpdateChannel.Stable),
                new Choice(loc.T("Beta"), UpdateChannel.Beta),
            },
            // The control shows the track in force: a beta build with no setting follows beta, and the
            // segmented control must say so instead of showing the first option.
            () => UpdateChannel.FollowsBeta(S.Str("ui_update_channel"), current) ? UpdateChannel.Beta : UpdateChannel.Stable,
            value => S.Set("ui_update_channel", value), glyph: Glyphs.Folder);

        var body = new StackPanel();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var bar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Width = 260,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0),
            Visibility = Visibility.Collapsed,
        };
        bar.SetResourceReference(StyleProperty, "Progress");
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
            if (source.Length == 0) return null;

            var beta = UpdateChannel.FollowsBeta(S.Str("ui_update_channel"), BuildStamp.Beta(GetType().Assembly));
            return new UpdateService(UpdateChannel.Source(source, beta), work, version, 0, state.Sink, loc)
            {
                AllowBeta = beta,
            };
        }

        body.Children.Add(buttons);
        body.Children.Add(bar);
        Add(Card(loc.T("Updates"), loc.T("Checks the channel and stages a package that matches its sha256."),
            body, out var status, wide: true, glyph: Glyphs.Save));
        SetHint(status, Service() == null ? loc.T("No update channel is configured.") : "");

        // The screenshots in the docs need the row as it looks mid-download, which no unattended
        // render can reach. LANTERN_DEMO_PROGRESS=<percent> fills the bar for that one picture.
        if (Environment.GetEnvironmentVariable("LANTERN_DEMO_PROGRESS") is { Length: > 0 } demo &&
            double.TryParse(demo, NumberStyles.Float, CultureInfo.InvariantCulture, out var filled))
        {
            bar.Value = Math.Clamp(filled, 0, 100);
            bar.Visibility = Visibility.Visible;
            var percentText = Math.Floor(bar.Value).ToString(CultureInfo.InvariantCulture);
            const string megabytes = "63.5";
            SetHint(status, loc.F($"Downloading {version.ToString(3)}... {percentText}% of {megabytes} MB"));
        }

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
                        ? service.LastMessage
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
                bar.Value = 0;
                bar.Visibility = Visibility.Visible;
                SetHint(status, loc.F($"Downloading {release.Label}..."));

                // The service reports on whatever thread copied the file; the bar belongs to the UI one.
                void Progress(long received, long total)
                {
                    var percent = total > 0 ? Math.Clamp(received * 100.0 / total, 0, 100) : 0;
                    var percentText = Math.Floor(percent).ToString(CultureInfo.InvariantCulture);
                    var megabytes = ((total > 0 ? total : received) / 1048576.0)
                        .ToString("0.#", CultureInfo.InvariantCulture);

                    Dispatcher.BeginInvoke(() =>
                    {
                        bar.Value = percent;
                        SetHint(status, total > 0
                            ? loc.F($"Downloading {release.Label}... {percentText}% of {megabytes} MB")
                            : loc.F($"Downloading {release.Label}... {megabytes} MB"));
                    });
                }

                service.DownloadProgress += Progress;
                UpdateResult result;
                try
                {
                    result = await service.DownloadAsync(release);
                }
                finally
                {
                    service.DownloadProgress -= Progress;
                }

                if (!result.Ok)
                {
                    bar.Visibility = Visibility.Collapsed;
                    SetHint(status, result.Message);
                    return;
                }

                bar.Visibility = Visibility.Collapsed;
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