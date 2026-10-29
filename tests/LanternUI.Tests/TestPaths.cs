using System.IO;

namespace LanternUI.Tests;

/// <summary>Where things are, found from the test binary instead of hardcoded.</summary>
internal static class TestPaths
{
    /// <summary>The repository root: the folder that holds the solution.</summary>
    public static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LanternUI.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("LanternUI.sln not found above " + AppContext.BaseDirectory);
    }

    /// <summary>The committed baselines (written by LANTERN_UPDATE_SNAPSHOTS=1).</summary>
    public static string SnapshotSource =>
        Path.Combine(RepoRoot(), "tests", "LanternUI.Tests", "snapshots");

    /// <summary>The copy beside the test binary that the assertions read.</summary>
    public static string SnapshotCopy => Path.Combine(AppContext.BaseDirectory, "snapshots");
}
