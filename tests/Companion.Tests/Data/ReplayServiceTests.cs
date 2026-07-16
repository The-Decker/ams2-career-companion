using Companion.Core.Career;
using Companion.Data;

namespace Companion.Tests.Data;

public class ReplayServiceTests
{
    [Fact]
    public void PlayerRespec_IsAProvenanceExcludedInputPhase()
    {
        Assert.True(DataJournalPhases.IsProvenance(JournalPhases.PlayerRespec));
    }

    [Fact]
    public void PlayerSkillPlan_IsAProvenanceExcludedInputPhase()
    {
        Assert.True(DataJournalPhases.IsProvenance(JournalPhases.PlayerSkillPlan));
    }

    private static (CareerDatabase Db, long SeasonId, Companion.Core.Packs.SeasonPack Pack) PlayedCareer(TempDb tmp)
    {
        var db = CareerDatabase.Open(tmp.Path);
        var (seasonId, pack) = DataCareerFixture.SetupCareer(db);
        DataCareerFixture.PlaySeason(db, seasonId, pack);
        return (db, seasonId, pack);
    }

    [Fact]
    public void MiniSeasonReplaysByteIdentical()
    {
        using var tmp = new TempDb();
        var (db, seasonId, pack) = PlayedCareer(tmp);
        using var _ = db;

        var report = ReplayService.Resimulate(
            db, pack, DataCareerFixture.MasterSeed, DataCareerFixture.Inputs());

        Assert.True(report.Identical,
            $"diverged: {report.FirstDivergence?.Reason} stored={report.FirstDivergence?.StoredDeltaJson} " +
            $"regenerated={report.FirstDivergence?.RegeneratedDeltaJson}");
        Assert.Null(report.FirstDivergence);

        // 3 rounds × (9 standings + race.result + opi + reputation + paceAnchor + headline)
        // = 42 per-round rows, plus the season-end pipeline rows.
        int storedSimRows = JournalStore.ReadSeason(db, seasonId)
            .Count(r => !DataJournalPhases.IsProvenance(r.Phase));
        Assert.Equal(storedSimRows, report.ComparedRows);
        Assert.True(report.ComparedRows > 42,
            $"Expected season-end rows beyond the 42 per-round rows, got {report.ComparedRows}.");
    }

    [Fact]
    public void ReplayRebuildsDerivedStateFromRawResults()
    {
        using var tmp = new TempDb();
        var (db, seasonId, pack) = PlayedCareer(tmp);
        using var _ = db;

        var driversBefore = StateStore.ReadDriverStates(db, seasonId, StateStore.StageEnd);
        var teamsBefore = StateStore.ReadTeamStates(db, seasonId, StateStore.StageEnd);
        var playerBefore = StateStore.ReadPlayerState(db, seasonId, StateStore.StageEnd);
        var offersBefore = StateStore.ReadOffers(db, seasonId);
        var roundStatesBefore = StateStore.ReadRoundPlayerStates(db, seasonId);
        Assert.NotEmpty(driversBefore);
        Assert.NotEmpty(offersBefore);
        Assert.Equal(3, roundStatesBefore.Count);

        var report = ReplayService.Resimulate(
            db, pack, DataCareerFixture.MasterSeed, DataCareerFixture.Inputs());

        Assert.True(report.Identical);
        Assert.Equal(driversBefore, StateStore.ReadDriverStates(db, seasonId, StateStore.StageEnd));
        Assert.Equal(teamsBefore, StateStore.ReadTeamStates(db, seasonId, StateStore.StageEnd));
        Assert.Equal(playerBefore, StateStore.ReadPlayerState(db, seasonId, StateStore.StageEnd));
        Assert.Equal(offersBefore, StateStore.ReadOffers(db, seasonId));
        Assert.Equal(roundStatesBefore, StateStore.ReadRoundPlayerStates(db, seasonId));
    }

