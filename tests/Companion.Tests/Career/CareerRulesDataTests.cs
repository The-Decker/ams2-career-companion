using System.Text.RegularExpressions;
using Companion.Core.Career;

namespace Companion.Tests.Career;

/// <summary>Living validation of the shipped career rules files (data/rules/career-*.json):
/// every file must parse through its Core loader and satisfy the structural invariants the
/// sim depends on.</summary>
public class CareerRulesDataTests
{
    // ---------- career-aging-curves.json ----------

    [Fact]
    public void AgingCurvesParseAndCoverTheCareerSpan()
    {
        var curves = CareerTestData.LoadAgingCurves();

        // Every season a career can reach must resolve to exactly one era.
        for (int year = 1950; year <= 2030; year++)
        {
            var curve = curves.ForYear(year);
            Assert.InRange(curve.PeakAgeStart, 20, 40);
            Assert.True(curve.PeakAgeEnd >= curve.PeakAgeStart);
        }
    }

    [Fact]
    public void SixtiesPeakMatchesTheContract()
    {
        // Contract: peak ~28–32 in the 60s, later in modern eras.
        var sixties = CareerTestData.LoadAgingCurves().ForYear(1967);
        Assert.Equal(28, sixties.PeakAgeStart);
        Assert.Equal(32, sixties.PeakAgeEnd);

        var modern = CareerTestData.LoadAgingCurves().ForYear(2022);
        Assert.True(modern.PeakAgeEnd > sixties.PeakAgeEnd,
            "Modern era peaks must extend later than the 60s.");
    }

    [Fact]
    public void AgingCurveDeclinesMonotonicallyPastThePeak()
    {
        foreach (var curve in CareerTestData.LoadAgingCurves().Eras)
        {
            double previous = 0.0;
            for (int age = curve.PeakAgeEnd + 1; age <= curve.PeakAgeEnd + 15; age++)
            {
                double delta = curve.AnnualDelta(age);
                Assert.True(delta < 0.0, $"{curve.Key}: past-peak delta at {age} must be negative.");
                Assert.True(delta < previous,
                    $"{curve.Key}: decline must steepen with age ({age}: {delta} vs {previous}).");
                previous = delta;
            }
        }
    }

    [Fact]
    public void AgingCurveRisesBeforeThePeakAndHoldsOnIt()
    {
        var curve = CareerTestData.LoadAgingCurves().ForYear(1967);
        Assert.True(curve.AnnualDelta(curve.PeakAgeStart - 1) > 0.0);
        Assert.Equal(0.0, curve.AnnualDelta(curve.PeakAgeStart));
        Assert.Equal(0.0, curve.AnnualDelta(curve.PeakAgeEnd));
    }

    [Fact]
    public void RetirementHazardGrowsWithAgeAndFadingSkill()
    {
        var hazard = CareerTestData.LoadAgingCurves().ForYear(1967).Retirement;
        Assert.Equal(0.0, hazard.Probability(28, 0.9));
        Assert.True(hazard.Probability(40, 0.9) > hazard.Probability(36, 0.9));
        Assert.True(hazard.Probability(36, 0.4) > hazard.Probability(36, 0.9));
        Assert.InRange(hazard.Probability(70, 0.0), 0.0, 0.95);
    }

    [Fact]
    public void GoldenAgeHazardLetsFrontLinersRacePastForty()
    {
        // M5 audit fix: Fangio was champion at 46, Brabham won at 44 — the 60s hazard starts
        // at 35 and accrues ~0.07/year, so a front-line 40-year-old more often stays than goes.
        var hazard = CareerTestData.LoadAgingCurves().ForYear(1967).Retirement;
        Assert.Equal(35, hazard.BaseAge);
        Assert.Equal(0.07, hazard.PerYearOverBase, 12);
        Assert.True(hazard.Probability(40, 0.75) < 0.5,
            $"A sharp 40-year-old must be more likely to stay than retire ({hazard.Probability(40, 0.75)}).");

        // Surviving 40 through 44 (a Brabham arc) must be plausible, not a statistical freak.
        double survival = 1.0;
        for (int age = 40; age <= 44; age++)
            survival *= 1.0 - hazard.Probability(age, 0.75);
        Assert.True(survival > 0.02,
            $"Racing from 40 through 44 at the front must be plausible (survival {survival:0.###}).");
    }

    // ---------- career-team-archetypes.json ----------

