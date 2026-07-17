using Companion.Core.Smgp;
using Companion.Tests.ViewModels;

namespace Companion.Tests.Smgp;

/// <summary>
/// Content validation for the authored 17-season SMGP campaign lore (data/rules/smgp/seasons.json,
/// SMGP-300 §8.2): exactly 17 seasons, unique identities, contiguous era blocks, per-season content
/// minimums, no placeholder text, and outcome-agnostic authorship (the sim decides results; canon
/// may only assert the PRE-CAMPAIGN baseline). Loaded from the same shipped file the app reads.
/// </summary>
public sealed class SmgpSeasonLoreTests
{
    private static readonly SmgpSeasonLore Lore = SmgpSeasonLore.Load(ViewModelTestData.RulesDirectory);

    private static readonly string[] EraBlocks =
    [
        "The Iron Circus",
        "The Horsepower War",
        "The Safety Reckoning",
        "The Golden Circus",
    ];

    [Fact]
    public void ShipsExactlySeventeenSeasons_Ordinals1Through17()
    {
        Assert.False(Lore.IsEmpty, "data/rules/smgp/seasons.json must ship");
        Assert.Equal(17, Lore.Seasons.Count);
        Assert.Equal(Enumerable.Range(1, 17), Lore.Seasons.Select(s => s.Ordinal));
        for (int ordinal = 1; ordinal <= 17; ordinal++)
            Assert.NotNull(Lore.ForOrdinal(ordinal));
        Assert.Null(Lore.ForOrdinal(0));
        Assert.Null(Lore.ForOrdinal(18)); // no season 18 exists to author
    }

    [Fact]
    public void EverySeasonHasAUniqueIdentity()
    {
        Assert.Equal(17, Lore.Seasons.Select(s => s.Title).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(17, Lore.Seasons.Select(s => s.Subtitle).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(17, Lore.Seasons.Select(s => s.Overview).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void ErasFormFourContiguousBlocksInCampaignOrder()
    {
        var eras = Lore.Seasons.Select(s => s.Era).ToList();
        Assert.All(eras, era => Assert.Contains(era, EraBlocks));

        // Contiguous: the sequence of DISTINCT eras in ordinal order is exactly the four blocks.
        var blocks = new List<string>();
        foreach (string era in eras)
        {
            if (blocks.Count == 0 || !string.Equals(blocks[^1], era, StringComparison.Ordinal))
                blocks.Add(era);
        }
        Assert.Equal(EraBlocks, blocks);
    }

    [Fact]
    public void EverySeasonMeetsTheContentMinimums()
    {
        foreach (var season in Lore.Seasons)
        {
            Assert.True(season.Title.Length >= 3, $"S{season.Ordinal} title");
            Assert.True(season.Subtitle.Length >= 10, $"S{season.Ordinal} subtitle");
            Assert.True(season.Overview.Length >= 200, $"S{season.Ordinal} overview must be substantial");
            Assert.True(season.Preseason.Length >= 60, $"S{season.Ordinal} preseason");
            Assert.True(season.Technical.Length >= 60, $"S{season.Ordinal} technical");
            Assert.True(season.Safety.Length >= 60, $"S{season.Ordinal} safety");
            Assert.True(season.Themes.Count >= 4, $"S{season.Ordinal} themes");
            Assert.True(season.Timeline.Count >= 6, $"S{season.Ordinal} timeline");
            Assert.True(season.Arcs.Count >= 4, $"S{season.Ordinal} arcs");
            Assert.True(season.Hooks.Count >= 8, $"S{season.Ordinal} hooks");
            Assert.True(season.Contenders.Count >= 3, $"S{season.Ordinal} contenders");
            Assert.True(season.Milestones.Count >= 2, $"S{season.Ordinal} milestones");
        }
    }

    [Fact]
    public void NoPlaceholderTextAnywhere()
    {
        string[] forbidden = ["TODO", "TBD", "lorem", "placeholder", "FIXME", "XXX"];
        foreach (var season in Lore.Seasons)
        {
            foreach (string text in AllStrings(season))
            {
                foreach (string token in forbidden)
                {
                    Assert.False(
                        text.Contains(token, StringComparison.OrdinalIgnoreCase),
                        $"S{season.Ordinal} contains placeholder token '{token}': {text[..Math.Min(80, text.Length)]}");
                }
            }
        }
    }

    [Fact]
    public void SennaHeadlinesTheOpeningSeason_TheBenchmarkIsNeverNerfed()
    {
        var opening = Lore.ForOrdinal(1)!;
        Assert.Contains(opening.Contenders, c => c.Contains("Senna", StringComparison.OrdinalIgnoreCase));
        // The summit season still fields the benchmark among its contenders.
        var summit = Lore.ForOrdinal(17)!;
        Assert.Contains(summit.Contenders, c => c.Contains("Senna", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TheLoreNeverAssertsACampaignChampion()
    {
        // Outcome sovereignty: no season may declare who won a CAMPAIGN season's title. The
        // guard phrases are outcome-declaring constructions; noun uses like "dry-race wins the
        // championship has recorded" are legitimate (the object is "wins", not a verdict).
        string[] forbidden = ["wins the title", "won the title", "takes the title", "took the title",
            "clinches the championship", "clinched the championship", "is crowned champion", "was crowned champion"];
        foreach (var season in Lore.Seasons)
        {
            foreach (string text in AllStrings(season))
            {
                foreach (string phrase in forbidden)
                {
                    Assert.False(
                        text.Contains(phrase, StringComparison.OrdinalIgnoreCase),
                        $"S{season.Ordinal} asserts a sim-owned outcome: {text[..Math.Min(100, text.Length)]}");
                }
            }
        }
    }

    private static IEnumerable<string> AllStrings(SmgpSeasonLoreEntry season)
    {
        yield return season.Title;
        yield return season.Subtitle;
        yield return season.Era;
        yield return season.Overview;
        yield return season.Preseason;
        yield return season.Technical;
        yield return season.Safety;
        foreach (string s in season.Themes) yield return s;
        foreach (string s in season.Timeline) yield return s;
        foreach (string s in season.Arcs) yield return s;
        foreach (string s in season.Hooks) yield return s;
        foreach (string s in season.Contenders) yield return s;
        foreach (string s in season.Milestones) yield return s;
    }
}
