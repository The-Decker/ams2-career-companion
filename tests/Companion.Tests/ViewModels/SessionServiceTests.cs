using Companion.Core.Numerics;
using Companion.Core.Career;
using Companion.Core.Character;
using Companion.Data;
using Companion.ViewModels.Services;

namespace Companion.Tests.ViewModels;

/// <summary>
/// Round-trip coverage for <see cref="CareerSessionService"/> against a temp career DB and
/// the REAL packs/f1-1967 from test output: create → briefing → preview → apply →
/// standings → next round → reopen from the pinned pack, plus the staging outcomes
/// (preflight errors abort, missing install degrades, backup-first, force gate).
/// </summary>
public sealed class SessionServiceTests : IDisposable
{
    private const string PlayerLivery = "Brabham-Repco #2 D. Hulme";

    private readonly string _root = Directory.CreateTempSubdirectory("companion-session-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // SQLite WAL sidecars can outlive the connection briefly on Windows; leaking a
            // temp folder is better than failing the suite.
        }
    }

    private string CareerPath => Path.Combine(_root, "career.ams2career");

    private string DocumentsDirectory => Path.Combine(_root, "docs");

    private CareerCreationRequest Request(string? packDirectory = null) => new()
    {
        PackDirectory = packDirectory ?? ViewModelTestData.RealPackDirectory,
        CareerFilePath = CareerPath,
        CareerName = "Test 1967",
        MasterSeed = 42,
        PlayerLiveryName = PlayerLivery,
    };

    private string CopyRealPack()
    {
        string target = Path.Combine(_root, "pack");
        Directory.CreateDirectory(target);
        foreach (string file in Directory.GetFiles(ViewModelTestData.RealPackDirectory, "*.json"))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        return target;
    }

    private static CharacterProfile VersionOneExpectationCharacter()
    {
        var talent = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["pace"] = 0.50,
            ["oneLap"] = 0.50,
            ["craft"] = 0.50,
            ["racecraft"] = 0.50,
            ["adaptability"] = 0.50,
        };
        var meta = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["marketability"] = 0.50,
            ["durability"] = 0.50,
        };
        return new CharacterProfile
        {
            Name = "Unstarted Driver",
            Age = 22,
            Stats = talent.Concat(meta).ToDictionary(
                pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            PerkIds = [],
            CreationPerkIds = [],
            ProgressionVersion = CharacterLevelProgression.Level300Version,
            MasteryEffectsVersion = CharacterProfile.CurrentMasteryEffectsVersion,
            ExpectationModelVersion = 1,
            RacingDnaId = "dna_all_rounder",
            RacingDnaVersion = 1,
            CreationBaseline = new CharacterCreationBaseline
            {
                Stats = talent,
                Meta = meta,
                TraitIds = [],
            },
        };
    }

    // ---------- the full round-trip ----------

    [Fact]
    public void UnstartedVersionOneExpectationProfile_UpgradesBeforeFirstBenchmark()
    {
        var environment = ViewModelTestData.Environment(DocumentsDirectory);

        using (CareerSessionService.CreateCareer(Request(), environment))
        {
        }

        using (var oldDb = CareerDatabase.Open(CareerPath))
        {
            long oldSeasonId = Assert.Single(CareerStore.ReadSeasons(oldDb)).Id;
            var oldStart = Assert.IsType<PlayerCareerState>(
                StateStore.ReadPlayerState(oldDb, oldSeasonId, StateStore.StageStart));
            StateStore.UpsertPlayerState(
                oldDb,
                oldSeasonId,
                StateStore.StageStart,
                oldStart with { Character = VersionOneExpectationCharacter() });
        }

        using (var session = CareerSessionService.OpenCareer(CareerPath, environment))
        {
            Assert.Null(session.Summary.Opi);
            Assert.NotNull(session.CurrentExpectedFinish());
        }

        using var db = CareerDatabase.Open(CareerPath);
        long seasonId = Assert.Single(CareerStore.ReadSeasons(db)).Id;
        var start = StateStore.ReadPlayerState(db, seasonId, StateStore.StageStart);
        CharacterProfile character = Assert.IsType<CharacterProfile>(start?.Character);
        Assert.Equal(
            CharacterProfile.CurrentExpectationModelVersion,
            character.ExpectationModelVersion);
        Assert.Empty(ResultStore.ReadSeasonResults(db, seasonId));
    }

    [Fact]
    public void CreatePreviewApplyReopen_RoundTripsOnTheRealPack()
    {
        string packDirectory = CopyRealPack();
        var environment = ViewModelTestData.Environment(DocumentsDirectory);

        List<string> gridOrder;
        using (var session = CareerSessionService.CreateCareer(Request(packDirectory), environment))
        {
            var summary = session.Summary;
            Assert.Equal("Test 1967", summary.CareerName);
            Assert.Equal(1967, summary.SeasonYear);
            Assert.Equal("Formula One World Championship", summary.SeriesName);
            Assert.Equal(1, summary.CurrentRound);
            Assert.Equal(11, summary.RoundCount);
            Assert.Equal("driver.denny_hulme", summary.PlayerDriverId);
            Assert.Equal(PlayerLivery, summary.PlayerLiveryName);
            Assert.Null(summary.PlayerPosition);
            Assert.False(summary.SeasonComplete);

            var briefing = session.CurrentBriefing();
            Assert.NotNull(briefing);
            Assert.Equal("South African Grand Prix", briefing.Round.Name);
            Assert.Equal("Kyalami Racing Circuit", briefing.VenueDisplayName);
            Assert.False(briefing.IsPlaceholder);

            // Grid = the pack entries whose rounds range covers round 1 (the player
            // REPLACES one of them, replacing never grows the grid).
            var grid = session.CurrentGrid();
            int expectedSeats = session.Pack.Entries.Count(e =>
                Companion.Core.Packs.RoundsRange.TryParse(e.Rounds, out var range, out _) &&
                range.Contains(1));
            Assert.Equal(expectedSeats, grid.Count);
            Assert.True(grid.Count >= 10);
            var player = Assert.Single(grid, s => s.IsPlayer);
            Assert.Equal(PlayerLivery, player.Ams2LiveryName);
            Assert.Equal("driver.denny_hulme", player.DriverId);

            // Full classification in grid order; the last two retire.
            gridOrder = grid.Select(s => s.DriverId).ToList();
            var draft = new ResultDraft
            {
                Classified = gridOrder.Take(gridOrder.Count - 2).ToList(),
                DidNotFinish = new Dictionary<string, string>
                {
                    [gridOrder[^2]] = "m",
                    [gridOrder[^1]] = "a",
                },
                Disqualified = [],
            };

            var confirm = session.Preview(draft);

            // 1967 points: 9-6-4-3-2-1, the confirm model must equal engine output.
            Assert.Equal(new Rational(9), confirm.RoundPoints.Single(p => p.DriverId == gridOrder[0]).Points);
            Assert.Equal(new Rational(6), confirm.RoundPoints.Single(p => p.DriverId == gridOrder[1]).Points);
            Assert.Equal(new Rational(1), confirm.RoundPoints.Single(p => p.DriverId == gridOrder[5]).Points);
            Assert.Equal(Rational.Zero, confirm.RoundPoints.Single(p => p.DriverId == gridOrder[6]).Points);
            Assert.Equal(Rational.Zero, confirm.RoundPoints.Single(p => p.DriverId == gridOrder[^1]).Points);

            // The confirm headline comes from the unified fold's RoundUpdate (the M5 news
            // engine replaced the static template) and previews deterministically.
            Assert.False(string.IsNullOrWhiteSpace(confirm.Headline));
            Assert.Equal(confirm.Headline, session.Preview(draft).Headline);

            var winnerMove = confirm.Movements.Single(m => m.DriverId == gridOrder[0]);
            Assert.Null(winnerMove.From); // no standings before round 1
            Assert.Equal(1, winnerMove.To);

            // Preview must not commit anything, not even through its fold preview.
            Assert.Null(session.CurrentStandings());
            Assert.Equal(1, session.Summary.CurrentRound);
            Assert.Null(session.CurrentSliderRecommendation());

            session.Apply(draft);

            summary = session.Summary;
            Assert.Equal(2, summary.CurrentRound);
            Assert.Equal(2, summary.PlayerPosition); // Hulme P2 on 6 points
            Assert.False(summary.SeasonComplete);

            // Apply went through the fold: the home header reads the folded player state
            // and the next round has a difficulty recommendation.
            Assert.NotNull(summary.Reputation);
            Assert.NotNull(summary.Opi);
            int? recommendation = session.CurrentSliderRecommendation();
            Assert.NotNull(recommendation);
            Assert.InRange(recommendation.Value, 70, 120);
            Assert.Equal(recommendation, session.CurrentBriefing()!.RecommendedSlider);

            var standings = session.CurrentStandings();
            Assert.NotNull(standings);
            Assert.Equal(1, standings.AfterRound);
            var leader = Assert.Single(standings.Drivers, d => d.Position == 1);
            Assert.Equal(gridOrder[0], leader.DriverId);
            Assert.Equal(new Rational(9), leader.CountedPoints);
            Assert.Single(session.AllSnapshots());
        }

        // Delete the mutable pack folder: reopening MUST rehydrate from the pinned bytes.
        Directory.Delete(packDirectory, recursive: true);

        var reopenEnvironment = ViewModelTestData.Environment(DocumentsDirectory);
        using var reopened = CareerSessionService.OpenCareer(CareerPath, reopenEnvironment);

        var reopenedSummary = reopened.Summary;
        Assert.Equal("Test 1967", reopenedSummary.CareerName);
        Assert.Equal(2, reopenedSummary.CurrentRound);
        Assert.Equal("driver.denny_hulme", reopenedSummary.PlayerDriverId);
        Assert.Equal(PlayerLivery, reopenedSummary.PlayerLiveryName);
        Assert.Equal(2, reopenedSummary.PlayerPosition);
        Assert.Equal(42, reopened.MasterSeed);

        // The folded player state persisted with the career: trend + recommendation survive.
        Assert.NotNull(reopenedSummary.Reputation);
        Assert.NotNull(reopened.CurrentSliderRecommendation());

        var nextBriefing = reopened.CurrentBriefing();
        Assert.NotNull(nextBriefing);
        Assert.Equal("Monaco Grand Prix", nextBriefing.Round.Name);

        var reopenedStandings = reopened.CurrentStandings();
        Assert.NotNull(reopenedStandings);
        Assert.Equal(gridOrder[0], Assert.Single(reopenedStandings.Drivers, d => d.Position == 1).DriverId);
    }

    [Fact]
    public void ReadFeed_ProjectsTheRoundHeadline_WithAWhyChip()
    {
        var environment = ViewModelTestData.Environment(DocumentsDirectory);
        using var session = CareerSessionService.CreateCareer(Request(), environment);

        // No race run yet → the paddock is quiet.
        Assert.Empty(session.ReadFeed());

        var gridOrder = session.CurrentGrid().Select(s => s.DriverId).ToList();
        session.Apply(new ResultDraft
        {
            Classified = gridOrder, // full classification, nobody retires
            DidNotFinish = new Dictionary<string, string>(),
            Disqualified = [],
        });

        // One race → one headline dispatch, projected read-only off the journal.
        var latest = Assert.Single(session.ReadFeed());
        Assert.False(string.IsNullOrWhiteSpace(latest.Headline));
        Assert.Equal(1967, latest.SeasonYear);
        Assert.Equal(1, latest.Round);
        Assert.Equal("race", latest.Kind);
        Assert.Contains("expected", latest.WhyText); // the Why? chip explains the number

        // The generative grammar fills a full period-voiced article body from the round's
        // facts, non-empty, with no unresolved slots, and mentioning the race by name.
        Assert.False(string.IsNullOrWhiteSpace(latest.Body));
        Assert.DoesNotContain("{", latest.Body);
        Assert.DoesNotContain("}", latest.Body);
        Assert.Contains("South African Grand Prix", latest.Body);
    }

    [Fact]
    public void ReadFeed_SeasonDigest_CarriesAPeriodVoicedSeasonInReviewBody()
    {
        var environment = ViewModelTestData.Environment(DocumentsDirectory);
        using var session = CareerSessionService.CreateCareer(Request(), environment);

        // Play every championship round (a clean full classification each time).
        for (int i = 0; i < 40 && !session.Summary.SeasonComplete; i++)
        {
            var gridOrder = session.CurrentGrid().Select(s => s.DriverId).ToList();
            session.Apply(new ResultDraft
            {
                Classified = gridOrder,
                DidNotFinish = new Dictionary<string, string>(),
                Disqualified = [],
            });
        }
        Assert.True(session.Summary.SeasonComplete, "the season should complete after its rounds are applied");

        // Completing the season runs the season-end pipeline, which journals the round-less
        // season-digest headline. The feed hangs a generated "season in review" body on it.
        Assert.NotNull(session.SeasonReview());

        // Several round-less headlines are season-kind (promoted/relegated, retirement
        // foreshadow), but ONLY the season-digest gets a composed article body, so exactly one
        // season dispatch carries a body, and that is the season-in-review.
        var seasonDispatch = Assert.Single(
            session.ReadFeed(), d => d.Kind == "season" && !string.IsNullOrWhiteSpace(d.Body));
        Assert.False(string.IsNullOrWhiteSpace(seasonDispatch.Headline));
        Assert.DoesNotContain("{", seasonDispatch.Body);
        Assert.DoesNotContain("}", seasonDispatch.Body);
        Assert.Contains("1967", seasonDispatch.Body); // {year}, every season template names it

        // Determinism: the derived body re-renders identically on a second read.
        var second = Assert.Single(
            session.ReadFeed(), d => d.Kind == "season" && !string.IsNullOrWhiteSpace(d.Body));
        Assert.Equal(seasonDispatch.Body, second.Body);
    }

    [Fact]
    public void ReadFeed_ArticleBodies_AreByteIdenticalAcrossCalls()
    {
        var environment = ViewModelTestData.Environment(DocumentsDirectory);
        using var session = CareerSessionService.CreateCareer(Request(), environment);

        // Run three rounds with varied outcomes so several phase|cause corpora are exercised.
        for (int round = 0; round < 3; round++)
        {
            var gridOrder = session.CurrentGrid().Select(s => s.DriverId).ToList();
            var draft = round switch
            {
                // Round 1: a clean win from the front (winner facts flow through).
                0 => new ResultDraft
                {
                    Classified = gridOrder,
                    DidNotFinish = new Dictionary<string, string>(),
                    Disqualified = [],
                },
                // Round 2: the player retires (DNF body path).
                1 => new ResultDraft
                {
                    Classified = gridOrder.Where(id => id != "driver.denny_hulme").ToList(),
                    DidNotFinish = new Dictionary<string, string> { ["driver.denny_hulme"] = "m" },
                    Disqualified = [],
                },
                // Round 3: mid-pack, reverse the order so the player runs down the field.
                _ => new ResultDraft
                {
                    Classified = Enumerable.Reverse(gridOrder).ToList(),
                    DidNotFinish = new Dictionary<string, string>(),
                    Disqualified = [],
                },
            };
            session.Apply(draft);
        }

        // Projection stability: the same career/journal renders byte-identical articles on
        // every read, the body is derived, seeded, never a stored input.
        var first = session.ReadFeed();
        var second = session.ReadFeed();

        Assert.Equal(first.Count, second.Count);
        Assert.True(first.Count >= 3);
        for (int i = 0; i < first.Count; i++)
        {
            Assert.Equal(first[i].Headline, second[i].Headline);
            Assert.Equal(first[i].Body, second[i].Body);
            Assert.Equal(first[i].WhyText, second[i].WhyText);
            Assert.False(string.IsNullOrWhiteSpace(first[i].Body));
            Assert.DoesNotContain("{", first[i].Body);
        }

        // Reopening the career from its pinned bytes must reproduce identical bodies, the
        // render is a pure function of the journal + master seed, independent of session state.
        session.Dispose();
        using var reopened = CareerSessionService.OpenCareer(CareerPath, ViewModelTestData.Environment(DocumentsDirectory));
        var reread = reopened.ReadFeed();
        Assert.Equal(first.Count, reread.Count);
        for (int i = 0; i < first.Count; i++)
            Assert.Equal(first[i].Body, reread[i].Body);
    }

    // ---------- history / scrapbook projection (Increment 3) ----------

    [Fact]
    public void CareerTimeline_IsEmptyBeforeAnyRound()
    {
        var environment = ViewModelTestData.Environment(DocumentsDirectory);
        using var session = CareerSessionService.CreateCareer(Request(), environment);

        // The season row exists but has no applied round yet, one in-progress card, empty book.
        var timeline = session.CareerTimeline();
        var card = Assert.Single(timeline.Seasons);
        Assert.Equal(1967, card.SeasonYear);
        Assert.False(card.IsComplete);
        Assert.Equal(0, card.RoundsApplied);
        Assert.Null(card.PlayerPosition);
        Assert.Null(card.ChampionName);

        // No race applied → the records book carries no bests and zeroed counts.
        Assert.Null(timeline.Records.BestFinish);
        Assert.Equal(0, timeline.Records.Wins);
        Assert.Equal(0, timeline.Records.Podiums);
        Assert.Equal(0, timeline.Records.SeasonsRaced);
    }

    [Fact]
    public void CareerTimeline_ProjectsTheSeasonCardAndAggregatesRecords_WhenThePlayerWinsEveryRace()
    {
        var environment = ViewModelTestData.Environment(DocumentsDirectory);
        using var session = CareerSessionService.CreateCareer(Request(), environment);

        // Drive the whole 1967 season with the player (Hulme) winning every round, a clean,
        // deterministic projection to assert bests/streaks/champion against.
        int rounds = session.Summary.RoundCount;
        for (int round = 0; round < rounds; round++)
        {
            var grid = session.CurrentGrid().Select(s => s.DriverId).ToList();
            string player = session.Summary.PlayerDriverId;
            // Player first, then the rest in grid order, a win every race.
            var classified = new List<string> { player };
            classified.AddRange(grid.Where(id => id != player));
            session.Apply(new ResultDraft
            {
                Classified = classified,
                DidNotFinish = new Dictionary<string, string>(),
                Disqualified = [],
            });
        }

        Assert.True(session.Summary.SeasonComplete);

        var timeline = session.CareerTimeline();
        var card = Assert.Single(timeline.Seasons);
        Assert.Equal(1967, card.SeasonYear);
        Assert.True(card.IsComplete);
        Assert.Equal(rounds, card.RoundsApplied);
        Assert.Equal(1, card.PlayerPosition);          // won the championship
        Assert.True(card.PlayerIsChampion);
        Assert.NotNull(card.ChampionName);
        Assert.NotNull(card.FinalReputation);          // folded season-end state
        Assert.NotNull(card.FinalOpi);
        Assert.NotEmpty(card.Headlines);               // the season's archived clippings

        var records = timeline.Records;
        Assert.Equal(1, records.BestFinish);           // P1 is the best possible
        Assert.Equal(rounds, records.Wins);            // won every race
        Assert.Equal(rounds, records.Podiums);         // every win is a podium
        Assert.Equal(rounds, records.LongestWinStreak);
        Assert.Equal(rounds, records.LongestPodiumStreak);
        Assert.Equal(1, records.Championships);
        Assert.Equal(1, records.SeasonsRaced);
        Assert.True(records.TotalPoints > 0);
    }

    [Fact]
    public void CareerTimeline_ReDerivesIdentically_AcrossTwoIndependentReads()
    {
        // Projection stability (career-hub-build.md determinism matrix, Increment 3): the
        // "relive it forever" promise is a pure read, so two reads of the same career must be
        // value-identical (guards against dictionary/culture-sensitive ordering).
        var environment = ViewModelTestData.Environment(DocumentsDirectory);
        using var session = CareerSessionService.CreateCareer(Request(), environment);

        var grid = session.CurrentGrid().Select(s => s.DriverId).ToList();
        string player = session.Summary.PlayerDriverId;
        session.Apply(new ResultDraft
        {
            Classified = new List<string> { player }.Concat(grid.Where(id => id != player)).ToList(),
            DidNotFinish = new Dictionary<string, string>(),
            Disqualified = [],
        });

        var first = session.CareerTimeline();
        var second = session.CareerTimeline();

        Assert.Equal(first.Seasons.Count, second.Seasons.Count);
        Assert.Equal(first.Seasons[0].PlayerPosition, second.Seasons[0].PlayerPosition);
        Assert.Equal(first.Seasons[0].ChampionName, second.Seasons[0].ChampionName);
        Assert.Equal(first.Seasons[0].Headlines, second.Seasons[0].Headlines);
        Assert.Equal(first.Records.BestFinish, second.Records.BestFinish);
        Assert.Equal(first.Records.Wins, second.Records.Wins);
        Assert.Equal(first.Records.TotalPoints, second.Records.TotalPoints);
    }

    [Fact]
    public void QualifyingOrder_IsInertToScoring_AndThePackWeekendIsSingleRace()
    {
        var environment = ViewModelTestData.Environment(DocumentsDirectory);
        using var session = CareerSessionService.CreateCareer(Request(), environment);

        // v0.4.0: the bundled packs author the real historical weekend, practice + qualifying +
        // ONE Grand Prix (no sprints in these eras), so scoring stays single-race shaped.
        var weekend = session.CurrentWeekend();
        Assert.NotNull(weekend);
        Assert.True(weekend.Qualifying is { Present: true });
        Assert.Single(weekend.Races);
        Assert.Equal("Grand Prix", weekend.Races[0].Label);

        var gridOrder = session.CurrentGrid().Select(s => s.DriverId).ToList();
        var draft = new ResultDraft
        {
            Classified = gridOrder,
            DidNotFinish = new Dictionary<string, string>(),
            Disqualified = [],
            QualifyingOrder = gridOrder.AsEnumerable().Reverse().ToList(), // any qualy order
        };

        // Qualifying rides in the envelope but never enters RoundResult, so the scored round is
        // byte-identical with or without it, the standings engine (and the oracle) never see it.
        var withQualy = session.Preview(draft);
        var withoutQualy = session.Preview(draft with { QualifyingOrder = null });
        Assert.Equal(
            withoutQualy.RoundPoints.Select(p => (p.DriverId, p.Points)),
            withQualy.RoundPoints.Select(p => (p.DriverId, p.Points)));
    }

    [Fact]
    public void Preview_RejectsDriversNotInTheRoundGrid()
    {
        var environment = ViewModelTestData.Environment(DocumentsDirectory);
        using var session = CareerSessionService.CreateCareer(Request(), environment);

        var draft = new ResultDraft
        {
            Classified = ["driver.not_in_this_grid"],
            DidNotFinish = new Dictionary<string, string>(),
            Disqualified = [],
        };

        var ex = Assert.Throws<ArgumentException>(() => session.Preview(draft));
        Assert.Contains("not in the round-1 grid", ex.Message);
    }

    [Fact]
    public void CreateCareer_RefusesAnExistingCareerFile()
    {
        var environment = ViewModelTestData.Environment(DocumentsDirectory);
        Directory.CreateDirectory(_root);
        File.WriteAllText(CareerPath, "already here");

        var ex = Assert.Throws<InvalidOperationException>(
            () => CareerSessionService.CreateCareer(Request(), environment));
        Assert.Contains("already exists", ex.Message);
    }

    [Fact]
    public void CreateCareer_OnACustomLivery_SeatsAnOwnEntrant()
    {
        // Player-as-own-entrant: a livery that is not a pack entry (a custom/non-standard skin) no longer
        // refuses, the player is seated as their own independent synthetic entrant so the career runs.
        var environment = ViewModelTestData.Environment(DocumentsDirectory);
        var request = Request() with { PlayerLiveryName = "No Such Livery" };

        using var session = CareerSessionService.CreateCareer(request, environment);
        var player = Assert.Single(session.CurrentGrid(), s => s.IsPlayer);
        Assert.Equal("No Such Livery", player.Ams2LiveryName);
        Assert.Equal(Companion.Core.Grid.RoundGridResolver.SyntheticPlayerDriverId, player.DriverId);
    }

    // ---------- staging ----------

    private string FakeInstallDirectory => Path.Combine(_root, "install");

    private string StagedFilePath =>
        Path.Combine(FakeInstallDirectory, "UserData", "CustomAIDrivers", "F-Vintage_Gen1.xml");

    [Fact]
    public void StageCurrentGrid_MissingInstall_FailsGracefully()
    {
        var environment = ViewModelTestData.Environment(DocumentsDirectory, installDirectory: null);
        using var session = CareerSessionService.CreateCareer(Request(), environment);

        var outcome = session.StageCurrentGrid();

        Assert.False(outcome.Success);
        Assert.Null(outcome.WrittenPath);
        Assert.Contains(outcome.Messages, m => m.Contains("No AMS2 installation"));
    }

    [Fact]
    public void StageCurrentGrid_PreflightError_AbortsBeforeWriting()
    {
        // An empty library makes the vehicle class unknown, a preflight ERROR.
        var environment = ViewModelTestData.Environment(
            DocumentsDirectory, FakeInstallDirectory, ViewModelTestData.EmptyLibrary());
        using var session = CareerSessionService.CreateCareer(Request(), environment);

        var outcome = session.StageCurrentGrid();

        Assert.False(outcome.Success);
        Assert.Null(outcome.WrittenPath);
        Assert.Contains(outcome.Messages,
            m => m.StartsWith("Error:") && m.Contains("'F-Vintage_Gen1' is not in the content library"));
        Assert.Contains(outcome.Messages, m => m.Contains("Staging aborted"));
        Assert.False(File.Exists(StagedFilePath));
    }

    [Fact]
    public void StageCurrentGrid_WarningsOnly_StagesBackupFirst()
    {
        // Real library, no installed skin packs: livery findings are warnings, which must
        // NOT abort staging.
        var environment = ViewModelTestData.Environment(DocumentsDirectory, FakeInstallDirectory);
        using var session = CareerSessionService.CreateCareer(Request(), environment);

        var first = session.StageCurrentGrid();
        Assert.True(first.Success);
        Assert.Equal(StagedFilePath, first.WrittenPath);
        Assert.True(File.Exists(StagedFilePath));
        Assert.Null(first.BackupPath); // nothing existed before the first stage
        Assert.False(first.NoOpAlreadyMatches);

        // Re-staging the identical round is a diff-aware NO-OP (NAMeS-first, locked decision
        // #7b): the installed file already matches, so nothing is written or backed up.
        var second = session.StageCurrentGrid();
        Assert.True(second.Success);
        Assert.True(second.NoOpAlreadyMatches);
        Assert.Null(second.BackupPath);
    }

    [Fact]
    public void StageCurrentGrid_ForeignFile_RequiresForce_AndForceStillBacksUp()
    {
        var environment = ViewModelTestData.Environment(DocumentsDirectory, FakeInstallDirectory);
        using var session = CareerSessionService.CreateCareer(Request(), environment);

        // A curated community file (no generated marker) already sits at the target.
        Directory.CreateDirectory(Path.GetDirectoryName(StagedFilePath)!);
        const string communityFile = "<custom_ai_drivers><!-- hand made --></custom_ai_drivers>";
        File.WriteAllText(StagedFilePath, communityFile);

        var refused = session.StageCurrentGrid();
        Assert.False(refused.Success);
        // The gate is an EXPECTED choice, not a failure: the outcome says so explicitly and
        // carries the calm explanation the amber banner shows.
        Assert.True(refused.BlockedByForceGate);
        Assert.Contains(refused.Messages, m =>
            m.Contains("Your installed F-Vintage_Gen1.xml differs from this round's grid") &&
            m.Contains("'Overwrite anyway' takes a timestamped backup first"));
        Assert.Equal(communityFile, File.ReadAllText(StagedFilePath)); // untouched

        var forced = ((IForceStaging)session).StageCurrentGrid(force: true);
        Assert.True(forced.Success);
        Assert.NotNull(forced.BackupPath);
        Assert.Equal(communityFile, File.ReadAllText(forced.BackupPath!)); // backup-first
        Assert.NotEqual(communityFile, File.ReadAllText(StagedFilePath));
    }
}
