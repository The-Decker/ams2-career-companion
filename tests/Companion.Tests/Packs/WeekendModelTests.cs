using System.Text.Json;
using Companion.Core.Json;
using Companion.Core.Packs;

namespace Companion.Tests.Packs;

/// <summary>The optional race-weekend block on a round (Increment 2, slice 2a). Additive: a round
/// without it parses with a null weekend, so every existing pack loads unchanged.</summary>
public sealed class WeekendModelTests
{
    [Fact]
    public void Round_without_a_weekend_block_parses_with_null_weekend()
    {
        const string json = """
        {"round":1,"name":"South African Grand Prix","date":"1967-01-02",
         "track":{"id":"kyalami"},"laps":80}
        """;

        var round = JsonSerializer.Deserialize<PackRound>(json, CoreJson.Options)!;

        Assert.Null(round.Weekend); // single-race default — the fold + scoring stay untouched
    }

    [Fact]
    public void Weekend_block_parses_its_sessions_and_two_races()
    {
        const string json = """
        {"round":3,"name":"Brazilian Grand Prix","date":"1988-04-03",
         "track":{"id":"interlagos"},"laps":60,
         "weekend":{
           "practice":{"present":true,"label":"Practice"},
           "qualifying":{"present":true,"label":"Qualifying"},
           "races":[
             {"id":"race","label":"Grand Prix","pointsTable":"primary"},
             {"id":"race2","label":"Sprint","pointsTable":"sprint","gridFrom":"race1Reverse"}
           ]
         }}
        """;

        var round = JsonSerializer.Deserialize<PackRound>(json, CoreJson.Options)!;

        Assert.NotNull(round.Weekend);
        Assert.True(round.Weekend!.Practice!.Present);
        Assert.Equal("Qualifying", round.Weekend.Qualifying!.Label);
        Assert.Equal(2, round.Weekend.Races.Count);
        Assert.Equal(
            ("race", "Grand Prix", (string?)"primary", (string?)null),
            (round.Weekend.Races[0].Id, round.Weekend.Races[0].Label,
             round.Weekend.Races[0].PointsTable, round.Weekend.Races[0].GridFrom));
        Assert.Equal(
            ("race2", "Sprint", (string?)"sprint", (string?)"race1Reverse"),
            (round.Weekend.Races[1].Id, round.Weekend.Races[1].Label,
             round.Weekend.Races[1].PointsTable, round.Weekend.Races[1].GridFrom));
    }

    [Fact]
    public void Single_race_weekend_round_trips_through_json()
    {
        var round = new PackRound
        {
            Round = 1,
            Name = "Monaco Grand Prix",
            Date = "1967-05-07",
            Track = new PackTrackRef { Id = "monaco" },
            Laps = 100,
            Weekend = new PackWeekend
            {
                Qualifying = new PackWeekendSession { Label = "Qualifying" },
                Races = [new PackWeekendRace { Id = "race", Label = "Grand Prix" }],
            },
        };

        var roundTripped = JsonSerializer.Deserialize<PackRound>(
            JsonSerializer.Serialize(round, CoreJson.Options), CoreJson.Options)!;

        Assert.Single(roundTripped.Weekend!.Races);
        Assert.Equal("Grand Prix", roundTripped.Weekend.Races[0].Label);
        Assert.Null(roundTripped.Weekend.Races[0].PointsTable);
    }
}
