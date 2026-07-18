using System.Text.Json;
using Companion.Core.Career;
using Companion.Core.Dynasty;
using Companion.Core.Numerics;
using Companion.Tests.Career;

namespace Companion.Tests.Dynasty;

/// <summary>The shipped Dynasty economy tables parse, validate, and resolve exactly, every
/// money helper is exact-rational (no drift), era scaling clamps sanely, and the authored
/// sponsor board covers every slot tier across the whole 1950–2029 product horizon.</summary>
public sealed class DynastyEconomyRulesTests
{
    private static DynastyEconomyRules Load() =>
        DynastyEconomyRules.Load(CareerTestData.RulesDirectory)!;

    [Fact]
    public void ShippedFile_ParsesAtTheCurrentSchemaVersion()
    {
        var rules = Load();
        Assert.Equal(DynastyEconomyRules.CurrentSchemaVersion, rules.SchemaVersion);
    }

    [Theory]
    [InlineData(1949, "1")]  // clamps below the first band
    [InlineData(1967, "1")]
    [InlineData(1974, "3")]
    [InlineData(1988, "10")]
    [InlineData(1995, "30")]
    [InlineData(2005, "80")]
    [InlineData(2016, "150")]
    [InlineData(2030, "150")] // clamps above the last band
    public void EraIndex_ResolvesAndClamps(int year, string expected)
    {
        Assert.Equal(Rational.Parse(expected), Load().IndexForYear(year));
    }

    [Fact]
    public void RacePrize_ScalesByPositionAndEra()
    {
        var rules = Load();
        Assert.Equal(Rational.Parse("10000"), rules.RacePrize(1, 1967));
        Assert.Equal(Rational.Parse("30000"), rules.RacePrize(1, 1974)); // ×3 in the 70s
        Assert.Equal(Rational.Parse("250"), rules.RacePrize(12, 1967)); // beyond the table
        Assert.Equal(Rational.Zero, rules.RacePrize(null, 1967)); // DNF earns nothing
    }

    [Fact]
    public void SeasonPrize_ScalesByConstructorPositionAndEra()
    {
        var rules = Load();
        Assert.Equal(Rational.Parse("40000"), rules.SeasonPrize(1, 1967));
        Assert.Equal(Rational.Parse("3000"), rules.SeasonPrize(9, 1967)); // beyond the table
        Assert.Equal(Rational.Parse("540000"), rules.SeasonPrize(3, 1995)); // 18000 × 30
        Assert.Equal(Rational.Zero, rules.SeasonPrize(null, 1967));
    }

    [Fact]
    public void DevelopmentCost_EscalatesExactly()
    {
        var rules = Load();
        Assert.Equal(Rational.Parse("8000"), rules.DevelopmentCost(0, 0, 1967));
        Assert.Equal(Rational.Parse("10800"), rules.DevelopmentCost(1, 0, 1967)); // ×27/20
        Assert.Equal(Rational.Parse("14580"), rules.DevelopmentCost(2, 0, 1967)); // ×(27/20)²
        // Staff tier 2 discounts by 2/12: 8000 × 5/6 = 20000/3, EXACT, no rounding.
        Assert.Equal(new Rational(20000, 3), rules.DevelopmentCost(0, 2, 1967));
        // Era-scaled: the same increment in the 80s costs ×10.
        Assert.Equal(Rational.Parse("80000"), rules.DevelopmentCost(0, 0, 1983));
    }

    [Fact]
    public void DevelopmentCarryover_FloorsTheFraction()
    {
        var rules = Load();
        Assert.Equal(0, rules.DevelopmentCarryoverLevel(0));
        Assert.Equal(0, rules.DevelopmentCarryoverLevel(1)); // floor(1/2)
        Assert.Equal(2, rules.DevelopmentCarryoverLevel(5)); // floor(5/2)
        Assert.Equal(4, rules.DevelopmentCarryoverLevel(8));
    }

    [Fact]
    public void PerRoundAccrual_SumsBackExactly()
    {
        var perRound = DynastyEconomyRules.PerRound(Rational.Parse("1000"), 3);
        Assert.Equal(new Rational(1000, 3), perRound);
        Assert.Equal(Rational.Parse("1000"), perRound + perRound + perRound); // zero drift
    }

    [Fact]
    public void PlayerRepair_BillsByAccidentSeverityThenDnfCause()
    {
        var rules = Load();
        Assert.Equal(Rational.Parse("1500"), rules.PlayerRepair(AccidentSeverity.Light, DnfCause.DriverError, 1967));
        Assert.Equal(Rational.Parse("4000"), rules.PlayerRepair(AccidentSeverity.Medium, null, 1967));
        Assert.Equal(Rational.Parse("9000"), rules.PlayerRepair(AccidentSeverity.Heavy, null, 1967));
        Assert.Equal(Rational.Parse("2000"), rules.PlayerRepair(null, DnfCause.Mechanical, 1967));
        Assert.Equal(Rational.Parse("2500"), rules.PlayerRepair(null, DnfCause.DriverError, 1967));
        Assert.Equal(Rational.Zero, rules.PlayerRepair(null, null, 1967)); // a clean finish
        Assert.Equal(Rational.Parse("90000"), rules.PlayerRepair(AccidentSeverity.Heavy, null, 1988)); // ×10
    }

