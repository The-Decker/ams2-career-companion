using Companion.Core.Career;
using Companion.Core.Character;
using Companion.Core.Scoring;
using Companion.Data;

namespace Companion.Tests.Data;

public sealed class ExpectationModelReplayTests
{
    [Theory]
    [InlineData(0, 4)]
    [InlineData(CharacterProfile.CurrentExpectationModelVersion, 3)]
    public void VersionedExpectationFoldsLiveAndReplaysByteIdentically(
        int modelVersion,
        int expectedFinish)
    {
        using var tmp = new TempDb();
        using var db = CareerDatabase.Open(tmp.Path);
        var (seasonId, pack) = DataCareerFixture.SetupCareer(db);
        var start = DataCareerFixture.PlayerStart() with
        {
            Opi = 5.0,
            Character = new CharacterProfile
            {
                CountryCode = "BRA",
                Stats = new Dictionary<string, double>(StringComparer.Ordinal),
                PerkIds = [],
                ExpectationModelVersion = modelVersion,
            },
        };
        StateStore.UpsertPlayerState(db, seasonId, StateStore.StageStart, start);

        var round = DataCareerFixture.Rounds()[0];
        RoundFoldResult live = ReplayService.ImportAndFoldRound(
            db,
            seasonId,
            pack,
            DataCareerFixture.MasterSeed,
            DataCareerFixture.Inputs(),
            round.Round,
            DataCareerFixture.Envelope(round),
            DataCareerFixture.Utc);

        Assert.Equal(expectedFinish, live.ExpectedFinish);
        Assert.Equal("BRA", live.State.Player.Character!.CountryCode);

        ReplayReport replay = ReplayService.Resimulate(
            db, pack, DataCareerFixture.MasterSeed, DataCareerFixture.Inputs());

        Assert.True(
            replay.Identical,
            $"diverged: {replay.FirstDivergence?.Reason} " +
            $"stored={replay.FirstDivergence?.StoredDeltaJson} " +
            $"regenerated={replay.FirstDivergence?.RegeneratedDeltaJson}");
    }

    [Fact]
    public void SecondRoundUsesFirstRoundsFoldedPerformanceAndReplaysIdentically()
    {
        using var tmp = new TempDb();
        using var db = CareerDatabase.Open(tmp.Path);
        var (seasonId, pack) = DataCareerFixture.SetupCareer(db);
        var start = DataCareerFixture.PlayerStart() with
        {
            // Just below the teammate-strength threshold: the opening win raises folded OPI
            // above it, so round two must move the expectation from P4 to P3.
            Opi = 0.9,
            Character = new CharacterProfile
            {
                Stats = new Dictionary<string, double>(StringComparer.Ordinal),
                PerkIds = [],
                ExpectationModelVersion = CharacterProfile.CurrentExpectationModelVersion,
            },
        };
        StateStore.UpsertPlayerState(db, seasonId, StateStore.StageStart, start);

        RoundResult openingWin = DataCareerFixture.Rounds()[2] with { Round = 1 };
        RoundFoldResult first = ReplayService.ImportAndFoldRound(
            db, seasonId, pack, DataCareerFixture.MasterSeed, DataCareerFixture.Inputs(),
            openingWin.Round, DataCareerFixture.Envelope(openingWin), DataCareerFixture.Utc);
        Assert.Equal(4, first.ExpectedFinish);
        Assert.Equal(1.32, first.State.Player.Opi, 12);

        RoundResult secondRound = DataCareerFixture.Rounds()[1];
        RoundFoldResult second = ReplayService.ImportAndFoldRound(
            db, seasonId, pack, DataCareerFixture.MasterSeed, DataCareerFixture.Inputs(),
            secondRound.Round, DataCareerFixture.Envelope(secondRound), DataCareerFixture.Utc);
        Assert.Equal(3, second.ExpectedFinish);

        ReplayReport replay = ReplayService.Resimulate(
            db, pack, DataCareerFixture.MasterSeed, DataCareerFixture.Inputs());
        Assert.True(
            replay.Identical,
            $"diverged: {replay.FirstDivergence?.Reason} " +
            $"stored={replay.FirstDivergence?.StoredDeltaJson} " +
            $"regenerated={replay.FirstDivergence?.RegeneratedDeltaJson}");
    }
}
