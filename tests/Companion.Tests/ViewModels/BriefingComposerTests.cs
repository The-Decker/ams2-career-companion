using Companion.Core.Packs;
using Companion.ViewModels.Services;

namespace Companion.Tests.ViewModels;

/// <summary>
/// Exact copy-string composition of the Race Day briefing against the REAL f1-1967 pack and
/// the REAL extracted content library: the fixed setting order (Track, Class, Laps, Date,
/// Start time, Weather slots, Opponents, Time progression, Mandatory pit stop), placeholder
/// labeling for 1967 R3 (Zandvoort driven at spielberg_vintage), and a normal round.
/// </summary>
public class BriefingComposerTests
{
    private static (SeasonPack Pack, PackRound Round) RealRound(int number)
    {
        var pack = ViewModelTestData.RealPack();
        return (pack, pack.Season.Rounds.Single(r => r.Round == number));
    }

    [Fact]
    public void Compose_PlaceholderRound_ZandvoortAtSpielbergVintage()
    {
        var (pack, round) = RealRound(3);

        var briefing = BriefingComposer.Compose(pack, round, ViewModelTestData.RealLibrary.Value);

        Assert.True(briefing.IsPlaceholder);
        // The REAL venue stays on record; the track driven is the placeholder.
        Assert.Equal("Circuit Park Zandvoort", briefing.VenueDisplayName);

        Assert.Equal(
        [
            ("Track", "Spielberg_Vintage"),
            ("Class", "F-Vintage_Gen1"),
            ("Laps", "64"),
            ("Date", "1967-06-04"),
            ("Start time", "14:00"),
            ("Weather slot 1", "Clear"),
            ("Opponents", "14"),
            ("Time progression", "1x"),
            ("Mandatory pit stop", "No"),
        ], briefing.Settings.Select(s => (s.Label, s.Value)));

        // The distance note (authored in setupGuide.notes) rides along verbatim.
        Assert.Equal(round.SetupGuide!.Notes, briefing.SetupNotes);
        Assert.Contains("377.4 km", briefing.SetupNotes);

        Assert.Equal("Dutch Grand Prix — placeholder: Spielberg_Vintage",
            BriefingComposer.ComposeTitle(briefing));
    }

    [Fact]
    public void Compose_NormalRound_Kyalami()
    {
        var (pack, round) = RealRound(1);

        var briefing = BriefingComposer.Compose(pack, round, ViewModelTestData.RealLibrary.Value);

        Assert.False(briefing.IsPlaceholder);
        Assert.Equal("Kyalami Racing Circuit", briefing.VenueDisplayName);

        Assert.Equal(
        [
            ("Track", "Kyalami_Historic"),
            ("Class", "F-Vintage_Gen1"),
            ("Laps", "80"),
            ("Date", "1967-01-02"),
            ("Start time", "14:00"),
            ("Weather slot 1", "Clear"),
            ("Opponents", "11"),
            ("Time progression", "1x"),
            ("Mandatory pit stop", "No"),
        ], briefing.Settings.Select(s => (s.Label, s.Value)));

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
}
