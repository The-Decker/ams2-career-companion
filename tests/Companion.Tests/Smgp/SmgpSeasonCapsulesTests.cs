using Companion.Core.Smgp;

namespace Companion.Tests.Smgp;

/// <summary>
/// Guards the shipped 408 team-season history capsules (SMGP-024): every one of the 17 season
/// files exists with all 24 canon teams, every capsule carries its four fields, summaries stay
/// prose-sized, no two capsules share a body, the corpus never declares a champion, and the
/// canon machinery rule holds (VAPOR DN never gains an architecture). The capsules are the base
/// universe's own history: no player references, no placeholders.
/// </summary>
public sealed class SmgpSeasonCapsulesTests
{
    private static SmgpSeasonCapsules LoadShipped() =>
        SmgpSeasonCapsules.Load(Path.Combine(RepoRoot(), "data", "rules"));

    [Fact]
    public void The408Capsules_AllExist_WithAllFields()
    {
        var capsules = LoadShipped();
        Assert.Equal(17, capsules.Seasons.Count);
        Assert.Equal(Enumerable.Range(1, 17), capsules.Seasons.OrderBy(s => s));
        Assert.Equal(408, capsules.Count);

        var canon = SmgpCanon.Parse(File.ReadAllText(
            Path.Combine(RepoRoot(), "data", "rules", "smgp", "canon.json")));
        foreach (var team in canon.Teams)
        {
            var arc = capsules.ForTeam(team.Id);
            Assert.Equal(17, arc.Count);
            foreach (var (season, capsule) in arc)
            {
                Assert.Equal(season, arc[season - 1].Season);
                Assert.False(string.IsNullOrWhiteSpace(capsule.Summary), $"{team.Id} s{season}: empty summary");
                Assert.False(string.IsNullOrWhiteSpace(capsule.Objective), $"{team.Id} s{season}: empty objective");
                Assert.False(string.IsNullOrWhiteSpace(capsule.DefiningEvent), $"{team.Id} s{season}: empty definingEvent");
                Assert.False(string.IsNullOrWhiteSpace(capsule.CarryForward), $"{team.Id} s{season}: empty carryForward");
            }
        }
    }

    [Fact]
    public void Summaries_AreProseSized_AndNoTwoCapsulesShareABody()
    {
        var capsules = LoadShipped();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (int season in capsules.Seasons)
        foreach (var (teamId, capsule) in capsules.ForSeason(season))
        {
            int words = capsule.Summary.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            Assert.InRange(words, 40, 140);
            string normalized = string.Join(' ', capsule.Summary.Split(
                ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            Assert.True(seen.Add(normalized), $"duplicate capsule body at {teamId} s{season}");
        }
        Assert.Equal(408, seen.Count);
    }

    [Fact]
    public void TheCorpus_DeclaresNoChampion_AndNamesNoPlayer()
    {
        var capsules = LoadShipped();
        var playerWord = new System.Text.RegularExpressions.Regex(
            @"\bplayer\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        foreach (int season in capsules.Seasons)
        foreach (var (teamId, capsule) in capsules.ForSeason(season))
        {
            string all = string.Join(' ',
                capsule.Summary, capsule.Objective, capsule.DefiningEvent, capsule.CarryForward);
            Assert.DoesNotContain("{player", all, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotMatch(playerWord, all);
            Assert.False(
                all.Contains("won the title", StringComparison.OrdinalIgnoreCase) ||
                all.Contains("took the crown", StringComparison.OrdinalIgnoreCase) ||
                all.Contains("was champion", StringComparison.OrdinalIgnoreCase) ||
                all.Contains("became champion", StringComparison.OrdinalIgnoreCase),
                $"{teamId} s{season} declares a season champion: {all[..Math.Min(120, all.Length)]}");
        }
    }

    [Fact]
    public void VaporDn_NeverGainsAnArchitecture_AnywhereInTheCorpus()
    {
        var capsules = LoadShipped();
        foreach (int season in capsules.Seasons)
        foreach (var (teamId, capsule) in capsules.ForSeason(season))
        {
            string all = string.Join(' ',
                capsule.Summary, capsule.Objective, capsule.DefiningEvent, capsule.CarryForward);
            int index = all.IndexOf("VAPOR DN", StringComparison.Ordinal);
            while (index >= 0)
            {
                string tail = all[(index + "VAPOR DN".Length)..];
                if (tail.StartsWith("PQ", StringComparison.Ordinal))
                {
                    // VAPOR DNPQ V8 is a different official engine, not an architecture suffix.
                    index = all.IndexOf("VAPOR DN", index + 1, StringComparison.Ordinal);
                    continue;
                }
                tail = tail.TrimStart();
                Assert.False(tail.StartsWith("V", StringComparison.Ordinal) && tail.Length > 1 && char.IsDigit(tail[1]),
                    $"{teamId} s{season} gives VAPOR DN an architecture: ...{all[index..Math.Min(all.Length, index + 30)]}");
                index = all.IndexOf("VAPOR DN", index + 1, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void EmptyAndMissingInputs_StayAbsentTolerant()
    {
        Assert.Equal(0, SmgpSeasonCapsules.Empty.Count);
        Assert.Null(SmgpSeasonCapsules.Empty.ForTeamSeason("team.madonna", 1));
        Assert.Empty(SmgpSeasonCapsules.Load(
            Path.Combine(Path.GetTempPath(), "no-such-capsules-dir")).Seasons);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Companion.slnx")))
            dir = dir.Parent;
        Assert.True(dir is not null, "Companion.slnx not found above the test output directory.");
        return dir.FullName;
    }
}
