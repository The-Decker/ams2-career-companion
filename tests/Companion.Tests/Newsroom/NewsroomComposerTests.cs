using Companion.Core.Newsroom;
using Companion.Tests.ViewModels;
using Xunit;

namespace Companion.Tests.Newsroom;

/// <summary>
/// The composer over the SHIPPED newsroom packs: situation specificity, deterministic output,
/// stable desks, and the no-unresolved-tokens guarantee across the whole content library.
/// </summary>
public class NewsroomComposerTests
{
    private static readonly string NewsroomDirectory =
        Path.Combine(ViewModelTestData.RulesDirectory, "newsroom");

    private static readonly NewsroomCorpus Corpus = NewsroomCorpus.LoadDirectory(NewsroomDirectory);
    private static readonly NewsDesks Desks = NewsDesks.Load(NewsroomDirectory);
    private static readonly NewsroomIdentity Identity = new()
    {
        PlayerName = "A. Tester",
        PlayerTeamName = "Test Racing",
    };

    [Fact]
    public void TheShippedPacksParseValidateAndCoverEveryCommonTrigger()
    {
        Assert.False(Corpus.IsEmpty);
        Corpus.Validate();
        Assert.True(Desks.All.Count >= 4, "expected a real desk roster");

        var covered = Corpus.Templates.Select(t => t.Event).ToHashSet(StringComparer.Ordinal);
        string[] common =
        [
            "raceWon", "podiumFinish", "pointsFinish", "midfieldResult", "retiredMechanical",
            "retiredDriverError", "firstWin", "firstPodium", "firstPoints", "firstStart",
            "polePosition", "championshipLeadTaken", "championshipLeadLost", "championCrowned",
            "titleClinchedEarly", "seasonStarted", "seasonCompleted", "careerCreated",
            "playerInjured", "playerDied", "aiWinStreak", "upsetWinner", "winStreak",
        ];
        foreach (var kind in common)
        {
            Assert.Contains(kind, covered);
        }
    }

    [Fact]
    public void EveryShippedTemplateRendersWithFullAndMinimalFactsWithoutSeams()
    {
        // Full = every token carries a value; minimal = the composer's real empty vocabulary.
        var full = NewsroomComposer.BuildTokens(RichEvent(), Identity)
            .ToDictionary(kv => kv.Key, kv => kv.Value.Length > 0 ? kv.Value : "x", StringComparer.Ordinal);
        var minimal = NewsroomComposer.BuildTokens(MinimalEvent(), new NewsroomIdentity());

        foreach (var template in Corpus.Templates)
        {
            foreach (var eraKey in new[] { "1960s", "1980s", "2010s", "smgp", NewsroomCorpus.DefaultEra })
            {
                foreach (var tokens in new[] { full, (IReadOnlyDictionary<string, string>)minimal })
                {
                    var stream = new Companion.Core.Determinism.StreamFactory(7UL)
                        .CreateStream("content-test", 1988, 1, template.Id);
                    IReadOnlyList<string>? Pools(string name) => Corpus.Pool(name, eraKey);

                    var pieces = new List<string>
                    {
                        NewsroomGrammar.Expand(template.Headline, tokens, Pools, stream),
                    };
                    if (template.Deck.Length > 0)
                    {
                        pieces.Add(NewsroomGrammar.Expand(template.Deck, tokens, Pools, stream));
                    }
                    if (template.Summary.Length > 0)
                    {
                        pieces.Add(NewsroomGrammar.Expand(template.Summary, tokens, Pools, stream));
                    }
                    foreach (var section in template.Sections.Values)
                    {
                        pieces.Add(NewsroomGrammar.Expand(section, tokens, Pools, stream));
                    }

                    foreach (var text in pieces)
                    {
                        Assert.DoesNotContain("{", text);
                        Assert.DoesNotContain("[[", text);
                        Assert.DoesNotContain("null", text, StringComparison.Ordinal);
                        Assert.DoesNotContain("  ", text);
                    }
                    Assert.True(pieces[0].Length > 0, $"template '{template.Id}' produced an empty headline");
                }
            }
        }
    }

