using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace LanternUI.Tests;

/// <summary>
/// The framework must not carry user-visible English of its own: a message goes through
/// ILocalizer.T (or the wrapper that does), otherwise it can never be translated and the i18n gate
/// cannot even see it. Any literal that still looks like a sentence has to be listed in
/// string-baseline.txt with a reason.
/// </summary>
public class StringAuditTests
{
    private static readonly Regex Literal = new("\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled);

    private static string RepoRoot() => TestPaths.RepoRoot();

    [Fact]
    public void No_user_visible_english_outside_the_localizer()
    {
        var root = RepoRoot();
        var source = Path.Combine(root, "src", "LanternUI");
        var baseline = Baseline();

        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file);
            if (relative.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) ||
                relative.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)) continue;

            foreach (var line in File.ReadLines(file))
            {
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("//") || trimmed.StartsWith("/*") || trimmed.StartsWith("*")) continue;

                foreach (Match match in Literal.Matches(line))
                {
                    var text = match.Groups[1].Value;
                    if (!text.Contains(' ')) continue;
                    if (text.Length < 8) continue;
                    // A fragment with a leading or trailing space is being concatenated ("update: " +
                    // what happened), not a sentence of the interface.
                    if (char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1])) continue;
                    if (!Regex.IsMatch(text, "[A-Za-z]{3}")) continue;
                    if (text.StartsWith("pack://") || text.StartsWith("http")) continue;

                    // An interpolated string sits behind "$", so drop it before looking for the call.
                    var before = line.Substring(0, match.Index).TrimEnd().TrimEnd('$');
                    if (before.EndsWith("T(") || before.EndsWith("F(") || before.Contains("Format(") ||
                        before.EndsWith("Log(") || before.EndsWith("Notify(") || before.EndsWith("SetHint(")) continue;
                    // An exception message is for the developer reading a stack trace, not for the window.
                    if (before.EndsWith("Exception(")) continue;

                    if (baseline.Contains(text)) continue;
                    offenders.Add(relative + "  \"" + text + "\"");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "user-visible English outside ILocalizer.T:" + Environment.NewLine + string.Join(Environment.NewLine, offenders) +
            Environment.NewLine + "Wrap it in T(…), or add it to tests/LanternUI.Tests/string-baseline.txt with a reason.");
    }

    /// <summary>
    /// The same rule for Chinese: a hardcoded Chinese label is a string no other language can change,
    /// and it hides from the key harvest just as well as a hardcoded English one does.
    /// </summary>
    [Fact]
    public void No_hardcoded_chinese_outside_the_localizer()
    {
        var root = RepoRoot();
        var source = Path.Combine(root, "src", "LanternUI");
        var baseline = Baseline();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file);
            if (relative.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) ||
                relative.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)) continue;

            foreach (var line in File.ReadLines(file))
            {
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("//") || trimmed.StartsWith("/*") || trimmed.StartsWith("*")) continue;
                if (!HasChinese(line)) continue;

                foreach (Match match in Literal.Matches(line))
                {
                    if (!HasChinese(match.Groups[1].Value)) continue;
                    var before = line.Substring(0, match.Index).TrimEnd().TrimEnd('$');
                    if (before.EndsWith("T(") || before.EndsWith("F(") || before.EndsWith("Exception(")) continue;
                    if (baseline.Contains(match.Groups[1].Value)) continue;
                    offenders.Add(relative + "  [" + match.Groups[1].Value + "]");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "hardcoded Chinese in the framework:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>Literals the audits allow, one per line; '#' starts a comment.</summary>
    private static HashSet<string> Baseline() =>
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "string-baseline.txt"))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>A character from the CJK unified ideographs block, without the regex escaping.</summary>
    private static bool HasChinese(string text) =>
        text.Any(character => character >= '\u4e00' && character <= '\u9fff');
}
