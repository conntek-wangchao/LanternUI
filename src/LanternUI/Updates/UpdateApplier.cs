using System.IO;
using System.Reflection;
using System.Text.Json;

namespace Lantern.UI;

/// <summary>One file to copy, and whether the running process holds it open.</summary>
public sealed record ApplyStep(string Source, string Destination, bool MoveAside);

/// <summary>What applying would do — computed without touching anything, so it can be asserted.</summary>
public sealed record ApplyPlan(string InstallRoot, IReadOnlyList<ApplyStep> Steps, IReadOnlyList<string> Skipped);

/// <summary>The outcome of an apply: ok, or why it stopped and what it managed to roll back.</summary>
public sealed record ApplyResult(bool Ok, string Message, IReadOnlyList<string> Written)
{
    public static ApplyResult Fail(string message, IReadOnlyList<string> written) => new(false, message, written);
}

/// <summary>
/// Applies a downloaded update to the install folder. Player data is never touched. A file the running
/// process has loaded cannot be written but can be renamed, so those files step aside to ".old" and are
/// deleted on the next start. Every new file is written next to its target first and only then swapped
/// in, and a swap that fails is rolled back — a half-updated application is worse than an old one.
/// </summary>
/// <summary>What finishing a pending update did: nothing pending, success, or why it failed.</summary>
public sealed record ApplyOutcome(bool Ok, string Message, string Label);

public static class UpdateApplier
{
    private const string PendingFile = "pending.json";

    /// <summary>Files and folders of the player's that a package never replaces.</summary>
    public static readonly IReadOnlyList<string> DefaultKept = new[] { "settings.json", "logs", "user", "mods" };

    /// <summary>
    /// The files this process is running from: the executable and every assembly it has loaded from the
    /// same folder. They are the ones a copy cannot overwrite. In a single-file build the assemblies
    /// have no location at all — then the executable is the only file in use, which is exactly right.
    /// </summary>
    public static IReadOnlyList<string> FilesInUse()
    {
        var files = new List<string>();
        if (Environment.ProcessPath is { } self) files.Add(self);

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            string? location;
            try { location = assembly.IsDynamic ? null : LocationOf(assembly); }
            catch (NotSupportedException) { location = null; }
            if (!string.IsNullOrEmpty(location) && File.Exists(location)) files.Add(location!);
        }

