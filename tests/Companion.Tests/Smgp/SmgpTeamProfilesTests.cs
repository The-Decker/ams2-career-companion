using Companion.Core.Smgp;

namespace Companion.Tests.Smgp;

/// <summary>
/// The SMGP-universe team profiles (<c>data/rules/smgp/team-profiles.json</c>): each SEGA-world team's
/// motto, ~5-paragraph history and quotes, shown on the promotion/demotion screen when the player joins
/// a team. DISPLAY-ONLY (never a fold input, like the rival quotes + almanac + news corpora). These pin
/// the loader (parse, team lookup, empty/absent = omitted) and, the important guard, that the SHIPPED
/// catalog authors EVERY team on the smgp-1 grid, so a promotion to any team always has a story.
/// </summary>
public sealed class SmgpTeamProfilesTests
{
    private const string Json = """
    {
      "$comment": "test fixture",
      "teams": {
        "team.madonna": {
          "name": "Madonna",
          "motto": "THE CROWN NEVER SLIPS",
          "history": ["The king's team.", "Yellow and red.", "Untouchable.", "The benchmark.", "The crown."],
          "quotes": ["We do not chase. We are chased.", "The crown is heavy. We carry it well."]
        }
      }
    }
    """;

    private static readonly SmgpTeamProfiles Catalog = SmgpTeamProfiles.Parse(Json);

    [Fact]
    public void An_authored_team_resolves_its_profile()
    {
        var profile = Catalog.ForTeam("team.madonna");
        Assert.NotNull(profile);
        Assert.Equal("Madonna", profile!.Name);
        Assert.Equal("THE CROWN NEVER SLIPS", profile.Motto);
        Assert.Equal(5, profile.History.Count);
        Assert.NotEmpty(profile.Quotes);
    }

    [Fact]
    public void An_unauthored_team_and_the_empty_catalog_resolve_to_null()
    {
        Assert.Null(Catalog.ForTeam("team.unknown"));
        Assert.Null(SmgpTeamProfiles.Empty.ForTeam("team.madonna"));
    }

    [Fact]
    public void A_missing_file_loads_the_empty_catalog()
    {
        string missing = Path.Combine(AppContext.BaseDirectory, "Fixtures", "no-such-rules-dir");
        Assert.Same(SmgpTeamProfiles.Empty, SmgpTeamProfiles.Load(missing));
    }

    [Fact]
    public void The_shipped_catalog_authors_every_smgp1_team_with_a_full_profile()
    {
        string rulesDir = Path.Combine(AppContext.BaseDirectory, "Fixtures", "rules");
        var catalog = SmgpTeamProfiles.Load(rulesDir);

        string packDir = Path.Combine(AppContext.BaseDirectory, "packs", "smgp-1");
        var pack = Companion.Core.Packs.PackLoader.Parse(
            File.ReadAllText(Path.Combine(packDir, "pack.json")),
            File.ReadAllText(Path.Combine(packDir, "season.json")),
            File.ReadAllText(Path.Combine(packDir, "teams.json")),
            File.ReadAllText(Path.Combine(packDir, "drivers.json")),
            File.ReadAllText(Path.Combine(packDir, "entries.json")));

        foreach (var team in pack.Teams)
        {
            var profile = catalog.ForTeam(team.Id);
            Assert.True(profile is not null, $"team-profiles.json has no profile for '{team.Id}' ({team.Name}).");
            Assert.False(string.IsNullOrWhiteSpace(profile!.Motto), $"{team.Id}: empty motto.");
            Assert.True(profile.History.Count >= 5, $"{team.Id}: history has {profile.History.Count} paragraphs (< 5).");
            Assert.All(profile.History, p => Assert.False(string.IsNullOrWhiteSpace(p), $"{team.Id}: blank history paragraph."));
            Assert.NotEmpty(profile.Quotes);
        }

        // No orphan profile that maps to no team on the grid.
        var teamIds = new HashSet<string>(pack.Teams.Select(t => t.Id), StringComparer.Ordinal);
        foreach (string id in catalog.Teams)
            Assert.True(teamIds.Contains(id), $"team-profiles.json has an orphan profile '{id}' (no such team).");
    }
}
