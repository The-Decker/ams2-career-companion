using System.Text.Json;
using Companion.Core.Career;
using Companion.Core.Character;
using Companion.Core.Json;
using Companion.Core.Packs;
using Companion.Data;
using Companion.ViewModels.Services;

namespace Companion.Tests.ViewModels;

/// <summary>
/// Persistence-boundary coverage for progression-v2's XP-funded committed-tree reset. Quotes and
/// rejected requests are read-only; an accepted reset is one canonical INPUT interleaved with skill
/// plans while the completed season's stage=end state remains immutable for rollover/replay.
/// </summary>
public sealed class SkillResetSessionTests : IDisposable
{
    private static readonly string[] MultiNodePlan =
    [
        "pace_rhythm",
        "pace_qualifying_sequence",
        "attribute_pace_01",
        "attribute_pace_02",
    ];

    private readonly string _root =
        Directory.CreateTempSubdirectory("companion-skill-reset-session-").FullName;

    [Fact]
    public void PreviewAndApplySkillReset_EmptyCommittedTreeAreReadOnlyAndBlocked()
    {
        string careerPath = CareerPath("empty-tree");
        using var session = CreateCompletedCareer(careerPath);
        string endStateBefore = ReadEndStateJson(careerPath);
        var dossier = Assert.IsType<CharacterDossier>(session.CharacterDossier());

        SkillResetPreview preview = Assert.IsType<SkillResetPreview>(session.PreviewSkillReset());

        Assert.False(preview.CanApply);
        Assert.Contains("no committed skill tree", preview.BlockReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(dossier.LifetimeXp, preview.LifetimeXp);
        Assert.Equal(dossier.AvailableResetXp, preview.AvailableResetXp);
        Assert.True(preview.Cost > 0);
        Assert.Equal(0, preview.SkillPointsRefunded);
        Assert.Equal(CharacterProgressionV2Math.LifetimeSkillPoints, preview.SkillPointsAfterReset);
        Assert.Equal(0, preview.AcquisitionCount);
        Assert.Null(preview.Input);
        Assert.Null(preview.ProjectedState);
        Assert.Empty(ReadSkillResetRows(careerPath));
        Assert.Equal(endStateBefore, ReadEndStateJson(careerPath));

        var error = Assert.Throws<InvalidOperationException>(session.ApplySkillReset);

        Assert.Contains("no committed skill tree", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(ReadSkillResetRows(careerPath));
        Assert.Empty(ReadDevelopmentRows(careerPath));
        Assert.Equal(endStateBefore, ReadEndStateJson(careerPath));
    }

    [Fact]
    public void ApplySkillReset_AfterPlanAppendsOneCanonicalInputAndImmediatelyRefundsTheTree()
    {
        string careerPath = CareerPath("canonical-reset");
        using var session = CreateCompletedCareer(careerPath);
        string endStateBefore = ReadEndStateJson(careerPath);
        session.ApplySkillPlan(MultiNodePlan);

        SkillResetPreview preview = Assert.IsType<SkillResetPreview>(session.PreviewSkillReset());

        Assert.True(preview.CanApply);
        Assert.Equal("", preview.BlockReason);
        Assert.Equal(5, preview.SkillPointsRefunded);
        Assert.Equal(CharacterProgressionV2Math.LifetimeSkillPoints, preview.SkillPointsAfterReset);
        Assert.Equal(4, preview.AcquisitionCount);
        Assert.True(preview.Cost > 0);
        Assert.Equal(preview.AvailableResetXp - preview.Cost, preview.AvailableResetXpAfter);
        CharacterSkillResetInput quotedInput = Assert.IsType<CharacterSkillResetInput>(preview.Input);
        Assert.Equal(
            ["attribute_pace_01", "attribute_pace_02", "pace_qualifying_sequence", "pace_rhythm"],
            quotedInput.PriorAcquisitions.Select(entry => entry.NodeId));
        Assert.Equal(
            ["attribute", "attribute", "mastery", "mastery"],
            quotedInput.PriorAcquisitions.Select(entry => entry.Kind));
        Assert.Equal([1, 1, 2, 1], quotedInput.PriorAcquisitions.Select(entry => entry.Cost));
        Assert.Equal(5, quotedInput.RefundedSkillPoints);
        Assert.Equal(preview.Cost, quotedInput.XpCost);

        var projected = Assert.IsType<PlayerCareerState>(preview.ProjectedState);
        Assert.Equal(preview.LifetimeXp, projected.Xp);
        Assert.Equal(preview.Cost, projected.Character!.XpSpentOnResets);
        Assert.Equal(1, projected.Character.SkillResetCount);
        Assert.Equal(0, projected.Character.SkillPointsSpent);
        Assert.Null(projected.Character.AcquiredSkillIds);
        Assert.Null(projected.Character.AcquiredAttributeNodeIds);

        // Preview must not consume the quote, append a reset row, or rewrite the authoritative end state.
        Assert.Empty(ReadSkillResetRows(careerPath));
        Assert.Single(ReadSkillPlanRows(careerPath));
        Assert.Equal(endStateBefore, ReadEndStateJson(careerPath));

        session.ApplySkillReset();

        JournalRow row = Assert.Single(ReadSkillResetRows(careerPath));
        Assert.Null(row.Round);
        Assert.Equal("player", row.Entity);
        Assert.Equal("development", row.Cause);
        Assert.Equal(JsonSerializer.Serialize(quotedInput, CoreJson.Options), row.DeltaJson);
        var persisted = JsonSerializer.Deserialize<CharacterSkillResetInput>(
            row.DeltaJson, CoreJson.Options)!;
        Assert.Equal(CharacterSkillResetInput.CurrentVersion, persisted.Version);
        Assert.Equal(CharacterLevelProgression.Level300Version, persisted.ProgressionVersion);
        Assert.Equal(quotedInput.PolicyVersion, persisted.PolicyVersion);
        Assert.Equal(quotedInput.XpCost, persisted.XpCost);
        Assert.Equal(quotedInput.RefundedSkillPoints, persisted.RefundedSkillPoints);
        Assert.Equal(
            quotedInput.PriorAcquisitions.Select(entry =>
                (entry.NodeId, entry.Kind, entry.Cost)),
            persisted.PriorAcquisitions.Select(entry =>
                (entry.NodeId, entry.Kind, entry.Cost)));
        Assert.Equal(endStateBefore, ReadEndStateJson(careerPath));

        Assert.Equal(preview.SkillPointsAfterReset, session.AvailableCharacterCp());
        var dossierAfter = Assert.IsType<CharacterDossier>(session.CharacterDossier());
        Assert.Equal(preview.LifetimeXp, dossierAfter.LifetimeXp);
        Assert.Equal(preview.AvailableResetXpAfter, dossierAfter.AvailableResetXp);
        Assert.Equal(preview.SkillPointsAfterReset, dossierAfter.CpUnspent);
        SkillTreeSnapshot tree = Assert.IsType<SkillTreeSnapshot>(session.SkillTree());
        foreach (string nodeId in MultiNodePlan)
        {
            SkillNode node = tree.Branches
                .SelectMany(branch => branch.Nodes)
                .Single(candidate => string.Equals(candidate.Id, nodeId, StringComparison.Ordinal));
            Assert.NotEqual(SkillNodeState.Owned, node.State);
            Assert.NotEqual(SkillNodeState.Pending, node.State);
        }
    }

    [Fact]
    public void ApplySkillReset_SecondEmptyResetAppendsNothingAndReplacementPlanIsAllowed()
    {
        string careerPath = CareerPath("replacement-plan");
        using var session = CreateCompletedCareer(careerPath);
        string endStateBefore = ReadEndStateJson(careerPath);
        session.ApplySkillPlan(["pace_rhythm"]);
        session.ApplySkillReset();
        JournalRow firstReset = Assert.Single(ReadSkillResetRows(careerPath));

        SkillResetPreview emptyPreview = Assert.IsType<SkillResetPreview>(session.PreviewSkillReset());
        Assert.False(emptyPreview.CanApply);
        Assert.Equal(0, emptyPreview.AcquisitionCount);
        Assert.Throws<InvalidOperationException>(session.ApplySkillReset);

        JournalRow afterRejectedReset = Assert.Single(ReadSkillResetRows(careerPath));
        Assert.Equal(firstReset, afterRejectedReset);
        Assert.Equal(
            [JournalPhases.PlayerSkillPlan, JournalPhases.PlayerSkillReset],
            ReadDevelopmentRows(careerPath).Select(row => row.Phase));
        Assert.Equal(endStateBefore, ReadEndStateJson(careerPath));

        session.ApplySkillPlan(["pace_rhythm"]);

        Assert.Equal(2, ReadSkillPlanRows(careerPath).Count);
        Assert.Single(ReadSkillResetRows(careerPath));
        Assert.Equal(
            [
                JournalPhases.PlayerSkillPlan,
                JournalPhases.PlayerSkillReset,
                JournalPhases.PlayerSkillPlan,
            ],
            ReadDevelopmentRows(careerPath).Select(row => row.Phase));
        Assert.Equal(CharacterProgressionV2Math.LifetimeSkillPoints - 1, session.AvailableCharacterCp());
        SkillNode pace = Assert.IsType<SkillTreeSnapshot>(session.SkillTree()).Branches
            .SelectMany(branch => branch.Nodes)
            .Single(node => string.Equals(node.Id, "pace_rhythm", StringComparison.Ordinal));
        Assert.Equal(SkillNodeState.Pending, pace.State);
        Assert.Equal(endStateBefore, ReadEndStateJson(careerPath));
    }

    public void Dispose()
    {
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
            CareerName = "Committed skill-reset session",
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
                PackId = "skill-reset-2020",
                Name = "Skill Reset 2020",
            },
            Season = pack.Season with
            {
                Year = 2020,
                SeriesName = "Skill Reset Championship",
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
            Name = "Reset Session Driver",
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

    private static IReadOnlyList<JournalRow> ReadSkillPlanRows(string careerPath) =>
        ReadDevelopmentRows(careerPath)
            .Where(row => string.Equals(
                row.Phase, JournalPhases.PlayerSkillPlan, StringComparison.Ordinal))
            .ToArray();

    private static IReadOnlyList<JournalRow> ReadSkillResetRows(string careerPath) =>
        ReadDevelopmentRows(careerPath)
            .Where(row => string.Equals(
                row.Phase, JournalPhases.PlayerSkillReset, StringComparison.Ordinal))
            .ToArray();

    private static IReadOnlyList<JournalRow> ReadDevelopmentRows(string careerPath)
    {
        using var db = CareerDatabase.Open(careerPath);
        long seasonId = CareerStore.ReadSeasons(db).Single().Id;
        return JournalStore.ReadSeason(db, seasonId)
            .Where(row => string.Equals(
                    row.Phase, JournalPhases.PlayerSkillPlan, StringComparison.Ordinal) ||
                string.Equals(
                    row.Phase, JournalPhases.PlayerSkillReset, StringComparison.Ordinal))
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
