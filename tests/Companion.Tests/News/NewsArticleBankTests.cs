using Companion.Core.Career;
using Companion.Core.Determinism;
using Companion.Core.News;

namespace Companion.Tests.News;

/// <summary>
/// Unit coverage for the generative news-article grammar (<see cref="NewsArticleBank"/>):
/// parse validation, deterministic seeded slot-fill, phrase-pool composition, and the shipped
/// 1960s corpus loaded through the same directory loader the app uses.
/// </summary>
public sealed class NewsArticleBankTests
{
    private const ulong Seed = 0xC0FFEE;

    private static NewsFacts SampleWin() => new()
    {
        Phase = "race.result",
        Cause = "win",
        Year = 1967,
        Round = 1,
        RaceName = "South African Grand Prix",
        PlayerName = "D. Hulme",
        TeamName = "Brabham-Repco",
        PlayerFinish = 1,
        ExpectedFinish = 3,
        WinnerName = "D. Hulme",
        FieldSize = 18,
        ChampionshipPosition = 1,
        ChampionshipDelta = 2,
        ChampionshipLeaderName = "D. Hulme",
        PlayerLeadsChampionship = true,
    };

    // ---------- the shipped 1960s corpus ----------

    private static string ShippedNewsDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "rules", "news");

    [Fact]
    public void ShippedCorpus_LoadsAndCoversEveryRaceCause()
    {
        var bank = NewsArticleBank.LoadDirectory(ShippedNewsDirectory);

        string[] causes =
        [
            "win", "podium", "points", "overperformed",
            "underperformed", "dnf-mechanical", "dnf-driver-error", "midfield",
        ];
        foreach (string cause in causes)
        {
            var templates = bank.Templates("race.result", cause, 1967);
            Assert.True(templates.Count >= 1, $"1960s corpus missing bodies for race.result|{cause}.");
        }
    }

    [Fact]
    public void ShippedCorpus_FillsEverySlot_ForASampleRace()
    {
        var bank = NewsArticleBank.LoadDirectory(ShippedNewsDirectory);
        var stream = new StreamFactory(Seed).CreateStream(CareerStreams.Headlines, 1967, 1, "race");

        string? body = bank.BuildBody(SampleWin(), stream);

        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body));
        // Facts are woven in, and no unresolved template/pool token survives.
        Assert.Contains("South African Grand Prix", body);
        Assert.Contains("Brabham-Repco", body);
        Assert.DoesNotContain("{", body);
        Assert.DoesNotContain("}", body);
    }

    [Fact]
    public void ShippedCorpus_IsByteIdentical_AcrossRepeatedBuilds()
    {
        var bank = NewsArticleBank.LoadDirectory(ShippedNewsDirectory);
        var facts = SampleWin();

        string? first = bank.BuildBody(
            facts, new StreamFactory(Seed).CreateStream(CareerStreams.Headlines, 1967, 1, "race"));
        string? second = bank.BuildBody(
            facts, new StreamFactory(Seed).CreateStream(CareerStreams.Headlines, 1967, 1, "race"));

        Assert.Equal(first, second);
    }

    [Fact]
    public void Composer_MatchesTheDirectStreamConstruction()
    {
        var bank = NewsArticleBank.LoadDirectory(ShippedNewsDirectory);
        var facts = SampleWin();

        string? viaComposer = NewsArticleComposer.Compose(bank, facts, Seed);
        string? viaStream = bank.BuildBody(
            facts, new StreamFactory(Seed).CreateStream(CareerStreams.Headlines, facts.Year, facts.Round, "race"));

        Assert.Equal(viaStream, viaComposer);
    }

    [Fact]
    public void DifferentSeeds_CanProduceDifferentBodies()
    {
        var bank = NewsArticleBank.LoadDirectory(ShippedNewsDirectory);
        var facts = SampleWin();

        // Across a spread of seeds the grammar must not collapse to a single body — the whole
        // point of the generative corpus. (Any two differing is enough to prove variety.)
        var bodies = new HashSet<string>(StringComparer.Ordinal);
        for (ulong seed = 1; seed <= 12; seed++)
            bodies.Add(NewsArticleComposer.Compose(bank, facts, seed) ?? "");

        Assert.True(bodies.Count > 1, "The corpus produced identical bodies for every seed.");
    }

    [Fact]
    public void UnknownCause_YieldsNoBody()
    {
        var bank = NewsArticleBank.LoadDirectory(ShippedNewsDirectory);
        var facts = SampleWin() with { Cause = "no-such-cause" };

        Assert.Null(NewsArticleComposer.Compose(bank, facts, Seed));
    }

    // ---------- parse + expansion validation (small in-memory corpora) ----------

    [Fact]
    public void Parse_RejectsABodyKeyWithoutAPipe()
    {
        const string json = """
        { "eras": [], "bodies": { "raceresult": { "default": ["x"] } } }
        """;
        Assert.Throws<System.Text.Json.JsonException>(() => NewsArticleBank.Parse(json));
    }

    [Fact]
    public void Parse_RejectsAnUndeclaredEra()
    {
        const string json = """
        { "eras": [], "bodies": { "race.result|win": { "1990s": ["x"] } } }
        """;
        Assert.Throws<System.Text.Json.JsonException>(() => NewsArticleBank.Parse(json));
    }

    [Fact]
    public void BuildBody_ThrowsOnAnUnknownToken()
    {
        var bank = NewsArticleBank.Parse("""
        { "eras": [], "bodies": { "race.result|win": { "default": ["{player} did {mystery}"] } } }
        """);
        var stream = new StreamFactory(Seed).CreateStream(CareerStreams.Headlines, 2000, 1, "race");

        Assert.Throws<InvalidOperationException>(() =>
            bank.BuildBody(SampleWin() with { Year = 2000 }, stream));
    }

    [Fact]
    public void BuildBody_ThrowsOnAnUndeclaredPool()
    {
        var bank = NewsArticleBank.Parse("""
        { "eras": [], "bodies": { "race.result|win": { "default": ["{player} {pool:ghost}"] } } }
        """);
        var stream = new StreamFactory(Seed).CreateStream(CareerStreams.Headlines, 2000, 1, "race");

        Assert.Throws<InvalidOperationException>(() =>
            bank.BuildBody(SampleWin() with { Year = 2000 }, stream));
    }

    [Fact]
    public void BuildBody_ExpandsAPoolFragment()
    {
        var bank = NewsArticleBank.Parse("""
        {
          "eras": [],
          "pools": { "tail": { "default": ["ONLY-FRAGMENT"] } },
          "bodies": { "race.result|win": { "default": ["{player}. {pool:tail}"] } }
        }
        """);
        var stream = new StreamFactory(Seed).CreateStream(CareerStreams.Headlines, 2000, 1, "race");

        string? body = bank.BuildBody(SampleWin() with { Year = 2000 }, stream);

        Assert.NotNull(body);
        Assert.Contains("ONLY-FRAGMENT", body);
    }

    [Fact]
    public void MissingFacts_DegradeToNeutralPhrases_NotRawTokens()
    {
        var bank = NewsArticleBank.Parse("""
        { "eras": [], "bodies": { "race.result|win": { "default": ["{player} at {race}, {position}, champ {champPosition}"] } } }
        """);
        var stream = new StreamFactory(Seed).CreateStream(CareerStreams.Headlines, 2000, 1, "race");

        // A near-empty facts row: no names, no finish, no championship position.
        string? body = bank.BuildBody(
            new NewsFacts { Phase = "race.result", Cause = "win", Year = 2000, Round = 1 },
            stream);

        Assert.NotNull(body);
        Assert.DoesNotContain("{", body);
        Assert.DoesNotContain("}", body);
    }
}
