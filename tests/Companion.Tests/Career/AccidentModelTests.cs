using Companion.Core.Career;
using Companion.Core.Character;

namespace Companion.Tests.Career;

/// <summary>
/// The PURE accident band resolver (character death &amp; injury §3.2–§3.4): a d500 roll + severity + safety
/// profile → none/injury/season-ending/death. Higher effective roll = more dangerous; a protective safety
/// profile shifts the effective roll DOWN toward the safe bands, a reckless one UP toward death.
/// </summary>
public sealed class AccidentModelTests
{
    private static readonly AccidentRules Rules = AccidentModel.DefaultRules;
    private const double Durability = 0.5; // neutral → Identity mods give a zero offset

    private static AccidentOutcome Resolve(AccidentSeverity severity, int roll, PlayerPerkModifiers? mods = null) =>
        AccidentModel.Resolve(severity, roll, Durability, mods ?? PlayerPerkModifiers.Identity, Rules);

    [Fact]
    public void LightShunt_IsMostlyHarmless_AndNeverFatalOrSeasonEnding()
    {
        // Decision B (2026-07-12): a light crash is MOSTLY HARMLESS, none for the vast majority, at worst a
        // single missed race, and NEVER a death or a season-ender, even on the very top roll.
        Assert.Equal(AccidentOutcomeKind.None, Resolve(AccidentSeverity.Light, 1).Kind);
        Assert.Equal(AccidentOutcomeKind.None, Resolve(AccidentSeverity.Light, 490).Kind);

        var top = Resolve(AccidentSeverity.Light, 500);
        Assert.Equal(AccidentOutcomeKind.MinorInjury, top.Kind);
        Assert.Equal(1, top.MissRaces);
    }

    [Fact]
    public void MediumMinorInjuryBands_MissTheRightNumberOfRaces()
    {
        var miss1 = Resolve(AccidentSeverity.Medium, 411);
        Assert.Equal(AccidentOutcomeKind.MinorInjury, miss1.Kind);
        Assert.Equal(1, miss1.MissRaces);

        var miss2 = Resolve(AccidentSeverity.Medium, 471);
        Assert.Equal(AccidentOutcomeKind.MinorInjury, miss2.Kind);
        Assert.Equal(2, miss2.MissRaces);
    }

    [Fact]
    public void HeavyShunt_IsDeadlierThanLight_ForTheSameRoll()
    {
        // A mid roll is harmless on a light shunt but a real injury on a heavy one.
        Assert.Equal(AccidentOutcomeKind.None, Resolve(AccidentSeverity.Light, 400).Kind);
        Assert.Equal(AccidentOutcomeKind.MinorInjury, Resolve(AccidentSeverity.Heavy, 400).Kind);

        Assert.Equal(AccidentOutcomeKind.SeasonEnding, Resolve(AccidentSeverity.Medium, 495).Kind);
        Assert.Equal(AccidentOutcomeKind.Death, Resolve(AccidentSeverity.Medium, 500).Kind);
    }

    [Fact]
    public void SafetyProfile_ShiftsTheOutcome_ProtectiveSafer_RecklessDeadlier()
    {
        // Same heavy roll (470): a protective profile survives with a minor injury, the neutral driver's
        // season ends, and a reckless (glass-cannon-like) profile is killed. This is the §3.4 promise that
        // a glass_cannon heavy shunt is meaningfully deadlier than an ironman's.
        var protective = new PlayerPerkModifiers { InjuryDurabilityDelta = 0.25 };  // ironman-like
        var reckless = new PlayerPerkModifiers { InjuryDurabilityDelta = -0.25 };   // glass-cannon-like

        Assert.Equal(AccidentOutcomeKind.MinorInjury, Resolve(AccidentSeverity.Heavy, 470, protective).Kind);
        Assert.Equal(AccidentOutcomeKind.SeasonEnding, Resolve(AccidentSeverity.Heavy, 470).Kind);
        Assert.Equal(AccidentOutcomeKind.Death, Resolve(AccidentSeverity.Heavy, 470, reckless).Kind);
    }

    [Fact]
    public void RecklessBaseAdd_ShiftsTowardDanger()
    {
        // A hot_head-style unconditional injury baseAdd (0.05) pushes the effective roll up by
        // 0.05 * 200 = 10 units, tipping a season-ending heavy shunt into a fatal one.
        var hotHead = new PlayerPerkModifiers { InjuryBaseAdd = 0.05 };
        Assert.Equal(AccidentOutcomeKind.SeasonEnding, Resolve(AccidentSeverity.Heavy, 480).Kind);       // effective 480
        Assert.Equal(AccidentOutcomeKind.Death, Resolve(AccidentSeverity.Heavy, 480, hotHead).Kind);    // effective 490
    }

    [Fact]
    public void SafetyOffset_IsQuantizedAndDurabilityDriven()
    {
        // Neutral durability → zero shift; max durability → a strong safe shift; the offset is an integer.
        Assert.Equal(0, AccidentModel.SafetyOffset(0.5, PlayerPerkModifiers.Identity, Rules));
        Assert.Equal(40, AccidentModel.SafetyOffset(1.0, PlayerPerkModifiers.Identity, Rules));   // (1.0-0.5)*80
        Assert.Equal(-40, AccidentModel.SafetyOffset(0.0, PlayerPerkModifiers.Identity, Rules));  // (0.0-0.5)*80
    }

