using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Companion.Tests.News;

/// <summary>
/// Semantic guards for the 1960s data-only news corpus. The composer knows the result class and
/// championship facts, but it does not know laps led, the exact retirement mechanism, a driver's
/// gender, or whether the player fought the winner directly.
/// </summary>
public sealed class News1960sSemanticGuardTests
{
    private static string CorpusPath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "rules", "news", "1960s.json");

    [Fact]
    public void PlayerFacingCopy_DoesNotAssumeMasculinePronouns()
    {
        var masculine = new Regex(
            @"\b(?:man|men|his|him|he)\b|\bworkman(?:like|'s)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        var offenders = AllStrings(LoadCorpus())
            .Where(row => masculine.IsMatch(row.Text))
            .Select(row => $"{row.Where}: {row.Text}")
            .ToList();

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void ResultCopy_DoesNotInventRaceShapeOrFailureMechanisms()
    {
        JsonObject corpus = LoadCorpus();
        var checks = new (string Section, string Key, string Pattern)[]
        {
            ("bodies", "race.result|win",
                @"flag-to-flag|lights to flag|led every lap|never headed|never troubled|off the line first"),
            ("bodies", "race.result|podium",
                @"behind only|only \{winner\} finished ahead"),
            ("pools", "rivalLine",
                @"\bduel\b|\bscrap with\b|would not be caught"),
            ("bodies", "race.result|dnf-mechanical",
                @"\bengine\b|motor let go|trail of oil|\bcomponent\b|coasted to a halt|failing part|out of the \{year\} running"),
            ("bodies", "race.result|dnf-driver-error",
                @"straw bales|lock-up|\bspin|ran wide|crumpled|beached|\bfence\b|unhurt|ran out of road|panel-beaters|crashed out|spun out"),
        };

        var offenders = new List<string>();
        foreach (var (section, key, pattern) in checks)
        {
            var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            foreach (var row in EntryStrings(corpus, section, key))
            {
                if (regex.IsMatch(row.Text))
                    offenders.Add($"{row.Where}: {row.Text}");
            }
        }

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void EveryPoolReference_ProvidesTheRequestedEraOrDefault()
    {
        JsonObject corpus = LoadCorpus();
        JsonObject pools = corpus["pools"]!.AsObject();
        var offenders = new List<string>();

        foreach (var (bodyKey, bodyNode) in corpus["bodies"]!.AsObject())
        foreach (string era in new[] { "1960s", "default" })
        {
            if (bodyNode![era] is not JsonArray templates)
                continue;

            foreach (JsonNode? templateNode in templates)
            {
                string template = templateNode!.GetValue<string>();
                foreach (Match match in Regex.Matches(template, @"\{pool:([^{}]+)\}"))
                {
                    string poolName = match.Groups[1].Value;
                    if (!pools.TryGetPropertyValue(poolName, out JsonNode? poolNode) ||
                        poolNode is not JsonObject byEra ||
                        (byEra[era] is not JsonArray && byEra["default"] is not JsonArray))
                    {
                        offenders.Add($"body {bodyKey} [{era}] references unresolved pool '{poolName}'");
                    }
                }
            }
        }

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    private static JsonObject LoadCorpus() =>
        JsonNode.Parse(File.ReadAllText(CorpusPath))!.AsObject();

    private static IEnumerable<(string Where, string Text)> AllStrings(JsonObject corpus)
    {
        foreach (string sectionName in new[] { "pools", "bodies" })
        foreach (var (key, entryNode) in corpus[sectionName]!.AsObject())
        foreach (var (era, stringsNode) in entryNode!.AsObject())
        {
            if (era is not ("1960s" or "default") || stringsNode is not JsonArray strings)
                continue;

            foreach (JsonNode? textNode in strings)
                yield return ($"{sectionName} {key} [{era}]", textNode!.GetValue<string>());
        }
    }

    private static IEnumerable<(string Where, string Text)> EntryStrings(
        JsonObject corpus,
        string section,
        string key)
    {
        JsonObject byEra = corpus[section]![key]!.AsObject();
        foreach (string era in new[] { "1960s", "default" })
        {
            if (byEra[era] is not JsonArray strings)
                continue;

            foreach (JsonNode? textNode in strings)
                yield return ($"{section} {key} [{era}]", textNode!.GetValue<string>());
        }
    }
}
