using Companion.Core.Career;
using Companion.Core.Character;
using Companion.Core.Dynasty;
using Companion.Core.Packs;
using Companion.Data;
using Companion.Tests.ViewModels;
using Companion.ViewModels.Services;
using Companion.ViewModels.Shell;

namespace Companion.Tests.Dynasty;

/// <summary>Bankruptcy is a REAL terminal state (economy §7), held to the PostDeathArchiveTests
/// bar: the fold sets it deterministically, all three scoring entries refuse it, the season end is
/// suppressed on the fatal settlement, the screen model + shell routing project it (live and on
/// reopen), the archive stays readable, the gallery badges it — and the whole ending re-simulates
/// byte-identically.</summary>
public sealed class DynastyBankruptcyGateTests : IDisposable
{
    private const long Seed = 20260718;

    private readonly string _root = Directory.CreateTempSubdirectory("companion-dynasty-bankrupt-").FullName;

    private string PacksRoot => Path.Combine(_root, "packs");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    [Fact]
    public void OverspentTeam_GoesBankruptAndTheCareerReallyEnds()
    {
        string careerPath = CreateBankruptCareer();

        // ---- the terminal state folded on the FINAL round; the season end was suppressed ----
        using (var db = CareerDatabase.Open(careerPath))
        {
            var season = CareerStore.ReadSeasons(db).Single();
            Assert.NotEqual("complete", season.Status);
            var journal = JournalStore.ReadSeason(db, season.Id);
            var terminal = Assert.Single(journal, r => r.Phase == JournalPhases.EconomyBankruptcy);
            Assert.Equal(2, terminal.Round);
            Assert.DoesNotContain(journal, r => r.Phase == JournalPhases.EconomySeason);
            Assert.DoesNotContain(journal, r => r.Phase == JournalPhases.OfferExtended);

            var end = StateStore.ReadRoundPlayerState(db, season.Id, 2)!;
            Assert.True(end.Player.Economy!.Bankrupt);
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        // ---- reopening lands on the takeover; every scoring entry refuses; the archive reads ----
        using (var session = CareerSessionService.OpenCareer(careerPath, Environment()))
        {
            var screen = session.BankruptcyScreen();
            Assert.NotNull(screen);
            Assert.Equal(2, screen!.Round);
            Assert.Equal(1, screen.DeficitRounds);
            Assert.Equal(4, screen.GraceRounds);
            Assert.StartsWith("-", screen.FinalBalance, StringComparison.Ordinal);
            Assert.NotEmpty(screen.Seasons);

            Assert.Contains("bankrupt", Assert.Throws<InvalidOperationException>(() =>
                session.Apply(new ResultDraft
                {
                    Classified = [],
                    DidNotFinish = new Dictionary<string, string>(),
                    Disqualified = [],
                })).Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("bankrupt", Assert.Throws<InvalidOperationException>(() =>
                session.AutoSimulateRound()).Message, StringComparison.OrdinalIgnoreCase);

            using var home = new HomeViewModel(session);
            Assert.NotNull(home.BankruptcyScreen);
            Assert.True(home.IsCareerTerminal);
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        // ---- the ending replays byte-identically, decisions and terminal row included ----
        using var verify = CareerDatabase.Open(careerPath);
        var rules = Environment().Rules;
        var report = ReplayService.Resimulate(verify, unchecked((ulong)Seed), new ReplaySimInputs
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
        Assert.True(
            report.Identical,
            $"diverged: {report.FirstDivergence?.Reason} " +
            $"stored={report.FirstDivergence?.StoredDeltaJson} " +
            $"regenerated={report.FirstDivergence?.RegeneratedDeltaJson}");
    }

    [Fact]
    public void SolventCareer_HasNoBankruptcyProjection()
    {
        WritePack(1967);
        string careerPath = Path.Combine(_root, "careers", "solvent.ams2career");
        using var session = CareerSessionService.CreateCareer(Request(careerPath), Environment());
        Assert.Null(session.BankruptcyScreen());
        using var home = new HomeViewModel(session);
        Assert.Null(home.BankruptcyScreen);
        Assert.False(home.IsCareerTerminal);
    }

    // ---------- harness ----------

    /// <summary>Creates a Dynasty economy career and buries it: eight development increments
    /// (≈229k against a 100k opening balance) declared for the FINAL round send the settlement
    /// far past the −25k hard floor — an immediate, deterministic bankruptcy on round 2.</summary>
    private string CreateBankruptCareer()
    {
        WritePack(1967);
        string careerPath = Path.Combine(_root, "careers", "bankrupt.ams2career");
        using (var session = CareerSessionService.CreateCareer(Request(careerPath), Environment()))
        {
            ApplyRound(session);
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        using (var db = CareerDatabase.Open(careerPath))
        {
            long seasonId = CareerStore.ReadSeasons(db).Single().Id;
            for (int i = 0; i < 8; i++)
            {
                ReplayService.DeclareEconomyDecision(db, seasonId, round: 2, new DynastyEconomyDecision
                {
                    Kind = DynastyEconomyDecisionKind.BuyDevelopment,
                }, $"2026-07-18T00:00:{i:00}.0000000Z");
            }
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        using (var session = CareerSessionService.OpenCareer(careerPath, Environment()))
        {
            ApplyRound(session);
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        return careerPath;
    }

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

    private CareerEnvironment Environment()
    {
        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "documents"),
            library: TestPackBuilder.Library());
        environment.PackSearchRoots = () => [PacksRoot];
        return environment;
    }

    private CareerCreationRequest Request(string careerPath) => new()
    {
        PackDirectory = Path.Combine(PacksRoot, "1967"),
        CareerFilePath = careerPath,
        CareerName = "Dynasty bankruptcy gate",
        MasterSeed = Seed,
        ExperienceMode = CareerExperienceModes.GrandPrixDynasty,
        PlayerLiveryName = TestPackBuilder.StockLivery2,
        Character = VersionTwoCharacter(),
        DynastyEconomy = true,
    };

    private void WritePack(int year)
    {
        var pack = TestPackBuilder.TwoRoundPack();
        TestPackBuilder.Write(pack with
        {
            Manifest = pack.Manifest with
            {
                PackId = $"dynasty-{year}",
                Name = $"Synthetic {year}",
            },
            Season = pack.Season with
            {
                Year = year,
                SeriesName = $"Synthetic Championship {year}",
                Rounds =
                [
                    TestPackBuilder.Round(1, $"{year}-01-02"),
                    TestPackBuilder.Round(2, $"{year}-05-07"),
                ],
            },
        }, Path.Combine(PacksRoot, year.ToString()));
        // A one-pack Dynasty catalog: 1967 is also the terminal year, so no later packs needed.
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
