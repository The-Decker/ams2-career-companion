using Companion.Core.Packs;
using Companion.Core.Smgp;

namespace Companion.Tests.Packs;

/// <summary>
/// The SEEDED per-career DNQ generator (Mike: "a random generator should determine the bottom 8 ... who
/// stays and who goes"). Where <see cref="SmgpDnqFieldTests"/> guards the pack's BAKED default, this
/// guards the live roll that replaces it at career creation: deterministic per seed (so replay off the
/// pinned pack is byte-identical), different ACROSS seeds (so each playthrough rotates its own tail),
/// always exact-fit + real, the strong never dropped, and the transform round-trips through season.json.
/// </summary>
public sealed class SmgpDnqGeneratorTests
{
    private static string PackDir => Path.Combine(AppContext.BaseDirectory, "packs", "smgp-1");
    private static string Read(string f) => File.ReadAllText(Path.Combine(PackDir, f));

    private static readonly Lazy<SeasonPack> Pack = new(() => PackLoader.Parse(
        Read("pack.json"), Read("season.json"), Read("teams.json"), Read("drivers.json"), Read("entries.json")));

    [Fact]
    public void HasDnqField_IsTrueForSmgp1()
    {
        Assert.True(SmgpDnqField.HasDnqField(Pack.Value)); // 34 cars, grids cap at 25-26
    }

    [Fact]
    public void Generate_IsDeterministic_ForAGivenSeed()
    {
        var a = SmgpDnqField.Generate(Pack.Value, 42);
        var b = SmgpDnqField.Generate(Pack.Value, 42);

        Assert.Equal(a.Keys.OrderBy(k => k), b.Keys.OrderBy(k => k));
        foreach (var round in a.Keys)
            Assert.Equal(a[round], b[round]); // byte-for-byte same order → the pinned pack is stable
    }

    [Fact]
    public void Generate_RollsADifferentField_ForADifferentSeed()
    {
        var a = SmgpDnqField.Generate(Pack.Value, 42);
        var b = SmgpDnqField.Generate(Pack.Value, 987654321);

        // At least one round seats a different SET of qualifiers — the rotation is per-career, not fixed.
        bool anyDiffers = a.Keys.Any(r =>
            !a[r].ToHashSet(StringComparer.Ordinal).SetEquals(b[r]));
        Assert.True(anyDiffers, "two seeds produced the identical field every round — the roll isn't seeded.");
    }

    [Fact]
    public void EveryRound_SeatsExactlyGridSize_DistinctRealEntries()
    {
        var field = Pack.Value.Entries.Select(e => e.DriverId).ToHashSet(StringComparer.Ordinal);
        var generated = SmgpDnqField.Generate(Pack.Value, 42);

        foreach (var round in Pack.Value.Season.Rounds)
        {
            Assert.True(generated.TryGetValue(round.Round, out var starters), $"round {round.Round}: no field rolled.");
            Assert.Equal(round.Grid!.Size, starters!.Count);
            Assert.Equal(round.Grid.Size, starters.Distinct(StringComparer.Ordinal).Count());
            Assert.All(starters, id => Assert.Contains(id, field));
            Assert.True(starters.Count < field.Count, $"round {round.Round}: nobody DNQ'd."); // a real tail sits out
        }
    }

    [Fact]
    public void TheStrong_AlwaysQualify_ForAnySeed()
    {
        string[] benchmarks = { "driver.ayrton_senna", "driver.gilberto_ceara", "driver.bruno_salgado", "driver.mika_larssen" };

        foreach (ulong seed in new ulong[] { 1, 42, 987654321, ulong.MaxValue })
        {
            var generated = SmgpDnqField.Generate(Pack.Value, seed);
            foreach (var (round, starters) in generated)
            {
                var set = starters.ToHashSet(StringComparer.Ordinal);
                foreach (var star in benchmarks)
                    Assert.True(set.Contains(star), $"seed {seed}, round {round}: '{star}' was rolled out — a benchmark must never DNQ.");
            }
        }
    }

