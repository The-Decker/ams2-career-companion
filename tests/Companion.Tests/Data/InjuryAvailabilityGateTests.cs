using Companion.Core.Career;
using Companion.Core.Character;
using Companion.Core.Determinism;
using Companion.Core.Packs;
using Companion.Data;
using Companion.Tests.ViewModels;
using Companion.ViewModels.Services;

namespace Companion.Tests.Data;

/// <summary>
/// The SERVICE-LEVEL injury availability gate + the medical-record projection. An injured player's
/// round is auto-simulated, never entered manually, <see cref="CareerSessionService.Apply"/> and
/// <see cref="CareerSessionService.Preview"/> must throw for EVERY caller while the folded player
/// state carries RaceSuspensionRemaining &gt; 0 (or a season-ending injury), and the round must still
/// fold through <see cref="ICareerSession.AutoSimulateRound"/>, healing the suspension so the next
/// round applies normally again. <see cref="ICareerSession.InjuryHistory"/> then projects the same
/// journaled accident verbatim. Outcomes are forced deterministically by engineering the driver's
/// durability against the (known) round-1 accident roll, the AutoSimFoldTests pattern.
/// </summary>
public sealed class InjuryAvailabilityGateTests : IDisposable
{
    private const string PlayerId = "driver.hulme";
    private const long Seed = 20260712;
    private const int Year = 1967;

    private readonly string _root = Directory.CreateTempSubdirectory("companion-injury-gate-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static CharacterProfile Character(double durability) => new()
    {
        Name = "Crash McTest",
        Stats = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["pace"] = 0.55, ["oneLap"] = 0.50, ["craft"] = 0.50, ["racecraft"] = 0.50,
            ["adaptability"] = 0.50, ["marketability"] = 0.55, ["durability"] = durability,
        },
        PerkIds = [],
        CpUnspent = 0,
    };

    /// <summary>The durability that makes round 1's (known, seeded) accident roll resolve to a target
    /// effective d500, so an injury outcome is forced deterministically. offset = (durability-0.5)*scale;
    /// effective = roll - offset ⇒ durability = 0.5 + (roll - target)/scale.</summary>
    private static double DurabilityForEffective(int targetEffective)
    {
        int roll = new StreamFactory(unchecked((ulong)Seed))
            .CreateStream(CareerStreams.Accident, Year, 1, "player").NextInt(1, 501);
        return 0.5 + (roll - targetEffective) / AccidentModel.DefaultRules.SafetyDurabilityScale;
    }

    private CareerSessionService Create(string name, double durability, SeasonPack pack)
    {
        string packDirectory = Path.Combine(_root, name, "pack");
        TestPackBuilder.Write(pack, packDirectory);
        return CareerSessionService.CreateCareer(
            new CareerCreationRequest
            {
                PackDirectory = packDirectory,
                CareerFilePath = Path.Combine(_root, name, name + ".ams2career"),
                CareerName = name,
                MasterSeed = Seed,
                PlayerLiveryName = TestPackBuilder.StockLivery2,
                Character = Character(durability),
                Mortality = MortalityMode.Normal,
            },
            ViewModelTestData.Environment(
                documentsDirectory: Path.Combine(_root, name, "docs"),
                library: TestPackBuilder.Library()));
    }

    private static SeasonPack NRoundPack(int rounds)
    {
        var basePack = TestPackBuilder.TwoRoundPack();
        var list = new List<PackRound> { basePack.Season.Rounds[0], basePack.Season.Rounds[1] };
        for (int n = 3; n <= rounds; n++)
            list.Add(TestPackBuilder.Round(n, $"1967-{n:D2}-01"));
        // The Entry helper hardcodes Rounds = "1-2"; widen every entry to cover the added rounds so the
        // full field is entered all season (otherwise later rounds resolve to just the player, no AI).
        return basePack with
        {
            Season = basePack.Season with { Rounds = list },
            Entries = basePack.Entries.Select(e => e with { Rounds = $"1-{rounds}" }).ToList(),
        };
    }

