using Companion.Core.Character;

namespace Companion.Tests.Career;

/// <summary>The pure Driver-dossier projection (character depth 3): identity, stats, perks and
/// level/XP progress derived from the folded player state + the character rules.</summary>
public sealed class CharacterDossierTests
{
    private static CharacterRules Rules() => CharacterRules.Parse(CareerTestData.ReadRules("perks.json"));

    private static CharacterProfile Character(IReadOnlyList<string> perks, string name = "Ace") => new()
    {
        Name = name,
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
        Assert.Equal(3, dossier.Level);
        Assert.Equal(250, dossier.Xp);
        Assert.Equal(2, dossier.CpUnspent);

        Assert.Equal(7, dossier.Stats.Count);
        Assert.Contains(dossier.Stats, s => s.Id == "pace" && s.Value == 0.70 && s.Talent);
        Assert.Contains(dossier.Stats, s => s.Id == "marketability" && !s.Talent);
        Assert.Equal("Pace", dossier.Stats.First(s => s.Id == "pace").Label);

        var perk = Assert.Single(dossier.Perks);
        Assert.Equal("rain_man", perk.Id);
        Assert.False(string.IsNullOrEmpty(perk.Name));
        Assert.False(string.IsNullOrEmpty(perk.Description));

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
}
