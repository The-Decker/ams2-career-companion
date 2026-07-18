using System.Text.RegularExpressions;

namespace Companion.Tests.Guards;

/// <summary>
/// The em-dash rule (owner decision, 2026-07-17): "—" (U+2014) is banned from everything
/// user-visible in the app — XAML views, JSON content (<c>data/rules</c>, <c>packs</c>,
/// <c>data/history</c>, <c>data/ams2</c>), and C# string literals — because readers judge the
/// writing by that one character. It stays allowed only in code comments, which the user never
/// sees. This guard scans the user-visible surface with comments stripped, so the ban holds
/// mechanically instead of by vigilance.
///
/// <para>Replacement style, per the owner: in stories/writing the em-dash becomes a comma;
/// headings and labels take a colon; empty-value glyphs take a plain hyphen "-".</para>
/// </summary>
public sealed class NoEmDashGuardTests
{
    private static readonly string EmDash = ((char)0x2014).ToString(); // the banned character, built from its code point so this file self-scans clean

    private static readonly Regex XmlComment = new("(?s)<!--.*?-->", RegexOptions.Compiled);

    private static readonly Regex CsBlockComment = new("(?s)/\\*.*?\\*/", RegexOptions.Compiled);

    private static readonly Regex CsFullLineComment = new("(?m)^\\s*//.*$", RegexOptions.Compiled);

    [Fact]
    public void No_em_dash_in_xaml_views()
    {
        string root = RepoRoot();
        var offenders = new List<string>();
        foreach (string file in Directory.EnumerateFiles(
                     Path.Combine(root, "src", "Companion.App"), "*.xaml", SearchOption.AllDirectories))
        {
            string visible = XmlComment.Replace(File.ReadAllText(file), "");
            if (visible.Contains(EmDash, StringComparison.Ordinal))
                offenders.Add(Rel(root, file));
        }

        AssertOffenders(offenders, "XAML views");
    }

    [Fact]
    public void No_em_dash_in_json_content()
    {
        string root = RepoRoot();
        var offenders = new List<string>();
        string[] trees =
        [
            Path.Combine(root, "data", "rules"),
            Path.Combine(root, "data", "history"),
            Path.Combine(root, "data", "ams2"),
            Path.Combine(root, "packs"),
        ];
        foreach (string tree in trees)
        {
            if (!Directory.Exists(tree))
                continue;
            foreach (string file in Directory.EnumerateFiles(tree, "*.json", SearchOption.AllDirectories))
            {
                if (File.ReadAllText(file).Contains(EmDash, StringComparison.Ordinal))
                    offenders.Add(Rel(root, file));
            }
        }

        AssertOffenders(offenders, "JSON content");
    }

    [Fact]
    public void No_em_dash_in_csharp_string_literals()
    {
        // Comments are exempt (users never see them): block comments and full-line // comments
        // are stripped before scanning. A trailing // comment carrying an em-dash WILL flag —
        // keep em-dashes out of those too; the exemption is for doc/comment lines, not code lines.
        string root = RepoRoot();
        var offenders = new List<string>();
        string[] trees =
        [
            Path.Combine(root, "src"),
            Path.Combine(root, "tests"),
        ];
        foreach (string tree in trees)
        {
            foreach (string file in Directory.EnumerateFiles(tree, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
                    file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                    continue;

                string code = CsFullLineComment.Replace(
                    CsBlockComment.Replace(File.ReadAllText(file), ""), "");
                if (code.Contains(EmDash, StringComparison.Ordinal))
                    offenders.Add(Rel(root, file));
            }
        }

        AssertOffenders(offenders, "C# string literals");
    }

    private static void AssertOffenders(List<string> offenders, string surface) =>
        Assert.True(
            offenders.Count == 0,
            $"The em-dash rule (owner, 2026-07-17): no \"{EmDash}\" in user-visible {surface}. " +
            "Prose gets a comma, headings a colon, glyphs a hyphen. Offenders: " +
            string.Join("; ", offenders));

    private static string Rel(string root, string file) =>
        Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');

    /// <summary>The repo root, found by walking up from the test output dir until
    /// <c>Companion.slnx</c> appears — the guard scans the SOURCE trees (not the copied
    /// fixtures) so it also covers XAML and the packs/history JSON the csproj does not copy.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Companion.slnx")))
            dir = dir.Parent;
        Assert.True(dir is not null, "Companion.slnx not found above the test output directory.");
        return dir.FullName;
    }
}
