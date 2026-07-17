using Companion.Core.Newsroom;
using Companion.Core.Smgp;

namespace Companion.Tests.Smgp;

/// <summary>
/// Direct coverage of the SMGP canon-divergence comparer (D9): career venue winners measured
/// against the almanac's remembered rulers, emitting SmgpCanonDiverged/Held events whose
/// provenance is SmgpFiction — the SEGA canon must never read as verified history, and detection
/// must be a deterministic pure projection (same seasons in, same events out).
/// </summary>
public sealed class SmgpCanonDivergenceTests
{
    private static SmgpWhatReallyHappened Almanac() => SmgpWhatReallyHappened.Parse(
        """
        {
          "races": {
            "Monaco": { "title": "MONACO", "circuit": "the crown", "champion": "A. Senna · Madonna" },
            "Brazil": { "title": "BRAZIL", "circuit": "the cauldron", "champion": "G. Ceara · Bullets" },
            "Nowhere": { "title": "NOWHERE", "circuit": "unnamed", "champion": "" }
          }
        }
        """);

    private static NewsroomSeason Season(params NewsroomRound[] rounds) => new()
    {
        Ordinal = 1,
        Year = 1990,
        ChampionshipRoundCount = rounds.Length,
        Rounds = rounds,
    };

    private static NewsroomRound Round(int round, string venue, string winnerId, string winnerName) => new()
    {
        Round = round,
        Venue = venue,
        WinnerId = winnerId,
        WinnerName = winnerName,
    };

    [Fact]
    public void MatchingWinnerHolds_DifferentWinnerDiverges()
    {
        var events = SmgpCanonDivergence.Compare(
            [Season(
                Round(1, "Monaco", "driver.senna", "A. Senna"),
                Round(2, "Brazil", "player", "Nova Reyes"))],
            Almanac());

        Assert.Equal(2, events.Count);

        var held = events[0];
        Assert.Equal(NewsEventKind.SmgpCanonHeld, held.Kind);
        Assert.Equal("Monaco", held.VenueName);
        Assert.Equal("A. Senna", held.Facts.RivalName); // the remembered ruler rides the rival token

        var diverged = events[1];
        Assert.Equal(NewsEventKind.SmgpCanonDiverged, diverged.Kind);
        Assert.Equal("Brazil", diverged.VenueName);
        Assert.Equal("G. Ceara", diverged.Facts.RivalName);
        Assert.Equal("Nova Reyes", diverged.Facts.WinnerName);
        Assert.Equal("player", diverged.SubjectId);
    }

    [Fact]
    public void UnknownVenues_EmptyChampions_AndUnfinishedRounds_EmitNothing()
    {
        var events = SmgpCanonDivergence.Compare(
            [Season(
                Round(1, "Atlantis", "driver.a", "A. Driver"),   // venue not in the almanac
                Round(2, "Nowhere", "driver.b", "B. Driver"),    // authored venue, empty champion
                Round(3, "Monaco", "", ""))],                    // no winner stored yet
            Almanac());

        Assert.Empty(events);
    }

    [Fact]
    public void BothKindsCarrySmgpFictionProvenance_NeverVerifiedHistory()
    {
        Assert.Equal(ContentProvenance.SmgpFiction,
            NewsroomCategories.ProvenanceFor(NewsEventKind.SmgpCanonDiverged));
        Assert.Equal(ContentProvenance.SmgpFiction,
            NewsroomCategories.ProvenanceFor(NewsEventKind.SmgpCanonHeld));
        Assert.Equal(NewsroomCategory.HistoricalRetrospective,
            NewsroomCategories.CategoryFor(NewsEventKind.SmgpCanonDiverged));
    }

    [Fact]
    public void DetectionIsDeterministic_WithUniqueDedupeKeys()
    {
        var seasons = new[]
        {
            Season(
                Round(1, "Monaco", "driver.senna", "A. Senna"),
                Round(2, "Brazil", "driver.x", "X. Upstart")),
        };

        var first = SmgpCanonDivergence.Compare(seasons, Almanac());
        var second = SmgpCanonDivergence.Compare(seasons, Almanac());

        Assert.Equal(first.Select(e => e.DedupeKey), second.Select(e => e.DedupeKey));
        Assert.Equal(first.Count, first.Select(e => e.DedupeKey).Distinct(StringComparer.Ordinal).Count());
    }
}
