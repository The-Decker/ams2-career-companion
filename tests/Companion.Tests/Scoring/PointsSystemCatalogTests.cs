using Companion.Core.Numerics;
using Companion.Core.Scoring;

namespace Companion.Tests.Scoring;

/// <summary>
/// Parses the real season-rules catalog (data/rules/f1-points-systems.json, linked into the
/// test output at Fixtures/rules/) and checks that season resolution produces the shapes the
/// engine consumes.
/// </summary>
public class PointsSystemCatalogTests
{
    private static readonly Lazy<PointsSystemCatalog> RealCatalog = new(() =>
        PointsSystemCatalog.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "rules", "f1-points-systems.json"))));

    private static IReadOnlyList<Rational> Table(params long[] points) =>
        points.Select(p => new Rational(p)).ToArray();

    // ---------- parsing the real catalog ----------

    [Fact]
    public void Parse_RealCatalog_HasEverySeasonFrom1950Through2026()
    {
        var catalog = RealCatalog.Value;

        Assert.Equal(77, catalog.Seasons.Count);
        for (int year = 1950; year <= 2026; year++)
            Assert.True(catalog.Seasons.ContainsKey(year), $"Season {year} missing from the catalog.");
    }

    [Fact]
    public void GetSeason_UnknownYear_Throws() =>
        Assert.Throws<KeyNotFoundException>(() => RealCatalog.Value.GetSeason(1949));

    [Fact]
    public void Parse_1950_ReadsFiftiesRulesExactly()
    {
        var season = RealCatalog.Value.GetSeason(1950);

        Assert.Equal(Table(8, 6, 4, 3, 2), season.RacePoints);
        Assert.Equal(SharedDrivePolicy.Split, season.SharedDrivePolicy);
        Assert.Null(season.Constructors); // no constructors championship before 1958

        Assert.NotNull(season.FastestLap);
        Assert.Equal(Rational.One, season.FastestLap!.Points);
        Assert.True(season.FastestLap.SplitOnTie);
        Assert.Equal(FastestLapEligibility.Any, season.FastestLap.Eligibility);
        Assert.False(season.FastestLap.CountsForConstructors);

        var system = season.ResolvePointsSystem(7); // 1950 had 7 rounds
        Assert.Null(system.Constructors);
        Assert.Equal(
            new[] { new BestNSegment { FromRound = 1, ToRound = 7, Count = 4 } },
            system.DriversBestN!.Segments);
    }

    [Fact]
    public void Parse_2019_ReadsTopTenFastestLapRule()
    {
        var fastestLap = RealCatalog.Value.GetSeason(2019).FastestLap;

        Assert.NotNull(fastestLap);
        Assert.Equal(FastestLapEligibility.ClassifiedTopTen, fastestLap!.Eligibility);
        Assert.False(fastestLap.SplitOnTie);
        Assert.True(fastestLap.CountsForConstructors);
        Assert.Equal(Rational.One, fastestLap.Points);
    }

    [Fact]
    public void Parse_RoundOverrides_CarryRationalFactorsAndConstructorExclusions()
    {
        var spain1975 = RealCatalog.Value.GetSeason(1975).RoundOverrides.Single(o => o.GrandPrix == "spain");
        Assert.Equal(Rational.Half, spain1975.PointsFactor);

        var abuDhabi2014 = RealCatalog.Value.GetSeason(2014).RoundOverrides.Single(o => o.GrandPrix == "abu-dhabi");
        Assert.Equal(new Rational(2), abuDhabi2014.PointsFactor);

        var indy1958 = RealCatalog.Value.GetSeason(1958).RoundOverrides.Single(o => o.GrandPrix == "indianapolis");
        Assert.False(indy1958.CountsForConstructors!.Value);
    }

    [Fact]
    public void Parse_2022_ReadsSprintAndAlternateTables()
    {
        var season = RealCatalog.Value.GetSeason(2022);

        Assert.Equal(Table(8, 7, 6, 5, 4, 3, 2, 1), season.SprintPoints!);
        Assert.NotNull(season.AlternateRaceTables);
        Assert.Equal(3, season.AlternateRaceTables!.Count);
        Assert.Equal(Table(6, 4, 3, 2, 1), season.AlternateRaceTables["reduced-2laps-25pct"]);
        Assert.Equal(Table(13, 10, 8, 6, 5, 4, 3, 2, 1), season.AlternateRaceTables["reduced-25-50pct"]);
        Assert.Equal(Table(19, 14, 12, 10, 8, 6, 4, 3, 2, 1), season.AlternateRaceTables["reduced-50-75pct"]);
    }

    [Fact]
    public void Parse_1997_ListsSchumacherAsExcluded()
    {
        var season = RealCatalog.Value.GetSeason(1997);

        Assert.Contains("michael-schumacher", season.ExcludedDrivers);

        var definition = season.ResolveScoringDefinition(17); // 1997 had 17 rounds
        Assert.Contains("michael-schumacher", definition.ExcludedDrivers);
        Assert.Equal(17, definition.RoundCount);
    }

    [Fact]
    public void Parse_1961_ResolvesTheConstructorsRacePointsOverride()
    {
        var season = RealCatalog.Value.GetSeason(1961);

        Assert.Equal(Table(9, 6, 4, 3, 2, 1), season.RacePoints);
        Assert.Equal(Table(8, 6, 4, 3, 2, 1), season.Constructors!.RacePoints);

        var system = season.ResolvePointsSystem(8); // 1961 had 8 rounds
        Assert.NotNull(system.Constructors);
        Assert.True(system.Constructors!.BestCarOnly);
        Assert.Equal(Table(8, 6, 4, 3, 2, 1), system.Constructors.RacePoints); // win-8 constructors scale
        Assert.Equal(Table(9, 6, 4, 3, 2, 1), system.RacePoints);              // win-9 drivers scale
    }

    [Fact]
    public void Parse_2018_CarriesTheForceIndiaExclusionAndEntitySplit()
    {
        var season = RealCatalog.Value.GetSeason(2018);

        Assert.Contains("force-india+mercedes", season.ExcludedConstructors);

        var split = Assert.Single(season.ConstructorEntitySplits);
        Assert.Equal("force-india", split.Constructor);
        Assert.Equal("mercedes", split.Engine);
        Assert.Equal(13, split.FromRound);
        Assert.Equal("racing-point-force-india+mercedes", split.NewId);

        var definition = season.ResolveScoringDefinition(21); // 2018 had 21 rounds
        Assert.Contains("force-india+mercedes", definition.ExcludedConstructors);
    }

    [Fact]
    public void Parse_2007_ListsMcLarenAsExcludedConstructor()
    {
        var definition = RealCatalog.Value.GetSeason(2007).ResolveScoringDefinition(17);

        Assert.Contains("mclaren+mercedes", definition.ExcludedConstructors);
        Assert.Empty(definition.ExcludedDrivers); // the drivers kept their championships
    }

    [Theory]
    [InlineData(1953, 9, "juan-manuel-fangio", "1/2")]    // Reims fastest-lap credit
    [InlineData(1956, 8, "juan-manuel-fangio", "-3/2")]   // Monaco second shared car
    [InlineData(1957, 8, "peter-collins", "-3/2")]        // British GP stewards' reallocation
    [InlineData(1957, 8, "maurice-trintignant", "3/2")]   // counterpart of the Collins reallocation
    public void Parse_DriverPointsAdjustments_ResolveWithExactRationalDeltas(
        int year, int roundCount, string driverId, string delta)
    {
        var definition = RealCatalog.Value.GetSeason(year).ResolveScoringDefinition(roundCount);

        Assert.Equal(Rational.Parse(delta), definition.DriverPointsAdjustments[driverId]);
        Assert.Empty(definition.ConstructorPointsAdjustments); // 1950s corrections are driver-side only
    }

    [Theory]
    [InlineData(1995, 17, "benetton+renault", "-10")]           // Brazil fuel ruling
    [InlineData(1995, 17, "williams+renault", "-6")]            // same ruling, Coulthard's P2
    [InlineData(2000, 17, "mclaren+mercedes", "-10")]           // Austria missing ECU seal
    [InlineData(2020, 17, "racing-point+bwt-mercedes", "-15")]  // brake-duct penalty
    public void Parse_ConstructorPointsAdjustments_ResolveWithExactRationalDeltas(
        int year, int roundCount, string constructorId, string delta)
    {
        var definition = RealCatalog.Value.GetSeason(year).ResolveScoringDefinition(roundCount);

        Assert.Equal(Rational.Parse(delta), definition.ConstructorPointsAdjustments[constructorId]);
        Assert.Empty(definition.DriverPointsAdjustments); // these penalties never touched the drivers
    }

    [Fact]
    public void Parse_PointsAdjustments_EveryEntryDocumentsItsReason()
    {
        foreach (var (year, season) in RealCatalog.Value.Seasons)
        {
            foreach (var adjustment in season.PointsAdjustments)
            {
                Assert.False(string.IsNullOrWhiteSpace(adjustment.Reason),
                    $"Season {year}: points adjustment without a documented reason.");
                Assert.True(adjustment.Driver is null ^ adjustment.Constructor is null,
                    $"Season {year}: adjustment must target exactly one of driver/constructor.");
            }
        }
    }

    [Fact]
    public void Parse_1979_ConstructorsCountEveryCar()
    {
        var season = RealCatalog.Value.GetSeason(1979);

        Assert.False(season.Constructors!.BestCarOnly);
        Assert.Null(season.Constructors.RacePoints); // drivers table applies

        var system = season.ResolvePointsSystem(15); // 1979 had 15 rounds, split 7 + 8
        Assert.False(system.Constructors!.BestCarOnly);
        Assert.Null(system.Constructors.RacePoints);
    }

    // ---------- season resolution ----------

    [Fact]
    public void Resolve_1967_ProducesTheSplitSeasonSegments()
    {
        var system = RealCatalog.Value.GetSeason(1967).ResolvePointsSystem(11);

        Assert.Equal(Table(9, 6, 4, 3, 2, 1), system.RacePoints);

        var expected = new[]
        {
            new BestNSegment { FromRound = 1, ToRound = 6, Count = 5 },
            new BestNSegment { FromRound = 7, ToRound = 11, Count = 4 },
        };
        Assert.Equal(expected, system.DriversBestN!.Segments);

        Assert.NotNull(system.Constructors);
        Assert.True(system.Constructors!.BestCarOnly);
        Assert.Equal(expected, system.Constructors.BestN!.Segments); // sameAsDrivers
    }

    [Fact]
    public void Resolve_1981_ProducesWholeSeasonBestEleven()
    {
        var system = RealCatalog.Value.GetSeason(1981).ResolvePointsSystem(15);

        Assert.Equal(
            new[] { new BestNSegment { FromRound = 1, ToRound = 15, Count = 11 } },
            system.DriversBestN!.Segments);

        Assert.NotNull(system.Constructors);
        Assert.False(system.Constructors!.BestCarOnly);
        Assert.Null(system.Constructors.BestN); // constructors count every round from 1981
    }

    [Fact]
    public void Resolve_SplitSeason_RoundCountMismatch_Throws()
    {
        // 1967's rule covers 6 + 5 rounds; resolving against a 12-round season must fail loudly.
        var season = RealCatalog.Value.GetSeason(1967);

        var exception = Assert.Throws<InvalidOperationException>(() => season.ResolvePointsSystem(12));
        Assert.Contains("12", exception.Message);
    }

    [Fact]
    public void Resolve_SameAsDrivers_MirrorsTheDriversBestNRule()
    {
        // 1958: whole-season best 6, constructors declared "sameAsDrivers".
        var system = RealCatalog.Value.GetSeason(1958).ResolvePointsSystem(11);

        var expected = new[] { new BestNSegment { FromRound = 1, ToRound = 11, Count = 6 } };
        Assert.Equal(expected, system.DriversBestN!.Segments);

        Assert.NotNull(system.Constructors);
        Assert.True(system.Constructors!.BestCarOnly);
        Assert.NotNull(system.Constructors.BestN);
        Assert.Equal(expected, system.Constructors.BestN!.Segments);
    }
}
