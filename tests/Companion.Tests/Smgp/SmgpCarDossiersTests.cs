using Companion.Core.Smgp;

namespace Companion.Tests.Smgp;

/// <summary>
/// The SMGP-universe car dossiers (<c>data/rules/smgp/car-dossiers.json</c>): each permanent
/// car's tagline, naming note, character paragraph, 3-paragraph history and quotes, keyed by
/// canon car id. DISPLAY-ONLY (never a fold input, like team-profiles + rival quotes). These
/// pin the loader (parse, car lookup, empty/absent = omitted) and, the important guard, that
/// the SHIPPED catalog authors every one of the 24 canon cars (SMGP-024) with full content,
/// exact canon names, and an exact team linkage, so a dossier surface always has the machine's
/// story. The shipped file is validated directly from the repo tree (the RepoRoot walk used by
/// <see cref="SmgpWorldCompletenessTests"/>), so a broken or drifted dossier fails here loudly.
/// </summary>
public sealed class SmgpCarDossiersTests
{
    private const string Json = """
    {
      "$comment": "test fixture",
      "cars": {
        "madonna-456": {
          "name": "MADONNA 456",
          "team": "team.madonna",
          "tagline": "THE CROWN IN METAL",
          "naming": "Program 456 in the founding ledger.",
          "character": "The reference the whole grid is measured against.",
          "history": ["Born with the house.", "Six crowns, one interruption.", "One name, every generation."],
          "quotes": ["We prepare the 456.", "You merely postpone it."]
        }
      }
    }
    """;

    private static readonly SmgpCarDossiers Catalog = SmgpCarDossiers.Parse(Json);

    /// <summary>Placeholder/unfinished-content tokens that must never reach the shipped lore
    /// (the dossier surfaces render the fields verbatim, so a stray token is a user-visible bug).</summary>
    private static readonly string[] PlaceholderTokens =
        ["TBD", "TODO", "FIXME", "XXX", "LOREM", "PLACEHOLDER", "{playerTeam}", "???"];

    [Fact]
    public void An_authored_car_resolves_its_dossier()
    {
        var dossier = Catalog.ForCar("madonna-456");
        Assert.NotNull(dossier);
        Assert.Equal("MADONNA 456", dossier!.Name);
        Assert.Equal("team.madonna", dossier.Team);
        Assert.Equal("THE CROWN IN METAL", dossier.Tagline);
        Assert.False(string.IsNullOrWhiteSpace(dossier.Naming));
        Assert.False(string.IsNullOrWhiteSpace(dossier.Character));
        Assert.Equal(3, dossier.History.Count);
        Assert.Equal(2, dossier.Quotes.Count);
    }

    [Fact]
    public void An_unauthored_car_and_the_empty_catalog_resolve_to_null()
    {
        Assert.Null(Catalog.ForCar("unknown-000"));
        Assert.Null(SmgpCarDossiers.Empty.ForCar("madonna-456"));
        Assert.Empty(SmgpCarDossiers.Empty.Cars);
    }

    [Fact]
    public void A_missing_file_loads_the_empty_catalog()
    {
        string missing = Path.Combine(AppContext.BaseDirectory, "Fixtures", "no-such-rules-dir");
        Assert.Same(SmgpCarDossiers.Empty, SmgpCarDossiers.Load(missing));
    }

    [Fact]
    public void The_shipped_catalog_authors_every_canon_car_with_full_content()
    {
        var canon = LoadShippedCanon();
        var dossiers = LoadShippedDossiers();

        Assert.Equal(canon.Teams.Count, dossiers.Cars.Count);
        foreach (var team in canon.Teams)
        {
            var dossier = dossiers.ForCar(team.CarId);
            Assert.True(dossier is not null, $"car-dossiers.json has no dossier for '{team.CarId}' ({team.CarDisplayName}).");
            Assert.False(string.IsNullOrWhiteSpace(dossier!.Tagline), $"{team.CarId}: empty tagline.");
            Assert.False(string.IsNullOrWhiteSpace(dossier.Naming), $"{team.CarId}: empty naming note.");
            Assert.False(string.IsNullOrWhiteSpace(dossier.Character), $"{team.CarId}: empty character paragraph.");
            Assert.True(dossier.History.Count == 3,
                $"{team.CarId}: history has {dossier.History.Count} paragraphs (expected 3).");
            Assert.All(dossier.History, p =>
                Assert.False(string.IsNullOrWhiteSpace(p), $"{team.CarId}: blank history paragraph."));
            Assert.True(dossier.Quotes.Count == 2,
                $"{team.CarId}: quotes has {dossier.Quotes.Count} entries (expected 2).");
            Assert.All(dossier.Quotes, q =>
                Assert.False(string.IsNullOrWhiteSpace(q), $"{team.CarId}: blank quote."));
        }
    }

    [Fact]
    public void The_shipped_catalog_matches_canon_names_and_team_linkage_exactly()
    {
        var canon = LoadShippedCanon();
        var dossiers = LoadShippedDossiers();

        // No orphan dossier that maps to no canon car id.
        var canonCarIds = canon.Teams.Select(t => t.CarId).ToHashSet(StringComparer.Ordinal);
        foreach (string carId in dossiers.Cars)
            Assert.True(canonCarIds.Contains(carId), $"car-dossiers.json has an orphan dossier '{carId}' (no such canon car).");

        // Every team in canon.json has EXACTLY ONE car dossier whose "team" field equals its id,
        // and it is the dossier keyed by that team's canon car id, named exactly as canon says.
        foreach (var team in canon.Teams)
        {
            string carId = Assert.Single(dossiers.Cars, id =>
                string.Equals(dossiers.ForCar(id)!.Team, team.Id, StringComparison.Ordinal));
            Assert.Equal(team.CarId, carId);
            Assert.Equal(team.CarDisplayName, dossiers.ForCar(carId)!.Name);
        }
    }

    [Fact]
    public void The_shipped_catalog_carries_no_placeholder_tokens()
    {
        var dossiers = LoadShippedDossiers();
        foreach (string carId in dossiers.Cars)
        {
            var dossier = dossiers.ForCar(carId)!;
            foreach (string field in AllText(dossier))
                foreach (string token in PlaceholderTokens)
                    Assert.False(
                        field.Contains(token, StringComparison.OrdinalIgnoreCase),
                        $"{carId}: placeholder token '{token}' in dossier text.");
        }
    }

    private static IEnumerable<string> AllText(SmgpCarDossier dossier) =>
        new[] { dossier.Name, dossier.Team, dossier.Tagline, dossier.Naming, dossier.Character }
            .Concat(dossier.History)
            .Concat(dossier.Quotes);

    private static SmgpCanon LoadShippedCanon()
    {
        string path = Path.Combine(RepoRoot(), "data", "rules", "smgp", "canon.json");
        Assert.True(File.Exists(path), $"canon.json missing at {path}");
        return SmgpCanon.Parse(File.ReadAllText(path));
    }

    private static SmgpCarDossiers LoadShippedDossiers()
    {
        string rulesDir = Path.Combine(RepoRoot(), "data", "rules");
        var dossiers = SmgpCarDossiers.Load(rulesDir);
        Assert.False(dossiers.Cars.Count == 0, "car-dossiers.json missing or empty under data/rules/smgp.");
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
