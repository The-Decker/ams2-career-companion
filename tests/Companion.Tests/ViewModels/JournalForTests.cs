using Companion.ViewModels.Services;

namespace Companion.Tests.ViewModels;

/// <summary>
/// The "Why?" inspector's seam member <see cref="ICareerSession.JournalFor"/> (career-hub-design.md
/// §5, decisions 4 + 5) against a temp career DB and the REAL packs/f1-1967: applying a round writes
/// the sim's journal rows, and JournalFor walks them back into an ordered plain-language contribution
/// breakdown. These pin the projection's ordering (journal seq), determinism (identical on repeat +
/// reopen), provenance exclusion, and per-round narrowing.
/// </summary>
public sealed class JournalForTests : IDisposable
{
    private const string PlayerLivery = "Brabham-Repco #2 D. Hulme";

    private readonly string _root = Directory.CreateTempSubdirectory("companion-journalfor-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // SQLite WAL sidecars can outlive the connection briefly on Windows.
        }
    }

    private CareerCreationRequest Request() => new()
    {
        PackDirectory = ViewModelTestData.RealPackDirectory,
        CareerFilePath = Path.Combine(_root, "career.ams2career"),
        CareerName = "Test 1967",
        MasterSeed = 42,
        PlayerLiveryName = PlayerLivery,
    };

    private static void ApplyOneRound(CareerSessionService session)
    {
        var gridOrder = session.CurrentGrid().Select(s => s.DriverId).ToList();
        session.Apply(new ResultDraft
        {
            Classified = gridOrder, // full classification, nobody retires
            DidNotFinish = new Dictionary<string, string>(),
            Disqualified = [],
        });
    }

    [Fact]
    public void UnknownEntity_and_no_rows_return_the_empty_chain()
    {
        var environment = ViewModelTestData.Environment(Path.Combine(_root, "docs"));
        using var session = CareerSessionService.CreateCareer(Request(), environment);

        // Before any round the player has no folded race rows to walk.
        Assert.True(session.JournalFor("player").IsEmpty);
        // An entity id that never appears in the journal is empty (never throws).
        Assert.True(session.JournalFor("driver.nobody").IsEmpty);
        Assert.True(session.JournalFor("").IsEmpty);
    }

    [Fact]
    public void JournalFor_player_walks_the_round_into_labelled_contributions()
    {
        var environment = ViewModelTestData.Environment(Path.Combine(_root, "docs"));
        using var session = CareerSessionService.CreateCareer(Request(), environment);

        ApplyOneRound(session);

        var chain = session.JournalFor("player");
        Assert.False(chain.IsEmpty);
        Assert.Equal("player", chain.Entity);
        Assert.Null(chain.Round);

        // The ordered breakdown carries the sim's per-round player rows as labelled rows.
        var labels = chain.Contributions.Select(c => c.Label).ToList();
        Assert.Contains("Expected finish", labels);
        Assert.Contains("OPI", labels);
        Assert.Contains("Reputation", labels);
        Assert.Contains("Pace anchor", labels);

        // The plain-language summary is the Why? chip's sentence (generalised from WhyFromResult).
        Assert.Contains("finished", chain.Summary, StringComparison.OrdinalIgnoreCase);

        // Every row anchors to a real journal seq, ordered ascending (the deterministic walk).
        var seqs = chain.Contributions.Select(c => c.SourceSeq).ToList();
        Assert.All(seqs, s => Assert.True(s > 0));
        Assert.Equal(seqs.OrderBy(s => s).ToList(), seqs);

        // The race.result row exposes the finishing position as its value.
        var expected = chain.Contributions.First(c => c.Label == "Expected finish");
        Assert.False(string.IsNullOrEmpty(expected.Value));
    }

    [Fact]
    public void JournalFor_marks_a_player_retirement_row_as_DNF()
    {
        var environment = ViewModelTestData.Environment(Path.Combine(_root, "docs"));
        using var session = CareerSessionService.CreateCareer(Request(), environment);

        // Retire the player (the last grid slot is the player's seat is not guaranteed; retire
        // whoever the player drives by DNF'ing the player driver id explicitly).
        var gridOrder = session.CurrentGrid().Select(s => s.DriverId).ToList();
        string playerId = session.Summary.PlayerDriverId;
        session.Apply(new ResultDraft
        {
            Classified = gridOrder.Where(id => id != playerId).ToList(),
            DidNotFinish = new Dictionary<string, string> { [playerId] = "m" }, // mechanical
            Disqualified = [],
        });

        var chain = session.JournalFor("player", 1);
        var result = chain.Contributions.First(c => c.Label == "Expected finish");
        // The DNF cause serialises as an enum string, not boolean true, the inspector still
        // reads it as a retirement.
        Assert.Equal("DNF", result.Value);
        Assert.Contains("Retired", result.Detail);
        Assert.Contains("retired", chain.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void JournalFor_excludes_provenance_rows()
    {
        var environment = ViewModelTestData.Environment(Path.Combine(_root, "docs"));
        using var session = CareerSessionService.CreateCareer(Request(), environment);

        ApplyOneRound(session);

        // Apply writes a "result" provenance row on the "round" entity (bookkeeping about the
        // entry event), the inspector walks derived sim state only, never provenance.
        Assert.True(session.JournalFor("round").IsEmpty);
    }

    [Fact]
    public void JournalFor_round_narrows_to_a_single_round()
    {
        var environment = ViewModelTestData.Environment(Path.Combine(_root, "docs"));
        using var session = CareerSessionService.CreateCareer(Request(), environment);

        ApplyOneRound(session); // round 1
        ApplyOneRound(session); // round 2

        var wholeSeason = session.JournalFor("player");
        var round2 = session.JournalFor("player", 2);

        Assert.All(round2.Contributions, c => Assert.True(c.SourceSeq > 0));
        // Narrowing to one round is a strict subset of the whole-season walk.
        Assert.True(round2.Contributions.Count < wholeSeason.Contributions.Count);
        Assert.Contains("Round 2", round2.Title);
    }

    [Fact]
    public void JournalFor_is_deterministic_across_repeat_calls_and_reopen()
    {
        var environment = ViewModelTestData.Environment(Path.Combine(_root, "docs"));
        string careerPath = Request().CareerFilePath;

        List<(string Label, string? Value, long Seq)> first;
        using (var session = CareerSessionService.CreateCareer(Request(), environment))
        {
            ApplyOneRound(session);
            var a = session.JournalFor("player");
            var b = session.JournalFor("player");
            // Byte-stable within a session (pure function of the stored journal).
            Assert.Equal(
                a.Contributions.Select(c => (c.Label, c.Value, c.SourceSeq)),
                b.Contributions.Select(c => (c.Label, c.Value, c.SourceSeq)));
            first = a.Contributions.Select(c => (c.Label, c.Value, c.SourceSeq)).ToList();
        }

        // Reopening the career file re-derives the identical chain (no cached state).
        using var reopened = CareerSessionService.OpenCareer(careerPath, environment);
        var again = reopened.JournalFor("player")
            .Contributions.Select(c => (c.Label, c.Value, c.SourceSeq)).ToList();
        Assert.Equal(first, again);
    }
}
