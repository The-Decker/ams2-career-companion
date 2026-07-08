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
        Assert.Null(FuelGuidance.Note("F-Classic_Gen2", 70, refuellingAllowed: false));
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
}
