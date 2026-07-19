using Companion.ViewModels.Services;

namespace Companion.Tests.ViewModels;

/// <summary>The drop-in era-art resolver (career-hub-design.md §11): a pure most-specific-first
/// candidate ordering plus a filesystem probe. A year-specific file beats the era-medium file; a
/// missing image resolves to null (the card then shows its coloured placeholder). No WPF, the
/// resolver lives in the ViewModels layer so it unit-tests with plain temp files.</summary>
public sealed class EraArtResolverTests
{
    [Fact]
    public void CandidateFileNames_orders_year_files_before_the_era_medium_file()
    {
        var names = EraArtResolver.CandidateFileNames(1967);

        // Year files (both extensions) come first, then the era-medium files.
        Assert.Equal(["1967.jpg", "1967.png", "telegram.jpg", "telegram.png"], names);
    }

    [Theory]
    [InlineData(1974, "telegram")]
    [InlineData(1988, "fax")]
    [InlineData(2019, "email")]
    public void CandidateFileNames_uses_the_year_s_era_medium_as_the_fallback_basename(int year, string medium)
    {
        var names = EraArtResolver.CandidateFileNames(year);

        Assert.Contains($"{year}.jpg", names);
        Assert.Contains($"{medium}.jpg", names);
        Assert.Contains($"{medium}.png", names);
    }

    [Fact]
    public void IdentityKey_resolves_the_smgp_art_beside_a_colliding_year()
    {
        // SMGP shares 1990 with the f1-1990 pack, so it keys its own art by identity ("smgp"),
        // never the year, CandidateFileNamesForKey is just <key>.jpg / <key>.png.
        Assert.Equal("smgp", EraArtResolver.SmgpArtKey);
        Assert.Equal(["smgp.jpg", "smgp.png"], EraArtResolver.CandidateFileNamesForKey("smgp"));

        using var dir = new TempDir();
        Assert.Null(EraArtResolver.ResolveKey(dir.Path, "smgp"));   // none present → placeholder
        File.WriteAllText(Path.Combine(dir.Path, "smgp.jpg"), "x");
        Assert.Equal(Path.Combine(dir.Path, "smgp.jpg"), EraArtResolver.ResolveKey(dir.Path, "smgp"));
    }

    [Fact]
    public void Resolve_returns_null_when_the_directory_is_missing()
    {
        string missing = Path.Combine(Path.GetTempPath(), "era-art-" + Guid.NewGuid().ToString("N"));

        Assert.Null(EraArtResolver.Resolve(missing, 1967));
    }

    [Fact]
    public void Resolve_returns_null_when_no_candidate_exists()
    {
        using var dir = new TempDir();
        // An unrelated file in the folder must not count as a match.
        File.WriteAllText(Path.Combine(dir.Path, "notes.txt"), "x");

        Assert.Null(EraArtResolver.Resolve(dir.Path, 1967));
    }

    [Fact]
    public void Resolve_finds_the_era_medium_file_when_no_year_file_exists()
    {
        using var dir = new TempDir();
        string mediumFile = Path.Combine(dir.Path, "telegram.jpg");
        File.WriteAllBytes(mediumFile, [0xFF, 0xD8]); // 1967 → telegram

        Assert.Equal(mediumFile, EraArtResolver.Resolve(dir.Path, 1967));
    }

    [Fact]
    public void Resolve_prefers_the_year_file_over_the_era_medium_file()
    {
        using var dir = new TempDir();
        string yearFile = Path.Combine(dir.Path, "1967.jpg");
        File.WriteAllBytes(yearFile, [0xFF, 0xD8]);
        File.WriteAllBytes(Path.Combine(dir.Path, "telegram.jpg"), [0xFF, 0xD8]);

        Assert.Equal(yearFile, EraArtResolver.Resolve(dir.Path, 1967));
    }

