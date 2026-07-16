using Companion.Core.Career;
using Companion.Core.Character;

namespace Companion.Tests.Career;

public sealed class CharacterSkillPlanBoundaryTests
{
    [Fact]
    public void EmptyPlanRemainsANoOpWithoutV2CampaignContext()
    {
        var player = SkillPlanBoundaryTestData.Player() with
        {
            ExperienceMode = null,
            CampaignProgressionPlan = null,
        };

        Assert.Same(player, CharacterSkillPlanTransition.Apply(player, [], catalog: null));
    }

    [Fact]
    public void Transition_AppliesPlanAndEntryOrderWithoutMutatingTheSeasonEndProfile()
    {
        var catalog = SkillPlanBoundaryTestData.Catalog();
        var playerEnd = SkillPlanBoundaryTestData.Player();
        var plans = SkillPlanBoundaryTestData.OrderedPlans(playerEnd, catalog);

        var transitioned = CharacterSkillPlanTransition.Apply(playerEnd, plans, catalog);

        Assert.Null(playerEnd.Character!.AcquiredSkillIds);
        Assert.Null(playerEnd.Character.AcquiredAttributeNodeIds);
        Assert.Equal(0, playerEnd.Character.SkillPointsSpent);
        Assert.Equal(0, playerEnd.Character.MasteryEffectsVersion);
        Assert.Equal(
            ["pace_rhythm", "pace_qualifying_sequence"],
            transitioned.Character!.AcquiredSkillIds);
        Assert.Equal(
            ["attribute_pace_01", "attribute_pace_02"],
            transitioned.Character.AcquiredAttributeNodeIds);
        Assert.Equal(5, transitioned.Character.SkillPointsSpent);
        Assert.Equal(CharacterProfile.CurrentMasteryEffectsVersion, transitioned.Character.MasteryEffectsVersion);
        Assert.Equal(0.60, transitioned.Character.Stat("pace"), 10);
    }

    [Fact]
    public void Rollover_UsesTheSameOrderedTransitionWhileReseatingThePlayer()
    {
        var catalog = SkillPlanBoundaryTestData.Catalog();
        var playerEnd = SkillPlanBoundaryTestData.Player();
        var plans = SkillPlanBoundaryTestData.OrderedPlans(playerEnd, catalog);
        DriverCareerState[] drivers =
        [
            new DriverCareerState { DriverId = "driver.a", Age = 29 },
        ];
        TeamCareerState[] teams =
        [
            new TeamCareerState { TeamId = "team.new", LineageId = "team.new", Tier = 4 },
        ];

        var start = SeasonRollover.Derive(
            playerEnd,
            drivers,
            teams,
            acceptedTeamId: "team.new",
            playerLiveryName: "New #7",
            skillPlans: plans,
            masterySkills: catalog);

        Assert.Equal("team.new", start.Player.CurrentTeamId);
        Assert.Equal("New #7", start.Player.LiveryName);
        Assert.Equal(
            ["pace_rhythm", "pace_qualifying_sequence"],
            start.Player.Character!.AcquiredSkillIds);
        Assert.Equal(
            ["attribute_pace_01", "attribute_pace_02"],
            start.Player.Character.AcquiredAttributeNodeIds);
        Assert.Equal(CharacterProfile.CurrentMasteryEffectsVersion, start.Player.Character.MasteryEffectsVersion);
        Assert.Same(drivers, start.Drivers);
        Assert.Same(teams, start.Teams);
    }

    [Fact]
    public void Transition_LegacyEffectEnvelopesPreserveDormantProfileGate()
    {
        var catalog = SkillPlanBoundaryTestData.Catalog();
        var playerEnd = SkillPlanBoundaryTestData.Player();
        CharacterSkillPlanInput[] legacyPlans = SkillPlanBoundaryTestData
            .OrderedPlans(playerEnd, catalog)
            .Select(plan => plan with { EffectsVersion = 0 })
            .ToArray();

        var transitioned = CharacterSkillPlanTransition.Apply(playerEnd, legacyPlans, catalog);

        Assert.Equal(
            ["pace_rhythm", "pace_qualifying_sequence"],
            transitioned.Character!.AcquiredSkillIds);
        Assert.Equal(0, transitioned.Character.MasteryEffectsVersion);
    }

