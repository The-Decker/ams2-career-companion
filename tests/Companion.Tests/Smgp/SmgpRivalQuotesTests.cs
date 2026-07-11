using Companion.Core.Smgp;

namespace Companion.Tests.Smgp;

/// <summary>
/// The rival dossier line is dynamic per DRIVER and per ladder MOOD (Mike: "every character will
/// say something different ... depending on if you first challenged them or beat them once"). These
/// pin the data-driven selection: the right mood's pool, per-driver override, shared fallback, the
/// deadpan default when nothing is authored, and a stable per-seed pick.
/// </summary>
public sealed class SmgpRivalQuotesTests
{
    private const string Json = """
    {
      "fallback": {
        "first": ["FALLBACK FIRST A", "FALLBACK FIRST B"],
        "playerLeads": ["FALLBACK LEAD"],
        "rivalLeads": ["FALLBACK RIVAL"]
      },
      "drivers": {
        "driver.ceara": {
          "first": ["I AM CEARA. KNEEL.", "THE HAIRPIN IS MINE."],
          "playerLeads": ["ONE WIN? LUCK RUNS OUT."],
          "rivalLeads": ["YOUR SEAT IS ALREADY MINE."]
        },
        "driver.partial": {
          "first": ["ONLY A FIRST LINE."],
          "playerLeads": [],
          "rivalLeads": []
        }
      }
    }
    """;

    private static readonly SmgpRivalQuotes Quotes = SmgpRivalQuotes.Parse(Json);

    [Theory]
    [InlineData(SmgpRivalMood.First, "I AM CEARA. KNEEL.", "THE HAIRPIN IS MINE.")]
    [InlineData(SmgpRivalMood.PlayerLeads, "ONE WIN? LUCK RUNS OUT.")]
    [InlineData(SmgpRivalMood.RivalLeads, "YOUR SEAT IS ALREADY MINE.")]
    public void ADriversLine_ComesFromHisOwnMoodPool(SmgpRivalMood mood, params string[] allowed)
    {
        for (uint seed = 0; seed < 20; seed++)
            Assert.Contains(Quotes.Line("driver.ceara", mood, seed), allowed);
    }

    [Fact]
    public void AnUnknownDriver_FallsBackToTheSharedPool()
    {
        Assert.Contains(Quotes.Line("driver.nobody", SmgpRivalMood.First, 0),
            new[] { "FALLBACK FIRST A", "FALLBACK FIRST B" });
        Assert.Equal("FALLBACK LEAD", Quotes.Line("driver.nobody", SmgpRivalMood.PlayerLeads, 7));
        Assert.Equal("FALLBACK RIVAL", Quotes.Line("driver.nobody", SmgpRivalMood.RivalLeads, 3));
    }

    [Fact]
    public void APartiallyAuthoredDriver_BackfillsTheMissingMoodFromFallback()
    {
        // Authored only 'first'; playerLeads/rivalLeads empty → the shared fallback fills in.
        Assert.Equal("ONLY A FIRST LINE.", Quotes.Line("driver.partial", SmgpRivalMood.First, 0));
        Assert.Equal("FALLBACK LEAD", Quotes.Line("driver.partial", SmgpRivalMood.PlayerLeads, 0));
        Assert.Equal("FALLBACK RIVAL", Quotes.Line("driver.partial", SmgpRivalMood.RivalLeads, 0));
    }

    [Fact]
    public void AnEmptyBank_AlwaysReturnsTheDeadpanDefault()
    {
        Assert.Equal(SmgpRivalQuotes.Default, SmgpRivalQuotes.Empty.Line("driver.x", SmgpRivalMood.First, 0));
        Assert.Equal(SmgpRivalQuotes.Default, SmgpRivalQuotes.Empty.Line("driver.x", SmgpRivalMood.RivalLeads, 99));
    }

    [Fact]
    public void TheSameSeed_PicksTheSameLine_AndTheSeedSelectsWithinThePool()
    {
        // Deterministic per seed (a re-opened briefing shows the same line).
        Assert.Equal(Quotes.Line("driver.ceara", SmgpRivalMood.First, 4),
                     Quotes.Line("driver.ceara", SmgpRivalMood.First, 4));
        // seed 0 and seed 1 hit the two different 'first' lines (pool size 2).
        Assert.Equal("I AM CEARA. KNEEL.", Quotes.Line("driver.ceara", SmgpRivalMood.First, 0));
        Assert.Equal("THE HAIRPIN IS MINE.", Quotes.Line("driver.ceara", SmgpRivalMood.First, 1));
    }

    /// <summary>The SHIPPED corpus loads and every driver in the SMGP field has his own dossier
    /// voice — a new field car with no authored lines would only fall back to the generic pool,
    /// which reads as a bug (the design intends a distinct line per character).</summary>
    [Fact]
    public void TheShippedCorpus_GivesEverySmgpFieldDriver_AVoice()
    {
        string rulesDir = Path.Combine(AppContext.BaseDirectory, "Fixtures", "rules");
        string quotesPath = Path.Combine(rulesDir, "smgp", "rival-quotes.json");
        Assert.True(File.Exists(quotesPath),
            $"'{quotesPath}' missing — check the smgp None-Include in Companion.Tests.csproj.");

        var authored = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(quotesPath))!["drivers"]!
            .AsObject().Select(kv => kv.Key).ToHashSet(StringComparer.Ordinal);

        string packDir = Path.Combine(AppContext.BaseDirectory, "packs", "smgp-1");
        var pack = Companion.Core.Packs.PackLoader.Parse(
            File.ReadAllText(Path.Combine(packDir, "pack.json")),
            File.ReadAllText(Path.Combine(packDir, "season.json")),
            File.ReadAllText(Path.Combine(packDir, "teams.json")),
            File.ReadAllText(Path.Combine(packDir, "drivers.json")),
            File.ReadAllText(Path.Combine(packDir, "entries.json")));

        foreach (var entry in pack.Entries)
            Assert.True(authored.Contains(entry.DriverId),
                $"SMGP field driver '{entry.DriverId}' has no authored rival quotes.");

        // …and the file loads through the real parser and resolves a non-empty line for each.
        var quotes = SmgpRivalQuotes.Load(rulesDir);
        foreach (var entry in pack.Entries)
            foreach (var mood in new[] { SmgpRivalMood.First, SmgpRivalMood.PlayerLeads, SmgpRivalMood.RivalLeads })
                Assert.False(string.IsNullOrWhiteSpace(quotes.Line(entry.DriverId, mood, 0)));
    }
}
