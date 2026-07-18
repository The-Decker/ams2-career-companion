using Companion.ViewModels.Briefing;
using Companion.ViewModels.Services;

namespace Companion.Tests.ViewModels;

/// <summary>
/// The race-setup circuit map keys by the PACK's authored year, not the career's current season year.
/// They are equal for every ordinary season, but a CARRYOVER season reuses the same pinned pack (its
/// calendar / its tracks) for a LATER year, so the career year runs ahead of the pack while the track
/// actually set up in AMS2 stays the pack's, the circuit map must follow the track being raced.
/// </summary>
public class BriefingCircuitTests
{
    private static HistoricalSeason SeasonWithRound1Circuit(int year, string layoutId) => new()
    {
        Year = year,
        Rounds =
        [
            new HistoricalRound
            {
                Round = 1,
                Name = "Grand Prix",
                Circuit = new HistoricalCircuit { LayoutId = layoutId, Name = $"Circuit {year}" },
            },
        ],
    };

    [Fact]
    public void Circuit_KeysByThePackAuthoredYear_NotTheCarriedOverCareerYear()
    {
        var pack = TestPackBuilder.TwoRoundPack(); // Season.Year = 1967
        var library = TestPackBuilder.Library();
        var briefing = BriefingComposer.Compose(pack, pack.Season.Rounds[0], library);

        var session = new FakeCareerSession
        {
            Pack = pack,
            Briefing = briefing,
            // Carryover: the career has advanced to 1968 on the SAME 1967 pack.
            Summary = new FakeCareerSession().Summary with { SeasonYear = 1968 },
        };
        session.HistoryByYear[1967] = SeasonWithRound1Circuit(1967, "kyalami-1967"); // the pack's real year
        session.HistoryByYear[1968] = SeasonWithRound1Circuit(1968, "jarama-1968");  // the drifted career year

        var vm = new BriefingViewModel(session);

        // The pack year wins: the map shows the track actually raced, not 1968's real round-1 circuit.
        Assert.Equal("kyalami-1967", vm.CircuitLayoutId);
    }

    [Fact]
    public void Circuit_OnAnOrdinarySeason_StillResolvesFromThatYear()
    {
        var pack = TestPackBuilder.TwoRoundPack(); // Season.Year = 1967; default Summary is also 1967
        var library = TestPackBuilder.Library();
        var briefing = BriefingComposer.Compose(pack, pack.Season.Rounds[0], library);

        var session = new FakeCareerSession { Pack = pack, Briefing = briefing };
        session.HistoryByYear[1967] = SeasonWithRound1Circuit(1967, "kyalami-1967");

        var vm = new BriefingViewModel(session);

        Assert.Equal("kyalami-1967", vm.CircuitLayoutId);
    }
}
