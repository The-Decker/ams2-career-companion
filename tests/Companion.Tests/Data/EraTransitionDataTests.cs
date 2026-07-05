using Companion.Core.Career;
using Companion.Core.Determinism;
using Companion.Data;

namespace Companion.Tests.Data;

/// <summary>
/// Era transition v1 through the Data layer (PLAN M6): CareerStore.StartNextSeason persists
/// the plan atomically and journals the transition, and the multi-pack
/// ReplayService.Resimulate overload replays a career that crosses a pack boundary (with a
/// bridged gap year) byte-identically — divergence rules identical to the rollover path.
/// </summary>
public class EraTransitionDataTests
{
    [Fact]
    public void StartNextSeasonPersistsThePlanAndJournalsTheTransition()
    {
        using var tmp = new TempDb();
        using var db = CareerDatabase.Open(tmp.Path);
        var (_, season2, toPack, plan) = EraTransitionFixture.PlayTransitionedCareer(db, playSeason2: false);

        // The new season row is pinned to ITS OWN pack.
        var seasons = CareerStore.ReadSeasons(db);
        Assert.Equal(2, seasons.Count);
        Assert.Equal(1969, seasons[1].Year);
        Assert.Equal(toPack.Manifest.PackId, seasons[1].PackId);
        Assert.Equal(SeasonStatus.Active, seasons[1].Status);
        // The pack got pinned (hash-verified read succeeds).
        CareerStore.ReadPinnedPack(db, toPack.Manifest.PackId, toPack.Manifest.Version);

        // Stage-'start' states come from the plan (rollover + transition carryover).
        Assert.Equal(plan.Player, StateStore.ReadPlayerState(db, season2, StateStore.StageStart));
        Assert.Equal(plan.Drivers, StateStore.ReadDriverStates(db, season2, StateStore.StageStart));
        Assert.Equal(plan.Teams, StateStore.ReadTeamStates(db, season2, StateStore.StageStart));
        Assert.Equal(EraTransitionFixture.Season2PlayerLivery, plan.Player.LiveryName);

        // The journal opens with the era.transition header, then the plan's events.
        var journal = JournalStore.ReadSeason(db, season2);
        Assert.Equal(DataJournalPhases.EraTransition, journal[0].Phase);
        Assert.Equal("accepted-offer", journal[0].Cause);
        Assert.Null(journal[0].Round);
        Assert.Contains("\"fromYear\":1967", journal[0].DeltaJson);
        Assert.Contains("\"toYear\":1969", journal[0].DeltaJson);
        Assert.Contains("\"bridgedYears\":[1968]", journal[0].DeltaJson);
        Assert.Contains($"\"teamId\":\"{EraTransitionFixture.AcceptedTeam}\"", journal[0].DeltaJson);

        var bridge = Assert.Single(journal, r => r.Phase == JournalPhases.EraBridge);
        Assert.Contains("\"year\":1968", bridge.DeltaJson);
        // driver.canon_next (canon final year 1968) retired inside the bridge.
        Assert.Contains("\"driver\":\"driver.canon_next\",\"cause\":\"canon\"", bridge.DeltaJson);

        // Departures: the renamed driver.b and the folded team.min never reach 1969.
        Assert.Contains(journal, r => r.Phase == JournalPhases.EraDeparted && r.Entity == "driver.b");
        Assert.Contains(journal, r => r.Phase == JournalPhases.EraDeparted && r.Entity == "team.min");
        // The Budget-Unit rescale seam is journaled (identity in v1).
        Assert.Contains(journal, r => r.Phase == JournalPhases.EraEconomy && r.Cause == "bu-rescale");
    }

