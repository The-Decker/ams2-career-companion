using Companion.Core.Character;
using Companion.Core.Packs;

namespace Companion.Tests.Career;

public sealed class PlayerRoundConditionsTests
{
    [Fact]
    public void Prepare_PinsVersionRoundTrackWeatherAndOddMedianBand()
    {
        SeasonPack pack = Pack([10, 20, 30]);

        PlayerRoundConditionsInput input = PlayerRoundConditions.Prepare(pack, 2, isWet: true);

        Assert.Equal(PlayerRoundConditionsInput.CurrentVersion, input.Version);
        Assert.Equal(CharacterLevelProgression.Level300Version, input.ProgressionVersion);
        Assert.Equal(2, input.Round);
        Assert.Equal("track-2", input.TrackId);
        Assert.True(input.IsWet);
        Assert.Equal(PlayerRoundLengthBand.Neutral, input.LengthBand);
    }

    [Fact]
    public void Prepare_PreservesReplayEvenMedianSemantics()
    {
        SeasonPack pack = Pack([10, 20, 30, 40]);

        Assert.Equal(PlayerRoundLengthBand.Short,
            PlayerRoundConditions.Prepare(pack, 2, false).LengthBand);
        Assert.Equal(PlayerRoundLengthBand.Long,
            PlayerRoundConditions.Prepare(pack, 3, false).LengthBand);
    }

    [Fact]
    public void Prepare_UsesNeutralWhenRoundLapsOrAllSeasonLapsAreUnknown()
    {
        SeasonPack partlyKnown = Pack([10, 0, 30]);
        SeasonPack allUnknown = Pack([0, 0]);

        Assert.Equal(PlayerRoundLengthBand.Neutral,
            PlayerRoundConditions.Prepare(partlyKnown, 2, false).LengthBand);
        Assert.Equal(PlayerRoundLengthBand.Neutral,
            PlayerRoundConditions.Prepare(allUnknown, 1, false).LengthBand);
    }

    [Fact]
    public void ActiveConditions_ReturnsExactClosedTokens()
    {
        static PlayerRoundConditionsInput Input(bool wet, PlayerRoundLengthBand band) => new()
        {
            Round = 1,
            TrackId = "track-1",
            IsWet = wet,
            LengthBand = band,
        };

        Assert.Equal(["dryRound"],
            PlayerRoundConditions.ActiveConditions(Input(false, PlayerRoundLengthBand.Neutral)).Order());
        Assert.Equal(["shortRace", "wetRound"],
            PlayerRoundConditions.ActiveConditions(Input(true, PlayerRoundLengthBand.Short)).Order());
        Assert.Equal(["dryRound", "longRace"],
            PlayerRoundConditions.ActiveConditions(Input(false, PlayerRoundLengthBand.Long)).Order());
    }

    [Fact]
    public void Validate_AcceptsCanonicalPayload()
    {
        SeasonPack pack = Pack([10, 20, 30]);
        PlayerRoundConditionsInput input = PlayerRoundConditions.Prepare(pack, 1, false);

        PlayerRoundConditions.Validate(input, pack, journalRound: 1);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("progression")]
    [InlineData("journalRound")]
    [InlineData("inputRound")]
    [InlineData("track")]
    [InlineData("trackCase")]
    [InlineData("length")]
    public void Validate_RejectsNonCanonicalOrMismatchedPayload(string mutation)
    {
        SeasonPack pack = Pack([10, 20, 30]);
        PlayerRoundConditionsInput input = PlayerRoundConditions.Prepare(pack, 1, false);
        int journalRound = 1;

        switch (mutation)
        {
            case "version":
                input = input with { Version = PlayerRoundConditionsInput.CurrentVersion + 1 };
                break;
            case "progression":
                input = input with { ProgressionVersion = CharacterLevelProgression.EraCappedVersion };
                break;
            case "journalRound":
                journalRound = 2;
                break;
            case "inputRound":
                input = input with { Round = 99 };
                journalRound = 99;
                break;
            case "track":
                input = input with { TrackId = "other-track" };
                break;
            case "trackCase":
                input = input with { TrackId = "TRACK-1" };
                break;
            case "length":
                input = input with { LengthBand = PlayerRoundLengthBand.Long };
                break;
        }

        Assert.Throws<InvalidOperationException>(() =>
            PlayerRoundConditions.Validate(input, pack, journalRound));
    }

    [Fact]
    public void Prepare_RejectsRoundMissingFromPinnedPack()
    {
        Assert.Throws<InvalidOperationException>(() =>
            PlayerRoundConditions.Prepare(Pack([10]), round: 2, isWet: false));
    }

