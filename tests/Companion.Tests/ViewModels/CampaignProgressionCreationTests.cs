using System.Text.Json;
using Companion.Core.Career;
using Companion.Core.Character;
using Companion.Core.Json;
using Companion.Core.Packs;
using Companion.Data;
using Companion.ViewModels.Services;

namespace Companion.Tests.ViewModels;

/// <summary>Creation-boundary coverage for progression-v2 campaign pinning. These tests keep the
/// mutable pack catalog outside the save-file authority: the selected mode, complete character
/// input, campaign sequence, and every referenced pack blob become immutable career inputs.</summary>
public sealed class CampaignProgressionCreationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("companion-campaign-create-").FullName;

    private string PacksRoot => Path.Combine(_root, "packs");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    [Fact]
    public void ExplicitV2Dynasty_PinsFullCatalogPlanAndCharacterCreationInput()
    {
        WriteDynastyCatalog();
        string careerPath = CareerPath("dynasty");
        CharacterProfile character = VersionTwoCharacter();

        using (CareerSessionService.CreateCareer(
                   Request(careerPath, character, CareerExperienceModes.GrandPrixDynasty),
                   Environment()))
        {
        }

        using var db = CareerDatabase.Open(careerPath);
        long seasonId = CareerStore.ReadSeasons(db).Single().Id;
        var start = StateStore.ReadPlayerState(db, seasonId, StateStore.StageStart)!;
        var plan = Assert.IsType<CampaignProgressionPlan>(start.CampaignProgressionPlan);

        Assert.Equal(CareerExperienceModes.GrandPrixDynasty, start.ExperienceMode);
        Assert.Equal(character, start.Character);
        Assert.Equal(1967, plan.StartYear);
        Assert.Equal(2020, plan.EndYear);
        Assert.Equal(3, plan.TotalSeasons);
        Assert.Equal([1967, 1969, 2020], plan.PinnedSeasonSequence.Select(s => s.Year));
        Assert.Equal(
            ["dynasty-1967", "dynasty-1969", "dynasty-2020"],
            plan.PinnedSeasonSequence.Select(s => s.PackId));

        using (var count = db.Connection.CreateCommand())
        {
            count.CommandText = "SELECT COUNT(*) FROM pinned_pack;";
            Assert.Equal(3L, (long)count.ExecuteScalar()!);
        }
        foreach (var planned in plan.PinnedSeasonSequence)
        {
            var pinned = CareerStore.ReadPinnedPack(db, planned.PackId, planned.PackVersion);
            Assert.Equal(planned.Sha256, pinned.Sha256);
            Assert.Equal(planned.Year, PinnedPackEnvelope.LoadSeasonPack(pinned.PackJson).Season.Year);
        }

        string deltaJson = Assert.Single(
            JournalStore.ReadSeason(db, seasonId),
            row => row.Phase == JournalPhases.PlayerCharacter).DeltaJson;
        var input = JsonSerializer.Deserialize<CharacterCreationInput>(deltaJson, CoreJson.Options)!;
        Assert.Equal(character, input.Profile);
        Assert.Equal(CareerExperienceModes.GrandPrixDynasty, input.ExperienceMode);
        Assert.Equal(plan, input.CampaignProgressionPlan);

        using var document = JsonDocument.Parse(deltaJson);
        Assert.True(document.RootElement.TryGetProperty("version", out _));
        Assert.True(document.RootElement.TryGetProperty("profile", out var profile));
        Assert.True(document.RootElement.TryGetProperty("campaignProgressionPlan", out _));
        Assert.True(profile.TryGetProperty("racingDnaId", out _));
        Assert.True(profile.TryGetProperty("creationBaseline", out _));
    }

    [Fact]
    public void Reopen_UsesStoredPlanAfterDiskCatalogIsDeletedAndExpanded()
    {
        WriteDynastyCatalog();
        string careerPath = CareerPath("catalog-mutated");

        using (CareerSessionService.CreateCareer(
                   Request(careerPath, VersionTwoCharacter(), CareerExperienceModes.GrandPrixDynasty),
                   Environment()))
        {
        }

        string before = ReadStartPlayerJson(careerPath);
        CampaignProgressionPlan storedPlan = ReadStartPlan(careerPath);

        Directory.Delete(PacksRoot, recursive: true);
        WritePack(1975);

        using (var reopened = CareerSessionService.OpenCareer(careerPath, Environment()))
        {
            Assert.Equal("dynasty-1967", reopened.Pack.Manifest.PackId);
            Assert.Equal(1967, reopened.Pack.Season.Year);
        }

        Assert.Equal(before, ReadStartPlayerJson(careerPath));
        Assert.Equal(storedPlan, ReadStartPlan(careerPath));
        Assert.Equal([1967, 1969, 2020], ReadStartPlan(careerPath).PinnedSeasonSequence.Select(s => s.Year));

        using var db = CareerDatabase.Open(careerPath);
        using var command = db.Connection.CreateCommand();
        command.CommandText = "SELECT pack_id FROM pinned_pack ORDER BY pack_id;";
        using var reader = command.ExecuteReader();
        var pinnedIds = new List<string>();
        while (reader.Read())
            pinnedIds.Add(reader.GetString(0));
        Assert.Equal(["dynasty-1967", "dynasty-1969", "dynasty-2020"], pinnedIds);
        Assert.DoesNotContain("dynasty-1975", pinnedIds);
    }

    [Fact]
    public void DynastyContinuation_IgnoresMutableInterveningPackAndStartsPrePinnedOccurrence()
    {
        const long seed = 20260712;
        WriteDynastyCatalog();
        string careerPath = CareerPath("planned-continuation");

        using (var session = CareerSessionService.CreateCareer(
                   Request(careerPath, VersionTwoCharacter(), CareerExperienceModes.GrandPrixDynasty),
                   Environment()))
        {
            while (!session.Summary.SeasonComplete)
            {
                var grid = session.CurrentGrid();
                session.Apply(new ResultDraft
                {
                    Classified = grid.Select(seat => seat.DriverId).ToList(),
                    DidNotFinish = new Dictionary<string, string>(),
                    Disqualified = [],
                });
            }

            // The future directories are mutable catalog data now: remove both packs that were
            // present at creation and add a tempting in-between year that the stored plan omitted.
            Directory.Delete(Path.Combine(PacksRoot, "1969"), recursive: true);
            Directory.Delete(Path.Combine(PacksRoot, "2020"), recursive: true);
            WritePack(1968);

            var next = Assert.IsType<NextSeasonInfo>(session.NextSeason());
            Assert.False(next.IsCarryover);
            Assert.Equal("", next.PackDirectory);
            Assert.Equal("dynasty-1969", next.PackId);
            Assert.Equal(1969, next.SeasonYear);
            Assert.Equal([1968], next.BridgedYears);

            var review = Assert.IsType<SeasonReviewModel>(session.SeasonReview());
            string teamId = Assert.Single(review.Offers).TeamId;
            session.AcceptOffer(teamId);
            session.StartNextSeason(teamId);
        }

        using (var reopened = CareerSessionService.OpenCareer(careerPath, Environment()))
        {
            Assert.Equal(1969, reopened.Summary.SeasonYear);
            Assert.Equal("dynasty-1969", reopened.Pack.Manifest.PackId);
            Assert.Equal(1969, reopened.Pack.Season.Year);
        }

        using var db = CareerDatabase.Open(careerPath);
        Assert.Equal(
            [(1967, "dynasty-1967"), (1969, "dynasty-1969")],
            CareerStore.ReadSeasons(db).Select(season => (season.Year, season.PackId)));
        using (var mutablePin = db.Connection.CreateCommand())
        {
            mutablePin.CommandText = "SELECT COUNT(*) FROM pinned_pack WHERE pack_id = 'dynasty-1968';";
            Assert.Equal(0L, (long)mutablePin.ExecuteScalar()!);
        }

        var rules = Environment().Rules;
        var report = ReplayService.Resimulate(db, unchecked((ulong)seed), new ReplaySimInputs
        {
            AgingCurves = rules.AgingCurves,
            Archetypes = rules.Archetypes,
            Headlines = rules.Headlines,
            PlayerDriverId = "driver.hulme",
            PlayerAge = 22,
            CharacterRules = rules.Character,
        });
        Assert.True(
            report.Identical,
            $"diverged: {report.FirstDivergence?.Reason} " +
            $"stored={report.FirstDivergence?.StoredDeltaJson} " +
            $"regenerated={report.FirstDivergence?.RegeneratedDeltaJson}");
    }

    [Fact]
    public void LegacyV1WithoutMode_KeepsCompactInputAndOmitsCampaignStartFields()
    {
        WritePack(1967);
        string careerPath = CareerPath("legacy");
        var character = VersionOneCharacter();

        using (CareerSessionService.CreateCareer(Request(careerPath, character, mode: null), Environment()))
        {
        }

        using var db = CareerDatabase.Open(careerPath);
        long seasonId = CareerStore.ReadSeasons(db).Single().Id;
        string deltaJson = Assert.Single(
            JournalStore.ReadSeason(db, seasonId),
            row => row.Phase == JournalPhases.PlayerCharacter).DeltaJson;

        using (var document = JsonDocument.Parse(deltaJson))
        {
            string[] names = document.RootElement.EnumerateObject()
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(["cpUnspent", "name", "perkIds", "stats"], names);
        }

        string startJson = ReadStartPlayerJson(db, seasonId);
        Assert.DoesNotContain("experienceMode", startJson, StringComparison.Ordinal);
        Assert.DoesNotContain("campaignProgressionPlan", startJson, StringComparison.Ordinal);
        Assert.DoesNotContain("xpScaleRemainder", startJson, StringComparison.Ordinal);

        var start = StateStore.ReadPlayerState(db, seasonId, StateStore.StageStart)!;
        Assert.Equal(character, start.Character);
        Assert.Null(start.ExperienceMode);
        Assert.Null(start.CampaignProgressionPlan);
        Assert.Equal(0, start.XpScaleRemainder);
    }

    public static TheoryData<string?, Type> InvalidVersionTwoModes => new()
    {
        { null, typeof(InvalidOperationException) },
        { "unknown-mode", typeof(InvalidOperationException) },
        { CareerExperienceModes.RacingPassport, typeof(InvalidOperationException) },
    };

    [Theory]
    [MemberData(nameof(InvalidVersionTwoModes))]
    public void InvalidV2Mode_FailsBeforeCareerFileExists(string? mode, Type exceptionType)
    {
        WritePack(1967);
        string suffix = mode ?? "missing";
        string careerPath = CareerPath(suffix + ".invalid");

        Exception exception = Assert.Throws(
            exceptionType,
            () => CareerSessionService.CreateCareer(
                Request(careerPath, VersionTwoCharacter(), mode),
                Environment()));

        Assert.NotNull(exception);
        Assert.False(File.Exists(careerPath));
    }

    [Fact]
    public void DynastyRejectsAStartOutsideThe1960To2020HorizonBeforeCreatingAFile()
    {
        WritePack(1959);
        string careerPath = CareerPath("before-horizon.invalid");
        var request = Request(
            careerPath,
            VersionTwoCharacter(),
            CareerExperienceModes.GrandPrixDynasty) with
        {
            PackDirectory = Path.Combine(PacksRoot, "1959"),
        };

        Assert.Throws<InvalidOperationException>(() =>
            CareerSessionService.CreateCareer(request, Environment()));
        Assert.False(File.Exists(careerPath));
    }

    [Fact]
    public void OneSeason2020DynastyTerminatesEvenWhenLaterDiskPacksExist()
    {
        WritePack(2020);
        WritePack(2021);
        string careerPath = CareerPath("terminal-2020");
        var request = Request(
            careerPath,
            VersionTwoCharacter(),
            CareerExperienceModes.GrandPrixDynasty) with
        {
            PackDirectory = Path.Combine(PacksRoot, "2020"),
        };

        using var session = CareerSessionService.CreateCareer(request, Environment());
        while (!session.Summary.SeasonComplete)
        {
            var grid = session.CurrentGrid();
            session.Apply(new ResultDraft
            {
                Classified = grid.Select(seat => seat.DriverId).ToList(),
                DidNotFinish = new Dictionary<string, string>(),
                Disqualified = [],
            });
        }

        Assert.Null(session.NextSeason());
    }

    [Fact]
    public void PinPackEnvelopeRejectsAnEmbeddedIdentityThatDoesNotMatchItsKey()
    {
        WritePack(1967);
        var files = SeasonPackFiles.Read(Path.Combine(PacksRoot, "1967"));
        byte[] bytes = files.ToPinnedEnvelope().ToBytes();
        string databasePath = Path.Combine(_root, "identity-mismatch.ams2career");

        using var db = CareerDatabase.Open(databasePath);
        Assert.Throws<ArgumentException>(() => CareerStore.PinPackEnvelope(
            db,
            packId: "different-id",
            packVersion: "1.0.0",
            bytes,
            pinnedUtc: "2026-07-12T00:00:00.0000000Z"));
        using var count = db.Connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM pinned_pack;";
        Assert.Equal(0L, (long)count.ExecuteScalar()!);
    }

    private CareerEnvironment Environment()
    {
        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "documents"),
            library: TestPackBuilder.Library());
        environment.PackSearchRoots = () => [PacksRoot];
        return environment;
    }

    private string CareerPath(string name) => Path.Combine(_root, "careers", name + ".ams2career");

    private static CareerCreationRequest Request(
        string careerPath,
        CharacterProfile character,
        string? mode) => new()
    {
        PackDirectory = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(careerPath))!, "packs", "1967"),
        CareerFilePath = careerPath,
        CareerName = "Campaign creation test",
        MasterSeed = 20260712,
        ExperienceMode = mode,
        PlayerLiveryName = TestPackBuilder.StockLivery2,
        Character = character,
    };

    private void WriteDynastyCatalog()
    {
        WritePack(1967);
        WritePack(1969);
        WritePack(2020);
    }

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
            Name = "Campaign Driver",
            Age = 22,
            Stats = all,
            PerkIds = ["rain_man"],
            CreationPerkIds = ["rain_man"],
            ChosenFlavor = "wetSkill",
            ProgressionVersion = CharacterLevelProgression.Level300Version,
            RacingDnaId = "dna_circuit_specialist",
            RacingDnaVersion = 1,
            RacingDnaChoice = "technical",
            CreationBaseline = new CharacterCreationBaseline
            {
                Stats = talent,
                Meta = meta,
                TraitIds = ["rain_man"],
                ChosenFlavor = "wetSkill",
            },
        };
    }

    private static CharacterProfile VersionOneCharacter() => new()
    {
        Name = "Legacy Driver",
        Age = 30,
        Stats = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["pace"] = 0.70,
            ["oneLap"] = 0.65,
            ["craft"] = 0.60,
            ["racecraft"] = 0.62,
            ["adaptability"] = 0.58,
            ["marketability"] = 0.50,
            ["durability"] = 0.55,
        },
        PerkIds = ["rain_man"],
        CreationPerkIds = ["rain_man"],
        ProgressionVersion = CharacterLevelProgression.EraCappedVersion,
        CpUnspent = 2,
    };

    private static CampaignProgressionPlan ReadStartPlan(string careerPath)
    {
        using var db = CareerDatabase.Open(careerPath);
        long seasonId = CareerStore.ReadSeasons(db).Single().Id;
        return StateStore.ReadPlayerState(db, seasonId, StateStore.StageStart)!.CampaignProgressionPlan!;
    }

    private static string ReadStartPlayerJson(string careerPath)
    {
        using var db = CareerDatabase.Open(careerPath);
        long seasonId = CareerStore.ReadSeasons(db).Single().Id;
        return ReadStartPlayerJson(db, seasonId);
    }

    private static string ReadStartPlayerJson(CareerDatabase db, long seasonId)
    {
        using var command = db.Connection.CreateCommand();
        command.CommandText =
            "SELECT state_json FROM player_state WHERE season_id = @season AND stage = 'start';";
        command.Parameters.AddWithValue("@season", seasonId);
        return (string)command.ExecuteScalar()!;
    }
}
