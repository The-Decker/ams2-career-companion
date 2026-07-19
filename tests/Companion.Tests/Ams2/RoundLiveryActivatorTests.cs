using Companion.Ams2.Skins;

namespace Companion.Tests.Ams2;

/// <summary>
/// Per-race skin activation (SMGP, Mike: "make sure EVERY CAR CAN SWITCH OFF AND ON FROM ACTIVE TO
/// INACTIVE PERFECTLY"). At staging the round's ≤26 qualifier liveries must be switched ON (a real
/// numeric slot) and every other pack livery parked OFF (a placeholder), per model override file,
/// cap-safe and idempotent, so the smart binder keeps all 26 grid cars' real SMGP paint instead of
/// flooring the installed-but-inactive ones to base-game (which AMS2 pool-fills). These pin the pure
/// per-file planner (<see cref="RoundLiveryActivator.PlanFile"/>) that carries all the activation
/// logic; the file wrapper is thin I/O over it.
/// </summary>
public sealed class RoundLiveryActivatorTests
{
    // A model override file: Senna active, Blume installed-but-INACTIVE (X6), a reserve car active
    // (will DNQ this round), and a base-game livery that is NOT part of the pack.
    private const string Xml =
        "<USER_OVERRIDES>\n" +
        "  <LIVERY_OVERRIDE LIVERY=\"51\" NAME=\"Madonna #1 A. Senna\" BASELIVERY=\"Default\"></LIVERY_OVERRIDE>\n" +
        "  <LIVERY_OVERRIDE LIVERY=\"X6\" NAME=\"Bestowal #8 M. Blume\" BASELIVERY=\"Default\"></LIVERY_OVERRIDE>\n" +
        "  <LIVERY_OVERRIDE LIVERY=\"52\" NAME=\"Comet #29 E. Tornio\" BASELIVERY=\"Default\"></LIVERY_OVERRIDE>\n" +
        "  <LIVERY_OVERRIDE LIVERY=\"53\" NAME=\"Team Hornsey #14\" BASELIVERY=\"Default\"></LIVERY_OVERRIDE>\n" +
        "</USER_OVERRIDES>\n";

    private static readonly HashSet<string> Pack = new(StringComparer.Ordinal)
    {
        "Madonna #1 A. Senna", "Bestowal #8 M. Blume", "Comet #29 E. Tornio",
        // "Team Hornsey #14" is a BASE-GAME livery, deliberately NOT in the pack set.
    };

    [Fact]
    public void Switches_on_inactive_qualifiers_and_parks_pack_non_qualifiers()
    {
        // This round fields Senna + Blume; Tornio pre-qualified out.
        var round = new HashSet<string>(StringComparer.Ordinal) { "Madonna #1 A. Senna", "Bestowal #8 M. Blume" };

        var plan = RoundLiveryActivator.PlanFile(Xml, round, Pack, maxSlot: 76);

        Assert.True(plan.Changed);
        Assert.Equal(1, plan.Activated);    // Blume X6 -> a numeric slot
        Assert.Equal(1, plan.Deactivated);  // Tornio 52 -> ##
        Assert.Empty(plan.Skipped);

        var after = LiveryOverrideWriter.Liveries(plan.Xml).ToDictionary(l => l.Name, l => l.Active, StringComparer.Ordinal);
        Assert.True(after["Madonna #1 A. Senna"]);       // already active, kept
        Assert.True(after["Bestowal #8 M. Blume"]);       // now active
        Assert.False(after["Comet #29 E. Tornio"]);       // parked (DNQ this round)
        Assert.True(after["Team Hornsey #14"]);           // base-game livery, NEVER touched
    }

    [Fact]
    public void A_base_game_livery_is_never_parked_even_when_not_in_the_round()
    {
        // Only Senna races; the pack's Blume/Tornio park, but the base-game "Team Hornsey #14"
        // (not in the pack set) stays exactly as it was, we only manage the pack's own liveries.
        var round = new HashSet<string>(StringComparer.Ordinal) { "Madonna #1 A. Senna" };

        var plan = RoundLiveryActivator.PlanFile(Xml, round, Pack, maxSlot: 76);

        Assert.Equal(0, plan.Activated);
        Assert.Equal(1, plan.Deactivated); // only Tornio (Blume was already inactive)
        var after = LiveryOverrideWriter.Liveries(plan.Xml).ToDictionary(l => l.Name, l => l.Active, StringComparer.Ordinal);
        Assert.True(after["Team Hornsey #14"]);
    }

    [Fact]
    public void Idempotent_a_restage_of_the_same_round_writes_nothing()
    {
        var round = new HashSet<string>(StringComparer.Ordinal) { "Madonna #1 A. Senna", "Bestowal #8 M. Blume" };

        var first = RoundLiveryActivator.PlanFile(Xml, round, Pack, maxSlot: 76);
        var second = RoundLiveryActivator.PlanFile(first.Xml, round, Pack, maxSlot: 76);

        Assert.True(first.Changed);
        Assert.False(second.Changed); // already matches the round, no churn on re-stage
        Assert.Equal(0, second.Activated);
        Assert.Equal(0, second.Deactivated);
    }

    [Fact]
    public void Deactivations_free_slots_so_activation_stays_under_the_cap()
    {
        // A cap-tight file: slots 51 and 52 are the only ones the class allows (maxSlot 52). Blume is
        // off (X6); Tornio holds slot 52 but DNQs. Parking Tornio FIRST frees 52 for Blume.
        var round = new HashSet<string>(StringComparer.Ordinal) { "Madonna #1 A. Senna", "Bestowal #8 M. Blume" };

        var plan = RoundLiveryActivator.PlanFile(Xml, round, Pack, maxSlot: 52);

        Assert.Empty(plan.Skipped);                // Blume fit into the slot Tornio vacated
        Assert.Equal(1, plan.Activated);
        Assert.Equal(1, plan.Deactivated);
        var after = LiveryOverrideWriter.Liveries(plan.Xml).ToDictionary(l => l.Name, l => l.Active, StringComparer.Ordinal);
        Assert.True(after["Bestowal #8 M. Blume"]);
        Assert.False(after["Comet #29 E. Tornio"]);
    }

    [Fact]
    public void A_qualifier_that_cannot_fit_the_cap_is_skipped_not_forced()
    {
        // Everyone races (nothing to park) but the file is already full to maxSlot 52, so Blume can't
        // be switched on, it is reported skipped (it floors to base-game downstream and still loads).
        var round = new HashSet<string>(StringComparer.Ordinal)
        {
            "Madonna #1 A. Senna", "Bestowal #8 M. Blume", "Comet #29 E. Tornio",
        };

        var plan = RoundLiveryActivator.PlanFile(Xml, round, Pack, maxSlot: 52);

        Assert.Contains("Bestowal #8 M. Blume", plan.Skipped);
        Assert.Equal(0, plan.Activated);
        Assert.Equal(0, plan.Deactivated);
    }
}
