using Companion.Core.Career;
using Companion.Core.Character;

namespace Companion.Tests.Career;

/// <summary>
/// The dossier's MAX-LEVEL display state and resolved-modifier lines (commit 8a0427c): a v2
/// driver at the 14,951-XP cap reads Level 300 / <see cref="CharacterDossier.IsAtLevelCap"/> with
/// XpIntoLevel clamped to 0 (banked XP never renders as progress toward a level 301), and
/// <see cref="CharacterDossier.ActiveModifiers"/> projects the SAME resolved perk modifiers the
/// sim consumes — unconditional talent deltas as always-active lines, round-conditional effects
/// carrying their friendly condition label ("Wet rounds").
/// </summary>
public sealed class CharacterDossierCapAndModifiersTests
{
    private const string PackHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const long XpAtCap = 14_951; // CumulativeXpToLevel(2, 300) — pinned by CharacterLevelProgressionTests

    private static CharacterRules Rules() => CharacterRules.Parse(CareerTestData.ReadRules("perks.json"));

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
        IReadOnlyList<string> perks, int progressionVersion = 0) => new()
    {
        Name = "Ace",
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

    private static CharacterDossier BuildV2(int level, long xp) => CharacterDossier.Build(
        Character([], progressionVersion: CharacterLevelProgression.Level300Version),
        level,
        xp,
        Rules(),
        progressionYear: 1990,
        campaignProgressionPlan: Plan(),
        completedSeasons: 16);

    [Fact]
    public void Build_VersionTwoAtExactlyTheCapXp_ReadsTheFullMaxLevelState()
    {
        var dossier = BuildV2(level: CharacterLevelProgression.Level300Max, xp: XpAtCap);

        Assert.Equal(300, dossier.Level);
        Assert.Equal(300, dossier.LevelCap);
        Assert.True(dossier.IsAtLevelCap);
        Assert.Equal(0, dossier.XpIntoLevel);
        Assert.Equal(0, dossier.XpForNextLevel);
        Assert.Equal(1.0, dossier.LevelProgress);
    }

    [Fact]
    public void Build_MoreXpAtTheCap_BanksItWithoutInventingLevel301()
    {
        var dossier = BuildV2(level: CharacterLevelProgression.Level300Max, xp: 50_000);

        Assert.Equal(300, dossier.Level);
        Assert.True(dossier.IsAtLevelCap);
        // Banked XP is lifetime currency, never progress toward a level past the cap.
        Assert.Equal(0, dossier.XpIntoLevel);
        Assert.Equal(0, dossier.XpForNextLevel);
        Assert.Equal(50_000, dossier.LifetimeXp);
        // The curve itself agrees: no total XP can ever read as a level above 300.
        Assert.Equal(300, CharacterLevelProgression.LevelForTotalXp(2, 50_000, 1990, Rules()));
    }

    [Fact]
    public void Build_OneLevelBelowTheCap_IsNotAtTheCap()
    {
        var dossier = BuildV2(level: 299, xp: 14_950); // 60 XP into level 299, 1 short of the cap

        Assert.False(dossier.IsAtLevelCap);
        Assert.Equal(300, dossier.LevelCap);
        Assert.Equal(60, dossier.XpIntoLevel);
        Assert.Equal(61, dossier.XpForNextLevel);
        Assert.True(dossier.LevelProgress < 1.0);
    }

    [Fact]
    public void Build_ProjectsUnconditionalAndConditionalPerkModifiersAsReadableLines()
    {
        // rain_man (data/rules/perks.json) carries BOTH shapes: an unconditional statDelta
        // (+0.30 wetSkill) and wet/dry-conditional carScalar effects.
        var dossier = CharacterDossier.Build(Character(["rain_man"]), level: 3, xp: 250, Rules());

        Assert.NotEmpty(dossier.ActiveModifiers);

        // The unconditional talent delta is an always-active line with the friendly rating label.
        var wetPace = Assert.Single(dossier.ActiveModifiers,
            line => line.Effect == "+0.30 wet-weather pace");
        Assert.Null(wetPace.Condition);
        Assert.True(wetPace.AlwaysActive);

        // The wetRound-conditional car effect waits on its friendly condition label.
        var wetOnly = Assert.Single(dossier.ActiveModifiers,
            line => line.Condition == "Wet rounds");
        Assert.Equal("+0.020 car power", wetOnly.Effect);
        Assert.False(wetOnly.AlwaysActive);

        // Its dry-weather counterpart proves each condition token maps independently.
        var dryOnly = Assert.Single(dossier.ActiveModifiers,
            line => line.Condition == "Dry rounds");
        Assert.Equal("-0.008 car power", dryOnly.Effect);
        Assert.False(dryOnly.AlwaysActive);
    }
}
