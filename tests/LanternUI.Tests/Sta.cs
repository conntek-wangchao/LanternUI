using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Lantern.UI;

namespace LanternUI.Tests;

/// <summary>
/// Runs WPF code on one STA thread with a themed Application, the way the app does it: the
/// framework's controls read their brushes from Application.Current.Resources.
/// </summary>
internal static class Sta
{
    private static readonly Lazy<Dispatcher> Ui = new(Start, LazyThreadSafetyMode.ExecutionAndPublication);

    private static Dispatcher Start()
    {
        var ready = new ManualResetEventSlim();
        Dispatcher? dispatcher = null;

        var thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            ThemeBoot.Install();
            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "lantern-ui-tests",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (!ready.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException("the UI thread did not start");
        return dispatcher!;
    }

    public static T Run<T>(Func<T> action) => Ui.Value.Invoke(action);

    public static void Run(Action action) => Ui.Value.Invoke(action);

    /// <summary>A settings file of its own, so tests never share state.</summary>
    public static Demo.DemoState NewState() =>
        new(Path.Combine(Path.GetTempPath(), "lanternui-tests", Guid.NewGuid().ToString("N") + ".json"));
}