    private static void ApplyPlayerAccident(ICareerSession session)
    {
        var seats = session.CurrentGrid();
        session.Apply(new ResultDraft
        {
            Classified = seats.Where(s => s.DriverId != PlayerId).Select(s => s.DriverId).ToList(),
            DidNotFinish = new Dictionary<string, string> { [PlayerId] = "a" },
            Disqualified = [],
            PlayerAccidentSeverity = AccidentSeverity.Heavy,
        });
    }

    private static ResultDraft NormalRoundDraft(ICareerSession session)
    {
        var seats = session.CurrentGrid();
        return new ResultDraft
        {
            Classified = seats.Select(s => s.DriverId).ToList(),
            DidNotFinish = new Dictionary<string, string>(),
            Disqualified = [],
        };
    }

    [Fact]
    public void InjuredRound_RefusesApplyAndPreview_AutoSimHeals_ThenApplyWorksAgain()
    {
        var pack = NRoundPack(3);
        using var session = Create("gate", DurabilityForEffective(300), pack); // heavy → miss 1 race

        ApplyPlayerAccident(session);                              // round 1: injured, miss 1
        Assert.Equal(1, session.PlayerMortality().RaceSuspensionRemaining);
        Assert.Equal(2, session.Summary.CurrentRound);

        // (a) A manual Apply of the sat-out round is refused at the SERVICE layer.
        var draft = NormalRoundDraft(session);
        var applyEx = Assert.Throws<InvalidOperationException>(() => session.Apply(draft));
        Assert.Contains("The driver is injured", applyEx.Message);
        Assert.Contains("auto-simulated", applyEx.Message);

        // (b) Preview is gated by the same rule, no path can even score an unfit player.
        var previewEx = Assert.Throws<InvalidOperationException>(() => session.Preview(draft));
        Assert.Contains("The driver is injured", previewEx.Message);
        Assert.Contains("auto-simulated", previewEx.Message);

        // Neither refusal moved the career.
        Assert.Equal(2, session.Summary.CurrentRound);
        Assert.Equal(1, session.PlayerMortality().RaceSuspensionRemaining);
        Assert.NotNull(session.CurrentSitOut());

        // (c) The sanctioned path folds the round and heals the suspension.
        session.AutoSimulateRound();
        Assert.Equal(0, session.PlayerMortality().RaceSuspensionRemaining);
        Assert.False(session.PlayerMortality().SeasonEndingInjury);
        Assert.Null(session.CurrentSitOut());
        Assert.Equal(3, session.Summary.CurrentRound);

        // (d) Fit again: the next round applies manually, exactly as before the injury.
        session.Apply(NormalRoundDraft(session));
        Assert.True(session.Summary.SeasonComplete);
    }

    // ---- the medical record (InjuryHistory projection) over the same forced scenario ----

    [Fact]
    public void InjuryHistory_ProjectsTheForcedMinorInjury_Verbatim()
    {
        var pack = TestPackBuilder.TwoRoundPack();
        using var session = Create("medrecord", DurabilityForEffective(300), pack); // heavy → miss 1

        Assert.Empty(session.InjuryHistory());                     // clean sheet before the accident

        ApplyPlayerAccident(session);                              // round 1: minor injury

        var entry = Assert.Single(session.InjuryHistory());
        Assert.Equal(1, entry.SeasonOrdinal);
        Assert.Equal(Year, entry.SeasonYear);
        Assert.Equal(1, entry.Round);
        Assert.Equal("minorInjury", entry.Outcome);
        Assert.True(entry.MissRaces > 0);
        Assert.StartsWith("Injured", entry.Label);
    }

    [Fact]
    public void InjuryHistory_IsEmptyForAnUninjuredCareer()
    {
        var pack = TestPackBuilder.TwoRoundPack();
        using var session = Create("healthy", durability: 50.0, pack); // hugely durable → never hurt

        session.Apply(NormalRoundDraft(session));
        session.Apply(NormalRoundDraft(session));
        Assert.True(session.Summary.SeasonComplete);
        Assert.Empty(session.InjuryHistory());
    }
}
