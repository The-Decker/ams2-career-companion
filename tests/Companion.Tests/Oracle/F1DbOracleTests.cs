using System.Globalization;
using System.Text;
using System.Text.Json;
using Companion.Core.Json;
using Companion.Core.Numerics;
using Companion.Core.Scoring;

namespace Companion.Tests.Oracle;

/// <summary>
/// The f1db oracle suite (docs/dev/oracle-fixtures.md): replays every generated season fixture
/// (Fixtures/f1db/&lt;year&gt;.json) through the points engine using the real rules catalog and
/// asserts the Final standings snapshot equals the official f1db standings.
///
/// Comparison contract (v2):
///  - every expected driver/constructor appears with |counted − expected| &lt; 0.01 points;
///  - positions compare tie-tolerantly: expected rows are grouped by points value, and the
///    engine position must fall within [groupMinExpectedPosition, min + groupSize − 1] —
///    dead-heat ranking conventions changed across eras (pre-2000 shared positions,
///    later countback order), so any permutation within a points tie is official-equivalent;
///  - null expected positions skip the position check (excluded competitors' EX rows:
///    1997 Schumacher keeps his points, 2007 McLaren's counted points arrive pre-zeroed);
///  - engine competitors absent from the expected list must have zero counted points;
///  - all mismatches for a season are collected into one failure, each naming the season,
///    the competitor, expected vs actual points and position, plus the engine's per-round
///    scores and dropped-rounds list — the debugging surface for rules-data bugs.
/// </summary>
public class F1DbOracleTests
{
    private const double PointsTolerance = 0.01;

    /// <summary>Sentinel theory case emitted when no fixtures exist, so the suite fails loudly
    /// instead of going green (or erroring with xunit's "no data" message).</summary>
    private const string NoFixturesSentinel = "‹no fixtures generated›";

