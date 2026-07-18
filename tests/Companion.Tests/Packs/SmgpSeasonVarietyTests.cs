using Companion.Core.Grid;
using Companion.Core.Packs;
using Companion.Core.Smgp;

namespace Companion.Tests.Packs;

/// <summary>
/// SMGP season-to-season variety (Mike: "the second year is all the races random and the weather
/// much different than the first season"). Season 1 is the authored baseline; season 2+ gets a
/// seeded shuffle (Monaco kept as the finale) + fresh weather. The load-bearing property is that
/// the transform is FOLD-INERT: it only moves display/staging fields, so the resolved grid, and
/// therefore the sim, the fold, and every replay, is identical to the un-varied pack.
/// </summary>
public sealed class SmgpSeasonVarietyTests
{
    private const long Seed = 20260711;

    private static string PackDirectory =>
        Path.Combine(AppContext.BaseDirectory, "packs", "smgp-1");

    private static readonly Lazy<SeasonPack> BasePack = new(() => PackLoader.Parse(
        Read("pack.json"), Read("season.json"), Read("teams.json"),
        Read("drivers.json"), Read("entries.json")));

    private static string Read(string filePart) =>
        File.ReadAllText(Path.Combine(PackDirectory, filePart));

    private static SeasonPack Pack => BasePack.Value;

    // ---------- season 1 / non-SMGP: untouched ----------

    [Fact]
    public void SeasonOne_ReturnsThePackVerbatim()
    {
        Assert.Same(Pack, SmgpSeasonVariety.ForSeason(Pack, 1, Seed));
    }

    [Fact]
    public void ANonSmgpPack_IsNeverVaried_EvenInLaterSeasons()
    {
        var notSmgp = Pack with { Manifest = Pack.Manifest with { CareerStyle = null } };
        Assert.Same(notSmgp, SmgpSeasonVariety.ForSeason(notSmgp, 4, Seed));
    }

    // ---------- season 2+: shuffled, weathered, but structurally sound ----------

    [Fact]
    public void SeasonTwo_KeepsMonacoAsTheFinale()
    {
        var s1 = Pack.Season.Rounds;
        var s2 = SmgpSeasonVariety.ForSeason(Pack, 2, Seed).Season.Rounds;

        Assert.Equal(s1[^1].Name, s2[^1].Name);
        Assert.Equal(s1[^1].Track.Id, s2[^1].Track.Id);
        Assert.Equal(s1[^1].Laps, s2[^1].Laps);
    }

    [Fact]
    public void SeasonTwo_IsAPermutationOfEveryVenue_NoneLostOrDuplicated()
    {
        var s1 = Pack.Season.Rounds.Select(r => r.Name).OrderBy(x => x, StringComparer.Ordinal);
        var s2 = SmgpSeasonVariety.ForSeason(Pack, 2, Seed).Season.Rounds
            .Select(r => r.Name).OrderBy(x => x, StringComparer.Ordinal);
        Assert.Equal(s1, s2);
    }

    [Fact]
    public void SeasonTwo_ActuallyReordersTheNonFinaleRounds()
    {
        var s1 = Pack.Season.Rounds;
        var s2 = SmgpSeasonVariety.ForSeason(Pack, 2, Seed).Season.Rounds;
        // At least one non-finale position now holds a different venue.
        bool reordered = Enumerable.Range(0, s1.Count - 1)
            .Any(i => !string.Equals(s1[i].Name, s2[i].Name, StringComparison.Ordinal));
        Assert.True(reordered, "season 2 ran the identical order, the shuffle did nothing.");
    }

    [Fact]
    public void SeasonTwo_PreservesEveryFoldRelevantSlotField()
    {
        var s1 = Pack.Season.Rounds;
        var s2 = SmgpSeasonVariety.ForSeason(Pack, 2, Seed).Season.Rounds;

        Assert.Equal(s1.Count, s2.Count);
        for (int i = 0; i < s1.Count; i++)
        {
            // The POSITION keeps the number, date, championship flag, and the whole grid block
            // (size + baked DNQ qualifiers), everything the resolver and fold read.
            Assert.Equal(s1[i].Round, s2[i].Round);
            Assert.Equal(s1[i].Date, s2[i].Date);
            Assert.Equal(s1[i].Championship, s2[i].Championship);
            Assert.Equal(s1[i].Grid!.Size, s2[i].Grid!.Size);
            Assert.Equal(s1[i].Grid!.StarterDriverIds, s2[i].Grid!.StarterDriverIds);
        }
    }

    [Fact]
    public void SeasonTwo_BringsWeather_WhereSeasonOneWasAllClear()
    {
        // Season 1 (authored) is ideal weather everywhere.
        Assert.All(Pack.Season.Rounds, r =>
            Assert.All(r.Weekend!.Races, race => Assert.All(race.WeatherSlots!, s => Assert.Equal("Clear", s))));

        // Season 2 has at least one non-Clear race slot somewhere on the calendar.
        var s2 = SmgpSeasonVariety.ForSeason(Pack, 2, Seed).Season.Rounds;
        bool anyWeather = s2.Any(r => r.Weekend!.Races.Any(race =>
            race.WeatherSlots!.Any(s => !string.Equals(s, "Clear", StringComparison.Ordinal))));
        Assert.True(anyWeather, "season 2 was still all Clear, weather never changed.");

        // The setup guide (what AMS2 gets told to run) carries up to 4 slots now, not the lone Clear.
        Assert.All(s2, r => Assert.InRange(r.SetupGuide!.Session.WeatherSlots.Count, 1, 4));
    }

    // ---------- determinism ----------

    [Fact]
    public void SameSeasonAndSeed_ReDerivesIdentically()
    {
        var a = SmgpSeasonVariety.ForSeason(Pack, 3, Seed).Season.Rounds;
        var b = SmgpSeasonVariety.ForSeason(Pack, 3, Seed).Season.Rounds;
        for (int i = 0; i < a.Count; i++)
        {
            Assert.Equal(a[i].Name, b[i].Name);
            Assert.Equal(a[i].Weekend!.Races[0].WeatherSlots, b[i].Weekend!.Races[0].WeatherSlots);
        }
    }

    [Fact]
    public void DifferentSeasons_RunDifferentCalendars()
    {
        var s2 = SmgpSeasonVariety.ForSeason(Pack, 2, Seed).Season.Rounds.Select(r => r.Name).ToList();
        var s3 = SmgpSeasonVariety.ForSeason(Pack, 3, Seed).Season.Rounds.Select(r => r.Name).ToList();
        Assert.NotEqual(s2, s3); // vanishingly unlikely to collide; the seed differs by ordinal
    }

    // ---------- the load-bearing guard: FOLD-INERT ----------

    [Fact]
    public void TheResolvedGrid_IsIdentical_BeforeAndAfterTheVariety()
    {
        // For every round POSITION, resolving the grid on the varied pack yields the same seats
        // (driver ids, ratings, order) as the un-varied pack, the shuffle touches only fields the
        // resolver never reads, so the sim/fold/replay cannot diverge.
        var varied = SmgpSeasonVariety.ForSeason(Pack, 2, Seed);

        foreach (var round in Pack.Season.Rounds)
        {
            var before = RoundGridResolver.Resolve(Pack, round.Round).Seats;
            var after = RoundGridResolver.Resolve(varied, round.Round).Seats;

            Assert.Equal(before.Count, after.Count);
            Assert.Equal(
                before.Select(s => (s.DriverId, s.Ams2LiveryName, s.Ratings.RaceSkill)),
                after.Select(s => (s.DriverId, s.Ams2LiveryName, s.Ratings.RaceSkill)));
        }
    }
}