    [Fact]
    public void SponsorBoard_ResolvesByIdAndRejectsUnknown()
    {
        var rules = Load();
        var deal = rules.SponsorById("sponsor.imperial-petroleum");
        Assert.NotNull(deal);
        Assert.Equal(DynastySponsorRules.TitleSlot, deal!.TierSlot);
        Assert.Null(rules.SponsorById("sponsor.does-not-exist"));
    }

    [Fact]
    public void SponsorBoard_CoversEverySlotTierAcrossTheWholeHorizon()
    {
        var rules = Load();
        foreach (string slot in new[]
        {
            DynastySponsorRules.TitleSlot, DynastySponsorRules.MajorSlot, DynastySponsorRules.MinorSlot,
        })
        {
            for (int year = 1950; year <= 2029; year++)
            {
                Assert.True(
                    rules.Sponsors.Board.Any(d =>
                        d.TierSlot == slot && d.FromYear <= year && year <= d.ToYear),
                    $"No '{slot}' sponsor is signable in {year}, the board has a coverage gap.");
            }
        }
    }

    [Fact]
    public void SlotCounts_AreAuthoredForEveryTier()
    {
        var rules = Load();
        Assert.Equal(1, rules.SlotsFor(DynastySponsorRules.TitleSlot));
        Assert.Equal(2, rules.SlotsFor(DynastySponsorRules.MajorSlot));
        Assert.Equal(3, rules.SlotsFor(DynastySponsorRules.MinorSlot));
        Assert.Equal(0, rules.SlotsFor("unknown"));
    }

    [Fact]
    public void StartingFunds_ScaleByTierAndEra()
    {
        var rules = Load();
        Assert.Equal(Rational.Parse("100000"), rules.StartingFunds(5, 1967));
        Assert.Equal(Rational.Parse("15000"), rules.StartingFunds(1, 1967));
        Assert.Equal(Rational.Parse("135000"), rules.StartingFunds(3, 1974)); // 45000 × 3
        Assert.Equal(rules.StartingFunds(1, 1967), rules.StartingFunds(0, 1967)); // tier clamps
    }

    [Fact]
    public void HardFloor_IsANegativeEraScaledOverdraft()
    {
        var rules = Load();
        Assert.True(rules.HardFloor(1967) < Rational.Zero);
        Assert.Equal(rules.HardFloor(1967) * 10, rules.HardFloor(1985));
        Assert.True(rules.Bankruptcy.GraceRounds >= 1);
    }

    // ---------- validation failures (mutations of the shipped file) ----------

    private static string ShippedJson() =>
        File.ReadAllText(Path.Combine(CareerTestData.RulesDirectory, "dynasty", "economy.json"));

    [Fact]
    public void UnsupportedSchemaVersion_Throws()
    {
        string mutated = ShippedJson().Replace("\"schemaVersion\": 1", "\"schemaVersion\": 99");
        var ex = Assert.Throws<JsonException>(() => DynastyEconomyRules.Parse(mutated));
        Assert.Contains("schema version 99", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EraBandGap_Throws()
    {
        string mutated = ShippedJson().Replace(
            "{ \"fromYear\": 1970, \"toYear\": 1979, \"index\": \"3\" }",
            "{ \"fromYear\": 1971, \"toYear\": 1979, \"index\": \"3\" }");
        var ex = Assert.Throws<JsonException>(() => DynastyEconomyRules.Parse(mutated));
        Assert.Contains("contiguous", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ZeroGraceRounds_Throws()
    {
        string mutated = ShippedJson().Replace("\"graceRounds\": 4", "\"graceRounds\": 0");
        var ex = Assert.Throws<JsonException>(() => DynastyEconomyRules.Parse(mutated));
        Assert.Contains("grace", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PositiveHardFloor_Throws()
    {
        string mutated = ShippedJson().Replace("\"hardFloor\": \"-25000\"", "\"hardFloor\": \"25000\"");
        var ex = Assert.Throws<JsonException>(() => DynastyEconomyRules.Parse(mutated));
        Assert.Contains("hard floor", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DuplicateSponsorId_Throws()
    {
        string mutated = ShippedJson().Replace("sponsor.hexa-parts", "sponsor.apex-lubricants");
        var ex = Assert.Throws<JsonException>(() => DynastyEconomyRules.Parse(mutated));
        Assert.Contains("duplicated", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingStartingFundsTier_Throws()
    {
        string mutated = ShippedJson().Replace("\"3\": \"45000\",", "");
        var ex = Assert.Throws<JsonException>(() => DynastyEconomyRules.Parse(mutated));
        Assert.Contains("tier 3", ex.Message, StringComparison.Ordinal);
    }
}
