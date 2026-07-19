using System.Text.RegularExpressions;
using Companion.Core.Smgp;

namespace Companion.Tests.Smgp;

/// <summary>
/// The SMGP-universe engine dossiers (<c>data/rules/smgp/engine-dossiers.json</c>): each
/// permanent engine specification's tagline, naming note, character paragraph, 3-paragraph
/// history and quotes, keyed by canon engine id. DISPLAY-ONLY (never a fold input, like
/// team-profiles + rival quotes). These pin the loader (parse, engine lookup, empty/absent =
/// omitted) and, the important guard, that the SHIPPED catalog authors every one of the 17
/// canon specifications (SMGP-024) with full content and exact canon names, that the shared
/// engines (LIZZIE 24 V8, VAPOR DNPQ V8, LORRY 32 V8, RAM V12) map to the right canon team
/// sets, and that VAPOR DN's architecture is never stated. The shipped file is validated
/// directly from the repo tree (the RepoRoot walk used by
/// <see cref="SmgpWorldCompletenessTests"/>), so drift fails here loudly.
/// </summary>
public sealed class SmgpEngineDossiersTests
{
    private const string Json = """
    {
      "$comment": "test fixture",
      "engines": {
        "palm-190-v10": {
          "name": "PALM 190 V10",
          "tagline": "THE ORDER OF THINGS",
          "naming": "The Palm Court Works, pattern 190.",
          "character": "Silken, wide powerband, bulletproof.",
          "history": ["Carriage-era atelier.", "Six crowns.", "One pattern, seventeen seasons."],
          "quotes": ["Questions not asked.", "Exactly as good as they say."]
        }
      }
    }
    """;

    private static readonly SmgpEngineDossiers Catalog = SmgpEngineDossiers.Parse(Json);

    /// <summary>Placeholder/unfinished-content tokens that must never reach the shipped lore
    /// (the dossier surfaces render the fields verbatim, so a stray token is a user-visible bug).</summary>
    private static readonly string[] PlaceholderTokens =
        ["TBD", "TODO", "FIXME", "XXX", "LOREM", "PLACEHOLDER", "{playerTeam}", "???"];

