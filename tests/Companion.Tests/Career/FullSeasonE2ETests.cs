using System.Text.Json;
using Companion.Core.Career;
using Companion.Core.Determinism;
using Companion.Core.Grid;
using Companion.Core.Packs;
using Companion.Core.Scoring;
using Companion.Data;
using Companion.Tests.Data;
using Companion.Tests.Grid;
using Xunit.Abstractions;

namespace Companion.Tests.Career;

/// <summary>
/// Full-season end-to-end run against the REAL packs/f1-1967 reference pack (loaded from the
/// test output), through the REAL Data layer: create a career in a temp db with a fixed
/// master seed, play all 11 rounds through the unified fold (raw envelope import + standings
/// + player round update, one atomic unit per round), run the season-end pipeline off the
/// round-11 FOLDED player state, then prove the whole career re-simulates byte-identically —
/// per-round events included — and that a divergence is report-only (tamper → rollback →
/// stored data intact).
///
/// The result synthesis is TEST SCAFFOLDING, not product code — the sim never invents race
/// results (they are imported from AMS2). It stands in for the human importing what the game
/// produced: finishing order = merged grid raceSkill + a small per-round deterministic
/// shuffle noise (Pcg32 stream 'results'), so outcomes stay rating-plausible while every
/// round's order differs. Round 6 is the player's mechanical DNF, exercising the envelope's
/// DNF-cause context.
/// </summary>
public class FullSeasonE2ETests(ITestOutputHelper output)
{
    private const ulong MasterSeed = 424242;
    private const string Utc = "2026-07-02T00:00:00Z";

    /// <summary>The player takes over Jo Siffert's Cooper entry (PlayerSeat v1: replace a
    /// historical driver, bound by exact livery) and scores under that entry's driver id.</summary>
    private const string PlayerDriverId = "driver.jo_siffert";
    private const string PlayerLivery = "Cooper-Maserati #12 J. Siffert";
    private const string PlayerTeamId = "team.cooper";

    private const double StartReputation = 35.0;

    /// <summary>The round the player's car expires (mechanical, no blame).</summary>
    private const int PlayerDnfRound = 6;

    /// <summary>Amplitude of the synthetic shuffle noise on the 0..1 raceSkill scale. Small
    /// enough that only rating-adjacent drivers swap, big enough that orders vary per round.</summary>
    private const double ShuffleAmplitude = 0.05;

