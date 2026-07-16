using System.Text.Json;
using Companion.Core.Career;
using Companion.Core.Character;
using Companion.Core.Json;

namespace Companion.Tests.Career;

public sealed class CharacterSkillResetTests
{
    [Fact]
    public void Preview_QuotesCanonicalResetAndProjectsExactCreationBaseline()
    {
        var rules = Rules();
        var catalog = Catalog(rules);
        var before = OwnedState(rules, catalog, level: 300);

        SkillResetPreview preview = CharacterSkillReset.Preview(before, rules, catalog);

        Assert.True(preview.CanApply);
        Assert.Equal("", preview.BlockReason);
        Assert.Equal(before.Xp, preview.LifetimeXp);
        Assert.Equal(before.Xp, preview.AvailableResetXp);
        Assert.Equal(750, preview.Cost);
        Assert.Equal(before.Xp - 750, preview.AvailableResetXpAfter);
        Assert.Equal(5, preview.SkillPointsRefunded);
        Assert.Equal(499, preview.SkillPointsAfterReset);
        Assert.Equal(4, preview.AcquisitionCount);

        CharacterSkillResetInput input = Assert.IsType<CharacterSkillResetInput>(preview.Input);
        Assert.Equal(CharacterSkillResetInput.CurrentVersion, input.Version);
        Assert.Equal(CharacterLevelProgression.Level300Version, input.ProgressionVersion);
        Assert.Equal(1, input.PolicyVersion);
        Assert.Equal(750, input.XpCost);
        Assert.Equal(5, input.RefundedSkillPoints);
        Assert.Equal(
            ["attribute_pace_01", "attribute_pace_02", "pace_qualifying_sequence", "pace_rhythm"],
            input.PriorAcquisitions.Select(entry => entry.NodeId));
        Assert.Equal(
            ["attribute", "attribute", "mastery", "mastery"],
            input.PriorAcquisitions.Select(entry => entry.Kind));
        Assert.Equal([1, 1, 2, 1], input.PriorAcquisitions.Select(entry => entry.Cost));

        PlayerCareerState after = Assert.IsType<PlayerCareerState>(preview.ProjectedState);
        CharacterProfile character = after.Character!;
        Assert.Equal(before.Xp, after.Xp);
        Assert.Equal(before.Level, after.Level);
        Assert.Equal(before.Character!.Name, character.Name);
        Assert.Equal("BRA", character.CountryCode);
        Assert.Equal(before.Character.Age, character.Age);
        Assert.Equal(before.Character.RacingDnaId, character.RacingDnaId);
        Assert.Equal(before.Character.RacingDnaVersion, character.RacingDnaVersion);
        Assert.Equal(before.Character.RacingDnaChoice, character.RacingDnaChoice);
        Assert.Equal(before.Character.CreationBaseline, character.CreationBaseline);
        Assert.Equal(0.50, character.Stat("pace"), 10);
        Assert.Equal(0.50, character.Stat("marketability"), 10);
        Assert.Equal(["rain_man"], character.PerkIds);
        Assert.Equal(["rain_man"], character.CreationPerkIds);
        Assert.Equal("wetSkill", character.ChosenFlavor);
        Assert.Null(character.AcquiredSkillIds);
        Assert.Null(character.AcquiredAttributeNodeIds);
        Assert.Equal(0, character.SkillPointsSpent);
        Assert.Equal(750, character.XpSpentOnResets);
        Assert.Equal(1, character.SkillResetCount);

        // Preview is pure, and DNA projections remain external rather than being baked into Stats.
        Assert.Equal(0.60, before.Character.Stat("pace"), 10);
        Assert.Equal(5, before.Character.SkillPointsSpent);
        Assert.Equal(0, before.Character.XpSpentOnResets);
    }

    [Fact]
    public void Apply_ReplaysJsonRoundTrippedPayloadWithoutRngOrInputMutation()
    {
        var rules = Rules();
        var catalog = Catalog(rules);
        var before = OwnedState(rules, catalog, level: 300);
        CharacterSkillResetInput prepared = CharacterSkillReset.Prepare(before, rules, catalog);
        string payloadJson = JsonSerializer.Serialize(prepared, CoreJson.Options);
        var replayInput = JsonSerializer.Deserialize<CharacterSkillResetInput>(
            payloadJson, CoreJson.Options)!;

        PlayerCareerState live = CharacterSkillReset.Apply(before, prepared, rules, catalog);
        PlayerCareerState replay = CharacterSkillReset.Apply(before, replayInput, rules, catalog);

        Assert.Equal(
            JsonSerializer.Serialize(live, CoreJson.Options),
            JsonSerializer.Serialize(replay, CoreJson.Options));
        Assert.Equal(payloadJson, JsonSerializer.Serialize(prepared, CoreJson.Options));
        Assert.Equal(before.Xp, live.Xp);
        Assert.Equal(before.Level, live.Level);
    }

