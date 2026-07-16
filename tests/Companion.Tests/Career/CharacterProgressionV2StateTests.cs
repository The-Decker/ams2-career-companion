using System.Text.Json;
using Companion.Core.Career;
using Companion.Core.Character;
using Companion.Core.Json;

namespace Companion.Tests.Career;

public sealed class CharacterProgressionV2StateTests
{
    private const string PackHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private static CampaignProgressionPlan Plan() => CampaignProgressionPlan.CreateSmgp(new PinnedCampaignSeason
    {
        PackId = "smgp-1",
        PackVersion = "1.0.0",
        Sha256 = PackHash,
        Year = 1990,
        ChampionshipRoundCount = 16,
    });

    private static CampaignProgressionPlan FractionalPlan() => CampaignProgressionPlan.Create(
        CareerExperienceModes.GrandPrixDynasty,
        1967,
        2020,
        [
            new PinnedCampaignSeason
            {
                PackId = "f1-1967", PackVersion = "1.0.0", Sha256 = new string('a', 64),
                Year = 1967, ChampionshipRoundCount = 11,
            },
            new PinnedCampaignSeason
            {
                PackId = "f1-1968", PackVersion = "1.0.0", Sha256 = new string('b', 64),
                Year = 1968, ChampionshipRoundCount = 12,
            },
            new PinnedCampaignSeason
            {
                PackId = "f1-2020", PackVersion = "1.0.0", Sha256 = new string('c', 64),
                Year = 2020, ChampionshipRoundCount = 17,
            },
        ]);

