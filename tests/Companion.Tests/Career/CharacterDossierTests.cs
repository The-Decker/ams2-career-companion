using Companion.Core.Career;
using Companion.Core.Character;

namespace Companion.Tests.Career;

/// <summary>The pure Driver-dossier projection (character depth 3): identity, stats, perks and
/// level/XP progress derived from the folded player state + the character rules.</summary>
public sealed class CharacterDossierTests
{
    private const string PackHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private static CharacterRules Rules() => CharacterRules.Parse(CareerTestData.ReadRules("perks.json"));

    private static MasterySkillCatalog Mastery(CharacterRules rules) => MasterySkillCatalog.Parse(
        CareerTestData.ReadRules("mastery-skills-v2.json"),
        rules,
        RacingDnaCatalog.Parse(CareerTestData.ReadRules("racing-dna-v2.json"), rules));

    private static RacingDnaCatalog RacingDna(CharacterRules rules) =>
        RacingDnaCatalog.Parse(CareerTestData.ReadRules("racing-dna-v2.json"), rules);

    private static CampaignProgressionPlan Plan() => CampaignProgressionPlan.CreateSmgp(
        new PinnedCampaignSeason
        {
            PackId = "smgp-1",
            PackVersion = "1.0.0",
            Sha256 = PackHash,
            Year = 1990,
            ChampionshipRoundCount = 16,
        });

