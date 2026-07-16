using Companion.Core.Character;
using Companion.Data;
using Companion.Tests.Career;

namespace Companion.Tests.Data;

/// <summary>Replay/fold trust-boundary coverage for mechanically active mastery effects. Invalid
/// catalog/profile/grid contracts must abort the transaction; they are never a synthetic DNS.</summary>
public sealed class MasteryGridFoldTests
{
    private static CharacterRules Rules() =>
        CharacterRules.Parse(CareerTestData.ReadRules("perks.json"));

    private static MasterySkillCatalog Catalog(CharacterRules rules) =>
        MasterySkillCatalog.Parse(
            CareerTestData.ReadRules("mastery-skills-v2.json"),
            rules,
            RacingDnaCatalog.Parse(CareerTestData.ReadRules("racing-dna-v2.json"), rules));

    private static CharacterProfile ActiveProfile(params string[] acquiredSkillIds) => new()
    {
        Stats = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["pace"] = 0.5,
            ["oneLap"] = 0.5,
            ["craft"] = 0.5,
            ["racecraft"] = 0.5,
            ["adaptability"] = 0.5,
            ["marketability"] = 0.5,
            ["durability"] = 0.5,
        },
        PerkIds = [],
        ProgressionVersion = CharacterLevelProgression.Level300Version,
        AcquiredSkillIds = acquiredSkillIds,
        MasteryEffectsVersion = CharacterProfile.CurrentMasteryEffectsVersion,
    };

    [Fact]
    public void ImportAndFold_ActiveOwnershipWithoutCatalog_ThrowsAndRollsBackInsteadOfDns()
    {
        using var tmp = new TempDb();
        using var db = CareerDatabase.Open(tmp.Path);
        var (seasonId, pack) = DataCareerFixture.SetupCareer(db);
        StateStore.UpsertPlayerState(
            db,
            seasonId,
            StateStore.StageStart,
            DataCareerFixture.PlayerStart() with
            {
                Character = ActiveProfile("physical_core_strength"),
            });
        CharacterRules rules = Rules();
        ReplaySimInputs inputs = DataCareerFixture.Inputs() with
        {
            CharacterRules = rules,
            MasterySkills = null,
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReplayService.ImportAndFoldRound(
                db,
                seasonId,
                pack,
                DataCareerFixture.MasterSeed,
                inputs,
                1,
                DataCareerFixture.Envelope(DataCareerFixture.Rounds()[0]),
                DataCareerFixture.Utc));

        Assert.Contains("mastery-skill catalog", exception.Message, StringComparison.Ordinal);
        Assert.Empty(ResultStore.ReadSeasonResults(db, seasonId));
        Assert.Empty(StateStore.ReadRoundPlayerStates(db, seasonId));
    }

    [Fact]
    public void ImportAndFold_UnknownMasteryOwnership_ThrowsAndRollsBackInsteadOfDns()
    {
        using var tmp = new TempDb();
        using var db = CareerDatabase.Open(tmp.Path);
        var (seasonId, pack) = DataCareerFixture.SetupCareer(db);
        StateStore.UpsertPlayerState(
            db,
            seasonId,
            StateStore.StageStart,
            DataCareerFixture.PlayerStart() with
            {
                Character = ActiveProfile("mastery.not-real"),
            });
        CharacterRules rules = Rules();
        ReplaySimInputs inputs = DataCareerFixture.Inputs() with
        {
            CharacterRules = rules,
            MasterySkills = Catalog(rules),
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReplayService.ImportAndFoldRound(
                db,
                seasonId,
                pack,
                DataCareerFixture.MasterSeed,
                inputs,
                1,
                DataCareerFixture.Envelope(DataCareerFixture.Rounds()[0]),
                DataCareerFixture.Utc));

        Assert.Contains("unknown mastery skill", exception.Message, StringComparison.Ordinal);
        Assert.Empty(ResultStore.ReadSeasonResults(db, seasonId));
        Assert.Empty(StateStore.ReadRoundPlayerStates(db, seasonId));
    }

    [Fact]
    public void ImportAndFold_GridContractFailure_ThrowsAndRollsBackInsteadOfDns()
    {
        using var tmp = new TempDb();
        using var db = CareerDatabase.Open(tmp.Path);
        var (seasonId, pinnedPack) = DataCareerFixture.SetupCareer(db);
        string duplicatedLivery = pinnedPack.Entries[0].Ams2LiveryName;
        var entries = pinnedPack.Entries.ToArray();
        entries[1] = entries[1] with { Ams2LiveryName = duplicatedLivery };
        var malformedPack = pinnedPack with { Entries = entries };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReplayService.ImportAndFoldRound(
                db,
                seasonId,
                malformedPack,
                DataCareerFixture.MasterSeed,
                DataCareerFixture.Inputs(),
                1,
                DataCareerFixture.Envelope(DataCareerFixture.Rounds()[0]),
                DataCareerFixture.Utc));

        Assert.Contains("duplicate liveries", exception.Message, StringComparison.Ordinal);
        Assert.Empty(ResultStore.ReadSeasonResults(db, seasonId));
        Assert.Empty(StateStore.ReadRoundPlayerStates(db, seasonId));
    }
}
