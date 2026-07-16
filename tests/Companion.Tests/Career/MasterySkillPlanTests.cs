using System.Text.Json;
using Companion.Core.Character;
using Companion.Core.Json;

namespace Companion.Tests.Career;

public sealed class MasterySkillPlanTests
{
    private static MasterySkillCatalog Catalog()
    {
        var rules = CharacterRules.Parse(CareerTestData.ReadRules("perks.json"));
        var dna = RacingDnaCatalog.Parse(CareerTestData.ReadRules("racing-dna-v2.json"), rules);
        return MasterySkillCatalog.Parse(CareerTestData.ReadRules("mastery-skills-v2.json"), rules, dna);
    }

    [Fact]
    public void Prepare_DerivesCanonicalOrderedPayload_AndApplyProjectsBothNodeKinds()
    {
        var catalog = Catalog();
        var before = Profile();
        string[] requested =
        [
            "pace_rhythm",
            "pace_qualifying_sequence",
            "attribute_pace_01",
            "attribute_pace_02",
        ];
        var facts = new MasteryProgressionFacts(30, 5, false);

        var input = MasterySkillPlan.Prepare(before, requested, facts, catalog);

        Assert.Equal(CharacterSkillPlanInput.CurrentVersion, input.Version);
        Assert.Equal(CharacterLevelProgression.Level300Version, input.ProgressionVersion);
        Assert.Equal(CharacterSkillPlanInput.CurrentEffectsVersion, input.EffectsVersion);
        Assert.Equal(requested, input.Entries.Select(entry => entry.NodeId));
        Assert.Equal(["mastery", "mastery", "attribute", "attribute"],
            input.Entries.Select(entry => entry.Kind));
        Assert.Equal([1, 2, 1, 1], input.Entries.Select(entry => entry.Cost));
        Assert.Equal(5, input.TotalCost);
        Assert.Null(before.AcquiredSkillIds);
        Assert.Null(before.AcquiredAttributeNodeIds);
        Assert.Equal(0.50, before.Stat("pace"));

        var after = MasterySkillPlan.Apply(before, input, facts, catalog);

        Assert.Equal(["pace_rhythm", "pace_qualifying_sequence"], after.AcquiredSkillIds);
        Assert.Equal(["attribute_pace_01", "attribute_pace_02"], after.AcquiredAttributeNodeIds);
        Assert.Equal(0.60, after.Stat("pace"), 10);
        Assert.Equal(5, after.SkillPointsSpent);
        Assert.Equal(CharacterProfile.CurrentMasteryEffectsVersion, after.MasteryEffectsVersion);
        Assert.Equal(0, after.CpSpent);
        Assert.Equal(0.50, after.CreationBaseline!.Stats["pace"]);
    }

