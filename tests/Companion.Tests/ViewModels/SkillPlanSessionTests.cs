using System.Text.Json;
using Companion.Core.Career;
using Companion.Core.Character;
using Companion.Core.Json;
using Companion.Core.Packs;
using Companion.Data;
using Companion.ViewModels.Services;

namespace Companion.Tests.ViewModels;

/// <summary>
/// Persistence-boundary coverage for progression-v2 development. The session may only append one
/// canonical INPUT after the complete season review exists; preview and every rejected request are
/// strictly read-only, and the folded stage=end state is left for rollover to consume.
/// </summary>
public sealed class SkillPlanSessionTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("companion-skill-plan-session-").FullName;

    [Fact]
    public void ApplySkillPlan_BeforeSeasonReviewRejectsWithoutAppendingARow()
    {
        string careerPath = CareerPath("before-review");
        using var session = CreateCareer(careerPath);

        var error = Assert.Throws<InvalidOperationException>(() =>
            session.ApplySkillPlan(["pace_rhythm"]));

        Assert.Contains("completed season review", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(ReadSkillPlanRows(careerPath));
    }

    [Fact]
    public void PreviewSkillPlan_IsReadOnlyAndProjectsCanonicalCostAndPendingNodes()
    {
        string careerPath = CareerPath("preview");
        using var session = CreateCompletedCareer(careerPath);
        string endStateBefore = ReadEndStateJson(careerPath);
        string[] requested =
        [
            "pace_rhythm",
            "pace_qualifying_sequence",
            "attribute_pace_01",
            "attribute_pace_02",
        ];

        SkillPlanPreview preview = session.PreviewSkillPlan(requested);

        Assert.Equal(CharacterSkillPlanInput.CurrentEffectsVersion, preview.Input.EffectsVersion);
        Assert.Equal(requested, preview.Input.Entries.Select(entry => entry.NodeId));
        Assert.Equal(
            ["mastery", "mastery", "attribute", "attribute"],
            preview.Input.Entries.Select(entry => entry.Kind));
        Assert.Equal([1, 2, 1, 1], preview.Input.Entries.Select(entry => entry.Cost));
        Assert.Equal(5, preview.Input.TotalCost);
        Assert.Equal(CharacterProgressionV2Math.LifetimeSkillPoints - 5, preview.SkillPointsAfterPlan);
        foreach (string nodeId in requested)
        {
            var node = preview.ProjectedTree.Branches
                .SelectMany(branch => branch.Nodes)
                .Single(candidate => string.Equals(candidate.Id, nodeId, StringComparison.Ordinal));
            Assert.Equal(SkillNodeState.Pending, node.State);
        }

        Assert.Empty(ReadSkillPlanRows(careerPath));
        Assert.Equal(endStateBefore, ReadEndStateJson(careerPath));
    }

    [Fact]
    public void ApplySkillPlan_ValidMultiNodeRequestAppendsOneCanonicalRowWithoutMutatingEndState()
    {
        string careerPath = CareerPath("valid");
        using var session = CreateCompletedCareer(careerPath);
        string endStateBefore = ReadEndStateJson(careerPath);
        string[] requested =
        [
            "pace_rhythm",
            "pace_qualifying_sequence",
            "attribute_pace_01",
            "attribute_pace_02",
        ];

        session.ApplySkillPlan(requested);

        JournalRow row = Assert.Single(ReadSkillPlanRows(careerPath));
        Assert.Null(row.Round);
        Assert.Equal("player", row.Entity);
        Assert.Equal("development", row.Cause);
        var persisted = JsonSerializer.Deserialize<CharacterSkillPlanInput>(
            row.DeltaJson, CoreJson.Options)!;
        Assert.Equal(CharacterSkillPlanInput.CurrentVersion, persisted.Version);
        Assert.Equal(CharacterLevelProgression.Level300Version, persisted.ProgressionVersion);
        Assert.Equal(CharacterSkillPlanInput.CurrentEffectsVersion, persisted.EffectsVersion);
        Assert.Equal(requested, persisted.Entries.Select(entry => entry.NodeId));
        Assert.Equal(
            ["mastery", "mastery", "attribute", "attribute"],
            persisted.Entries.Select(entry => entry.Kind));
        Assert.Equal([1, 2, 1, 1], persisted.Entries.Select(entry => entry.Cost));
        Assert.Equal(5, persisted.TotalCost);
        Assert.Equal(endStateBefore, ReadEndStateJson(careerPath));
    }

    [Fact]
    public void ApplySkillPlan_DuplicateOrUnknownLaterNodeAppendsNothing()
    {
        string careerPath = CareerPath("invalid-later-node");
        using var session = CreateCompletedCareer(careerPath);
        string endStateBefore = ReadEndStateJson(careerPath);

        Assert.Throws<InvalidOperationException>(() =>
            session.ApplySkillPlan(["pace_rhythm", "pace_rhythm"]));
        Assert.Empty(ReadSkillPlanRows(careerPath));

        Assert.Throws<InvalidOperationException>(() =>
            session.ApplySkillPlan(["pace_rhythm", "unknown_node"]));
        Assert.Empty(ReadSkillPlanRows(careerPath));
        Assert.Equal(endStateBefore, ReadEndStateJson(careerPath));
    }

    [Fact]
    public void ApplySkillPlan_SecondOverlappingConfirmationRejectsWithoutChangingRowCount()
    {
        string careerPath = CareerPath("overlap");
        using var session = CreateCompletedCareer(careerPath);
        session.ApplySkillPlan(["pace_rhythm"]);
        JournalRow first = Assert.Single(ReadSkillPlanRows(careerPath));
        string endStateBeforeSecondAttempt = ReadEndStateJson(careerPath);

        Assert.Throws<InvalidOperationException>(() =>
            session.ApplySkillPlan(["pace_rhythm", "pace_telemetry_habit"]));

        JournalRow after = Assert.Single(ReadSkillPlanRows(careerPath));
        Assert.Equal(first, after);
        Assert.Equal(endStateBeforeSecondAttempt, ReadEndStateJson(careerPath));
    }

    [Fact]
    public void CharacterDossier_ProjectsRiskFromAConfirmedMasteryInjuryDrawback()
    {
        string careerPath = CareerPath("mastery-injury-risk");
        using var session = CreateCompletedCareer(careerPath);

        session.ApplySkillPlan(
        [
            "racecraft_clean_overtake",
            "racecraft_first_lap_reader",
            "racecraft_switchback_school",
        ]);

        CharacterDossier dossier = Assert.IsType<CharacterDossier>(session.CharacterDossier());
        Assert.Equal("Low", dossier.InjuryRisk);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private CareerSessionService CreateCompletedCareer(string careerPath)
    {
        CareerSessionService session = CreateCareer(careerPath);
        while (!session.Summary.SeasonComplete)
        {
            var grid = session.CurrentGrid();
            var classified = grid.Select(seat => seat.DriverId).ToList();
            string playerId = grid.Single(seat => seat.IsPlayer).DriverId;
            Assert.True(classified.Remove(playerId));
            classified.Insert(0, playerId);
            session.Apply(new ResultDraft
            {
                Classified = classified,
                DidNotFinish = new Dictionary<string, string>(),
                Disqualified = [],
            });
        }

        Assert.NotNull(session.SeasonReview());
        Assert.Equal(
            CharacterProgressionV2Math.LifetimeSkillPoints,
            session.AvailableCharacterCp());
        return session;
    }

    private CareerSessionService CreateCareer(string careerPath)
    {
        string packDirectory = Path.Combine(_root, "packs", "2020");
        if (!Directory.Exists(packDirectory))
            TestPackBuilder.Write(SyntheticPack(), packDirectory);
        return CareerSessionService.CreateCareer(new CareerCreationRequest
        {
            PackDirectory = packDirectory,
            CareerFilePath = careerPath,
            CareerName = "Atomic skill-plan session",
            MasterSeed = 20260713,
            ExperienceMode = CareerExperienceModes.GrandPrixDynasty,
            PlayerLiveryName = TestPackBuilder.StockLivery2,
            Character = VersionTwoCharacter(),
        }, Environment());
    }

    private CareerEnvironment Environment()
    {
        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "documents"),
            library: TestPackBuilder.Library());
        environment.PackSearchRoots = () => [Path.Combine(_root, "packs")];
        return environment;
    }

    private string CareerPath(string name) =>
        Path.Combine(_root, "careers", name + ".ams2career");

    private static SeasonPack SyntheticPack()
    {
        var pack = TestPackBuilder.TwoRoundPack();
        return pack with
        {
            Manifest = pack.Manifest with
            {
                PackId = "skill-plan-2020",
                Name = "Skill Plan 2020",
            },
            Season = pack.Season with
            {
                Year = 2020,
                SeriesName = "Skill Plan Championship",
                Rounds =
                [
                    TestPackBuilder.Round(1, "2020-01-02"),
                    TestPackBuilder.Round(2, "2020-05-07"),
                ],
            },
        };
    }

    private static CharacterProfile VersionTwoCharacter()
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
            Name = "Session Plan Driver",
            Age = 22,
            Stats = talent.Concat(meta)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            PerkIds = ["engineers_favorite"],
            CreationPerkIds = ["engineers_favorite"],
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
                TraitIds = ["engineers_favorite"],
            },
        };
    }

    private static IReadOnlyList<JournalRow> ReadSkillPlanRows(string careerPath)
    {
        using var db = CareerDatabase.Open(careerPath);
        long seasonId = CareerStore.ReadSeasons(db).Single().Id;
        return JournalStore.ReadSeason(db, seasonId)
            .Where(row => string.Equals(
                row.Phase, JournalPhases.PlayerSkillPlan, StringComparison.Ordinal))
            .ToArray();
    }

    private static string ReadEndStateJson(string careerPath)
    {
        using var db = CareerDatabase.Open(careerPath);
        long seasonId = CareerStore.ReadSeasons(db).Single().Id;
        using var command = db.Connection.CreateCommand();
        command.CommandText =
            "SELECT state_json FROM player_state WHERE season_id = @season AND stage = 'end';";
        command.Parameters.AddWithValue("@season", seasonId);
        return Assert.IsType<string>(command.ExecuteScalar());
    }
}
