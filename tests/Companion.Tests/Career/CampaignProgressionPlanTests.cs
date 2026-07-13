using System.Text.Json;
using Companion.Core.Career;
using Companion.Core.Character;
using Companion.Core.Json;

namespace Companion.Tests.Career;

public sealed class CampaignProgressionPlanTests
{
    private const string HashA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string HashB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string HashC = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

    private static PinnedCampaignSeason Season(
        string packId,
        int year,
        int rounds,
        string hash = HashA) => new()
    {
        PackId = packId,
        PackVersion = "1.0.0",
        Sha256 = hash,
        Year = year,
        ChampionshipRoundCount = rounds,
    };

    [Fact]
    public void SmgpPlanPinsSeventeenSeasonsAndHasIdentityXpScale()
    {
        var plan = CampaignProgressionPlan.CreateSmgp(Season("smgp-1", 1990, rounds: 16));

        Assert.Equal(CampaignProgressionPlan.CurrentVersion, plan.Version);
        Assert.Equal(CareerExperienceModes.Smgp, plan.Mode);
        Assert.Equal(1990, plan.StartYear);
        Assert.Equal(2006, plan.EndYear);
        Assert.Equal(17, plan.TotalSeasons);
        Assert.Equal(16, plan.MasterySeason);
        Assert.Equal(17, plan.PinnedSeasonSequence.Count);
        Assert.All(plan.PinnedSeasonSequence, season => Assert.Equal("smgp-1", season.PackId));
        Assert.Equal(15_680, plan.PlannedReferenceXp);
        Assert.Equal(1, plan.XpScaleNumerator);
        Assert.Equal(1, plan.XpScaleDenominator);
        Assert.Equal(CharacterLevelProgression.Level300Max, plan.MaxLevel);
    }

    [Fact]
    public void MultiSeasonDynastyExcludesTheFinalSeasonFromItsReferenceHorizon()
    {
        var plan = CampaignProgressionPlan.Create(
            CareerExperienceModes.GrandPrixDynasty,
            startYear: 1967,
            endYear: 2020,
            [
                Season("f1-1967", 1967, rounds: 11, HashA),
                Season("f1-1968", 1968, rounds: 12, HashB),
                Season("f1-1970", 1970, rounds: 13, HashC),
            ]);

        // (11*40+340) + (12*40+340) = 1,600; the 1970 finale is deliberately excluded.
        Assert.Equal(1_600, plan.PlannedReferenceXp);
        Assert.Equal(49, plan.XpScaleNumerator);
        Assert.Equal(5, plan.XpScaleDenominator);
        Assert.Equal(2, plan.MasterySeason);
    }

    [Fact]
    public void OneSeasonDynastyIncludesItsOnlySeasonAndAvoidsDivisionByZero()
    {
        var plan = CampaignProgressionPlan.Create(
            CareerExperienceModes.GrandPrixDynasty,
            startYear: 2020,
            endYear: 2020,
            [Season("f1-2020", 2020, rounds: 17)]);

        Assert.Equal(1_020, plan.PlannedReferenceXp);
        Assert.Equal(784, plan.XpScaleNumerator);
        Assert.Equal(51, plan.XpScaleDenominator);
        Assert.Equal(1, plan.MasterySeason);
    }

    [Fact]
    public void CreateDefensivelyCopiesTheSemanticSequence()
    {
        PinnedCampaignSeason[] source = [Season("f1-2020", 2020, rounds: 17)];
        var plan = CampaignProgressionPlan.Create(
            CareerExperienceModes.GrandPrixDynasty, 2020, 2020, source);

        source[0] = Season("replacement", 2020, rounds: 1, HashB);

        Assert.Equal("f1-2020", plan.PinnedSeasonSequence[0].PackId);
    }

    [Fact]
    public void JsonRoundTripUsesStructuralPlanEqualityAndHashing()
    {
        var plan = CampaignProgressionPlan.CreateSmgp(Season("smgp-1", 1990, rounds: 16));

        string json = JsonSerializer.Serialize(plan, CoreJson.Options);
        var back = JsonSerializer.Deserialize<CampaignProgressionPlan>(json, CoreJson.Options)!;

        Assert.False(ReferenceEquals(plan.PinnedSeasonSequence, back.PinnedSeasonSequence));
        Assert.Equal(plan, back);
        Assert.Equal(plan.GetHashCode(), back.GetHashCode());
        back.Validate();
    }

    [Fact]
    public void ValidationRejectsUnknownModesAndCorruptDerivedValues()
    {
        var valid = CampaignProgressionPlan.Create(
            CareerExperienceModes.GrandPrixDynasty,
            2020,
            2020,
            [Season("f1-2020", 2020, rounds: 17)]);

        Assert.Throws<InvalidOperationException>(() => (valid with { Mode = "unknown" }).Validate());
        Assert.Throws<InvalidOperationException>(() =>
            (valid with { PlannedReferenceXp = valid.PlannedReferenceXp + 1 }).Validate());
        Assert.Throws<InvalidOperationException>(() =>
            (valid with { XpScaleNumerator = valid.XpScaleNumerator * 2 }).Validate());
        Assert.Throws<NotSupportedException>(() => (valid with { Version = 2 }).Validate());
    }

    [Fact]
    public void ValidationRejectsOutOfOrderOrUnverifiablePins()
    {
        Assert.Throws<InvalidOperationException>(() => CampaignProgressionPlan.Create(
            CareerExperienceModes.GrandPrixDynasty,
            1967,
            2020,
            [Season("f1-1967", 1967, 11), Season("f1-1966", 1966, 9)]));
        Assert.Throws<InvalidOperationException>(() => CampaignProgressionPlan.Create(
            CareerExperienceModes.GrandPrixDynasty,
            2020,
            2020,
            [Season("f1-2020", 2020, 17) with { Sha256 = "not-a-hash" }]));
    }

    [Fact]
    public void SmgpValidationRejectsAChangingOrNonstandardRoundCount()
    {
        var valid = CampaignProgressionPlan.CreateSmgp(Season("smgp-1", 1990, rounds: 16));
        var changed = valid.PinnedSeasonSequence
            .Select((season, index) => index == 8
                ? season with { ChampionshipRoundCount = 15 }
                : season)
            .ToArray();

        Assert.Throws<InvalidOperationException>(() =>
            (valid with { PinnedSeasonSequence = changed }).Validate());
        Assert.Throws<InvalidOperationException>(() =>
            CampaignProgressionPlan.CreateSmgp(Season("smgp-1", 1990, rounds: 15)));
    }
}