    [Fact]
    public void Apply_RejectsEveryAuthoritativePayloadRewriteWithoutMutatingState()
    {
        var rules = Rules();
        var catalog = Catalog(rules);
        var state = OwnedState(rules, catalog, level: 300);
        CharacterSkillResetInput valid = CharacterSkillReset.Prepare(state, rules, catalog);
        CharacterSkillPlanEntry[] reordered = valid.PriorAcquisitions.Reverse().ToArray();
        CharacterSkillPlanEntry[] wrongKind = valid.PriorAcquisitions
            .Select((entry, index) => index == 0
                ? entry with { Kind = CharacterSkillPlanEntry.MasteryKind }
                : entry)
            .ToArray();
        CharacterSkillPlanEntry[] wrongNodeCost = valid.PriorAcquisitions
            .Select((entry, index) => index == 0 ? entry with { Cost = entry.Cost + 1 } : entry)
            .ToArray();
        CharacterSkillResetInput[] rewrites =
        [
            valid with { Version = 2 },
            valid with { ProgressionVersion = 1 },
            valid with { PolicyVersion = 2 },
            valid with { XpCost = valid.XpCost + 1 },
            valid with { RefundedSkillPoints = valid.RefundedSkillPoints + 1 },
            valid with { PriorAcquisitions = reordered },
            valid with { PriorAcquisitions = wrongKind },
            valid with { PriorAcquisitions = wrongNodeCost },
            valid with { PriorAcquisitions = valid.PriorAcquisitions.Skip(1).ToArray() },
            valid with { PriorAcquisitions = null! },
        ];
        string before = JsonSerializer.Serialize(state, CoreJson.Options);

        foreach (CharacterSkillResetInput rewrite in rewrites)
        {
            Assert.ThrowsAny<Exception>(() =>
                CharacterSkillReset.Apply(state, rewrite, rules, catalog));
            Assert.Equal(before, JsonSerializer.Serialize(state, CoreJson.Options));
        }
    }

    [Fact]
    public void Preview_BlocksOrdinaryEmptyAndUnaffordableCasesWithoutThrowing()
    {
        var rules = Rules();
        var catalog = Catalog(rules);
        var empty = State(BaseProfile(), rules, level: 30);

        SkillResetPreview emptyPreview = CharacterSkillReset.Preview(empty, rules, catalog);

        Assert.False(emptyPreview.CanApply);
        Assert.Contains("no committed skill tree", emptyPreview.BlockReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, emptyPreview.AcquisitionCount);
        Assert.Equal(500, emptyPreview.Cost);
        Assert.Equal(48, emptyPreview.SkillPointsAfterReset);
        Assert.Null(emptyPreview.Input);
        Assert.Null(emptyPreview.ProjectedState);

        var owned = OwnedState(rules, catalog, level: 30);
        var unaffordable = owned with
        {
            Character = owned.Character! with
            {
                XpSpentOnResets = 500,
                SkillResetCount = 1,
            },
        };
        SkillResetPreview insufficient = CharacterSkillReset.Preview(unaffordable, rules, catalog);

        Assert.False(insufficient.CanApply);
        Assert.Contains("only", insufficient.BlockReason, StringComparison.Ordinal);
        Assert.Equal(1_000, insufficient.Cost);
        Assert.Equal(unaffordable.Xp - 500, insufficient.AvailableResetXp);
        Assert.Equal(48, insufficient.SkillPointsAfterReset);
        Assert.Null(insufficient.Input);
        Assert.Null(insufficient.ProjectedState);
        Assert.Throws<InvalidOperationException>(() =>
            CharacterSkillReset.Prepare(unaffordable, rules, catalog));
    }

    [Fact]
    public void RepeatCost_UsesPersistedCounterButNeverDelevelsTheCharacter()
    {
        var rules = Rules();
        var catalog = Catalog(rules);
        var owned = OwnedState(rules, catalog, level: 300);
        var repeated = owned with
        {
            Character = owned.Character! with
            {
                XpSpentOnResets = 750,
                SkillResetCount = 1,
            },
        };

        SkillResetPreview preview = CharacterSkillReset.Preview(repeated, rules, catalog);
        PlayerCareerState after = CharacterSkillReset.Apply(
            repeated,
            CharacterSkillReset.Prepare(repeated, rules, catalog),
            rules,
            catalog);

        Assert.Equal(1_500, preview.Cost);
        Assert.Equal(repeated.Xp, after.Xp);
        Assert.Equal(300, after.Level);
        Assert.Equal(2_250, after.Character!.XpSpentOnResets);
        Assert.Equal(2, after.Character.SkillResetCount);
    }