    [Theory]
    [InlineData("Clear", false)]
    [InlineData("light cloud", false)]
    [InlineData("Medium Cloud", false)]
    [InlineData("HEAVY CLOUD", false)]
    [InlineData("Overcast", false)]
    [InlineData("Foggy", false)]
    [InlineData("Hazy", false)]
    [InlineData("Light Rain", true)]
    [InlineData("RAIN", true)]
    [InlineData("Heavy Rain", true)]
    [InlineData("Storm", true)]
    [InlineData("thunderstorm", true)]
    public void TryInferIsWet_RecognizesExactCaseInsensitiveAms2Weather(
        string weather,
        bool expectedWet)
    {
        SeasonPack pack = WeatherPack(setupWeather: [weather]);

        Assert.Equal(expectedWet, PlayerRoundConditions.TryInferIsWet(pack, 1));
    }

    [Fact]
    public void TryInferIsWet_UsesEachScoringRaceAndRequiresConsensus()
    {
        SeasonPack agreeing = WeatherPack(
            setupWeather: ["Clear"],
            raceWeather: [["Rain", "Heavy Rain"], ["Storm"]]);
        SeasonPack disagreeing = WeatherPack(
            setupWeather: ["Clear"],
            raceWeather: [["Rain"], ["Clear"]]);

        Assert.True(PlayerRoundConditions.TryInferIsWet(agreeing, 1));
        Assert.Null(PlayerRoundConditions.TryInferIsWet(disagreeing, 1));
    }

    [Fact]
    public void TryInferIsWet_NullRaceSlotsFallBackButExplicitlyEmptySlotsAreUnknown()
    {
        SeasonPack fallback = WeatherPack(
            setupWeather: ["Rain"],
            raceWeather: [null, ["Heavy Rain"]]);
        SeasonPack explicitlyEmpty = WeatherPack(
            setupWeather: ["Rain"],
            raceWeather: [[]]);

        Assert.True(PlayerRoundConditions.TryInferIsWet(fallback, 1));
        Assert.Null(PlayerRoundConditions.TryInferIsWet(explicitlyEmpty, 1));
    }

    [Theory]
    [MemberData(nameof(AmbiguousWeather))]
    public void TryInferIsWet_ReturnsNullForMissingMixedDynamicOrUnknownWeather(
        IReadOnlyList<string>? weather)
    {
        SeasonPack pack = WeatherPack(setupWeather: weather);

        Assert.Null(PlayerRoundConditions.TryInferIsWet(pack, 1));
    }

    public static TheoryData<IReadOnlyList<string>?> AmbiguousWeather
    {
        get
        {
            var data = new TheoryData<IReadOnlyList<string>?>();
            data.Add(null);
            data.Add(Array.Empty<string>());
            data.Add([""]);
            data.Add(["Clear", "Rain"]);
            data.Add(["Random"]);
            data.Add(["Real Weather"]);
            data.Add(["Showers"]);
            data.Add(["Wet"]);
            data.Add(["Clear Sky"]);
            return data;
        }
    }

    [Fact]
    public void TryInferIsWet_ReturnsNullForMissingRoundOrEmptyWeekend()
    {
        SeasonPack pack = WeatherPack(setupWeather: ["Clear"]);
        PackRound round = pack.Season.Rounds[0] with
        {
            Weekend = new PackWeekend { Races = [] },
        };
        pack = pack with { Season = pack.Season with { Rounds = [round] } };

        Assert.Null(PlayerRoundConditions.TryInferIsWet(pack, 99));
        Assert.Null(PlayerRoundConditions.TryInferIsWet(pack, 1));
    }

    private static SeasonPack Pack(IReadOnlyList<int> laps)
    {
        SeasonPack original = CareerTestData.Pack();
        PackRound template = original.Season.Rounds[0];
        PackRound[] rounds = laps.Select((lapCount, index) => template with
        {
            Round = index + 1,
            Name = $"Round {index + 1}",
            Track = new PackTrackRef { Id = $"track-{index + 1}" },
            Laps = lapCount,
        }).ToArray();
        return original with { Season = original.Season with { Rounds = rounds } };
    }

    private static SeasonPack WeatherPack(
        IReadOnlyList<string>? setupWeather,
        IReadOnlyList<IReadOnlyList<string>?>? raceWeather = null)
    {
        SeasonPack pack = Pack([10]);
        PackRound round = pack.Season.Rounds[0] with
        {
            SetupGuide = setupWeather is null
                ? null
                : new PackSetupGuide
                {
                    Session = new PackSessionSettings
                    {
                        Opponents = 5,
                        WeatherSlots = setupWeather,
                    },
                },
            Weekend = raceWeather is null
                ? null
                : new PackWeekend
                {
                    Races = raceWeather.Select((weather, index) => new PackWeekendRace
                    {
                        Id = $"race-{index + 1}",
                        Label = $"Race {index + 1}",
                        WeatherSlots = weather,
                    }).ToArray(),
                },
        };
        return pack with { Season = pack.Season with { Rounds = [round] } };
    }
}
