using Companion.Core.Career;
using Companion.Core.Json;
using Companion.Core.Packs;
using Companion.Core.Smgp;
using Companion.Data;
using Companion.ViewModels.Services;
using Companion.ViewModels.Wizard;

namespace Companion.Tests.ViewModels;

/// <summary>Racing Passport, the pure-racing mode (2026-07-18 product-owner decision,
/// docs/dev/racing-passport-pure-racing.md): an independently saved, faithful, single-season
/// historical racing career with NO character progression and NO owner economy. These tests pin
/// the whole contract: the wizard route (any faithful year, SMGP excluded, no character creator,
/// no grid editor), creation (one pinned season, the faithful field, one driver replaced exactly,
/// zero progression/economy/SMGP state seeded, contradictory input rejected), and the season
/// itself (races, standings, completes without rollover, reopens, replays byte-identically).
/// Scaffolding mirrors <c>CampaignProgressionCreationTests</c>' real-machinery ladder.</summary>
public sealed class RacingPassportTests : IDisposable
{
    private const long Seed = 20260718;
    private readonly string _root = Directory.CreateTempSubdirectory("companion-passport-").FullName;

    private string PacksRoot => Path.Combine(_root, "packs");
    private string CareerPath(string name) => Path.Combine(_root, "careers", name + ".ams2career");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    // ---------- wizard ----------

    [Fact]
    public void Wizard_ExcludesSmgpPacks_AndOffersEveryFaithfulYear()
    {
        WriteHistoricalPack(1967);
        WriteHistoricalPack(1991);
        WriteHistoricalPack(2008);
        WriteSmgpPack();

        var wizard = PassportWizard();

        // Any faithful year is selectable with no chronological gate; SMGP is never offered.
        Assert.Equal([1967, 1991, 2008],
            wizard.Packs.Select(p => p.SeasonYear!.Value).OrderBy(y => y).ToArray());
        Assert.DoesNotContain(wizard.Packs, p =>
            string.Equals(p.Manifest!.CareerStyle, SmgpRules.CareerStyle, StringComparison.Ordinal));
    }

    [Fact]
    public void Wizard_TraversesSeasonVerificationSeatConfirm_SkippingCharacterAndGrid()
    {
        WriteHistoricalPack(1991);
        var wizard = PassportWizard();
        var visited = new List<WizardStep> { wizard.Step };

        wizard.SelectedPack = Assert.Single(wizard.Packs);
        wizard.NextCommand.Execute(null); // -> Verification
        visited.Add(wizard.Step);
        if (wizard.HasWarnings) wizard.ProceedAnyway = true;
        wizard.NextCommand.Execute(null); // -> SeatPick (Character skipped)
        visited.Add(wizard.Step);
        wizard.SelectedSeat = wizard.Seats.First(seat => seat.LiveryName == TestPackBuilder.StockLivery2);
        wizard.NextCommand.Execute(null); // -> Confirm (Grid skipped)
        visited.Add(wizard.Step);

        Assert.Equal(
            [WizardStep.SeasonPick, WizardStep.Verification, WizardStep.SeatPick, WizardStep.Confirm],
            visited);
        Assert.DoesNotContain(WizardStep.Character, visited);
        Assert.DoesNotContain(WizardStep.Grid, visited);
        Assert.True(wizard.CanCreate);

        // Back walks the same pure-racing route in reverse.
        wizard.BackCommand.Execute(null);
        Assert.Equal(WizardStep.SeatPick, wizard.Step);
    }

