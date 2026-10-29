using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Lantern.UI;

namespace LanternUI.Demo;

public partial class App : Application
{
    private static readonly string CrashLog = Path.Combine(Path.GetTempPath(), "lanternui-crash.log");

    private SingleInstance? _single;
    private FileLogSink? _sink;

    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Record(args.ExceptionObject as Exception);
        DispatcherUnhandledException += (_, args) => { Record(args.Exception); args.Handled = true; };

        try
        {
            Start(e);
        }
        catch (Exception exception)
        {
            Record(exception);
            throw;
        }
    }

    private void Start(StartupEventArgs e)
    {
        base.OnStartup(e);
        ThemeBoot.Install();

        var args = e.Args;
        // The palette has to be looked at too, not only trusted: this is how a screenshot of it is taken.
        if (args.Contains("--high-contrast")) ThemeBoot.ApplyPalette(true);

        // One window per user. The automation switches (a screenshot, the i18n gate) must be able
        // to run beside a normal session, so they skip the guard.
        var automation = args.Any(arg => arg is "--shot" or "--check-i18n" or "--test-prompt");
        if (!automation)
        {
            _single = SingleInstance.Acquire("LanternUI.Demo");
            if (!_single.IsFirst)
            {
                SingleInstance.ActivateExisting(System.Diagnostics.Process.GetCurrentProcess().ProcessName);
                Shutdown();
                return;
            }
        }

        var configAt = Array.IndexOf(args, "--config");
        var configPath = configAt >= 0 && configAt + 1 < args.Length
            ? args[configAt + 1]
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Lantern UI", "settings.json");

        var catalog = JsonCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Localization"));
        var state = new DemoState(configPath);

        var sourceAt = Array.IndexOf(args, "--update-source");
        if (sourceAt >= 0 && sourceAt + 1 < args.Length) state.Settings.Set("ui_update_source", args[sourceAt + 1]);

        var trackAt = Array.IndexOf(args, "--update-channel");
        if (trackAt >= 0 && trackAt + 1 < args.Length) state.Settings.Set("ui_update_channel", args[trackAt + 1]);

        // The log goes to a file as well as to the pane: a report from a user without the file is a
        // report nobody can act on. Two switches: the setting, and --no-log-file for a clean run.
        if (state.Settings.Bool("log_to_file") && !args.Contains("--no-log-file"))
        {
            var days = int.TryParse(state.Settings.Str("log_keep_days"), out var keep) ? keep : 7;
            var logs = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(configPath))!, "logs");
            _sink = new FileLogSink(logs, "lantern", days);
            state.Sink = _sink;
        }

        var langAt = Array.IndexOf(args, "--lang");
        var language = langAt >= 0 && langAt + 1 < args.Length ? args[langAt + 1] : state.Settings.Str("ui_language");
        if (language.Length == 0) language = "en";
        if (langAt >= 0) state.Settings.Set("ui_language", language);

        var localizer = new Localizer(language, catalog.For(language));

        // The first thing a start does: finish an update that was downloaded last time.
        ApplyPendingUpdate(state, localizer);

        // The template ships one sample picture so the Modern backdrop has something to show. A stored
        // path can point at a previous single-file extraction folder, so a missing file counts as empty.
        var backdrop = state.Settings.Str("ui_background_image");
        if (backdrop.Length == 0 || !File.Exists(backdrop))
            state.Settings.Set("ui_background_image", Path.Combine(AppContext.BaseDirectory, "Assets", "backdrop-sample.png"));

        var backdropAt = Array.IndexOf(args, "--backdrop");
        if (backdropAt >= 0 && backdropAt + 1 < args.Length)
        {
            state.Settings.Set("ui_background", "custom");
            state.Settings.Set("ui_background_image", args[backdropAt + 1]);
        }

        var promptShotAt = Array.IndexOf(args, "--test-prompt");
        if (promptShotAt >= 0 && promptShotAt + 1 < args.Length) Prompt.SnapshotPath = args[promptShotAt + 1];

        if (args.Contains("--check-updates") || args.Contains("--download-update"))
        {
            var source = state.Settings.Str("ui_update_source");
            if (source.Length == 0)
            {
                state.Log("update: no channel configured", LogLevel.Bad);
                Shutdown(1);
                return;
            }

            var service = ChannelFor(state, configPath, source, localizer);
            var release = service.CheckAsync().GetAwaiter().GetResult();
            if (release == null)
            {
                state.Sink.Flush();
                // 1 = the channel could not be read, 2 = it answered and has nothing newer: a script
                // watching a release has to be able to tell those apart.
                Shutdown(service.Stage == UpdateStage.Failed ? 1 : 2);
                return;
            }

            if (!args.Contains("--download-update"))
            {
                state.Sink.Flush();
                Shutdown(0);
                return;
            }

            var result = service.DownloadAsync(release).GetAwaiter().GetResult();
            state.Sink.Flush();
            Shutdown(result.Ok ? 0 : 1);
            return;
        }

        // The self-check builds the interface with a recorder and reports the gaps; it shows nothing.
        // Its exit code is the CI gate: 1 when a required language has gaps or any placeholder broke.
        if (args.Contains("--check-i18n"))
        {
            var requireAt = Array.IndexOf(args, "--require");
            var required = requireAt >= 0 && requireAt + 1 < args.Length ? args[requireAt + 1] : null;
            Shutdown(I18nCheck.Run(catalog, state, required).ExitCode);
            return;
        }

        var layoutAt = Array.IndexOf(args, "--layout");
        var layout = layoutAt >= 0 && layoutAt + 1 < args.Length && args[layoutAt + 1] == "classic"
            ? UiLayout.Classic
            : UiLayout.Modern;

        var navAt = Array.IndexOf(args, "--nav");
        if (navAt >= 0 && navAt + 1 < args.Length) state.Settings.Set("ui_navigation", args[navAt + 1]);

        var accentAt = Array.IndexOf(args, "--accent");
        if (accentAt >= 0 && accentAt + 1 < args.Length) state.Settings.Set("ui_theme_colour", args[accentAt + 1]);

        Prompt.Localizer = localizer;

        var shell = new ShellWindow { App = state, Localizer = localizer };
        shell.SetLayout(layout);
        shell.ApplyLanguage();
        WindowPlacement.Restore(shell, state.Settings);
        WindowPlacement.Track(shell, state.Settings, state.SaveSoon);
        state.Logged += (message, level) => shell.Log(message, level);
        state.Saved += shell.ShowSaved;

        shell.SetPages(new[]
        {
            new PageEntry("appearance", localizer.T("Appearance"),
                () => new Pages.AppearancePage(state, shell, localizer, catalog.Languages)),
            new PageEntry("about", localizer.T("About"),
                () => new Pages.AboutPage(state, shell, localizer)),
            new PageEntry("shortcuts", localizer.T("Shortcuts"),
                () => new Pages.ShortcutsPage(state, shell, localizer)),
            new PageEntry("registers", localizer.T("Registers"),
                () => new Pages.RegisterPage(state, shell, localizer)),
            new PageEntry("live", localizer.T("Live"),
                () => new Pages.LivePage(state, shell, localizer)),
            new PageEntry("more", localizer.T("More"),
                () => new Pages.MorePage(state, shell, localizer)),
            new PageEntry("list", localizer.T("List"),
                () => new Pages.ListDemoPage(state, shell, localizer)),
        });
        shell.ApplyTheme();

        // The look a previous run saved is applied before the first frame: without this a restart
        // silently came back in the default layout and direction.
        shell.ApplyLayout(state.Settings.Str("ui_layout") == "classic" ? UiLayout.Classic : UiLayout.Modern);
        shell.ApplyFlowDirection(state.Settings.Str("ui_flow") == "rtl");

        shell.Show();
        state.Log(localizer.T("Ready") + "  路  " + localizer.Code + "  路  " + configPath, LogLevel.Good);

        if (args.Contains("--toast"))
            shell.Notify(localizer.T("Notification"), localizer.T("A notification card, shown in the corner."));

        if (Prompt.SnapshotPath != null)
            Dispatcher.BeginInvoke(new Action(() => Prompt.Confirm(localizer.T("Change language"),
                    localizer.T("Restart the launcher to apply the language."),
                    localizer.T("Restart now"), localizer.T("Later"))),
                DispatcherPriority.ApplicationIdle);

        var pageAt = Array.IndexOf(args, "--page");
        if (pageAt >= 0 && pageAt + 1 < args.Length) shell.ShowPage(args[pageAt + 1]);

        if (args.Contains("--log")) shell.ShowLog();

        var scrollAt = Array.IndexOf(args, "--scroll");
        if (scrollAt >= 0 && scrollAt + 1 < args.Length &&
            double.TryParse(args[scrollAt + 1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var fraction))
            Dispatcher.BeginInvoke(new Action(() => shell.ScrollTo(fraction)), DispatcherPriority.ApplicationIdle);

        var searchAt = Array.IndexOf(args, "--search");
        if (searchAt >= 0 && searchAt + 1 < args.Length)
            Dispatcher.BeginInvoke(new Action(() => shell.FocusSearch(args[searchAt + 1])),
                DispatcherPriority.ApplicationIdle);

        if (args.Contains("--save"))
        {
            state.Settings.Set("demo_stamp", DateTime.Now.ToString("HH:mm:ss"));
            state.SaveNow();
        }

        var at = Array.IndexOf(args, "--shot");
        if (at >= 0 && at + 1 < args.Length)
        {
            var path = args[at + 1];
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                Shot(shell, path);
                Shutdown();
            };
            timer.Start();
        }
    }

    private static UpdateService ChannelFor(DemoState state, string configPath, string source, ILocalizer localizer)
    {
        var work = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(configPath))!, "updates");
        var current = BuildStamp.Version(typeof(App).Assembly);
        var beta = UpdateChannel.FollowsBeta(state.Settings.Str("ui_update_channel"), BuildStamp.Beta(typeof(App).Assembly));

        return new UpdateService(UpdateChannel.Source(source, beta), work, current, 0, state.Sink, localizer)
        {
            AllowBeta = beta,
        };
    }

    /// <summary>
    /// The end of an update: leftovers from a previous one are removed, and a staged package is put in
    /// place before the window opens. The files being replaced are the ones next to this executable,
    /// so nothing is touched until the download has been verified.
    /// </summary>
    private void ApplyPendingUpdate(DemoState state, ILocalizer localizer)
    {
        // The installation folder is the executable's own: a single-file build reports a temporary
        // extraction folder as AppContext.BaseDirectory, and an update written there would be lost.
        var install = UpdateApplier.InstallRoot;
        UpdateApplier.Cleanup(install);

        var work = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(state.FilePath))!, "updates");
        var result = UpdateApplier.ApplyPending(work, install, message => state.Log(message, LogLevel.Good));

        // The marker stays when the apply failed: the next start has nothing locked and tries again.
        if (!result.Ok)
        {
            state.Log(localizer.T("The update could not be applied:") + " " + result.Message, LogLevel.Bad);
            return;
        }

        if (result.Label.Length > 0) state.Log(localizer.T("Update installed") + " " + result.Label, LogLevel.Good);
    }

    /// <summary>Renders the window to a PNG (the off-screen trick the reference uses for its dialogs).</summary>
    private static void Shot(Window window, string path)
    {
        window.UpdateLayout();
        var width = (int)Math.Max(1, window.ActualWidth);
        var height = (int)Math.Max(1, window.ActualHeight);

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

    private void Record(Exception? exception)
    {
        var text = "unhandled: " + (exception?.ToString() ?? "(no exception)");

        // Both places, on purpose: the file survives the process, the crash file survives a
        // log folder that cannot be written to.
        try { _sink?.Write(DateTime.Now, LogLevel.Bad, text); }
        catch { /* never let logging be the crash */ }

        try
        {
            File.AppendAllText(CrashLog, "=== " + DateTime.Now.ToString("HH:mm:ss") + Environment.NewLine +
                                        (exception?.ToString() ?? "(no exception)") + Environment.NewLine);
        }
        catch
        {
            // never let logging be the crash
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _sink?.Dispose();
        base.OnExit(e);
    }
}