    [Fact]
    public void Resolve_prefers_jpg_over_png_at_the_same_specificity()
    {
        using var dir = new TempDir();
        string jpg = Path.Combine(dir.Path, "1988.jpg");
        File.WriteAllBytes(jpg, [0xFF, 0xD8]);
        File.WriteAllBytes(Path.Combine(dir.Path, "1988.png"), [0x89, 0x50]);

        Assert.Equal(jpg, EraArtResolver.Resolve(dir.Path, 1988));
    }

    [Fact]
    public void Resolve_accepts_a_png_year_file()
    {
        using var dir = new TempDir();
        string png = Path.Combine(dir.Path, "1994.png");
        File.WriteAllBytes(png, [0x89, 0x50]);

        Assert.Equal(png, EraArtResolver.Resolve(dir.Path, 1994));
    }

    [Theory]
    [InlineData("Formula One World Championship 1967", 1967)]
    [InlineData("My 1988 season", 1988)]
    [InlineData("Career 2019", 2019)]
    public void YearFromText_reads_the_year_out_of_a_career_name(string name, int expected) =>
        Assert.Equal(expected, EraArtResolver.YearFromText(name));

    [Theory]
    [InlineData("My Career")]
    [InlineData("")]
    [InlineData(null)]
    public void YearFromText_returns_null_when_there_is_no_year(string? name) =>
        Assert.Null(EraArtResolver.YearFromText(name));

    // ---------- YearForEntry: the robust stored-year path + legacy fallback ----------

    [Fact]
    public void YearForEntry_uses_the_stored_season_year_even_when_the_name_disagrees()
    {
        // The stored year is authoritative, a name with a DIFFERENT (or no) year never overrides it.
        var entry = Entry(name: "My 1972 rebuild", seasonYear: 1967);

        Assert.Equal(1967, EraArtResolver.YearForEntry(entry));
    }

    [Fact]
    public void YearForEntry_falls_back_to_the_name_year_for_a_legacy_entry_with_no_stored_year()
    {
        // seasonYear == 0 is the JSON read-with-default for an entry persisted before the field
        // existed: resolve the era the old, fragile way (parse the name) rather than lose it.
        var entry = Entry(name: "Formula One 1988", seasonYear: 0);

        Assert.Equal(1988, EraArtResolver.YearForEntry(entry));
    }

    [Fact]
    public void YearForEntry_is_null_for_a_legacy_entry_whose_name_carries_no_year()
    {
        // No stored year AND no parseable name year → null → the card shows its neutral placeholder.
        var entry = Entry(name: "My Career", seasonYear: 0);

        Assert.Null(EraArtResolver.YearForEntry(entry));
    }

    private static RecentCareer Entry(string name, int seasonYear) => new()
    {
        Path = @"C:\careers\x.ams2career",
        CareerName = name,
        LastOpenedUtc = DateTimeOffset.UnixEpoch,
        SeasonYear = seasonYear,
    };

    [Fact]
    public void ResolveForText_resolves_by_the_year_in_the_name()
    {
        using var dir = new TempDir();
        string yearFile = Path.Combine(dir.Path, "1967.jpg");
        File.WriteAllBytes(yearFile, [0xFF, 0xD8]);

        Assert.Equal(yearFile, EraArtResolver.ResolveForText(dir.Path, "Formula One 1967"));
    }

    [Fact]
    public void ResolveForText_returns_null_for_a_name_with_no_year()
    {
        using var dir = new TempDir();
        File.WriteAllBytes(Path.Combine(dir.Path, "telegram.jpg"), [0xFF, 0xD8]);

        // No year in the name → no era to resolve → placeholder (null), even though telegram.jpg exists.
        Assert.Null(EraArtResolver.ResolveForText(dir.Path, "My Career"));
    }

    /// <summary>A throwaway directory that deletes itself (best-effort) on dispose.</summary>
    private sealed class TempDir : IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "era-art-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }
}
