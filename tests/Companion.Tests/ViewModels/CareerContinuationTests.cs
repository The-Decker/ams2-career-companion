using Companion.Core.Smgp;
using Companion.ViewModels.Services;

namespace Companion.Tests.ViewModels;

/// <summary>Continuation policy against the complete bundled pack root. These are deliberately
/// not isolated synthetic-root tests: a newly added pack must never make SMGP leak into historical
/// discovery or make the 17-season replica campaign jump into a historical year.</summary>
public sealed class CareerContinuationTests
{
    [Fact]
    public void BundledRoot_SmgpSeasonOneCarriesItsOwnPack_AndSeasonSeventeenTerminates()
    {
        var packs = BundledPacks();
        var smgp = packs.Single(p => p.Manifest?.PackId == "smgp-1");
        Assert.NotNull(smgp.Manifest);
        Assert.Equal(SmgpRules.CareerStyle, smgp.Manifest.CareerStyle);
        Assert.Equal(1990, smgp.SeasonYear);

        var seasonTwo = PackDiscovery.PlanNextSeason(
            smgp.Manifest,
            currentYear: smgp.SeasonYear!.Value,
            seasonOrdinal: 1,
            packs);

        Assert.NotNull(seasonTwo);
        Assert.True(seasonTwo.IsCarryover);
        Assert.Equal("smgp-1", seasonTwo.PackId);
        Assert.Equal(1991, seasonTwo.SeasonYear);
        Assert.Equal("", seasonTwo.PackDirectory);

        var afterSummit = PackDiscovery.PlanNextSeason(
            smgp.Manifest,
            currentYear: 1990 + SmgpRules.CampaignSeasons - 1,
            seasonOrdinal: SmgpRules.CampaignSeasons,
            packs);

        Assert.Null(afterSummit);
    }

    [Fact]
    public void BundledRoot_HistoricalCarryoverFrom1989ChangesIntoF11990_NotSmgp()
    {
        var packs = BundledPacks();
        var historical = packs.Single(p => p.Manifest?.PackId == "f1-1988");
        Assert.NotNull(historical.Manifest);
        Assert.True(string.IsNullOrWhiteSpace(historical.Manifest.CareerStyle));

        // Model a 1989 carryover season on the pinned 1988 car. Both f1-1990 and smgp-1 have
        // authored year 1990 in the full root; only the ordinary historical pack is eligible.
        var next = PackDiscovery.PlanNextSeason(
            historical.Manifest,
            currentYear: 1989,
            seasonOrdinal: 2,
            packs);

        Assert.NotNull(next);
        Assert.False(next.IsCarryover);
        Assert.Equal("f1-1990", next.PackId);
        Assert.Equal(1990, next.SeasonYear);
        Assert.EndsWith("f1-1990", next.PackDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<DiscoveredPack> BundledPacks()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "packs");
        Assert.True(Directory.Exists(root), $"Bundled test pack root is missing: {root}");
        var packs = PackDiscovery.Discover([root]);
        Assert.Contains(packs, p => p.Manifest?.PackId == "smgp-1");
        Assert.Contains(packs, p => p.Manifest?.PackId == "f1-1990");
        return packs;
    }
}
