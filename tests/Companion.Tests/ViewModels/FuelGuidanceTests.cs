using Companion.ViewModels.Services;

namespace Companion.Tests.ViewModels;

/// <summary>The honest, qualitative fuel advisory (ams2-custom-race-reference §8): per-class
/// one-tank range vs the race length, the era "don't refuel" caveat only when refuelling is
/// explicitly disallowed, and the always-present AMS2 "set your own fuel" gotcha.</summary>
public class FuelGuidanceTests
{
    private const string Vintage = "F-Vintage_Gen1";

    [Fact]
    public void UnknownClass_HasNoProfile_ReturnsNull()
    {
        Assert.Null(FuelGuidance.Note("Not_A_Class", 70, refuellingAllowed: false));
    }

    [Fact]
    public void EveryBundledPackClass_HasAProfile()
    {
        // The 19 shipped pack classes (m2 deep pass) — each must produce a fuel note.
        string[] classes =
        [
            "F-Vintage_Gen1", "F-Vintage_Gen2", "F-Retro_Gen1", "F-Retro_Gen2", "F-Retro_Gen3",
            "F-Classic_Gen1", "F-Classic_Gen2", "F-Classic_Gen3", "F-Classic_Gen4",
            "F-Hitech_Gen1", "F-Hitech_Gen2", "FE-G1", "F-V10_Gen1", "F-V10_Gen2", "F-V10_Gen3",
            "F-V8_Gen1", "F-V8_Gen2", "F-Ultimate_Gen1", "F-Ultimate",
        ];
        foreach (string cls in classes)
            Assert.NotNull(FuelGuidance.Note(cls, 50, refuellingAllowed: null));
    }

    [Fact]
    public void RefuelEra_ShortRace_OffersNoStopOrStrategy()
    {
        // 2005 V10 at a sub-tank distance: refuelling framed as the era-authentic option.
        string? note = FuelGuidance.Note("F-V10_Gen3", laps: 44, refuellingAllowed: true);

        Assert.NotNull(note);
        Assert.Contains("Refuelling is allowed", note);
        Assert.Contains("non-stop", note);
        Assert.Contains("pit strategy", note);
    }

    [Fact]
    public void RefuelEra_LongRace_RequiresAFuelStop_NeverFillToDistance()
    {
        // 2006 V8 (conservative est. tank): a GP distance is beyond one tank — plan a stop.
        string? note = FuelGuidance.Note("F-V8_Gen1", laps: 60, refuellingAllowed: true);

        Assert.NotNull(note);
        Assert.Contains("likely needed", note);
        Assert.Contains("at least one fuel stop", note);
        Assert.DoesNotContain("full distance", note);
    }

    [Fact]
    public void ShortRace_WithinOneTank_SaysFillToDistance()
    {
        string? note = FuelGuidance.Note(Vintage, laps: 40, refuellingAllowed: false);

        Assert.NotNull(note);
        Assert.Contains("One tank", note);
        Assert.Contains("full distance", note);
        Assert.Contains("don't refuel", note); // era caveat (refuelling disallowed)
    }

    [Fact]
    public void LongRace_BeyondOneTank_RecommendsFuelSaving_NotRefuelling()
    {
        string? note = FuelGuidance.Note(Vintage, laps: 80, refuellingAllowed: false);

        Assert.NotNull(note);
        Assert.Contains("80 laps", note);
        Assert.Contains("beyond", note);
        Assert.Contains("save fuel", note);
        // Never suggests turning refuelling on for an era that didn't refuel.
        Assert.DoesNotContain("enable refuel", note!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnknownRefuellingRule_OmitsTheEraCaveat()
    {
        string? note = FuelGuidance.Note(Vintage, laps: 40, refuellingAllowed: null);

        Assert.NotNull(note);
        Assert.DoesNotContain("don't refuel", note);
    }

    [Fact]
    public void EveryNote_CarriesTheAms2SetYourOwnFuelGotcha()
    {
        Assert.Contains("set the fuel load yourself", FuelGuidance.Note(Vintage, 40, false));
        Assert.Contains("set the fuel load yourself", FuelGuidance.Note(Vintage, 80, true));
    }

    [Theory]
    [InlineData(55, "One tank")]  // at the safe boundary → fill-to-distance
    [InlineData(56, "beyond")]    // one past it → the fuel-saving warning
    public void SafeLapBoundary_SwitchesGuidanceAtFiftyFive(int laps, string expected)
    {
        Assert.Contains(expected, FuelGuidance.Note(Vintage, laps, refuellingAllowed: false));
    }

    [Fact]
    public void SafeToOneTankWindow_NeverContradictsItself()
    {
        // 59 laps sits in F-Retro_Gen3's SafeLaps(56)..OneTankLaps(59) window: the warning must
        // quote the safe range it switched on, never "59 laps is beyond the ~59-lap range".
        string? note = FuelGuidance.Note("F-Retro_Gen3", laps: 59, refuellingAllowed: false);

        Assert.NotNull(note);
        Assert.Contains("~56-lap safe range", note);
        Assert.DoesNotContain("beyond the ~59-lap", note);
    }

    [Fact]
    public void RefuelEra_SafeToOneTankWindow_QuotesTheSafeRange()
    {
        // Same window on the refuel branch: F-V10_Gen3 SafeLaps 52, OneTankLaps 55 → 53 laps.
        string? note = FuelGuidance.Note("F-V10_Gen3", laps: 53, refuellingAllowed: true);

        Assert.NotNull(note);
        Assert.Contains("likely needed", note);
        Assert.Contains("~52-lap safe range", note);
        Assert.DoesNotContain("beyond the ~55-lap", note);
    }
}