    private static CharacterProfile Profile() => new()
    {
        Name = "Zeroforce",
        CountryCode = "BRA",
        Age = 23,
        Stats = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["pace"] = 0.7,
            ["oneLap"] = 0.65,
            ["craft"] = 0.6,
            ["racecraft"] = 0.62,
            ["adaptability"] = 0.58,
            ["marketability"] = 0.5,
            ["durability"] = 0.55,
        },
        PerkIds = ["rain_man"],
        CreationPerkIds = ["rain_man"],
        ProgressionVersion = CharacterLevelProgression.Level300Version,
        RacingDnaId = "dna_circuit_specialist",
        RacingDnaVersion = 1,
        RacingDnaChoice = "technical",
        CreationBaseline = new CharacterCreationBaseline
        {
            Stats = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["oneLap"] = 0.65,
                ["pace"] = 0.7,
                ["craft"] = 0.6,
                ["racecraft"] = 0.62,
                ["adaptability"] = 0.58,
            },
            Meta = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["durability"] = 0.55,
                ["marketability"] = 0.5,
            },
            TraitIds = ["rain_man"],
        },
        AcquiredSkillIds = ["pace.launch-control"],
        AcquiredAttributeNodeIds = ["attribute.pace.1"],
        SkillPointsSpent = 3,
        XpSpentOnResets = 120,
        SkillResetCount = 1,
    };

    private static CharacterProfile NewProfile() => Profile() with
    {
        AcquiredSkillIds = null,
        AcquiredAttributeNodeIds = null,
        SkillPointsSpent = 0,
        XpSpentOnResets = 0,
        SkillResetCount = 0,
        MasteryEffectsVersion = CharacterProfile.CurrentMasteryEffectsVersion,
        ExpectationModelVersion = CharacterProfile.CurrentExpectationModelVersion,
    };

    [Fact]
    public void LegacyStateStillOmitsEveryVersionTwoKey()
    {
        var state = new PlayerCareerState
        {
            Reputation = 40,
            Opi = 0,
            PaceAnchor = 0,
            SeasonsCompleted = 0,
        };

        string json = JsonSerializer.Serialize(state, CoreJson.Options);

        Assert.DoesNotContain("experienceMode", json);
        Assert.DoesNotContain("campaignProgressionPlan", json);
        Assert.DoesNotContain("xpScaleRemainder", json);

        string profileJson = JsonSerializer.Serialize(new CharacterProfile
        {
            Stats = new Dictionary<string, double>(StringComparer.Ordinal) { ["pace"] = 0.5 },
            PerkIds = [],
        }, CoreJson.Options);
        Assert.DoesNotContain("racingDna", profileJson);
        Assert.DoesNotContain("creationBaseline", profileJson);
        Assert.DoesNotContain("acquiredSkillIds", profileJson);
        Assert.DoesNotContain("masteryEffectsVersion", profileJson);
        Assert.DoesNotContain("skillPointsSpent", profileJson);
        Assert.DoesNotContain("xpSpentOnResets", profileJson);
        Assert.DoesNotContain("skillResetCount", profileJson);
        Assert.DoesNotContain("countryCode", profileJson);
    }

    [Fact]
    public void VersionTwoStateRoundTripsStructurallyAcrossFreshCollections()
    {
        var state = new PlayerCareerState
        {
            Reputation = 40,
            Opi = 0,
            PaceAnchor = 0,
            SeasonsCompleted = 0,
            Character = Profile(),
            Level = 30,
            Xp = 1_174,
            ExperienceMode = CareerExperienceModes.GrandPrixDynasty,
            CampaignProgressionPlan = FractionalPlan(),
            XpScaleRemainder = 4,
        };

        string json = JsonSerializer.Serialize(state, CoreJson.Options);
        var back = JsonSerializer.Deserialize<PlayerCareerState>(json, CoreJson.Options)!;

        Assert.Equal(state, back);
        Assert.Equal(state.GetHashCode(), back.GetHashCode());
        Assert.Equal(CareerExperienceModes.GrandPrixDynasty, back.ExperienceMode);
        Assert.Equal("dna_circuit_specialist", back.Character!.RacingDnaId);
        Assert.Equal("BRA", back.Character.CountryCode);
        Assert.Equal("technical", back.Character.RacingDnaChoice);
        Assert.Equal(["rain_man"], back.Character.CreationBaseline!.TraitIds);
        Assert.Equal(4, back.XpScaleRemainder);
    }

    [Fact]
    public void VersionedCreationInputCarriesTheCompleteProfileAndPlan()
    {
        var input = new CharacterCreationInput
        {
            Profile = NewProfile() with
            {
                MasteryEffectsVersion = CharacterProfile.CurrentMasteryEffectsVersion,
                ExpectationModelVersion = CharacterProfile.CurrentExpectationModelVersion,
            },
            ExperienceMode = CareerExperienceModes.Smgp,
            CampaignProgressionPlan = Plan(),
        };

        input.ValidateForNewCareer();

        string json = JsonSerializer.Serialize(input, CoreJson.Options);
        var back = JsonSerializer.Deserialize<CharacterCreationInput>(json, CoreJson.Options)!;

        Assert.Equal(CharacterCreationInput.CurrentVersion, back.Version);
        Assert.Equal(input, back);
        Assert.Contains("racingDnaId", json);
        Assert.Contains("creationBaseline", json);
        Assert.Contains("campaignProgressionPlan", json);
        Assert.Contains("masteryEffectsVersion", json);
        Assert.Contains("expectationModelVersion", json);
        Assert.Contains("\"countryCode\"", json);
        Assert.Contains("\"BRA\"", json);
    }

    [Fact]
    public void CreationValidationRejectsIncompleteOrAlreadySpentProfiles()
    {
        var valid = new CharacterCreationInput
        {
            Profile = NewProfile(),
            ExperienceMode = CareerExperienceModes.Smgp,
            CampaignProgressionPlan = Plan(),
        };

        Assert.Throws<InvalidOperationException>(() =>
            (valid with { CampaignProgressionPlan = null }).ValidateForNewCareer());
        Assert.Throws<InvalidOperationException>(() =>
            (valid with { Profile = valid.Profile with { RacingDnaId = null } }).ValidateForNewCareer());
        Assert.Throws<InvalidOperationException>(() =>
            (valid with { Profile = valid.Profile with { Name = "   " } }).ValidateForNewCareer());
        Assert.Throws<InvalidOperationException>(() =>
            (valid with { Profile = valid.Profile with { SkillPointsSpent = 1 } }).ValidateForNewCareer());
        valid.ValidateForNewCareer();
        Assert.Throws<NotSupportedException>(() =>
            (valid with { Profile = valid.Profile with { MasteryEffectsVersion = 0 } }).ValidateForNewCareer());
        Assert.Throws<NotSupportedException>(() =>
            (valid with { Profile = valid.Profile with { MasteryEffectsVersion = 2 } }).ValidateForNewCareer());
        Assert.Throws<NotSupportedException>(() =>
            (valid with { Profile = valid.Profile with { ExpectationModelVersion = 0 } }).ValidateForNewCareer());
        Assert.Throws<NotSupportedException>(() =>
            (valid with { Profile = valid.Profile with { ExpectationModelVersion = 3 } }).ValidateForNewCareer());
        Assert.Throws<InvalidOperationException>(() =>
            (valid with
            {
                Profile = valid.Profile with
                {
                    ChosenFlavor = "notAWritableRating",
                    CreationBaseline = valid.Profile.CreationBaseline! with
                    {
                        ChosenFlavor = "notAWritableRating",
                    },
                },
            }).ValidateForNewCareer());
        Assert.Throws<InvalidOperationException>(() =>
            (valid with { Profile = valid.Profile with { CpUnspent = 1 } }).ValidateForNewCareer());
        Assert.Throws<InvalidOperationException>(() =>
            (valid with
            {
                Profile = valid.Profile with
                {
                    CreationBaseline = valid.Profile.CreationBaseline! with
                    {
                        Stats = valid.Profile.CreationBaseline!.Stats
                            .Where(kv => kv.Key != "craft")
                            .ToDictionary(),
                    },
                },
            }).ValidateForNewCareer());
        Assert.Throws<InvalidOperationException>(() =>
            (valid with { Profile = null! }).ValidateForNewCareer());
        Assert.Throws<NotSupportedException>(() =>
            (valid with { Version = 2 }).ValidateForNewCareer());
    }
}
