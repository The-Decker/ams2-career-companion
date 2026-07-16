using System.Text.Json;
using Companion.Core.Character;
using Companion.Core.Json;

namespace Companion.Tests.Career;

public sealed class ExpectationModelProfileTests
{
    [Fact]
    public void LegacyProfileOmitsExpectationVersionAndRoundTripsAsZero()
    {
        CharacterProfile legacy = Profile();

        string json = JsonSerializer.Serialize(legacy, CoreJson.Options);
        CharacterProfile roundTrip = JsonSerializer.Deserialize<CharacterProfile>(json, CoreJson.Options)!;

        Assert.DoesNotContain("expectationModelVersion", json, StringComparison.Ordinal);
        Assert.Equal(0, roundTrip.ExpectationModelVersion);
        Assert.Equal(legacy, roundTrip);
        Assert.Equal(legacy.GetHashCode(), roundTrip.GetHashCode());
    }

    [Fact]
    public void OptedInVersionIsPersistedAndParticipatesInStructuralEquality()
    {
        CharacterProfile legacy = Profile();
        CharacterProfile optedIn = legacy with
        {
            ExpectationModelVersion = CharacterProfile.CurrentExpectationModelVersion,
        };

        string json = JsonSerializer.Serialize(optedIn, CoreJson.Options);
        CharacterProfile roundTrip = JsonSerializer.Deserialize<CharacterProfile>(json, CoreJson.Options)!;

        Assert.Contains(
            $"\"expectationModelVersion\": {CharacterProfile.CurrentExpectationModelVersion}",
            json,
            StringComparison.Ordinal);
        Assert.NotEqual(legacy, optedIn);
        Assert.Equal(optedIn, roundTrip);
        Assert.Equal(optedIn.GetHashCode(), roundTrip.GetHashCode());
    }

    private static CharacterProfile Profile() => new()
    {
        Stats = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["pace"] = 0.7,
        },
        PerkIds = [],
    };
}
