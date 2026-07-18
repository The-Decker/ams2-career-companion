using System.Text.RegularExpressions;
using Companion.Core.Newsroom;
using Companion.Tests.ViewModels;

namespace Companion.Tests.Newsroom;

/// <summary>The per-season newsroom packs (data/rules/newsroom/seasons/*.json): the
/// season-scoped layer that lets one era ("smgp") carry 17 different years of writing. These
/// tests pin the scoping contract (a season pack fires only in its own season) and the pool
/// integrity of every pack (every {pool:x} referenced resolves in the merged corpus).</summary>
public sealed class SeasonPackScopingTests
{
    private static readonly string NewsroomDir =
        Path.Combine(ViewModelTestData.RulesDirectory, "newsroom");

    private static readonly Regex PoolToken = new(@"\{pool:([a-zA-Z0-9]+)\}", RegexOptions.Compiled);

    private static NewsroomCorpus Corpus() => NewsroomCorpus.LoadDirectory(NewsroomDir);

    [Fact]
    public void TheActOnePacks_LoadThroughTheSubdirectoryMerge()
    {
        var corpus = Corpus();

        var actOne = corpus.Templates.Where(t => t.Id.StartsWith("s0", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(actOne);
        Assert.Contains(actOne, t => t.Id.StartsWith("s01.", StringComparison.Ordinal));
        Assert.Contains(actOne, t => t.Id.StartsWith("s02.", StringComparison.Ordinal));
        Assert.Contains(actOne, t => t.Id.StartsWith("s03.", StringComparison.Ordinal));
        Assert.Contains(actOne, t => t.Id.StartsWith("s04.", StringComparison.Ordinal));
        Assert.All(actOne, t => Assert.NotEmpty(t.Seasons));
    }

    [Fact]
    public void ASeasonPack_IsEligibleOnlyInItsOwnSeason()
    {
        var corpus = Corpus();
        var s01Ids = corpus.Templates
            .Where(t => t.Id.StartsWith("s01.", StringComparison.Ordinal))
            .Select(t => t.Id)
            .ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(s01Ids);

        var won = new NewsEvent
        {
            Kind = NewsEventKind.RaceWon,
            SeasonOrdinal = 1,
            SeasonYear = 1990,
            Round = 4,
            SubjectId = "player",
            SubjectName = "Pat Player",
            SubjectTeamId = "team.x",
            SubjectTeamName = "Xenon",
            VenueName = "Test Ring",
        };

        // In season 1 the pack competes for the slot; across many seeds it wins some of them.
        bool sawSeasonPack = false;
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var selected = corpus.Select(won, "smgp", deskId: "", masterSeed: seed);
            if (selected is not null && s01Ids.Contains(selected.Id))
            {
                sawSeasonPack = true;
                break;
            }
        }

        Assert.True(sawSeasonPack, "the s01 pack never won selection in its own season");

        // In seasons 2 and 14 the Act-I packs are simply ineligible, whatever the seed.
        foreach (int season in new[] { 2, 3, 14 })
        {
            var other = won with { SeasonOrdinal = season, SeasonYear = 1990 + season - 1 };
            for (ulong seed = 1; seed <= 40; seed++)
            {
                var selected = corpus.Select(other, "smgp", deskId: "", masterSeed: seed);
                if (selected is null)
                    continue;
                Assert.False(
                    selected.Id.StartsWith("s01.", StringComparison.Ordinal),
                    $"s01 template {selected.Id} was eligible in season {season}");
            }
        }
    }

    [Fact]
    public void EveryPoolReferencedByASeasonPack_ExistsInTheMergedCorpus()
    {
        var corpus = Corpus();
        var poolNames = corpus.Pools.Keys.ToHashSet(StringComparer.Ordinal);

        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(
                     Path.Combine(NewsroomDir, "seasons"), "*.json", SearchOption.TopDirectoryOnly))
        {
            string text = File.ReadAllText(file);
            foreach (Match match in PoolToken.Matches(text))
            {
                string pool = match.Groups[1].Value;
                if (!poolNames.Contains(pool))
                    offenders.Add($"{Path.GetFileName(file)}: pool:{pool}");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "unresolved pool references: " + string.Join("; ", offenders));
    }
}
