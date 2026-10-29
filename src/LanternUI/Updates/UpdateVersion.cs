namespace Lantern.UI;

/// <summary>
/// Version comparison for updates: a plain Version, plus the beta number. A stable release is newer
/// than any beta of the same version (1.2.0 &gt; 1.2.0-beta.3), which is the order the channel uses.
/// </summary>
public static class UpdateVersion
{
    /// <summary>Reads "1.2", "1.2.0", "1.2.0-beta.3" or "v1.2.0-beta.3".</summary>
    public static Version Parse(string text)
    {
        var body = text.Trim().TrimStart('v', 'V');
        var dash = body.IndexOf('-');
        if (dash >= 0) body = body[..dash];
        return Normalize(Version.TryParse(body, out var parsed) ? parsed : new Version(0, 0, 0));
    }

    /// <summary>The beta number of "1.2.0-beta.3"; 0 for a stable version.</summary>
    public static int BetaOf(string text)
    {
        var body = text.Trim().TrimStart('v', 'V');
        var dash = body.IndexOf('-');
        if (dash < 0) return 0;
        var rest = body[(dash + 1)..];
        var dot = rest.LastIndexOf('.');
        return dot >= 0 && int.TryParse(rest[(dot + 1)..], out var beta) ? beta : 0;
    }

    /// <summary>Three components, so 1.2 and 1.2.0 compare equal.</summary>
    public static Version Normalize(Version version) =>
        new(version.Major, version.Minor, Math.Max(0, version.Build));

    /// <summary>Negative when the left is older, positive when it is newer.</summary>
    public static int Compare(Version left, int leftBeta, Version right, int rightBeta)
    {
        var byVersion = Normalize(left).CompareTo(Normalize(right));
        if (byVersion != 0) return byVersion;

        if (leftBeta == rightBeta) return 0;
        if (leftBeta == 0) return 1;      // stable beats its own betas
        if (rightBeta == 0) return -1;
        return leftBeta.CompareTo(rightBeta);
    }

    public static bool IsNewer(ReleaseInfo release, Version current, int currentBeta) =>
        Compare(release.Version, release.Beta, current, currentBeta) > 0;

    /// <summary>What a person reads: "1.0.5" or "1.0.5-beta.1".</summary>
    public static string Label(Version version, int beta) =>
        beta <= 0 ? Normalize(version).ToString() : Normalize(version) + "-beta." + beta;
}