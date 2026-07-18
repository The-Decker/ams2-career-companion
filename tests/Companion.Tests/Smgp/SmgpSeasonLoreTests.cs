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

    [Fact]
    public void PlayerTeamIsAToken_NotBakedIn()
    {
        // The player's OWN team is a creation choice that changes across the campaign, so the lore
        // must never hard-code one team as the player's. Season 1's arrival line is the canonical
        // case: it carries the {playerTeam} token, not "Minarae" (the skeleton's original bug).
        var season1 = Lore.ForOrdinal(1)!;
        Assert.Contains(SmgpSeasonLoreEntry.PlayerTeamToken, season1.Subtitle, StringComparison.Ordinal);
        Assert.DoesNotContain("Minarae garage", season1.Subtitle, StringComparison.OrdinalIgnoreCase);

        // Any field that carries the token is a PLAYER reference, never a hard-coded team beside it.
        foreach (var season in Lore.Seasons)
        {
            foreach (string text in AllStrings(season))
            {
                if (text.Contains(SmgpSeasonLoreEntry.PlayerTeamToken, StringComparison.Ordinal))
                {
                    Assert.DoesNotContain("Minarae", text, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }

    [Fact]
    public void WithPlayerTeam_ResolvesEveryToken_ToTheRealTeam()
    {
        foreach (var season in Lore.Seasons)
        {
            var resolved = season.WithPlayerTeam("Zeroforce");
            foreach (string text in AllStrings(resolved))
            {
                Assert.DoesNotContain(SmgpSeasonLoreEntry.PlayerTeamToken, text, StringComparison.Ordinal);
            }
        }

        // The visible bug's exact fix: season 1 now names the driver's real team.
        var season1 = Lore.ForOrdinal(1)!.WithPlayerTeam("Zeroforce");
        Assert.Contains("Zeroforce garage", season1.Subtitle, StringComparison.Ordinal);
        Assert.DoesNotContain("Minarae", season1.Subtitle, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NoPlayerLineNamesALiteralTeam()
    {
        // The guard that makes the "Minarae bug" impossible to reintroduce: the player's OWN team
        // is dynamic (a creation choice that moves across the campaign), so it is ALWAYS the
        // {playerTeam} token, never a literal team in a membership construction. World teams
        // (Senna's Madonna, Nono's Minarae) stay literal, but they never sit in a PLAYER sentence.
        // A player-marked line naming "<Team> garage/signing/seat/overalls" fails the build.
        string[] teams =
        [
            "Azalea", "Bestowal", "Blanche", "Bullets", "Comet", "Cool", "Dardan", "Feet",
            "Firenze", "Iris", "Joke", "Lares", "Linden", "Losel", "Madonna", "May", "Millions",
            "Minarae", "Moon", "Orchis", "Rigel", "Serga", "Tyrant", "Zeroforce",
        ];
        // Phrases that describe ONLY the player in this lore (verified, never an AI driver).
        string[] playerMarkers =
        [
            "the climber", "unheralded", "stranger at the anvil", "the new name",
            "posted, without ceremony", "the newcomer",
        ];
        // Team-membership nouns: "<Team> garage" reads as "the player is AT that team".
        string[] membership = ["garage", "signing", "seat", "overalls", "locker", "colours"];

        foreach (var season in Lore.Seasons)
        {
            foreach (string text in AllStrings(season))
            {
                if (!playerMarkers.Any(m => text.Contains(m, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
                foreach (string team in teams)
                {
                    foreach (string noun in membership)
                    {
                        Assert.False(
                            text.Contains($"{team} {noun}", StringComparison.OrdinalIgnoreCase),
                            $"S{season.Ordinal} names the player at the literal team '{team} {noun}', " +
                            $"use the {SmgpSeasonLoreEntry.PlayerTeamToken} token instead. Line: {text}");
                    }
                }
            }
        }
    }

    [Fact]
    public void WithoutReplacedDriver_DropsEveryLineNamingThatDriver()
    {
        // A player who takes Nono's Minarae seat benches him, the lore must not narrate Nono as an
        // active participant. Season 17 gives him an arc and a hook in the raw lore.
        var season17 = Lore.ForOrdinal(17)!;
        Assert.Contains(season17.Arcs, a => a.Contains("Nono", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(season17.Hooks, h => h.Contains("Nono", StringComparison.OrdinalIgnoreCase));

        var reconciled = season17.WithoutReplacedDriver("Nono");
        Assert.DoesNotContain(reconciled.Arcs, a => a.Contains("Nono", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(reconciled.Hooks, h => h.Contains("Nono", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(reconciled.Timeline, t => t.Contains("Nono", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(reconciled.Contenders, c => c.Contains("Nono", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(reconciled.Milestones, m => m.Contains("Nono", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(reconciled.Themes, t => t.Contains("Nono", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WithoutReplacedDriver_LeavesUnnamedDriversAndUsesWholeWords()
    {
        var season17 = Lore.ForOrdinal(17)!;

        // A driver the lore never names (the player's real Zeroforce seat) drops nothing.
        var klinger = season17.WithoutReplacedDriver("Klinger");
        Assert.Equal(season17.Arcs.Count, klinger.Arcs.Count);
        Assert.Equal(season17.Hooks.Count, klinger.Hooks.Count);

        // Empty surname is a no-op (same instance).
        Assert.Same(season17, season17.WithoutReplacedDriver(""));
        Assert.Same(season17, season17.WithoutReplacedDriver(null));

        // Whole-word only: a surname embedded in a longer word never triggers a false drop.
        Assert.Equal(season17.Arcs.Count, season17.WithoutReplacedDriver("No").Arcs.Count);
    }

    [Fact]
    public void WithPlayerTeam_EmptyName_FallsBackGrammatically()
    {
        var season1 = Lore.ForOrdinal(1)!.WithPlayerTeam("");
        Assert.DoesNotContain(SmgpSeasonLoreEntry.PlayerTeamToken, season1.Subtitle, StringComparison.Ordinal);
        // "the {playerTeam} garage" → "the home garage", grammatical, never a raw token or "the  garage".
        Assert.Contains("the home garage", season1.Subtitle, StringComparison.Ordinal);
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
