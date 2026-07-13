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

    /// <summary>The two-round pack, smgp-styled, with the player's seat on its OWN team — the
    /// briefing's rival list excludes teammates, so a one-team pack would offer no rivals.</summary>
    private static SeasonPack SmgpPack()
    {
        var basePack = TestPackBuilder.TwoRoundPack(secondTeamId: "team.hulme");
        return basePack with
        {
            Manifest = basePack.Manifest with { CareerStyle = SmgpRules.CareerStyle },
            Teams =
            [
                // brabham = LEVEL C (prestige 3) so the LEVEL-D player can CHALLENGE him (D→C).
                basePack.Teams[0] with { Prestige = 3, BudgetTier = 3 },
                new Companion.Core.Packs.PackTeam
                {
                    Id = "team.hulme",
                    Name = "Hulme Racing",
                    CarVehicleIds = [TestPackBuilder.VintageCar],
                    Reliability = 0.9,
                    Prestige = 2, // LEVEL D — the player's team
                    BudgetTier = 2,
                },
            ],
        };
    }

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
            // The SMGP clean-swap player races as their OWN distinct driver, so replay scores them
            // under the synthetic id (not the seat's authored driver).
            PlayerDriverId = Companion.Core.Grid.RoundGridResolver.SyntheticPlayerDriverId,
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
        Assert.Equal("SEASON  —", briefing.SeasonLine); // no round scored yet
        Assert.Equal("", briefing.CareerLine);           // the player has no record yet
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

    /// <summary>A 4-car SMGP pack whose rounds cap the grid at 3 — so one car DNQs each round, and which
    /// one is the seeded per-career roll under test.</summary>
    private static SeasonPack SmgpDnqPack()
    {
        var basePack = TestPackBuilder.TwoRoundPack(secondTeamId: "team.hulme");
        var grid = new PackRoundGrid
        {
            Size = 3,
            StarterDriverIds = ["driver.brabham", "driver.hulme", "driver.c"], // baked default; the transform re-rolls
        };
        return basePack with
        {
            Manifest = basePack.Manifest with { CareerStyle = SmgpRules.CareerStyle },
            Teams =
            [
                basePack.Teams[0] with { Prestige = 3, BudgetTier = 3 },
                new PackTeam
                {
                    Id = "team.hulme", Name = "Hulme Racing", CarVehicleIds = [TestPackBuilder.VintageCar],
                    Reliability = 0.9, Prestige = 2, BudgetTier = 2,
                },
            ],
            Drivers =
            [
                TestPackBuilder.Driver("driver.brabham"), TestPackBuilder.Driver("driver.hulme"),
                TestPackBuilder.Driver("driver.c"), TestPackBuilder.Driver("driver.d"),
            ],
            Entries =
            [
                TestPackBuilder.Entry("team.brabham", "driver.brabham", "1", TestPackBuilder.StockLivery1),
                TestPackBuilder.Entry("team.hulme", "driver.hulme", "2", TestPackBuilder.StockLivery2),
                TestPackBuilder.Entry("team.brabham", "driver.c", "3", "Stock Livery #3"),
                TestPackBuilder.Entry("team.brabham", "driver.d", "4", "Stock Livery #4"),
            ],
            Season = basePack.Season with
            {
                Rounds = basePack.Season.Rounds.Select(r => r with { Grid = grid }).ToList(),
            },
        };
    }

    [Fact]
    public void SmgpDnqField_IsSeededPerCareer_AndPinned()
    {
        var pack = SmgpDnqPack();
        string packDir = Path.Combine(_root, "packs", "dnq");
        TestPackBuilder.Write(pack, packDir);
        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs", "dnq"), library: TestPackBuilder.Library());

        CareerSessionService Create(long seed, string name) => CareerSessionService.CreateCareer(
            new CareerCreationRequest
            {
                PackDirectory = packDir, CareerFilePath = Path.Combine(_root, "careers", name + ".ams2career"),
                CareerName = name, MasterSeed = seed, PlayerLiveryName = TestPackBuilder.StockLivery2, SmgpMode = true,
            },
            environment);

        var genA = SmgpDnqField.Generate(pack, unchecked((ulong)111L));
        var genB = SmgpDnqField.Generate(pack, unchecked((ulong)222L));

        using (var a = Create(111, "dnq-a"))
        using (var b = Create(222, "dnq-b"))
        {
            // Each career pinned exactly ITS seed's roll (the creation transform applied the seed).
            foreach (var round in a.Pack.Season.Rounds)
                Assert.Equal(genA[round.Round].ToHashSet(StringComparer.Ordinal),
                    round.Grid!.StarterDriverIds.ToHashSet(StringComparer.Ordinal));
            foreach (var round in b.Pack.Season.Rounds)
                Assert.Equal(genB[round.Round].ToHashSet(StringComparer.Ordinal),
                    round.Grid!.StarterDriverIds.ToHashSet(StringComparer.Ordinal));
        }

        // ...and the two seeds rolled a different field on at least one round — per-career, not fixed.
        Assert.True(pack.Season.Rounds.Any(r =>
            !genA[r.Round].ToHashSet(StringComparer.Ordinal).SetEquals(genB[r.Round])),
            "both seeds pinned the identical DNQ field every round — the roll isn't per-career.");
    }

    [Fact]
    public void SmgpDnqField_ReplaysByteIdentical()
    {
        var pack = SmgpDnqPack();
        string packDir = Path.Combine(_root, "packs", "dnq-replay");
        TestPackBuilder.Write(pack, packDir);
        string careerPath = Path.Combine(_root, "careers", "dnq-replay.ams2career");
        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs", "dnq-replay"), library: TestPackBuilder.Library());

        const long seed = 909;
        SeasonPack pinned;
        using (var session = CareerSessionService.CreateCareer(
                   new CareerCreationRequest
                   {
                       PackDirectory = packDir, CareerFilePath = careerPath, CareerName = "DNQ Replay",
                       MasterSeed = seed, PlayerLiveryName = TestPackBuilder.StockLivery2, SmgpMode = true,
                   },
                   environment))
        {
            session.Apply(new ResultDraft
            {
                Classified = session.CurrentGrid().Select(s => s.DriverId).ToList(),
                DidNotFinish = new Dictionary<string, string>(), Disqualified = [], SliderUsed = 100.0,
            });
            pinned = session.Pack; // the pinned pack carries the seeded starters
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using var db = CareerDatabase.Open(careerPath);
        var rules = CareerRulesData.Load(ViewModelTestData.RulesDirectory);
        var report = ReplayService.Resimulate(db, pinned, unchecked((ulong)seed), new ReplaySimInputs
        {
            AgingCurves = rules.AgingCurves,
            Archetypes = rules.Archetypes,
            Headlines = rules.Headlines,
            PlayerDriverId = Companion.Core.Grid.RoundGridResolver.SyntheticPlayerDriverId,
            PlayerAge = 30,
            CharacterRules = rules.Character,
        });

        Assert.True(report.Identical, $"diverged: {report.FirstDivergence?.Reason}");
        Assert.True(report.ComparedRows > 0);
    }

    [Fact]
    public void PlayerStats_AccrueFromResults_IntoTheRivalReadout_AndPaddock()
    {
        // A fresh SMGP career, then the player wins round 1 from pole — their live record must appear in
        // the rival readout (which retired "D.P.") and in their own Paddock card, built from zero.
        string packDirectory = Path.Combine(_root, "packs", "accrue");
        TestPackBuilder.Write(SmgpPack(), packDirectory);
        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs", "accrue"),
            library: TestPackBuilder.Library());
        using var session = CareerSessionService.CreateCareer(
            new CareerCreationRequest
            {
                PackDirectory = packDirectory,
                CareerFilePath = Path.Combine(_root, "careers", "accrue.ams2career"),
                CareerName = "Accrue",
                MasterSeed = Seed,
                PlayerLiveryName = TestPackBuilder.StockLivery2,
                SmgpMode = true,
            },
            environment);

        // Before any round: the readout is blank and the player's Paddock card is at zero.
        Assert.Equal("SEASON  —", session.CurrentSmgpBriefing()!.SeasonLine);
        Assert.Equal(0, session.SmgpPaddock()!.Drivers.First(d => d.IsPlayer).Career!.Wins);

        // Round 1: the player wins from pole (order them first; the AI second).
        var order = session.CurrentGrid()
            .OrderByDescending(s => s.IsPlayer)
            .Select(s => s.DriverId)
            .ToList();
        session.Apply(new ResultDraft
        {
            Classified = order,
            QualifyingOrder = order,
            DidNotFinish = new Dictionary<string, string>(),
            Disqualified = [],
        });

        // The rival readout (now round 2) shows the player's live season standing + career record.
        var briefing = session.CurrentSmgpBriefing()!;
        Assert.StartsWith("SEASON  P", briefing.SeasonLine);
        Assert.Contains("1 WIN", briefing.CareerLine);
        Assert.Contains("1 POLE", briefing.CareerLine);

        // The Paddock shows the player's own card, built from zero + the win/pole they just took.
        var player = session.SmgpPaddock()!.Drivers.First(d => d.IsPlayer);
        Assert.Equal(1, player.Career!.Wins);
        Assert.Equal(1, player.Career.Poles);
        Assert.Equal(1, player.Career.Podiums);
        Assert.Equal(0, player.Career.Titles); // the season is not complete
        Assert.NotNull(player.Season);
        Assert.Equal(1, player.Season!.Wins);
    }

    [Fact]
    public void SmgpPlayer_WithNoCharacterName_ShowsAStableName_NotTheBenchedAiOrRawId()
    {
        // The SMGP clean-swap player is their OWN distinct synthetic driver (id not in pack.Drivers),
        // sitting in a car whose authored AI is BENCHED. With no character name, every name-rendering
        // screen must show a stable default — never the benched AI it displaced, never the raw id.
        string packDirectory = Path.Combine(_root, "packs", "cleanswap-name");
        TestPackBuilder.Write(SmgpPack(), packDirectory);
        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs", "cleanswap-name"),
            library: TestPackBuilder.Library());
        using var session = CareerSessionService.CreateCareer(
            new CareerCreationRequest
            {
                PackDirectory = packDirectory,
                CareerFilePath = Path.Combine(_root, "careers", "cleanswap-name.ams2career"),
                CareerName = "Clean Swap",
                MasterSeed = Seed,
                PlayerLiveryName = TestPackBuilder.StockLivery2,
                SmgpMode = true,
            },
            environment);

        string synthetic = Companion.Core.Grid.RoundGridResolver.SyntheticPlayerDriverId;

        // The standings/review seam: a non-null identity carrying the default name (it feeds
        // StandingsViewModel's driverNames, so the Drivers tab + round matrix render "You", not
        // the raw "driver.player-entrant").
        var identity = session.PlayerIdentity();
        Assert.NotNull(identity);
        Assert.Equal(synthetic, identity!.Value.DriverId);
        Assert.Equal("You", identity.Value.DisplayName);

        // The grid-card seam: the player's own seat shows that name, NOT the benched AI whose car they
        // hold (the seat scores under the synthetic id but reads as the player).
        var playerSeat = Assert.Single(session.CurrentGrid(), s => s.IsPlayer);
        Assert.Equal(synthetic, playerSeat.DriverId);
        Assert.Equal("You", playerSeat.DriverName);
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