    [Fact]
    public void SeasonEndConsumesTheFoldedFinalPlayerState()
    {
        using var tmp = new TempDb();
        using var db = CareerDatabase.Open(tmp.Path);
        var (seasonId, pack) = DataCareerFixture.SetupCareer(db);
        var seasonEnd = DataCareerFixture.PlaySeason(db, seasonId, pack);

        // Per-round accrual moved the player (P3, P2, P1 across the three rounds)...
        var folded = StateStore.ReadRoundPlayerState(db, seasonId, 3);
        Assert.NotNull(folded);
        Assert.True(folded.Player.Reputation > DataCareerFixture.PlayerStart().Reputation);

        // ...and the season-final reputation row folds FROM that state, not from the start.
        var repRow = JournalStore.ReadSeason(db, seasonId).Single(r =>
            r.Phase == JournalPhases.PlayerReputation && r.Cause == "season-final");
        string expectedFrom = Math.Round(folded.Player.Reputation, 4)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains($"\"from\":{expectedFrom}", repRow.DeltaJson);
        Assert.True(seasonEnd.Player.Reputation >= folded.Player.Reputation);
    }

    [Fact]
    public void FoldRoundRefusesToFoldARoundTwice()
    {
        using var tmp = new TempDb();
        using var db = CareerDatabase.Open(tmp.Path);
        var (seasonId, pack) = DataCareerFixture.SetupCareer(db);

        var round = DataCareerFixture.Rounds()[0];
        ReplayService.ImportAndFoldRound(
            db, seasonId, pack, DataCareerFixture.MasterSeed, DataCareerFixture.Inputs(),
            1, DataCareerFixture.Envelope(round), DataCareerFixture.Utc);

        var ex = Assert.Throws<InvalidOperationException>(() => ReplayService.FoldRound(
            db, seasonId, pack, DataCareerFixture.MasterSeed, DataCareerFixture.Inputs(),
            1, DataCareerFixture.Utc));
        Assert.Contains("already folded", ex.Message);
    }

    [Fact]
    public void ImportAndFoldOfAFoldedRoundRollsTheImportBack()
    {
        using var tmp = new TempDb();
        using var db = CareerDatabase.Open(tmp.Path);
        var (seasonId, pack) = DataCareerFixture.SetupCareer(db);

        var rounds = DataCareerFixture.Rounds();
        ReplayService.ImportAndFoldRound(
            db, seasonId, pack, DataCareerFixture.MasterSeed, DataCareerFixture.Inputs(),
            1, DataCareerFixture.Envelope(rounds[0]), DataCareerFixture.Utc);
        string storedPayload = ResultStore.ReadSeasonResults(db, seasonId)[0].PayloadJson;

        // A second ImportAndFold for the same round must throw AND leave the stored raw
        // payload untouched — the import and the fold are one atomic unit.
        Assert.Throws<InvalidOperationException>(() => ReplayService.ImportAndFoldRound(
            db, seasonId, pack, DataCareerFixture.MasterSeed, DataCareerFixture.Inputs(),
            1, DataCareerFixture.Envelope(rounds[1] with { Round = 1 }), DataCareerFixture.Utc));

        Assert.Equal(storedPayload, ResultStore.ReadSeasonResults(db, seasonId)[0].PayloadJson);
        Assert.Single(StateStore.ReadRoundPlayerStates(db, seasonId));
    }

    [Fact]
    public void AcceptedOfferSurvivesResimulation()
    {
        using var tmp = new TempDb();
        var (db, seasonId, pack) = PlayedCareer(tmp);
        using var _ = db;

        string acceptedTeam = StateStore.ReadOffers(db, seasonId)[0].Terms.TeamId;
        StateStore.SetOfferAccepted(db, seasonId, acceptedTeam);

        var report = ReplayService.Resimulate(
            db, pack, DataCareerFixture.MasterSeed, DataCareerFixture.Inputs());

        Assert.True(report.Identical);
        var offers = StateStore.ReadOffers(db, seasonId);
        Assert.True(offers.Single(o => o.Terms.TeamId == acceptedTeam).Accepted);
        Assert.Single(offers, o => o.Accepted);
    }