    [Fact]
    public void CrossTransitionCareerReplaysByteIdentical()
    {
        using var tmp = new TempDb();
        using var db = CareerDatabase.Open(tmp.Path);
        var (season1, season2, _, _) = EraTransitionFixture.PlayTransitionedCareer(db);

        var report = ReplayService.Resimulate(
            db, DataCareerFixture.MasterSeed, DataCareerFixture.Inputs());

        Assert.True(report.Identical,
            $"diverged: {report.FirstDivergence?.Reason} stored={report.FirstDivergence?.StoredDeltaJson} " +
            $"regenerated={report.FirstDivergence?.RegeneratedDeltaJson}");
        Assert.Null(report.FirstDivergence);

        // Every non-provenance row of BOTH seasons — transition rows included — was compared.
        int storedSimRows =
            JournalStore.ReadSeason(db, season1).Count(r => !DataJournalPhases.IsProvenance(r.Phase)) +
            JournalStore.ReadSeason(db, season2).Count(r => !DataJournalPhases.IsProvenance(r.Phase));
        Assert.Equal(storedSimRows, report.ComparedRows);

        // Derived state was rebuilt for both seasons.
        Assert.Equal(3, StateStore.ReadRoundPlayerStates(db, season1).Count);
        Assert.Equal(2, StateStore.ReadRoundPlayerStates(db, season2).Count);
        Assert.NotEmpty(StateStore.ReadDriverStates(db, season2, StateStore.StageEnd));

        // The accepted offer that drove the transition survived re-simulation.
        Assert.True(StateStore.ReadOffers(db, season1)
            .Single(o => o.Terms.TeamId == EraTransitionFixture.AcceptedTeam).Accepted);
    }

    [Fact]
    public void SingleSeasonCareerReplaysIdenticallyThroughTheMultiPackOverload()
    {
        using var tmp = new TempDb();
        using var db = CareerDatabase.Open(tmp.Path);
        var (seasonId, pack) = DataCareerFixture.SetupCareer(db);
        DataCareerFixture.PlaySeason(db, seasonId, pack);

        // Byte-compat: an existing single-season career replays identically whether the
        // caller supplies the pack (legacy overload) or lets seasons resolve their own.
        var multiPack = ReplayService.Resimulate(
            db, DataCareerFixture.MasterSeed, DataCareerFixture.Inputs());
        Assert.True(multiPack.Identical);

        var legacy = ReplayService.Resimulate(
            db, pack, DataCareerFixture.MasterSeed, DataCareerFixture.Inputs());
        Assert.True(legacy.Identical);
        Assert.Equal(legacy.ComparedRows, multiPack.ComparedRows);
    }

    [Fact]
    public void LegacySinglePackOverloadStillRefusesAMultiPackCareer()
    {
        using var tmp = new TempDb();
        using var db = CareerDatabase.Open(tmp.Path);
        EraTransitionFixture.PlayTransitionedCareer(db);

        var ex = Assert.Throws<InvalidOperationException>(() => ReplayService.Resimulate(
            db, DataCareerFixture.Pack(), DataCareerFixture.MasterSeed, DataCareerFixture.Inputs()));
        Assert.Contains("single pinned pack", ex.Message);
    }

    [Fact]
    public void TamperedTransitionStartStateIsAStartStateDivergenceWithZeroDataLoss()
    {
        using var tmp = new TempDb();
        using var db = CareerDatabase.Open(tmp.Path);
        var (season1, season2, _, _) = EraTransitionFixture.PlayTransitionedCareer(db);

        var stored = StateStore.ReadPlayerState(db, season2, StateStore.StageStart)!;
        StateStore.UpsertPlayerState(
            db, season2, StateStore.StageStart, stored with { Reputation = 99.0 });
        var journalBefore = JournalStore.ReadSeason(db, season2);
        var roundStatesBefore = StateStore.ReadRoundPlayerStates(db, season1);

        var report = ReplayService.Resimulate(
            db, DataCareerFixture.MasterSeed, DataCareerFixture.Inputs());

        Assert.False(report.Identical);
        Assert.NotNull(report.FirstDivergence);
        Assert.Equal("start-state", report.FirstDivergence.Reason);
        Assert.Equal(season2, report.FirstDivergence.SeasonId);

        // Report-only: the transaction rolled back — tampered row still reads back tampered,
        // season-1 folds and season-2 journal untouched.
        Assert.Equal(99.0, StateStore.ReadPlayerState(db, season2, StateStore.StageStart)!.Reputation);
        Assert.Equal(journalBefore, JournalStore.ReadSeason(db, season2));
        Assert.Equal(roundStatesBefore, StateStore.ReadRoundPlayerStates(db, season1));
    }

