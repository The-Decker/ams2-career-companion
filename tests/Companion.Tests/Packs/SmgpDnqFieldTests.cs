using Companion.Core.Grid;
using Companion.Core.Packs;

namespace Companion.Tests.Packs;

/// <summary>
/// The SMGP replica pack's DYNAMIC per-race DNQ field (Mike, "use the max ... swap out the cars
/// that did not qualify ... change per race ... like our 1988 examples"). The pack fields all 34
/// painted cars, but the F-Classic_Gen3 class shows at most 26 distinct liveries, so each round's
/// <c>grid.starterDriverIds</c> bakes only that round's ~26 qualifiers — the slowest 8-9 DID NOT
/// QUALIFY, and WHICH ones sit out rotates race to race. Baked = deterministic + replay-safe (the
/// resolver seats exactly the listed starters; <see cref="PlayerDnqSeatingTests"/> proves the
/// player's own car is never the one dropped). These guards are STRUCTURAL — they survive a
/// re-tune of the perturbation magnitude, only failing if the DNQ field stops being dynamic, the
/// strong stop always qualifying, or the pool drifts off 34.
/// </summary>
public sealed class SmgpDnqFieldTests
{
    private static string PackDirectory =>
        Path.Combine(AppContext.BaseDirectory, "packs", "smgp-1");

    private static readonly Lazy<SeasonPack> Pack = new(() => PackLoader.Parse(
        Read("pack.json"), Read("season.json"), Read("teams.json"),
        Read("drivers.json"), Read("entries.json")));

    private static string Read(string filePart)
    {
        string path = Path.Combine(PackDirectory, filePart);
        Assert.True(File.Exists(path), $"Pack file '{path}' missing — check the packs None-Include.");
        return File.ReadAllText(path);
    }