    [Fact]
    public void AcceptedOfferMissingFromTheRegeneratedSetIsADivergenceNotASilentDrop()
    {
        using var tmp = new TempDb();
        var (db, seasonId, pack) = PlayedCareer(tmp);
        using var _ = db;

        // Tamper: the accepted offer names a team the sim never offered.
        StateStore.SetOfferAccepted(db, seasonId, StateStore.ReadOffers(db, seasonId)[0].Terms.TeamId);
        using (var tamper = db.Connection.CreateCommand())
        {
            tamper.CommandText = "UPDATE offer SET team_id = 'team.ghost' WHERE season_id = @season AND accepted = 1;";
            tamper.Parameters.AddWithValue("@season", seasonId);
            tamper.ExecuteNonQuery();
        }
        var offersBefore = StateStore.ReadOffers(db, seasonId);

        var report = ReplayService.Resimulate(
            db, pack, DataCareerFixture.MasterSeed, DataCareerFixture.Inputs());

        Assert.False(report.Identical);
        Assert.NotNull(report.FirstDivergence);
        Assert.Equal("accepted-offer", report.FirstDivergence.Reason);
        Assert.Equal("team.ghost", report.FirstDivergence.StoredDeltaJson);

        // Report-only: the transaction rolled back — the stored rows (ghost included)
        // survived byte-for-byte. Zero data loss on divergence.
        Assert.Equal(offersBefore, StateStore.ReadOffers(db, seasonId));
        Assert.Equal(3, StateStore.ReadRoundPlayerStates(db, seasonId).Count);
    }

    [Fact]
    public void TamperedJournalRowIsCaughtAsTheFirstDivergenceAndRollsBack()
    {
        using var tmp = new TempDb();
        var (db, seasonId, pack) = PlayedCareer(tmp);
        using var _ = db;

        // Corrupt one round-2 standings row's delta.
        var victim = JournalStore.ReadSeason(db, seasonId)
            .First(r => r.Phase == DataJournalPhases.RoundStandings && r.Round == 2);
        using (var tamper = db.Connection.CreateCommand())
        {
            tamper.CommandText = "UPDATE journal SET delta_json = @delta WHERE seq = @seq;";
            tamper.Parameters.AddWithValue("@delta", """{"position":99,"points":"999"}""");
            tamper.Parameters.AddWithValue("@seq", victim.Seq);
            tamper.ExecuteNonQuery();
        }

        var journalBefore = JournalStore.ReadSeason(db, seasonId);
        var offersBefore = StateStore.ReadOffers(db, seasonId);
        var endStatesBefore = StateStore.ReadDriverStates(db, seasonId, StateStore.StageEnd);
        var roundStatesBefore = StateStore.ReadRoundPlayerStates(db, seasonId);

        var report = ReplayService.Resimulate(
            db, pack, DataCareerFixture.MasterSeed, DataCareerFixture.Inputs());

        Assert.False(report.Identical);
        var divergence = report.FirstDivergence;
        Assert.NotNull(divergence);
        Assert.Equal(seasonId, divergence.SeasonId);
        Assert.Equal(victim.Seq, divergence.StoredSeq);
        Assert.Equal("deltaJson", divergence.Reason);
        Assert.Equal("""{"position":99,"points":"999"}""", divergence.StoredDeltaJson);
        // The regenerated side carries the truth the raw results refold to.
        Assert.Equal(victim.DeltaJson, divergence.RegeneratedDeltaJson);

        // Divergence is report-only: everything stored — journal, offers, end states,
        // per-round folds — survived the rolled-back transaction untouched.
        Assert.Equal(journalBefore, JournalStore.ReadSeason(db, seasonId));
        Assert.Equal(offersBefore, StateStore.ReadOffers(db, seasonId));
        Assert.Equal(endStatesBefore, StateStore.ReadDriverStates(db, seasonId, StateStore.StageEnd));
        Assert.Equal(roundStatesBefore, StateStore.ReadRoundPlayerStates(db, seasonId));
    }

