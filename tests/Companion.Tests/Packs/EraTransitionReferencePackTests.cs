using Companion.Core.Career;
using Companion.Core.Determinism;
using Companion.Core.Packs;
using Companion.Tests.Career;

namespace Companion.Tests.Packs;

/// <summary>
/// M6 verification (added by the adversarial-verification pass): the era transition across
/// the SHIPPED reference packs, f1-1967 → f1-1969, bridging 1968, reproduces facts
/// hand-verified against f1db (github.com/f1db/f1db):
///   · drivers who raced both seasons (f1db season_driver 1967 ∩ 1969) carry by lineage id
///     and arrive in 1969 exactly two years older than their 1967 season age;
///   · Jim Clark (f1db: died 1968-04-07, raced 1967 only) does NOT carry and is journaled
///     as departed;
///   · teams map by lineage id (lotus/mclaren/matra/ferrari/brm/brabham carry their drifted
///     tier; cooper/honda/lola/eagle depart; bmw/tecno start at their authored tier);
///   · the 1968 gap year is bridged, never blocked, and journaled as era.bridge.
/// </summary>
public class EraTransitionReferencePackTests
{
    private const ulong Seed = 20260703;

    private static string PacksDirectory => Path.Combine(AppContext.BaseDirectory, "packs");

    private static SeasonPack LoadPack(string packId)
    {
        string dir = Path.Combine(PacksDirectory, packId);
        string Read(string file) => File.ReadAllText(Path.Combine(dir, file));
        return PackLoader.Parse(
            Read("pack.json"), Read("season.json"), Read("teams.json"),
            Read("drivers.json"), Read("entries.json"));
    }

    /// <summary>Both-season drivers per f1db (subset shipped in both packs), with f1db birth
    /// years, the pack Born fields were independently checked against f1db driver rows.</summary>
    private static readonly (string Id, int Born)[] CarriedDrivers =
    [
        ("driver.graham_hill", 1929),
        ("driver.denny_hulme", 1936),
        ("driver.bruce_mclaren", 1937),
        ("driver.jochen_rindt", 1942),
        ("driver.jacky_ickx", 1945),
    ];

    private static TransitionPlan BuildRealPackPlan(
        SeasonPack from, SeasonPack to, out IReadOnlyList<DriverCareerState> driversEnd)
    {
        // End-of-1967 states exactly as the season-end pipeline leaves them: everyone one
        // year older than their 1967 season age (step 3 ages +1), zero drift for clarity.
        driversEnd = from.Drivers
            .Select(d => new DriverCareerState
            {
                DriverId = d.Id,
                Age = 1968 - (d.Born ?? 1937),
            })
            .ToList();

        // Team tiers with one visible drift so lineage carry is distinguishable from the
        // authored 1969 tier: Lotus drifted DOWN to 2 (1969 authors it at 4).
        var teamsEnd = from.Teams
            .Select(t => new TeamCareerState
            {
                TeamId = t.Id,
                LineageId = t.Id,
                Tier = t.Id == "team.lotus" ? 2 : t.BudgetTier,
            })
            .ToList();

        var playerEnd = new PlayerCareerState
        {
            Reputation = 61.5,
            Opi = 0.8,
            PaceAnchor = 92.4,
            SeasonsCompleted = 1,
            CurrentTeamId = "team.lotus",
            LiveryName = from.Entries.First(e => e.TeamId == "team.lotus").Ams2LiveryName,
        };

        var offer = new PlayerOffer
        {
            TeamId = "team.matra",
            Tier = 5,
            SalaryBu = 6.0,
            Score = 1.0,
        };

        return EraTransition.Build(
            from, to, driversEnd, teamsEnd, playerEnd, offer,
            new StreamFactory(Seed), CareerTestData.LoadAgingCurves());
    }