    [Fact]
    public void Full1967SeasonRunsEndToEndAndReplaysByteIdentical()
    {
        var pack = GridTestData.LoadReferencePack("f1-1967");
        Assert.Equal(1967, pack.Season.Year);
        Assert.Equal(11, pack.Season.Rounds.Count);

        using var tmp = new TempDb();
        using var db = CareerDatabase.Open(tmp.Path);

        // ---- career wizard: create, pin the pack, start the season, seed start states ----
        CareerStore.CreateCareer(db, "1967 E2E Career", MasterSeed, "0.5.0-test", Utc);
        CareerStore.PinPack(db, pack, Utc);
        long seasonId = CareerStore.StartSeason(
            db, pack.Season.Year, pack.Manifest.PackId, pack.Manifest.Version);

        var aiDrivers = pack.Drivers
            .Where(d => !string.Equals(d.Id, PlayerDriverId, StringComparison.Ordinal))
            .Select(d => new DriverCareerState
            {
                DriverId = d.Id,
                Age = pack.Season.Year - (d.Born ?? pack.Season.Year - 30),
            })
            .ToList();
        var teams = pack.Teams
            .Select(t => new TeamCareerState { TeamId = t.Id, LineageId = t.Id, Tier = t.BudgetTier })
            .ToList();
        var playerStart = new PlayerCareerState
        {
            Reputation = StartReputation,
            Opi = 0.0,
            PaceAnchor = 0.0,
            SeasonsCompleted = 0,
            CurrentTeamId = PlayerTeamId,
            LiveryName = PlayerLivery,
        };
        StateStore.UpsertDriverStates(db, seasonId, StateStore.StageStart, aiDrivers);
        StateStore.UpsertTeamStates(db, seasonId, StateStore.StageStart, teams);
        StateStore.UpsertPlayerState(db, seasonId, StateStore.StageStart, playerStart);

        // ---- the season: every round through the unified fold (the live app path) ----
        var inputs = SimInputs(pack);
        var streams = new StreamFactory(MasterSeed);
        var playerSeat = new PlayerSeat { Ams2LiveryName = PlayerLivery };

        RoundFoldResult? lastFold = null;
        foreach (var packRound in pack.Season.Rounds.OrderBy(r => r.Round))
        {
            var grid = RoundGridResolver.Resolve(pack, packRound.Round, playerSeat);
            Assert.Contains(grid.Seats, s => s.IsPlayer);

            bool dnfRound = packRound.Round == PlayerDnfRound;
            var envelope = new RoundResultEnvelope
            {
                Result = SynthesizeResult(grid, streams, playerDnf: dnfRound),
                // The result screen prefilled with the last recommendation; round 1 raced
                // at an arbitrary human pick.
                SliderUsed = lastFold is { State.RecommendedSlider: > 0 }
                    ? lastFold.State.RecommendedSlider
                    : 95.0,
                PlayerDnfCause = dnfRound ? DnfCause.Mechanical : null,
            };

            lastFold = ReplayService.ImportAndFoldRound(
                db, seasonId, pack, MasterSeed, inputs, packRound.Round, envelope, Utc);
            Assert.True(lastFold.PlayerRaced);
        }

        // ---- assert: per-round events journaled, round by round ----
        var roundJournal = JournalStore.ReadSeason(db, seasonId);
        foreach (var packRound in pack.Season.Rounds)
        {
            var phases = roundJournal
                .Where(r => r.Round == packRound.Round)
                .Select(r => r.Phase)
                .ToList();
            Assert.Contains(DataJournalPhases.RoundStandings, phases);
            Assert.Contains(JournalPhases.RaceResult, phases);
            Assert.Contains(JournalPhases.PlayerOpi, phases);
            Assert.Contains(JournalPhases.PlayerReputation, phases);
            Assert.Contains(JournalPhases.PlayerPaceAnchor, phases);
        }
        Assert.Equal("dnf-mechanical", roundJournal
            .First(r => r.Round == PlayerDnfRound && r.Phase == JournalPhases.RaceResult).Cause);

        // ---- assert: the fold accrued reputation round over round (results warrant it) ----
        var folded = StateStore.ReadRoundPlayerState(db, seasonId, 11);
        Assert.NotNull(folded);
        Assert.NotEqual(StartReputation, folded.Player.Reputation);
        Assert.True(folded.Player.PaceAnchor > 0.0, "eleven rounds must calibrate the anchor");

        // ---- season end through the shared fold, consuming the folded final state ----
        var seasonEnd = ReplayService.RunSeasonEnd(db, seasonId, pack, MasterSeed, inputs, Utc);
        Assert.NotEqual(StartReputation, seasonEnd.Player.Reputation);

        var journal = JournalStore.ReadSeason(db, seasonId);
        var repFinalRow = journal.Single(r =>
            r.Phase == JournalPhases.PlayerReputation && r.Cause == "season-final");
        string foldedRep = Math.Round(folded.Player.Reputation, 4)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains($"\"from\":{foldedRep}", repFinalRow.DeltaJson);

        // ---- assert: offers are scored from the FOLDED rep/OPI, not the start state ----
        Assert.NotEmpty(seasonEnd.Offers);
        double salaryAsk = Math.Max(1.0, seasonEnd.Player.Reputation / 10.0);
        double ageRisk = Math.Max(0,
            inputs.PlayerAge + 1 - inputs.AgingCurves.ForYear(pack.Season.Year).PeakAgeEnd);
        foreach (var offer in seasonEnd.Offers)
        {
            var archetype = inputs.Archetypes.ForTeam(offer.Tier, null);
            double expectedScore = Math.Round(TeamArchetypeCatalog.OfferScore(
                archetype,
                seasonEnd.Player.Reputation,
                folded.Player.Opi,
                folded.Player.SeasonsCompleted + 1,
                salaryAsk,
                ageRisk), 4);
            Assert.Equal(expectedScore, offer.Score);
            Assert.Equal(
                inputs.Archetypes.SalaryOffer(offer.Tier, seasonEnd.Player.Reputation),
                offer.SalaryBu);
        }
        var storedOffers = StateStore.ReadOffers(db, seasonId);
        Assert.Equal(
            seasonEnd.Offers.Select(o => o.TeamId),
            storedOffers.Select(o => o.Terms.TeamId));

        // ---- assert: the champion is rating-plausible (top-4 raceSkill in the pack) ----
        var champion = seasonEnd.FinalStandings.Drivers.First(d => d.Position == 1);
        var topRated = pack.Drivers
            .OrderByDescending(d => d.Ratings.RaceSkill)
            .ThenBy(d => d.Id, StringComparer.Ordinal)
            .Take(4)
            .Select(d => d.Id)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains(champion.DriverId, topRated);

        // ---- assert: the journal is non-empty, ordered, rounds before season end ----
        Assert.NotEmpty(journal);
        for (int i = 1; i < journal.Count; i++)
            Assert.True(journal[i].Seq > journal[i - 1].Seq, "journal seq must be strictly increasing");

        var roundRows = journal.Where(r => r.Round is not null).ToList();
        Assert.Equal(11, roundRows.Select(r => r.Round).Distinct().Count());
        Assert.Equal(roundRows.Select(r => r.Round!.Value), roundRows.Select(r => r.Round!.Value).OrderBy(r => r));
        Assert.Equal(11, journal.Count(r => r.Phase == JournalPhases.RaceResult));

        long lastRoundSeq = roundRows.Max(r => r.Seq);
        var seasonEndRows = journal.Where(r => r.Round is null).ToList();
        Assert.NotEmpty(seasonEndRows);
        Assert.All(seasonEndRows, r => Assert.True(
            r.Seq > lastRoundSeq, "season-end rows must follow every round row"));
        Assert.Contains(seasonEndRows, r => r.Phase == JournalPhases.Championship);
        Assert.Contains(seasonEndRows, r => r.Phase == JournalPhases.PlayerExperience);

        // ---- assert: re-simulation from raw results is byte-identical, rounds included ----
        var report = ReplayService.Resimulate(db, pack, MasterSeed, inputs);
        Assert.True(report.Identical,
            $"Replay diverged at season {report.FirstDivergence?.SeasonId} index " +
            $"{report.FirstDivergence?.Index} ({report.FirstDivergence?.Reason}): " +
            $"stored={report.FirstDivergence?.StoredDeltaJson} " +
            $"regenerated={report.FirstDivergence?.RegeneratedDeltaJson}");
        Assert.Null(report.FirstDivergence);
        int storedSimRows = journal.Count(r => !DataJournalPhases.IsProvenance(r.Phase));
        Assert.Equal(storedSimRows, report.ComparedRows);

        // ---- assert: divergence is report-only — tamper → rollback → data intact ----
        StateStore.SetOfferAccepted(db, seasonId, seasonEnd.Offers[0].TeamId);

        var victim = journal.First(r =>
            r.Round == 4 && r.Phase == JournalPhases.RaceResult);
        Tamper(db, victim.Seq, """{"round":4,"expectedFinish":1,"actualFinish":1,"dnf":null}""");

        var storedJournalAfterTamper = JournalStore.ReadSeason(db, seasonId);
        var storedOffersAfterTamper = StateStore.ReadOffers(db, seasonId);
        var storedEndDrivers = StateStore.ReadDriverStates(db, seasonId, StateStore.StageEnd);
        var storedEndTeams = StateStore.ReadTeamStates(db, seasonId, StateStore.StageEnd);
        var storedEndPlayer = StateStore.ReadPlayerState(db, seasonId, StateStore.StageEnd);
        var storedRoundStates = StateStore.ReadRoundPlayerStates(db, seasonId);
        var storedResults = ResultStore.ReadSeasonResults(db, seasonId);

        var tamperedReport = ReplayService.Resimulate(db, pack, MasterSeed, inputs);
        Assert.False(tamperedReport.Identical);
        Assert.Equal("deltaJson", tamperedReport.FirstDivergence!.Reason);
        Assert.Equal(victim.Seq, tamperedReport.FirstDivergence.StoredSeq);

        // Zero data loss: every stored artifact — journal (tamper included), raw results,
        // offers with the accepted flag, end states, per-round folds — reads back untouched.
        Assert.Equal(storedJournalAfterTamper, JournalStore.ReadSeason(db, seasonId));
        Assert.Equal(storedResults, ResultStore.ReadSeasonResults(db, seasonId));
        Assert.Equal(storedOffersAfterTamper, StateStore.ReadOffers(db, seasonId));
        Assert.Single(StateStore.ReadOffers(db, seasonId), o => o.Accepted);
        Assert.Equal(storedEndDrivers, StateStore.ReadDriverStates(db, seasonId, StateStore.StageEnd));
        Assert.Equal(storedEndTeams, StateStore.ReadTeamStates(db, seasonId, StateStore.StageEnd));
        Assert.Equal(storedEndPlayer, StateStore.ReadPlayerState(db, seasonId, StateStore.StageEnd));
        Assert.Equal(storedRoundStates, StateStore.ReadRoundPlayerStates(db, seasonId));

        // ---- and the career recovers: restore the row, replay is identical again ----
        Tamper(db, victim.Seq, victim.DeltaJson);
        var recovered = ReplayService.Resimulate(db, pack, MasterSeed, inputs);
        Assert.True(recovered.Identical);
        Assert.True(StateStore.ReadOffers(db, seasonId)
            .Single(o => o.Terms.TeamId == seasonEnd.Offers[0].TeamId).Accepted);

        // ---- narrative (test output only) ----
        string championName = pack.Drivers.First(d => d.Id == champion.DriverId).Name;
        output.WriteLine($"1967 champion: {championName} ({champion.DriverId}), " +
            $"{champion.CountedPoints} points (position {champion.Position})");

        var playerFinal = seasonEnd.FinalStandings.Drivers
            .FirstOrDefault(d => string.Equals(d.DriverId, PlayerDriverId, StringComparison.Ordinal));
        output.WriteLine($"Player ({PlayerLivery}): championship P{playerFinal?.Position}, " +
            $"{playerFinal?.CountedPoints} points, rep {StartReputation} -> " +
            $"{folded.Player.Reputation:0.##} (folded) -> {seasonEnd.Player.Reputation:0.##} (final)");

        foreach (var offer in seasonEnd.Offers)
            output.WriteLine($"Offer: {offer.TeamId} (tier {offer.Tier}) {offer.SalaryBu:0.##} BU, score {offer.Score}");

        foreach (var row in seasonEndRows.Where(r => r.Phase == JournalPhases.Retirement))
            output.WriteLine($"Retired: {row.Entity} ({row.Cause})");

        var digest = seasonEndRows.LastOrDefault(r =>
            r.Phase == JournalPhases.Headline && r.Entity == "season");
        if (digest is not null)
        {
            using var doc = JsonDocument.Parse(digest.DeltaJson);
            output.WriteLine($"Season digest: {doc.RootElement.GetProperty("text").GetString()}");
        }

        output.WriteLine($"Replay: identical={recovered.Identical}, comparedRows={recovered.ComparedRows}, " +
            $"journalRows={journal.Count}");
    }

