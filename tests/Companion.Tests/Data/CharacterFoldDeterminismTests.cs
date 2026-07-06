using Companion.Core.Character;
using Companion.Core.Career;
using Companion.Core.Determinism;
using Companion.Data;
using Companion.Tests.ViewModels;
using Companion.ViewModels.Services;

namespace Companion.Tests.Data;

/// <summary>
/// Increment 4a determinism gate for the character system (CI matrix row 4): a career that carries a
/// character folds WITH it (the player seat is patched, the perk modifier shapes OPI/rep/anchor) and
/// re-simulates byte-identically — the <c>player.character</c> creation row is provenance-excluded
/// while its data rides in the start player state. A character-free career is unaffected (proven by
/// the rest of the suite staying byte-identical).
/// </summary>
public sealed class CharacterFoldDeterminismTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("companion-character-fold-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static CharacterProfile Character() => new()
    {
        Name = "Ace McTest",
        Stats = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["pace"] = 0.85, ["oneLap"] = 0.60, ["craft"] = 0.50,
            ["racecraft"] = 0.50, ["adaptability"] = 0.50,
            ["marketability"] = 0.70, ["durability"] = 0.50,
        },
        PerkIds = ["engineers_favorite"], // power +0.010, drag -0.008 — a real on-track car edge
        CpUnspent = 2,
    };

    private static CharacterProfile FragileInjuryCharacter() => new()
    {
        Name = "Glass Cannon",
        Stats = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["pace"] = 0.60, ["oneLap"] = 0.55, ["craft"] = 0.40, ["racecraft"] = 0.50,
            ["adaptability"] = 0.50, ["marketability"] = 0.55, ["durability"] = 0.25,
        },
        PerkIds = ["glass_cannon"], // stream: injury → the season-end injury roll auto-enables
        CpUnspent = 0,
    };

    [Fact]
    public void InjuryPerkCharacter_RollsInjuryAtSeasonEnd_AndReplaysByteIdentically()
    {
        string packDirectory = Path.Combine(_root, "pack");
        TestPackBuilder.Write(TestPackBuilder.TwoRoundPack(), packDirectory);
        string careerPath = Path.Combine(_root, "careers", "injury.ams2career");

        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs"),
            library: TestPackBuilder.Library());

        const string playerId = "driver.hulme";
        const long seed = 987654321;
        Companion.Core.Packs.SeasonPack pack;

        using (var session = CareerSessionService.CreateCareer(
                   new CareerCreationRequest
                   {
                       PackDirectory = packDirectory,
                       CareerFilePath = careerPath,
                       CareerName = "Injury Fold",
                       MasterSeed = seed,
                       PlayerLiveryName = TestPackBuilder.StockLivery2,
                       Character = FragileInjuryCharacter(),
                   },
                   environment))
        {
            pack = session.Pack;
            for (int round = 0; round < 2; round++)
            {
                var grid = session.CurrentGrid();
                session.Apply(new ResultDraft
                {
                    Classified = grid.Select(s => s.DriverId).ToList(),
                    DidNotFinish = new Dictionary<string, string>(),
                    Disqualified = [],
                });
            }
            // Trigger the season-end pipeline (the injury roll lives there).
            Assert.NotNull(session.SeasonReview());
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using var db = CareerDatabase.Open(careerPath);
        long seasonId = CareerStore.ReadSeasons(db).Single().Id;

        // The injury roll is a deterministic draw on the injury stream — recompute it and assert the
        // player.injury row's presence matches (proving the roll fires and is provenance-correct).
        var rules = CareerRulesData.Load(ViewModelTestData.RulesDirectory);
        var mods = PerkResolver.Resolve(["glass_cannon"], rules.Character);
        double hazard = InjuryModel.Hazard(0.25, mods);
        double roll = new StreamFactory(unchecked((ulong)seed))
            .CreateStream(CareerStreams.Injury, pack.Season.Year, 0, "player").NextDouble();
        bool expectInjury = roll < hazard;

        var journal = JournalStore.ReadSeason(db, seasonId);
        Assert.Equal(expectInjury, journal.Any(r => r.Phase == JournalPhases.PlayerInjury));

        var inputs = new ReplaySimInputs
        {
            AgingCurves = rules.AgingCurves,
            Archetypes = rules.Archetypes,
            Headlines = rules.Headlines,
            PlayerDriverId = playerId,
            PlayerAge = 30,
            CharacterRules = rules.Character,
        };
        var report = ReplayService.Resimulate(db, pack, unchecked((ulong)seed), inputs);
        Assert.True(report.Identical,
            $"diverged: {report.FirstDivergence?.Reason} stored={report.FirstDivergence?.StoredDeltaJson} " +
            $"regenerated={report.FirstDivergence?.RegeneratedDeltaJson}");
    }

    [Fact]
    public void CharacterCareer_FoldsWithTheCharacterAndReplaysByteIdentically()
    {
        string packDirectory = Path.Combine(_root, "pack");
        TestPackBuilder.Write(TestPackBuilder.TwoRoundPack(), packDirectory);
        string careerPath = Path.Combine(_root, "careers", "character.ams2career");

        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs"),
            library: TestPackBuilder.Library());

        const string playerId = "driver.hulme";
        const long seed = 42424242;
        Companion.Core.Packs.SeasonPack pack;

        using (var session = CareerSessionService.CreateCareer(
                   new CareerCreationRequest
                   {
                       PackDirectory = packDirectory,
                       CareerFilePath = careerPath,
                       CareerName = "Character Fold",
                       MasterSeed = seed,
                       PlayerLiveryName = TestPackBuilder.StockLivery2,
                       Character = Character(),
                   },
                   environment))
        {
            pack = session.Pack;

            // The character bites the STAGED grid: the player seat's raceSkill is the talent-stat
            // write (pace 0.85 → 0.35 + 0.55*0.85 = 0.8175), not the pack baseline.
            var grid1 = session.CurrentGrid();
            var playerSeat = grid1.Single(s => string.Equals(s.DriverId, playerId, StringComparison.Ordinal));
            Assert.Equal(0.8175, playerSeat.Ratings.RaceSkill, 6);

            session.Apply(new ResultDraft
            {
                Classified = grid1.Select(s => s.DriverId).ToList(),
                DidNotFinish = new Dictionary<string, string>(),
                Disqualified = [],
            });

            var grid2 = session.CurrentGrid();
            session.Apply(new ResultDraft
            {
                Classified = grid2.Select(s => s.DriverId).ToList(),
                DidNotFinish = new Dictionary<string, string>(),
                Disqualified = [],
            });
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using var db = CareerDatabase.Open(careerPath);
        long seasonId = CareerStore.ReadSeasons(db).Single().Id;

        // The creation INPUT row is present (round = null), and the character rode into the start
        // player state (Level 1, perks preserved).
        var journal = JournalStore.ReadSeason(db, seasonId);
        Assert.Contains(journal, r => r.Round is null && r.Phase == JournalPhases.PlayerCharacter);

        var start = StateStore.ReadPlayerState(db, seasonId, StateStore.StageStart)!;
        Assert.True(start.HasCharacter);
        Assert.Equal(1, start.Level);
        Assert.Equal("Ace McTest", start.Character!.Name); // the chosen driver name folds + persists
        Assert.Equal(["engineers_favorite"], start.Character.PerkIds);

        // The character accrues XP: a player.xp row per raced round, and the folded state carries a
        // level (>= the creation level 1). A character-free career emits no player.xp row.
        Assert.Equal(2, journal.Count(r => r.Round is not null && r.Phase == JournalPhases.PlayerXp));
        var round2State = StateStore.ReadRoundPlayerState(db, seasonId, 2)!;
        Assert.True(round2State.Player.Level >= 1);

        // The whole character career re-simulates byte-identically. The re-sim MUST receive the same
        // character rules the live fold used; the player.character row being provenance-excluded is
        // proven by there being no "extra stored row" divergence.
        var rules = CareerRulesData.Load(ViewModelTestData.RulesDirectory);
        var inputs = new ReplaySimInputs
        {
            AgingCurves = rules.AgingCurves,
            Archetypes = rules.Archetypes,
            Headlines = rules.Headlines,
            PlayerDriverId = playerId,
            PlayerAge = 30,
            CharacterRules = rules.Character,
        };

        var report = ReplayService.Resimulate(db, pack, unchecked((ulong)seed), inputs);

        Assert.True(report.Identical,
            $"diverged: {report.FirstDivergence?.Reason} stored={report.FirstDivergence?.StoredDeltaJson} " +
            $"regenerated={report.FirstDivergence?.RegeneratedDeltaJson}");
        Assert.True(report.ComparedRows > 0);
    }
}
