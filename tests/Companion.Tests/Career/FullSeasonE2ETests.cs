using System.Text.Json;
using Companion.Core.Career;
using Companion.Core.Determinism;
using Companion.Core.Grid;
using Companion.Core.Json;
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
/// master seed, play all 11 rounds (resolve the grid, import a synthesized result, journal
/// the refolded standings), run the season-end pipeline, then prove the whole career
/// re-simulates byte-identically.
///
/// The result synthesis is TEST SCAFFOLDING, not product code — the sim never invents race
/// results (they are imported from AMS2). It stands in for the human importing what the game
/// produced: finishing order = merged grid raceSkill + a small per-round deterministic
/// shuffle noise (Pcg32 stream 'results'), so outcomes stay rating-plausible while every
/// round's order differs.
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
            Reputation = 35.0,
            Opi = 0.0,
            PaceAnchor = 0.0,
            SeasonsCompleted = 0,
            CurrentTeamId = PlayerTeamId,
            LiveryName = PlayerLivery,
        };
        StateStore.UpsertDriverStates(db, seasonId, StateStore.StageStart, aiDrivers);
        StateStore.UpsertTeamStates(db, seasonId, StateStore.StageStart, teams);
        StateStore.UpsertPlayerState(db, seasonId, StateStore.StageStart, playerStart);

        // ---- the season: resolve grid -> import synthesized result -> journal standings ----
        var streams = new StreamFactory(MasterSeed);
        var playerSeat = new PlayerSeat { Ams2LiveryName = PlayerLivery };
        var playedSoFar = new List<RoundResult>();

        foreach (var packRound in pack.Season.Rounds.OrderBy(r => r.Round))
        {
            var grid = RoundGridResolver.Resolve(pack, packRound.Round, playerSeat);
            Assert.Contains(grid.Seats, s => s.IsPlayer);

            var result = SynthesizeResult(grid, streams);
            playedSoFar.Add(result);

            string payload = JsonSerializer.Serialize(result, CoreJson.Options);
            var import = ResultStore.Append(db, seasonId, packRound.Round, payload, Utc);
            Assert.False(import.ReImported);

            JournalStore.AppendMany(
                db, seasonId, packRound.Round,
                ReplayService.RoundStandingsEvents(pack, playedSoFar), Utc);
        }

        // ---- season end through the shared fold (the live app path) ----
        var inputs = SimInputs(pack);
        var seasonEnd = ReplayService.RunSeasonEnd(db, seasonId, pack, MasterSeed, inputs, Utc);

        // ---- assert: the champion is rating-plausible (top-4 raceSkill in the pack) ----
        var champion = seasonEnd.FinalStandings.Drivers.First(d => d.Position == 1);
        var topRated = pack.Drivers
            .OrderByDescending(d => d.Ratings.RaceSkill)
            .ThenBy(d => d.Id, StringComparer.Ordinal)
            .Take(4)
            .Select(d => d.Id)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains(champion.DriverId, topRated);

        // ---- assert: a configured player seat yields offer letters ----
        Assert.NotEmpty(seasonEnd.Offers);
        var storedOffers = StateStore.ReadOffers(db, seasonId);
        Assert.NotEmpty(storedOffers);
        Assert.Equal(
            seasonEnd.Offers.Select(o => o.TeamId),
            storedOffers.Select(o => o.Terms.TeamId));

        // ---- assert: the journal is non-empty and ordered ----
        var journal = JournalStore.ReadSeason(db, seasonId);
        Assert.NotEmpty(journal);
        for (int i = 1; i < journal.Count; i++)
            Assert.True(journal[i].Seq > journal[i - 1].Seq, "journal seq must be strictly increasing");

        var roundRows = journal.Where(r => r.Round is not null).ToList();
        Assert.Equal(11, roundRows.Select(r => r.Round).Distinct().Count());
        Assert.Equal(roundRows.Select(r => r.Round!.Value), roundRows.Select(r => r.Round!.Value).OrderBy(r => r));

        long lastRoundSeq = roundRows.Max(r => r.Seq);
        var seasonEndRows = journal.Where(r => r.Round is null).ToList();
        Assert.NotEmpty(seasonEndRows);
        Assert.All(seasonEndRows, r => Assert.True(
            r.Seq > lastRoundSeq, "season-end rows must follow every round row"));
        Assert.Contains(seasonEndRows, r => r.Phase == JournalPhases.Championship);

        // ---- assert: re-simulation from raw results is byte-identical ----
        var report = ReplayService.Resimulate(db, pack, MasterSeed, inputs);
        Assert.True(report.Identical,
            $"Replay diverged at season {report.FirstDivergence?.SeasonId} index " +
            $"{report.FirstDivergence?.Index} ({report.FirstDivergence?.Reason}): " +
            $"stored={report.FirstDivergence?.StoredDeltaJson} " +
            $"regenerated={report.FirstDivergence?.RegeneratedDeltaJson}");
        Assert.Null(report.FirstDivergence);
        int storedSimRows = journal.Count(r =>
            !r.Phase.StartsWith(DataJournalPhases.AuditPrefix, StringComparison.Ordinal));
        Assert.Equal(storedSimRows, report.ComparedRows);

        // ---- narrative (test output only) ----
        string championName = pack.Drivers.First(d => d.Id == champion.DriverId).Name;
        output.WriteLine($"1967 champion: {championName} ({champion.DriverId}), " +
            $"{champion.CountedPoints} points (position {champion.Position})");

        var playerFinal = seasonEnd.FinalStandings.Drivers
            .FirstOrDefault(d => string.Equals(d.DriverId, PlayerDriverId, StringComparison.Ordinal));
        output.WriteLine($"Player ({PlayerLivery}): championship P{playerFinal?.Position}, " +
            $"{playerFinal?.CountedPoints} points, rep {playerStart.Reputation} -> {seasonEnd.Player.Reputation:0.##}");

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

        output.WriteLine($"Replay: identical={report.Identical}, comparedRows={report.ComparedRows}, " +
            $"journalRows={journal.Count}");
    }

    // ---------- helpers ----------

    /// <summary>TEST SCAFFOLDING — stands in for importing a real AMS2 result. Deterministic
    /// plausible classification: merged raceSkill + per-round 'results'-stream shuffle noise,
    /// everyone classified (DNF-free seasons keep the assertion surface crisp).</summary>
    private static RoundResult SynthesizeResult(GridPlan grid, StreamFactory streams)
    {
        var noise = streams.CreateStream("results", grid.Year, grid.Round, "");
        var order = grid.Seats
            .Select(seat => (
                Seat: seat,
                Key: seat.Ratings.RaceSkill + (2.0 * noise.NextDouble() - 1.0) * ShuffleAmplitude))
            .OrderByDescending(x => x.Key)
            .ThenBy(x => x.Seat.DriverId, StringComparer.Ordinal)
            .ToList();

        return new RoundResult
        {
            Round = grid.Round,
            Sessions =
            [
                new SessionResult
                {
                    Kind = SessionKind.Race,
                    Entries = order.Select((x, i) => new ClassifiedEntry
                    {
                        DriverId = x.Seat.DriverId,
                        ConstructorId = x.Seat.TeamId,
                        Position = i + 1,
                    }).ToList(),
                },
            ],
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
