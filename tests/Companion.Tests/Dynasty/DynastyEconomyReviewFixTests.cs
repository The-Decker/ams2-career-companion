using Companion.Core.Career;
using Companion.Core.Character;
using Companion.Core.Dynasty;
using Companion.Core.Numerics;
using Companion.Core.Packs;
using Companion.Data;
using Companion.Tests.ViewModels;
using Companion.ViewModels.Services;
using Companion.ViewModels.Shell;

namespace Companion.Tests.Dynasty;

/// <summary>Regression tests for the adversarial-review findings on the Dynasty economy: the
/// auto-sim bankruptcy suppression, the optional-mode-rules dormancy, the report-only resim of a
/// tampered decision row, and the authoritative badge clear on save-restore.</summary>
public sealed class DynastyEconomyReviewFixTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("companion-dynasty-reviewfix-").FullName;

    private string PacksRoot => Path.Combine(_root, "packs");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    [Fact]
    public void BankruptcyOnAnAutoSimulatedFinalRound_SuppressesSeasonEnd()
    {
        WritePack(1967);
        string careerPath = Path.Combine(_root, "careers", "autosim-bankrupt.ams2career");
        using (var session = CareerSessionService.CreateCareer(Request(careerPath), Environment()))
        {
            // Fold round 1 normally.
            ApplyRound(session);
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        // Inject the terminal setup into round 1's end state: the driver is out for the season (so
        // the FINAL round auto-simulates) and the ledger sits just above the era-scaled hard floor,
        // so the sit-out settlement's fees push it over.
        using (var db = CareerDatabase.Open(careerPath))
        {
            long seasonId = CareerStore.ReadSeasons(db).Single().Id;
            var round1 = StateStore.ReadRoundPlayerState(db, seasonId, 1)!;
            StateStore.UpdateRoundPlayerState(db, seasonId, 1, round1 with
            {
                Player = round1.Player with
                {
                    SeasonEndingInjury = true,
                    Economy = round1.Player.Economy! with { Balance = Rational.Parse("-24000") },
                },
            });
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        // The final round is now a sit-out; the auto-sim settlement folds the team.
        using (var session = CareerSessionService.OpenCareer(careerPath, Environment()))
        {
            Assert.NotNull(session.CurrentSitOut());
            session.AutoSimulateRound();
            Assert.NotNull(session.BankruptcyScreen());
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        // The terminal contract: the season is NOT completed, no season settlement banked, no
        // offers rolled — exactly the outcome the Apply path already guarantees.
        using var verify = CareerDatabase.Open(careerPath);
        var season = CareerStore.ReadSeasons(verify).Single();
        Assert.NotEqual("complete", season.Status);
        var journal = JournalStore.ReadSeason(verify, season.Id);
        Assert.Single(journal, r => r.Phase == JournalPhases.EconomyBankruptcy);
        Assert.DoesNotContain(journal, r => r.Phase == JournalPhases.EconomySeason);
        Assert.DoesNotContain(journal, r => r.Phase == JournalPhases.OfferExtended);
    }

    [Fact]
    public void AutoSimBankruptcy_HandsOffToTheTakeoverInTheLiveShell()
    {
        WritePack(1967);
        string careerPath = Path.Combine(_root, "careers", "autosim-shell.ams2career");
        using (var session = CareerSessionService.CreateCareer(Request(careerPath), Environment()))
        {
            ApplyRound(session);
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        using (var db = CareerDatabase.Open(careerPath))
        {
            long seasonId = CareerStore.ReadSeasons(db).Single().Id;
            var round1 = StateStore.ReadRoundPlayerState(db, seasonId, 1)!;
            StateStore.UpdateRoundPlayerState(db, seasonId, 1, round1 with
            {
                Player = round1.Player with
                {
                    SeasonEndingInjury = true,
                    Economy = round1.Player.Economy! with { Balance = Rational.Parse("-24000") },
                },
            });
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        using var session2 = CareerSessionService.OpenCareer(careerPath, Environment());
        using var home = new HomeViewModel(session2);
        Assert.True(home.IsSitOutStep);
        var sitOut = Assert.IsType<SitOutViewModel>(home.CurrentContent);
        sitOut.ContinueCommand.Execute(null); // folds the auto-sim round
        Assert.NotNull(home.BankruptcyScreen);
        Assert.True(home.IsCareerTerminal);
    }

    [Fact]
    public void LegacyCareer_LoadsAndFoldsWhenTheDynastyRulesFileIsAbsent()
    {
        // A stale-data install: a rules directory with NO dynasty subfolder.
        string rulesDir = Path.Combine(_root, "rules-no-dynasty");
        CopyRulesWithout(rulesDir, "dynasty");

        WritePack(1967);
        string careerPath = Path.Combine(_root, "careers", "legacy-no-dynasty.ams2career");
        var environment = Environment(rulesDir);

        // A plain (non-economy) career creates, folds a round, and reopens with no economy rules —
        // the whole economy stays inert, exactly the dormancy contract.
        using (var session = CareerSessionService.CreateCareer(new CareerCreationRequest
        {
            PackDirectory = Path.Combine(PacksRoot, "1967"),
            CareerFilePath = careerPath,
            CareerName = "Legacy",
            MasterSeed = 7,
            PlayerLiveryName = TestPackBuilder.StockLivery2,
        }, environment))
        {
            ApplyRound(session);
            Assert.Null(session.EconomyDashboard());
            Assert.Null(session.BankruptcyScreen());
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        using var reopened = CareerSessionService.OpenCareer(careerPath, environment);
        Assert.Equal(2, reopened.Summary.CurrentRound);
    }

    [Fact]
    public void EconomyCareer_FailsWithAClearMessageWhenTheRulesFileIsAbsent()
    {
        string rulesDir = Path.Combine(_root, "rules-no-dynasty2");
        CopyRulesWithout(rulesDir, "dynasty");
        WritePack(1967);
        string careerPath = Path.Combine(_root, "careers", "economy-no-rules.ams2career");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            CareerSessionService.CreateCareer(Request(careerPath), Environment(rulesDir)));
        Assert.Contains("economy.json", ex.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(careerPath));
    }

    [Fact]
    public void TamperedEconomyDecisionRow_ResimulatesAsAReportedDivergenceNotACrash()
    {
        WritePack(1967);
        string careerPath = Path.Combine(_root, "careers", "tampered-decision.ams2career");
        using (var session = CareerSessionService.CreateCareer(Request(careerPath), Environment()))
        {
            // A VALID buy-development decision for round 1, folded normally, so the round persists
            // its economy.applied/round rows.
            using (var db = CareerDatabase.Open(careerPath))
            {
                long seasonId = CareerStore.ReadSeasons(db).Single().Id;
                ReplayService.DeclareEconomyDecision(db, seasonId, round: 1, new DynastyEconomyDecision
                {
                    Kind = DynastyEconomyDecisionKind.BuyDevelopment,
                }, "2026-07-20T00:00:00.0000000Z");
            }
            ApplyRound(session);
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        // Tamper the STORED (already-folded) economy.decision row into a semantically-impossible one
        // that still parses — sign a sponsor that is not on the board. On re-fold the economy fold
        // throws; the report-only contract requires a reported divergence, never a crash.
        using (var db = CareerDatabase.Open(careerPath))
        {
            long seasonId = CareerStore.ReadSeasons(db).Single().Id;
            string tampered = System.Text.Json.JsonSerializer.Serialize(
                new DynastyEconomyDecision
                {
                    Kind = DynastyEconomyDecisionKind.SignSponsor,
                    SponsorId = "sponsor.tampered-nonexistent",
                },
                Companion.Core.Json.CoreJson.Options);
            using var command = db.Connection.CreateCommand();
            command.CommandText =
                "UPDATE journal SET delta_json = @d, cause = 'sign-sponsor' " +
                "WHERE season_id = @s AND phase = @p;";
            command.Parameters.AddWithValue("@d", tampered);
            command.Parameters.AddWithValue("@s", seasonId);
            command.Parameters.AddWithValue("@p", JournalPhases.EconomyDecision);
            Assert.Equal(1, command.ExecuteNonQuery());
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        using var verify = CareerDatabase.Open(careerPath);
        var rules = Environment().Rules;
        ReplayReport report = default!;
        var ex = Record.Exception(() =>
        {
            report = ReplayService.Resimulate(verify, 20260720, new ReplaySimInputs
            {
                AgingCurves = rules.AgingCurves,
                Archetypes = rules.Archetypes,
                Headlines = rules.Headlines,
                PlayerDriverId = "driver.hulme",
                PlayerAge = 22,
                CharacterRules = rules.Character,
                MasterySkills = rules.MasterySkills,
                DynastyEconomy = rules.DynastyEconomy,
            });
        });
        Assert.Null(ex); // report-only: no unhandled exception escapes Resimulate
        Assert.False(report.Identical);
        Assert.Equal("economy-decision-validation", report.FirstDivergence!.Reason);
    }

    [Fact]
    public void FreeDecisionLedgerLine_ShowsBlankNotPlusZero()
    {
        WritePack(1967);
        string careerPath = Path.Combine(_root, "careers", "free-line.ams2career");
        using var session = CareerSessionService.CreateCareer(Request(careerPath), Environment());

        // Sign then drop a sponsor in the round-1 window: the sign credits +500, the drop is free.
        session.DeclareEconomyDecision(new DynastyEconomyDecision
        {
            Kind = DynastyEconomyDecisionKind.SignSponsor,
            SponsorId = "sponsor.apex-lubricants",
        });
        session.DeclareEconomyDecision(new DynastyEconomyDecision
        {
            Kind = DynastyEconomyDecisionKind.DropSponsor,
            SponsorId = "sponsor.apex-lubricants",
        });
        ApplyRound(session);

        var statement = session.EconomyDashboard()!.Statement;
        Assert.Equal("+500", Assert.Single(statement, l => l.Label.Contains("sign sponsor", StringComparison.Ordinal)).Net);
        // A zero-money line reads blank, matching the pending-decision display — never "+0".
        Assert.Equal("", Assert.Single(statement, l => l.Label.Contains("drop sponsor", StringComparison.Ordinal)).Net);
    }

    [Fact]
    public void ObservedTouch_ClearsAStaleBadge_WhileUnobservedTouchCarriesItForward()
    {
        string file = Path.Combine(_root, "recent.json");
        var store = new RecentCareersStore(file, careerFileExists: _ => true);
        const string path = "C:/careers/revived.ams2career";

        store.Touch(path, "Revived", 1967, "historical", terminalState: "bankrupt");
        Assert.Equal("bankrupt", store.Load().Single(c => c.Path == path).TerminalState);

        // An unobserved re-touch (rename / plain Continue) carries the badge forward.
        store.Touch(path, "Revived", 1967);
        Assert.Equal("bankrupt", store.Load().Single(c => c.Path == path).TerminalState);

        // An observed touch with a live (null) state clears it — the restored career un-badges.
        store.Touch(path, "Revived", 1967, "historical", terminalState: null);
        Assert.Null(store.Load().Single(c => c.Path == path).TerminalState);
    }

    // ---------- harness ----------

    private static void ApplyRound(ICareerSession session)
    {
        var grid = session.CurrentGrid();
        session.Apply(new ResultDraft
        {
            Classified = grid.Select(seat => seat.DriverId).ToList(),
            DidNotFinish = new Dictionary<string, string>(),
            Disqualified = [],
        });
    }

    private CareerEnvironment Environment(string? rulesDirectory = null)
    {
        var environment = new CareerEnvironment
        {
            ContentLibrary = TestPackBuilder.Library(),
            LocateInstall = static () => null,
            DocumentsDirectory = Path.Combine(_root, "documents"),
            RulesDirectory = rulesDirectory ?? ViewModelTestData.RulesDirectory,
            PackSearchRoots = () => [PacksRoot],
        };
        return environment;
    }

    private static void CopyRulesWithout(string destination, string excludedSubfolder)
    {
        string source = ViewModelTestData.RulesDirectory;
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        foreach (string dir in Directory.GetDirectories(source))
        {
            string name = Path.GetFileName(dir);
            if (string.Equals(name, excludedSubfolder, StringComparison.OrdinalIgnoreCase))
                continue;
            string destSub = Path.Combine(destination, name);
            Directory.CreateDirectory(destSub);
            foreach (string file in Directory.GetFiles(dir))
                File.Copy(file, Path.Combine(destSub, Path.GetFileName(file)), overwrite: true);
        }
    }

    private CareerCreationRequest Request(string careerPath) => new()
    {
        PackDirectory = Path.Combine(PacksRoot, "1967"),
        CareerFilePath = careerPath,
        CareerName = "Dynasty review-fix test",
        MasterSeed = 20260720,
        ExperienceMode = CareerExperienceModes.GrandPrixDynasty,
        PlayerLiveryName = TestPackBuilder.StockLivery2,
        Character = VersionTwoCharacter(),
        DynastyEconomy = true,
        Mortality = MortalityMode.Normal,
    };

    private void WritePack(int year)
    {
        var pack = TestPackBuilder.TwoRoundPack();
        TestPackBuilder.Write(pack with
        {
            Manifest = pack.Manifest with { PackId = $"dynasty-{year}", Name = $"Synthetic {year}" },
            Season = pack.Season with
            {
                Year = year,
                Rounds =
                [
                    TestPackBuilder.Round(1, $"{year}-01-02"),
                    TestPackBuilder.Round(2, $"{year}-05-07"),
                ],
            },
        }, Path.Combine(PacksRoot, year.ToString()));
    }

    private static CharacterProfile VersionTwoCharacter()
    {
        var talent = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["pace"] = 0.70,
            ["oneLap"] = 0.65,
            ["craft"] = 0.60,
            ["racecraft"] = 0.62,
            ["adaptability"] = 0.58,
        };
        var meta = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["marketability"] = 0.50,
            ["durability"] = 0.55,
        };
        var all = talent.Concat(meta).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        return new CharacterProfile
        {
            Name = "Owner Driver",
            CountryCode = "BRA",
            Age = 22,
            Stats = all,
            PerkIds = ["engineers_favorite"],
            CreationPerkIds = ["engineers_favorite"],
            ProgressionVersion = CharacterLevelProgression.Level300Version,
            MasteryEffectsVersion = CharacterProfile.CurrentMasteryEffectsVersion,
            ExpectationModelVersion = CharacterProfile.CurrentExpectationModelVersion,
            RacingDnaId = "dna_circuit_specialist",
            RacingDnaVersion = 1,
            RacingDnaChoice = "technical",
            CreationBaseline = new CharacterCreationBaseline
            {
                Stats = talent,
                Meta = meta,
                TraitIds = ["engineers_favorite"],
            },
        };
    }
}