    private static string FixturesDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "f1db");

    private static string CatalogPath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "rules", "f1-points-systems.json");

    private static readonly Lazy<PointsSystemCatalog> Catalog = new(() =>
    {
        if (!File.Exists(CatalogPath))
            throw new FileNotFoundException(
                $"Rules catalog not found at '{CatalogPath}'. The test project links " +
                "data/rules/f1-points-systems.json into the output — rebuild tests/Companion.Tests.",
                CatalogPath);
        return PointsSystemCatalog.Parse(File.ReadAllText(CatalogPath));
    });

    public static TheoryData<string> SeasonFixtureNames()
    {
        var data = new TheoryData<string>();

        string[] files = Directory.Exists(FixturesDirectory)
            ? Directory.GetFiles(FixturesDirectory, "*.json")
            : [];

        if (files.Length == 0)
        {
            data.Add(NoFixturesSentinel);
            return data;
        }

        foreach (string file in files.OrderBy(f => f, StringComparer.Ordinal))
            data.Add(Path.GetFileNameWithoutExtension(file));

        return data;
    }

    [Theory]
    [MemberData(nameof(SeasonFixtureNames))]
    public void Season_MatchesOfficialF1DbStandings(string seasonFixture)
    {
        if (seasonFixture == NoFixturesSentinel)
            Assert.Fail(
                $"No f1db oracle fixtures found under '{FixturesDirectory}' — no fixtures generated yet. " +
                "Run tools/Companion.FixtureGen <f1db.db> <catalog.json> tests/Companion.Tests/Fixtures/f1db " +
                "and rebuild the test project so the fixtures copy to the output.");

        if (!int.TryParse(seasonFixture, NumberStyles.None, CultureInfo.InvariantCulture, out int year))
            Assert.Fail(
                $"Fixture file '{seasonFixture}.json' under '{FixturesDirectory}' does not name a season " +
                "year — oracle fixtures must be named '<year>.json' (docs/dev/oracle-fixtures.md).");

        var fixture = LoadFixture(seasonFixture);
        var failures = new List<string>();

        if (fixture.Year != year)
            failures.Add(
                $"[{year}] fixture file '{seasonFixture}.json' declares \"year\": {fixture.Year} — " +
                "file name and payload disagree (FixtureGen bug). Comparing using the file-name year.");

        SeasonScoringDefinition definition;
        try
        {
            definition = Catalog.Value.GetSeason(year).ResolveScoringDefinition(fixture.RoundCount);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or JsonException)
        {
            Assert.Fail(
                $"[{year}] the rules catalog could not resolve a {fixture.RoundCount}-round season: " +
                $"{ex.Message} (rules-data bug in data/rules/f1-points-systems.json).");
            return;
        }

        SeasonStandingsResult result;
        try
        {
            result = StandingsEngine.ComputeSeason(definition, F1DbFixtureMapper.MapRounds(fixture));
        }
        catch (ArgumentException ex)
        {
            Assert.Fail(
                $"[{year}] the engine rejected the fixture/rules combination: {ex.Message} " +
                "(either a FixtureGen bug or a rules-data bug).");
            return;
        }

        var final = result.Final;

        Compare(
            year, "driver",
            fixture.ExpectedDrivers,
            final.Drivers.Select(d => new ActualCompetitor(
                d.DriverId, d.Position, d.GrossPoints, d.CountedPoints, d.RoundScores, d.Dropped,
                d.AdjustmentPoints, d.Excluded)).ToList(),
            failures);

        if (fixture.ExpectedConstructors is { } expectedConstructors)
        {
            if (final.Constructors is null)
                failures.Add(
                    $"[{year}] fixture expects a constructors championship ({expectedConstructors.Count} entries) " +
                    "but the season's rules define none — catalog gap in data/rules/f1-points-systems.json.");
            else
                Compare(
                    year, "constructor",
                    expectedConstructors,
                    final.Constructors.Select(c => new ActualCompetitor(
                        c.ConstructorId, c.Position, c.GrossPoints, c.CountedPoints, c.RoundScores, c.Dropped,
                        c.AdjustmentPoints, c.Excluded)).ToList(),
                    failures);
        }
        else if (final.Constructors is not null)
        {
            failures.Add(
                $"[{year}] the season's rules define a constructors championship but the fixture has no " +
                "expectedConstructors — FixtureGen gap; the constructors standings went unverified.");
        }

        if (failures.Count > 0)
        {
            var message = new StringBuilder();
            message.AppendLine(
                $"f1db oracle: season {year} has {failures.Count} standings mismatch(es). " +
                "These are usually rules-data bugs in data/rules/f1-points-systems.json — " +
                "debug with the per-round scores and dropped-rounds lists below.");
            foreach (string failure in failures)
            {
                message.AppendLine();
                message.AppendLine(failure);
            }
            Assert.Fail(message.ToString());
        }
    }

    // ---------- comparison ----------

    /// <summary>Championship-agnostic view of one engine standing (driver or constructor).</summary>
    private sealed record ActualCompetitor(
        string Id,
        int? Position,
        Rational GrossPoints,
        Rational CountedPoints,
        IReadOnlyList<RoundScore> RoundScores,
        IReadOnlyList<DroppedResult> Dropped,
        Rational AdjustmentPoints,
        bool Excluded);

    private static void Compare(
        int year,
        string kind,
        IReadOnlyList<F1DbExpectedStanding> expected,
        IReadOnlyList<ActualCompetitor> actual,
        List<string> failures)
    {
        var actualById = actual.ToDictionary(a => a.Id, StringComparer.Ordinal);
        var expectedIds = expected.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);

        // Tie-tolerant positions (contract v2): dead-heat ranking conventions vary by era
        // (pre-2000 shared positions with gaps, later countback/chronology order), so group
        // the EXPECTED rows by points value — exact double equality is safe within one
        // fixture file — and accept any engine position inside the group's span. Rows with
        // null expected positions occupy no ranking slot and never widen a span.
        var tieGroups = expected
            .Where(e => e.Position is not null)
            .GroupBy(e => e.Points)
            .ToDictionary(g => g.Key, g => (Min: g.Min(e => e.Position!.Value), Size: g.Count()));

        foreach (var exp in expected)
        {
            if (!actualById.TryGetValue(exp.Id, out var act))
            {
                failures.Add(
                    $"[{year}] {kind} '{exp.Id}': expected points={FormatPoints(exp.Points)} " +
                    $"position={FormatPosition(exp.Position)}, but the engine produced no standing for " +
                    "this competitor at all.");
                continue;
            }

            bool pointsMatch = Math.Abs(act.CountedPoints.ToDouble() - exp.Points) < PointsTolerance;

            // Null expected positions skip the check (excluded competitors' EX rows —
            // 1997 Schumacher, 2007 McLaren — carry no official position).
            bool positionMatch = true;
            string allowedSpan = "";
            if (exp.Position is int expectedPosition)
            {
                var (min, size) = tieGroups[exp.Points];
                int max = min + size - 1;
                positionMatch = act.Position is int enginePosition
                                && enginePosition >= min && enginePosition <= max;
                allowedSpan = size > 1
                    ? $" (tie group of {size} on {FormatPoints(exp.Points)} points allows positions {min}–{max})"
                    : $" (untied: position must be exactly {expectedPosition})";
            }

            if (pointsMatch && positionMatch)
                continue;

            var mismatched = new List<string>(2);
            if (!pointsMatch)
                mismatched.Add("points");
            if (!positionMatch)
                mismatched.Add("position");

            failures.Add(
                $"[{year}] {kind} '{exp.Id}' mismatch on {string.Join(" and ", mismatched)}: " +
                $"expected points={FormatPoints(exp.Points)} position={FormatPosition(exp.Position)}" +
                (positionMatch ? "" : allowedSpan) + "; " +
                $"actual counted={FormatRational(act.CountedPoints)} position={FormatActualPosition(act)}." +
                Diagnostics(act));
        }

        // f1db may omit pointless entrants; the engine never omits anyone who raced —
        // so anything the oracle doesn't list must have scored nothing that counted.
        foreach (var act in actual)
        {
            if (expectedIds.Contains(act.Id) || act.CountedPoints.IsZero)
                continue;

            failures.Add(
                $"[{year}] {kind} '{act.Id}' is absent from the f1db expected standings, so its counted " +
                $"points must be zero, but the engine counted {FormatRational(act.CountedPoints)}." +
                Diagnostics(act));
        }
    }

    /// <summary>The debugging surface: every round's score plus what best-N dropped.</summary>
    private static string Diagnostics(ActualCompetitor act)
    {
        string roundScores = act.RoundScores.Count == 0
            ? "(none)"
            : string.Join(", ", act.RoundScores.OrderBy(s => s.Round).Select(s => $"R{s.Round}={s.Points}"));

        string dropped = act.Dropped.Count == 0
            ? "(none)"
            : string.Join(", ", act.Dropped.Select(d => $"R{d.Round} (-{d.PointsDropped})"));

        return
            $"\n      per-round scores: {roundScores}" +
            $"\n      gross={FormatRational(act.GrossPoints)} counted={FormatRational(act.CountedPoints)}" +
            (act.AdjustmentPoints.IsZero
                ? ""
                : $"\n      season points adjustment: {FormatRational(act.AdjustmentPoints)} (already in counted)") +
            $"\n      dropped rounds: {dropped}" +
            (act.Excluded ? "\n      (excluded from the championship classification)" : "");
    }

    // ---------- loading ----------

    private static F1DbSeasonFixture LoadFixture(string seasonFixture)
    {
        string path = Path.Combine(FixturesDirectory, seasonFixture + ".json");
        return JsonSerializer.Deserialize<F1DbSeasonFixture>(File.ReadAllText(path), CoreJson.Options)
               ?? throw new JsonException($"Fixture '{path}' deserialized to null.");
    }

    // ---------- formatting ----------

    private static string FormatPoints(double points) =>
        points.ToString("0.###", CultureInfo.InvariantCulture);

    private static string FormatRational(Rational value) =>
        value.IsInteger
            ? value.ToString()
            : $"{value} ({value.ToDouble().ToString("0.###", CultureInfo.InvariantCulture)})";

    private static string FormatPosition(int? position) =>
        position?.ToString(CultureInfo.InvariantCulture) ?? "(none)";

    private static string FormatActualPosition(ActualCompetitor act) =>
        act.Excluded ? "(excluded)" : FormatPosition(act.Position);
}