    /// <summary>The full field is the 34 painted skins — no reserves, every livery a season entry
    /// (the DNQ field replaces the old drop-to-reserve trimming).</summary>
    [Fact]
    public void TheFieldIsAllThirtyFourPaintedCars()
    {
        Assert.Equal(34, Pack.Value.Entries.Count);
        Assert.Equal(34, Pack.Value.Entries.Select(e => e.DriverId).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>Every round bakes an exact-fit qualifier list: grid.size is the class-livery ceiling
    /// (26, or 25 where the venue caps lower), and starterDriverIds holds exactly that many real
    /// entry drivers — so the resolver never has to trim (and the fold scores a full grid).</summary>
    [Fact]
    public void EveryRoundBakesExactlyGridSizeQualifiers_FromRealEntries()
    {
        var entryDrivers = Pack.Value.Entries.Select(e => e.DriverId).ToHashSet(StringComparer.Ordinal);

        foreach (var round in Pack.Value.Season.Rounds)
        {
            var grid = round.Grid;
            Assert.NotNull(grid);
            Assert.InRange(grid!.Size, 25, 26); // 26 everywhere the venue allows; Monaco caps at 25.
            Assert.Equal(grid.Size, grid.StarterDriverIds.Count);
            Assert.Equal(grid.Size, grid.StarterDriverIds.Distinct(StringComparer.Ordinal).Count());
            foreach (var id in grid.StarterDriverIds)
                Assert.Contains(id, entryDrivers);
            // 8 (or 9) of the 34 sit out every round — the field really is pre-qualified.
            Assert.True(grid.StarterDriverIds.Count < Pack.Value.Entries.Count,
                $"round {round.Round}: all 34 cars started — the DNQ field must bench the slowest.");
        }
    }

    /// <summary>The stars never DNQ: A. Senna, G. Ceara and the two McLaren aces are on EVERY
    /// round's grid — the perturbation only reshuffles the backmarker bubble, never the top.</summary>
    [Fact]
    public void TheStrongAlwaysQualify_EveryRound()
    {
        string[] alwaysRacing =
        {
            "driver.ayrton_senna", "driver.gilberto_ceara",
            "driver.bruno_salgado", "driver.mika_larssen",
        };

        foreach (var round in Pack.Value.Season.Rounds)
        {
            var starters = round.Grid!.StarterDriverIds.ToHashSet(StringComparer.Ordinal);
            foreach (var id in alwaysRacing)
                Assert.True(starters.Contains(id),
                    $"round {round.Round}: '{id}' failed to qualify — a benchmark car must never DNQ.");
        }
    }

    /// <summary>The field is DYNAMIC: the DNQ set is not the same every round (some rounds differ),
    /// and at least one car qualifies for some rounds yet sits out others — the boundary really
    /// rotates race to race, rather than a fixed set of cars always being benched.</summary>
    [Fact]
    public void TheDnqFieldRotatesRaceToRace()
    {
        var entryDrivers = Pack.Value.Entries.Select(e => e.DriverId).ToHashSet(StringComparer.Ordinal);

        var perRoundDnq = Pack.Value.Season.Rounds
            .Select(r => entryDrivers
                .Except(r.Grid!.StarterDriverIds, StringComparer.Ordinal)
                .ToHashSet(StringComparer.Ordinal))
            .ToList();

        // More distinct cars DNQ across the season than in any single round → the set rotates
        // (a static DNQ field would have the season union equal to each round's set).
        var everDnq = perRoundDnq.SelectMany(s => s).ToHashSet(StringComparer.Ordinal);
        int maxSingleRound = perRoundDnq.Max(s => s.Count);
        Assert.True(everDnq.Count > maxSingleRound,
            $"only {everDnq.Count} cars ever DNQ'd but a single round benches up to {maxSingleRound} — " +
            "the field is not rotating, so the DNQ set is effectively fixed.");

        // And a genuinely rotating car exists: qualifies at least once AND DNQs at least once.
        bool hasRotator = entryDrivers.Any(id =>
            perRoundDnq.Any(s => s.Contains(id)) && perRoundDnq.Any(s => !s.Contains(id)));
        Assert.True(hasRotator, "no car both qualified and DNQ'd across the season — nothing rotates.");
    }

    // ---------- end-to-end through the real resolver (the in-game grid) ----------

    /// <summary>The player's own car is never the DNQ casualty: a rookie who picked the perennial
    /// backmarker Zeroforce #32 (whose AI driver fails to qualify every round) still appears on the
    /// resolved grid, and the field is held at grid.size — the resolver's player-protection seats
    /// them and drops the slowest AI qualifier instead. This is the exact rookie-start scenario.</summary>
    [Fact]
    public void PlayerInThePerennialDnqCar_IsStillSeated_AtGridSize()
    {
        const string zeroforce = "Zeroforce #32 P. Kilnger"; // the skin's verbatim label (typo and all)
        var round1 = Pack.Value.Season.Rounds.Single(r => r.Round == 1);
        // Precondition: this car did NOT qualify round 1 on its own AI merit.
        Assert.DoesNotContain(
            "driver.paul_klinger", round1.Grid!.StarterDriverIds, StringComparer.Ordinal);

        var plan = RoundGridResolver.Resolve(
            Pack.Value, round: 1, playerSeat: new PlayerSeat { Ams2LiveryName = zeroforce });

        Assert.Equal(round1.Grid!.Size, plan.Seats.Count); // 26 — one AI qualifier dropped for the player
        var player = Assert.Single(plan.Seats, s => s.IsPlayer);
        Assert.Equal(zeroforce, player.Ams2LiveryName);
        Assert.Equal("driver.paul_klinger", player.DriverId);
    }

    /// <summary>A car that DID NOT qualify is absent from the resolved grid — so it cannot be named
    /// as a rival that round (the briefing builds its rival list from the resolved grid), while a
    /// car that qualified is present. This is what makes the DNQ safe for the rival ladder: you can
    /// only ever battle someone who is actually on track.</summary>
    [Fact]
    public void ADnqdCarIsAbsentFromTheGrid_AQualifierIsPresent()
    {
        // Round 1: a strong car (Senna) qualifies; a backmarker (P. White, raceSkill 0.53) does not.
        var plan = RoundGridResolver.Resolve(Pack.Value, round: 1);
        var onGrid = plan.Seats.Select(s => s.DriverId).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("driver.ayrton_senna", onGrid);
        Assert.DoesNotContain("driver.paul_white", onGrid);
        Assert.Equal(Pack.Value.Season.Rounds.Single(r => r.Round == 1).Grid!.Size, plan.Seats.Count);
    }

    /// <summary>The staging naming pass (phase 1 of the g3m2/g3m4 in-game pool-fill fix) enumerates the
    /// WHOLE covering field via <c>ignoreStarters</c> — so every SMGP livery, including the per-race DNQ
    /// tail (P. White etc.), gets a name in the AMS2 custom-AI file and no slot AMS2 fields can
    /// stock-fill. The SIM grid (default resolve) is untouched — it still caps + DNQs, so the byte-
    /// identical replay + f1db oracle never see this cosmetic enumeration.</summary>
    [Fact]
    public void ResolveWithIgnoreStarters_EnumeratesTheWholeField_IncludingTheDnqTail()
    {
        var full = RoundGridResolver.Resolve(
            Pack.Value, round: 1, capToGridSize: false, ignoreStarters: true);
        var onGrid = full.Seats.Select(s => s.DriverId).ToHashSet(StringComparer.Ordinal);

        // The whole 34-car field enumerates — including the DNQ'd backmarker the sim grid drops.
        Assert.Equal(Pack.Value.Entries.Count, full.Seats.Count);
        Assert.Contains("driver.ayrton_senna", onGrid);
        Assert.Contains("driver.paul_white", onGrid); // DNQ'd in the sim grid; present here for naming

        // ...and the default (sim) resolve is unchanged — the DNQ tail stays off the capped grid.
        var sim = RoundGridResolver.Resolve(Pack.Value, round: 1);
        Assert.DoesNotContain("driver.paul_white", sim.Seats.Select(s => s.DriverId));
        Assert.True(full.Seats.Count > sim.Seats.Count);
    }
}
