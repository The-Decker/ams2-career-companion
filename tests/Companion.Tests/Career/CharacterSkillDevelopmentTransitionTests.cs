using Companion.Core.Career;
using Companion.Core.Character;

namespace Companion.Tests.Career;

public sealed class CharacterSkillDevelopmentTransitionTests
{
    [Fact]
    public void Reducer_AppliesPlanResetAndReplacementInJournalOrder()
    {
        var fixture = DevelopmentSequenceFixture.Create();

        var transitioned = CharacterSkillDevelopmentTransition.Apply(
            fixture.Player,
            fixture.Actions,
            fixture.Rules,
            fixture.Catalog);

        Assert.Equal(
            ["attribute_pace_01", "pace_rhythm"],
            fixture.Reset.PriorAcquisitions.Select(entry => entry.NodeId));
        Assert.DoesNotContain("pace_rhythm", transitioned.Character!.AcquiredSkillIds ?? []);
        Assert.DoesNotContain("attribute_pace_01", transitioned.Character.AcquiredAttributeNodeIds ?? []);
        Assert.Equal(["racecraft_clean_overtake"], transitioned.Character.AcquiredSkillIds);
        Assert.Equal(["attribute_racecraft_01"], transitioned.Character.AcquiredAttributeNodeIds);
        Assert.Equal(fixture.Replacement.TotalCost, transitioned.Character.SkillPointsSpent);
        Assert.Equal(fixture.Reset.XpCost, transitioned.Character.XpSpentOnResets);
        Assert.Equal(1, transitioned.Character.SkillResetCount);
        Assert.Equal(fixture.Player.Xp, transitioned.Xp);
        Assert.Equal(fixture.Player.Level, transitioned.Level);
        Assert.Equal(fixture.Player.Character!.CreationBaseline!.Stats["pace"], transitioned.Character.Stat("pace"));
        Assert.Equal(
            fixture.Player.Character.CreationBaseline.Stats["racecraft"] + 0.05,
            transitioned.Character.Stat("racecraft"),
            10);
    }

    [Fact]
    public void SeasonRollover_UsesTheSameOrderedDevelopmentSequence()
    {
        var fixture = DevelopmentSequenceFixture.Create();
        var direct = CharacterSkillDevelopmentTransition.Apply(
            fixture.Player,
            fixture.Actions,
            fixture.Rules,
            fixture.Catalog);
        DriverCareerState[] drivers =
        [
            new DriverCareerState { DriverId = "driver.a", Age = 29 },
        ];
        TeamCareerState[] teams =
        [
            new TeamCareerState { TeamId = "team.new", LineageId = "team.new", Tier = 4 },
        ];

        var start = SeasonRollover.Derive(
            fixture.Player,
            drivers,
            teams,
            acceptedTeamId: "team.new",
            playerLiveryName: "New #7",
            characterRules: fixture.Rules,
            masterySkills: fixture.Catalog,
            skillDevelopment: fixture.Actions);

        Assert.Equal(direct.Character, start.Player.Character);
        Assert.Equal(direct.Xp, start.Player.Xp);
        Assert.Equal(direct.Level, start.Player.Level);
        Assert.Equal("team.new", start.Player.CurrentTeamId);
        Assert.Equal("New #7", start.Player.LiveryName);
        Assert.Same(drivers, start.Drivers);
        Assert.Same(teams, start.Teams);
    }
}

internal sealed record DevelopmentSequenceFixture(
    CharacterRules Rules,
    MasterySkillCatalog Catalog,
    PlayerCareerState Player,
    CharacterSkillResetInput Reset,
    CharacterSkillPlanInput Replacement,
    IReadOnlyList<CharacterSkillDevelopmentAction> Actions)
{
    public static DevelopmentSequenceFixture Create()
    {
        CharacterRules rules = SkillPlanBoundaryTestData.Rules();
        RacingDnaCatalog dnaCatalog = RacingDnaCatalog.Parse(
            CareerTestData.ReadRules("racing-dna-v2.json"),
            rules);
        MasterySkillCatalog catalog = MasterySkillCatalog.Parse(
            CareerTestData.ReadRules("mastery-skills-v2.json"),
            rules,
            dnaCatalog);
        RacingDnaDefinition dna = dnaCatalog.Get("dna_prodigy", version: 1);
        CharacterProfile profile = Profile(dna);
        PlayerCareerState player = SkillPlanBoundaryTestData.Player() with { Character = profile };

        CharacterSkillPlanInput first = MasterySkillPlan.Prepare(
            player.Character,
            ["pace_rhythm", "attribute_pace_01"],
            SkillPlanBoundaryTestData.Facts(player),
            catalog);
        PlayerCareerState afterFirst = CharacterSkillDevelopmentTransition.Apply(
            player,
            [new CharacterSkillPlanAction(first)],
            rules,
            catalog);
        CharacterSkillResetInput reset = CharacterSkillReset.Prepare(afterFirst, rules, catalog);
        PlayerCareerState afterReset = CharacterSkillReset.Apply(afterFirst, reset, rules, catalog);
        CharacterSkillPlanInput replacement = MasterySkillPlan.Prepare(
            afterReset.Character!,
            ["racecraft_clean_overtake", "attribute_racecraft_01"],
            SkillPlanBoundaryTestData.Facts(afterReset),
            catalog);

        CharacterSkillDevelopmentAction[] actions =
        [
            new CharacterSkillPlanAction(first),
            new CharacterSkillResetAction(reset),
            new CharacterSkillPlanAction(replacement),
        ];
        return new DevelopmentSequenceFixture(rules, catalog, player, reset, replacement, actions);
    }

    private static CharacterProfile Profile(RacingDnaDefinition dna)
    {
        var talent = new Dictionary<string, double>(dna.StartingStats, StringComparer.Ordinal);
        var meta = new Dictionary<string, double>(dna.StartingMeta, StringComparer.Ordinal);
        string[] traits = dna.StartingTraitIds.ToArray();
        return new CharacterProfile
        {
            Stats = talent.Concat(meta)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            PerkIds = traits,
            CreationPerkIds = traits,
            ProgressionVersion = CharacterLevelProgression.Level300Version,
            RacingDnaId = dna.Id,
            RacingDnaVersion = dna.Version,
            CreationBaseline = new CharacterCreationBaseline
            {
                Stats = talent,
                Meta = meta,
                TraitIds = traits,
            },
        };
    }
}
