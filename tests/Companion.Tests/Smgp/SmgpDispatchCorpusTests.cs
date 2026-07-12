using System.Text.Json;
using System.Text.RegularExpressions;
using Companion.Core.Determinism;
using Companion.Core.Smgp;
using Companion.Tests.Career;

namespace Companion.Tests.Smgp;

/// <summary>
/// The SMGP dispatch corpus (Task 4): the templated in-world news renderer. These cover the pure grammar
/// (deterministic template/pool selection, fallback, the rumor pool) AND a consistency guard over the SHIPPED
/// <c>data/rules/smgp/dispatches.json</c> — every template renders without throwing (no undeclared pool) and
/// only ever names known tokens/pools, so the lenient runtime renderer never quietly drops a real token.
/// </summary>
public sealed class SmgpDispatchCorpusTests
{
    /// <summary>The token vocabulary the ViewModels supply — a shipped template may name only these.</summary>
    private static readonly HashSet<string> KnownTokens = new(StringComparer.Ordinal)
    {
        "player", "team", "rival", "venue", "season", "number", "subject", "other", "leader", "benchmark",
    };

    private static readonly Regex Token = new(@"\{([^{}]+)\}", RegexOptions.Compiled);

    private static Pcg32 Stream(int round = 1, string entity = "k") =>
        new StreamFactory(12345).CreateStream("smgp-dispatch", 1, round, entity);

    private static IReadOnlyDictionary<string, string> Tokens(params (string Key, string Value)[] pairs)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (k, v) in pairs)
            d[k] = v;
        return d;
    }

    [Fact]
    public void Render_expands_tokens_and_pools_deterministically()
    {
        var corpus = SmgpDispatchCorpus.Parse("""
        {
          "pools": { "close": ["nice.", "great."] },
          "templates": { "m.win": ["{player} wins at {venue}. {pool:close}"] }
        }
        """);

        string a = corpus.Render("m.win", Tokens(("player", "Ryan"), ("venue", "Monaco")), Stream(), "fb");
        string b = corpus.Render("m.win", Tokens(("player", "Ryan"), ("venue", "Monaco")), Stream(), "fb");

        Assert.Equal(a, b);                              // same seed -> same body
        Assert.StartsWith("Ryan wins at Monaco.", a);
        Assert.Matches("(nice|great)\\.$", a);           // the pool expanded to one of its fragments
    }

    [Fact]
    public void Render_returns_the_fallback_when_the_key_is_absent()
    {
        var corpus = SmgpDispatchCorpus.Parse("""{ "templates": { "known": ["x"] } }""");
        Assert.Equal("the milestone speaks for itself", corpus.Render("missing", Tokens(), Stream(), "the milestone speaks for itself"));
    }

    [Fact]
    public void An_unknown_token_renders_empty_rather_than_crashing()
    {
        var corpus = SmgpDispatchCorpus.Parse("""{ "templates": { "t": ["a {mystery} b"] } }""");
        Assert.Equal("a  b", corpus.Render("t", Tokens(), Stream(), "fb"));
    }

    [Fact]
    public void An_undeclared_pool_reference_throws_so_a_corpus_bug_surfaces()
    {
        var corpus = SmgpDispatchCorpus.Parse("""{ "templates": { "t": ["a {pool:nope} b"] } }""");
        Assert.Throws<InvalidOperationException>(() => corpus.Render("t", Tokens(), Stream(), "fb"));
    }

    [Fact]
    public void Rumor_draws_from_the_rumor_pool_and_is_empty_without_one()
    {
        var with = SmgpDispatchCorpus.Parse("""{ "pools": { "rumor": ["word is {benchmark} is untouchable."] }, "templates": {} }""");
        Assert.Equal("word is Senna is untouchable.", with.Rumor(Tokens(("benchmark", "Senna")), Stream()));

        var without = SmgpDispatchCorpus.Parse("""{ "templates": { "t": ["x"] } }""");
        Assert.Equal("", without.Rumor(Tokens(), Stream()));
    }

    [Fact]
    public void Empty_corpus_always_returns_the_fallback()
    {
        Assert.Equal("fb", SmgpDispatchCorpus.Empty.Render("anything", Tokens(), Stream(), "fb"));
        Assert.Equal("", SmgpDispatchCorpus.Empty.Rumor(Tokens(), Stream()));
    }

    // ---------- the SHIPPED corpus ----------

    private static string DispatchesPath =>
        Path.Combine(CareerTestData.RulesDirectory, "smgp", "dispatches.json");

    [Fact]
    public void The_shipped_corpus_loads_with_templates_and_a_rumor_pool()
    {
        var corpus = SmgpDispatchCorpus.Load(CareerTestData.RulesDirectory);
        Assert.NotEmpty(corpus.TemplateKeys);
        Assert.Contains("rumor", corpus.PoolNames);
        // The full set the ViewModels emit must all be present so no dispatch silently falls back.
        foreach (string key in new[]
        {
            "milestone.arrived", "milestone.first-win", "milestone.promotion", "milestone.title",
            "milestone.rivalry-won", "milestone.finale", "milestone.season", "setback.demotion",
            "setback.rivalry-lost", "setback.near-miss", "setback.career-over",
            "setback.injured", "setback.season-ending-injury", "setback.died", "world.rival-streak",
            "world.benchmark", "world.leader-change", "world.title-tightens", "world.standings-move",
        })
            Assert.Contains(key, corpus.TemplateKeys);
    }

    [Fact]
    public void Every_shipped_template_renders_without_throwing_and_is_non_empty()
    {
        var corpus = SmgpDispatchCorpus.Load(CareerTestData.RulesDirectory);
        var tokens = Tokens(
            ("player", "Ryan Cotman"), ("team", "Madonna"), ("rival", "G. Ceara"), ("venue", "Monaco"),
            ("season", "3"), ("number", "2"), ("subject", "A. Senna"), ("other", "F. Elssler"),
            ("leader", "A. Senna"), ("benchmark", "A. Senna"));

        foreach (string key in corpus.TemplateKeys)
        {
            // Several seeds so different template variants + pool picks are all exercised.
            for (int r = 0; r < 8; r++)
            {
                string body = corpus.Render(key, tokens, Stream(r, key), "fallback");
                Assert.False(string.IsNullOrWhiteSpace(body), $"template '{key}' rendered empty");
            }
        }
    }

    [Fact]
    public void The_shipped_corpus_only_names_known_tokens_and_declared_pools()
    {
        var corpus = SmgpDispatchCorpus.Load(CareerTestData.RulesDirectory);
        var declaredPools = new HashSet<string>(corpus.PoolNames, StringComparer.Ordinal);

        using var doc = JsonDocument.Parse(File.ReadAllText(DispatchesPath));
        var root = doc.RootElement;

        foreach (string section in new[] { "templates", "pools" })
        {
            if (!root.TryGetProperty(section, out var obj))
                continue;
            foreach (var group in obj.EnumerateObject())
                foreach (var fragment in group.Value.EnumerateArray())
                    foreach (Match m in Token.Matches(fragment.GetString() ?? ""))
                    {
                        string tok = m.Groups[1].Value;
                        if (tok.StartsWith("pool:", StringComparison.Ordinal))
                            Assert.Contains(tok["pool:".Length..], declaredPools);
                        else
                            Assert.Contains(tok, KnownTokens);
                    }
        }
    }
}