    [Fact]
    public void TransitionWithoutAnAcceptedOfferIsADivergence()
    {
        using var tmp = new TempDb();
        using var db = CareerDatabase.Open(tmp.Path);
        var (season1, _, _, _) = EraTransitionFixture.PlayTransitionedCareer(db);

        using (var clear = db.Connection.CreateCommand())
        {
            clear.CommandText = "UPDATE offer SET accepted = 0 WHERE season_id = @season;";
            clear.Parameters.AddWithValue("@season", season1);
            clear.ExecuteNonQuery();
        }

        var report = ReplayService.Resimulate(
            db, DataCareerFixture.MasterSeed, DataCareerFixture.Inputs());

        Assert.False(report.Identical);
        Assert.NotNull(report.FirstDivergence);
        Assert.Equal("accepted-offer", report.FirstDivergence.Reason);
    }

    [Fact]
    public void StartNextSeasonRefusesAPlanWithValidationErrors()
    {
        using var tmp = new TempDb();
        using var db = CareerDatabase.Open(tmp.Path);
        var (season1, fromPack) = DataCareerFixture.SetupCareer(db);
        var season1End = DataCareerFixture.PlaySeason(db, season1, fromPack);
        StateStore.SetOfferAccepted(db, season1, EraTransitionFixture.AcceptedTeam);

        var toPack = EraTransitionFixture.ToPack();
        var ghostOffer = new PlayerOffer
        {
            TeamId = "team.ghost",
            Tier = 3,
            SalaryBu = 4.0,
            Score = 1.0,
        };
        var plan = EraTransition.Build(
            fromPack, toPack, season1End, season1End.Player, ghostOffer,
            new StreamFactory(DataCareerFixture.MasterSeed),
            Companion.Tests.Career.CareerTestData.LoadAgingCurves(),
            DataCareerFixture.Inputs().CanonRetirements);
        Assert.NotEmpty(plan.ValidationErrors);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            CareerStore.StartNextSeason(db, plan, toPack, EraTransitionFixture.Utc2));
        Assert.Contains("team.ghost", ex.Message);

        // Nothing persisted: one season, and the target pack never got pinned.
        Assert.Single(CareerStore.ReadSeasons(db));
        Assert.Throws<InvalidOperationException>(() =>
            CareerStore.ReadPinnedPack(db, toPack.Manifest.PackId, toPack.Manifest.Version));
    }

    [Fact]
    public void StartNextSeasonRefusesAnUnfinishedFromSeason()
    {
        using var tmp = new TempDb();
        using var db = CareerDatabase.Open(tmp.Path);
        var (season1, fromPack) = DataCareerFixture.SetupCareer(db);

        // Fold one round but never run season end — the season is still active. Build a
        // plan from a played-out copy so the plan itself is well-formed.
        var round = DataCareerFixture.Rounds()[0];
        ReplayService.ImportAndFoldRound(
            db, season1, fromPack, DataCareerFixture.MasterSeed, DataCareerFixture.Inputs(),
            1, DataCareerFixture.Envelope(round), DataCareerFixture.Utc);

        using var tmp2 = new TempDb();
        using var db2 = CareerDatabase.Open(tmp2.Path);
        var (other1, otherPack) = DataCareerFixture.SetupCareer(db2);
        var otherEnd = DataCareerFixture.PlaySeason(db2, other1, otherPack);
        var plan = EraTransitionFixture.BuildPlan(otherEnd, EraTransitionFixture.ToPack());

        var ex = Assert.Throws<InvalidOperationException>(() =>
            CareerStore.StartNextSeason(db, plan, EraTransitionFixture.ToPack(), EraTransitionFixture.Utc2));
        Assert.Contains("finish", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(CareerStore.ReadSeasons(db));
    }
}
