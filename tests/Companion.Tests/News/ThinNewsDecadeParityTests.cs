using System.Text.RegularExpressions;
using Companion.Core.News;

namespace Companion.Tests.News;

/// <summary>
/// Pins the two formerly thin corpora to the same useful depth as the richer decades and keeps
/// their prose inside the facts exposed by NewsFacts. Exact incident parts, race shape, audience
/// size, and constructor-title state are not available to a data-only corpus.
/// </summary>
public sealed class ThinNewsDecadeParityTests
{
    private static string ShippedNewsDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "rules", "news");

    private static readonly Lazy<NewsArticleBank> Bank = new(() =>
        NewsArticleBank.LoadDirectory(ShippedNewsDirectory));

    [Theory]
    [InlineData("1980s", 10)]
    [InlineData("2000s", 9)]
    public void EveryCauseHasRichDepth_AndPoolsHaveUsefulVariety(
        string era,
        int minimumPoolCount)
    {
        var bodies = Bank.Value.Bodies
            .Where(pair => pair.Value.ContainsKey(era))
            .ToArray();
        Assert.Equal(10, bodies.Length);
        Assert.All(bodies, pair =>
            Assert.InRange(pair.Value[era].Count, 9, 11));

        var pools = Bank.Value.Pools
            .Where(pair => pair.Value.TryGetValue(era, out var fragments) && fragments.Count > 0)
            .ToArray();
        Assert.True(pools.Length >= minimumPoolCount,
            $"{era} has {pools.Length} pools; expected at least {minimumPoolCount}.");
        Assert.All(pools, pair =>
            Assert.True(pair.Value[era].Count >= 8,
                $"{era} pool {pair.Key} has only {pair.Value[era].Count} fragments."));
    }

    [Theory]
    [InlineData("1980s")]
    [InlineData("2000s")]
    public void EraVariantsAreUnique_AndEveryEraPoolIsUsed(string era)
    {
        foreach (var (key, byEra) in Bank.Value.Bodies)
        {
            if (byEra.TryGetValue(era, out var variants))
                AssertUnique($"{era} body {key}", variants);
        }

        var eraPools = Bank.Value.Pools
            .Where(pair => pair.Value.TryGetValue(era, out var fragments) && fragments.Count > 0)
            .ToArray();
        foreach (var (name, byEra) in eraPools)
            AssertUnique($"{era} pool {name}", byEra[era]);

        var referenced = Bank.Value.Bodies.Values
            .Where(byEra => byEra.ContainsKey(era))
            .SelectMany(byEra => byEra[era])
            .SelectMany(template => Regex.Matches(template, @"\{pool:([^{}]+)\}")
                .Select(match => match.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);

        Assert.All(eraPools, pair =>
            Assert.Contains(pair.Key, referenced));
    }

    [Fact]
    public void ThinDecadeBodyDefaults_DoNotDoubleWeightTheSameFallbackCopy()
    {
        NewsArticleBank eighties = NewsArticleBank.Parse(
            File.ReadAllText(Path.Combine(ShippedNewsDirectory, "1980s.json")));
        NewsArticleBank twoThousands = NewsArticleBank.Parse(
            File.ReadAllText(Path.Combine(ShippedNewsDirectory, "2000s.json")));

        string[] duplicates = DefaultBodies(eighties)
            .Intersect(DefaultBodies(twoThousands), StringComparer.Ordinal)
            .ToArray();

        Assert.True(duplicates.Length == 0,
            "1980s/2000s body defaults are double-weighted:" + Environment.NewLine +
            string.Join(Environment.NewLine, duplicates));
    }

    [Fact]
    public void EightiesCopyWorksForTurbo_AndNaturallyAspiratedEntries()
    {
        AssertNoMatch(
            "1980s",
            EraStrings("1980s"),
            @"\b(?:turbo|boost|wastegate)\b");
    }

    [Fact]
    public void TwoThousandsCopyDoesNotInventUnavailableDataOrManufacturerState()
    {
        AssertNoMatch(
            "2000s",
            EraStrings("2000s"),
            @"\b(?:telemetry|manufacturer|mini-sectors?|high-definition|global\s+HD)\b");

        Assert.DoesNotContain(
            Bank.Value.Pools["champLine"]["2000s"],
            fragment => fragment.Contains("{champLeader}", StringComparison.Ordinal));
        AssertNoMatch(
            "2000s closeGood",
            Bank.Value.Pools["closeGood"]["2000s"],
            @"\bpoints\b");
    }

    [Theory]
    [InlineData("1980s")]
    [InlineData("2000s")]
    public void CauseCopyDoesNotInventRaceShapeOrFailureMechanism(string era)
    {
        AssertNoMatch(
            $"{era} win",
            Bodies(era, "race.result|win"),
            @"(?:lights?|flag)[ -]to[ -]flag|led every|every lap|never .*(?:threat|doubt)|\bgap\b|\bdominat\w*|\bprocession\b");
        AssertNoMatch(
            $"{era} podium",
            Bodies(era, "race.result|podium"),
            @"only \{winner\}|ahead of the rest|the only .* ahead");
        AssertNoMatch(
            $"{era} mechanical DNF",
            Bodies(era, "race.result|dnf-mechanical"),
            @"\b(?:engine|hose|seal|smoke|fire|flames?|detonat\w*|gearbox|suspension)\b");
        AssertNoMatch(
            $"{era} mechanical DNF voice",
            Bodies(era, "race.result|dnf-mechanical"),
            @"specific (?:component|mechanism|incident)|no more specific|result (?:identifies|supplies)");
        AssertNoMatch(
            $"{era} driver-error DNF",
            Bodies(era, "race.result|dnf-driver-error"),
            @"\b(?:wall|barriers?|gravel|spin|spun|crash\w*|collision|throttle|replay|onboard)\b");
        AssertNoMatch(
            $"{era} driver-error DNF voice",
            Bodies(era, "race.result|dnf-driver-error"),
            @"specific (?:component|mechanism|incident)|no more specific|result (?:identifies|supplies)");
        AssertNoMatch(
            $"{era} points",
            Bodies(era, "race.result|points"),
            @"\{team\}\s+(?:tally|score)|\{team\}.*adds? to (?:its|the) score");
    }

    [Theory]
    [InlineData("1980s")]
    [InlineData("2000s")]
    public void ExpectedToken_CopyAlsoReadsCleanlyWithTheFallbackValue(string era)
    {
        AssertNoMatch(
            era,
            EraStrings(era),
            @"\b(?:an|the)\s+(?:expected|predicted|projected|forecast)\s+\{expected\}|\bthe\s+\{expected\}");
    }

    [Theory]
    [InlineData("1980s")]
    [InlineData("2000s")]
    public void PlayerCopyIsGenderNeutral(string era)
    {
        AssertNoMatch(
            era,
            EraStrings(era),
            @"\b(?:man|men|he|him|his)\b");
    }

    private static IReadOnlyList<string> Bodies(string era, string key) =>
        Bank.Value.Bodies[key][era];

    private static IEnumerable<string> DefaultBodies(NewsArticleBank bank) =>
        bank.Bodies.Values
            .Where(byEra => byEra.ContainsKey(NewsArticleBank.DefaultEra))
            .SelectMany(byEra => byEra[NewsArticleBank.DefaultEra]);

    private static IEnumerable<string> EraStrings(string era) =>
        Bank.Value.Bodies.Values
            .Where(byEra => byEra.ContainsKey(era))
            .SelectMany(byEra => byEra[era])
            .Concat(Bank.Value.Pools.Values
                .Where(byEra => byEra.ContainsKey(era))
                .SelectMany(byEra => byEra[era]));

    private static void AssertNoMatch(
        string label,
        IEnumerable<string> values,
        string pattern)
    {
        string[] offenders = values
            .Where(value => Regex.IsMatch(
                value,
                pattern,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            .ToArray();

        Assert.True(offenders.Length == 0,
            $"{label} contains unsupported copy:{Environment.NewLine}" +
            string.Join(Environment.NewLine, offenders));
    }

    private static void AssertUnique(string label, IReadOnlyList<string> values)
    {
        string[] normalized = values
            .Select(value => Regex.Replace(value.Trim(), @"\s+", " ").ToUpperInvariant())
            .ToArray();
        int uniqueCount = normalized.Distinct(StringComparer.Ordinal).Count();
        Assert.True(
            uniqueCount == normalized.Length,
            $"{label} has {normalized.Length - uniqueCount} normalized duplicate(s).");
    }
}
