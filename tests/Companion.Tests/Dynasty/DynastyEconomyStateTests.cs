using System.Text.Json;
using Companion.Core.Career;
using Companion.Core.Dynasty;
using Companion.Core.Json;
using Companion.Core.Numerics;

namespace Companion.Tests.Dynasty;

/// <summary>DynastyEconomyState is replay-safe by construction: canonical sponsor-key order,
/// structural (not reference) equality, exact-rational money round-trips, and a null state
/// serializes to NOTHING on the player blob — the byte-identical gate for every non-economy
/// career.</summary>
public sealed class DynastyEconomyStateTests
{
    private static DynastyEconomyState Fresh() => new()
    {
        Version = 1,
        Balance = Rational.Parse("100000"),
    };

    [Fact]
    public void FreshState_OmitsEveryDefaultOptionalField()
    {
        string json = JsonSerializer.Serialize(Fresh(), CoreJson.Options);
        Assert.Contains("\"version\"", json, StringComparison.Ordinal);
        Assert.Contains("\"100000\"", json, StringComparison.Ordinal); // Rational as canonical string
        Assert.DoesNotContain("developmentLevel", json, StringComparison.Ordinal);
        Assert.DoesNotContain("staffTier", json, StringComparison.Ordinal);
        Assert.DoesNotContain("secondSeat", json, StringComparison.Ordinal);
        Assert.DoesNotContain("deficitRounds", json, StringComparison.Ordinal);
        Assert.DoesNotContain("bankrupt", json, StringComparison.Ordinal);
    }

    [Fact]
    public void RoundTrip_IsValueEqual()
    {
        var state = Fresh() with
        {
            Balance = new Rational(20000, 3), // a non-integer exact balance survives
            DevelopmentLevel = 3,
            StaffTier = 2,
            SecondSeat = SecondSeatDeal.PayDriver,
            DeficitRounds = 2,
        };
        var sponsored = state
            .WithSponsor("sponsor.b", new DynastySponsorContract { SeasonsRemaining = 2 })
            .WithSponsor("sponsor.a", new DynastySponsorContract { SeasonsRemaining = 1 });

        string json = JsonSerializer.Serialize(sponsored, CoreJson.Options);
        var back = JsonSerializer.Deserialize<DynastyEconomyState>(json, CoreJson.Options)!;

        Assert.Equal(sponsored, back);
        Assert.Equal(sponsored.GetHashCode(), back.GetHashCode());
        Assert.Equal(new Rational(20000, 3), back.Balance);
    }

    [Fact]
    public void SponsorKeys_AreCanonicallyOrderedRegardlessOfInsertionOrder()
    {
        var contract = new DynastySponsorContract { SeasonsRemaining = 2 };
        var forward = Fresh().WithSponsor("sponsor.a", contract).WithSponsor("sponsor.b", contract);
        var reverse = Fresh().WithSponsor("sponsor.b", contract).WithSponsor("sponsor.a", contract);

        Assert.Equal(forward, reverse);
        Assert.Equal(
            JsonSerializer.Serialize(forward, CoreJson.Options),
            JsonSerializer.Serialize(reverse, CoreJson.Options));
        Assert.Equal(["sponsor.a", "sponsor.b"], forward.Sponsors.Keys);
    }

    [Fact]
    public void WithoutSponsor_RemovesAndKeepsCanonicalOrder()
    {
        var contract = new DynastySponsorContract { SeasonsRemaining = 2 };
        var state = Fresh()
            .WithSponsor("sponsor.c", contract)
            .WithSponsor("sponsor.a", contract)
            .WithSponsor("sponsor.b", contract)
            .WithoutSponsor("sponsor.b");

        Assert.Equal(["sponsor.a", "sponsor.c"], state.Sponsors.Keys);
        Assert.Same(state, state.WithoutSponsor("sponsor.unknown")); // absent removal is a no-op
    }

    [Fact]
    public void PlayerBlob_OmitsEconomyWhenNull()
    {
        var player = new PlayerCareerState { Reputation = 40.0 };
        string json = JsonSerializer.Serialize(player, CoreJson.Options);
        Assert.DoesNotContain("economy", json, StringComparison.OrdinalIgnoreCase);

        var withEconomy = player with { Economy = Fresh() };
        string economyJson = JsonSerializer.Serialize(withEconomy, CoreJson.Options);
        Assert.Contains("\"economy\"", economyJson, StringComparison.Ordinal);

        // And record equality on the carrier state remains structural through the new member.
        var reread = JsonSerializer.Deserialize<PlayerCareerState>(economyJson, CoreJson.Options)!;
        Assert.Equal(withEconomy, reread);
    }

    [Fact]
    public void BankruptAndDeficit_SurviveTheRoundTrip()
    {
        var state = Fresh() with { Balance = Rational.Parse("-5000"), DeficitRounds = 4, Bankrupt = true };
        string json = JsonSerializer.Serialize(state, CoreJson.Options);
        Assert.Contains("\"bankrupt\": true", json, StringComparison.Ordinal);
        var back = JsonSerializer.Deserialize<DynastyEconomyState>(json, CoreJson.Options)!;
        Assert.True(back.Bankrupt);
        Assert.Equal(Rational.Parse("-5000"), back.Balance);
        Assert.Equal(4, back.DeficitRounds);
    }
}
