using Companion.Core.Grid;
using Companion.ViewModels.ResultEntry;

namespace Companion.Tests.ViewModels;

/// <summary>
/// The product target behind the grammar: a full 26-car race result — classifications,
/// a penalty re-position, a DSQ, reasoned and bulk DNFs — entered end-to-end through the
/// keyboard grammar in well under 120 keystrokes (the proxy for the &lt;90-second budget:
/// the research digest clocks the grammar at ~80s for 26 cars).
/// </summary>
public class ResultEntrySimulationTests
{
    private const string PlayerId = "d.player";

    private static readonly (string Id, string Name, string Number)[] Roster =
    [
        ("d.brabham", "Jack Brabham", "1"),
        ("d.hulme", "Denny Hulme", "2"),
        ("d.clark", "Jim Clark", "3"),
        ("d.ghill", "Graham Hill", "4"),
        ("d.stewart", "Jackie Stewart", "5"),
        ("d.amon", "Chris Amon", "6"),
        ("d.surtees", "John Surtees", "7"),
        ("d.rodriguez", "Pedro Rodriguez", "8"),
        ("d.gurney", "Dan Gurney", "9"),
        ("d.mclaren", "Bruce McLaren", "10"),
        ("d.rindt", "Jochen Rindt", "11"),
        (PlayerId, "Mike Kobra", "12"),
        ("d.siffert", "Jo Siffert", "13"),
        ("d.spence", "Mike Spence", "14"),
        ("d.bonnier", "Jo Bonnier", "15"),
        ("d.ligier", "Guy Ligier", "16"),
        ("d.irwin", "Chris Irwin", "17"),
        ("d.attwood", "Richard Attwood", "18"),
        ("d.courage", "Piers Courage", "19"),
        ("d.hobbs", "David Hobbs", "20"),
        ("d.moser", "Silvio Moser", "21"),
        ("d.williams", "Jonathan Williams", "22"),
        ("d.wilson", "Vic Wilson", "23"),
        ("d.anderson", "Bob Anderson", "24"),
        ("d.ahrens", "Kurt Ahrens", "25"),
        ("d.rees", "Alan Rees", "26"),
    ];

    private static IReadOnlyList<GridSeat> Grid() =>
        Roster.Select(r => ResultEntryViewModelTests.Seat(r.Id, r.Name, r.Number, r.Id == PlayerId))
            .ToArray();

    [Fact]
    public void Full26CarResult_EnteredThroughTheGrammar_InUnder120Keystrokes()
    {
        var vm = new ResultEntryViewModel(Grid(), PlayerId);
        var k = new ResultEntryViewModelTests.Keys(vm);

        // Mirror of the classification we build, mutated alongside the keystrokes so the
        // final draft can be asserted without hand-numbering 20 positions.
        var expected = new List<string>();
        void Place(string keys, string driverId)
        {
            k.Line(keys);
            expected.Add(driverId);
        }

        // -- 20 classified: numbers where short, surname prefixes where shorter, 'me' --
        Place("3", "d.clark");
        Place("hu", "d.hulme");
        Place("me", PlayerId);
        Place("1", "d.brabham");      // exact number beats the 1x-prefixed cars
        Place("st", "d.stewart");     // Stewart vs Surtees/Siffert/Spence: two letters suffice
        Place("am", "d.amon");        // vs Attwood/Anderson/Ahrens
        Place("su", "d.surtees");
        Place("8", "d.rodriguez");
        Place("gu", "d.gurney");
        Place("10", "d.mclaren");
        Place("ri", "d.rindt");       // vs Rodriguez/Rees
        Place("13", "d.siffert");
        Place("sp", "d.spence");
        Place("15", "d.bonnier");
        Place("li", "d.ligier");
        Place("ir", "d.irwin");
        Place("18", "d.attwood");
        Place("co", "d.courage");
        Place("ho", "d.hobbs");

        // Ambiguous on purpose: "wi" hits Williams then Wilson; Tab picks Wilson.
        k.Type("wi");
        Assert.Equal(new[] { "d.williams", "d.wilson" }, vm.Candidates.Select(c => c.DriverId));
        k.Tab();
        k.Enter();
        expected.Add("d.wilson");

        // -- stewards: Hobbs gets a two-place penalty, P19 -> P17 --
        k.Line("ho 17");
        expected.Remove("d.hobbs");
        expected.Insert(16, "d.hobbs");

        // -- Moser disqualified --
        k.Line("21q");

        // -- F8, then the retirements: two with reasons, the rest bulk-confirmed --
        k.F8();
        k.Line("4 m");   // Graham Hill, mechanical
        k.Line("wil a"); // Williams (Wilson is placed, so "wil" is unambiguous), accident
        k.Enter();       // bulk: Anderson (list order)
        k.Enter();       // bulk: Ahrens
        k.Enter();       // bulk: Rees

        // -- the assertions the 90-second target rides on --
        Assert.True(k.Count < 120, $"Full 26-car entry took {k.Count} keystrokes; budget is <120.");
        Assert.True(vm.IsComplete, $"Entry incomplete: {vm.ProgressText}");
        Assert.Equal("26/26 placed", vm.ProgressText);

        var draft = vm.BuildDraft();
        Assert.Equal(20, draft.Classified.Count);
        Assert.Equal(expected, draft.Classified);
        Assert.Equal(new[] { "d.moser" }, draft.Disqualified);
        Assert.Equal(5, draft.DidNotFinish.Count);
        Assert.Equal("m", draft.DidNotFinish["d.ghill"]);
        Assert.Equal("a", draft.DidNotFinish["d.williams"]);
        Assert.Equal("o", draft.DidNotFinish["d.anderson"]);
        Assert.Equal("o", draft.DidNotFinish["d.ahrens"]);
        Assert.Equal("o", draft.DidNotFinish["d.rees"]);
    }

    [Fact]
    public void Full26CarResult_TimedWithAFakeClock_TracksTheEntrySession()
    {
        var clock = new ResultEntryViewModelTests.FakeClock();
        var vm = new ResultEntryViewModel(Grid(), PlayerId, clock);
        var k = new ResultEntryViewModelTests.Keys(vm);

        clock.Advance(TimeSpan.FromMinutes(5)); // the race itself; timer must not run yet
        Assert.Equal(TimeSpan.Zero, vm.Elapsed);

        k.Line("3");
        clock.Advance(TimeSpan.FromSeconds(80)); // the research-digest pace for 26 cars
        k.Line("hu");

        Assert.Equal(TimeSpan.FromSeconds(80), vm.Elapsed);
        Assert.Equal("1:20", vm.ElapsedText);
    }
}
