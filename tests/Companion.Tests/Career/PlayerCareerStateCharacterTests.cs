using System.Text.Json;
using Companion.Core.Career;
using Companion.Core.Character;
using Companion.Core.Json;

namespace Companion.Tests.Career;

/// <summary>The character fields added to <see cref="PlayerCareerState"/> must not perturb a
/// pre-character career's serialized state, they are omitted when default, so an existing
/// career's player_state blob is byte-identical. A character career round-trips them.</summary>
public sealed class PlayerCareerStateCharacterTests
{
    private static PlayerCareerState CharacterFree() => new()
    {
        Reputation = 40,
        Opi = 1.2,
        PaceAnchor = 92.0,
        SeasonsCompleted = 1,
        CurrentTeamId = "team.mid",
        LiveryName = "Mid #4",
    };

    [Fact]
    public void CharacterFreeState_OmitsEveryCharacterKey()
    {
        string json = JsonSerializer.Serialize(CharacterFree(), CoreJson.Options);

        Assert.DoesNotContain("\"character\"", json);
        Assert.DoesNotContain("\"level\"", json);
        Assert.DoesNotContain("\"xp\"", json);
        Assert.False(CharacterFree().HasCharacter);
    }

    [Fact]
    public void LegacyStateJson_WithoutCharacterKeys_DeserializesToNoCharacter()
    {
        // A player_state blob written before the character system omits the keys entirely.
        const string legacy =
            "{\"reputation\":40,\"opi\":1.2,\"paceAnchor\":92,\"qualifyingAnchor\":0," +
            "\"seasonsCompleted\":1,\"currentTeamId\":\"team.mid\",\"liveryName\":\"Mid #4\"}";

        var state = JsonSerializer.Deserialize<PlayerCareerState>(legacy, CoreJson.Options)!;

        Assert.Null(state.Character);
        Assert.False(state.HasCharacter);
        Assert.Equal(0, state.Level);
        Assert.Equal(0L, state.Xp);
    }

    [Fact]
    public void StructurallyEqualCharacters_CompareEqual_AcrossSeparateInstances()
    {
        // The replay start-state gate compares PlayerCareerState with record ==/!=. A character's
        // Stats (dictionary) and PerkIds (list) are collections; two states built independently
        // (a re-derived season start vs the stored one) hold DIFFERENT collection instances with
        // the SAME values. Without structural equality on CharacterProfile they compare unequal —
        // a FALSE replay divergence at every season boundary. This locks the fix.
        PlayerCareerState WithCharacter() => CharacterFree() with
        {
            Character = new CharacterProfile
            {
                Stats = new Dictionary<string, double>(StringComparer.Ordinal) { ["pace"] = 0.6, ["oneLap"] = 0.55 },
                PerkIds = ["glass_cannon", "wonderkid"],
                CpUnspent = 3,
            },
            Level = 2,
            Xp = 200,
        };

        var a = WithCharacter();
        var b = WithCharacter();

        Assert.False(ReferenceEquals(a.Character!.Stats, b.Character!.Stats));
        Assert.False(ReferenceEquals(a.Character.PerkIds, b.Character.PerkIds));
        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());

        // A genuinely different character is still unequal.
        var different = a with { Character = a.Character with { PerkIds = ["glass_cannon"] } };
        Assert.NotEqual(a, different);
    }

    [Fact]
    public void CharacterState_SerializesAndRoundTripsTheCharacter()
    {
        var state = CharacterFree() with
        {
            Character = new CharacterProfile
            {
                Stats = new Dictionary<string, double>(StringComparer.Ordinal)
                {
                    ["pace"] = 0.6, ["oneLap"] = 0.55, ["marketability"] = 0.7,
                },
                PerkIds = ["glass_cannon", "wonderkid"],
                CpUnspent = 3,
            },
            Level = 4,
            Xp = 512,
        };

        string json = JsonSerializer.Serialize(state, CoreJson.Options);
        Assert.Contains("\"character\"", json);
        Assert.Contains("\"level\"", json);

        var back = JsonSerializer.Deserialize<PlayerCareerState>(json, CoreJson.Options)!;
        Assert.True(back.HasCharacter);
        Assert.Equal(0.6, back.Character!.Stat("pace"), 6);
        Assert.Equal(["glass_cannon", "wonderkid"], back.Character.PerkIds);
        Assert.Equal(3, back.Character.CpUnspent);
        Assert.Equal(4, back.Level);
        Assert.Equal(512L, back.Xp);

        // The exact replay scenario: a DESERIALIZED stored state must compare equal to the
        // in-memory one (the start-state gate uses record ==). Without CharacterProfile's structural
        // equality this fails on the freshly-deserialized Stats/PerkIds collections.
        Assert.Equal(state, back);
    }

    [Fact]
    public void ProgressionDefaults_AreOmitted_ButOptInFieldsRoundTripStructurally()
    {
        var legacy = new CharacterProfile
        {
            Stats = new Dictionary<string, double>(StringComparer.Ordinal) { ["pace"] = 0.5 },
            PerkIds = [],
        };
        string legacyJson = JsonSerializer.Serialize(legacy, CoreJson.Options);
        Assert.DoesNotContain("progressionVersion", legacyJson);
        Assert.DoesNotContain("unlockedSkillNodeIds", legacyJson);
        Assert.DoesNotContain("creationPerkIds", legacyJson);
        Assert.DoesNotContain("countryCode", legacyJson);

        var current = legacy with
        {
            ProgressionVersion = 1,
            UnlockedSkillNodeIds = ["raise_pace_1"],
            CreationPerkIds = ["rain_man"],
            PerkIds = ["rain_man"],
            CountryCode = "BRA",
        };
        string json = JsonSerializer.Serialize(current, CoreJson.Options);
        var back = JsonSerializer.Deserialize<CharacterProfile>(json, CoreJson.Options)!;
        Assert.Equal(current, back);
        Assert.Equal(current.GetHashCode(), back.GetHashCode());
        Assert.Equal("BRA", back.CountryCode);
        Assert.NotEqual(current, current with { CountryCode = "USA" });
    }
}
