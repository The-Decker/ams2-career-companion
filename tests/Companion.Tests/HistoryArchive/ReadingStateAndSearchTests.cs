using Companion.Core.HistoryArchive;
using Companion.Core.Newsroom;
using Companion.Data;
using Companion.ViewModels.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Companion.Tests.HistoryArchive;

/// <summary>Schema v6 reading state (user preference, survives replay wipes) + unified search.</summary>
public sealed class ReadingStateAndSearchTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("companion-readingstate-").FullName;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    [Fact]
    public void AGenuineV5FileGainsTheReadingStateTableInPlace()
    {
        var path = Path.Combine(_root, "old.ams2career");
        using (var raw = new SqliteConnection($"Data Source={path};Pooling=false"))
        {
            raw.Open();
            Migrations.Apply(raw, targetVersion: 5);
        }

        using var db = CareerDatabase.Open(path);
        Assert.Equal(6, db.SchemaVersion);
        Assert.Empty(NewsReadingStateStore.ReadAll(db)); // upgraded in place, no rows invented

        NewsReadingStateStore.MarkRead(db, "raceWon:1:5:player", "2026-07-16T12:00:00Z");
        Assert.True(NewsReadingStateStore.ReadAll(db)["raceWon:1:5:player"].IsRead);
    }

    [Fact]
    public void ReadAndBookmarkStateRoundTripsHonestly()
    {
        using var db = CareerDatabase.Open(Path.Combine(_root, "state.ams2career"));

        // The FIRST read timestamp is kept; re-reading never rewrites history.
        NewsReadingStateStore.MarkRead(db, "k1", "2026-07-16T10:00:00Z");
        NewsReadingStateStore.MarkRead(db, "k1", "2026-07-16T11:00:00Z");
        Assert.Equal("2026-07-16T10:00:00Z", NewsReadingStateStore.ReadAll(db)["k1"].ReadUtc);

        // Bookmarks toggle; clearing one clears its timestamp; read state is untouched.
        NewsReadingStateStore.SetBookmark(db, "k1", bookmarked: true, "2026-07-16T12:00:00Z");
        var bookmarked = NewsReadingStateStore.ReadAll(db)["k1"];
        Assert.True(bookmarked.Bookmarked);
        Assert.Equal("2026-07-16T12:00:00Z", bookmarked.BookmarkedUtc);
        Assert.True(bookmarked.IsRead);

        NewsReadingStateStore.SetBookmark(db, "k1", bookmarked: false, "2026-07-16T13:00:00Z");
        var cleared = NewsReadingStateStore.ReadAll(db)["k1"];
        Assert.False(cleared.Bookmarked);
        Assert.Null(cleared.BookmarkedUtc);
        Assert.True(cleared.IsRead);

        // A bookmark on an unread story stands alone.
        NewsReadingStateStore.SetBookmark(db, "k2", bookmarked: true, "2026-07-16T14:00:00Z");
        Assert.False(NewsReadingStateStore.ReadAll(db)["k2"].IsRead);
    }

    [Fact]
    public void ReadingStateSurvivesTheDerivedDataWipe()
    {
        using var db = CareerDatabase.Open(Path.Combine(_root, "wipe.ams2career"));
        NewsReadingStateStore.MarkRead(db, "k1", "2026-07-16T10:00:00Z");
        NewsReadingStateStore.SetBookmark(db, "k2", bookmarked: true, "2026-07-16T10:05:00Z");

        StateStore.WipeDerived(db); // what Resimulate does before refolding

        var states = NewsReadingStateStore.ReadAll(db);
        Assert.True(states["k1"].IsRead);
        Assert.True(states["k2"].Bookmarked);
    }

    [Fact]
    public void SearchSpansNewsAndHistoryWithMatchReasons()
    {
        var index = BuildIndex();

        var senna = index.Search("Ayrton");
        Assert.Contains(senna, r => r.Kind == "driver" && r.MatchedOn == "title");

        var articles = index.Search("maiden victory");
        var hit = Assert.Single(articles, r => r.Kind == "article");
        Assert.Equal("body", hit.MatchedOn);
        Assert.Equal(ContentProvenance.CareerUniverse, hit.Provenance);

        // Scope + provenance filters carve the same corpus.
        Assert.DoesNotContain(index.Search("Ayrton", ArchiveSearchScope.News), r => r.Kind == "driver");
        Assert.DoesNotContain(
            index.Search("victory", provenance: ArchiveSearchProvenance.RealHistory),
            r => r.Provenance == ContentProvenance.CareerUniverse);
    }

    [Fact]
    public void TeamAliasStringsFindTheirCanonicalIdentity()
    {
        var results = BuildIndex().Search("Lotus-Honda");
        var team = Assert.Single(results, r => r.Kind == "team");
        Assert.Equal("Lotus", team.Title);
        Assert.Equal("alias", team.MatchedOn);
    }

    [Fact]
    public void ShortAndUnmatchedQueriesReturnEmpty()
    {
        var index = BuildIndex();
        Assert.Empty(index.Search("a"));
        Assert.Empty(index.Search("zzzzxq"));
        Assert.Empty(index.Search("   "));
    }

    [Fact]
    public void TitlePrefixOutranksBodyMentions()
    {
        var results = BuildIndex().Search("Ayrton Senna");
        Assert.Equal("driver", results[0].Kind); // name match beats article body mentions
    }

    private static ArchiveSearchIndex BuildIndex()
    {
        var article = new NewsroomArticle
        {
            Key = "firstWin:1:5:player",
            EventKind = NewsEventKind.FirstWin,
            Category = NewsroomCategory.RecordsAndMilestones,
            Status = EditorialStatus.Confirmed,
            Provenance = ContentProvenance.CareerUniverse,
            SeasonOrdinal = 1,
            SeasonYear = 1988,
            Round = 5,
            VenueName = "Monza",
            SubjectName = "A. Tester",
            Headline = "Breakthrough at Monza",
            Summary = "A first career win.",
            Sections = [new NewsroomSection("lead", "A maiden victory arrives, with Ayrton Senna second.")],
            ImportanceScore = 80,
            Tier = EditorialTier.Lead,
        };

        var thread = new StoryThread
        {
            Key = "thread:title:1",
            Type = StoryThreadType.TitleFight,
            State = StoryThreadState.Developing,
            Title = "The season 1 title race",
            SeasonOrdinal = 1,
        };

        var history = new HistoryArchiveIndex
        {
            Drivers =
            [
                new DriverHistoryProfile
                {
                    Name = "Ayrton Senna", FirstYear = 1984, LastYear = 1994, SeasonsEntered = 11,
                    Starts = 161, Wins = 41, Podiums = 80, FastestLaps = 19,
                    Stints = [new DriverTeamStint("McLaren-Honda", 1988, 1992)],
                },
            ],
            Teams =
            [
                new TeamHistoryProfile
                {
                    Canonical = "Lotus", Aliases = ["Lotus-Ford", "Lotus-Honda"],
                    FirstYear = 1967, LastYear = 1994, Wins = 45,
                    DriversFielded = 30,
                },
            ],
            Circuits =
            [
                new CircuitHistoryProfile
                {
                    LayoutId = "monza-1", Name = "Monza", Place = "Italy",
                    Editions = [new CircuitEdition(1988, 12, "Italian Grand Prix", "Gerhard Berger", "Ferrari")],
                },
            ],
            Timeline = [],
            Reference = HistoryArchiveData.Empty,
            YearsCovered = [1988],
        };

        return ArchiveSearchIndex.Build([article], [thread], history);
    }
}