        return files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Trimming", "IL3000",
        Justification = "A single-file app reports no location; the executable is then the only file in use.")]
    private static string LocationOf(System.Reflection.Assembly assembly) => assembly.Location;

    /// <summary>
    /// The folder the application was installed into. Not AppContext.BaseDirectory: a single-file
    /// build sets that to a temporary extraction folder, and an update applied there would vanish
    /// with the next start while the real installation stayed at the old version.
    /// </summary>
    public static string InstallRoot
    {
        get
        {
            var executable = Environment.ProcessPath;
            var folder = string.IsNullOrEmpty(executable) ? null : Path.GetDirectoryName(executable);
            return string.IsNullOrEmpty(folder) ? AppContext.BaseDirectory : folder;
        }
    }

    /// <summary>
    /// Finishes a pending update against a given installation folder: the staged package replaces the
    /// files, the player's own files are kept, and a success clears the marker and the staged copy.
    /// </summary>
    public static ApplyOutcome ApplyPending(string workDirectory, string installRoot, Action<string>? log = null)
    {
        if (ReadPending(workDirectory) is not { } pending) return new ApplyOutcome(true, "", "");

        var plan = BuildPlan(pending.Staged, installRoot, DefaultKept, FilesInUse());
        var result = Apply(plan, log);
        if (result.Ok) ClearPending(workDirectory);
        return new ApplyOutcome(result.Ok, result.Message, pending.Label);
    }

    public static PendingUpdate? ReadPending(string workDirectory)
    {
        var file = Path.Combine(workDirectory, PendingFile);
        if (!File.Exists(file)) return null;
        try
        {
            return JsonSerializer.Deserialize<PendingUpdate>(File.ReadAllText(file));
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Forgets the pending update: the stamped package goes too, or every finished update would leave
    /// a copy of itself — tens of megabytes — in the work folder.
    /// </summary>
    public static void ClearPending(string workDirectory)
    {
        if (ReadPending(workDirectory) is { } pending)
        {
            try { if (Directory.Exists(pending.Staged)) Directory.Delete(pending.Staged, true); }
            catch (IOException) { }
        }

        foreach (var scratch in new[] { Path.Combine(workDirectory, "download"), Path.Combine(workDirectory, "staging") })
        {
            try { if (Directory.Exists(scratch)) Directory.Delete(scratch, true); }
            catch (IOException) { }
        }

        var file = Path.Combine(workDirectory, PendingFile);
        try { if (File.Exists(file)) File.Delete(file); }
        catch (IOException) { }
    }

    /// <summary>Maps the staged files onto the install folder, skipping the player's own.</summary>
    public static ApplyPlan BuildPlan(string stagedRoot, string installRoot,
        IReadOnlyCollection<string>? kept = null, IReadOnlyCollection<string>? inUse = null)
    {
        kept ??= DefaultKept;
        var busy = (inUse ?? Array.Empty<string>())
            .Where(path => !string.IsNullOrEmpty(path))
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var steps = new List<ApplyStep>();
        var skipped = new List<string>();

        foreach (var file in Directory.EnumerateFiles(stagedRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(stagedRoot, file);
            if (IsKept(relative, kept))
            {
                skipped.Add(relative);
                continue;
            }

            var destination = Path.GetFullPath(Path.Combine(installRoot, relative));
            steps.Add(new ApplyStep(file, destination, busy.Contains(destination)));
        }

        steps.Sort((left, right) => string.CompareOrdinal(left.Destination, right.Destination));
        skipped.Sort(StringComparer.OrdinalIgnoreCase);
        return new ApplyPlan(Path.GetFullPath(installRoot), steps, skipped);
    }

    /// <summary>A path is kept when it is one of the entries or sits under one of the folders.</summary>
    public static bool IsKept(string relative, IReadOnlyCollection<string> kept)
    {
        foreach (var entry in kept)
        {
            var clean = entry.Replace('/', '\\').TrimEnd('\\');
            if (relative.Equals(clean, StringComparison.OrdinalIgnoreCase)) return true;
            if (relative.StartsWith(clean + "\\", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>
    /// Two passes: every new file is first written beside its target (nothing locked is touched), then
    /// each target is swapped. A failed swap restores what was already swapped.
    /// </summary>
    public static ApplyResult Apply(ApplyPlan plan, Action<string>? log = null)
    {
        var prepared = new List<(ApplyStep Step, string Incoming)>();
        var written = new List<string>();

        try
        {
            foreach (var step in plan.Steps)
            {
                var folder = Path.GetDirectoryName(step.Destination);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

                var incoming = step.Destination + ".incoming";
                if (File.Exists(incoming)) File.Delete(incoming);
                File.Copy(step.Source, incoming, overwrite: true);
                prepared.Add((step, incoming));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            foreach (var (_, incoming) in prepared)
            {
                try { if (File.Exists(incoming)) File.Delete(incoming); }
                catch (IOException) { }
            }
            return ApplyResult.Fail("could not prepare the files: " + e.Message, Array.Empty<string>());
        }

        var swapped = new List<(string Destination, string? Aside)>();
        foreach (var (step, incoming) in prepared)
        {
            try
            {
                string? aside = null;
                if (File.Exists(step.Destination))
                {
                    if (step.MoveAside)
                    {
                        aside = step.Destination + ".old";
                        if (File.Exists(aside)) File.Delete(aside);
                        File.Move(step.Destination, aside);
                    }
                }

                File.Move(incoming, step.Destination, overwrite: true);
                swapped.Add((step.Destination, aside));
                written.Add(step.Destination);
                log?.Invoke("update: " + Path.GetRelativePath(plan.InstallRoot, step.Destination));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Rollback(swapped);
                return ApplyResult.Fail("could not replace " + Path.GetFileName(step.Destination) + ": " + e.Message,
                    written);
            }
        }

        return new ApplyResult(true, "", written);
    }

    private static void Rollback(IReadOnlyList<(string Destination, string? Aside)> swapped)
    {
        for (var index = swapped.Count - 1; index >= 0; index--)
        {
            var (destination, aside) = swapped[index];
            try
            {
                if (aside != null && File.Exists(aside)) File.Move(aside, destination, overwrite: true);
            }
            catch (IOException) { /* the next start retries the whole update: pending.json is still there */ }
        }
    }

    /// <summary>The ".old" and ".incoming" files a previous update left behind; call it on start.</summary>
    public static void Cleanup(string installRoot)
    {
        if (!Directory.Exists(installRoot)) return;
        foreach (var pattern in new[] { "*.old", "*.incoming" })
            foreach (var file in Directory.EnumerateFiles(installRoot, pattern, SearchOption.AllDirectories))
            {
                try { File.Delete(file); }
                catch (IOException) { /* still in use: next start */ }
            }
    }
}