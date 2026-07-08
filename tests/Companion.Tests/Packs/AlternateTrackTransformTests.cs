using System.Text.Json;
using Companion.Core.Json;
using Companion.Core.Packs;

namespace Companion.Tests.Packs;

/// <summary>The creation-time alternate-track transform: swaps every round that declares an alternate
/// to that alternate (id + laps + placeholder flag) and leaves the rest untouched. Runs on the
/// season.json string before pinning, so the pinned pack drives the alternates.</summary>
public class AlternateTrackTransformTests
{
    private const string Season = """
    {"year":1978,"seriesName":"F1","ams2Class":"F-Vintage_Gen1",
     "pointsSystem":{"racePoints":[9,6,4,3,2,1]},
     "rounds":[
       {"round":1,"name":"Belgian GP","date":"1978-05-21","laps":72,
        "track":{"realVenue":"Zolder","id":"spa_1993","isPlaceholder":true,
                 "alternate":{"id":"Heusden","laps":74,"isRealVenue":true}}},
       {"round":2,"name":"French GP","date":"1978-07-02","laps":54,
        "track":{"realVenue":"Dijon-Prenois","id":"estoril_1988","isPlaceholder":true,
                 "alternate":{"id":"moravia","laps":49,"isRealVenue":false}}},
       {"round":3,"name":"British GP","date":"1978-07-16","laps":76,
        "track":{"realVenue":"Brands Hatch","id":"brandshatch_gp","isPlaceholder":false}}
     ]}
    """;

    private static SeasonDefinition Apply(string json) =>
        JsonSerializer.Deserialize<SeasonDefinition>(
            AlternateTrackTransform.ApplyToSeasonJson(json), CoreJson.Options)!;

    [Fact]
    public void Apply_RealVenueAlternate_SwapsTrackAndClearsPlaceholder()
    {
        var round = Apply(Season).Rounds[0];

        Assert.Equal("Heusden", round.Track.Id);
        Assert.Equal(74, round.Laps);
        Assert.False(round.Track.IsPlaceholder); // the authentic venue is no longer a stand-in
        Assert.Equal("Zolder", round.Track.RealVenue); // venue name preserved
    }

    [Fact]
    public void Apply_FillerAlternate_SwapsTrackButStaysPlaceholder()
    {
        var round = Apply(Season).Rounds[1];

        Assert.Equal("moravia", round.Track.Id);
        Assert.Equal(49, round.Laps);
        Assert.True(round.Track.IsPlaceholder); // a filler stand-in is still a labelled placeholder
    }

    [Fact]
    public void Apply_RoundWithoutAlternate_IsUntouched()
    {
        var round = Apply(Season).Rounds[2];

        Assert.Equal("brandshatch_gp", round.Track.Id);
        Assert.Equal(76, round.Laps);
        Assert.False(round.Track.IsPlaceholder);
    }

    [Fact]
    public void HasAlternates_TrueOnlyWhenARoundDeclaresOne()
    {
        var withAlts = JsonSerializer.Deserialize<SeasonDefinition>(Season, CoreJson.Options)!;
        var pack = new SeasonPack
        {
            Manifest = new PackManifest { PackId = "p", Name = "p", Version = "1", FormatVersion = 1 },
            Season = withAlts, Teams = [], Drivers = [], Entries = [],
        };
        Assert.True(AlternateTrackTransform.HasAlternates(pack));

        // Strip the alternates → false.
        var stripped = pack with
        {
            Season = withAlts with
            {
                Rounds = withAlts.Rounds.Select(r => r with { Track = r.Track with { Alternate = null } }).ToList(),
            },
        };
        Assert.False(AlternateTrackTransform.HasAlternates(stripped));
    }
}
