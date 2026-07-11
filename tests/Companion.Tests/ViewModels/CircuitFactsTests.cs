using Companion.ViewModels.Hub;
using Companion.ViewModels.Services;

namespace Companion.Tests.ViewModels;

/// <summary>Era-capped circuit FUN FACTS (reference data baked by derive_history.cs): the store
/// parses the additive <c>circuit.facts</c> array (and defaults it empty on pre-facts files), the
/// season schedule projection carries it, and the Calendar round VM exposes it. Sim-inert — the
/// fold never reads any of this (same contract as <c>History</c>).</summary>
public sealed class CircuitFactsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("companion-facts-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    [Fact]
    public void Store_parses_facts_and_defaults_empty_when_absent()
    {
        // Round 1 carries facts; round 2 is the pre-facts file shape (no "facts" key) — back-compat.
        File.WriteAllText(Path.Combine(_root, "1967.json"), """
            {"year":1967,"rounds":[
              {"round":1,"name":"South African Grand Prix",
               "circuit":{"layoutId":"kyalami","name":"Kyalami","history":"The Kyalami circuit.",
                          "facts":["Hosts its first World Championship Grand Prix this season."]}},
              {"round":2,"name":"Monaco Grand Prix",
               "circuit":{"layoutId":"monaco","name":"Monaco"}}]}
            """);
        var season = new HistoricalSeasonStore(_root).ForYear(1967);

        Assert.NotNull(season);
        var fact = Assert.Single(season!.Rounds[0].Circuit!.Facts);
        Assert.Equal("Hosts its first World Championship Grand Prix this season.", fact);
        Assert.Empty(season.Rounds[1].Circuit!.Facts);
    }

    [Fact]
    public void SeasonSchedule_carries_facts_from_the_history_file()
    {
        string packDir = Path.Combine(_root, "pack");
        TestPackBuilder.Write(TestPackBuilder.TwoRoundPack(), packDir);

        // History for the pack's year (1967): round 1 has facts, round 2 has none.
        string historyDir = Path.Combine(_root, "history");
        Directory.CreateDirectory(historyDir);
        File.WriteAllText(Path.Combine(historyDir, "1967.json"), """
            {"year":1967,"rounds":[
              {"round":1,"name":"Round 1",
               "circuit":{"layoutId":"kyalami","name":"Kyalami",
                          "facts":["Most pole positions: Juan Manuel Fangio (5).","Home-crowd wins: 4."]}},
              {"round":2,"name":"Round 2","circuit":{"layoutId":"kyalami","name":"Kyalami"}}]}
            """);

        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs"),
            library: TestPackBuilder.Library(),
            historyDirectory: historyDir);
        using var session = CareerSessionService.CreateCareer(
            new CareerCreationRequest
            {
                PackDirectory = packDir,
                CareerFilePath = Path.Combine(_root, "careers", "facts.ams2career"),
                CareerName = "Facts",
                MasterSeed = 11,
                PlayerLiveryName = TestPackBuilder.StockLivery2,
            },
            environment);

        var schedule = session.SeasonSchedule();
        Assert.Equal(2, schedule.Count);
        string[] expected = ["Most pole positions: Juan Manuel Fangio (5).", "Home-crowd wins: 4."];
        Assert.Equal(expected, schedule[0].CircuitFacts);
        Assert.Empty(schedule[1].CircuitFacts);
    }

    [Fact]
    public void CalendarRound_exposes_facts_and_flags_presence()
    {
        var session = new FakeCareerSession();
        session.ScheduleEntries.Add(new SeasonScheduleEntry
        {
            Round = 1, Name = "Dutch GP", Date = "1978-08-27", RealVenue = "Circuit Zandvoort",
            Ams2TrackName = "Zandvoort", Laps = 75, Kind = SeasonTrackKind.RealVenue,
            CircuitLayoutId = "zandvoort",
            CircuitFacts = ["First Grand Prix here: 1952 — 25 World Championship GPs held coming into this season."],
        });
        session.ScheduleEntries.Add(new SeasonScheduleEntry
        {
            Round = 2, Name = "German GP", Date = "1978-07-30", RealVenue = "Hockenheimring",
            Ams2TrackName = "Hockenheim", Laps = 45, Kind = SeasonTrackKind.RealVenue,
        });
        var vm = new CalendarViewModel(session);

        Assert.True(vm.Rounds[0].HasCircuitFacts);
        Assert.Contains("First Grand Prix here: 1952", Assert.Single(vm.Rounds[0].CircuitFacts));
        Assert.False(vm.Rounds[1].HasCircuitFacts);
        Assert.Empty(vm.Rounds[1].CircuitFacts);
    }
}
