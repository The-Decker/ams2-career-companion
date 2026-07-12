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
    public void LightShunt_AlmostNeverHurts()
    {
        Assert.Equal(AccidentOutcomeKind.None, Resolve(AccidentSeverity.Light, 1).Kind);
        Assert.Equal(AccidentOutcomeKind.None, Resolve(AccidentSeverity.Light, 480).Kind);
        // Only the single top unit kills on a light shunt (0.2%).
        Assert.Equal(AccidentOutcomeKind.Death, Resolve(AccidentSeverity.Light, 500).Kind);
    }

    [Fact]
    public void LightMinorInjuryBands_MissTheRightNumberOfRaces()
    {
        var miss1 = Resolve(AccidentSeverity.Light, 481);
        Assert.Equal(AccidentOutcomeKind.MinorInjury, miss1.Kind);
        Assert.Equal(1, miss1.MissRaces);

        var miss2 = Resolve(AccidentSeverity.Light, 497);
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
        // escapes the death ceiling — the effective roll stays inside [1, 500].
        var superSafe = new PlayerPerkModifiers { InjuryDurabilityDelta = 100.0 };
        var superReckless = new PlayerPerkModifiers { InjuryDurabilityDelta = -100.0 };

        var safe = Resolve(AccidentSeverity.Heavy, 500, superSafe);
        Assert.Equal(1, safe.EffectiveRoll);
        Assert.Equal(AccidentOutcomeKind.None, safe.Kind);

        var doomed = Resolve(AccidentSeverity.Light, 1, superReckless);
        Assert.Equal(500, doomed.EffectiveRoll);
        Assert.Equal(AccidentOutcomeKind.Death, doomed.Kind);
    }
}
