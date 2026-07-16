using Companion.Core.HistoryArchive;
using Companion.ViewModels.Services;
using Xunit;

namespace Companion.Tests.HistoryArchive;

/// <summary>
/// The history archive over the REAL shipped reference set (60 season files + the authored
/// eras/subjects/aliases) — the shipped data must validate, and the computed entity profiles
/// must reproduce canonical f1db facts exactly (aggregation correctness, zero fabrication).
/// </summary>
public class HistoryArchiveTests
{
    private static readonly string HistoryDirectory =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "history");

    private static readonly Lazy<HistoryArchiveData> Reference =
        new(() => HistoryArchiveData.Load(HistoryDirectory));

    private static readonly Lazy<HistoryArchiveIndex> Index = new(() =>
        HistoryEntityIndex.Build(
            new HistoricalSeasonStore(HistoryDirectory).ForYear,
            Reference.Value));

    [Fact]
    public void TheShippedReferenceDataLoadsAndValidates()
    {
        var data = Reference.Value;
        Assert.False(data.IsEmpty);
        data.Validate();

        Assert.Equal(8, data.Eras.Count);
        Assert.Equal(1967, data.Eras.Min(e => e.FromYear));
        Assert.Equal(2026, data.Eras.Max(e => e.ToYear));
        Assert.Equal(23, data.Subjects.Count);
        Assert.Equal(110, data.Teams.Count);
        Assert.All(data.Eras, e => Assert.NotEmpty(e.Sources));
        Assert.All(data.Subjects, s => Assert.NotEmpty(s.Sources));
    }

    [Fact]
    public void EveryYearResolvesToExactlyOneEra()
    {
        var data = Reference.Value;
        for (var year = 1967; year <= 2026; year++)
        {
            var matches = data.Eras.Count(e => year >= e.FromYear && year <= e.ToYear);
            Assert.True(matches == 1, $"year {year} matched {matches} eras");
        }
        Assert.Null(data.EraForYear(1949));
    }

    [Fact]
    public void TheIndexCoversTheFullDocumentedSpan()
    {
        var index = Index.Value;
        Assert.Equal(60, index.YearsCovered.Count);
        Assert.Equal(1967, index.YearsCovered[0]);
        Assert.Equal(2026, index.YearsCovered[^1]);
        Assert.True(index.Drivers.Count > 400, $"expected hundreds of drivers, got {index.Drivers.Count}");
        Assert.True(index.Circuits.Count > 50, $"expected dozens of circuits, got {index.Circuits.Count}");
    }

    [Fact]
    public void DriverProfilesReproduceCanonicalFacts()
    {
        // Ayrton Senna, fully inside the documented span (1984-1994): the aggregation must
        // land exactly on the canonical record for wins and titles.
        var senna = Assert.Single(Index.Value.Drivers, d => d.Name == "Ayrton Senna");
        Assert.Equal(41, senna.Wins);
        Assert.Equal([1988, 1990, 1991], senna.ChampionshipYears.OrderBy(y => y));
        Assert.Equal(1984, senna.FirstYear);
        Assert.Equal(1994, senna.LastYear);
        Assert.InRange(senna.Starts, 155, 165);
        Assert.InRange(senna.Podiums, 75, 85);
        Assert.InRange(senna.FastestLaps, 15, 25);
        Assert.Equal(41, senna.WinList.Count);
        Assert.True(senna.Stints.Count >= 3, "Toleman, Lotus, McLaren at minimum");
    }

    [Fact]
    public void TeamProfilesAggregateAcrossAliasStringsWithoutSilentMerges()
    {
        var index = Index.Value;

        // McLaren raced under engine-suffixed strings across eras; the identity table must
        // gather them under one canonical profile.
        var mclaren = Assert.Single(index.Teams, t => t.Canonical.Contains("McLaren"));
        Assert.Contains(1988, mclaren.ConstructorsChampionshipYears);
        Assert.True(mclaren.Wins > 100, $"McLaren wins {mclaren.Wins}");
        Assert.True(mclaren.DriversFielded > 20);

        // Lineage is a LINK, not a merge: connected teams remain separate profiles.
        foreach (var team in index.Teams)
        {
            foreach (var link in team.Lineage)
            {
                Assert.NotEqual(team.Canonical, link.RelatedTo);
            }
        }
    }

    [Fact]
    public void CircuitProfilesCarryEveryDocumentedEdition()
    {
        var index = Index.Value;
        var monaco = index.Circuits.Where(c => c.Name.Contains("Monaco") || c.Place.Contains("Monte")).ToList();
        Assert.NotEmpty(monaco);
        Assert.True(monaco.Sum(c => c.Editions.Count) >= 50,
            "Monaco should have most of 60 editions across its layouts");
        Assert.All(index.Circuits, c => Assert.NotEmpty(c.Editions));
    }

    [Fact]
    public void TheTimelineMergesChampionsErasAndSubjectsWithProvenance()
    {
        var timeline = Index.Value.Timeline;

        var champions = timeline.Where(t => t.Category == "championship").ToList();
        Assert.InRange(champions.Count, 58, 60); // an in-progress reference year has no champion yet
        Assert.Equal(8, timeline.Count(t => t.Category == "era"));
        Assert.Equal(23, timeline.Count(t =>
            t.Category is "technology" or "regulation" or "safety"));
        Assert.All(timeline, t => Assert.Equal("verifiedHistorical", t.Provenance));
        Assert.All(champions, t => Assert.StartsWith("season:", t.RelatedKey));

        var y1976 = Assert.Single(champions, t => t.Year == 1976);
        Assert.Contains("James Hunt", y1976.Summary);
        Assert.Contains("Ferrari", y1976.Summary); // constructors champion that year
    }

    [Fact]
    public void UnknownTeamStringsStayTheirOwnHonestEntities()
    {
        // Build against a reference with an EMPTY alias table: nothing may merge, and
        // every profile flags itself incomplete rather than pretending.
        var bare = HistoryEntityIndex.Build(
            new HistoricalSeasonStore(HistoryDirectory).ForYear,
            HistoryArchiveData.Empty);
        Assert.True(bare.Teams.Count >= 110);
        Assert.All(bare.Teams, t => Assert.False(t.IsComplete));
    }

    [Fact]
    public void AMissingHistoryDirectoryDegradesToEmptyNotAnError()
    {
        var data = HistoryArchiveData.Load(Path.Combine(HistoryDirectory, "does-not-exist"));
        Assert.True(data.IsEmpty);

        var index = HistoryEntityIndex.Build(_ => null, data);
        Assert.Empty(index.Drivers);
        Assert.Empty(index.Timeline);
    }
}