    private static CharacterProfile Character(
        IReadOnlyList<string> perks, string name = "Ace", int progressionVersion = 0) => new()
    {
        Name = name,
        CountryCode = "BRA",
        ProgressionVersion = progressionVersion,
        Stats = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["pace"] = 0.70, ["oneLap"] = 0.55, ["craft"] = 0.50, ["racecraft"] = 0.45,
            ["adaptability"] = 0.50, ["marketability"] = 0.60, ["durability"] = 0.50,
        },
        PerkIds = perks,
        CpUnspent = 2,
    };

    [Fact]
    public void Build_ProjectsIdentityStatsPerksAndProgression()
    {
        var rules = Rules();
        var dossier = CharacterDossier.Build(Character(["rain_man"]), level: 3, xp: 250, rules);

        Assert.Equal("Ace", dossier.Name);
        Assert.Equal("BRA", dossier.CountryCode);
        Assert.Equal(3, dossier.Level);
        Assert.Equal(250, dossier.Xp);
        Assert.Equal(250, dossier.LifetimeXp);
        Assert.Equal(250, dossier.AvailableResetXp);
        // Available CP = creation leftover (2) + level grants (2/level × 2 levels past L1) − spent (0).
        Assert.Equal(2 + rules.Levels.LevelGrants.CharacterPointsPerLevel * 2, dossier.CpUnspent);

        Assert.Equal(7, dossier.Stats.Count);
        Assert.Contains(dossier.Stats, s => s.Id == "pace" && s.Value == 0.70 && s.Talent);
        Assert.Contains(dossier.Stats, s => s.Id == "marketability" && !s.Talent);
        Assert.Equal("Pace", dossier.Stats.First(s => s.Id == "pace").Label);

        var perk = Assert.Single(dossier.Perks);
        Assert.Equal("rain_man", perk.Id);
        Assert.False(string.IsNullOrEmpty(perk.Name));
        Assert.False(string.IsNullOrEmpty(perk.Description));
        Assert.NotEmpty(perk.Effects);
        Assert.Equal(
            perk.Effects.Where(line => line.Kind == "benefit").Select(line => line.Text),
            perk.Benefits);
        Assert.Equal(
            perk.Effects.Where(line => line.Kind == "drawback").Select(line => line.Text),
            perk.Drawbacks);
        Assert.Contains(perk.Effects, line =>
            line.Classification == CharacterEffectClass.Expectation &&
            line.ClassificationLabel == "EXPECTATION");
        Assert.Contains(perk.Effects, line =>
            line.Classification == CharacterEffectClass.Car &&
            line.ClassificationLabel == "CAR" &&
            line.IsConditional);

        // Cumulative XP to reach level 3 = XpForLevel(2) + XpForLevel(3) = 100 + 135 = 235.
        Assert.Equal(15, dossier.XpIntoLevel);                       // 250 − 235
        Assert.Equal(rules.Levels.XpCurve.XpForLevel(4), dossier.XpForNextLevel);
        Assert.InRange(dossier.LevelProgress, 0.0, 1.0);
    }

    [Fact]
    public void Build_AtMaxLevel_HasNoNextLevel_AndFullProgress()
    {
        var rules = Rules();
        int maxLevel = rules.Levels.XpCurve.MaxLevel;

        var dossier = CharacterDossier.Build(Character([]), maxLevel, xp: 9_999_999, rules);

        Assert.Equal(0, dossier.XpForNextLevel);
        Assert.Equal(1.0, dossier.LevelProgress);
        Assert.Empty(dossier.Perks);
    }

    [Fact]
    public void Build_VersionTwoAtLevel299_UsesTheLevel300CurveInEveryEra()
    {
        var dossier = CharacterDossier.Build(
            Character([], progressionVersion: CharacterLevelProgression.Level300Version),
            level: 299,
            xp: 14_890,
            Rules(),
            progressionYear: 1967,
            campaignProgressionPlan: Plan(),
            completedSeasons: 16);

        Assert.Equal(0, dossier.XpIntoLevel);
        Assert.Equal(61, dossier.XpForNextLevel);
        Assert.Equal(0.0, dossier.LevelProgress);
    }

    [Fact]
    public void Build_VersionTwoAtLevel300_HasNoNextLevelAndFullProgress()
    {
        var dossier = CharacterDossier.Build(
            Character([], progressionVersion: CharacterLevelProgression.Level300Version),
            level: CharacterLevelProgression.Level300Max,
            xp: 14_951,
            Rules(),
            progressionYear: 1967,
            campaignProgressionPlan: Plan(),
            completedSeasons: 16);

        Assert.Equal(0, dossier.XpIntoLevel);
        Assert.Equal(0, dossier.XpForNextLevel);
        Assert.Equal(1.0, dossier.LevelProgress);
    }

    [Fact]
    public void Build_VersionTwoProjectsTheAuthoritativeSkillPointPool()
    {
        var character = Character(
            [],
            progressionVersion: CharacterLevelProgression.Level300Version) with
        {
            CpUnspent = 999,
            CpSpent = 888,
            SkillPointsSpent = 7,
            XpSpentOnResets = 500,
            SkillResetCount = 1,
        };

        var dossier = CharacterDossier.Build(
            character,
            level: CharacterLevelProgression.Level300Max,
            xp: 14_951,
            Rules(),
            progressionYear: 1990,
            campaignProgressionPlan: Plan(),
            completedSeasons: 16);

        Assert.Equal(492, dossier.CpUnspent);
        Assert.Equal(14_951, dossier.LifetimeXp);
        Assert.Equal(14_451, dossier.AvailableResetXp);
    }

    [Fact]
    public void Build_VersionTwoProjectsThePersistedExactRacingDnaIdentity()
    {
        CharacterRules rules = Rules();
        RacingDnaCatalog catalog = RacingDna(rules);
        CharacterProfile character = Character(
            [],
            progressionVersion: CharacterLevelProgression.Level300Version) with
        {
            RacingDnaId = "dna_circuit_specialist",
            RacingDnaVersion = 1,
            RacingDnaChoice = "technical",
        };

        CharacterDossier dossier = CharacterDossier.Build(
            character,
            level: 1,
            xp: 0,
            rules,
            campaignProgressionPlan: Plan(),
            racingDnaCatalog: catalog);

        RacingDnaDefinition definition = catalog.Get("dna_circuit_specialist", 1);
        DossierRacingDna dna = Assert.IsType<DossierRacingDna>(dossier.RacingDna);
        Assert.Equal(definition.Id, dna.Id);
        Assert.Equal(definition.Version, dna.Version);
        Assert.Equal(definition.Name, dna.Name);
        Assert.Equal(definition.Description, dna.Description);
        Assert.Equal("Pace / Era-flavor", dna.FamilyLine);
        Assert.Equal(definition.PersistentEffects, dna.PersistentEffects);
        Assert.Equal(definition.TradeoffEffects, dna.TradeoffEffects);
        Assert.Equal(definition.Choice!.Kind, dna.ChoiceKind);
        Assert.Equal(definition.Choice.Prompt, dna.ChoicePrompt);
        Assert.Equal("technical", dna.ChoiceValue);
    }

    [Fact]
    public void Build_RacingDnaProjectionNeverSubstitutesAnotherDefinitionVersion()
    {
        CharacterRules rules = Rules();
        CharacterProfile character = Character(
            [],
            progressionVersion: CharacterLevelProgression.Level300Version) with
        {
            RacingDnaId = "dna_prodigy",
            RacingDnaVersion = 999,
        };

        Assert.Throws<KeyNotFoundException>(() => CharacterDossier.Build(
            character,
            level: 1,
            xp: 0,
            rules,
            campaignProgressionPlan: Plan(),
            racingDnaCatalog: RacingDna(rules)));
    }

    [Fact]
    public void Build_VersionOneIn1967_RetainsTheEraLevelCap()
    {
        const int goldenAgeCap = 26;
        var rules = Rules();
        long xpAtCap = CharacterLevelProgression.CumulativeXpToLevel(
            CharacterLevelProgression.EraCappedVersion,
            goldenAgeCap,
            rules);

        var dossier = CharacterDossier.Build(
            Character([], progressionVersion: CharacterLevelProgression.EraCappedVersion),
            level: goldenAgeCap,
            xp: xpAtCap,
            rules,
            progressionYear: 1967);

        Assert.Equal(goldenAgeCap, dossier.Level);
        Assert.Equal(0, dossier.XpIntoLevel);
        Assert.Equal(0, dossier.XpForNextLevel);
        Assert.Equal(1.0, dossier.LevelProgress);
    }

    [Fact]
    public void Build_HealthyDriver_ReadsFit()
    {
        var dossier = CharacterDossier.Build(Character([]), level: 1, xp: 0, Rules());

        Assert.Equal(AvailabilityStatus.Fit, dossier.Availability);
        Assert.Equal("Fit", dossier.AvailabilityLabel);
    }

    [Fact]
    public void Build_MasteryInjuryDrawbackSurfacesTheSameRiskGateAsSeasonEnd()
    {
        CharacterRules rules = Rules();
        MasterySkillCatalog mastery = Mastery(rules);
        CharacterProfile character = Character(
            [],
            progressionVersion: CharacterLevelProgression.Level300Version) with
        {
            MasteryEffectsVersion = CharacterProfile.CurrentMasteryEffectsVersion,
            AcquiredSkillIds = ["racecraft_switchback_school"],
        };

        CharacterDossier dossier = CharacterDossier.Build(
            character,
            level: 90,
            xp: CharacterLevelProgression.CumulativeXpToLevel(
                CharacterLevelProgression.Level300Version,
                90,
                rules),
            rules,
            campaignProgressionPlan: Plan(),
            masterySkills: mastery);

        Assert.Equal("Low", dossier.InjuryRisk);
    }

    [Fact]
    public void Build_ProtectionOnlyAndEffectsVersionZeroDoNotInventAnInjuryRoll()
    {
        CharacterRules rules = Rules();
        MasterySkillCatalog mastery = Mastery(rules);
        CharacterProfile active = Character(
            [],
            progressionVersion: CharacterLevelProgression.Level300Version) with
        {
            MasteryEffectsVersion = CharacterProfile.CurrentMasteryEffectsVersion,
            AcquiredSkillIds = ["physical_recovery_habits"],
        };
        CharacterProfile legacyEnvelope = active with
        {
            MasteryEffectsVersion = 0,
            AcquiredSkillIds = ["racecraft_switchback_school"],
        };

        CharacterDossier protection = CharacterDossier.Build(
            active,
            level: 1,
            xp: 0,
            rules,
            campaignProgressionPlan: Plan(),
            masterySkills: mastery);
        CharacterDossier inert = CharacterDossier.Build(
            legacyEnvelope,
            level: 1,
            xp: 0,
            rules,
            campaignProgressionPlan: Plan(),
            masterySkills: mastery);

        Assert.Null(protection.InjuryRisk);
        Assert.Null(inert.InjuryRisk);
    }

    [Fact]
    public void Build_LegacyInjuryPerkKeepsItsExistingRiskWithoutAMasteryCatalog()
    {
        CharacterDossier dossier = CharacterDossier.Build(
            Character(["hard_charger"]),
            level: 1,
            xp: 0,
            Rules());

        Assert.NotNull(dossier.InjuryRisk);
    }

    [Theory]
    [InlineData(1, false, false, AvailabilityStatus.Injured, "Injured, out 1 race")]
    [InlineData(2, false, false, AvailabilityStatus.Injured, "Injured, out 2 races")]
    [InlineData(0, true, false, AvailabilityStatus.SeasonOver, "Season over, recovering")]
    [InlineData(0, false, true, AvailabilityStatus.Deceased, "Deceased")]
    // Precedence, deceased trumps a season-ending injury trumps a suspension (mirrors IsFit).
    [InlineData(3, true, true, AvailabilityStatus.Deceased, "Deceased")]
    [InlineData(3, true, false, AvailabilityStatus.SeasonOver, "Season over, recovering")]
    public void Build_ProjectsAvailabilityFromFoldedInjuryState(
        int suspension, bool seasonEnding, bool deceased, AvailabilityStatus expected, string expectedLabel)
    {
        var dossier = CharacterDossier.Build(
            Character([]), level: 1, xp: 0, Rules(), age: null,
            raceSuspensionRemaining: suspension, seasonEndingInjury: seasonEnding, deceased: deceased);

        Assert.Equal(expected, dossier.Availability);
        Assert.Equal(expectedLabel, dossier.AvailabilityLabel);
    }
}