    [Fact]
    public void Preview_FailsClosedForCorruptOrUnsupportedState()
    {
        var rules = Rules();
        var catalog = Catalog(rules);
        var valid = OwnedState(rules, catalog, level: 300);
        var wrongStats = valid.Character!.Stats.ToDictionary(
            pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        wrongStats["pace"] = 0.61;
        PlayerCareerState[] corrupt =
        [
            valid with { Character = valid.Character with { ProgressionVersion = 1 } },
            valid with { Character = valid.Character with { CreationBaseline = null } },
            valid with { Character = valid.Character with { SkillPointsSpent = 4 } },
            valid with { Character = valid.Character with { Stats = wrongStats } },
            valid with { Level = 299 },
            valid with { CampaignProgressionPlan = null },
            valid with
            {
                Character = valid.Character with
                {
                    XpSpentOnResets = 1,
                    SkillResetCount = 0,
                },
            },
            valid with
            {
                Character = valid.Character with
                {
                    AcquiredSkillIds = ["pace_qualifying_sequence"],
                    AcquiredAttributeNodeIds = null,
                    SkillPointsSpent = 2,
                    Stats = BaselineStats(valid.Character.CreationBaseline!),
                },
            },
        ];

        foreach (PlayerCareerState state in corrupt)
            Assert.ThrowsAny<Exception>(() => CharacterSkillReset.Preview(state, rules, catalog));
    }

    private static CharacterRules Rules() =>
        CharacterRules.Parse(CareerTestData.ReadRules("perks.json"));

    private static MasterySkillCatalog Catalog(CharacterRules rules)
    {
        var dna = RacingDnaCatalog.Parse(
            CareerTestData.ReadRules("racing-dna-v2.json"), rules);
        return MasterySkillCatalog.Parse(
            CareerTestData.ReadRules("mastery-skills-v2.json"), rules, dna);
    }

    private static PlayerCareerState OwnedState(
        CharacterRules rules,
        MasterySkillCatalog catalog,
        int level)
    {
        CharacterProfile profile = BaseProfile();
        var facts = new MasteryProgressionFacts(level, 499, true);
        CharacterSkillPlanInput plan = MasterySkillPlan.Prepare(
            profile,
            ["pace_rhythm", "pace_qualifying_sequence", "attribute_pace_01", "attribute_pace_02"],
            facts,
            catalog);
        return State(MasterySkillPlan.Apply(profile, plan, facts, catalog), rules, level);
    }

    private static PlayerCareerState State(
        CharacterProfile profile,
        CharacterRules rules,
        int level) =>
        new()
        {
            Reputation = 42,
            Opi = 1.25,
            PaceAnchor = 91,
            SeasonsCompleted = 1,
            CurrentTeamId = "team.lotus",
            LiveryName = "Lotus #5",
            Character = profile,
            Level = level,
            Xp = CharacterLevelProgression.CumulativeXpToLevel(
                CharacterLevelProgression.Level300Version, level, rules),
            ExperienceMode = CareerExperienceModes.GrandPrixDynasty,
            CampaignProgressionPlan = SkillPlanBoundaryTestData.Campaign(),
        };

    private static CharacterProfile BaseProfile()
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
            Name = "Zeroforce",
            CountryCode = "BRA",
            Age = 23,
            Stats = talent.Concat(meta).ToDictionary(
                pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            PerkIds = ["rain_man"],
            CreationPerkIds = ["rain_man"],
            ChosenFlavor = "wetSkill",
            ProgressionVersion = CharacterLevelProgression.Level300Version,
            RacingDnaId = "dna_rain_master",
            RacingDnaVersion = 1,
            RacingDnaChoice = "wet",
            CreationBaseline = new CharacterCreationBaseline
            {
                Stats = talent,
                Meta = meta,
                TraitIds = ["rain_man"],
                ChosenFlavor = "wetSkill",
            },
        };
    }

    private static IReadOnlyDictionary<string, double> BaselineStats(
        CharacterCreationBaseline baseline) =>
        baseline.Stats.Concat(baseline.Meta).ToDictionary(
            pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
}