    [Fact]
    public void ReImportAuditRowsDoNotBreakReplay()
    {
        using var tmp = new TempDb();
        var (db, seasonId, pack) = PlayedCareer(tmp);
        using var _ = db;

        // Re-import round 2 with the identical payload: adds an import.result audit row,
        // which the byte-compare excludes by contract.
        string payload = ResultStore.ReadSeasonResults(db, seasonId)[1].PayloadJson;
        var reimport = ResultStore.Append(db, seasonId, 2, payload, "2026-07-04T00:00:00Z");
        Assert.True(reimport.ReImported);
        Assert.False(reimport.PayloadChanged);

        var report = ReplayService.Resimulate(
            db, pack, DataCareerFixture.MasterSeed, DataCareerFixture.Inputs());

        Assert.True(report.Identical);
    }

    [Fact]
    public void CorrectedReImportShowsUpAsADivergenceUntilResimulated()
    {
        using var tmp = new TempDb();
        var (db, seasonId, pack) = PlayedCareer(tmp);
        using var _ = db;

        // Round 3 gets a corrected result: swap the top two finishers.
        var corrected = DataCareerFixture.Rounds()[2];
        var session = corrected.Sessions[0];
        var swapped = session.Entries.ToList();
        (swapped[0], swapped[1]) = (
            swapped[1] with { Position = 1 },
            swapped[0] with { Position = 2 });
        corrected = corrected with { Sessions = [session with { Entries = swapped }] };

        string payload = System.Text.Json.JsonSerializer.Serialize(
            DataCareerFixture.Envelope(corrected), Companion.Core.Json.CoreJson.Options);
        var reimport = ResultStore.Append(db, seasonId, 3, payload, "2026-07-04T00:00:00Z");
        Assert.True(reimport.PayloadChanged);

        // The stored journal was folded from the OLD payload — replay must flag it.
        var report = ReplayService.Resimulate(
            db, pack, DataCareerFixture.MasterSeed, DataCareerFixture.Inputs());
        Assert.False(report.Identical);
        Assert.NotNull(report.FirstDivergence);
    }

    [Fact]
    public void MasterSeedIsPartOfTheReplayContract()
    {
        using var tmp = new TempDb();
        var (db, seasonId, pack) = PlayedCareer(tmp);
        using var _ = db;

        var report = ReplayService.Resimulate(
            db, pack, DataCareerFixture.MasterSeed + 1, DataCareerFixture.Inputs());

        // Standings rows are seed-independent, but headline selections and season-end rolls
        // are not: the wrong seed must be detected as a divergence.
        Assert.False(report.Identical);
        Assert.NotNull(report.FirstDivergence);
    }

