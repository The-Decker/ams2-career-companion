using Companion.Core.Career;
using Companion.Data;

namespace Companion.Tests.Data;

public class StateStoreTests
{
    private static (CareerDatabase Db, long SeasonId) Setup(TempDb tmp)
    {
        var db = CareerDatabase.Open(tmp.Path);
        var (seasonId, _) = DataCareerFixture.SetupCareer(db);
        return (db, seasonId);
    }

    [Fact]
    public void DriverStatesRoundTripPreservingCallerOrder()
    {
        using var tmp = new TempDb();
        var (db, seasonId) = Setup(tmp);
        using var _ = db;

        // Deliberately NOT alphabetical: journal order follows this order, so the store
        // must preserve it verbatim.
        var read = StateStore.ReadDriverStates(db, seasonId, StateStore.StageStart);
        Assert.Equal(DataCareerFixture.DriverStates(), read);
        Assert.Equal(
            DataCareerFixture.DriverStates().Select(d => d.DriverId),
            read.Select(d => d.DriverId));
    }

    [Fact]
    public void UpsertReplacesTheStageWholesale()
    {
        using var tmp = new TempDb();
        var (db, seasonId) = Setup(tmp);
        using var _ = db;

        StateStore.UpsertDriverStates(db, seasonId, StateStore.StageStart,
            [new DriverCareerState { DriverId = "driver.only", Age = 22, RaceSkillDelta = 0.01 }]);

        var read = Assert.Single(StateStore.ReadDriverStates(db, seasonId, StateStore.StageStart));
        Assert.Equal("driver.only", read.DriverId);
        Assert.Equal(0.01, read.RaceSkillDelta);
    }

    [Fact]
    public void StagesAreIndependent()
    {
        using var tmp = new TempDb();
        var (db, seasonId) = Setup(tmp);
        using var _ = db;

        StateStore.UpsertDriverStates(db, seasonId, StateStore.StageEnd,
            [new DriverCareerState { DriverId = "driver.a", Age = 26, Retired = true }]);

        Assert.Equal(5, StateStore.ReadDriverStates(db, seasonId, StateStore.StageStart).Count);
        var end = Assert.Single(StateStore.ReadDriverStates(db, seasonId, StateStore.StageEnd));
        Assert.True(end.Retired);
    }

    [Fact]
    public void PlayerStateUpsertOverwrites()
    {
        using var tmp = new TempDb();
        var (db, seasonId) = Setup(tmp);
        using var _ = db;

        Assert.Equal(DataCareerFixture.PlayerStart(),
            StateStore.ReadPlayerState(db, seasonId, StateStore.StageStart));

        var updated = DataCareerFixture.PlayerStart() with { Reputation = 55.5 };
        StateStore.UpsertPlayerState(db, seasonId, StateStore.StageStart, updated);
        Assert.Equal(updated, StateStore.ReadPlayerState(db, seasonId, StateStore.StageStart));

        Assert.Null(StateStore.ReadPlayerState(db, seasonId, StateStore.StageEnd));
    }

    [Fact]
    public void TeamStatesRoundTripWithLineage()
    {
        using var tmp = new TempDb();
        var (db, seasonId) = Setup(tmp);
        using var _ = db;

        Assert.Equal(DataCareerFixture.TeamStates(),
            StateStore.ReadTeamStates(db, seasonId, StateStore.StageStart));
    }

    [Fact]
    public void OffersRoundTripInRankOrderAndAcceptanceIsExclusive()
    {
        using var tmp = new TempDb();
        var (db, seasonId) = Setup(tmp);
        using var _ = db;

        var offers = new List<PlayerOffer>
        {
            new() { TeamId = "team.mid", Tier = 3, SalaryBu = 12.0, Score = 9.9 },
            new() { TeamId = "team.min", Tier = 1, SalaryBu = 4.0, Score = 5.5 },
        };
        StateStore.UpsertOffers(db, seasonId, offers);

        var read = StateStore.ReadOffers(db, seasonId);
        Assert.Equal(offers, read.Select(o => o.Terms));
        Assert.All(read, o => Assert.False(o.Accepted));

        StateStore.SetOfferAccepted(db, seasonId, "team.min");
        StateStore.SetOfferAccepted(db, seasonId, "team.mid");

        read = StateStore.ReadOffers(db, seasonId);
        Assert.True(read.Single(o => o.Terms.TeamId == "team.mid").Accepted);
        Assert.False(read.Single(o => o.Terms.TeamId == "team.min").Accepted);
    }

