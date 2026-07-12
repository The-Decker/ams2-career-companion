using Companion.Core.Character;

namespace Companion.Tests.Career;

public sealed class SkillTreeTests
{
    private static CharacterRules Rules() =>
        CharacterRules.Parse(CareerTestData.ReadRules("perks.json"));

    private static CharacterProfile Character(params string[] perks) => new()
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
        PerkIds = perks,
        ProgressionVersion = 1,
    };

    [Fact]
    public void Build_PreservesBranchOrderAndMetaFlags()
    {
        var rules = Rules();
        var tree = SkillTree.Build(Character(), level: 1, availableSp: 3, rules);

        Assert.Equal(rules.SkillTree.BranchOrder, tree.Branches.Select(branch => branch.Id));
        Assert.True(tree.Branches.Single(branch => branch.Id == "physical").IsMeta);
        Assert.False(tree.Branches.Single(branch => branch.Id == "pace").IsMeta);
        Assert.Equal(42 + 15, tree.Branches.Sum(branch => branch.Nodes.Count));
    }

    [Fact]
    public void Build_ProjectsOwnedUnlockableAndLockedReasons()
    {
        var rules = Rules();
        var tree = SkillTree.Build(
            Character("engineers_favorite"), level: 6, availableSp: 3, rules);
        var nodes = tree.Branches.SelectMany(branch => branch.Nodes).ToDictionary(node => node.Id);

        Assert.Equal(SkillNodeState.Owned, nodes["engineers_favorite"].State);
        Assert.Equal(SkillNodeState.Unlockable, nodes["late_braker"].State);
        Assert.Equal(SkillNodeState.Locked, nodes["ice_in_the_veins"].State);
        Assert.Equal("Reach level 12", nodes["ice_in_the_veins"].LockReason);
        Assert.Equal("Creation-only perk", nodes["sunday_driver"].LockReason);

        var poor = SkillTree.Build(Character("engineers_favorite"), 6, 0, rules)
            .Branches.SelectMany(branch => branch.Nodes).Single(node => node.Id == "late_braker");
        Assert.Equal("Costs 1 SP", poor.LockReason);
    }

    [Fact]
    public void Build_SequencesStatNodesByDurableOwnership()
    {
        var rules = Rules();
        var character = Character() with { UnlockedSkillNodeIds = ["raise_pace_1"] };
        var nodes = SkillTree.Build(character, level: 6, availableSp: 2, rules)
            .Branches.SelectMany(branch => branch.Nodes).ToDictionary(node => node.Id);

        Assert.Equal(SkillNodeState.Owned, nodes["raise_pace_1"].State);
        Assert.Equal(SkillNodeState.Unlockable, nodes["raise_pace_2"].State);
        Assert.Equal("Reach level 12", nodes["raise_pace_3"].LockReason);
    }
}
