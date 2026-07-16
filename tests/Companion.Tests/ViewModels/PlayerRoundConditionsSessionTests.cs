using System.Text.Json;
using Companion.Core.Career;
using Companion.Core.Character;
using Companion.Core.Json;
using Companion.Core.Packs;
using Companion.Data;
using Companion.ViewModels.Services;

namespace Companion.Tests.ViewModels;

/// <summary>
/// Session-boundary coverage for progression-v2 conditional player-car physics. These tests use
/// the real rules catalog and SQLite replay path but never locate or write an AMS2 installation.
/// </summary>
public sealed class PlayerRoundConditionsSessionTests : IDisposable
{
    private const long Seed = 20260713;
    private readonly string _root =
        Directory.CreateTempSubdirectory("companion-round-conditions-").FullName;

    [Fact]
    public void RainMan_ExplicitWetAndDryInputsProduceExactAuthoritativePowerScalars()
    {
        using var wet = CreateSession("wet");
        using var dry = CreateSession("dry");

        wet.DeclareCurrentRoundWeather(isWet: true);
        dry.DeclareCurrentRoundWeather(isWet: false);
        // Repeating the same declaration is deliberately idempotent: one INPUT row per round.
        wet.DeclareCurrentRoundWeather(isWet: true);

        var wetPlayer = Assert.Single(wet.CurrentGrid(), seat => seat.IsPlayer);
        var dryPlayer = Assert.Single(dry.CurrentGrid(), seat => seat.IsPlayer);

        Assert.True(wetPlayer.PlayerCarScalarsAuthoritative);
        Assert.True(dryPlayer.PlayerCarScalarsAuthoritative);
        Assert.Equal(1.020, wetPlayer.PowerScalar, 6);
        Assert.Equal(0.992, dryPlayer.PowerScalar, 6);
        Assert.Equal(0.028, wetPlayer.PowerScalar - dryPlayer.PowerScalar, 6);

        AssertSingleConditionRow(wet.CareerFilePath, wet.Pack, expectedWet: true);
        AssertSingleConditionRow(dry.CareerFilePath, dry.Pack, expectedWet: false);
    }

    [Fact]
    public void Preview_WithPersistedRoundConditions_JoinsTransactionAndLeavesDatabaseUnchanged()
    {
        using var session = CreateSession("preview-transaction");
        session.DeclareCurrentRoundWeather(isWet: true);
        IReadOnlyList<Companion.Core.Grid.GridSeat> grid = session.CurrentGrid();

        ConfirmModel preview = session.Preview(Draft(grid, isWet: true));

        Assert.NotEmpty(preview.RoundPoints);
        using var db = CareerDatabase.Open(session.CareerFilePath);
        long seasonId = CareerStore.ReadSeasons(db).Single().Id;
        Assert.Empty(ResultStore.ReadSeasonResults(db, seasonId));
        Assert.Null(StateStore.ReadRoundPlayerState(db, seasonId, 1));
        AssertSingleConditionRow(db, seasonId, session.Pack, expectedWet: true);
    }

    [Fact]
    public void ContradictoryResultWeatherFailsBeforeRawResultIsStored()
    {
        using var session = CreateSession("contradictory-result");
        session.DeclareCurrentRoundWeather(isWet: true);
        IReadOnlyList<Companion.Core.Grid.GridSeat> grid = session.CurrentGrid();

        var error = Assert.Throws<InvalidOperationException>(() => session.Apply(
            Draft(grid, isWet: false)));

        Assert.Contains("staged as wet", error.Message, StringComparison.Ordinal);
        using var db = CareerDatabase.Open(session.CareerFilePath);
        long seasonId = CareerStore.ReadSeasons(db).Single().Id;
        Assert.Empty(ResultStore.ReadSeasonResults(db, seasonId));
        Assert.Single(PlayerRoundConditionsStore.ReadSeason(db, seasonId, session.Pack));
        Assert.Null(StateStore.ReadRoundPlayerState(db, seasonId, 1));
    }

