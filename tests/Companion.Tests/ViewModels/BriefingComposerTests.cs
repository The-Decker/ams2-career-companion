using Companion.Core.Packs;
using Companion.ViewModels.Services;

namespace Companion.Tests.ViewModels;

/// <summary>
/// Exact composition of the sectioned Race Day briefing against the REAL f1-1967 pack (now
/// authored with per-session weekend weather + durations + the season refuelling flag) and the
/// REAL extracted content library: the AMS2 custom-race screen sections (Event → Practice →
/// Qualifying → Race → Rules), the 60-min timed sessions, four weather slots per session, the
/// Refuelling: No row, the fuel advisory, placeholder labeling for 1967 R3 (Zandvoort driven at
/// spielberg_vintage), and a normal round.
/// </summary>
public class BriefingComposerTests
{
    private static (SeasonPack Pack, PackRound Round) RealRound(int number)
    {
        var pack = ViewModelTestData.RealPack();
        return (pack, pack.Season.Rounds.Single(r => r.Round == number));
    }

    /// <summary>Every 1967 round is authored with practice+qualifying at 60 min and four Clear
    /// weather slots per session (practice/qualifying/race), the invariant block both real-round
    /// tests share, parameterised only by laps/opponents/date/start.</summary>
    private static IReadOnlyList<(string Section, string Label, string Value)> ExpectedSettings(
        string track, string opponents, string laps, string date) =>
    [
        ("Event", "Track", track),
        ("Event", "Class", "F-Vintage_Gen1"),
        ("Event", "Opponents", opponents),
        ("Practice", "Duration", "60 min"),
        ("Practice", "Weather slot 1", "Clear"),
        ("Practice", "Weather slot 2", "Clear"),
        ("Practice", "Weather slot 3", "Clear"),
        ("Practice", "Weather slot 4", "Clear"),
        ("Qualifying", "Duration", "60 min"),
        ("Qualifying", "Weather slot 1", "Clear"),
        ("Qualifying", "Weather slot 2", "Clear"),
        ("Qualifying", "Weather slot 3", "Clear"),
        ("Qualifying", "Weather slot 4", "Clear"),
        ("Race", "Laps", laps),
        ("Race", "Weather slot 1", "Clear"),
        ("Race", "Weather slot 2", "Clear"),
        ("Race", "Weather slot 3", "Clear"),
        ("Race", "Weather slot 4", "Clear"),
        ("Race", "Date", date),
        ("Race", "Start time", "14:00"),
        ("Race", "Time progression", "1x"),
        ("Rules", "Mandatory pit stop", "No"),
        ("Rules", "Refuelling", "No"),
    ];

    [Fact]
    public void Compose_PlaceholderRound_ZandvoortAtSpielbergVintage()
    {
        var (pack, round) = RealRound(3);

        var briefing = BriefingComposer.Compose(pack, round, ViewModelTestData.RealLibrary.Value);

        Assert.True(briefing.IsPlaceholder);
        // The REAL venue stays on record; the track driven is the placeholder.
        Assert.Equal("Circuit Park Zandvoort", briefing.VenueDisplayName);

        // grid.size 19 (the max-grid roster covering round 3) - 1 for the player's seat = 18.
        Assert.Equal(
            ExpectedSettings(track: "Spielberg_Vintage", opponents: "18", laps: "64", date: "1967-06-04"),
            briefing.Settings.Select(s => (s.Section, s.Label, s.Value)));

        // The distance note (authored in setupGuide.notes) rides along verbatim.
        Assert.Equal(round.SetupGuide!.Notes, briefing.SetupNotes);
        Assert.Contains("377.4 km", briefing.SetupNotes);

        // 64 laps is beyond the F-Vintage one-tank range → the warning variant, with the era caveat.
        Assert.Contains("64 laps", briefing.FuelNote);
        Assert.Contains("don't refuel", briefing.FuelNote);

        Assert.Equal("Dutch Grand Prix, placeholder: Spielberg_Vintage",
            BriefingComposer.ComposeTitle(briefing));
    }

    [Fact]
    public void Compose_NormalRound_Kyalami()
    {
        var (pack, round) = RealRound(1);

        var briefing = BriefingComposer.Compose(pack, round, ViewModelTestData.RealLibrary.Value);

        Assert.False(briefing.IsPlaceholder);
        Assert.Equal("Kyalami Racing Circuit", briefing.VenueDisplayName);

        // grid.size 20 (the FULL 20-car skinpack roster, no more 11-car Kyalami) - 1 = 19.
        Assert.Equal(
            ExpectedSettings(track: "Kyalami_Historic", opponents: "19", laps: "80", date: "1967-01-02"),
            briefing.Settings.Select(s => (s.Section, s.Label, s.Value)));

        Assert.Contains("80 laps", briefing.FuelNote);

        // No placeholder labeling on a real venue.
        Assert.Equal("South African Grand Prix", BriefingComposer.ComposeTitle(briefing));
    }

    [Fact]
    public void Compose_RoundWithoutSetupGuide_DegradesToTheCalendarFacts()
    {
        var pack = TestPackBuilder.TwoRoundPack();
        var round = pack.Season.Rounds[0] with { SetupGuide = null };

        var briefing = BriefingComposer.Compose(pack, round, TestPackBuilder.Library());

        Assert.Equal(
        [
            ("Track", "Kyalami Historic"),
            ("Class", "F-Vintage_Gen1"),
            ("Laps", "40"),
            ("Date", "1967-01-02"), // falls back to the round's historical date
        ], briefing.Settings.Select(s => (s.Label, s.Value)));
        Assert.Null(briefing.SetupNotes);
    }

    [Fact]
    public void Compose_WeekendWithoutPerRaceWeather_FallsBackToRoundLevelWeatherSlots()
    {
        // A round with a weekend block whose RACE authors no per-session weather, but the round-level
        // setupGuide DOES, the composer must fall back to it and emit one Race weather row per slot.
        var pack = TestPackBuilder.TwoRoundPack();
        var round = pack.Season.Rounds[0] with
        {
            Weekend = new PackWeekend
            {
                Practice = new PackWeekendSession { Present = true, Label = "Practice" },   // no detail
                Qualifying = new PackWeekendSession { Present = true, Label = "Qualifying" },
                Races = [new PackWeekendRace { Id = "race", Label = "Grand Prix" }],          // no weather
            },
            SetupGuide = new PackSetupGuide
            {
                Session = new PackSessionSettings { Opponents = 5, WeatherSlots = ["Light Rain", "Clear"] },
            },
        };

        var briefing = BriefingComposer.Compose(pack, round, TestPackBuilder.Library());

        // The fallback populates the Race section's weather rows (one per round-level slot).
        Assert.Equal(
            [("Weather slot 1", "Light Rain"), ("Weather slot 2", "Clear")],
            briefing.Settings
                .Where(s => s.Section == "Race" && s.Label.StartsWith("Weather slot", StringComparison.Ordinal))
                .Select(s => (s.Label, s.Value)));

        // Practice/Qualifying authored neither a duration nor weather → no section rows for them
        // (and they never fall back to the round-level weather, that is the race's).
        Assert.DoesNotContain(briefing.Settings, s => s.Section == "Practice");
        Assert.DoesNotContain(briefing.Settings, s => s.Section == "Qualifying");
    }
}
