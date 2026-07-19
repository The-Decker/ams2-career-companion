using Companion.Core.Career;

namespace Companion.Tests.Career;

/// <summary>The era-themes.json decade-override catalog (docs/dev/era-theming-assets-brief.md,
/// Workstream A1): a tolerant, pure loader reconciling the documented
/// decade→{medium, accent, fontStack, paperTexture, datelineFormat} schema with the built
/// <see cref="EraTheme"/> record. Resolution is medium-matched so the hard-coded
/// <see cref="EraThemes"/> table keeps owning the era boundaries, and the shipped file —
/// a mirror of that table, is a true no-op.</summary>
public sealed class EraThemeCatalogTests
{
    [Fact]
    public void Built_in_themes_carry_their_paper_texture_keys()
    {
        Assert.Equal("ochre-wire", EraThemes.Telegram.PaperTextureKey);
        Assert.Equal("thermal-grain", EraThemes.Fax.PaperTextureKey);
        Assert.Equal("", EraThemes.Email.PaperTextureKey);
    }

    [Fact]
    public void ForYear_picks_the_latest_matching_decade_entry_at_or_before_the_year()
    {
        var catalog = EraThemeCatalog.Parse("""
            {
              "1960": { "medium": "telegram", "accent": "#111111" },
              "1970": { "medium": "telegram", "accent": "#222222" }
            }
            """);

        Assert.Equal("#111111", catalog.ForYear(1960)!.AccentHex); // exact decade
        Assert.Equal("#111111", catalog.ForYear(1967)!.AccentHex); // 1967 reads the 1960 entry
        Assert.Equal("#222222", catalog.ForYear(1974)!.AccentHex); // the later decade wins
    }

    [Fact]
    public void ForYear_misses_when_no_entry_carries_the_years_built_in_medium()
    {
        var catalog = EraThemeCatalog.Parse("""{ "1960": { "medium": "telegram" } }""");

        Assert.Null(catalog.ForYear(1955)); // before every declared decade
        Assert.Null(catalog.ForYear(1988)); // fax year, the catalog only restyles telegram
    }

    [Fact]
    public void ForYear_never_lets_a_decade_entry_cross_a_built_in_era_boundary()
    {
        // The built-in table flips to email in 1994, mid-decade. A "1990" fax entry must
        // restyle 1990–1993 only; 1994+ keeps resolving the built-in email skin via fallback.
        var catalog = EraThemeCatalog.Parse("""{ "1990": { "medium": "fax", "accent": "#123456" } }""");

        Assert.Equal("#123456", catalog.ForYear(1993)!.AccentHex);
        Assert.Null(catalog.ForYear(1994));
        Assert.Equal(
            EraThemes.Email,
            catalog.ForYear(1994) ?? EraThemes.ForYear(1994)); // the fallback composition
    }

    [Fact]
    public void Parse_applies_a_full_override_entry()
    {
        var catalog = EraThemeCatalog.Parse("""
            {
              "1980": {
                "medium": "fax",
                "accent": "#A1B2C3",
                "fontStack": "Lucida Console, monospace",
                "paperTexture": "custom-grain",
                "datelineFormat": "VIA FAX"
              }
            }
            """);

        var theme = catalog.ForYear(1985)!;
        Assert.Equal(EraMedium.Fax, theme.Medium);
        Assert.Equal("FAX", theme.Label); // the label always derives from the medium
        Assert.Equal("#A1B2C3", theme.AccentHex);
        Assert.Equal("Lucida Console, monospace", theme.DocumentFontStack);
        Assert.Equal("custom-grain", theme.PaperTextureKey);
        Assert.Equal("VIA FAX", theme.DatelineFlourish); // datelineFormat → the flourish
    }

    [Fact]
    public void Sparse_entries_inherit_the_built_in_fields_of_their_medium()
    {
        var catalog = EraThemeCatalog.Parse("""{ "1960": { "accent": "#0F0F0F" } }""");

        var theme = catalog.ForYear(1967)!;
        Assert.Equal(EraThemes.Telegram with { AccentHex = "#0F0F0F" }, theme);
    }

