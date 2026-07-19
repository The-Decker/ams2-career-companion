using Companion.Core.Smgp;

namespace Companion.Tests.Smgp;

/// <summary>
/// The SMGP predetermined driver career stats (<c>data/rules/smgp/driver-stats.json</c>), the world's
/// history the moment the player arrived. DISPLAY-ONLY. Pins the loader and, the important guards —
/// that the SHIPPED table covers every smgp-1 driver AND is internally coherent (the totals add up and
/// each line is monotone), so a Paddock stat block is never blank or nonsensical.
/// </summary>
public sealed class SmgpDriverStatsTests
{
    private const string Json = """
    {
      "loreSeasons": 2,
      "roundsPerSeason": 16,
      "champions": [ { "season": 1, "driverId": "driver.a" }, { "season": 2, "driverId": "driver.a" } ],
      "drivers": [
        { "driverId": "driver.a", "careerStarts": 32, "careerWins": 20, "careerPodiums": 28, "careerPoles": 22, "careerTop5s": 30, "careerPoints": 400, "championships": 2 },
        { "driverId": "driver.b", "careerStarts": 32, "careerWins": 12, "careerPodiums": 18, "careerPoles": 10, "careerTop5s": 24, "careerPoints": 240, "championships": 0 }
      ]
    }
    """;

    private static readonly SmgpDriverStats Catalog = SmgpDriverStats.Parse(Json);

    [Fact]
    public void An_authored_driver_resolves_its_stats()
    {
        var stat = Catalog.ForDriver("driver.a");
        Assert.NotNull(stat);
        Assert.Equal(20, stat!.CareerWins);
        Assert.Equal(2, stat.Championships);
        Assert.Equal(2, Catalog.LoreSeasons);
        Assert.Equal(2, Catalog.Champions.Count);
    }

    [Fact]
    public void An_unauthored_driver_and_the_empty_catalog_resolve_to_null()
    {
        Assert.Null(Catalog.ForDriver("driver.nobody"));
        Assert.Null(SmgpDriverStats.Empty.ForDriver("driver.a"));
    }

    [Fact]
    public void A_missing_file_loads_the_empty_catalog()
    {
        string missing = Path.Combine(AppContext.BaseDirectory, "Fixtures", "no-such-rules-dir");
        Assert.Same(SmgpDriverStats.Empty, SmgpDriverStats.Load(missing));
    }

    [Fact]
    public void The_shipped_table_covers_every_smgp1_driver()
    {
        var stats = SmgpDriverStats.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "rules"));
        var pack = SmgpTestPack.Load();

        foreach (var driver in pack.Drivers)
            Assert.True(stats.ForDriver(driver.Id) is not null,
                $"driver-stats.json has no stats for '{driver.Id}' ({driver.Name}).");

        var driverIds = new HashSet<string>(pack.Drivers.Select(d => d.Id), StringComparer.Ordinal);
        foreach (string id in stats.Drivers)
            Assert.True(driverIds.Contains(id), $"driver-stats.json has an orphan line '{id}' (no such driver).");
    }

    [Fact]
    public void The_shipped_table_is_globally_coherent()
    {
        var stats = SmgpDriverStats.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "rules"));
        var pack = SmgpTestPack.Load();
        var lines = pack.Drivers.Select(d => stats.ForDriver(d.Id)!).ToList();

        int races = stats.LoreSeasons * stats.RoundsPerSeason;
        Assert.Equal(races, lines.Sum(s => s.CareerWins));       // one winner per race
        Assert.Equal(races, lines.Sum(s => s.CareerPoles));      // one pole per race
        Assert.Equal(stats.LoreSeasons, lines.Sum(s => s.Championships)); // one champion per season
        Assert.Equal(stats.LoreSeasons, stats.Champions.Count);

        // Every champion named is a driver who actually carries a title.
        var titled = lines.Where(s => s.Championships > 0).Select(s => s.DriverId).ToHashSet(StringComparer.Ordinal);
        foreach (var champ in stats.Champions)
            Assert.Contains(champ.DriverId, titled);

        // Each line is monotone: starts >= top5s >= podiums >= wins, and poles/points non-negative.
        foreach (var s in lines)
        {
            Assert.True(s.CareerStarts >= s.CareerTop5s, $"{s.DriverId}: starts < top5s.");
            Assert.True(s.CareerTop5s >= s.CareerPodiums, $"{s.DriverId}: top5s < podiums.");
            Assert.True(s.CareerPodiums >= s.CareerWins, $"{s.DriverId}: podiums < wins.");
            Assert.True(s.CareerPoles >= 0 && s.CareerPoints >= 0, $"{s.DriverId}: negative poles/points.");
        }

        // Canon: A. Senna leads wins, poles and titles by a clear margin (the untouchable crown).
        var senna = stats.ForDriver("driver.ayrton_senna")!;
        Assert.True(lines.All(s => s.DriverId == "driver.ayrton_senna" || s.CareerWins < senna.CareerWins),
            "Senna must lead career wins outright.");
        Assert.True(senna.Championships >= 3, "Senna must be a serial champion.");
    }
}
