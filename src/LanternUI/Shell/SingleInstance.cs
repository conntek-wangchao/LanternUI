using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Lantern.UI;

/// <summary>
/// One window per user: a named mutex decides who runs, and a second start raises the first
/// window instead of opening a second copy. Not a security boundary — a same-session convention.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly bool _owned;

    private SingleInstance(Mutex mutex, bool owned)
    {
        _mutex = mutex;
        _owned = owned;
    }

    public bool IsFirst => _owned;

    public static SingleInstance Acquire(string name)
    {
        var mutex = new Mutex(initiallyOwned: true, "Local\\" + name, out var created);
        return new SingleInstance(mutex, created);
    }

    /// <summary>Restores and focuses the window of the other running copy; false when none was found.</summary>
    public static bool ActivateExisting(string processName)
    {
        foreach (var process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                if (process.Id == Environment.ProcessId) continue;
                var handle = process.MainWindowHandle;
                if (handle == IntPtr.Zero) continue;

                ShowWindow(handle, SwRestore);
                SetForegroundWindow(handle);
                return true;
            }
        }
        return false;
    }

    public void Dispose()
    {
        if (_owned)
        {
            try { _mutex.ReleaseMutex(); }
            catch (ApplicationException) { /* not held: nothing to release */ }
        }
        _mutex.Dispose();
    }

    private const int SwRestore = 9;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);
}
