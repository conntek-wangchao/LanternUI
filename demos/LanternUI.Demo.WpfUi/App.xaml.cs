using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Lantern.UI;
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;

namespace LanternUI.Demo.WpfUi;

public partial class App : Application
{
    private static readonly string CrashLog = Path.Combine(Path.GetTempPath(), "lanternui-wpfui-crash.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, a) => Record(a.ExceptionObject as Exception);
        DispatcherUnhandledException += (_, a) => { Record(a.Exception); a.Handled = true; };

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

        // Order matters: the framework palette, then WPF-UI's dictionaries, then the bridge last so
        // the bridge's keys win for every control WPF-UI brings.
        ThemeBoot.Install();

        var resources = Current.Resources.MergedDictionaries;
        resources.Add(new ThemesDictionary { Theme = ApplicationTheme.Dark });
        resources.Add(new ControlsDictionary());
        resources.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/LanternUI.Demo.WpfUi;component/Theming/WpfUiCompat.xaml"),
        });

        var state = new CompactState();
        var shell = new ShellWindow { App = state };
        state.Logged += (message, level) => shell.Log(message, level);
        state.Saved += shell.ShowSaved;

        shell.SetPages(new[]
        {
            new PageEntry("native", "Native controls", () => new Pages.NativePage(state, shell)),
        });
        shell.ApplyTheme();
        shell.Show();
        state.Log("WPF-UI 4.3.0 merged, then bridged onto the Lantern palette.", LogLevel.Good);

        var at = Array.IndexOf(e.Args, "--shot");
        if (at >= 0 && at + 1 < e.Args.Length)
        {
            var path = e.Args[at + 1];
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

    private static void Shot(Window window, string path)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Max(1, window.ActualWidth), (int)Math.Max(1, window.ActualHeight),
            96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        using var stream = File.Create(full);
        encoder.Save(stream);
    }

    private static void Record(Exception? exception)
    {
        try
        {
            File.AppendAllText(CrashLog, "=== " + DateTime.Now.ToString("HH:mm:ss") + Environment.NewLine +
                                         (exception?.ToString() ?? "(none)") + Environment.NewLine);
        }
        catch
        {
            // never let logging be the crash
        }
    }
}