    [Fact]
    public void SuppliedPackMustMatchThePinnedBytes()
    {
        using var tmp = new TempDb();
        var (db, _, pack) = PlayedCareer(tmp);
        using var _2 = db;

        var mutated = pack with { Manifest = pack.Manifest with { Name = "Not The Pinned Pack" } };
        var ex = Assert.Throws<InvalidOperationException>(() => ReplayService.Resimulate(
            db, mutated, DataCareerFixture.MasterSeed, DataCareerFixture.Inputs()));
        Assert.Contains("pinned", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RunSeasonEndRefusesToRunTwice()
    {
        using var tmp = new TempDb();
        var (db, seasonId, pack) = PlayedCareer(tmp);
        using var _ = db;

        Assert.Throws<InvalidOperationException>(() => ReplayService.RunSeasonEnd(
            db, seasonId, pack, DataCareerFixture.MasterSeed, DataCareerFixture.Inputs(), DataCareerFixture.Utc));
    }

    [Fact]
    public void ActiveSeasonReplaysItsRoundsWithoutASeasonEnd()
    {
        using var tmp = new TempDb();
        using var db = CareerDatabase.Open(tmp.Path);
        var (seasonId, pack) = DataCareerFixture.SetupCareer(db);

        // Import two of three rounds, no season end yet — mid-season replay must hold.
        foreach (var round in DataCareerFixture.Rounds().Take(2))
        {
            ReplayService.ImportAndFoldRound(
                db, seasonId, pack, DataCareerFixture.MasterSeed, DataCareerFixture.Inputs(),
                round.Round, DataCareerFixture.Envelope(round), DataCareerFixture.Utc);
        }

        var report = ReplayService.Resimulate(
            db, pack, DataCareerFixture.MasterSeed, DataCareerFixture.Inputs());

        Assert.True(report.Identical);
        // 2 rounds × (9 standings + 5 player-fold rows incl. the headline).
        Assert.Equal(28, report.ComparedRows);
        Assert.Empty(StateStore.ReadOffers(db, seasonId));
        Assert.Equal(2, StateStore.ReadRoundPlayerStates(db, seasonId).Count);
    }

    // ---------- multi-season: rollover re-derivation ----------

    /// <summary>Plays season 1, accepts the team.mid offer, rolls season 2's start states
    /// through <see cref="SeasonRollover"/> exactly like the live path, and folds one
    /// season-2 round.</summary>
    private static (CareerDatabase Db, long Season1, long Season2, Companion.Core.Packs.SeasonPack Pack)
        TwoSeasonCareer(TempDb tmp)
    {
        var db = CareerDatabase.Open(tmp.Path);
        var (season1, pack) = DataCareerFixture.SetupCareer(db);
        DataCareerFixture.PlaySeason(db, season1, pack);

        Assert.Contains(StateStore.ReadOffers(db, season1), o => o.Terms.TeamId == "team.mid");
        StateStore.SetOfferAccepted(db, season1, "team.mid");

        var derived = SeasonRollover.Derive(
            StateStore.ReadPlayerState(db, season1, StateStore.StageEnd)!,
            StateStore.ReadDriverStates(db, season1, StateStore.StageEnd),
            StateStore.ReadTeamStates(db, season1, StateStore.StageEnd),
            acceptedTeamId: "team.mid",
            playerLiveryName: DataCareerFixture.PlayerLivery);

        long season2 = CareerStore.StartSeason(db, 1968, pack.Manifest.PackId, pack.Manifest.Version);
        StateStore.UpsertPlayerState(db, season2, StateStore.StageStart, derived.Player);
        StateStore.UpsertDriverStates(db, season2, StateStore.StageStart, derived.Drivers);
        StateStore.UpsertTeamStates(db, season2, StateStore.StageStart, derived.Teams);

        var round = DataCareerFixture.Rounds()[0];
        ReplayService.ImportAndFoldRound(
            db, season2, pack, DataCareerFixture.MasterSeed, DataCareerFixture.Inputs(),
            1, DataCareerFixture.Envelope(round), DataCareerFixture.Utc);

        return (db, season1, season2, pack);
    }

    [Fact]
    public void FollowOnSeasonStartStatesReDeriveThroughTheRollover()
    {
        using var tmp = new TempDb();
        var (db, _, season2, pack) = TwoSeasonCareer(tmp);
        using var _1 = db;

        var report = ReplayService.Resimulate(
            db, pack, DataCareerFixture.MasterSeed, DataCareerFixture.Inputs());

        Assert.True(report.Identical,
            $"diverged: {report.FirstDivergence?.Reason} stored={report.FirstDivergence?.StoredDeltaJson} " +
            $"regenerated={report.FirstDivergence?.RegeneratedDeltaJson}");
        Assert.Single(StateStore.ReadRoundPlayerStates(db, season2));
    }

    [Fact]
    public void CarryoverViaStartCarryoverSeason_ReplaysByteIdentical()
    {
        using var tmp = new TempDb();
        var db = CareerDatabase.Open(tmp.Path);
        using var _1 = db;
        var (season1, pack) = DataCareerFixture.SetupCareer(db);
        DataCareerFixture.PlaySeason(db, season1, pack);
        StateStore.SetOfferAccepted(db, season1, "team.mid");

        var derived = SeasonRollover.Derive(
            StateStore.ReadPlayerState(db, season1, StateStore.StageEnd)!,
            StateStore.ReadDriverStates(db, season1, StateStore.StageEnd),
            StateStore.ReadTeamStates(db, season1, StateStore.StageEnd),
            acceptedTeamId: "team.mid",
            playerLiveryName: DataCareerFixture.PlayerLivery);

        // A CARRYOVER: reuse the SAME 1967 pack for the next calendar year (1968) via the new
        // store method — the live path when no dedicated 1968 pack exists.
        long season2 = CareerStore.StartCarryoverSeason(
            db, derived, 1968, pack.Manifest.PackId, pack.Manifest.Version, DataCareerFixture.Utc);

        // The season row carries year 1968 while pinned to the 1967 pack (the invariant the
        // carryover deliberately relaxes).
        var record = CareerStore.ReadSeasons(db).Single(s => s.Id == season2);
        Assert.Equal(1968, record.Year);
        Assert.Equal(pack.Manifest.PackId, record.PackId);
        Assert.NotEqual(record.Year, pack.Season.Year);

        var round = DataCareerFixture.Rounds()[0];
        ReplayService.ImportAndFoldRound(
            db, season2, pack, DataCareerFixture.MasterSeed, DataCareerFixture.Inputs(),
            1, DataCareerFixture.Envelope(round), DataCareerFixture.Utc);

        // Same-pack → replay routes through the rollover path and re-derives byte-identically.
        var report = ReplayService.Resimulate(
            db, pack, DataCareerFixture.MasterSeed, DataCareerFixture.Inputs());
        Assert.True(report.Identical,
            $"diverged: {report.FirstDivergence?.Reason} stored={report.FirstDivergence?.StoredDeltaJson} " +
            $"regenerated={report.FirstDivergence?.RegeneratedDeltaJson}");
    }

    [Fact]
    public void TamperedFollowOnStartStateIsAStartStateDivergenceWithZeroDataLoss()
    {
        using var tmp = new TempDb();
        var (db, season1, season2, pack) = TwoSeasonCareer(tmp);
        using var _1 = db;

        // Tamper the season-2 start player row (a rep the rollover never produced).
        var stored = StateStore.ReadPlayerState(db, season2, StateStore.StageStart)!;
        StateStore.UpsertPlayerState(db, season2, StateStore.StageStart, stored with { Reputation = 99.0 });
        var roundStatesBefore = StateStore.ReadRoundPlayerStates(db, season1);

        var report = ReplayService.Resimulate(
            db, pack, DataCareerFixture.MasterSeed, DataCareerFixture.Inputs());

        Assert.False(report.Identical);
        Assert.NotNull(report.FirstDivergence);
        Assert.Equal("start-state", report.FirstDivergence.Reason);
        Assert.Equal(season2, report.FirstDivergence.SeasonId);

        // Rollback: the tampered row still reads back tampered; season-1 folds untouched.
        Assert.Equal(99.0, StateStore.ReadPlayerState(db, season2, StateStore.StageStart)!.Reputation);
        Assert.Equal(roundStatesBefore, StateStore.ReadRoundPlayerStates(db, season1));
    }

    [Fact]
    public void FollowOnSeasonWithoutAnAcceptedOfferIsADivergence()
    {
        using var tmp = new TempDb();
        var (db, season1, _, pack) = TwoSeasonCareer(tmp);
        using var _1 = db;

        // Clear the acceptance: season 2 now exists with no accepted season-1 offer.
        using (var clear = db.Connection.CreateCommand())
        {
            clear.CommandText = "UPDATE offer SET accepted = 0 WHERE season_id = @season;";
            clear.Parameters.AddWithValue("@season", season1);
            clear.ExecuteNonQuery();
        }

        var report = ReplayService.Resimulate(
            db, pack, DataCareerFixture.MasterSeed, DataCareerFixture.Inputs());

        Assert.False(report.Identical);
        Assert.NotNull(report.FirstDivergence);
        Assert.Equal("accepted-offer", report.FirstDivergence.Reason);
    }
}
