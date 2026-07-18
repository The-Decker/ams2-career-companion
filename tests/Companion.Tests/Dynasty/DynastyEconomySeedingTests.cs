using Companion.Core.Career;
using Companion.Core.Character;
using Companion.Core.Numerics;
using Companion.Core.Packs;
using Companion.Core.Smgp;
using Companion.Data;
using Companion.Tests.ViewModels;
using Companion.ViewModels.Services;

namespace Companion.Tests.Dynasty;

/// <summary>The economy gate at creation: ONLY an opted-in grandPrixDynasty career seeds
/// <see cref="Companion.Core.Dynasty.DynastyEconomyState"/> (opening balance pinned from the
/// starting team's tier, era-scaled). A Dynasty career without the opt-in, a legacy career, and
/// an SMGP career all seed nothing, their raw start blobs carry no "economy" key at all, the
/// byte-identical guarantee for every pre-feature save.</summary>
public sealed class DynastyEconomySeedingTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("companion-dynasty-economy-").FullName;

    private string PacksRoot => Path.Combine(_root, "packs");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    [Fact]
    public void OptedInDynasty_SeedsTheLedgerWithTierScaledOpeningFunds()
    {
        WritePack(1967);
        string careerPath = CareerPath("dynasty-economy");
        using (CareerSessionService.CreateCareer(
                   Request(careerPath, CareerExperienceModes.GrandPrixDynasty) with { DynastyEconomy = true },
                   Environment()))
        {
        }

        using var db = CareerDatabase.Open(careerPath);
        long seasonId = CareerStore.ReadSeasons(db).Single().Id;
        var start = StateStore.ReadPlayerState(db, seasonId, StateStore.StageStart)!;

        Assert.NotNull(start.Economy);
        var economy = start.Economy!;
        Assert.Equal(Companion.Core.Dynasty.DynastyEconomyRules.CurrentSchemaVersion, economy.Version);
        // The synthetic pack's only team is tier 5; 1967 sits in the index-1 era: 100000 × 1.
        Assert.Equal(Rational.Parse("100000"), economy.Balance);
        Assert.Equal(0, economy.DevelopmentLevel);
        Assert.Equal(0, economy.StaffTier);
        Assert.Empty(economy.Sponsors);
        Assert.False(economy.Bankrupt);
    }

    [Fact]
    public void Dynasty_WithoutTheOptIn_SeedsNothing()
    {
        WritePack(1967);
        string careerPath = CareerPath("dynasty-no-economy");
        using (CareerSessionService.CreateCareer(
                   Request(careerPath, CareerExperienceModes.GrandPrixDynasty),
                   Environment()))
        {
        }

        Assert.Null(ReadStartEconomy(careerPath));
        Assert.DoesNotContain("economy", ReadStartPlayerJson(careerPath), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LegacyCareer_IgnoresTheFlagEntirely()
    {
        WritePack(1967);
        string careerPath = CareerPath("legacy-flagged");
        // A legacy (null-mode) creation with the flag set anyway: no campaign plan exists, so the
        // mode half of the gate can never be satisfied, §2.1's "absent mode never infers Dynasty".
        using (CareerSessionService.CreateCareer(
                   RequestWithoutCharacter(careerPath) with { DynastyEconomy = true },
                   Environment()))
        {
        }

        Assert.Null(ReadStartEconomy(careerPath));
        Assert.DoesNotContain("economy", ReadStartPlayerJson(careerPath), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SmgpCareer_NeverSeedsTheEconomyEvenWithTheFlag()
    {
        string packDirectory = Path.Combine(PacksRoot, "smgp");
        TestPackBuilder.Write(SyntheticSmgpPack(), packDirectory);
        string careerPath = CareerPath("smgp-flagged");
        using (CareerSessionService.CreateCareer(
                   Request(careerPath, CareerExperienceModes.Smgp) with
                   {
                       PackDirectory = packDirectory,
                       SmgpMode = true,
                       DynastyEconomy = true,
                   },
                   Environment()))
        {
        }

        Assert.Null(ReadStartEconomy(careerPath));
        Assert.DoesNotContain("economy", ReadStartPlayerJson(careerPath), StringComparison.OrdinalIgnoreCase);
    }

    // ---------- harness ----------

    private CareerEnvironment Environment()
    {
        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "documents"),
            library: TestPackBuilder.Library());
        environment.PackSearchRoots = () => [PacksRoot];
        return environment;
    }

    private string CareerPath(string name) => Path.Combine(_root, "careers", name + ".ams2career");

    private Companion.Core.Dynasty.DynastyEconomyState? ReadStartEconomy(string careerPath)
    {
        using var db = CareerDatabase.Open(careerPath);
        long seasonId = CareerStore.ReadSeasons(db).Single().Id;
        return StateStore.ReadPlayerState(db, seasonId, StateStore.StageStart)!.Economy;
    }

    private static string ReadStartPlayerJson(string careerPath)
    {
        using var db = CareerDatabase.Open(careerPath);
        long seasonId = CareerStore.ReadSeasons(db).Single().Id;
        using var command = db.Connection.CreateCommand();
        command.CommandText =
            "SELECT state_json FROM player_state WHERE season_id = @season AND stage = 'start';";
        command.Parameters.AddWithValue("@season", seasonId);
        return (string)command.ExecuteScalar()!;
    }

    private CareerCreationRequest Request(string careerPath, string mode) => new()
    {
        PackDirectory = Path.Combine(PacksRoot, "1967"),
        CareerFilePath = careerPath,
        CareerName = "Dynasty economy seeding test",
        MasterSeed = 20260717,
        ExperienceMode = mode,
        PlayerLiveryName = TestPackBuilder.StockLivery2,
        Character = VersionTwoCharacter(),
    };

    private CareerCreationRequest RequestWithoutCharacter(string careerPath) => new()
    {
        PackDirectory = Path.Combine(PacksRoot, "1967"),
        CareerFilePath = careerPath,
        CareerName = "Legacy seeding test",
        MasterSeed = 20260717,
        PlayerLiveryName = TestPackBuilder.StockLivery2,
    };

    private void WritePack(int year) =>
        TestPackBuilder.Write(SyntheticPack(year), Path.Combine(PacksRoot, year.ToString()));

    private static SeasonPack SyntheticPack(int year)
    {
        var pack = TestPackBuilder.TwoRoundPack();
        return pack with
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
        };
    }

    private static SeasonPack SyntheticSmgpPack()
    {
        var pack = SyntheticPack(1990);
        var firstDate = new DateOnly(1990, 1, 1);
        return pack with
        {
            Manifest = pack.Manifest with
            {
                PackId = "smgp-1",
                Name = "Synthetic SMGP",
                CareerStyle = SmgpRules.CareerStyle,
            },
            Season = pack.Season with
            {
                Rounds = Enumerable.Range(1, 16)
                    .Select(round => TestPackBuilder.Round(
                        round,
                        firstDate.AddDays((round - 1) * 14).ToString("yyyy-MM-dd")))
                    .ToArray(),
            },
            Entries = pack.Entries
                .Select(entry => entry with { Rounds = "1-16" })
                .ToArray(),
        };
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