    [Fact]
    public void Wizard_SeatAndOptionalName_FlowIntoAnHonestConfirm()
    {
        WriteHistoricalPack(1991);
        var wizard = PassportWizard();
        wizard.SelectedPack = Assert.Single(wizard.Packs);
        wizard.NextCommand.Execute(null);
        if (wizard.HasWarnings) wizard.ProceedAnyway = true;
        wizard.NextCommand.Execute(null);

        var seat = wizard.Seats.First(s => s.LiveryName == TestPackBuilder.StockLivery2);
        wizard.SelectedSeat = seat;
        Assert.Equal($"{seat.TeamName} · replacing {seat.DriverName} · #{seat.Number}",
            wizard.PassportSeatSummary);
        Assert.Contains("1991 · ", wizard.PassportSeasonSummary);

        // The name is OPTIONAL and validated, not a character creator.
        Assert.Equal("", wizard.PlayerDisplayNameError);
        wizard.PlayerDisplayName = new string('x', NewCareerWizardViewModel.MaxPlayerDisplayNameLength + 1);
        Assert.NotEmpty(wizard.PlayerDisplayNameError);
        Assert.False(wizard.CanCreate);
        wizard.PlayerDisplayName = "Jo Ramírez";
        Assert.Equal("", wizard.PlayerDisplayNameError);

        wizard.NextCommand.Execute(null); // -> Confirm
        var lines = wizard.PassportConfirmLines;
        Assert.Contains("RACING PASSPORT", lines);
        Assert.Contains($"Season: {1991}", lines);
        Assert.Contains($"Team: {seat.TeamName}", lines);
        Assert.Contains("Driver: Jo Ramírez", lines);
        Assert.Contains("Progression: None, pure racing", lines);
        Assert.Contains("Team management: None", lines);
        Assert.DoesNotContain(lines, line => line.Contains("XP", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("economy", StringComparison.OrdinalIgnoreCase));
    }

    // ---------- creation ----------

    [Fact]
    public void Creation_PersistsPureRacingMode_AndPinsExactlyOneSeason()
    {
        WriteHistoricalPack(1967);
        WriteHistoricalPack(1991); // a tempting later pack exists and is NOT pinned or entered
        string careerPath = CareerPath("pin-one");

        using (CareerSessionService.CreateCareer(PassportRequest(careerPath), Environment()))
        {
        }

        using var db = CareerDatabase.Open(careerPath);
        long seasonId = CareerStore.ReadSeasons(db).Single().Id;
        var start = StateStore.ReadPlayerState(db, seasonId, StateStore.StageStart)!;

        Assert.Equal(CareerExperienceModes.RacingPassport, start.ExperienceMode);
        Assert.Null(start.CampaignProgressionPlan); // no bounded horizon, no XP scale

        using var command = db.Connection.CreateCommand();
        command.CommandText = "SELECT pack_id FROM pinned_pack;";
        var pinnedIds = new List<string>();
        using (var reader = command.ExecuteReader())
            while (reader.Read())
                pinnedIds.Add(reader.GetString(0));
        Assert.Equal(["hist-1967"], pinnedIds);
    }

    [Fact]
    public void Creation_ReplacesExactlyOneDriver_AndKeepsTheFaithfulField()
    {
        WriteHistoricalPack(1967);

        using var session = CareerSessionService.CreateCareer(
            PassportRequest(CareerPath("seat-swap")), Environment());

        var grid = session.CurrentGrid();
        // The player sits in the chosen seat's livery, RACING UNDER the replaced driver's identity
        // (the real-driver model): driver.hulme appears exactly once, as the player, never as a
        // separate AI car. Every other authored entry keeps its seat; no cascade, no shuffle.
        var playerSeat = Assert.Single(grid, seat => seat.IsPlayer);
        Assert.Equal(TestPackBuilder.StockLivery2, playerSeat.Ams2LiveryName);
        Assert.Equal("driver.hulme", playerSeat.DriverId);
        Assert.Equal(1, grid.Count(seat => seat.DriverId == "driver.hulme"));
        Assert.DoesNotContain(grid, seat => seat.DriverId == "driver.hulme" && !seat.IsPlayer);
        Assert.Contains(grid, seat => seat.DriverId == "driver.brabham" && !seat.IsPlayer);

        using var db = CareerDatabase.Open(CareerPath("seat-swap"));
        long seasonId = CareerStore.ReadSeasons(db).Single().Id;
        var start = StateStore.ReadPlayerState(db, seasonId, StateStore.StageStart)!;
        Assert.Null(start.GridSelection); // the whole faithful field, never a narrowed grid
    }

    [Fact]
    public void Creation_SeedsNoProgressionEconomyOrSmgpState()
    {
        WriteHistoricalPack(1967);
        string careerPath = CareerPath("clean-state");

        using (CareerSessionService.CreateCareer(PassportRequest(careerPath), Environment()))
        {
        }

        using var db = CareerDatabase.Open(careerPath);
        long seasonId = CareerStore.ReadSeasons(db).Single().Id;
        var start = StateStore.ReadPlayerState(db, seasonId, StateStore.StageStart)!;

        // No character progression of any kind.
        Assert.Null(start.Character);
        Assert.Equal(0, start.Level);
        Assert.Equal(0, start.Xp);
        // No owner economy, no SMGP, mortality Off.
        Assert.Null(start.Economy);
        Assert.Null(start.Smgp);
        Assert.Equal(MortalityMode.Off, start.Mortality);

        // No progression journal rows were seeded at creation.
        var phases = JournalStore.ReadSeason(db, seasonId).Select(row => row.Phase).ToArray();
        foreach (string forbidden in new[]
                 {
                     JournalPhases.PlayerCharacter, JournalPhases.PlayerStatSpend,
                     JournalPhases.PlayerSkillPlan, JournalPhases.PlayerSkillReset,
                     JournalPhases.PlayerRespec, JournalPhases.PlayerXp,
                 })
        {
            Assert.DoesNotContain(forbidden, phases);
        }
    }

    [Fact]
    public void Creation_RejectsContradictoryInput()
    {
        WriteHistoricalPack(1967);
        WriteSmgpPack();

        // A character on a Passport request: rejected, never silently downgraded.
        var withCharacter = PassportRequest(CareerPath("with-character")) with
        {
            Character = new Companion.Core.Character.CharacterProfile
            {
                Name = "Contradiction",
                Stats = new Dictionary<string, double>(StringComparer.Ordinal),
                PerkIds = [],
            },
        };
        Assert.Throws<InvalidOperationException>(() =>
            CareerSessionService.CreateCareer(withCharacter, Environment()));
        Assert.False(File.Exists(CareerPath("with-character")));

        // DynastyEconomy on a Passport request: rejected (the hard mode gate holds).
        var withEconomy = PassportRequest(CareerPath("with-economy")) with { DynastyEconomy = true };
        Assert.Throws<InvalidOperationException>(() =>
            CareerSessionService.CreateCareer(withEconomy, Environment()));
        Assert.False(File.Exists(CareerPath("with-economy")));

        // SmgpMode on a Passport request: rejected.
        var withSmgpFlag = PassportRequest(CareerPath("with-smgp-flag")) with { SmgpMode = true };
        Assert.Throws<InvalidOperationException>(() =>
            CareerSessionService.CreateCareer(withSmgpFlag, Environment()));
        Assert.False(File.Exists(CareerPath("with-smgp-flag")));

        // An SMGP pack for a Passport: rejected (SMGP is its own campaign).
        var withSmgpPack = PassportRequest(CareerPath("with-smgp-pack")) with
        {
            PackDirectory = Path.Combine(PacksRoot, "smgp"),
        };
        Assert.Throws<InvalidOperationException>(() =>
            CareerSessionService.CreateCareer(withSmgpPack, Environment()));
        Assert.False(File.Exists(CareerPath("with-smgp-pack")));
    }

    // ---------- the season: race, complete, reopen, replay ----------

    [Fact]
    public void PassportSeason_RacesToChampion_CompletesWithoutRollover_OrOffers()
    {
        WriteHistoricalPack(1967);
        using var session = CareerSessionService.CreateCareer(
            PassportRequest(CareerPath("full-season")), Environment());

        // Race the full (two-round) season through the real deterministic fold, the player wins.
        string playerId = "";
        while (!session.Summary.SeasonComplete)
        {
            var grid = session.CurrentGrid();
            var classified = grid.Select(seat => seat.DriverId).ToList();
            playerId = grid.Single(seat => seat.IsPlayer).DriverId;
            Assert.True(classified.Remove(playerId));
            classified.Insert(0, playerId);
            session.Apply(new ResultDraft
            {
                Classified = classified,
                DidNotFinish = new Dictionary<string, string>(),
                Disqualified = [],
            });
        }

        // Standings updated through the real engine: the player is the champion.
        var standings = session.CurrentStandings();
        Assert.NotNull(standings);
        var champion = standings!.Drivers.Single(d => d.Position == 1);
        Assert.Equal(playerId, champion.DriverId);

        // The season review is the whole arc: produced normally, with NO contract offers.
        var review = session.SeasonReview();
        Assert.NotNull(review);
        Assert.Empty(review!.Offers);

        // No rollover: no next season to preview or sign for, ever.
        Assert.Null(session.NextSeason());
        var error = Assert.Throws<InvalidOperationException>(() => session.StartNextSeason("team.brabham"));
        Assert.Contains("one complete faithful season", error.Message);

        // And still no XP/SP/economy after committed races.
        Assert.Equal(0, session.AvailableCharacterCp());
        Assert.Null(session.CharacterDossier());
        Assert.Null(session.EconomyDashboard());
        using var db = CareerDatabase.Open(CareerPath("full-season"));
        long seasonId = CareerStore.ReadSeasons(db).Single().Id;
        Assert.DoesNotContain(
            JournalStore.ReadSeason(db, seasonId),
            row => row.Phase is JournalPhases.PlayerXp or JournalPhases.PlayerStatSpend
                or JournalPhases.OfferExtended or JournalPhases.SeatMarket);
    }

    [Fact]
    public void PassportSave_ReopensWithSeatAndName_AndResimulatesByteIdentical()
    {
        WriteHistoricalPack(1967);
        string careerPath = CareerPath("reopen");
        using (var session = CareerSessionService.CreateCareer(
                   PassportRequest(careerPath, playerName: "Jo Ramírez"), Environment()))
        {
            Assert.Equal("Jo Ramírez", session.PlayerIdentity()!.Value.DisplayName);
            ApplyOneRound(session, playerWins: true);
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using (var reopened = CareerSessionService.OpenCareer(careerPath, Environment()))
        {
            // The custom name and the chosen seat survive the reopen.
            var identity = reopened.PlayerIdentity();
            Assert.NotNull(identity);
            Assert.Equal("Jo Ramírez", identity!.Value.DisplayName);
            var playerSeat = Assert.Single(reopened.CurrentGrid(), seat => seat.IsPlayer);
            Assert.Equal(TestPackBuilder.StockLivery2, playerSeat.Ams2LiveryName);

            ApplyOneRound(reopened, playerWins: false);
        }

        // The whole season replays byte-identically (the pure-racing season-end gate included).
        using var db = CareerDatabase.Open(careerPath);
        var rules = Environment().Rules;
        var report = ReplayService.Resimulate(db, unchecked((ulong)Seed), new ReplaySimInputs
        {
            AgingCurves = rules.AgingCurves,
            Archetypes = rules.Archetypes,
            Headlines = rules.Headlines,
            PlayerDriverId = "driver.hulme",
            PlayerAge = 30,
        });
        Assert.True(
            report.Identical,
            $"diverged: {report.FirstDivergence?.Reason} " +
            $"stored={report.FirstDivergence?.StoredDeltaJson} " +
            $"regenerated={report.FirstDivergence?.RegeneratedDeltaJson}");
    }

    [Fact]
    public void PassportSave_WithoutACustomName_KeepsTheAuthoredDriverName()
    {
        WriteHistoricalPack(1967);

        using var session = CareerSessionService.CreateCareer(
            PassportRequest(CareerPath("authored-name")), Environment());

        // No custom name: the real-driver resolution keeps the seat's authored driver (the
        // historical driver the player wears), PlayerIdentity stays null like any such career.
        Assert.Null(session.PlayerIdentity());
    }

    // ---------- nationality (the optional identity field) ----------

    [Fact]
    public void Creation_PersistsAndResolvesTheChosenNationality()
    {
        WriteHistoricalPack(1967);
        string careerPath = CareerPath("nationality");

        using (var session = CareerSessionService.CreateCareer(
                   PassportRequest(careerPath, countryCode: "BRA"), Environment()))
        {
            Assert.Equal("BRA", session.CurrentPlayerCountryCode());
        }

        using var db = CareerDatabase.Open(careerPath);
        long seasonId = CareerStore.ReadSeasons(db).Single().Id;
        var start = StateStore.ReadPlayerState(db, seasonId, StateStore.StageStart)!;
        Assert.Equal("BRA", start.CustomCountryCode);

        // No pick: the field stays empty and the session resolves null, so the seat's authored
        // country shows, exactly like a career with no nationality at all.
        using var plainSession = CareerSessionService.CreateCareer(
            PassportRequest(CareerPath("no-nationality")), Environment());
        Assert.Null(plainSession.CurrentPlayerCountryCode());
    }

    [Fact]
    public void Wizard_ExposesTheNationalityPicker_AndShowsItInTheConfirm()
    {
        WriteHistoricalPack(1991);
        var wizard = PassportWizard();

        Assert.NotEmpty(wizard.PassportCountryOptions);
        Assert.Equal("", wizard.PassportNationalitySummary); // no pick = the authored country

        var brazil = Assert.Single(wizard.PassportCountryOptions, o => o.Code == "BRA");
        wizard.SelectedPassportCountry = brazil;
        Assert.Contains("BRA", wizard.PassportNationalitySummary);

        wizard.SelectedPack = Assert.Single(wizard.Packs);
        wizard.NextCommand.Execute(null);
        if (wizard.HasWarnings) wizard.ProceedAnyway = true;
        wizard.NextCommand.Execute(null);
        wizard.SelectedSeat = wizard.Seats.First(seat => seat.LiveryName == TestPackBuilder.StockLivery2);
        wizard.NextCommand.Execute(null);

        Assert.Contains(wizard.PassportConfirmLines,
            line => line.StartsWith("Nationality:", StringComparison.Ordinal) &&
                    line.Contains("BRA", StringComparison.Ordinal));
    }

    // ---------- scaffolding ----------

    private NewCareerWizardViewModel PassportWizard() => new(
        Environment(),
        new CapturingFactory(),
        packSearchRoots: [PacksRoot],
        careersDirectory: Path.Combine(_root, "wizard-careers"),
        seedSource: new Random(7),
        experienceMode: CareerExperienceModes.RacingPassport);

    private CareerEnvironment Environment()
    {
        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "documents"),
            library: TestPackBuilder.Library());
        environment.PackSearchRoots = () => [PacksRoot];
        return environment;
    }