    [Fact]
    public void AppliedWeatherCopiesToEnvelope_ReplaysIdentically_AndTamperRollsBackDerivedState()
    {
        using var session = CreateSession("replay-and-tamper");
        session.DeclareCurrentRoundWeather(isWet: true);
        IReadOnlyList<Companion.Core.Grid.GridSeat> grid = session.CurrentGrid();
        session.Apply(Draft(grid, isWet: true));

        using var db = CareerDatabase.Open(session.CareerFilePath);
        long seasonId = CareerStore.ReadSeasons(db).Single().Id;
        StoredRoundResult stored = Assert.Single(ResultStore.ReadSeasonResults(db, seasonId));
        RoundResultEnvelope envelope = stored.ToEnvelope();
        Assert.True(envelope.IsWet);
        AssertSingleConditionRow(db, seasonId, session.Pack, expectedWet: true);

        ReplayReport identical = ReplayService.Resimulate(db, unchecked((ulong)Seed), Inputs());
        Assert.True(
            identical.Identical,
            $"diverged: {identical.FirstDivergence?.Reason} " +
            $"stored={identical.FirstDivergence?.StoredDeltaJson} " +
            $"regenerated={identical.FirstDivergence?.RegeneratedDeltaJson}");

        var roundStatesBefore = StateStore.ReadRoundPlayerStates(db, seasonId);
        var derivedRowsBefore = JournalStore.ReadSeason(db, seasonId)
            .Where(row => !DataJournalPhases.IsProvenance(row.Phase))
            .ToArray();

        RoundResultEnvelope tampered = envelope with { IsWet = false };
        ResultStore.Append(
            db,
            seasonId,
            round: 1,
            JsonSerializer.Serialize(tampered, CoreJson.Options),
            "2026-07-13T02:00:00.0000000Z",
            source: "tamper-test");

        ReplayReport report = ReplayService.Resimulate(db, unchecked((ulong)Seed), Inputs());

        Assert.False(report.Identical);
        Assert.Equal("round-conditions", report.FirstDivergence?.Reason);
        Assert.Equal(roundStatesBefore, StateStore.ReadRoundPlayerStates(db, seasonId));
        Assert.Equal(
            derivedRowsBefore,
            JournalStore.ReadSeason(db, seasonId)
                .Where(row => !DataJournalPhases.IsProvenance(row.Phase))
                .ToArray());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private CareerSessionService CreateSession(string name)
    {
        string packDirectory = Path.Combine(_root, name, "packs", "1967");
        TestPackBuilder.Write(TestPackBuilder.TwoRoundPack(), packDirectory);
        string careerPath = Path.Combine(_root, name, name + ".ams2career");
        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, name, "documents"),
            library: TestPackBuilder.Library());
        environment.PackSearchRoots = () => [Path.Combine(_root, name, "packs")];

        return CareerSessionService.CreateCareer(
            new CareerCreationRequest
            {
                PackDirectory = packDirectory,
                CareerFilePath = careerPath,
                CareerName = "Round conditions test",
                MasterSeed = Seed,
                ExperienceMode = CareerExperienceModes.GrandPrixDynasty,
                PlayerLiveryName = TestPackBuilder.StockLivery2,
                Character = RainMan(),
            },
            environment);
    }

    private static ResultDraft Draft(
        IReadOnlyList<Companion.Core.Grid.GridSeat> grid,
        bool isWet) => new()
    {
        Classified = grid.Select(seat => seat.DriverId).ToList(),
        DidNotFinish = new Dictionary<string, string>(),
        Disqualified = [],
        IsWet = isWet,
    };

    private static CharacterProfile RainMan()
    {
        var talent = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["pace"] = 0.70,
            ["oneLap"] = 0.65,
            ["craft"] = 0.60,
            ["racecraft"] = 0.62,
            ["adaptability"] = 0.58,
        };
        var meta = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["marketability"] = 0.50,
            ["durability"] = 0.55,
        };

        return new CharacterProfile
        {
            Name = "Rain Driver",
            Age = 22,
            Stats = talent.Concat(meta).ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal),
            PerkIds = ["rain_man"],
            CreationPerkIds = ["rain_man"],
            ProgressionVersion = CharacterLevelProgression.Level300Version,
            MasteryEffectsVersion = CharacterProfile.CurrentMasteryEffectsVersion,
            ExpectationModelVersion = CharacterProfile.CurrentExpectationModelVersion,
            RacingDnaId = "dna_circuit_specialist",
            RacingDnaVersion = 1,
            RacingDnaChoice = "technical",
            CreationBaseline = new CharacterCreationBaseline
            {
                Stats = talent,
                Meta = meta,
                TraitIds = ["rain_man"],
            },
        };
    }

    private static ReplaySimInputs Inputs()
    {
        CareerRulesData rules = CareerRulesData.Load(ViewModelTestData.RulesDirectory);
        return new ReplaySimInputs
        {
            AgingCurves = rules.AgingCurves,
            Archetypes = rules.Archetypes,
            Headlines = rules.Headlines,
            PlayerDriverId = "driver.hulme",
            PlayerAge = 22,
            CharacterRules = rules.Character,
            MasterySkills = rules.MasterySkills,
        };
    }

    private static void AssertSingleConditionRow(
        string careerPath,
        SeasonPack pack,
        bool expectedWet)
    {
        using var db = CareerDatabase.Open(careerPath);
        long seasonId = CareerStore.ReadSeasons(db).Single().Id;
        AssertSingleConditionRow(db, seasonId, pack, expectedWet);
    }

    private static void AssertSingleConditionRow(
        CareerDatabase db,
        long seasonId,
        SeasonPack pack,
        bool expectedWet)
    {
        KeyValuePair<int, PlayerRoundConditionsInput> entry =
            Assert.Single(PlayerRoundConditionsStore.ReadSeason(db, seasonId, pack));
        Assert.Equal(1, entry.Key);
        Assert.Equal(expectedWet, entry.Value.IsWet);
        Assert.Single(
            JournalStore.ReadSeason(db, seasonId),
            row => row.Phase == JournalPhases.PlayerRoundConditions && row.Round == 1);
    }
}
