using Companion.Core.Career;
using Companion.Core.Character;
using Companion.Core.Determinism;

namespace Companion.Tests.Career;

public sealed class ExpectationRoundUpdateTests
{
    [Fact]
    public void LegacyProfileKeepsTheShippedRoundExpectationPath()
    {
        RoundUpdateResult result = RoundUpdate.Apply(Context(modelVersion: 0));

        Assert.Equal(2, result.ExpectedFinish);
        Assert.Equal(-4.0, result.Player.Opi, 12);
    }

    [Fact]
    public void OptedInProfileUsesPriorOpiForTheRoundExpectation()
    {
        RoundUpdateResult result = RoundUpdate.Apply(
            Context(modelVersion: CharacterProfile.CurrentExpectationModelVersion));

        Assert.Equal(3, result.ExpectedFinish);
        Assert.Equal(-3.8, result.Player.Opi, 12);
    }

    private static RoundUpdateContext Context(int modelVersion) => new()
    {
        Grid = CareerTestData.PlayerGrid(),
        Player = new PlayerCareerState
        {
            Reputation = 40.0,
            Opi = -5.0,
            PaceAnchor = 90.0,
            SeasonsCompleted = 1,
            CurrentTeamId = "team.mid",
            LiveryName = CareerTestData.PlayerLivery,
            Character = new CharacterProfile
            {
                Stats = new Dictionary<string, double>(StringComparer.Ordinal),
                PerkIds = [],
                ExpectationModelVersion = modelVersion,
            },
        },
        PlayerTeamTier = 3,
        PlayerFinish = 2,
        HasTeammate = true,
        TeammateFinish = 3,
        SliderUsed = 90.0,
        PointsPositions = 6,
        IsChampionshipRound = true,
        IsPrimaryRace = true,
        Streams = new StreamFactory(42),
    };
}
