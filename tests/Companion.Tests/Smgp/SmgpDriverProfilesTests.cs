using Companion.Core.Smgp;

namespace Companion.Tests.Smgp;

/// <summary>
/// The SMGP-universe driver biographies (<c>data/rules/smgp/driver-profiles.json</c>): each driver's
/// epithet + ~3-paragraph bio + quotes, shown on the Paddock driver-preview tab. DISPLAY-ONLY. These
/// pin the loader and, the important guard, that the SHIPPED catalog authors EVERY driver on the
/// smgp-1 grid, so no driver card ever renders blank.
/// </summary>
public sealed class SmgpDriverProfilesTests
{
    private const string Json = """
    {
      "$comment": "test fixture",
      "drivers": [
        {
          "driverId": "driver.ayrton_senna",
          "name": "Ayrton Senna",
          "epithet": "THE UNTOUCHABLE KING",
          "bio": ["The king.", "Serene and inevitable.", "The crown."],
          "quotes": ["I set the time they fail to reach."]
        }
      ]
    }
    """;

    private static readonly SmgpDriverProfiles Catalog = SmgpDriverProfiles.Parse(Json);

    [Fact]
    public void An_authored_driver_resolves_its_profile()
    {
        var profile = Catalog.ForDriver("driver.ayrton_senna");
        Assert.NotNull(profile);
        Assert.Equal("Ayrton Senna", profile!.Name);
        Assert.Equal("THE UNTOUCHABLE KING", profile.Epithet);
        Assert.Equal(3, profile.Bio.Count);
        Assert.NotEmpty(profile.Quotes);
    }

    [Fact]
    public void An_unauthored_driver_and_the_empty_catalog_resolve_to_null()
    {
        Assert.Null(Catalog.ForDriver("driver.nobody"));
        Assert.Null(SmgpDriverProfiles.Empty.ForDriver("driver.ayrton_senna"));
    }

    [Fact]
    public void A_missing_file_loads_the_empty_catalog()
    {
        string missing = Path.Combine(AppContext.BaseDirectory, "Fixtures", "no-such-rules-dir");
        Assert.Same(SmgpDriverProfiles.Empty, SmgpDriverProfiles.Load(missing));
    }

    [Fact]
    public void The_shipped_catalog_authors_every_smgp1_driver_with_a_full_bio()
    {
        var catalog = SmgpDriverProfiles.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "rules"));
        var pack = SmgpTestPack.Load();

        foreach (var driver in pack.Drivers)
        {
            var profile = catalog.ForDriver(driver.Id);
            Assert.True(profile is not null, $"driver-profiles.json has no profile for '{driver.Id}' ({driver.Name}).");
            Assert.False(string.IsNullOrWhiteSpace(profile!.Epithet), $"{driver.Id}: empty epithet.");
            Assert.True(profile.Bio.Count == 3, $"{driver.Id}: bio has {profile.Bio.Count} paragraphs (expected 3).");
            Assert.All(profile.Bio, p => Assert.False(string.IsNullOrWhiteSpace(p), $"{driver.Id}: blank bio paragraph."));
            Assert.NotEmpty(profile.Quotes);
        }

        // No orphan profile that maps to no driver on the grid.
        var driverIds = new HashSet<string>(pack.Drivers.Select(d => d.Id), StringComparer.Ordinal);
        foreach (string id in catalog.Drivers)
            Assert.True(driverIds.Contains(id), $"driver-profiles.json has an orphan profile '{id}' (no such driver).");
    }
}