    [Fact]
    public void RealPacks_TransitionCarriesTheF1dbVerifiedLineages()
    {
        var from = LoadPack("f1-1967");
        var to = LoadPack("f1-1969");
        var plan = BuildRealPackPlan(from, to, out _);

        Assert.Empty(plan.ValidationErrors);
        Assert.Equal(1967, plan.FromYear);
        Assert.Equal(1969, plan.ToYear);
        Assert.Equal([1968], plan.BridgedYears);

        // f1db-verified both-season drivers carry: same lineage id, 1967 age + 2.
        foreach (var (id, born) in CarriedDrivers)
        {
            var carried = plan.Drivers.SingleOrDefault(d => d.DriverId == id);
            Assert.True(carried is not null, $"{id} raced 1967 AND 1969 (f1db) but did not carry.");
            int ageIn1967 = 1967 - born;
            Assert.True(ageIn1967 + 2 == carried!.Age,
                $"{id}: 1967 age {ageIn1967} must arrive in 1969 as {ageIn1967 + 2}, got {carried.Age}.");
        }

        // Jim Clark raced 1967 only (f1db: died 1968-04-07): no carry, journaled departed.
        Assert.DoesNotContain(plan.Drivers, d => d.DriverId == "driver.jim_clark");
        Assert.Contains(plan.Events, e =>
            e.Phase == JournalPhases.EraDeparted && e.Entity == "driver.jim_clark" &&
            e.Cause == "not-in-next-pack");

        // Every 1969 non-player driver has exactly one state row, in the new pack's order.
        Assert.Equal(
            to.Drivers.Select(d => d.Id).Where(id => id != plan.DisplacedDriverId),
            plan.Drivers.Select(d => d.DriverId));
    }

    [Fact]
    public void RealPacks_TeamsMapByLineageAndTheGapYearBridges()
    {
        var from = LoadPack("f1-1967");
        var to = LoadPack("f1-1969");
        var plan = BuildRealPackPlan(from, to, out var driversEnd);

        // Shared lineages carry the DRIFTED tier, not the 1969 authored one.
        Assert.Equal(2, plan.Teams.Single(t => t.TeamId == "team.lotus").Tier);
        foreach (string id in new[]
                 { "team.mclaren", "team.matra", "team.ferrari", "team.brm", "team.brabham" })
        {
            int endTier = from.Teams.Single(t => t.Id == id).BudgetTier;
            Assert.Equal(endTier, plan.Teams.Single(t => t.TeamId == id).Tier);
        }

        // 1969-only teams start at their authored budget tier.
        Assert.Equal(
            to.Teams.Single(t => t.Id == "team.bmw").BudgetTier,
            plan.Teams.Single(t => t.TeamId == "team.bmw").Tier);
        Assert.Equal(
            to.Teams.Single(t => t.Id == "team.tecno").BudgetTier,
            plan.Teams.Single(t => t.TeamId == "team.tecno").Tier);

        // 1967 teams with no 1969 lineage are journaled departed.
        foreach (string id in new[] { "team.cooper", "team.honda", "team.lola", "team.eagle" })
            Assert.Contains(plan.Events, e =>
                e.Phase == JournalPhases.EraDeparted && e.Entity == id &&
                e.Cause == "not-in-next-pack");

        // The 1968 gap year bridged (never blocked) and journaled: every non-retired
        // end-of-1967 driver aged through it.
        var bridge = Assert.Single(plan.Events, e => e.Phase == JournalPhases.EraBridge);
        Assert.Equal("gap-year", bridge.Cause);
        Assert.Contains("\"year\":1968", bridge.DeltaJson);
        Assert.Contains($"\"aged\":{driversEnd.Count}", bridge.DeltaJson);

        // The player signed for Matra: the full-season seat resolves with its exact livery.
        Assert.Equal("team.matra", plan.PlayerTeamId);
        Assert.Equal("driver.jackie_stewart", plan.DisplacedDriverId);
        Assert.Equal("Matra-Ford Cosworth #7 J. Stewart", plan.PlayerSeatLiveryName);
        Assert.Equal("team.matra", plan.Player.CurrentTeamId);
        // Rep/OPI/pace anchor carry verbatim across the era boundary.
        Assert.Equal(61.5, plan.Player.Reputation);
        Assert.Equal(0.8, plan.Player.Opi);
        Assert.Equal(92.4, plan.Player.PaceAnchor);
        Assert.Equal(1, plan.Player.SeasonsCompleted);
    }
}
