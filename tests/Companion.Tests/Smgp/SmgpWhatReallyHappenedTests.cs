using Companion.Core.Smgp;

namespace Companion.Tests.Smgp;

/// <summary>
/// The SMGP-universe "What Really Happened" almanac (<c>data/rules/smgp/what-really-happened.json</c>)
/// is the fictional SEGA world's account of every calendar circuit, revealed on the History tab once
/// the player has raced it. DISPLAY-ONLY (never a fold input, like the rival quotes + news corpora).
/// These pin the loader (parse, venue lookup, empty/absent = hidden) and — the important guard — that
/// the SHIPPED almanac authors EVERY venue on the smgp-1 calendar, so a new/renamed round never shows
/// a blank legend, and never carries an orphan entry that maps to no round.
/// </summary>
public sealed class SmgpWhatReallyHappenedTests
{
    private const string Json = """
    {
      "$comment": "test fixture",
      "races": {
        "San Marino": {
          "title": "SAN MARINO — THE OPENER",
          "circuit": "the season's lights-out",
          "champion": "A. Senna · Madonna",
          "body": ["The king sets the tone.", "The insurgent lurks."],
          "notes": ["A note.", "Another note."]
        },
        "Monaco": {
          "title": "MONACO — THE JEWEL",
          "circuit": "the hairpin the game is named for",
          "champion": "A. Senna · Madonna",
          "body": ["Where kings are crowned."],
          "notes": ["The hairpin bites."]
        }
      }
    }
    """;

    private static readonly SmgpWhatReallyHappened Almanac = SmgpWhatReallyHappened.Parse(Json);

    [Fact]
    public void An_authored_venue_resolves_its_full_legend()
    {
        var lore = Almanac.ForVenue("San Marino");
        Assert.NotNull(lore);
        Assert.Equal("SAN MARINO — THE OPENER", lore!.Title);
        Assert.Equal("the season's lights-out", lore.Circuit);
        Assert.Equal("A. Senna · Madonna", lore.Champion);
        Assert.Equal(2, lore.Body.Count);
        Assert.Equal(2, lore.Notes.Count);
    }

    [Fact]
    public void An_unauthored_venue_resolves_to_null()
    {
        Assert.Null(Almanac.ForVenue("Brazil"));
        // The lookup is exact + case-sensitive (venue names key straight off the pack round name).
        Assert.Null(Almanac.ForVenue("san marino"));
        Assert.Null(Almanac.ForVenue("Monaco "));
    }

    [Fact]
    public void Venues_lists_exactly_the_authored_keys()
    {
        Assert.Equal(new[] { "Monaco", "San Marino" }, Almanac.Venues.OrderBy(v => v).ToArray());
    }

    [Fact]
    public void An_empty_bank_resolves_everything_to_null()
    {
        Assert.Null(SmgpWhatReallyHappened.Empty.ForVenue("Monaco"));
        Assert.Empty(SmgpWhatReallyHappened.Empty.Venues);
    }

    [Fact]
    public void An_absent_file_loads_as_the_empty_bank()
    {
        // A rules directory with no smgp/what-really-happened.json → Empty (the panel simply hides;
        // a non-SMGP install or an un-updated data folder is unaffected).
        string missing = Path.Combine(AppContext.BaseDirectory, "Fixtures", "no-such-rules-dir");
        var almanac = SmgpWhatReallyHappened.Load(missing);
        Assert.Empty(almanac.Venues);
        Assert.Null(almanac.ForVenue("Monaco"));
    }

    /// <summary>THE guard: the shipped almanac must author EVERY circuit on the smgp-1 calendar (keyed
    /// by the round's venue NAME so season 2+ variety still resolves it), every entry must be complete,
    /// and there must be no orphan venue that maps to no round on the calendar.</summary>
    [Fact]
    public void The_shipped_almanac_authors_every_smgp_venue_completely_with_no_orphans()
    {
        string rulesDir = Path.Combine(AppContext.BaseDirectory, "Fixtures", "rules");
        string path = Path.Combine(rulesDir, "smgp", "what-really-happened.json");
        Assert.True(File.Exists(path),
            $"'{path}' missing — check the smgp None-Include in Companion.Tests.csproj.");

        var almanac = SmgpWhatReallyHappened.Load(rulesDir);

        string packDir = Path.Combine(AppContext.BaseDirectory, "packs", "smgp-1");
        var pack = Companion.Core.Packs.PackLoader.Parse(
            File.ReadAllText(Path.Combine(packDir, "pack.json")),
            File.ReadAllText(Path.Combine(packDir, "season.json")),
            File.ReadAllText(Path.Combine(packDir, "teams.json")),
            File.ReadAllText(Path.Combine(packDir, "drivers.json")),
            File.ReadAllText(Path.Combine(packDir, "entries.json")));

        var venueNames = pack.Season.Rounds.Select(r => r.Name).ToHashSet(StringComparer.Ordinal);

        // (1) every calendar venue has a COMPLETE authored legend.
        foreach (var round in pack.Season.Rounds)
        {
            var lore = almanac.ForVenue(round.Name);
            Assert.True(lore is not null,
                $"smgp-1 round {round.Round} venue '{round.Name}' has no authored 'what really happened' entry.");
            Assert.False(string.IsNullOrWhiteSpace(lore!.Title), $"'{round.Name}' has no title.");
            Assert.False(string.IsNullOrWhiteSpace(lore.Circuit), $"'{round.Name}' has no circuit line.");
            Assert.False(string.IsNullOrWhiteSpace(lore.Champion), $"'{round.Name}' has no champion of record.");
            Assert.NotEmpty(lore.Body);
            Assert.All(lore.Body, p => Assert.False(string.IsNullOrWhiteSpace(p)));
            Assert.NotEmpty(lore.Notes);
            Assert.All(lore.Notes, n => Assert.False(string.IsNullOrWhiteSpace(n)));
        }

        // (2) no orphan: every authored venue maps to a real round on the calendar (a typo'd key would
        // silently never render, so pin it).
        foreach (var venue in almanac.Venues)
            Assert.True(venueNames.Contains(venue),
                $"almanac venue '{venue}' matches no smgp-1 calendar round — a typo or a stale key.");
    }
}