    [Fact]
    public void StoredPlanWithoutPinnedCatalogFailsClosedOnBothBoundaryEntryPoints()
    {
        var catalog = SkillPlanBoundaryTestData.Catalog();
        var playerEnd = SkillPlanBoundaryTestData.Player();
        var plans = SkillPlanBoundaryTestData.OrderedPlans(playerEnd, catalog);

        var direct = Assert.Throws<InvalidOperationException>(() =>
            CharacterSkillPlanTransition.Apply(playerEnd, plans, catalog: null));
        var rollover = Assert.Throws<InvalidOperationException>(() => SeasonRollover.Derive(
            playerEnd,
            [],
            [],
            acceptedTeamId: "team.new",
            playerLiveryName: null,
            skillPlans: plans,
            masterySkills: null));

        Assert.Contains("pinned mastery-skill catalog", direct.Message, StringComparison.Ordinal);
        Assert.Equal(direct.Message, rollover.Message);
        Assert.Null(playerEnd.Character!.AcquiredSkillIds);
        Assert.Equal(0, playerEnd.Character.SkillPointsSpent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(CareerExperienceModes.Smgp)]
    [InlineData(CareerExperienceModes.RacingPassport)]
    public void StoredPlanWithInconsistentExperienceModeFailsClosedOnBothBoundaryEntryPoints(
        string? experienceMode)
    {
        var catalog = SkillPlanBoundaryTestData.Catalog();
        var valid = SkillPlanBoundaryTestData.Player();
        var plans = SkillPlanBoundaryTestData.OrderedPlans(valid, catalog);
        var inconsistent = valid with { ExperienceMode = experienceMode };

        var direct = Assert.Throws<InvalidOperationException>(() =>
            CharacterSkillPlanTransition.Apply(inconsistent, plans, catalog));
        var rollover = Assert.Throws<InvalidOperationException>(() => SeasonRollover.Derive(
            inconsistent,
            [],
            [],
            acceptedTeamId: "team.new",
            playerLiveryName: null,
            skillPlans: plans,
            masterySkills: catalog));

        Assert.Equal(
            "The committed skill-plan experience mode and campaign plan do not match.",
            direct.Message);
        Assert.Equal(direct.Message, rollover.Message);
        Assert.Null(inconsistent.Character!.AcquiredSkillIds);
        Assert.Equal(0, inconsistent.Character.SkillPointsSpent);
    }
}

internal static class SkillPlanBoundaryTestData
{
    private const string HashA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string HashB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    public static CharacterRules Rules() =>
        CharacterRules.Parse(CareerTestData.ReadRules("perks.json"));

    public static MasterySkillCatalog Catalog()
    {
        var rules = Rules();
        var dna = RacingDnaCatalog.Parse(
            CareerTestData.ReadRules("racing-dna-v2.json"), rules);
        return MasterySkillCatalog.Parse(
            CareerTestData.ReadRules("mastery-skills-v2.json"), rules, dna);
    }

    public static CampaignProgressionPlan Campaign() => CampaignProgressionPlan.Create(
        CareerExperienceModes.GrandPrixDynasty,
        startYear: 1967,
        endYear: 2020,
        [
            Season("f1-1967", 1967, HashA),
            Season("f1-2020", 2020, HashB),
        ]);

    public static PlayerCareerState Player() => new()
    {
        Reputation = 40,
        Opi = 1.2,
        PaceAnchor = 92,
        SeasonsCompleted = 1,
        CurrentTeamId = "team.old",
        LiveryName = "Old #4",
        Character = Profile(),
        Level = 30,
        Xp = CharacterLevelProgression.CumulativeXpToLevel(
            CharacterLevelProgression.Level300Version, 30, Rules()),
        ExperienceMode = CareerExperienceModes.GrandPrixDynasty,
        CampaignProgressionPlan = Campaign(),
    };

    public static IReadOnlyList<CharacterSkillPlanInput> OrderedPlans(
        PlayerCareerState player,
        MasterySkillCatalog catalog)
    {
        var facts = Facts(player);
        var first = MasterySkillPlan.Prepare(
            player.Character!,
            ["pace_rhythm", "attribute_pace_01"],
            facts,
            catalog);
        var afterFirst = MasterySkillPlan.Apply(player.Character!, first, facts, catalog);
        var second = MasterySkillPlan.Prepare(
            afterFirst,
            ["pace_qualifying_sequence", "attribute_pace_02"],
            facts with { AvailableSkillPoints = facts.AvailableSkillPoints - first.TotalCost },
            catalog);
        return [first, second];
    }

    public static MasteryProgressionFacts Facts(PlayerCareerState player)
    {
        var campaign = player.CampaignProgressionPlan!;
        int available = CharacterProgressionV2Math.SkillPoints(
            player.Level,
            player.SeasonsCompleted,
            campaign.MasterySeason,
            player.Character!.SkillPointsSpent).Available;
        return new MasteryProgressionFacts(
            player.Level,
            available,
            player.SeasonsCompleted >= campaign.MasterySeason);
    }

    private static PinnedCampaignSeason Season(string packId, int year, string hash) => new()
    {
        PackId = packId,
        PackVersion = "1.0.0",
        Sha256 = hash,
        Year = year,
        ChampionshipRoundCount = 3,
    };

    private static CharacterProfile Profile()
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
}