    [Fact]
    public void TheMostSpecificSituationAlwaysWins()
    {
        // The invariant is TIER, not identity: as the library grows, the winning template must
        // always come from the most specific guard set the situation satisfies.
        NewsroomTemplate WinnerFor(NewsEvent e) =>
            Corpus.Templates.Single(t => t.Id == Compose(e)!.TemplateId);

        var plainWin = RichEvent();
        var firstWin = plainWin with { Facts = plainWin.Facts with { IsFirstEver = true } };
        var titleWin = plainWin with { Facts = plainWin.Facts with { ClinchedTitle = true } };
        var wetWin = plainWin with { Facts = plainWin.Facts with { IsWet = true } };

        Assert.Empty(WinnerFor(plainWin).When); // nothing special happened: the generic tier
        Assert.True(WinnerFor(firstWin).When.ContainsKey("isFirstEver"));
        Assert.True(WinnerFor(titleWin).When.ContainsKey("clinchedTitle"));
        Assert.True(WinnerFor(wetWin).When.ContainsKey("isWet"));
    }

    [Fact]
    public void ArticlesAreDeterministicAndCarryTheEventIdentity()
    {
        var e = RichEvent();
        var a = Compose(e)!;
        var b = Compose(e)!;

        Assert.Equal(a.Headline, b.Headline);
        Assert.Equal(a.Body, b.Body);
        Assert.Equal(a.DeskId, b.DeskId);
        Assert.Equal(e.DedupeKey, a.Key);
        Assert.Equal(NewsroomCategory.RaceReport, a.Category);
        Assert.Equal(EditorialStatus.Confirmed, a.Status);
        Assert.Equal(ContentProvenance.CareerUniverse, a.Provenance);
        Assert.True(a.ReadingSeconds >= 15);
        Assert.Contains("A. Tester", a.Headline + a.Body);
    }

    [Fact]
    public void DesksPreferTheirCategoriesAndStayStable()
    {
        var race = Compose(RichEvent())!;
        Assert.Equal("wire", race.DeskId); // raceReport is the wire's beat

        var title = Compose(RichEvent() with { Kind = NewsEventKind.ChampionshipLeadTaken })!;
        Assert.Equal("titlewatch", title.DeskId);

        var market = Compose(RichEvent() with
        {
            Kind = NewsEventKind.OfferReceived,
            SubjectName = "Big Team",
            Round = CareerNewsEvents.SeasonEndRound,
        })!;
        Assert.Equal("whispers", market.DeskId);
    }

    [Fact]
    public void GrowingTheCorpusOnlyRepicksWhatTheNewcomerWins()
    {
        var extraJson = """
        {
          "version": 1,
          "templates": [
            { "id": "race-won.zz-newcomer", "event": "raceWon",
              "headline": "{subject} triumphant", "sections": { "lead": "{subject} won." } }
          ]
        }
        """;
        var grown = MergeForTest(Corpus, NewsroomCorpus.Parse(extraJson));

        var changed = 0;
        var total = 0;
        for (var round = 1; round <= 40; round++)
        {
            var e = RichEvent() with { Round = round };
            var before = Corpus.Select(e, "1980s", "wire", 99UL)!.Id;
            var after = grown.Select(e, "1980s", "wire", 99UL)!.Id;
            total++;
            if (before != after)
            {
                changed++;
                Assert.Equal("race-won.zz-newcomer", after); // a change can only be a newcomer win
            }
        }
        Assert.True(changed < total, "the newcomer must not steal every pick");
    }

    private static NewsroomArticle? Compose(NewsEvent e) =>
        NewsroomComposer.Compose(e, Corpus, Desks, Identity, masterSeed: 99UL);

    private static NewsEvent RichEvent() => new()
    {
        Kind = NewsEventKind.RaceWon,
        SeasonOrdinal = 1,
        SeasonYear = 1988,
        Round = 5,
        SubjectId = "player",
        SubjectTeamId = "team.test",
        SubjectTeamName = "Test Racing",
        VenueName = "Monza",
        Facts = new NewsEventFacts
        {
            PlayerFinish = 1,
            ExpectedFinish = 4,
            ChampionshipPosition = 2,
            QualifyingPosition = 3,
            PointsGapToLeader = 6,
        },
    };

    private static NewsEvent MinimalEvent() => new()
    {
        Kind = NewsEventKind.SeasonStarted,
        SeasonOrdinal = 1,
        SeasonYear = 1988,
        Round = 0,
        SubjectId = "player",
    };

    private static NewsroomCorpus MergeForTest(NewsroomCorpus a, NewsroomCorpus b) => new()
    {
        Eras = a.Eras,
        Templates = [.. a.Templates, .. b.Templates],
        Pools = a.Pools,
    };
}
