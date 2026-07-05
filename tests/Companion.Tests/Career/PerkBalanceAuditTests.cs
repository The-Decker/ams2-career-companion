using System.Text.Json;
using System.Text.RegularExpressions;

namespace Companion.Tests.Career;

/// <summary>
/// CI balance audit for the driver-character perk rules (data/rules/perks.json), enforcing the
/// mechanical invariants documented in docs/dev/character-system.md §11.4 (and the schema in §7).
///
/// This is a data-only, additive audit: it parses the shipped perks.json (copied to the test
/// output at Fixtures/rules/perks.json by the csproj) and asserts the balance contract WITHOUT
/// touching any fold/scoring/sim code. If a NEW authored archetype or perk fails, the fix is to
/// the DATA, never to this audit.
///
/// The audit proves: 42 perks with unique snake_case ids; every effect lever/condition/stream
/// token is from the documented closed set (§7.2); |Σ cpEquivalent − cost| ≤ 0.5 per perk; no
/// free-lunch (positive benefit-CP with zero drawback-CP) and no pure-trap (negative benefit-CP
/// with no benefit at all); every perk carries ≥1 benefit AND ≥1 drawback effect; all 13 archetype
/// presets reference real perk ids with net spend in [0, budget+headroom] = [0, 16]; ≥3 perks per
/// category; the XP curve is strictly increasing.
/// </summary>
public class PerkBalanceAuditTests
{
    private const int ExpectedPerkCount = 42;
    private const int ExpectedArchetypeCount = 13;

    // §7.2 — the closed lever vocabulary. Every effect's "lever" must be one of these.
    private static readonly HashSet<string> KnownLevers = new(StringComparer.Ordinal)
    {
        "statDelta", "carScalar", "opiRetention", "opiErrorBlame", "reputationGainRate",
        "underdogMultiplier", "marketability", "paceAnchorAlpha", "agingCurve", "offerWeight",
        "income", "injuryHazard", "xpRate", "statPoints",
    };

    // §7.1 — the closed condition set. A missing condition (null) is always allowed.
    private static readonly HashSet<string> KnownConditions = new(StringComparer.Ordinal)
    {
        "wetRound", "dryRound", "longRace", "shortRace", "tierLte2", "tierGte4",
        "eraTransition", "driverErrorDnf", "ageLtPeak", "ageGtePeak",
    };

    // The registered streams a perk may name (§6.2 + streams block). "none" = deterministic.
    private static readonly HashSet<string> KnownStreams = new(StringComparer.Ordinal)
    {
        "none", "injury", "form-swing", "character-gen",
    };

    private static readonly Regex SnakeCase = new("^[a-z][a-z0-9_]*$", RegexOptions.Compiled);

    private static JsonElement Root()
    {
        string json = CareerTestData.ReadRules("perks.json");
        // Parse tolerantly: the file carries $comment / $auditNote annotations and is authored by
        // hand, so allow trailing commas / comments the same way the Core loader would.
        var options = new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
        // Keep the document alive for the duration of the test by cloning the root element.
        using var doc = JsonDocument.Parse(json, options);
        return doc.RootElement.Clone();
    }

    private static JsonElement Perks(JsonElement root) => root.GetProperty("perks");

    private readonly record struct Effect(
        string Kind, string Lever, string? Target, double Magnitude, string? Condition, double CpEquivalent);

    private static IReadOnlyList<Effect> EffectsOf(JsonElement perk)
    {
        var list = new List<Effect>();
        foreach (var e in perk.GetProperty("effects").EnumerateArray())
        {
            list.Add(new Effect(
                Kind: e.GetProperty("kind").GetString()!,
                Lever: e.GetProperty("lever").GetString()!,
                Target: e.TryGetProperty("target", out var t) ? t.GetString() : null,
                Magnitude: e.TryGetProperty("magnitude", out var m) ? m.GetDouble() : 0.0,
                Condition: e.TryGetProperty("condition", out var c) && c.ValueKind == JsonValueKind.String
                    ? c.GetString()
                    : null,
                CpEquivalent: e.GetProperty("cpEquivalent").GetDouble()));
        }
        return list;
    }

    // ---------- structural invariants ----------

    [Fact]
    public void PerksFileParsesAndHasExactlyFortyTwoPerks()
    {
        var root = Root();
        Assert.Equal(ExpectedPerkCount, Perks(root).GetArrayLength());
    }

