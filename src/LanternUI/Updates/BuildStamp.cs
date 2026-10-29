using System.Reflection;

namespace Lantern.UI;

/// <summary>
/// What this build is, read from its own assembly attributes. The packaging script stamps
/// InformationalVersion ("1.0.5-beta.1"), and the beta number in it is the only way a build can know
/// it is a pre-release — a build without a stamp reads 1.0.0 and always looks stable.
/// </summary>
public static class BuildStamp
{
    public static Version Version(Assembly assembly) => assembly.GetName().Version ?? new Version(0, 0, 0);

    public static int Beta(Assembly assembly)
    {
        var informational = assembly
            .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
            .OfType<AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion ?? "";

        var plus = informational.IndexOf('+');
        if (plus > 0) informational = informational[..plus];
        return UpdateVersion.BetaOf(informational);
    }

    /// <summary>What a person reads: "1.0.5" or "1.0.5-beta.1".</summary>
    public static string Label(Assembly assembly) => UpdateVersion.Label(Version(assembly), Beta(assembly));
}