    /// <summary>An architecture assignment ("V8", "V 10", "V12"). In the VAPOR DN dossier this
    /// may never survive stripping the sister specification's name (the DNPQ V8), because the
    /// DN's architecture is deliberately unpublished in-world.</summary>
    private static readonly Regex ArchitectureMention =
        new(@"\bV\s?(6|8|10|12)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    [Fact]
    public void An_authored_engine_resolves_its_dossier()
    {
        var dossier = Catalog.ForEngine("palm-190-v10");
        Assert.NotNull(dossier);
        Assert.Equal("PALM 190 V10", dossier!.Name);
        Assert.Equal("THE ORDER OF THINGS", dossier.Tagline);
        Assert.False(string.IsNullOrWhiteSpace(dossier.Naming));
        Assert.False(string.IsNullOrWhiteSpace(dossier.Character));
        Assert.Equal(3, dossier.History.Count);
        Assert.Equal(2, dossier.Quotes.Count);
    }

    [Fact]
    public void An_unauthored_engine_and_the_empty_catalog_resolve_to_null()
    {
        Assert.Null(Catalog.ForEngine("unknown-00-x0"));
        Assert.Null(SmgpEngineDossiers.Empty.ForEngine("palm-190-v10"));
        Assert.Empty(SmgpEngineDossiers.Empty.Engines);
    }

    [Fact]
    public void A_missing_file_loads_the_empty_catalog()
    {
        string missing = Path.Combine(AppContext.BaseDirectory, "Fixtures", "no-such-rules-dir");
        Assert.Same(SmgpEngineDossiers.Empty, SmgpEngineDossiers.Load(missing));
    }

    [Fact]
    public void The_shipped_catalog_authors_every_canon_engine_with_full_content()
    {
        var canon = LoadShippedCanon();
        var dossiers = LoadShippedDossiers();

        Assert.Equal(canon.Engines.Count, dossiers.Engines.Count);
        foreach (var engine in canon.Engines)
        {
            var dossier = dossiers.ForEngine(engine.Id);
            Assert.True(dossier is not null, $"engine-dossiers.json has no dossier for '{engine.Id}' ({engine.DisplayName}).");
            Assert.False(string.IsNullOrWhiteSpace(dossier!.Tagline), $"{engine.Id}: empty tagline.");
            Assert.False(string.IsNullOrWhiteSpace(dossier.Naming), $"{engine.Id}: empty naming note.");
            Assert.False(string.IsNullOrWhiteSpace(dossier.Character), $"{engine.Id}: empty character paragraph.");
            Assert.True(dossier.History.Count == 3,
                $"{engine.Id}: history has {dossier.History.Count} paragraphs (expected 3).");
            Assert.All(dossier.History, p =>
                Assert.False(string.IsNullOrWhiteSpace(p), $"{engine.Id}: blank history paragraph."));
            Assert.True(dossier.Quotes.Count == 2,
                $"{engine.Id}: quotes has {dossier.Quotes.Count} entries (expected 2).");
            Assert.All(dossier.Quotes, q =>
                Assert.False(string.IsNullOrWhiteSpace(q), $"{engine.Id}: blank quote."));
        }
    }

    [Fact]
    public void The_shipped_catalog_matches_canon_names_and_team_linkage_exactly()
    {
        var canon = LoadShippedCanon();
        var dossiers = LoadShippedDossiers();

        // Every dossier name echoes the canon display name exactly; no orphan dossiers.
        var canonEngineIds = canon.Engines.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        foreach (string engineId in dossiers.Engines)
        {
            Assert.True(canonEngineIds.Contains(engineId),
                $"engine-dossiers.json has an orphan dossier '{engineId}' (no such canon engine).");
            Assert.Equal(
                canon.ForEngine(engineId)!.DisplayName,
                dossiers.ForEngine(engineId)!.Name);
        }

        // Every canon team's engine resolves to the single dossier carrying its canon name.
        foreach (var team in canon.Teams)
        {
            var dossier = dossiers.ForEngine(team.EngineId);
            Assert.True(dossier is not null,
                $"{team.Id}: engine '{team.EngineId}' has no dossier.");
            Assert.Equal(team.EngineDisplayName, dossier!.Name);
        }

        // The shared engines stay ONE dossier serving the exact canon team sets.
        var teamSets = canon.Teams
            .GroupBy(t => t.EngineId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(t => t.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        Assert.Equal(["team.bullets", "team.comet", "team.tyrant"], teamSets["lizzie-24-v8"]);
        Assert.Equal(["team.dardan", "team.losel"], teamSets["vapor-dnpq-v8"]);
        Assert.Equal(["team.linden", "team.may", "team.rigel", "team.zeroforce"], teamSets["lorry-32-v8"]);
        Assert.Equal(["team.lares", "team.moon"], teamSets["ram-v12"]);
        // VAPOR DN and VAPOR DNPQ V8 are distinct official specifications, one dossier each.
        Assert.NotNull(dossiers.ForEngine("vapor-dn"));
        Assert.NotNull(dossiers.ForEngine("vapor-dnpq-v8"));
        Assert.Single(canon.Teams, t => t.EngineId == "vapor-dn");
    }

    [Fact]
    public void The_shipped_vapor_dn_dossier_never_states_an_architecture()
    {
        var dossier = LoadShippedDossiers().ForEngine("vapor-dn");
        Assert.NotNull(dossier);
        foreach (string field in AllText(dossier!))
        {
            // The sister specification's own name is allowed (a manufacturer relationship may
            // be acknowledged); once it is stripped, no architecture assignment may remain.
            string stripped = field
                .Replace("VAPOR DNPQ V8", "", StringComparison.Ordinal)
                .Replace("DNPQ V8", "", StringComparison.Ordinal);
            Assert.False(ArchitectureMention.IsMatch(stripped),
                $"vapor-dn dossier states or implies an architecture: {field}");
            // Cylinder talk is only ever allowed as part of the non-disclosure stance itself.
            if (field.Contains("cylinder", StringComparison.OrdinalIgnoreCase))
                Assert.True(
                    field.Contains("no architecture", StringComparison.OrdinalIgnoreCase),
                    $"vapor-dn dossier mentions cylinders outside the non-disclosure stance: {field}");
        }
    }

    [Fact]
    public void The_shipped_catalog_carries_no_placeholder_tokens()
    {
        var dossiers = LoadShippedDossiers();
        foreach (string engineId in dossiers.Engines)
        {
            var dossier = dossiers.ForEngine(engineId)!;
            foreach (string field in AllText(dossier))
                foreach (string token in PlaceholderTokens)
                    Assert.False(
                        field.Contains(token, StringComparison.OrdinalIgnoreCase),
                        $"{engineId}: placeholder token '{token}' in dossier text.");
        }
    }

    private static IEnumerable<string> AllText(SmgpEngineDossier dossier) =>
        new[] { dossier.Name, dossier.Tagline, dossier.Naming, dossier.Character }
            .Concat(dossier.History)
            .Concat(dossier.Quotes);

    private static SmgpCanon LoadShippedCanon()
    {
        string path = Path.Combine(RepoRoot(), "data", "rules", "smgp", "canon.json");
        Assert.True(File.Exists(path), $"canon.json missing at {path}");
        return SmgpCanon.Parse(File.ReadAllText(path));
    }

    private static SmgpEngineDossiers LoadShippedDossiers()
    {
        string rulesDir = Path.Combine(RepoRoot(), "data", "rules");
        var dossiers = SmgpEngineDossiers.Load(rulesDir);
        Assert.False(dossiers.Engines.Count == 0, "engine-dossiers.json missing or empty under data/rules/smgp.");
        return dossiers;
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
