using Companion.Core.Career;
using Companion.Core.Determinism;
using Companion.Core.Packs;
using Companion.Core.Scoring;

namespace Companion.Tests.Career;

/// <summary>
/// Era transition v1 (PLAN M6), pure Core: lineage carry across a synthetic 2-pack pair
/// (including a renamed driver who must NOT carry), deterministic gap-year bridging with
/// year-keyed streams, the bridge-or-block rule, seat resolution in the new pack, and the
/// offer-team-missing validation surface.
/// </summary>
public class EraTransitionTests
{
    private const ulong Seed = 42;

    // ---------- the synthetic next-era pack ----------

    /// <summary>The 1969-style follow-on to <see cref="CareerTestData.Pack"/> (1967):
    /// team.top and team.mid carry by lineage id, team.min is gone, team.new arrives;
    /// driver.a and driver.old carry, driver.b is RENAMED to driver.bob_brakes (a new
    /// lineage id, must not carry), driver.young and driver.new_guy arrive. team.mid's
    /// entries deliberately list a partial-season seat BEFORE the full-season one.</summary>
    private static SeasonPack ToPack(int year = 1969, bool teamNewHasEntries = true)
    {
        PackTeam Team(string id, string name, int tier) => new()
        {
            Id = id,
            Name = name,
            CarVehicleIds = ["formula_vintage_g2m1"],
            Reliability = 0.9,
            BudgetTier = tier,
        };

        PackDriver Driver(string id, string name, int born, double race, double quali) => new()
        {
            Id = id,
            Name = name,
            Country = "TST",
            Born = born,
            Ratings = CareerTestData.Ratings(race, quali),
        };

        PackEntry Entry(string teamId, string driverId, string number, string rounds, string livery) => new()
        {
            TeamId = teamId,
            DriverId = driverId,
            Number = number,
            Rounds = rounds,
            Ams2LiveryName = livery,
        };

        PackRound Round(int round) => new()
        {
            Round = round,
            Name = $"Next Era Grand Prix {round}",
            Date = $"{year}-0{round}-01",
            Track = new PackTrackRef { Id = "kyalami_historic" },
            Laps = 10,
        };

        var entries = new List<PackEntry>
        {
            Entry("team.top", "driver.a", "1", "1-2", "T69 #1"),
            Entry("team.top", "driver.bob_brakes", "2", "1-2", "T69 #2"),
            // Partial-season seat FIRST: full-season preference must skip it.
            Entry("team.mid", "driver.young", "3", "1", "M69 #3g"),
            Entry("team.mid", "driver.p", "4", "1-2", "M69 #4"),
            Entry("team.min2", "driver.old", "7", "1-2", "O69 #7"),
        };
        if (teamNewHasEntries)
            entries.Add(Entry("team.new", "driver.new_guy", "9", "1-2", "N69 #9"));

        return new SeasonPack
        {
            Manifest = new PackManifest
            {
                PackId = $"era-test-pack-{year}",
                Name = $"Era Test Pack {year}",
                Version = "1.0.0",
                FormatVersion = 1,
            },
            Season = new SeasonDefinition
            {
                Year = year,
                SeriesName = "Test Series",
                Ams2Class = "F-Vintage_Gen2",
                PointsSystem = new CatalogSeason
                {
                    RacePoints = [new(9), new(6), new(4), new(3), new(2), new(1)],
                    Constructors = new CatalogConstructors { BestCarOnly = false },
                },
                Rounds = [Round(1), Round(2)],
            },
            Teams =
            [
                Team("team.top", "Top Team Mk2", 5),
                Team("team.mid", "Mid Team Mk2", 3),
                Team("team.min2", "Second Minnow", 1),
                Team("team.new", "New Era Racing", 2),
            ],
            Drivers =
            [
                Driver("driver.a", "Alan Apex", 1941, 0.82, 0.83),
                Driver("driver.p", "Pat Player", 1940, 0.72, 0.72),
                Driver("driver.bob_brakes", "Bob Brakes", 1943, 0.76, 0.75),
                Driver("driver.young", "Yves Young", 1948, 0.68, 0.69),
                Driver("driver.old", "Old Oscar", 1926, 0.48, 0.50),
                Driver("driver.new_guy", "Nino New", 1945, 0.66, 0.66),
            ],
            Entries = entries,
        };
    }