    // ---------- helpers ----------

    private static void Tamper(CareerDatabase db, long seq, string deltaJson)
    {
        using var tamper = db.Connection.CreateCommand();
        tamper.CommandText = "UPDATE journal SET delta_json = @delta WHERE seq = @seq;";
        tamper.Parameters.AddWithValue("@delta", deltaJson);
        tamper.Parameters.AddWithValue("@seq", seq);
        tamper.ExecuteNonQuery();
    }

    /// <summary>TEST SCAFFOLDING — stands in for importing a real AMS2 result. Deterministic
    /// plausible classification: merged raceSkill + per-round 'results'-stream shuffle noise.
    /// When <paramref name="playerDnf"/> is set the player retires instead of classifying
    /// (the envelope carries the cause).</summary>
    private static RoundResult SynthesizeResult(GridPlan grid, StreamFactory streams, bool playerDnf)
    {
        var noise = streams.CreateStream("results", grid.Year, grid.Round, "");
        var order = grid.Seats
            .Select(seat => (
                Seat: seat,
                Key: seat.Ratings.RaceSkill + (2.0 * noise.NextDouble() - 1.0) * ShuffleAmplitude))
            .OrderByDescending(x => x.Key)
            .ThenBy(x => x.Seat.DriverId, StringComparer.Ordinal)
            .ToList();

        var entries = new List<ClassifiedEntry>();
        foreach (var (seat, _) in order)
        {
            if (playerDnf && seat.IsPlayer)
                continue;
            entries.Add(new ClassifiedEntry
            {
                DriverId = seat.DriverId,
                ConstructorId = seat.TeamId,
                Position = entries.Count + 1,
            });
        }
        if (playerDnf)
        {
            var player = grid.Seats.Single(s => s.IsPlayer);
            entries.Add(new ClassifiedEntry
            {
                DriverId = player.DriverId,
                ConstructorId = player.TeamId,
                Status = FinishStatus.Retired,
            });
        }

        return new RoundResult
        {
            Round = grid.Round,
            Sessions = [new SessionResult { Kind = SessionKind.Race, Entries = entries }],
        };
    }

    /// <summary>The career-setup facts the wizard would fix. v1 packs carry no canon
    /// retirement/lineage data, so none are passed; three authored free agents let
    /// hazard-retirement vacancies fill deterministically.</summary>
    private static ReplaySimInputs SimInputs(SeasonPack pack) => new()
    {
        AgingCurves = CareerTestData.LoadAgingCurves(),
        Archetypes = CareerTestData.LoadArchetypes(),
        Headlines = CareerTestData.LoadHeadlines(),
        PlayerDriverId = PlayerDriverId,
        PlayerAge = pack.Season.Year - pack.Drivers
            .First(d => string.Equals(d.Id, PlayerDriverId, StringComparison.Ordinal)).Born!.Value,
        FreeAgents =
        [
            new SeatCandidate { DriverId = "driver.fa_merit", RaceSkill = 0.74, Age = 26 },
            new SeatCandidate { DriverId = "driver.fa_veteran", RaceSkill = 0.66, Age = 34 },
            new SeatCandidate { DriverId = "driver.fa_pay", RaceSkill = 0.58, Age = 28, PayBudgetBu = 25.0 },
        ],
        PlayerName = "Test Player",
    };
}
