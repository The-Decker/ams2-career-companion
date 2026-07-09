using Companion.ViewModels.Hub;
using Companion.ViewModels.Services;

namespace Companion.Tests.ViewModels;

/// <summary>The Calendar lens projects the season track schedule up front: each round's driven AMS2
/// track, its real venue, a kind badge (real venue / stand-in / alternate) + track line, an unused-
/// alternate note, and the header tally.</summary>
public class CalendarViewModelTests
{
    private static SeasonScheduleEntry Entry(
        int round, string name, string venue, string track, SeasonTrackKind kind, string? unused = null) => new()
    {
        Round = round, Name = name, Date = $"1978-05-0{round}", RealVenue = venue,
        Ams2TrackName = track, Laps = 70, Kind = kind, UnusedAlternateName = unused,
    };

    private static CalendarViewModel Vm(params SeasonScheduleEntry[] entries)
    {
        var session = new FakeCareerSession();
        session.ScheduleEntries.AddRange(entries);
        return new CalendarViewModel(session);
    }

    [Fact]
    public void Projects_Kind_Badge_TrackLine_AndUnusedAlternate()
    {
        var vm = Vm(
            Entry(1, "Monaco GP", "Monaco", "Monaco", SeasonTrackKind.RealVenue),
            Entry(2, "Belgian GP", "Circuit Zolder", "Zolder", SeasonTrackKind.Alternate),
            Entry(3, "Dutch GP", "Circuit Zandvoort", "Hockenheim 1988", SeasonTrackKind.StandIn, unused: "Zolder"));

        Assert.Equal(3, vm.Rounds.Count);

        var real = vm.Rounds[0];
        Assert.True(real.IsRealVenue);
        Assert.Equal("Real venue", real.BadgeText);
        Assert.Equal("Monaco", real.TrackLine); // just the track — no "the actual …" tail
        Assert.False(real.HasUnusedAlternate);

        var alt = vm.Rounds[1];
        Assert.True(alt.IsAlternate);
        Assert.Equal("Alternate", alt.BadgeText);
        Assert.Contains("mod alternate", alt.TrackLine);
        Assert.Contains("Circuit Zolder", alt.TrackLine);

        var stand = vm.Rounds[2];
        Assert.True(stand.IsStandIn);
        Assert.Equal("Stand-in", stand.BadgeText);
        Assert.Contains("stand-in", stand.TrackLine);
        Assert.True(stand.HasUnusedAlternate);
        Assert.Contains("Zolder", stand.UnusedAlternateNote);
        Assert.Contains("R3", stand.RoundLabel);
        Assert.Contains("70 laps", stand.LapsText);

        Assert.True(vm.HasUnusedAlternates);
    }

    [Fact]
    public void HeaderNote_CountsAlternatesAndStandIns()
    {
        var vm = Vm(
            Entry(1, "a", "a", "a", SeasonTrackKind.RealVenue),
            Entry(2, "b", "b", "b", SeasonTrackKind.Alternate),
            Entry(3, "c", "c", "c", SeasonTrackKind.StandIn));

        Assert.Contains("3 rounds", vm.HeaderNote);
        Assert.Contains("1 on mod alternate", vm.HeaderNote);
        Assert.Contains("1 stand-in", vm.HeaderNote);
    }

    [Fact]
    public void Empty_Schedule_ReportsEmpty()
    {
        var vm = Vm();

        Assert.True(vm.IsEmpty);
        Assert.False(vm.HasUnusedAlternates);
        Assert.Equal("", vm.HeaderNote);
    }

    [Fact]
    public void Round_TogglesExpanded_AndExposesTheOriginalCircuit()
    {
        var session = new FakeCareerSession();
        session.ScheduleEntries.Add(new SeasonScheduleEntry
        {
            Round = 1, Name = "Dutch GP", Date = "1978-08-27", RealVenue = "Circuit Zandvoort",
            Ams2TrackName = "Hockenheim 1988", Laps = 44, Kind = SeasonTrackKind.StandIn,
            CircuitLayoutId = "zandvoort", CircuitCaption = "Zandvoort · 4.25 km · 14 turns",
            CircuitHistory = "Zandvoort hosted the Dutch GP from 1952.",
        });
        var round = new CalendarViewModel(session).Rounds[0];

        // The ORIGINAL circuit (Zandvoort), not the stand-in (Hockenheim), is what's exposed.
        Assert.True(round.HasCircuit);
        Assert.Equal("zandvoort", round.CircuitLayoutId);
        Assert.True(round.HasCircuitCaption);
        Assert.True(round.HasCircuitHistory);
        Assert.Contains("Zandvoort", round.CircuitHistory);

        Assert.False(round.IsExpanded);
        round.ToggleCommand.Execute(null);
        Assert.True(round.IsExpanded);
        round.ToggleCommand.Execute(null);
        Assert.False(round.IsExpanded);
    }
}