    [Fact]
    public void RoundPlayerStatesRoundTripInRoundOrderAndDoubleInsertThrows()
    {
        using var tmp = new TempDb();
        var (db, seasonId) = Setup(tmp);
        using var _ = db;

        var afterRound1 = new RoundPlayerState
        {
            Player = DataCareerFixture.PlayerStart() with { Reputation = 42.5, Opi = 0.8 },
            RecommendedSlider = 93,
        };
        var afterRound2 = new RoundPlayerState
        {
            Player = DataCareerFixture.PlayerStart() with { Reputation = 44.0 },
            RecommendedSlider = 94,
        };
        StateStore.InsertRoundPlayerState(db, seasonId, 2, afterRound2);
        StateStore.InsertRoundPlayerState(db, seasonId, 1, afterRound1);

        Assert.Equal(afterRound1, StateStore.ReadRoundPlayerState(db, seasonId, 1));
        Assert.Null(StateStore.ReadRoundPlayerState(db, seasonId, 3));
        Assert.Equal(
            [(1, afterRound1), (2, afterRound2)],
            StateStore.ReadRoundPlayerStates(db, seasonId));

        // Folding a round twice is a bug, the strict insert must throw, never replace.
        Assert.ThrowsAny<Microsoft.Data.Sqlite.SqliteException>(() =>
            StateStore.InsertRoundPlayerState(db, seasonId, 1, afterRound1));
    }

    [Fact]
    public void WipeDerivedKeepsStartStatesAndDropsEndStatesAndOffers()
    {
        using var tmp = new TempDb();
        var (db, seasonId) = Setup(tmp);
        using var _ = db;

        StateStore.UpsertDriverStates(db, seasonId, StateStore.StageEnd,
            [new DriverCareerState { DriverId = "driver.a", Age = 26 }]);
        StateStore.UpsertTeamStates(db, seasonId, StateStore.StageEnd,
            [new TeamCareerState { TeamId = "team.top", LineageId = "team.top", Tier = 4 }]);
        StateStore.UpsertPlayerState(db, seasonId, StateStore.StageEnd, DataCareerFixture.PlayerStart());
        StateStore.UpsertOffers(db, seasonId,
            [new PlayerOffer { TeamId = "team.mid", Tier = 3, SalaryBu = 1.0, Score = 1.0 }]);
        StateStore.InsertRoundPlayerState(db, seasonId, 1, new RoundPlayerState
        {
            Player = DataCareerFixture.PlayerStart(),
            RecommendedSlider = 92,
        });

        StateStore.WipeDerived(db);

        Assert.Empty(StateStore.ReadDriverStates(db, seasonId, StateStore.StageEnd));
        Assert.Empty(StateStore.ReadTeamStates(db, seasonId, StateStore.StageEnd));
        Assert.Null(StateStore.ReadPlayerState(db, seasonId, StateStore.StageEnd));
        Assert.Empty(StateStore.ReadOffers(db, seasonId));
        Assert.Empty(StateStore.ReadRoundPlayerStates(db, seasonId));

        // The inputs survive.
        Assert.Equal(5, StateStore.ReadDriverStates(db, seasonId, StateStore.StageStart).Count);
        Assert.Equal(3, StateStore.ReadTeamStates(db, seasonId, StateStore.StageStart).Count);
        Assert.NotNull(StateStore.ReadPlayerState(db, seasonId, StateStore.StageStart));
    }

    [Fact]
    public void UnknownStageIsRejected()
    {
        using var tmp = new TempDb();
        var (db, seasonId) = Setup(tmp);
        using var _ = db;

        Assert.Throws<ArgumentException>(() => StateStore.ReadDriverStates(db, seasonId, "middle"));
    }
}
