using Companion.Core.Character;

namespace Companion.Tests.Career;

public sealed class MasterySkillGraphTests
{
    private static MasterySkillCatalog Catalog()
    {
        var rules = CharacterRules.Parse(CareerTestData.ReadRules("perks.json"));
        var dna = RacingDnaCatalog.Parse(CareerTestData.ReadRules("racing-dna-v2.json"), rules);
        return MasterySkillCatalog.Parse(CareerTestData.ReadRules("mastery-skills-v2.json"), rules, dna);
    }

    [Fact]
    public void Build_ProjectsNinetyMasterySkillsAndSevenCompleteRailsInStableOrder()
    {
        var catalog = Catalog();
        var tree = MasterySkillGraph.Build(Profile(), 1, 10, catalog, masteryCheckpointComplete: false);

        Assert.Equal(catalog.FamilyOrder, tree.Branches.Select(branch => branch.Id));
        Assert.Equal(90, tree.Branches.SelectMany(branch => branch.Nodes).Count(node => node.Kind == "mastery"));
        Assert.Equal(119, tree.Branches.SelectMany(branch => branch.Nodes).Count(node => node.Kind == "attribute"));
        foreach (var branch in tree.Branches)
        {
            Assert.Equal(10, branch.Nodes.Count(node => node.Kind == "mastery"));
            Assert.Equal(
                catalog.Skills.Where(skill => skill.Family == branch.Id).Select(skill => skill.Id),
                branch.Nodes.Where(node => node.Kind == "mastery").Select(node => node.Id));
        }

        var rails = tree.Branches.SelectMany(branch => branch.Nodes)
            .Where(node => node.Kind == "attribute")
            .GroupBy(node => node.RailId, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(catalog.AttributeRails.Select(rail => rail.Id), rails.Select(rail => rail.Key));
        Assert.Equal(7, rails.Length);
        for (int index = 0; index < rails.Length; index++)
        {
            var definition = catalog.AttributeRails[index];
            var nodes = rails[index].ToArray();
            Assert.Equal(17, nodes.Length);
            Assert.All(nodes, node =>
            {
                Assert.Equal(definition.Id, node.RailId);
                Assert.Equal(definition.Name, node.RailName);
                Assert.Equal(definition.Stat, node.AttributeStatId);
            });
        }
    }

    [Fact]
    public void Build_ProjectsLevelPrerequisiteAndCostLocksWithoutMutatingTheProfile()
    {
        var catalog = Catalog();
        var profile = Profile();
        var before = profile.GetHashCode();
        var nodes = MasterySkillGraph.Build(profile, 1, 1, catalog, false)
            .Branches.SelectMany(branch => branch.Nodes).ToDictionary(node => node.Id);

        Assert.Equal(SkillNodeState.Unlockable, nodes["pace_rhythm"].State);
        Assert.Equal(SkillNodeState.Locked, nodes["pace_qualifying_sequence"].State);
        Assert.Contains("Reach level 30", nodes["pace_qualifying_sequence"].LockReason, StringComparison.Ordinal);
        Assert.Equal(SkillNodeState.Locked, nodes["pace_total_performance"].State);
        Assert.Equal(before, profile.GetHashCode());
        Assert.Null(profile.AcquiredSkillIds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SecondCapstoneRequiresBothLevel285AndThePersistedCheckpointRegardlessOfOrder(bool reverse)
    {
        var catalog = Catalog();
        var capstones = catalog.Skills.Where(node => node.Family == "pace" && node.Tier == 5)
            .OrderBy(node => node.Order).ToArray();
        var first = reverse ? capstones[1] : capstones[0];
        var second = reverse ? capstones[0] : capstones[1];
        var owned = catalog.Skills.Where(node => node.Family == "pace" && node.Id != second.Id)
            .Select(node => node.Id).ToArray();
        var profile = Profile() with { AcquiredSkillIds = owned };

        SkillNode At(string id, int level, bool checkpoint) => MasterySkillGraph.Build(
                profile, level, 20, catalog, checkpoint)
            .Branches.SelectMany(branch => branch.Nodes).Single(node => node.Id == id);

        Assert.Equal(SkillNodeState.Owned, At(first.Id, 285, true).State);
        Assert.Equal(SkillNodeState.Locked, At(second.Id, 284, true).State);
        Assert.Contains("level 285", At(second.Id, 284, true).LockReason, StringComparison.Ordinal);
        Assert.Equal(SkillNodeState.Locked, At(second.Id, 285, false).State);
        Assert.Contains("checkpoint", At(second.Id, 285, false).LockReason, StringComparison.OrdinalIgnoreCase);
        var mastered = At(second.Id, 285, true);
        Assert.Equal(SkillNodeState.Mastery, mastered.State);
        Assert.True(mastered.IsMasteryOverride);
        Assert.True(mastered.ExclusiveGroup is not null);
    }

    [Fact]
    public void AttributeRail_ClampsTheFinalStepAndLocksRedundantNodesForAHigherBaseline()
    {
        var catalog = Catalog();
        var low = MasterySkillGraph.Build(Profile(pace: 0.15), 300, 499, catalog, true)
            .Branches.SelectMany(branch => branch.Nodes)
            .Where(node => node.RailId == "attribute.pace").OrderBy(node => node.Order).ToArray();
        Assert.Equal(17, low.Length);
        Assert.Equal(0.99, low[^1].AttributeValueAfter);

        var high = MasterySkillGraph.Build(Profile(pace: 0.85), 300, 499, catalog, true)
            .Branches.SelectMany(branch => branch.Nodes)
            .Where(node => node.RailId == "attribute.pace").OrderBy(node => node.Order).ToArray();
        Assert.Equal(SkillNodeState.Unlockable, high[0].State);
        Assert.Equal(SkillNodeState.Locked, high[1].State); // first step is still a prerequisite
        Assert.Contains("Requires", high[1].LockReason, StringComparison.Ordinal);
        Assert.Equal(SkillNodeState.Locked, high[3].State);
        Assert.Contains("0.99", high[3].LockReason, StringComparison.Ordinal);
        Assert.Equal(0.99, high[2].AttributeValueAfter);
    }

    [Fact]
    public void Build_FailsClosedOnUnknownOrNonSequentialPersistedOwnership()
    {
        var catalog = Catalog();
        Assert.Throws<InvalidOperationException>(() => MasterySkillGraph.Build(
            Profile() with { AcquiredSkillIds = ["missing_skill"] }, 300, 499, catalog, true));
        Assert.Throws<InvalidOperationException>(() => MasterySkillGraph.Build(
            Profile() with { AcquiredSkillIds = ["pace_rhythm", "pace_rhythm"] }, 300, 499, catalog, true));
        Assert.Throws<InvalidOperationException>(() => MasterySkillGraph.Build(
            Profile() with { AcquiredSkillIds = ["pace_qualifying_sequence"] }, 300, 499, catalog, true));
        Assert.Throws<InvalidOperationException>(() => MasterySkillGraph.Build(
            Profile() with { AcquiredSkillIds = ["pace_rhythm", "pace_qualifying_sequence"] }, 1, 499, catalog, true));
        Assert.Throws<InvalidOperationException>(() => MasterySkillGraph.Build(
            Profile() with
            {
                AcquiredSkillIds = catalog.Skills.Where(node => node.Family == "pace")
                    .Select(node => node.Id).ToArray(),
            },
            285,
            499,
            catalog,
            false));
        Assert.Throws<InvalidOperationException>(() => MasterySkillGraph.Build(
            Profile() with { AcquiredAttributeNodeIds = ["attribute_pace_02"] }, 300, 499, catalog, true));
        Assert.Throws<InvalidOperationException>(() => MasterySkillGraph.Build(
            Profile(pace: 0.0), 300, 499, catalog, true));
        Assert.Throws<InvalidOperationException>(() => MasterySkillGraph.Build(
            Profile() with { ProgressionVersion = 1 }, 300, 499, catalog, true));
        Assert.Throws<InvalidOperationException>(() => MasterySkillGraph.Build(
            Profile(), 300, -1, catalog, true));
        Assert.Throws<InvalidOperationException>(() => MasterySkillGraph.Build(
            Profile(), 300, 500, catalog, true));
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
            Stats = talent.Concat(meta).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
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
}
