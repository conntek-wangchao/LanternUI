using System.IO;

namespace Lantern.UI;

/// <summary>
/// The stable and beta manifests next to each other: "release.json" and "release-beta.json". A channel
/// address names one of them (a file) or the folder holding both, and the chosen track decides which
/// file is read.
/// </summary>
public static class UpdateChannel
{
    public const string Stable = "stable";
    public const string Beta = "beta";

    public static string Name(bool beta) => beta ? "release-beta.json" : "release.json";

    /// <summary>The manifest address for the track: a file name keeps its folder, a folder gets the file.</summary>
    public static string Address(string channel, bool beta)
    {
        var source = (channel ?? "").Trim();
        if (source.Length == 0) return "";

        var name = Name(beta);
        var isFile = source.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

        // A URL is not a path: Path.Combine would turn "https://host/x" into separators Windows uses.
        if (source.Contains("://", StringComparison.Ordinal))
        {
            var folder = isFile ? source[..(source.LastIndexOf('/') + 1)] : source.TrimEnd('/') + "/";
            return folder + name;
        }

        return Path.Combine(isFile ? Path.GetDirectoryName(source) ?? "" : source, name);
    }

    /// <summary>The source for that address: a URL when the channel is one, a folder otherwise.</summary>
    public static IUpdateSource Source(string channel, bool beta, TimeSpan? timeout = null)
    {
        var source = (channel ?? "").Trim();
        if (source.Contains("://", StringComparison.Ordinal))
            return new HttpUpdateSource(Address(source, beta).Replace('\\', '/'), timeout);

        return new FolderUpdateSource(source, Name(beta));
    }

    /// <summary>Reads the track from a setting value; anything unknown is stable.</summary>
    public static bool IsBeta(string? setting) =>
        string.Equals((setting ?? "").Trim(), Beta, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Which track a build follows: the setting when it says something, otherwise the build itself —
    /// a beta stays on beta until someone chooses, which is what the reference launcher does.
    /// </summary>
    public static bool FollowsBeta(string? setting, int currentBeta)
    {
        var chosen = (setting ?? "").Trim();
        if (chosen.Length == 0) return currentBeta > 0;
        return IsBeta(chosen);
    }
}