    [Fact]
    public void TheDnqTail_Rotates_AcrossRounds()
    {
        var field = Pack.Value.Entries.Select(e => e.DriverId).ToHashSet(StringComparer.Ordinal);
        var generated = SmgpDnqField.Generate(Pack.Value, 42);

        var perRoundDnq = generated.Values
            .Select(s => field.Except(s, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal))
            .ToList();

        int everDnq = perRoundDnq.SelectMany(s => s).Distinct(StringComparer.Ordinal).Count();
        int maxSingleRound = perRoundDnq.Max(s => s.Count);
        Assert.True(everDnq > maxSingleRound, "the DNQ set doesn't rotate — more cars DNQ across the season than in any one round should hold.");
    }

    // ---------- the per-season re-roll (17-season campaign) ----------

    [Fact]
    public void ForSeason_Season1_ReturnsThePackVerbatim()
    {
        // Season 1 keeps its PINNED creation roll — ForSeason must be a no-op (same reference).
        Assert.Same(Pack.Value, SmgpDnqField.ForSeason(Pack.Value, 1, 424242));
        Assert.Same(Pack.Value, SmgpDnqField.ForSeason(Pack.Value, 0, 424242));
    }

    [Fact]
    public void ForSeason_ReRollsADifferentField_ForEachSeason()
    {
        var pack = Pack.Value;
        const ulong seed = 424242;

        var s1Roll = SmgpDnqField.Generate(pack, seed, 1); // season 1's actual seeded roll
        var s2 = SmgpDnqField.ForSeason(pack, 2, seed);
        var s3 = SmgpDnqField.ForSeason(pack, 3, seed);

        bool Differs(SeasonPack a, IReadOnlyDictionary<int, IReadOnlyList<string>> b) =>
            a.Season.Rounds.Any(r =>
                !r.Grid!.StarterDriverIds.ToHashSet(StringComparer.Ordinal).SetEquals(b[r.Round]));
        bool DiffersPacks(SeasonPack a, SeasonPack b) =>
            a.Season.Rounds.Any(r =>
                !r.Grid!.StarterDriverIds.ToHashSet(StringComparer.Ordinal).SetEquals(
                    b.Season.Rounds.Single(x => x.Round == r.Round).Grid!.StarterDriverIds));

        Assert.True(Differs(s2, s1Roll), "season 2 matched season 1's roll on every round — no re-roll.");
        Assert.True(DiffersPacks(s2, s3), "season 2 and 3 rolled the identical field — the ordinal isn't in the seed key.");
    }

    [Fact]
    public void ForSeason_ReRolledField_StaysExactFit_AndKeepsTheStrong()
    {
        var pack = Pack.Value;
        var field = pack.Entries.Select(e => e.DriverId).ToHashSet(StringComparer.Ordinal);
        string[] benchmarks = { "driver.ayrton_senna", "driver.gilberto_ceara" };

        foreach (int ord in new[] { 2, 5, 17 })
        {
            var transformed = SmgpDnqField.ForSeason(pack, ord, 55);
            foreach (var round in transformed.Season.Rounds)
            {
                var starters = round.Grid!.StarterDriverIds;
                Assert.Equal(round.Grid.Size, starters.Count);
                Assert.Equal(round.Grid.Size, starters.Distinct(StringComparer.Ordinal).Count());
                Assert.All(starters, id => Assert.Contains(id, field));
                foreach (var star in benchmarks)
                    Assert.Contains(star, starters); // a benchmark never DNQs, any season
            }
        }
    }

    [Fact]
    public void Transform_PinsTheGeneratedStarters_IntoSeasonJson_AndReparses()
    {
        var generated = SmgpDnqField.Generate(Pack.Value, 42);
        string transformed = SmgpDnqField.ApplyToSeasonJson(Read("season.json"), generated);

        var reloaded = PackLoader.Parse(
            Read("pack.json"), transformed, Read("teams.json"), Read("drivers.json"), Read("entries.json"));

        bool anyChangedFromBaked = false;
        foreach (var round in reloaded.Season.Rounds)
        {
            Assert.Equal(
                generated[round.Round].ToHashSet(StringComparer.Ordinal),
                round.Grid!.StarterDriverIds.ToHashSet(StringComparer.Ordinal));
            if (!Pack.Value.Season.Rounds.Single(r => r.Round == round.Round).Grid!
                    .StarterDriverIds.ToHashSet(StringComparer.Ordinal)
                    .SetEquals(round.Grid.StarterDriverIds))
                anyChangedFromBaked = true;
        }
        Assert.True(anyChangedFromBaked, "the seeded field matched the baked default every round — the roll had no effect.");
    }
}
