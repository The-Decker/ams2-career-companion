using Companion.Core.Grid;
using Companion.Core.Packs;
using Companion.Core.Scoring;
using Companion.ViewModels.Hub;
using Companion.ViewModels.Services;

namespace Companion.Tests.ViewModels;

/// <summary>
/// The Calendar lens's per-round player-status chip (commit 8a0427c): a SatOut round reads
/// "SAT OUT, injured", a WillMiss round reads "WILL MISS, injured", and ordinary Raced /
/// Upcoming rounds stay quiet (empty label, HasPlayerStatus false) so the calendar shows the
/// injury story only when there is one. Uses a local stub session (the interface's default
/// members mean only <c>SeasonSchedule()</c> and the abstract core need implementing).
/// </summary>
public sealed class CalendarPlayerStatusTests
{
    private sealed class StubSession : ICareerSession
    {
        public IReadOnlyList<SeasonScheduleEntry> Schedule { get; set; } = [];

        public CareerSummary Summary => throw new NotSupportedException();

        public SeasonPack Pack => throw new NotSupportedException();

        public BriefingModel? CurrentBriefing() => null;

        public StageOutcome StageCurrentGrid() => throw new NotSupportedException();

        public IReadOnlyList<GridSeat> CurrentGrid() => [];

        public ConfirmModel Preview(ResultDraft draft) => throw new NotSupportedException();

        public void Apply(ResultDraft draft) => throw new NotSupportedException();

        public StandingsSnapshot? CurrentStandings() => null;

        public IReadOnlyList<StandingsSnapshot> AllSnapshots() => [];

        public int? CurrentSliderRecommendation() => null;

        public SeasonReviewModel? SeasonReview() => null;

        public void AcceptOffer(string teamId) => throw new NotSupportedException();

        public IReadOnlyList<SeasonScheduleEntry> SeasonSchedule() => Schedule;
    }

    private static SeasonScheduleEntry Entry(int round, SchedulePlayerStatus status) => new()
    {
        Round = round,
        Name = $"Round {round} Grand Prix",
        Date = $"1990-05-{round + 10:00}",
        RealVenue = "Imola",
        Ams2TrackName = "Imola 1988",
        Laps = 30,
        Kind = SeasonTrackKind.RealVenue,
        PlayerStatus = status,
    };

    private static CalendarRoundViewModel Round(SchedulePlayerStatus status)
    {
        var session = new StubSession { Schedule = [Entry(1, status)] };
        var vm = new CalendarViewModel(session);
        return Assert.Single(vm.Rounds);
    }

    [Fact]
    public void SatOutRound_ShowsTheInjuryChip()
    {
        var round = Round(SchedulePlayerStatus.SatOut);

        Assert.Equal(SchedulePlayerStatus.SatOut, round.PlayerStatus);
        Assert.Equal("SAT OUT, injured", round.PlayerStatusLabel);
        Assert.True(round.HasPlayerStatus);
    }

    [Fact]
    public void WillMissRound_ShowsTheForwardLookingInjuryChip()
    {
        var round = Round(SchedulePlayerStatus.WillMiss);

        Assert.Equal(SchedulePlayerStatus.WillMiss, round.PlayerStatus);
        Assert.Equal("WILL MISS, injured", round.PlayerStatusLabel);
        Assert.True(round.HasPlayerStatus);
    }

    [Theory]
    [InlineData(SchedulePlayerStatus.Raced)]
    [InlineData(SchedulePlayerStatus.Upcoming)]
    public void OrdinaryRounds_StayQuiet(SchedulePlayerStatus status)
    {
        var round = Round(status);

        Assert.Equal(status, round.PlayerStatus);
        Assert.Equal("", round.PlayerStatusLabel);
        Assert.False(round.HasPlayerStatus);
    }
}
