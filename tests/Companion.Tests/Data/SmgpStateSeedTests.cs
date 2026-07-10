using System.Text.Json;
using Companion.Core.Career;
using Companion.Core.Json;
using Companion.Core.Packs;
using Companion.Core.Smgp;
using Companion.Data;
using Companion.Tests.ViewModels;
using Companion.ViewModels.Services;

namespace Companion.Tests.Data;

/// <summary>
/// The SMGP replica mode's per-career gate (M3 slice 1) — <see cref="PlayerCareerState.Smgp"/>
/// seeds ONLY when the pack declares <c>careerStyle "smgp"</c> AND the creation request opted in
/// (mirroring <see cref="PlayerCareerState.FormAware"/>). Everything else — the missing flag, a
/// normal pack, every existing career — stays null, serializes to nothing, and folds exactly as
/// before; the seeded career itself folds untouched state through every round and re-simulates
/// byte-identically (no battle rows exist until slice 2 stores rival calls).
/// </summary>
public sealed class SmgpStateSeedTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("companion-smgp-seed-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static SeasonPack SmgpPack() => TestPackBuilder.TwoRoundPack() is var basePack
        ? basePack with { Manifest = basePack.Manifest with { CareerStyle = SmgpRules.CareerStyle } }
        : throw new InvalidOperationException();

    [Fact]
    public void SmgpCareer_SeedsState_CarriesItForward_AndReplaysByteIdentically()
    {
        var pack = SmgpPack();
        var (careerPath, seasonId) = CreateAndFoldTwoRounds(pack, "smgp.ams2career", smgpMode: true);

        using var db = CareerDatabase.Open(careerPath);
        var start = StateStore.ReadPlayerState(db, seasonId, StateStore.StageStart)!;
        Assert.NotNull(start.Smgp);
        Assert.Equal(TestPackBuilder.StockLivery2, start.Smgp!.CurrentSeatLivery);
        Assert.Empty(start.Smgp.Tallies);
        Assert.Empty(start.Smgp.AiSeatOverrides);
        Assert.Equal(0, start.Smgp.Titles);
        Assert.False(start.Smgp.CareerOver);

        // The fold carries the state forward each round via record `with` — the last folded
        // round still holds the seat (nothing battled it away; no rival calls exist yet).
        var lastRound = StateStore.ReadRoundPlayerState(db, seasonId, 2)!;
        Assert.NotNull(lastRound.Player.Smgp);
        Assert.Equal(TestPackBuilder.StockLivery2, lastRound.Player.Smgp!.CurrentSeatLivery);

        // ...and the whole career re-simulates byte-identically (the determinism gate).
        var rules = CareerRulesData.Load(ViewModelTestData.RulesDirectory);
        var report = ReplayService.Resimulate(db, pack, unchecked((ulong)Seed), new ReplaySimInputs
        {
            AgingCurves = rules.AgingCurves,
            Archetypes = rules.Archetypes,
            Headlines = rules.Headlines,
            PlayerDriverId = "driver.hulme",
            PlayerAge = 30,
            CharacterRules = rules.Character,
        });
        Assert.True(report.Identical,
            $"diverged: {report.FirstDivergence?.Reason} stored={report.FirstDivergence?.StoredDeltaJson} " +
            $"regenerated={report.FirstDivergence?.RegeneratedDeltaJson}");
        Assert.True(report.ComparedRows > 0);
    }

    [Fact]
    public void SmgpPack_WithoutTheOptIn_SeedsNothing()
    {
        var (careerPath, seasonId) = CreateAndFoldTwoRounds(SmgpPack(), "no-opt-in.ams2career", smgpMode: false);

        using var db = CareerDatabase.Open(careerPath);
        Assert.Null(StateStore.ReadPlayerState(db, seasonId, StateStore.StageStart)!.Smgp);
        Assert.Null(StateStore.ReadRoundPlayerState(db, seasonId, 2)!.Player.Smgp);
    }

    [Fact]
    public void NormalPack_IgnoresTheOptIn()
    {
        // The pack's declared style is the other half of the gate — a stray flag on a normal
        // season seeds nothing (the wizard never sets it, but the request is public surface).
        var (careerPath, seasonId) = CreateAndFoldTwoRounds(
            TestPackBuilder.TwoRoundPack(), "normal-pack.ams2career", smgpMode: true);

        using var db = CareerDatabase.Open(careerPath);
        Assert.Null(StateStore.ReadPlayerState(db, seasonId, StateStore.StageStart)!.Smgp);
    }

    [Fact]
    public void SmgpBriefing_SurfacesTheRivalPanelData_OnlyInTheMode()
    {
        // The smgp career briefs the panel: the game's header format, the D.P. readout, and
        // every AI seat as a namable rival (2 seats, 1 AI). A normal career briefs null.
        string packDirectory = Path.Combine(_root, "packs", "briefing");
        TestPackBuilder.Write(SmgpPack(), packDirectory);
        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs", "briefing"),
            library: TestPackBuilder.Library());
        using var session = CareerSessionService.CreateCareer(
            new CareerCreationRequest
            {
                PackDirectory = packDirectory,
                CareerFilePath = Path.Combine(_root, "careers", "briefing.ams2career"),
                CareerName = "Briefing",
                MasterSeed = Seed,
                PlayerLiveryName = TestPackBuilder.StockLivery2,
                SmgpMode = true,
            },
            environment);

        var briefing = session.CurrentSmgpBriefing();
        Assert.NotNull(briefing);
        Assert.Equal("ROUND 1 · ROUND 1", briefing!.RoundHeader); // the test round's name IS "Round 1"
        Assert.Equal("0 D.P.", briefing.PointsLine);
        Assert.False(briefing.CareerOver);
        Assert.Null(briefing.ForcedChallengerDriverId);
        var rival = Assert.Single(briefing.Rivals);
        Assert.Equal("driver.brabham", rival.DriverId);
        Assert.False(rival.OfferOnWin);

        string normalDirectory = Path.Combine(_root, "packs", "briefing-normal");
        TestPackBuilder.Write(TestPackBuilder.TwoRoundPack(), normalDirectory);
        using var normal = CareerSessionService.CreateCareer(
            new CareerCreationRequest
            {
                PackDirectory = normalDirectory,
                CareerFilePath = Path.Combine(_root, "careers", "briefing-normal.ams2career"),
                CareerName = "Normal",
                MasterSeed = Seed,
                PlayerLiveryName = TestPackBuilder.StockLivery2,
            },
            environment);
        Assert.Null(normal.CurrentSmgpBriefing());
    }

    [Fact]
    public void StateWithoutSmgp_SerializesWithoutTheKey()
    {
        // WhenWritingNull is the byte-identity contract for every existing career's player_state.
        string json = JsonSerializer.Serialize(new PlayerCareerState { Reputation = 45.0 }, CoreJson.Options);
        Assert.DoesNotContain("smgp", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CanonicalHelpers_KeepOrdinalKeyOrder()
    {
        // The With* helpers re-sort so the serialized blob is canonical no matter the battle order.
        var state = new SmgpState { CurrentSeatLivery = "Seat A" }
            .WithTally("driver.zeta", new SmgpBattleTally { PlayerStreak = 1 })
            .WithTally("driver.alpha", new SmgpBattleTally { RivalStreak = 1 })
            .WithAiSeatOverride("driver.m", "Seat M")
            .WithAiSeatOverride("driver.b", "Seat B");

        Assert.Equal(["driver.alpha", "driver.zeta"], state.Tallies.Keys);
        Assert.Equal(["driver.b", "driver.m"], state.AiSeatOverrides.Keys);
        Assert.Equal(1, state.TallyFor("driver.zeta").PlayerStreak);
        Assert.Equal(SmgpBattleTally.Empty, state.TallyFor("driver.never-battled"));
    }

    private const long Seed = 20260710;

    /// <summary>Creates a career on <paramref name="pack"/> and folds both rounds with the whole
    /// grid classified, returning the career file path + season id for direct DB assertions.</summary>
    private (string CareerPath, long SeasonId) CreateAndFoldTwoRounds(SeasonPack pack, string fileName, bool smgpMode)
    {
        string packDirectory = Path.Combine(_root, "packs", Path.GetFileNameWithoutExtension(fileName));
        TestPackBuilder.Write(pack, packDirectory);
        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs", Path.GetFileNameWithoutExtension(fileName)),
            library: TestPackBuilder.Library());

        string careerPath = Path.Combine(_root, "careers", fileName);
        using (var session = CareerSessionService.CreateCareer(
                   new CareerCreationRequest
                   {
                       PackDirectory = packDirectory,
                       CareerFilePath = careerPath,
                       CareerName = "SMGP Seed",
                       MasterSeed = Seed,
                       PlayerLiveryName = TestPackBuilder.StockLivery2,
                       SmgpMode = smgpMode,
                   },
                   environment))
        {
            for (int round = 0; round < 2; round++)
            {
                var seats = session.CurrentGrid();
                session.Apply(new ResultDraft
                {
                    Classified = seats.Select(s => s.DriverId).ToList(),
                    DidNotFinish = new Dictionary<string, string>(),
                    Disqualified = [],
                });
            }
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using var db = CareerDatabase.Open(careerPath);
        return (careerPath, CareerStore.ReadSeasons(db).Single().Id);
    }
}