    private CareerCreationRequest PassportRequest(
        string careerPath, int year = 1967, string? playerName = null, string? countryCode = null) => new()
    {
        PackDirectory = Path.Combine(PacksRoot, year.ToString()),
        CareerFilePath = careerPath,
        CareerName = "Passport test",
        MasterSeed = Seed,
        ExperienceMode = CareerExperienceModes.RacingPassport,
        PlayerLiveryName = TestPackBuilder.StockLivery2,
        Character = null,
        PlayerDisplayName = playerName,
        PlayerCountryCode = countryCode,
    };

    private void WriteHistoricalPack(int year)
    {
        var pack = TestPackBuilder.TwoRoundPack();
        TestPackBuilder.Write(pack with
        {
            Manifest = pack.Manifest with
            {
                PackId = $"hist-{year}",
                Name = $"Historical {year}",
                CareerStyle = null,
            },
            Season = pack.Season with
            {
                Year = year,
                SeriesName = $"Historical Championship {year}",
                Rounds =
                [
                    TestPackBuilder.Round(1, $"{year}-01-02"),
                    TestPackBuilder.Round(2, $"{year}-05-07"),
                ],
            },
        }, Path.Combine(PacksRoot, year.ToString()));
    }

    private void WriteSmgpPack()
    {
        var pack = TestPackBuilder.TwoRoundPack();
        TestPackBuilder.Write(pack with
        {
            Manifest = pack.Manifest with
            {
                PackId = "smgp-decoy",
                Name = "SMGP Decoy",
                CareerStyle = SmgpRules.CareerStyle,
            },
        }, Path.Combine(PacksRoot, "smgp"));
    }

    private static void ApplyOneRound(ICareerSession session, bool playerWins)
    {
        var grid = session.CurrentGrid();
        var classified = grid.Select(seat => seat.DriverId).ToList();
        string playerId = grid.Single(seat => seat.IsPlayer).DriverId;
        Assert.True(classified.Remove(playerId));
        classified.Insert(playerWins ? 0 : classified.Count, playerId);
        session.Apply(new ResultDraft
        {
            Classified = classified,
            DidNotFinish = new Dictionary<string, string>(),
            Disqualified = [],
        });
    }

    private sealed class CapturingFactory : ICareerFactory
    {
        public CareerCreationRequest? LastRequest { get; private set; }

        public ICareerSession Create(CareerCreationRequest request)
        {
            LastRequest = request;
            return new FakeCareerSession();
        }

        public ICareerSession Open(string careerFilePath) => new FakeCareerSession();
    }
}