    [Fact]
    public void EveryPerkIdIsUniqueSnakeCase()
    {
        var root = Root();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var perk in Perks(root).EnumerateArray())
        {
            string id = perk.GetProperty("id").GetString()!;
            Assert.True(SnakeCase.IsMatch(id), $"Perk id '{id}' is not snake_case.");
            Assert.True(seen.Add(id), $"Duplicate perk id '{id}'.");
        }
        Assert.Equal(ExpectedPerkCount, seen.Count);
    }

    [Fact]
    public void EveryEffectUsesOnlyDocumentedLeverConditionAndStreamTokens()
    {
        var root = Root();
        foreach (var perk in Perks(root).EnumerateArray())
        {
            string id = perk.GetProperty("id").GetString()!;

            string stream = perk.GetProperty("stream").GetString()!;
            Assert.True(KnownStreams.Contains(stream),
                $"Perk '{id}' names unknown stream '{stream}'.");

            foreach (var e in EffectsOf(perk))
            {
                Assert.True(e.Kind is "benefit" or "drawback",
                    $"Perk '{id}' has effect kind '{e.Kind}' (must be benefit|drawback).");
                Assert.True(KnownLevers.Contains(e.Lever),
                    $"Perk '{id}' uses unknown lever '{e.Lever}'.");
                if (e.Condition is not null)
                    Assert.True(KnownConditions.Contains(e.Condition),
                        $"Perk '{id}' uses unknown condition '{e.Condition}'.");
            }
        }
    }

    // ---------- balance invariants (§4.1 / §11.4) ----------

    [Fact]
    public void EveryPerkCpEquivalentSumMatchesItsDeclaredCost()
    {
        var root = Root();
        foreach (var perk in Perks(root).EnumerateArray())
        {
            string id = perk.GetProperty("id").GetString()!;
            double cost = perk.GetProperty("cost").GetDouble();
            double sum = EffectsOf(perk).Sum(e => e.CpEquivalent);
            Assert.True(Math.Abs(sum - cost) <= 0.5 + 1e-9,
                $"Perk '{id}': |Σ cpEquivalent ({sum:0.###}) − cost ({cost})| = {Math.Abs(sum - cost):0.###} > 0.5.");
        }
    }

    [Fact]
    public void NoPerkIsAFreeLunchOrAPureTrap()
    {
        var root = Root();
        foreach (var perk in Perks(root).EnumerateArray())
        {
            string id = perk.GetProperty("id").GetString()!;
            var effects = EffectsOf(perk);

            // Benefit-CP = the positive value the perk hands out; drawback-CP = the negative it charges.
            double benefitCp = effects.Where(e => e.Kind == "benefit").Sum(e => e.CpEquivalent);
            double drawbackCp = effects.Where(e => e.Kind == "drawback").Sum(e => e.CpEquivalent);

            // (b) No free lunch: a perk that hands out real benefit-CP must pay for it with real
            // drawback-CP (strictly negative), so nothing is strictly dominant.
            if (benefitCp > 1e-9)
                Assert.True(drawbackCp < -1e-9,
                    $"Perk '{id}' is a FREE LUNCH: benefit-CP {benefitCp:0.###} with zero/positive drawback-CP {drawbackCp:0.###}.");

            // (c) No pure trap: a perk that charges real drawback-CP must return real benefit-CP,
            // so nothing is strictly a trap.
            if (drawbackCp < -1e-9)
                Assert.True(benefitCp > 1e-9,
                    $"Perk '{id}' is a PURE TRAP: drawback-CP {drawbackCp:0.###} with zero/negative benefit-CP {benefitCp:0.###}.");
        }
    }

    [Fact]
    public void EveryPerkHasAtLeastOneBenefitAndOneDrawbackEffect()
    {
        var root = Root();
        foreach (var perk in Perks(root).EnumerateArray())
        {
            string id = perk.GetProperty("id").GetString()!;
            var effects = EffectsOf(perk);
            Assert.Contains(effects, e => e.Kind == "benefit");
            Assert.Contains(effects, e => e.Kind == "drawback");
        }
    }

    // ---------- category spread (§8) ----------

    [Fact]
    public void EveryCategoryHasAtLeastThreePerks()
    {
        var root = Root();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var perk in Perks(root).EnumerateArray())
        {
            string category = perk.GetProperty("category").GetString()!;
            counts[category] = counts.GetValueOrDefault(category) + 1;
        }

        Assert.NotEmpty(counts);
        foreach (var (category, count) in counts)
            Assert.True(count >= 3, $"Category '{category}' has only {count} perk(s) (< 3).");
    }

    // ---------- archetype presets (§8.1 + the 6 new) ----------

    [Fact]
    public void AllThirteenArchetypesReferenceRealPerksWithInBudgetNetSpend()
    {
        var root = Root();

        // Budget envelope from the file: net spend must land in [minAfterSpend, budget+headroom].
        var cp = root.GetProperty("characterPoints");
        int budget = cp.GetProperty("creationBudget").GetInt32();
        int headroom = cp.GetProperty("maxRefundHeadroom").GetInt32();
        int minAfter = cp.GetProperty("minBudgetAfterSpend").GetInt32();
        int maxNet = budget + headroom;

        // Real perk id -> cost map, for net-spend arithmetic.
        var perkCost = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var perk in Perks(root).EnumerateArray())
            perkCost[perk.GetProperty("id").GetString()!] = perk.GetProperty("cost").GetInt32();

        var archetypes = root.GetProperty("creation").GetProperty("archetypes");
        Assert.Equal(ExpectedArchetypeCount, archetypes.GetArrayLength());

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var a in archetypes.EnumerateArray())
        {
            string id = a.GetProperty("id").GetString()!;
            Assert.True(seenIds.Add(id), $"Duplicate archetype id '{id}'.");

            var ids = a.GetProperty("perkIds").EnumerateArray().Select(e => e.GetString()!).ToList();
            Assert.NotEmpty(ids);

            int net = 0;
            foreach (string pid in ids)
            {
                Assert.True(perkCost.ContainsKey(pid),
                    $"Archetype '{id}' references unknown perk id '{pid}'.");
                net += perkCost[pid];
            }

            Assert.True(net >= minAfter && net <= maxNet,
                $"Archetype '{id}' net spend {net} is outside [{minAfter}, {maxNet}].");

            // Every archetype must also author a full stat spread (5 talent stats in 0.15..0.85)
            // and the two meta-stats, so a one-click preset is a complete, valid character.
            var stats = a.GetProperty("startStats");
            foreach (string statId in new[] { "pace", "oneLap", "craft", "racecraft", "adaptability" })
            {
                Assert.True(stats.TryGetProperty(statId, out var s),
                    $"Archetype '{id}' is missing start stat '{statId}'.");
                Assert.InRange(s.GetDouble(), 0.15, 0.85);
            }
            var meta = a.GetProperty("startMeta");
            foreach (string metaId in new[] { "marketability", "durability" })
            {
                Assert.True(meta.TryGetProperty(metaId, out var s),
                    $"Archetype '{id}' is missing start meta '{metaId}'.");
                Assert.InRange(s.GetDouble(), 0.0, 1.0);
            }
        }
    }

    // ---------- progression curve (§3.2) ----------

    [Fact]
    public void XpCurveIsStrictlyIncreasing()
    {
        var root = Root();
        var curve = root.GetProperty("levels").GetProperty("xpCurve");
        double baseXp = curve.GetProperty("baseXpToLevel2").GetDouble();
        double growth = curve.GetProperty("growth").GetDouble();
        int maxLevel = curve.GetProperty("maxLevel").GetInt32();

        Assert.True(baseXp > 0, "baseXpToLevel2 must be positive.");
        Assert.True(growth > 1.0, "growth must exceed 1.0 for a strictly increasing curve.");
        Assert.True(maxLevel >= 2, "maxLevel must be at least 2.");

        // xpForLevel(n) = round(baseXpToLevel2 * growth^(n-2)); the per-level and cumulative
        // thresholds must both be strictly increasing (§3.2 loader contract).
        double cumulative = 0.0;
        double previousStep = 0.0;
        double previousCumulative = 0.0;
        for (int n = 2; n <= maxLevel; n++)
        {
            double step = Math.Round(baseXp * Math.Pow(growth, n - 2));
            Assert.True(step > previousStep,
                $"xpForLevel({n}) = {step} is not greater than the previous step {previousStep}.");
            cumulative += step;
            Assert.True(cumulative > previousCumulative,
                $"Cumulative XP at level {n} ({cumulative}) is not strictly increasing.");
            previousStep = step;
            previousCumulative = cumulative;
        }
    }
}