    [Fact]
    public void ArchetypesParseWithTheThreeContractArchetypes()
    {
        var catalog = CareerTestData.LoadArchetypes();
        Assert.Contains("works", catalog.Archetypes.Keys);
        Assert.Contains("privateer", catalog.Archetypes.Keys);
        Assert.Contains("minnow", catalog.Archetypes.Keys);
        Assert.True(catalog.MaxOffers >= 1);

        // Every tier must resolve to a default archetype, a rep floor, and a salary band.
        for (int tier = 1; tier <= 5; tier++)
        {
            Assert.NotNull(catalog.ForTeam(tier, null));
            Assert.True(catalog.RepFloor(tier) >= 0.0);
            Assert.True(catalog.SalaryOffer(tier, 50.0) > 0.0);
        }
    }

    [Fact]
    public void RicherTiersPayMoreAndDemandMoreReputation()
    {
        var catalog = CareerTestData.LoadArchetypes();
        for (int tier = 2; tier <= 5; tier++)
        {
            Assert.True(catalog.SalaryOffer(tier, 50.0) >= catalog.SalaryOffer(tier - 1, 50.0),
                $"Tier {tier} must pay at least tier {tier - 1}.");
            Assert.True(catalog.RepFloor(tier) >= catalog.RepFloor(tier - 1),
                $"Tier {tier} must gate at least as hard as tier {tier - 1}.");
        }
    }

    [Fact]
    public void SalariesStayWithinBudgetUnitScale()
    {
        // Top team ≈ 100 BU/season (research): even a superstar's salary must stay single/low
        // double digit BU.
        var catalog = CareerTestData.LoadArchetypes();
        Assert.InRange(catalog.SalaryOffer(5, 100.0), 1.0, 20.0);
        Assert.InRange(catalog.SalaryOffer(1, 0.0), 0.0, 2.0);
    }

    [Fact]
    public void HigherReputationNeverScoresWorse_AllArchetypes()
    {
        var catalog = CareerTestData.LoadArchetypes();
        foreach (var (name, archetype) in catalog.Archetypes)
        {
            double low = TeamArchetypeCatalog.OfferScore(archetype, 40.0, 1.0, 2.0, 4.0, 0.0);
            double high = TeamArchetypeCatalog.OfferScore(archetype, 80.0, 1.0, 2.0, 4.0, 0.0);
            Assert.True(high > low, $"Archetype '{name}' must reward reputation.");
        }
    }

    // ---------- career-headline-templates.json ----------

    private static readonly string[] RequiredKeys =
    [
        "race.result|win",
        "race.result|podium",
        "race.result|points",
        "race.result|overperformed",
        "race.result|underperformed",
        "race.result|dnf-mechanical",
        "race.result|dnf-driver-error",
        "race.result|midfield",
        "driver.retirement|canon",
        "driver.retirement|age-performance",
        "driver.retirement.foreshadow|considering-future",
        "team.tier|promoted",
        "team.tier|relegated",
        "season.digest|season-complete",
    ];

    [Fact]
    public void HeadlineBankParsesWithEveryRequiredKey()
    {
        var bank = CareerTestData.LoadHeadlines();
        foreach (string key in RequiredKeys)
            Assert.True(bank.Templates.ContainsKey(key), $"Headline bank is missing '{key}'.");
    }

    [Fact]
    public void SixtiesFlavorIsRealNotToken()
    {
        // Contract: modest but real — 6–10 variants per key event type, 1960s flavor first.
        var bank = CareerTestData.LoadHeadlines();
        foreach (string key in RequiredKeys)
        {
            string[] parts = key.Split('|');
            var sixties = bank.Variants(parts[0], parts[1], 1967);
            Assert.True(sixties.Count >= 6, $"'{key}' needs at least 6 1960s variants, has {sixties.Count}.");
            var fallback = bank.Variants(parts[0], parts[1], 2050);
            Assert.True(fallback.Count >= 1, $"'{key}' needs a default fallback.");
        }
    }

    [Fact]
    public void EveryVariantUsesOnlyDocumentedTokens()
    {
        string[] known = ["player", "team", "race", "position", "year", "driver", "champion"];
        var bank = CareerTestData.LoadHeadlines();
        foreach (var (key, byEra) in bank.Templates)
        {
            foreach (var variants in byEra.Values)
            {
                foreach (string variant in variants)
                {
                    foreach (Match match in Regex.Matches(variant, @"\{([a-zA-Z]+)\}"))
                    {
                        Assert.True(known.Contains(match.Groups[1].Value),
                            $"'{key}' variant uses unknown token {{{match.Groups[1].Value}}}: {variant}");
                    }
                }
            }
        }
    }
}