    [Fact]
    public void A_medium_override_swaps_the_base_theme_the_entry_builds_from()
    {
        // A pack that declares the 1960s an EMAIL decade gets email-base fields for email years.
        var catalog = EraThemeCatalog.Parse("""{ "1960": { "medium": "email", "paperTexture": "dot-matrix" } }""");

        var theme = catalog.ForYear(1994)!; // the first built-in email year
        Assert.Equal(EraMedium.Email, theme.Medium);
        Assert.Equal(EraThemes.Email.AccentHex, theme.AccentHex); // inherited from the email base
        Assert.Equal("dot-matrix", theme.PaperTextureKey);
    }

    [Theory]
    [InlineData("""{ "1960": { "medium": "carrier-pigeon" } }""")] // unrecognized medium
    [InlineData("""{ "sixties": { "accent": "#111111" } }""")] // non-numeric decade key
    [InlineData("""{ "1960": "telegram" }""")] // non-object entry
    public void Invalid_entries_are_skipped(string json)
    {
        var catalog = EraThemeCatalog.Parse(json);

        Assert.True(catalog.IsEmpty);
        Assert.Null(catalog.ForYear(1967));
    }

    [Fact]
    public void A_malformed_accent_is_ignored_but_the_entry_survives()
    {
        var catalog = EraThemeCatalog.Parse("""
            { "1980": { "medium": "fax", "accent": "slate", "paperTexture": "custom-grain" } }
            """);

        var theme = catalog.ForYear(1988)!;
        Assert.Equal(EraThemes.Fax.AccentHex, theme.AccentHex); // fell back to the base accent
        Assert.Equal("custom-grain", theme.PaperTextureKey); // the valid field still applies
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("""["1960"]""")] // a non-object root
    [InlineData("""{ "1960": { "medium": "fax", } }""")] // a trailing comma (syntax error)
    public void A_broken_file_parses_to_empty(string json) =>
        Assert.True(EraThemeCatalog.Parse(json).IsEmpty);

    [Fact]
    public void Parse_is_deterministic()
    {
        const string json = """{ "1970": { "medium": "telegram", "accent": "#765432" } }""";

        var first = EraThemeCatalog.Parse(json);
        var second = EraThemeCatalog.Parse(json);

        Assert.Equal(first.ForYear(1974), second.ForYear(1974));
        Assert.Equal(first.Decades, second.Decades);
    }

    [Fact]
    public void Decades_list_the_override_keys_ascending()
    {
        var catalog = EraThemeCatalog.Parse("""
            { "1990": { "medium": "fax" }, "1960": { "medium": "telegram" } }
            """);

        Assert.Equal([1960, 1990], catalog.Decades);
    }

    [Fact]
    public void Load_of_a_missing_file_is_empty()
    {
        var catalog = EraThemeCatalog.Load(
            Path.Combine(CareerTestData.RulesDirectory, "no-such-subfolder"));

        Assert.True(catalog.IsEmpty);
    }

    [Fact]
    public void The_shipped_file_loads_and_mirrors_the_built_in_table_year_for_year()
    {
        // Contract: data/rules/era-themes.json is a behavior-preserving MIRROR of the hard-coded
        // table, with it in play, every year resolves exactly the built-in skin (so the file is
        // safe to ship, and community packs get a complete authoring example).
        var catalog = EraThemeCatalog.Load(CareerTestData.RulesDirectory);

        Assert.False(catalog.IsEmpty);
        foreach (int year in new[] { 1960, 1967, 1974, 1979, 1980, 1985, 1988, 1993, 1994, 2000, 2010, 2015, 2022 })
        {
            EraTheme resolved = catalog.ForYear(year) ?? EraThemes.ForYear(year);
            Assert.Equal(EraThemes.ForYear(year), resolved);
        }
    }

    [Fact]
    public void The_fallback_composition_resolves_the_built_in_skin_when_the_catalog_misses()
    {
        var composed = EraThemeCatalog.Empty.ForYear(1967) ?? EraThemes.ForYear(1967);

        Assert.Equal(EraThemes.Telegram, composed);
    }
}