    [Fact]
    public void EffectiveRoll_IsClampedIntoRange()
    {
        // A hugely protective driver can never exceed the safe floor; a hugely reckless one never
        // escapes the death ceiling, the effective roll stays inside [1, 500].
        var superSafe = new PlayerPerkModifiers { InjuryDurabilityDelta = 100.0 };
        var superReckless = new PlayerPerkModifiers { InjuryDurabilityDelta = -100.0 };

        var safe = Resolve(AccidentSeverity.Heavy, 500, superSafe);
        Assert.Equal(1, safe.EffectiveRoll);
        Assert.Equal(AccidentOutcomeKind.None, safe.Kind);

        // A heavy shunt still has a death ceiling the clamp lands on (a light one no longer does).
        var doomed = Resolve(AccidentSeverity.Heavy, 1, superReckless);
        Assert.Equal(500, doomed.EffectiveRoll);
        Assert.Equal(AccidentOutcomeKind.Death, doomed.Kind);
    }

    // ---------- distribution-level "the spread feels right" guard (open decision B) ----------

    /// <summary>Realistic safety archetypes at their true perks.json magnitudes (durabilityDelta / baseAdd),
    /// paired with a plausible base durability.</summary>
    private static readonly (string Name, double Durability, PlayerPerkModifiers Mods)[] Archetypes =
    [
        ("ironman",      0.60, new PlayerPerkModifiers { InjuryDurabilityDelta = 0.25 }),
        ("iron_const",   0.55, new PlayerPerkModifiers { InjuryDurabilityDelta = 0.20 }),
        ("average",      0.50, PlayerPerkModifiers.Identity),
        ("hot_head",     0.50, new PlayerPerkModifiers { InjuryBaseAdd = 0.05 }),
        ("glass_cannon", 0.40, new PlayerPerkModifiers { InjuryDurabilityDelta = -0.20 }),
        ("injury_prone", 0.40, new PlayerPerkModifiers { InjuryDurabilityDelta = -0.30 }),
    ];

    private static int OutcomeCount(
        AccidentSeverity severity, double durability, PlayerPerkModifiers mods, AccidentOutcomeKind kind)
    {
        int n = 0;
        for (int roll = 1; roll <= 500; roll++)
            if (AccidentModel.Resolve(severity, roll, durability, mods, Rules).Kind == kind)
                n++;
        return n;
    }

    [Fact]
    public void TheSpread_IsOrderedAcrossSafetyProfiles_DeathsRare_HeavyDeadlierThanLight()
    {
        // The §3.4 promise at the DISTRIBUTION level (sweeping all 500 rolls): a fragile build dies from a
        // heavy shunt meaningfully more than an average driver, who dies more than an ironman (death-proof
        // from a heavy shunt); deaths stay RARE even for an average heavy shunt; and a heavy shunt is never
        // safer than a light one. Encodes the design INVARIANTS (not exact odds), so a sane retune of the
        // tunable perks.json bands passes but an inversion of the intended ordering fails.
        int Death(string name)
        {
            var a = Archetypes.First(x => x.Name == name);
            return OutcomeCount(AccidentSeverity.Heavy, a.Durability, a.Mods, AccidentOutcomeKind.Death);
        }

        int ironman = Death("ironman"), average = Death("average"),
            glass = Death("glass_cannon"), prone = Death("injury_prone");

        Assert.True(ironman <= average, "an ironman heavy shunt must be no deadlier than an average driver's");
        Assert.Equal(0, ironman);                                    // in fact death-proof from a heavy shunt
        Assert.InRange(average, 1, 24);                              // deaths are RARE even here (< ~5%)
        Assert.True(glass > average, "a glass_cannon heavy shunt must be deadlier than an average driver's");
        Assert.True(prone >= glass, "injury_prone must be the most fragile of all");

        // Decision B: a LIGHT crash is mostly harmless, it NEVER kills (or ends a season), even for the most
        // fragile build on the worst roll (the safety-offset clamp can at most cost them one race). And a
        // heavier shunt is never safer than a lighter one, for every profile.
        foreach (var a in Archetypes)
        {
            Assert.Equal(0, OutcomeCount(AccidentSeverity.Light, a.Durability, a.Mods, AccidentOutcomeKind.Death));
            Assert.Equal(0, OutcomeCount(AccidentSeverity.Light, a.Durability, a.Mods, AccidentOutcomeKind.SeasonEnding));
            Assert.True(
                OutcomeCount(AccidentSeverity.Heavy, a.Durability, a.Mods, AccidentOutcomeKind.Death)
                    >= OutcomeCount(AccidentSeverity.Light, a.Durability, a.Mods, AccidentOutcomeKind.Death),
                $"a heavy shunt must be at least as deadly as a light one for {a.Name}");
        }
    }
}