    private static readonly IReadOnlyDictionary<string, int> CanonRetirements =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["driver.canon_now"] = 1967,
            ["driver.canon_next"] = 1968,
        };

    private static PlayerOffer MidOffer() => new()
    {
        TeamId = "team.mid",
        Tier = 3,
        SalaryBu = 4.0,
        Score = 1.0,
    };

    private static TransitionPlan BuildPlan(
        SeasonEndResult end, SeasonPack toPack, PlayerOffer? offer = null) =>
        EraTransition.Build(
            CareerTestData.Pack(), toPack, end, end.Player, offer ?? MidOffer(),
            new StreamFactory(Seed), CareerTestData.LoadAgingCurves(), CanonRetirements);

    private static SeasonEndResult RunSeasonEnd() => SeasonEndPipeline.Run(CareerTestData.Context());

    // ---------- lineage carry ----------

    [Fact]
    public void MatchedLineagesCarryAndUnmatchedEntitiesStartFresh()
    {
        var end = RunSeasonEnd();
        var plan = BuildPlan(end, ToPack());

        Assert.Empty(plan.ValidationErrors);
        Assert.Equal([1968], plan.BridgedYears);

        // Teams follow the NEW pack's order; matched lineages carry the tier-drift result.
        Assert.Equal(["team.top", "team.mid", "team.min2", "team.new"],
            plan.Teams.Select(t => t.TeamId).ToArray());
        int endTopTier = end.Teams.Single(t => t.TeamId == "team.top").Tier;
        int endMidTier = end.Teams.Single(t => t.TeamId == "team.mid").Tier;
        Assert.Equal(endTopTier, plan.Teams.Single(t => t.TeamId == "team.top").Tier);
        Assert.Equal(endMidTier, plan.Teams.Single(t => t.TeamId == "team.mid").Tier);
        // New-era teams start at their authored budget tier.
        Assert.Equal(2, plan.Teams.Single(t => t.TeamId == "team.new").Tier);
        Assert.All(plan.Teams, t => Assert.InRange(t.Tier, 1, 5));

        // Drivers follow the new pack's order, minus the player's seat (driver.p).
        Assert.Equal(
            ["driver.a", "driver.bob_brakes", "driver.young", "driver.old", "driver.new_guy"],
            plan.Drivers.Select(d => d.DriverId).ToArray());

        // driver.a carried: aged by the pipeline (+1) and the bridged 1968 (+1).
        var endA = end.Drivers.Single(d => d.DriverId == "driver.a");
        var planA = plan.Drivers.Single(d => d.DriverId == "driver.a");
        Assert.Equal(endA.Age + 1, planA.Age);
        Assert.False(planA.Retired);

        // The RENAMED driver does NOT carry: driver.b departed, driver.bob_brakes is fresh.
        var bob = plan.Drivers.Single(d => d.DriverId == "driver.bob_brakes");
        Assert.Equal(1969 - 1943, bob.Age);
        Assert.Equal(0.0, bob.RaceSkillDelta);
        Assert.Equal(0.0, bob.QualifyingSkillDelta);
        Assert.Contains(plan.Events, e =>
            e.Phase == JournalPhases.EraDeparted && e.Entity == "driver.b" &&
            e.Cause == "not-in-next-pack");

        // Departed team journaled.
        Assert.Contains(plan.Events, e =>
            e.Phase == JournalPhases.EraDeparted && e.Entity == "team.min" &&
            e.Cause == "not-in-next-pack");
    }

    [Fact]
    public void CanonRetirementFiresDuringTheBridgeYear()
    {
        var end = RunSeasonEnd();
        // driver.canon_next (canon final year 1968) survives the 1967 pipeline...
        Assert.False(end.Drivers.Single(d => d.DriverId == "driver.canon_next").Retired);

        var plan = BuildPlan(end, ToPack());

        // ...and retires inside the bridged 1968, on schedule, journaled in era.bridge.
        var bridge = Assert.Single(plan.Events, e => e.Phase == JournalPhases.EraBridge);
        Assert.Equal("gap-year", bridge.Cause);
        Assert.Contains("\"year\":1968", bridge.DeltaJson);
        Assert.Contains("\"driver\":\"driver.canon_next\",\"cause\":\"canon\"", bridge.DeltaJson);

        // Not in the 1969 pack either: departed, with the retired flag in the delta.
        var departed = Assert.Single(plan.Events, e =>
            e.Phase == JournalPhases.EraDeparted && e.Entity == "driver.canon_next");
        Assert.Contains("\"retired\":true", departed.DeltaJson);
    }

    // ---------- gap-year determinism ----------

    [Fact]
    public void SameSeedRebuildsTheIdenticalPlan()
    {
        var end = RunSeasonEnd();
        var first = BuildPlan(end, ToPack());
        var second = BuildPlan(end, ToPack());

        Assert.Equal(first.Player, second.Player);
        Assert.Equal(first.Drivers, second.Drivers);
        Assert.Equal(first.Teams, second.Teams);
        Assert.Equal(first.Events, second.Events);
        Assert.Equal(first.BridgedYears, second.BridgedYears);
    }

    [Fact]
    public void BridgeStreamsAreKeyedByTheBridgedYearNotTheTarget()
    {
        var end = RunSeasonEnd();
        var to1969 = BuildPlan(end, ToPack(1969));
        var to1970 = BuildPlan(end, ToPack(1970));

        Assert.Equal([1968], to1969.BridgedYears);
        Assert.Equal([1968, 1969], to1970.BridgedYears);

        // The 1968 bridge is IDENTICAL whichever pack the career lands in: the aging and
        // retirement streams are keyed with the bridged year, not the transition target.
        var bridge1969 = to1969.Events.Single(e => e.Phase == JournalPhases.EraBridge);
        var bridge1970First = to1970.Events.First(e => e.Phase == JournalPhases.EraBridge);
        Assert.Equal(bridge1969, bridge1970First);
        Assert.Equal(2, to1970.Events.Count(e => e.Phase == JournalPhases.EraBridge));
    }

    [Fact]
    public void AdjacentYearTransitionBridgesNothing()
    {
        var end = RunSeasonEnd();
        var plan = BuildPlan(end, ToPack(1968));

        Assert.Empty(plan.BridgedYears);
        Assert.DoesNotContain(plan.Events, e => e.Phase == JournalPhases.EraBridge);
        // No bridge: the carried age is exactly the pipeline's end-state age.
        Assert.Equal(
            end.Drivers.Single(d => d.DriverId == "driver.a").Age,
            plan.Drivers.Single(d => d.DriverId == "driver.a").Age);
    }

    // ---------- bridge-or-block ----------

    [Fact]
    public void TransitionIntoTheSameOrEarlierYearIsBlocked()
    {
        var end = RunSeasonEnd();
        var sameYear = Assert.Throws<InvalidOperationException>(() => BuildPlan(end, ToPack(1967)));
        Assert.Contains("forward", sameYear.Message, StringComparison.OrdinalIgnoreCase);
        var earlier = Assert.Throws<InvalidOperationException>(() => BuildPlan(end, ToPack(1960)));
        Assert.Contains("forward", earlier.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- player seat ----------

    [Fact]
    public void PlayerSeatPrefersTheFullSeasonEntryAndCarriesRepOpiAnchor()
    {
        var end = RunSeasonEnd();
        var plan = BuildPlan(end, ToPack());

        // team.mid lists a partial-season seat first; the "1-N" entry wins.
        Assert.Equal("team.mid", plan.PlayerTeamId);
        Assert.Equal("M69 #4", plan.PlayerSeatLiveryName);
        Assert.Equal("driver.p", plan.DisplacedDriverId);
        Assert.DoesNotContain(plan.Drivers, d => d.DriverId == "driver.p");

        Assert.Equal("team.mid", plan.Player.CurrentTeamId);
        Assert.Equal("M69 #4", plan.Player.LiveryName);
        // Carryover: rep/OPI/pace anchor verbatim (identity BU rescale in v1); gap years
        // are not completed seasons.
        Assert.Equal(end.Player.Reputation, plan.Player.Reputation);
        Assert.Equal(end.Player.Opi, plan.Player.Opi);
        Assert.Equal(end.Player.PaceAnchor, plan.Player.PaceAnchor);
        Assert.Equal(end.Player.SeasonsCompleted, plan.Player.SeasonsCompleted);
    }

    [Fact]
    public void OfferTeamMissingFromTheNewPackIsAValidationErrorNotAThrow()
    {
        var end = RunSeasonEnd();
        var ghost = MidOffer() with { TeamId = "team.ghost" };
        var plan = BuildPlan(end, ToPack(), ghost);

        string error = Assert.Single(plan.ValidationErrors);
        Assert.Contains("team.ghost", error);
        Assert.Null(plan.PlayerTeamId);
        Assert.Null(plan.PlayerSeatLiveryName);
        // The player state is carried unchanged so the UI can show the mismatch.
        Assert.Equal(end.Player.CurrentTeamId, plan.Player.CurrentTeamId);
        Assert.Equal(end.Player.LiveryName, plan.Player.LiveryName);
    }

    [Fact]
    public void OfferTeamWithoutEntriesIsAValidationError()
    {
        var end = RunSeasonEnd();
        var offer = MidOffer() with { TeamId = "team.new" };
        var plan = BuildPlan(end, ToPack(teamNewHasEntries: false), offer);

        string error = Assert.Single(plan.ValidationErrors);
        Assert.Contains("team.new", error);
        Assert.Contains("no entries", error);
    }

    // ---------- Budget-Unit rescale seam ----------

    [Fact]
    public void BudgetRescaleIsIdentityInV1AndJournaledAsTheSeam()
    {
        var end = RunSeasonEnd();
        var plan = BuildPlan(end, ToPack());

        Assert.Equal(1.0, plan.BudgetRescaleFactor);
        var economy = Assert.Single(plan.Events, e => e.Phase == JournalPhases.EraEconomy);
        Assert.Equal("bu-rescale", economy.Cause);
        Assert.Contains("\"factor\":1", economy.DeltaJson);
        // The economy note is the LAST transition event, after bridges and departures.
        Assert.Equal(economy, plan.Events[^1]);
    }
}
