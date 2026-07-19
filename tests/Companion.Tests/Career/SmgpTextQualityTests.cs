using System.IO;

namespace Companion.Tests.Career;

/// <summary>
/// Guards the authored SMGP display text against the double-encoding MOJIBAKE that plagued the
/// PowerShell-assembled corpora (an em-dash / curly quote written as UTF-8, misread through CP1252,
/// then re-saved, surfacing in-app as "â€"" etc.). The tell-tale is 'â'(U+00E2) immediately followed by
/// '€'(U+20AC). Fixed for driver-profiles (regenerated ASCII) + team-profiles (cleaned to real em-dashes);
/// this keeps it from creeping back.
/// </summary>
public sealed class SmgpTextQualityTests
{
    private const string Mojibake = "â€"; // "â€", the double-encoding marker

    [Theory]
    [InlineData("driver-profiles.json")]
    [InlineData("team-profiles.json")]
    [InlineData("rival-quotes.json")]
    [InlineData("pit-crew-advice.json")]
    [InlineData("what-really-happened.json")]
    [InlineData("sponsors.json")]
    public void SmgpAuthoredText_HasNoDoubleEncodingMojibake(string fileName)
    {
        string path = Path.Combine(RepoRoot(), "data", "rules", "smgp", fileName);
        string text = File.ReadAllText(path, System.Text.Encoding.UTF8);
        Assert.False(
            text.Contains(Mojibake, StringComparison.Ordinal),
            $"{fileName} contains double-encoding mojibake (â€…), re-clean it and write UTF-8 (no BOM).");
    }

    // Parse with the SAME reader the app uses, so a mojibake-cleanup that accidentally injects a raw
    // quote/backslash (which would close a JSON string early and break EVERY team) fails here, loudly.
    [Theory]
    [InlineData("driver-profiles.json")]
    [InlineData("team-profiles.json")]
    [InlineData("rival-quotes.json")]
    [InlineData("pit-crew-advice.json")]
    [InlineData("what-really-happened.json")]
    [InlineData("sponsors.json")]
    public void SmgpAuthoredText_IsWellFormedJson(string fileName)
    {
        string path = Path.Combine(RepoRoot(), "data", "rules", "smgp", fileName);
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(System.Text.Json.JsonValueKind.Object, doc.RootElement.ValueKind);
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "data", "rules", "smgp")))
                return dir.FullName;
        throw new DirectoryNotFoundException("Could not find data/rules/smgp above " + AppContext.BaseDirectory);
    }
}