    [Fact]
    public void Apply_ReplaysAJsonRoundTrippedPayloadDeterministically()
    {
        var catalog = Catalog();
        var profile = Profile();
        var facts = new MasteryProgressionFacts(30, 5, false);
        var prepared = MasterySkillPlan.Prepare(
            profile,
            ["pace_rhythm", "pace_qualifying_sequence", "attribute_pace_01", "attribute_pace_02"],
            facts,
            catalog);
        string inputJson = JsonSerializer.Serialize(prepared, CoreJson.Options);
        var replayInput = JsonSerializer.Deserialize<CharacterSkillPlanInput>(inputJson, CoreJson.Options)!;

        string live = JsonSerializer.Serialize(
            MasterySkillPlan.Apply(profile, prepared, facts, catalog), CoreJson.Options);
        string replay = JsonSerializer.Serialize(
            MasterySkillPlan.Apply(profile, replayInput, facts, catalog), CoreJson.Options);

        Assert.Equal(live, replay);
        Assert.Equal(CharacterSkillPlanInput.CurrentEffectsVersion, replayInput.EffectsVersion);
        Assert.Equal(
            CharacterProfile.CurrentMasteryEffectsVersion,
            JsonSerializer.Deserialize<CharacterProfile>(live, CoreJson.Options)!.MasteryEffectsVersion);
        using var document = JsonDocument.Parse(inputJson);
        Assert.Equal(
            ["version", "progressionVersion", "effectsVersion", "entries", "totalCost"],
            document.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal(4, document.RootElement.GetProperty("entries").GetArrayLength());
    }

    [Fact]
    public void LegacyJsonWithoutEffectsVersion_RemainsVersionZeroAndDoesNotActivateProfile()
    {
        var catalog = Catalog();
        var profile = Profile();
        var facts = new MasteryProgressionFacts(1, 1, false);
        var legacyInput = new CharacterSkillPlanInput
        {
            Entries =
            [
                new CharacterSkillPlanEntry
                {
                    NodeId = "pace_rhythm",
                    Kind = CharacterSkillPlanEntry.MasteryKind,
                    Cost = 1,
                },
            ],
            TotalCost = 1,
        };
        string legacyJson = JsonSerializer.Serialize(legacyInput, CoreJson.Options);
        var replayInput = JsonSerializer.Deserialize<CharacterSkillPlanInput>(
            legacyJson, CoreJson.Options)!;

        Assert.DoesNotContain("effectsVersion", legacyJson, StringComparison.Ordinal);
        Assert.Equal(0, replayInput.EffectsVersion);
        Assert.Equal(0, profile.MasteryEffectsVersion);
        Assert.DoesNotContain(
            "masteryEffectsVersion",
            JsonSerializer.Serialize(profile, CoreJson.Options),
            StringComparison.Ordinal);

        var after = MasterySkillPlan.Apply(profile, replayInput, facts, catalog);

        Assert.Equal(["pace_rhythm"], after.AcquiredSkillIds);
        Assert.Equal(0, after.MasteryEffectsVersion);
        Assert.DoesNotContain(
            "masteryEffectsVersion",
            JsonSerializer.Serialize(after, CoreJson.Options),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ProfileMasteryEffectsVersion_ParticipatesInStructuralEqualityAndHashing()
    {
        var inactive = Profile();
        var active = inactive with
        {
            MasteryEffectsVersion = CharacterProfile.CurrentMasteryEffectsVersion,
        };
        var roundTripped = JsonSerializer.Deserialize<CharacterProfile>(
            JsonSerializer.Serialize(active, CoreJson.Options), CoreJson.Options)!;

        Assert.NotEqual(inactive, active);
        Assert.NotEqual(inactive.GetHashCode(), active.GetHashCode());
        Assert.Equal(active, roundTripped);
        Assert.Equal(active.GetHashCode(), roundTripped.GetHashCode());
    }

    [Theory]
    [InlineData(284, true, "level 285")]
    [InlineData(285, false, "checkpoint")]
    public void SecondCapstone_IsRejectedWithoutBothMasteryGates(
        int level,
        bool checkpoint,
        string expectedReason)
    {
        var catalog = Catalog();
        var profile = WithOwnedSkills(
            Profile(),
            catalog.Skills.Where(node => node.Family == "pace" && node.Tier < 5)
                .Select(node => node.Id),
            catalog);
        var capstones = catalog.Skills.Where(node => node.Family == "pace" && node.Tier == 5)
            .OrderBy(node => node.Order).Select(node => node.Id).ToArray();

        var error = Assert.Throws<InvalidOperationException>(() => MasterySkillPlan.Prepare(
            profile,
            capstones,
            new MasteryProgressionFacts(level, 20, checkpoint),
            catalog));

        Assert.Contains(expectedReason, error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(8, profile.AcquiredSkillIds!.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SecondCapstone_IsAcceptedSequentiallyAtThePersistedMasteryCheckpoint(bool reverse)
    {
        var catalog = Catalog();
        var profile = WithOwnedSkills(
            Profile(),
            catalog.Skills.Where(node => node.Family == "pace" && node.Tier < 5)
                .Select(node => node.Id),
            catalog);
        var capstones = catalog.Skills.Where(node => node.Family == "pace" && node.Tier == 5)
            .OrderBy(node => node.Order).Select(node => node.Id).ToArray();
        if (reverse)
            Array.Reverse(capstones);
        var facts = new MasteryProgressionFacts(285, 20, true);

        var input = MasterySkillPlan.Prepare(profile, capstones, facts, catalog);
        var after = MasterySkillPlan.Apply(profile, input, facts, catalog);

        Assert.Equal(13, input.TotalCost);
        Assert.Equal(capstones, after.AcquiredSkillIds!.TakeLast(2));
        Assert.Equal(33, after.SkillPointsSpent);
    }

    [Fact]
    public void Level300MasteryCheckpoint_CanOwnEverySkillAndMaxEveryAttributeRail()
    {
        var catalog = Catalog();
        var before = FloorProfile();
        string[] completeBuild =
        [
            .. catalog.Skills.Select(node => node.Id),
            .. catalog.AttributeNodes.Select(node => node.Id),
        ];
        var facts = new MasteryProgressionFacts(
            CharacterLevelProgression.Level300Max,
            MasterySkillCatalog.SkillPointsMaximum,
            MasteryCheckpointComplete: true);

        CharacterSkillPlanInput input = MasterySkillPlan.Prepare(
            before,
            completeBuild,
            facts,
            catalog);
        CharacterProfile after = MasterySkillPlan.Apply(before, input, facts, catalog);

        Assert.Equal(90, after.AcquiredSkillIds!.Count);
        Assert.Equal(119, after.AcquiredAttributeNodeIds!.Count);
        Assert.Equal(catalog.MaximumCompleteCost, input.TotalCost);
        Assert.Equal(399, after.SkillPointsSpent);
        Assert.Equal(100, MasterySkillCatalog.SkillPointsMaximum - after.SkillPointsSpent);
        Assert.All(catalog.AttributeRails, rail => Assert.Equal(0.99, after.Stat(rail.Stat), 10));
        Assert.Equal(CharacterProfile.CurrentMasteryEffectsVersion, after.MasteryEffectsVersion);
    }

    [Fact]
    public void Prepare_EnforcesRequestedOrderLevelAndAffordability()
    {
        var catalog = Catalog();
        var profile = Profile();

        var order = Assert.Throws<InvalidOperationException>(() => MasterySkillPlan.Prepare(
            profile,
            ["pace_qualifying_sequence", "pace_rhythm"],
            new MasteryProgressionFacts(30, 3, false),
            catalog));
        Assert.Contains("Requires", order.Message, StringComparison.Ordinal);

        var level = Assert.Throws<InvalidOperationException>(() => MasterySkillPlan.Prepare(
            profile,
            ["pace_rhythm", "pace_qualifying_sequence"],
            new MasteryProgressionFacts(1, 3, false),
            catalog));
        Assert.Contains("level 30", level.Message, StringComparison.OrdinalIgnoreCase);

        var cost = Assert.Throws<InvalidOperationException>(() => MasterySkillPlan.Prepare(
            profile,
            ["pace_rhythm", "pace_telemetry_habit"],
            new MasteryProgressionFacts(1, 1, false),
            catalog));
        Assert.Contains("only 1 SP", cost.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Prepare_RejectsChosenFlavorSkillUntilTheChoiceIsPersisted()
    {
        var catalog = Catalog();
        var profile = Profile();
        string before = JsonSerializer.Serialize(profile, CoreJson.Options);

        var error = Assert.Throws<InvalidOperationException>(() => MasterySkillPlan.Prepare(
            profile,
            ["v2_wonderkid", "signature_focus"],
            new MasteryProgressionFacts(30, 3, false),
            catalog));

        Assert.Contains("persisted chosen flavor", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, JsonSerializer.Serialize(profile, CoreJson.Options));

        var invalid = profile with
        {
            ChosenFlavor = "notAWritableRating",
            CreationBaseline = profile.CreationBaseline! with { ChosenFlavor = "notAWritableRating" },
        };
        Assert.Throws<InvalidOperationException>(() => MasterySkillPlan.Prepare(
            invalid,
            ["v2_wonderkid", "signature_focus"],
            new MasteryProgressionFacts(30, 3, false),
            catalog));

        var chosen = profile with
        {
            ChosenFlavor = "tyreManagement",
            CreationBaseline = profile.CreationBaseline! with { ChosenFlavor = "tyreManagement" },
        };
        var accepted = MasterySkillPlan.Prepare(
            chosen,
            ["v2_wonderkid", "signature_focus"],
            new MasteryProgressionFacts(30, 3, false),
            catalog);
        Assert.Equal(3, accepted.TotalCost);
    }

    [Fact]
    public void AttributePlan_UsesImmutableBaselineAndClampsTheLastUsefulStep()
    {
        var catalog = Catalog();
        var profile = Profile(pace: 0.85);
        string[] useful = ["attribute_pace_01", "attribute_pace_02", "attribute_pace_03"];
        var facts = new MasteryProgressionFacts(300, 3, true);

        var after = MasterySkillPlan.Apply(
            profile,
            MasterySkillPlan.Prepare(profile, useful, facts, catalog),
            facts,
            catalog);

        Assert.Equal(0.99, after.Stat("pace"));
        Assert.Equal(0.85, after.CreationBaseline!.Stats["pace"]);
        Assert.Equal(3, after.SkillPointsSpent);

        var redundant = Assert.Throws<InvalidOperationException>(() => MasterySkillPlan.Prepare(
            profile,
            [.. useful, "attribute_pace_04"],
            new MasteryProgressionFacts(300, 4, true),
            catalog));
        Assert.Contains("already reaches 0.99", redundant.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_RejectsTamperedEnvelopeCanonicalFieldsAndDuplicates()
    {
        var catalog = Catalog();
        var profile = Profile();
        var facts = new MasteryProgressionFacts(1, 10, false);
        var valid = MasterySkillPlan.Prepare(profile, ["pace_rhythm"], facts, catalog);

        Assert.Throws<NotSupportedException>(() =>
            MasterySkillPlan.Apply(profile, valid with { Version = 2 }, facts, catalog));
        Assert.Throws<InvalidOperationException>(() => MasterySkillPlan.Apply(
            profile, valid with { ProgressionVersion = 1 }, facts, catalog));
        Assert.Throws<NotSupportedException>(() => MasterySkillPlan.Apply(
            profile, valid with { EffectsVersion = 2 }, facts, catalog));
        Assert.Throws<NotSupportedException>(() => MasterySkillPlan.Apply(
            profile with { MasteryEffectsVersion = 2 }, valid, facts, catalog));
        Assert.Throws<InvalidOperationException>(() => MasterySkillPlan.Apply(
            profile,
            valid with { Entries = [valid.Entries[0] with { Kind = "attribute" }] },
            facts,
            catalog));
        Assert.Throws<InvalidOperationException>(() => MasterySkillPlan.Apply(
            profile,
            valid with { Entries = [valid.Entries[0] with { Cost = 2 }], TotalCost = 2 },
            facts,
            catalog));
        Assert.Throws<InvalidOperationException>(() => MasterySkillPlan.Apply(
            profile, valid with { TotalCost = 2 }, facts, catalog));
        Assert.Throws<InvalidOperationException>(() => MasterySkillPlan.Apply(
            profile,
            valid with { Entries = [valid.Entries[0], valid.Entries[0]], TotalCost = 2 },
            facts,
            catalog));
        Assert.Throws<InvalidOperationException>(() => MasterySkillPlan.Prepare(
            profile, ["unknown_node"], facts, catalog));
        Assert.Throws<InvalidOperationException>(() => MasterySkillPlan.Prepare(
            profile, [], facts, catalog));
    }

    [Fact]
    public void Apply_FailsClosedOnPersistedSpendOwnershipOrAttributeDrift()
    {
        var catalog = Catalog();
        var facts = new MasteryProgressionFacts(30, 10, false);
        var one = new CharacterSkillPlanInput
        {
            Entries = [new CharacterSkillPlanEntry { NodeId = "pace_rhythm", Kind = "mastery", Cost = 1 }],
            TotalCost = 1,
        };

        Assert.Throws<InvalidOperationException>(() => MasterySkillPlan.Apply(
            Profile() with { SkillPointsSpent = 1 }, one, facts, catalog));
        Assert.Throws<InvalidOperationException>(() => MasterySkillPlan.Apply(
            Profile() with
            {
                AcquiredAttributeNodeIds = ["attribute_pace_01"],
                SkillPointsSpent = 1,
            },
            one,
            facts,
            catalog));
        Assert.Throws<InvalidOperationException>(() => MasterySkillPlan.Apply(
            Profile() with
            {
                AcquiredAttributeNodeIds = ["attribute_pace_02"],
                SkillPointsSpent = 1,
            },
            one,
            facts,
            catalog));
        Assert.Throws<InvalidOperationException>(() => MasterySkillPlan.Apply(
            Profile() with { ProgressionVersion = 1 }, one, facts, catalog));
    }

    [Fact]
    public void Apply_LateSequentialFailureLeavesTheOriginalProfileByteIdentical()
    {
        var catalog = Catalog();
        var profile = Profile();
        string before = JsonSerializer.Serialize(profile, CoreJson.Options);
        var input = new CharacterSkillPlanInput
        {
            Entries =
            [
                new CharacterSkillPlanEntry { NodeId = "pace_rhythm", Kind = "mastery", Cost = 1 },
                new CharacterSkillPlanEntry
                    { NodeId = "pace_qualifying_sequence", Kind = "mastery", Cost = 2 },
            ],
            TotalCost = 3,
        };

        Assert.Throws<InvalidOperationException>(() => MasterySkillPlan.Apply(
            profile,
            input,
            new MasteryProgressionFacts(1, 3, false),
            catalog));

        Assert.Equal(before, JsonSerializer.Serialize(profile, CoreJson.Options));
        Assert.Null(profile.AcquiredSkillIds);
        Assert.Equal(0, profile.SkillPointsSpent);
    }

    [Fact]
    public void ApplyAll_PreservesJournalAndEntryOrderAcrossConfirmedPlans()
    {
        var catalog = Catalog();
        var profile = Profile();
        var facts = new MasteryProgressionFacts(30, 5, false);
        var first = MasterySkillPlan.Prepare(
            profile,
            ["pace_rhythm", "pace_qualifying_sequence"],
            facts,
            catalog);
        var afterFirst = MasterySkillPlan.Apply(profile, first, facts, catalog);
        var second = MasterySkillPlan.Prepare(
            afterFirst,
            ["attribute_pace_01", "attribute_pace_02"],
            facts with { AvailableSkillPoints = 2 },
            catalog);

        var after = MasterySkillPlan.ApplyAll(profile, [first, second], facts, catalog);

        Assert.Equal(["pace_rhythm", "pace_qualifying_sequence"], after.AcquiredSkillIds);
        Assert.Equal(["attribute_pace_01", "attribute_pace_02"], after.AcquiredAttributeNodeIds);
        Assert.Equal(5, after.SkillPointsSpent);
        Assert.Equal(0.60, after.Stat("pace"), 10);
    }

    private static CharacterProfile WithOwnedSkills(
        CharacterProfile profile,
        IEnumerable<string> skillIds,
        MasterySkillCatalog catalog)
    {
        string[] acquired = skillIds.ToArray();
        return profile with
        {
            AcquiredSkillIds = acquired,
            SkillPointsSpent = acquired.Sum(id => catalog.GetSkill(id).Cost),
        };
    }

    private static CharacterProfile Profile(double pace = 0.50)
    {
        var talent = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["pace"] = pace,
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
            Stats = talent.Concat(meta)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            PerkIds = [],
            ProgressionVersion = CharacterLevelProgression.Level300Version,
            CreationBaseline = new CharacterCreationBaseline
            {
                Stats = talent,
                Meta = meta,
                TraitIds = [],
            },
        };
    }

    private static CharacterProfile FloorProfile()
    {
        var talent = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["pace"] = 0.15,
            ["oneLap"] = 0.15,
            ["craft"] = 0.15,
            ["racecraft"] = 0.15,
            ["adaptability"] = 0.15,
        };
        var meta = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["marketability"] = 0.15,
            ["durability"] = 0.15,
        };
        const string chosenFlavor = "tyreManagement";
        return new CharacterProfile
        {
            Stats = talent.Concat(meta)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            PerkIds = [],
            ChosenFlavor = chosenFlavor,
            ProgressionVersion = CharacterLevelProgression.Level300Version,
            CreationBaseline = new CharacterCreationBaseline
            {
                Stats = talent,
                Meta = meta,
                TraitIds = [],
                ChosenFlavor = chosenFlavor,
            },
        };
    }
}
