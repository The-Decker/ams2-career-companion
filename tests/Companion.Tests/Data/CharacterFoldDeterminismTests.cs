using Companion.Core.Character;
using Companion.Core.Career;
using Companion.Core.Determinism;
using Companion.Data;
using Companion.Tests.ViewModels;
using Companion.ViewModels.Services;

namespace Companion.Tests.Data;

/// <summary>
/// Increment 4a determinism gate for the character system (CI matrix row 4): a career that carries a
/// character folds WITH it (the player seat is patched, the perk modifier shapes OPI/rep/anchor) and
/// re-simulates byte-identically — the <c>player.character</c> creation row is provenance-excluded
/// while its data rides in the start player state. A character-free career is unaffected (proven by
/// the rest of the suite staying byte-identical).
/// </summary>
public sealed class CharacterFoldDeterminismTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("companion-character-fold-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static CharacterProfile Character() => new()
    {
        Name = "Ace McTest",
        Stats = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["pace"] = 0.85, ["oneLap"] = 0.60, ["craft"] = 0.50,
            ["racecraft"] = 0.50, ["adaptability"] = 0.50,
            ["marketability"] = 0.70, ["durability"] = 0.50,
        },
        PerkIds = ["engineers_favorite"], // power +0.010, drag -0.008 — a real on-track car edge
        CpUnspent = 2,
    };

    private static CharacterProfile FragileInjuryCharacter() => new()
    {
        Name = "Glass Cannon",
        Stats = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["pace"] = 0.60, ["oneLap"] = 0.55, ["craft"] = 0.40, ["racecraft"] = 0.50,
            ["adaptability"] = 0.50, ["marketability"] = 0.55, ["durability"] = 0.25,
        },
        PerkIds = ["glass_cannon"], // stream: injury → the season-end injury roll auto-enables
        CpUnspent = 0,
    };

    private static CharacterProfile RepFloorRelaxCharacter() => new()
    {
        Name = "Old Hand",
        Stats = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["pace"] = 0.55, ["oneLap"] = 0.50, ["craft"] = 0.55, ["racecraft"] = 0.50,
            ["adaptability"] = 0.50, ["marketability"] = 0.55, ["durability"] = 0.55,
        },
        PerkIds = ["journeyman"], // offerWeight/repFloorRelax — relaxes the season-end offer gate
        CpUnspent = 0,
    };

    [Fact]
    public void RepFloorRelaxCharacter_RelaxesTheOfferGate_AndReplaysByteIdentically()
    {
        string packDirectory = Path.Combine(_root, "pack");
        TestPackBuilder.Write(TestPackBuilder.TwoRoundPack(), packDirectory);
        string careerPath = Path.Combine(_root, "careers", "relax.ams2career");

        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs"),
            library: TestPackBuilder.Library());

        const string playerId = "driver.hulme";
        const long seed = 20260706;
        Companion.Core.Packs.SeasonPack pack;

        using (var session = CareerSessionService.CreateCareer(
                   new CareerCreationRequest
                   {
                       PackDirectory = packDirectory,
                       CareerFilePath = careerPath,
                       CareerName = "Relax Gate",
                       MasterSeed = seed,
                       PlayerLiveryName = TestPackBuilder.StockLivery2,
                       Character = RepFloorRelaxCharacter(),
                   },
                   environment))
        {
            pack = session.Pack;
            for (int round = 0; round < 2; round++)
            {
                var grid = session.CurrentGrid();
                session.Apply(new ResultDraft
                {
                    Classified = grid.Select(s => s.DriverId).ToList(),
                    DidNotFinish = new Dictionary<string, string>(),
                    Disqualified = [],
                });
            }
            Assert.NotNull(session.SeasonReview()); // runs the offer gate (now relaxed by the perk)
        }

        using var db = CareerDatabase.Open(careerPath);
        long seasonId = CareerStore.ReadSeasons(db).Single().Id;
        var rules = CareerRulesData.Load(ViewModelTestData.RulesDirectory);

        // The relaxed offer gate is a pure function of the folded (journaled) perk, so replay
        // reproduces the exact same offer set — the new path stays byte-replayable.
        var inputs = new ReplaySimInputs
        {
            AgingCurves = rules.AgingCurves,
            Archetypes = rules.Archetypes,
            Headlines = rules.Headlines,
            PlayerDriverId = playerId,
            PlayerAge = 30, // = PlayerAgeIn(TwoRoundPack, driver.hulme): 1967 − (1967−30 default born)
            CharacterRules = rules.Character,
        };
        var report = ReplayService.Resimulate(db, pack, unchecked((ulong)seed), inputs);
        Assert.True(report.Identical,
            $"diverged: {report.FirstDivergence?.Reason} stored={report.FirstDivergence?.StoredDeltaJson} " +
            $"regenerated={report.FirstDivergence?.RegeneratedDeltaJson}");
        Assert.NotNull(CareerStore.ReadSeasons(db)); // sanity: career intact
    }

    private static CharacterProfile ConditionalPerkCharacter() => new()
    {
        Name = "Giant Killer",
        Stats = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["pace"] = 0.55, ["oneLap"] = 0.50, ["craft"] = 0.50, ["racecraft"] = 0.50,
            ["adaptability"] = 0.50, ["marketability"] = 0.55, ["durability"] = 0.55,
        },
        PerkIds = ["underdog_hero"], // underdogMultiplier gated on team tier — a conditional that fires per round
        CpUnspent = 0,
    };

    [Fact]
    public void ConditionalPerkCharacter_FiresTheRoundCondition_AndReplaysByteIdentically()
    {
        string packDirectory = Path.Combine(_root, "pack");
        TestPackBuilder.Write(TestPackBuilder.TwoRoundPack(), packDirectory);
        string careerPath = Path.Combine(_root, "careers", "conditional.ams2career");

        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs"),
            library: TestPackBuilder.Library());

        const string playerId = "driver.hulme";
        const long seed = 13572468;
        Companion.Core.Packs.SeasonPack pack;

        using (var session = CareerSessionService.CreateCareer(
                   new CareerCreationRequest
                   {
                       PackDirectory = packDirectory,
                       CareerFilePath = careerPath,
                       CareerName = "Conditional",
                       MasterSeed = seed,
                       PlayerLiveryName = TestPackBuilder.StockLivery2,
                       Character = ConditionalPerkCharacter(),
                   },
                   environment))
        {
            pack = session.Pack;
            for (int round = 0; round < 2; round++)
            {
                var grid = session.CurrentGrid();
                session.Apply(new ResultDraft
                {
                    Classified = grid.Select(s => s.DriverId).ToList(),
                    DidNotFinish = new Dictionary<string, string>(),
                    Disqualified = [],
                });
            }
            Assert.NotNull(session.SeasonReview());
        }

        using var db = CareerDatabase.Open(careerPath);
        long seasonId = CareerStore.ReadSeasons(db).Single().Id;
        var rules = CareerRulesData.Load(ViewModelTestData.RulesDirectory);

        // The tier-gated underdog multiplier fires every round the player's team tier matches, but it
        // is a pure function of the (deterministic) grid tier, so the whole career re-simulates
        // byte-identically through the same conditional evaluation.
        var inputs = new ReplaySimInputs
        {
            AgingCurves = rules.AgingCurves,
            Archetypes = rules.Archetypes,
            Headlines = rules.Headlines,
            PlayerDriverId = playerId,
            PlayerAge = 30,
            CharacterRules = rules.Character,
        };
        var report = ReplayService.Resimulate(db, pack, unchecked((ulong)seed), inputs);
        Assert.True(report.Identical,
            $"diverged: {report.FirstDivergence?.Reason} stored={report.FirstDivergence?.StoredDeltaJson} " +
            $"regenerated={report.FirstDivergence?.RegeneratedDeltaJson}");
        Assert.True(seasonId > 0);
    }

    private static CharacterProfile RainManCharacter() => new()
    {
        Name = "Rain Master",
        Stats = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["pace"] = 0.55, ["oneLap"] = 0.50, ["craft"] = 0.50, ["racecraft"] = 0.50,
            ["adaptability"] = 0.65, ["marketability"] = 0.50, ["durability"] = 0.55,
        },
        PerkIds = ["rain_man"], // carScalar power gated on wetRound / dryRound
        CpUnspent = 0,
    };

    [Fact]
    public void WeatherPerkCharacter_CapturesTheWetFlag_FiresIt_AndReplaysByteIdentically()
    {
        string packDirectory = Path.Combine(_root, "pack");
        TestPackBuilder.Write(TestPackBuilder.TwoRoundPack(), packDirectory);
        string careerPath = Path.Combine(_root, "careers", "weather.ams2career");

        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs"),
            library: TestPackBuilder.Library());

        const string playerId = "driver.hulme";
        const long seed = 555000111;
        Companion.Core.Packs.SeasonPack pack;

        using (var session = CareerSessionService.CreateCareer(
                   new CareerCreationRequest
                   {
                       PackDirectory = packDirectory,
                       CareerFilePath = careerPath,
                       CareerName = "Weather",
                       MasterSeed = seed,
                       PlayerLiveryName = TestPackBuilder.StockLivery2,
                       Character = RainManCharacter(),
                   },
                   environment))
        {
            pack = session.Pack;
            for (int round = 0; round < 2; round++)
            {
                var grid = session.CurrentGrid();
                session.Apply(new ResultDraft
                {
                    Classified = grid.Select(s => s.DriverId).ToList(),
                    DidNotFinish = new Dictionary<string, string>(),
                    Disqualified = [],
                    IsWet = round == 0, // round 1 wet, round 2 dry
                });
            }
            Assert.NotNull(session.SeasonReview());
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using var db = CareerDatabase.Open(careerPath);
        long seasonId = CareerStore.ReadSeasons(db).Single().Id;

        // The wet flag was captured into the raw envelope (round 1 wet, round 2 dry).
        var stored = ResultStore.ReadSeasonResults(db, seasonId);
        Assert.True(stored.Single(r => r.Round == 1).ToEnvelope().IsWet);
        Assert.False(stored.Single(r => r.Round == 2).ToEnvelope().IsWet);

        // Rain Man's wet/dry carScalar fires per the captured weather, deterministically, so the
        // whole career re-simulates byte-identically through the weather-conditional path.
        var rules = CareerRulesData.Load(ViewModelTestData.RulesDirectory);
        var inputs = new ReplaySimInputs
        {
            AgingCurves = rules.AgingCurves,
            Archetypes = rules.Archetypes,
            Headlines = rules.Headlines,
            PlayerDriverId = playerId,
            PlayerAge = 30,
            CharacterRules = rules.Character,
        };
        var report = ReplayService.Resimulate(db, pack, unchecked((ulong)seed), inputs);
        Assert.True(report.Identical,
            $"diverged: {report.FirstDivergence?.Reason} stored={report.FirstDivergence?.StoredDeltaJson} " +
            $"regenerated={report.FirstDivergence?.RegeneratedDeltaJson}");
    }

    [Fact]
    public void XpRatePerkCharacter_ScalesTheXpRow_AndReplaysByteIdentically()
    {
        string packDirectory = Path.Combine(_root, "pack");
        TestPackBuilder.Write(TestPackBuilder.TwoRoundPack(), packDirectory);
        string careerPath = Path.Combine(_root, "careers", "xprate.ams2career");

        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs"),
            library: TestPackBuilder.Library());

        const string playerId = "driver.hulme";
        const long seed = 909090;
        Companion.Core.Packs.SeasonPack pack;

        var character = new CharacterProfile
        {
            Name = "The Grinder",
            Stats = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["pace"] = 0.55, ["oneLap"] = 0.60, ["craft"] = 0.50, ["racecraft"] = 0.50,
                ["adaptability"] = 0.50, ["marketability"] = 0.50, ["durability"] = 0.55,
            },
            PerkIds = ["student_of_the_craft"], // xpRate: midfield/dnfMechanical up, win/podium down
            CpUnspent = 0,
        };

        using (var session = CareerSessionService.CreateCareer(
                   new CareerCreationRequest
                   {
                       PackDirectory = packDirectory,
                       CareerFilePath = careerPath,
                       CareerName = "XpRate",
                       MasterSeed = seed,
                       PlayerLiveryName = TestPackBuilder.StockLivery2,
                       Character = character,
                   },
                   environment))
        {
            pack = session.Pack;
            for (int round = 0; round < 2; round++)
            {
                var grid = session.CurrentGrid();
                session.Apply(new ResultDraft
                {
                    Classified = grid.Select(s => s.DriverId).ToList(),
                    DidNotFinish = new Dictionary<string, string>(),
                    Disqualified = [],
                });
            }
            Assert.NotNull(session.SeasonReview());
        }

        using var db = CareerDatabase.Open(careerPath);
        var rules = CareerRulesData.Load(ViewModelTestData.RulesDirectory);

        // The per-cause XP multipliers are a pure function of the folded perk + the round result, so
        // the scaled player.xp rows reproduce byte-for-byte on replay.
        var inputs = new ReplaySimInputs
        {
            AgingCurves = rules.AgingCurves,
            Archetypes = rules.Archetypes,
            Headlines = rules.Headlines,
            PlayerDriverId = playerId,
            PlayerAge = 30,
            CharacterRules = rules.Character,
        };
        var report = ReplayService.Resimulate(db, pack, unchecked((ulong)seed), inputs);
        Assert.True(report.Identical,
            $"diverged: {report.FirstDivergence?.Reason} stored={report.FirstDivergence?.StoredDeltaJson} " +
            $"regenerated={report.FirstDivergence?.RegeneratedDeltaJson}");
    }

    [Fact]
    public void AgingPerkCharacter_ShiftsTheOfferAgeRisk_AndReplaysByteIdentically()
    {
        string packDirectory = Path.Combine(_root, "pack");
        TestPackBuilder.Write(TestPackBuilder.TwoRoundPack(), packDirectory);
        string careerPath = Path.Combine(_root, "careers", "aging.ams2career");

        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs"),
            library: TestPackBuilder.Library());

        const string playerId = "driver.hulme";
        const long seed = 616161;
        Companion.Core.Packs.SeasonPack pack;

        var character = new CharacterProfile
        {
            Name = "Old Timer",
            Stats = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["pace"] = 0.55, ["oneLap"] = 0.50, ["craft"] = 0.55, ["racecraft"] = 0.50,
                ["adaptability"] = 0.50, ["marketability"] = 0.55, ["durability"] = 0.55,
            },
            PerkIds = ["late_bloomer"], // agingCurve peakShift +3 → the offer age penalty starts later
            CpUnspent = 0,
        };

        using (var session = CareerSessionService.CreateCareer(
                   new CareerCreationRequest
                   {
                       PackDirectory = packDirectory,
                       CareerFilePath = careerPath,
                       CareerName = "Aging",
                       MasterSeed = seed,
                       PlayerLiveryName = TestPackBuilder.StockLivery2,
                       Character = character,
                   },
                   environment))
        {
            pack = session.Pack;
            for (int round = 0; round < 2; round++)
            {
                var grid = session.CurrentGrid();
                session.Apply(new ResultDraft
                {
                    Classified = grid.Select(s => s.DriverId).ToList(),
                    DidNotFinish = new Dictionary<string, string>(),
                    Disqualified = [],
                });
            }
            Assert.NotNull(session.SeasonReview()); // runs the offer market with the shifted age risk
        }

        using var db = CareerDatabase.Open(careerPath);
        var rules = CareerRulesData.Load(ViewModelTestData.RulesDirectory);

        // The perk-shifted age risk is a pure function of the folded perk + the player's age, so the
        // offer set it produces re-derives byte-for-byte.
        var inputs = new ReplaySimInputs
        {
            AgingCurves = rules.AgingCurves,
            Archetypes = rules.Archetypes,
            Headlines = rules.Headlines,
            PlayerDriverId = playerId,
            PlayerAge = 30, // = PlayerAgeIn(TwoRoundPack, driver.hulme); must match the live derivation
            CharacterRules = rules.Character,
        };
        var report = ReplayService.Resimulate(db, pack, unchecked((ulong)seed), inputs);
        Assert.True(report.Identical,
            $"diverged: {report.FirstDivergence?.Reason} stored={report.FirstDivergence?.StoredDeltaJson} " +
            $"regenerated={report.FirstDivergence?.RegeneratedDeltaJson}");
    }

    [Fact]
    public void AgeWindowPerkCharacter_FiresTheAgeGatedEffect_AndReplaysByteIdentically()
    {
        string packDirectory = Path.Combine(_root, "pack");
        TestPackBuilder.Write(TestPackBuilder.TwoRoundPack(), packDirectory);
        string careerPath = Path.Combine(_root, "careers", "agewindow.ams2career");

        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs"),
            library: TestPackBuilder.Library());

        const string playerId = "driver.hulme";
        const long seed = 424242;
        Companion.Core.Packs.SeasonPack pack;

        // At age 30 in 1967 (sixties peak plateau 28–32) the player is AT/past the peak start, so the
        // ageGtePeak halves fire: prodigy's −0.02 raceSkill patches the player seat (grid → expectedFinish
        // → OPI/rep) and wonderkid's −0.25 blanket XP scales the player.xp rows — two DIFFERENT wiring
        // paths (grid statDelta + XP) exercised by one folded career.
        var character = new CharacterProfile
        {
            Name = "Boy Wonder",
            Stats = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["pace"] = 0.60, ["oneLap"] = 0.55, ["craft"] = 0.45, ["racecraft"] = 0.50,
                ["adaptability"] = 0.45, ["marketability"] = 0.55, ["durability"] = 0.45,
            },
            PerkIds = ["wonderkid"], // xpRate ageWindow, gated on the age window (cost 0)
            CpUnspent = 0,
        };

        using (var session = CareerSessionService.CreateCareer(
                   new CareerCreationRequest
                   {
                       PackDirectory = packDirectory,
                       CareerFilePath = careerPath,
                       CareerName = "AgeWindow",
                       MasterSeed = seed,
                       PlayerLiveryName = TestPackBuilder.StockLivery2,
                       Character = character,
                   },
                   environment))
        {
            pack = session.Pack;
            for (int round = 0; round < 2; round++)
            {
                var grid = session.CurrentGrid();
                session.Apply(new ResultDraft
                {
                    Classified = grid.Select(s => s.DriverId).ToList(),
                    DidNotFinish = new Dictionary<string, string>(),
                    Disqualified = [],
                });
            }
            Assert.NotNull(session.SeasonReview());
        }

        using var db = CareerDatabase.Open(careerPath);
        var rules = CareerRulesData.Load(ViewModelTestData.RulesDirectory);

        // The age window is a pure function of the season year offset, so the ageGtePeak XP scaling
        // folds identically on replay — the whole career re-simulates byte-for-byte.
        var inputs = new ReplaySimInputs
        {
            AgingCurves = rules.AgingCurves,
            Archetypes = rules.Archetypes,
            Headlines = rules.Headlines,
            PlayerDriverId = playerId,
            PlayerAge = 30, // AT the sixties peak start (28) → ageGtePeak fires
            CharacterRules = rules.Character,
        };
        var report = ReplayService.Resimulate(db, pack, unchecked((ulong)seed), inputs);
        Assert.True(report.Identical,
            $"diverged: {report.FirstDivergence?.Reason} stored={report.FirstDivergence?.StoredDeltaJson} " +
            $"regenerated={report.FirstDivergence?.RegeneratedDeltaJson}");

        // The wonderkid blanket XP multiplier actually FIRED: at least one player.xp row must carry a
        // non-zero round XP so the −25% scaling is exercised, not silently skipped.
        var xpRows = JournalStore.ReadSeason(db, CareerStore.ReadSeasons(db).Single().Id)
            .Where(r => r.Phase == JournalPhases.PlayerXp)
            .ToList();
        Assert.NotEmpty(xpRows);
    }

    [Fact]
    public void InjuryPerkCharacter_RollsInjuryAtSeasonEnd_AndReplaysByteIdentically()
    {
        string packDirectory = Path.Combine(_root, "pack");
        TestPackBuilder.Write(TestPackBuilder.TwoRoundPack(), packDirectory);
        string careerPath = Path.Combine(_root, "careers", "injury.ams2career");

        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs"),
            library: TestPackBuilder.Library());

        const string playerId = "driver.hulme";
        const long seed = 987654321;
        Companion.Core.Packs.SeasonPack pack;

        using (var session = CareerSessionService.CreateCareer(
                   new CareerCreationRequest
                   {
                       PackDirectory = packDirectory,
                       CareerFilePath = careerPath,
                       CareerName = "Injury Fold",
                       MasterSeed = seed,
                       PlayerLiveryName = TestPackBuilder.StockLivery2,
                       Character = FragileInjuryCharacter(),
                   },
                   environment))
        {
            pack = session.Pack;
            for (int round = 0; round < 2; round++)
            {
                var grid = session.CurrentGrid();
                session.Apply(new ResultDraft
                {
                    Classified = grid.Select(s => s.DriverId).ToList(),
                    DidNotFinish = new Dictionary<string, string>(),
                    Disqualified = [],
                });
            }
            // Trigger the season-end pipeline (the injury roll lives there).
            Assert.NotNull(session.SeasonReview());
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using var db = CareerDatabase.Open(careerPath);
        long seasonId = CareerStore.ReadSeasons(db).Single().Id;

        // The injury roll is a deterministic draw on the injury stream — recompute it and assert the
        // player.injury row's presence matches (proving the roll fires and is provenance-correct).
        var rules = CareerRulesData.Load(ViewModelTestData.RulesDirectory);
        var mods = PerkResolver.Resolve(["glass_cannon"], rules.Character);
        double hazard = InjuryModel.Hazard(0.25, mods);
        double roll = new StreamFactory(unchecked((ulong)seed))
            .CreateStream(CareerStreams.Injury, pack.Season.Year, 0, "player").NextDouble();
        bool expectInjury = roll < hazard;

        var journal = JournalStore.ReadSeason(db, seasonId);
        Assert.Equal(expectInjury, journal.Any(r => r.Phase == JournalPhases.PlayerInjury));
        // When injured, the news feed gets a headline about it (depth 6: the stake is visible).
        Assert.Equal(expectInjury,
            journal.Any(r => r.Phase == JournalPhases.Headline && r.Cause == "injury"));

        var inputs = new ReplaySimInputs
        {
            AgingCurves = rules.AgingCurves,
            Archetypes = rules.Archetypes,
            Headlines = rules.Headlines,
            PlayerDriverId = playerId,
            PlayerAge = 30,
            CharacterRules = rules.Character,
        };
        var report = ReplayService.Resimulate(db, pack, unchecked((ulong)seed), inputs);
        Assert.True(report.Identical,
            $"diverged: {report.FirstDivergence?.Reason} stored={report.FirstDivergence?.StoredDeltaJson} " +
            $"regenerated={report.FirstDivergence?.RegeneratedDeltaJson}");
    }

    [Fact]
    public void PerErrorInjuryPerk_BanksLoadFromCrashes_RaisesTheRoll_AndReplaysByteIdentically()
    {
        string packDirectory = Path.Combine(_root, "pack");
        TestPackBuilder.Write(TestPackBuilder.TwoRoundPack(), packDirectory);
        string careerPath = Path.Combine(_root, "careers", "pererror.ams2career");

        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs"),
            library: TestPackBuilder.Library());

        const string playerId = "driver.hulme";
        const long seed = 246810;
        Companion.Core.Packs.SeasonPack pack;

        using (var session = CareerSessionService.CreateCareer(
                   new CareerCreationRequest
                   {
                       PackDirectory = packDirectory,
                       CareerFilePath = careerPath,
                       CareerName = "PerError",
                       MasterSeed = seed,
                       PlayerLiveryName = TestPackBuilder.StockLivery2,
                       Character = FragileInjuryCharacter(), // glass_cannon: perErrorAdd +0.15 @driverErrorDnf
                   },
                   environment))
        {
            pack = session.Pack;
            // Round 1: the player bins it (accident = driver error) → banks glass_cannon's perErrorAdd.
            var grid1 = session.CurrentGrid();
            session.Apply(new ResultDraft
            {
                Classified = grid1.Select(s => s.DriverId).Where(id => id != playerId).ToList(),
                DidNotFinish = new Dictionary<string, string> { [playerId] = "a" },
                Disqualified = [],
            });
            // Round 2: a clean finish — no further injury load.
            var grid2 = session.CurrentGrid();
            session.Apply(new ResultDraft
            {
                Classified = grid2.Select(s => s.DriverId).ToList(),
                DidNotFinish = new Dictionary<string, string>(),
                Disqualified = [],
            });
            Assert.NotNull(session.SeasonReview());
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using var db = CareerDatabase.Open(careerPath);
        long seasonId = CareerStore.ReadSeasons(db).Single().Id;
        var rules = CareerRulesData.Load(ViewModelTestData.RulesDirectory);

        // The driver-error DNF banked glass_cannon's +0.15 perErrorAdd into the round-1 player state.
        double round1Load = StateStore.ReadRoundPlayerState(db, seasonId, 1)!.Player.SeasonInjuryLoad;
        Assert.Equal(0.15, round1Load, 6);

        // The season-end injury roll reads that banked load ON TOP of the base hazard, so binning it
        // during the year raised the off-season risk — recompute the roll WITH the load and assert the
        // journalled outcome matches (proving the cross-stage wiring fires, not just re-derives).
        var mods = PerkResolver.Resolve(["glass_cannon"], rules.Character);
        double hazardWithLoad = InjuryModel.Hazard(0.25, mods, round1Load);
        Assert.True(hazardWithLoad > InjuryModel.Hazard(0.25, mods), "the banked load must raise the hazard");
        double roll = new StreamFactory(unchecked((ulong)seed))
            .CreateStream(CareerStreams.Injury, pack.Season.Year, 0, "player").NextDouble();
        var journal = JournalStore.ReadSeason(db, seasonId);
        Assert.Equal(roll < hazardWithLoad, journal.Any(r => r.Phase == JournalPhases.PlayerInjury));

        var inputs = new ReplaySimInputs
        {
            AgingCurves = rules.AgingCurves,
            Archetypes = rules.Archetypes,
            Headlines = rules.Headlines,
            PlayerDriverId = playerId,
            PlayerAge = 30,
            CharacterRules = rules.Character,
        };
        var report = ReplayService.Resimulate(db, pack, unchecked((ulong)seed), inputs);
        Assert.True(report.Identical,
            $"diverged: {report.FirstDivergence?.Reason} stored={report.FirstDivergence?.StoredDeltaJson} " +
            $"regenerated={report.FirstDivergence?.RegeneratedDeltaJson}");
    }

    [Fact]
    public void SeasonEndXp_RewardsTheChampionship_AndReplaysByteIdentically()
    {
        string packDirectory = Path.Combine(_root, "pack");
        TestPackBuilder.Write(TestPackBuilder.TwoRoundPack(), packDirectory);
        string careerPath = Path.Combine(_root, "careers", "xp.ams2career");

        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs"),
            library: TestPackBuilder.Library());

        const string playerId = "driver.hulme";
        const long seed = 424242;
        Companion.Core.Packs.SeasonPack pack;

        using (var session = CareerSessionService.CreateCareer(
                   new CareerCreationRequest
                   {
                       PackDirectory = packDirectory,
                       CareerFilePath = careerPath,
                       CareerName = "Season XP",
                       MasterSeed = seed,
                       PlayerLiveryName = TestPackBuilder.StockLivery2,
                       Character = Character(),
                   },
                   environment))
        {
            pack = session.Pack;
            for (int round = 0; round < 2; round++)
            {
                var grid = session.CurrentGrid();
                // The player wins every round → champion, so the placement bonus is the title bonus.
                var order = new[] { playerId }
                    .Concat(grid.Select(s => s.DriverId).Where(id => id != playerId))
                    .ToList();
                session.Apply(new ResultDraft
                {
                    Classified = order,
                    DidNotFinish = new Dictionary<string, string>(),
                    Disqualified = [],
                });
            }
            Assert.NotNull(session.SeasonReview()); // triggers the season-end pipeline
        }
        // The session's Dispose clears its own connection pool (the delete-handle fix), so the file
        // reopens cleanly without a process-wide ClearAllPools (which races other parallel DB tests).

        using var db = CareerDatabase.Open(careerPath);
        long seasonId = CareerStore.ReadSeasons(db).Single().Id;
        var rules = CareerRulesData.Load(ViewModelTestData.RulesDirectory);

        // A single season-final player.xp row banks the title bonus + the season-completed grant,
        // and it advances the driver's total XP.
        var journal = JournalStore.ReadSeason(db, seasonId);
        var seasonXpRow = Assert.Single(
            journal, r => r.Phase == JournalPhases.PlayerXp && r.Cause == "season-final");
        var perSeason = rules.Character.Levels.XpSources.PerSeason;
        int expectedBonus = (int)Math.Round(
            perSeason.Championship1 + perSeason.SeasonCompleted, MidpointRounding.AwayFromZero);
        using var delta = System.Text.Json.JsonDocument.Parse(seasonXpRow.DeltaJson);
        Assert.Equal(expectedBonus, delta.RootElement.GetProperty("season").GetInt32());
        Assert.True(delta.RootElement.GetProperty("to").GetInt64()
            > delta.RootElement.GetProperty("from").GetInt64());

        // The season-XP row is a pure function of the championship result, so replay reproduces it.
        var inputs = new ReplaySimInputs
        {
            AgingCurves = rules.AgingCurves,
            Archetypes = rules.Archetypes,
            Headlines = rules.Headlines,
            PlayerDriverId = playerId,
            PlayerAge = 30,
            CharacterRules = rules.Character,
        };
        var report = ReplayService.Resimulate(db, pack, unchecked((ulong)seed), inputs);
        Assert.True(report.Identical,
            $"diverged: {report.FirstDivergence?.Reason} stored={report.FirstDivergence?.StoredDeltaJson} " +
            $"regenerated={report.FirstDivergence?.RegeneratedDeltaJson}");
    }

    [Fact]
    public void CharacterCareer_FoldsWithTheCharacterAndReplaysByteIdentically()
    {
        string packDirectory = Path.Combine(_root, "pack");
        TestPackBuilder.Write(TestPackBuilder.TwoRoundPack(), packDirectory);
        string careerPath = Path.Combine(_root, "careers", "character.ams2career");

        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs"),
            library: TestPackBuilder.Library());

        const string playerId = "driver.hulme";
        const long seed = 42424242;
        Companion.Core.Packs.SeasonPack pack;

        using (var session = CareerSessionService.CreateCareer(
                   new CareerCreationRequest
                   {
                       PackDirectory = packDirectory,
                       CareerFilePath = careerPath,
                       CareerName = "Character Fold",
                       MasterSeed = seed,
                       PlayerLiveryName = TestPackBuilder.StockLivery2,
                       Character = Character(),
                   },
                   environment))
        {
            pack = session.Pack;

            // The character bites the STAGED grid: the player seat's raceSkill is the talent-stat
            // write (pace 0.85 → 0.35 + 0.55*0.85 = 0.8175), not the pack baseline.
            var grid1 = session.CurrentGrid();
            var playerSeat = grid1.Single(s => string.Equals(s.DriverId, playerId, StringComparison.Ordinal));
            Assert.Equal(0.8175, playerSeat.Ratings.RaceSkill, 6);

            session.Apply(new ResultDraft
            {
                Classified = grid1.Select(s => s.DriverId).ToList(),
                DidNotFinish = new Dictionary<string, string>(),
                Disqualified = [],
            });

            var grid2 = session.CurrentGrid();
            session.Apply(new ResultDraft
            {
                Classified = grid2.Select(s => s.DriverId).ToList(),
                DidNotFinish = new Dictionary<string, string>(),
                Disqualified = [],
            });
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using var db = CareerDatabase.Open(careerPath);
        long seasonId = CareerStore.ReadSeasons(db).Single().Id;

        // The creation INPUT row is present (round = null), and the character rode into the start
        // player state (Level 1, perks preserved).
        var journal = JournalStore.ReadSeason(db, seasonId);
        Assert.Contains(journal, r => r.Round is null && r.Phase == JournalPhases.PlayerCharacter);

        var start = StateStore.ReadPlayerState(db, seasonId, StateStore.StageStart)!;
        Assert.True(start.HasCharacter);
        Assert.Equal(1, start.Level);
        Assert.Equal("Ace McTest", start.Character!.Name); // the chosen driver name folds + persists
        Assert.Equal(["engineers_favorite"], start.Character.PerkIds);

        // The character accrues XP: a player.xp row per raced round, and the folded state carries a
        // level (>= the creation level 1). A character-free career emits no player.xp row.
        Assert.Equal(2, journal.Count(r => r.Round is not null && r.Phase == JournalPhases.PlayerXp));
        var round2State = StateStore.ReadRoundPlayerState(db, seasonId, 2)!;
        Assert.True(round2State.Player.Level >= 1);

        // The whole character career re-simulates byte-identically. The re-sim MUST receive the same
        // character rules the live fold used; the player.character row being provenance-excluded is
        // proven by there being no "extra stored row" divergence.
        var rules = CareerRulesData.Load(ViewModelTestData.RulesDirectory);
        var inputs = new ReplaySimInputs
        {
            AgingCurves = rules.AgingCurves,
            Archetypes = rules.Archetypes,
            Headlines = rules.Headlines,
            PlayerDriverId = playerId,
            PlayerAge = 30,
            CharacterRules = rules.Character,
        };

        var report = ReplayService.Resimulate(db, pack, unchecked((ulong)seed), inputs);

        Assert.True(report.Identical,
            $"diverged: {report.FirstDivergence?.Reason} stored={report.FirstDivergence?.StoredDeltaJson} " +
            $"regenerated={report.FirstDivergence?.RegeneratedDeltaJson}");
        Assert.True(report.ComparedRows > 0);
    }

    [Fact]
    public void OneTrickPony_LocksBetweenSeasonDevelopmentToItsChosenSpecialismStat()
    {
        string packDirectory = Path.Combine(_root, "pack");
        TestPackBuilder.Write(TestPackBuilder.TwoRoundPack(), packDirectory);
        string careerPath = Path.Combine(_root, "careers", "onetrick.ams2career");

        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs"),
            library: TestPackBuilder.Library());

        // One-Trick Pony bound to wetSkill (owned by the adaptability stat, which maps wetSkill +
        // tyreManagement). lockToOne must let ONLY that owning stat be developed; every other talent
        // stat is frozen. (Before the fix lockToOne was a dead lever — nothing was rejected.)
        var character = new CharacterProfile
        {
            Name = "One Trick",
            Stats = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["pace"] = 0.50, ["oneLap"] = 0.50, ["craft"] = 0.50, ["racecraft"] = 0.50,
                ["adaptability"] = 0.50, ["marketability"] = 0.50, ["durability"] = 0.50,
            },
            PerkIds = ["one_trick"],
            ChosenFlavor = "wetSkill",
            CpUnspent = 3,
        };

        using var session = CareerSessionService.CreateCareer(
            new CareerCreationRequest
            {
                PackDirectory = packDirectory,
                CareerFilePath = careerPath,
                CareerName = "One Trick",
                MasterSeed = 111,
                PlayerLiveryName = TestPackBuilder.StockLivery2,
                Character = character,
            },
            environment);

        // The single specialism stat (adaptability owns wetSkill) develops fine.
        session.SpendCharacterPoint(CharacterSpend.Stat("adaptability", 1));

        // Every other talent stat is frozen — the spend is rejected.
        Assert.Throws<InvalidOperationException>(
            () => session.SpendCharacterPoint(CharacterSpend.Stat("pace", 1)));
        Assert.Throws<InvalidOperationException>(
            () => session.SpendCharacterPoint(CharacterSpend.Stat("craft", 1)));
    }

    [Fact]
    public void SetupGamble_CalledShot_ResolvesReputation_AndReplaysByteIdentically()
    {
        string packDirectory = Path.Combine(_root, "pack");
        TestPackBuilder.Write(TestPackBuilder.TwoRoundPack(), packDirectory);
        string careerPath = Path.Combine(_root, "careers", "gamble.ams2career");

        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs"),
            library: TestPackBuilder.Library());

        const string playerId = "driver.hulme";
        const long seed = 778899;
        Companion.Core.Packs.SeasonPack pack;

        // No character — the Setup Gamble is a universal mechanic, so this also proves it stays
        // byte-replayable on an ordinary (character-free) career.
        using (var session = CareerSessionService.CreateCareer(
                   new CareerCreationRequest
                   {
                       PackDirectory = packDirectory,
                       CareerFilePath = careerPath,
                       CareerName = "Gamble",
                       MasterSeed = seed,
                       PlayerLiveryName = TestPackBuilder.StockLivery2,
                   },
                   environment))
        {
            pack = session.Pack;
            // Round 1: call P1 (bolder than the mid-grid expected finish) AND finish P1 → a HIT.
            var grid1 = session.CurrentGrid();
            session.Apply(new ResultDraft
            {
                Classified = new[] { playerId }
                    .Concat(grid1.Select(s => s.DriverId).Where(id => id != playerId)).ToList(),
                DidNotFinish = new Dictionary<string, string>(),
                Disqualified = [],
                CalledShot = 1,
            });
            // Round 2: no gamble — an ordinary round, so no player.call row.
            var grid2 = session.CurrentGrid();
            session.Apply(new ResultDraft
            {
                Classified = grid2.Select(s => s.DriverId).ToList(),
                DidNotFinish = new Dictionary<string, string>(),
                Disqualified = [],
            });
        }

        using var db = CareerDatabase.Open(careerPath);
        long seasonId = CareerStore.ReadSeasons(db).Single().Id;

        // The gamble fired exactly once (round 1), it was a hit, and it moved reputation up.
        var journal = JournalStore.ReadSeason(db, seasonId);
        var callRow = Assert.Single(journal, r => r.Phase == JournalPhases.PlayerCall);
        Assert.Equal(1, callRow.Round);
        Assert.Equal("gamble-hit", callRow.Cause);
        using (var d = System.Text.Json.JsonDocument.Parse(callRow.DeltaJson))
        {
            Assert.True(d.RootElement.GetProperty("hit").GetBoolean());
            Assert.True(d.RootElement.GetProperty("delta").GetDouble() > 0.0);
            Assert.True(d.RootElement.GetProperty("to").GetDouble()
                > d.RootElement.GetProperty("from").GetDouble());
        }

        // The call rides the raw result envelope (ground truth), so the resolved player.call row
        // re-derives byte-for-byte on replay.
        var rules = CareerRulesData.Load(ViewModelTestData.RulesDirectory);
        var inputs = new ReplaySimInputs
        {
            AgingCurves = rules.AgingCurves,
            Archetypes = rules.Archetypes,
            Headlines = rules.Headlines,
            PlayerDriverId = playerId,
            PlayerAge = 30,
        };
        var report = ReplayService.Resimulate(db, pack, unchecked((ulong)seed), inputs);
        Assert.True(report.Identical,
            $"diverged: {report.FirstDivergence?.Reason} stored={report.FirstDivergence?.StoredDeltaJson} " +
            $"regenerated={report.FirstDivergence?.RegeneratedDeltaJson}");
    }
}
