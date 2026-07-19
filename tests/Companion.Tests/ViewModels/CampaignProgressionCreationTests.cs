using System.Text.Json;
using Companion.Core.Career;
using Companion.Core.Character;
using Companion.Core.Json;
using Companion.Core.Packs;
using Companion.Core.Smgp;
using Companion.Data;
using Companion.ViewModels.Hub;
using Companion.ViewModels.Review;
using Companion.ViewModels.Services;
using Companion.ViewModels.Wizard;

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
    public void DynastyWizardPreview_UsesThePinnedSparseCatalogSequenceCreationWillPersist()
    {
        WriteDynastyCatalog();
        var environment = Environment();
        var wizard = new NewCareerWizardViewModel(
            environment,
            new FakeCareerFactory(),
            packSearchRoots: [PacksRoot],
            careersDirectory: Path.Combine(_root, "wizard-careers"),
            seedSource: new Random(7),
            experienceMode: CareerExperienceModes.GrandPrixDynasty);

        wizard.SelectedPack = Assert.Single(wizard.Packs, pack => pack.SeasonYear == 1967);
        wizard.NextCommand.Execute(null);

        var preview = Assert.IsType<CampaignProgressionPlan>(wizard.ResolvedCampaignPlan);
        Assert.Equal(3, wizard.CampaignTotalSeasons);
        Assert.Equal(2, wizard.CampaignMasterySeason);
        Assert.Equal([1967, 1969, 2020], preview.PinnedSeasonSequence.Select(season => season.Year));
        Assert.Contains("3 FAITHFUL SEASONS PINNED", wizard.CampaignCoverageSummary, StringComparison.Ordinal);
        Assert.Contains("PLAYABLE YEARS: 1967, 1969, 2020", wizard.CampaignCoverageSummary, StringComparison.Ordinal);
        Assert.Contains("MASTERY AFTER SEASON 2", wizard.CampaignPacingSummary, StringComparison.Ordinal);

        string careerPath = CareerPath("preview-parity");
        using (CareerSessionService.CreateCareer(
                   Request(
                       careerPath,
                       VersionTwoCharacter(),
                       CareerExperienceModes.GrandPrixDynasty),
                   environment))
        {
        }

        Assert.Equal(preview, ReadStartPlan(careerPath));
    }

    [Fact]
    public void ChangingSeasonAfterAParsedCampaign_ClearsEveryStaleConfirmAndPreviewProjection()
    {
        WriteDynastyCatalog();
        var environment = Environment();
        var wizard = new NewCareerWizardViewModel(
            environment,
            new FakeCareerFactory(),
            packSearchRoots: [PacksRoot],
            careersDirectory: Path.Combine(_root, "wizard-stale-careers"),
            seedSource: new Random(8),
            inferExperienceModeFromPack: true);

        wizard.SelectedPack = Assert.Single(wizard.Packs, pack => pack.SeasonYear == 1967);
        wizard.NextCommand.Execute(null); // -> Verification and resolved three-season preview
        if (wizard.HasWarnings) wizard.ProceedAnyway = true;
        wizard.NextCommand.Execute(null); // -> Character
        var firstCharacter = Assert.IsType<CharacterViewModel>(wizard.Character);
        firstCharacter.SelectedCountry = firstCharacter.CountryOptions.Single(option => option.Code == "BRA");
        wizard.NextCommand.Execute(null); // -> SeatPick
        wizard.SelectedSeat = wizard.Seats.First(seat => seat.LiveryName == TestPackBuilder.StockLivery2);
        wizard.NextCommand.Execute(null); // -> Grid
        wizard.NextCommand.Execute(null); // -> Confirm
        Assert.True(wizard.CanCreate);
        Assert.Equal(3, wizard.CampaignTotalSeasons);
        Assert.Equal(CareerExperienceModes.GrandPrixDynasty, wizard.ExperienceMode);

        while (wizard.Step != WizardStep.SeasonPick)
            wizard.BackCommand.Execute(null);
        Assert.True(wizard.CanCreate); // the unchanged parsed campaign is still internally complete

        var notifications = new HashSet<string?>();
        wizard.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);
        wizard.SelectedPack = Assert.Single(wizard.Packs, pack => pack.SeasonYear == 1969);

        Assert.Null(wizard.Pack);
        Assert.Null(wizard.ResolvedCampaignPlan);
        Assert.Equal(0, wizard.CampaignTotalSeasons);
        Assert.Null(wizard.ExperienceMode);
        Assert.False(wizard.HasResolvedExperienceMode);
        Assert.Equal("CAREER MODE PENDING", wizard.ExperienceModeLabel);
        Assert.Contains("SELECT A SEASON", wizard.CampaignPacingSummary, StringComparison.Ordinal);
        Assert.False(wizard.CanCreate);
        Assert.Null(wizard.SelectedSeat);
        Assert.Empty(wizard.GridChoices);
        Assert.Same(firstCharacter, wizard.Character); // preserved until the next pack is prepared
        Assert.Contains(nameof(NewCareerWizardViewModel.ExperienceMode), notifications);
        Assert.Contains(nameof(NewCareerWizardViewModel.HasResolvedExperienceMode), notifications);
        Assert.Contains(nameof(NewCareerWizardViewModel.ExperienceModeLabel), notifications);
        Assert.Contains(nameof(NewCareerWizardViewModel.CampaignPacingSummary), notifications);
        Assert.Contains(nameof(NewCareerWizardViewModel.IncludedCount), notifications);
        Assert.Contains(nameof(NewCareerWizardViewModel.CanCreate), notifications);

        wizard.NextCommand.Execute(null); // -> Verification for 1969
        Assert.Equal(CareerExperienceModes.GrandPrixDynasty, wizard.ExperienceMode);
        Assert.Equal([1969, 2020], wizard.ResolvedCampaignPlan!.PinnedSeasonSequence.Select(pin => pin.Year));
        Assert.Same(firstCharacter, wizard.Character);
        if (wizard.HasWarnings) wizard.ProceedAnyway = true;
        wizard.NextCommand.Execute(null); // -> Character, now rebuilt against the new campaign roster
        Assert.NotSame(firstCharacter, wizard.Character);
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
        Assert.Equal("BRA", start.Character!.CountryCode);
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
        Assert.Equal("BRA", profile.GetProperty("countryCode").GetString());
    }

    [Fact]
    public void ExplicitV2Smgp_ReopensWithTheSeventeenSeasonPlanAndNinetyNodeTree()
    {
        string packDirectory = Path.Combine(PacksRoot, "smgp");
        TestPackBuilder.Write(SyntheticSmgpPack(), packDirectory);
        string careerPath = CareerPath("smgp-v2");
        var request = Request(
            careerPath,
            VersionTwoCharacter(),
            CareerExperienceModes.Smgp) with
        {
            PackDirectory = packDirectory,
            SmgpMode = true,
        };

        using (var session = CareerSessionService.CreateCareer(request, Environment()))
        {
            var tree = Assert.IsType<SkillTreeSnapshot>(session.SkillTree());
            Assert.Equal(9, tree.Branches.Count);
            Assert.Equal(90, tree.Branches.Sum(branch => branch.Nodes.Count(node => node.Kind == "mastery")));
            Assert.Equal(119, tree.Branches.Sum(branch => branch.Nodes.Count(node => node.Kind == "attribute")));
        }

        using (var session = CareerSessionService.OpenCareer(careerPath, Environment()))
        {
            var tree = Assert.IsType<SkillTreeSnapshot>(session.SkillTree());
            Assert.Equal(90, tree.Branches.Sum(branch => branch.Nodes.Count(node => node.Kind == "mastery")));
            Assert.Equal(119, tree.Branches.Sum(branch => branch.Nodes.Count(node => node.Kind == "attribute")));
        }

        using var db = CareerDatabase.Open(careerPath);
        long seasonId = CareerStore.ReadSeasons(db).Single().Id;
        var start = StateStore.ReadPlayerState(db, seasonId, StateStore.StageStart)!;
        var plan = Assert.IsType<CampaignProgressionPlan>(start.CampaignProgressionPlan);
        Assert.Equal(CareerExperienceModes.Smgp, start.ExperienceMode);
        Assert.Equal(CharacterLevelProgression.Level300Version, start.Character!.ProgressionVersion);
        Assert.Equal(SmgpRules.CampaignSeasons, plan.TotalSeasons);
        Assert.Equal(SmgpRules.CampaignSeasons - 1, plan.MasterySeason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("oval")]
    public void ExplicitV2Creation_FailsClosedOnMissingOrInvalidCatalogChoice(string? choice)
    {
        WriteDynastyCatalog();
        string careerPath = CareerPath("invalid-dna-choice");
        var character = VersionTwoCharacter() with { RacingDnaChoice = choice };

        Assert.Throws<InvalidOperationException>(() =>
            CareerSessionService.CreateCareer(
                Request(careerPath, character, CareerExperienceModes.GrandPrixDynasty),
                Environment()));
    }

    [Theory]
    [InlineData("driver.unknown", null, "is not present")]
    [InlineData("driver.hulme", null, "seat the player is replacing")]
    [InlineData("driver.brabham", TestPackBuilder.StockLivery2, "excluded from the selected season grid")]
    public void DuelistCreation_FailsBeforeFileWhenThePersistedRivalIsUnavailable(
        string rivalDriverId,
        string? onlyIncludedLivery,
        string expectedMessage)
    {
        WritePack(1967);
        string careerPath = CareerPath("invalid-duelist-context");
        var character = VersionTwoCharacter() with
        {
            RacingDnaId = "dna_duelist",
            RacingDnaChoice = rivalDriverId,
        };
        var request = Request(
            careerPath,
            character,
            CareerExperienceModes.GrandPrixDynasty) with
        {
            GridSelection = onlyIncludedLivery is null
                ? null
                : new Companion.Core.Grid.GridSelection
                {
                    IncludedLiveries = [onlyIncludedLivery],
                },
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            CareerSessionService.CreateCareer(request, Environment()));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(careerPath));
    }

    [Fact]
    public void NationalHeroCreation_FailsBeforeFileWhenAffinityIsAbsentFromTheRoster()
    {
        WritePack(1967);
        string careerPath = CareerPath("invalid-nationality-context");
        var character = VersionTwoCharacter() with
        {
            RacingDnaId = "dna_national_hero",
            RacingDnaChoice = "GBR",
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            CareerSessionService.CreateCareer(
                Request(careerPath, character, CareerExperienceModes.GrandPrixDynasty),
                Environment()));

        Assert.Contains("is not present in the selected campaign roster", exception.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(careerPath));
    }

    [Fact]
    public void ContextualRacingDnaChoice_IsAcceptedWhenThePackAndGridContainIt()
    {
        var pack = SyntheticPack(1967) with
        {
            Drivers = SyntheticPack(1967).Drivers
                .Select(driver => driver with { Country = "GBR" })
                .ToArray(),
        };
        TestPackBuilder.Write(pack, Path.Combine(PacksRoot, "1967"));
        string careerPath = CareerPath("valid-nationality-context");
        var character = VersionTwoCharacter() with
        {
            RacingDnaId = "dna_national_hero",
            RacingDnaChoice = "GBR",
        };

        using var session = CareerSessionService.CreateCareer(
            Request(careerPath, character, CareerExperienceModes.GrandPrixDynasty),
            Environment());

        Assert.True(File.Exists(careerPath));
        Assert.NotNull(session.CharacterDossier());
    }

    [Fact]
    public void VersionTwoSessionAndDossierShareThePhaseGatedSkillPointBalance()
    {
        WriteDynastyCatalog();
        string careerPath = CareerPath("skill-point-projection");
        using (CareerSessionService.CreateCareer(
                   Request(
                       careerPath,
                       VersionTwoCharacter(),
                       CareerExperienceModes.GrandPrixDynasty),
                   Environment()))
        {
        }

        // A three-season Dynasty plan masters after season two. At level 300 after one completed
        // season, the season gate (floor(499 * 1 / 2) = 249) is authoritative despite the full
        // 499-point level pool.
        using (var db = CareerDatabase.Open(careerPath))
        {
            long seasonId = CareerStore.ReadSeasons(db).Single().Id;
            var start = StateStore.ReadPlayerState(db, seasonId, StateStore.StageStart)!;
            StateStore.UpsertPlayerState(
                db,
                seasonId,
                StateStore.StageStart,
                start with
                {
                    Level = CharacterLevelProgression.Level300Max,
                    Xp = 14_951,
                    SeasonsCompleted = 1,
                });
        }

        using (var session = CareerSessionService.OpenCareer(careerPath, Environment()))
        {
            Assert.Equal(249, session.AvailableCharacterCp());
            Assert.Equal(249, session.CharacterDossier()!.CpUnspent);

            Assert.Equal(0, session.RespecTokensAvailable());
            Assert.Throws<InvalidOperationException>(() => session.RespecNode("engineers_favorite"));

            var legacySpend = Assert.Throws<InvalidOperationException>(() =>
                session.SpendCharacterPoint(CharacterSpend.Stat("raise_pace_1", cost: 999)));
            Assert.Contains("ApplySkillPlan", legacySpend.Message, StringComparison.Ordinal);
            Assert.Equal(249, session.AvailableCharacterCp());
            Assert.Equal(249, session.CharacterDossier()!.CpUnspent);
        }

        using var verify = CareerDatabase.Open(careerPath);
        long verifySeasonId = CareerStore.ReadSeasons(verify).Single().Id;
        Assert.DoesNotContain(
            JournalStore.ReadSeason(verify, verifySeasonId),
            row => row.Phase is JournalPhases.PlayerRespec or JournalPhases.PlayerStatSpend);
    }

    [Fact]
    public void OneSeasonDynasty_ReachesLevelAndSkillPointMasteryAtTheRealSeasonReview()
    {
        WritePack(2020);
        string careerPath = CareerPath("one-season-mastery");
        var request = new CareerCreationRequest
        {
            PackDirectory = Path.Combine(PacksRoot, "2020"),
            CareerFilePath = careerPath,
            CareerName = "One-season mastery",
            MasterSeed = 20260713,
            ExperienceMode = CareerExperienceModes.GrandPrixDynasty,
            PlayerLiveryName = TestPackBuilder.StockLivery2,
            Character = VersionTwoCharacter(),
        };

        using var session = CareerSessionService.CreateCareer(request, Environment());
        while (!session.Summary.SeasonComplete)
        {
            var classified = session.CurrentGrid().Select(seat => seat.DriverId).ToList();
            string playerId = session.CurrentGrid().Single(seat => seat.IsPlayer).DriverId;
            Assert.True(classified.Remove(playerId));
            classified.Insert(0, playerId);
            session.Apply(new ResultDraft
            {
                Classified = classified,
                DidNotFinish = new Dictionary<string, string>(),
                Disqualified = [],
            });
        }

        Assert.NotNull(session.SeasonReview());
        var dossier = Assert.IsType<CharacterDossier>(session.CharacterDossier());
        Assert.Equal(CharacterLevelProgression.Level300Max, dossier.Level);
        Assert.True(dossier.Xp >= 14_951);
        Assert.Equal(0, dossier.XpForNextLevel);
        Assert.Equal(CharacterProgressionV2Math.LifetimeSkillPoints, session.AvailableCharacterCp());
        Assert.Equal(CharacterProgressionV2Math.LifetimeSkillPoints, dossier.CpUnspent);

        var review = new SeasonReviewViewModel(session);
        var dossierVm = new DossierViewModel(session);
        Assert.Equal(CharacterProgressionV2Math.LifetimeSkillPoints, review.AvailableCp);
        Assert.Equal(CharacterProgressionV2Math.LifetimeSkillPoints, dossierVm.SkillPointsAvailable);

        using var db = CareerDatabase.Open(careerPath);
        long seasonId = CareerStore.ReadSeasons(db).Single().Id;
        var end = StateStore.ReadPlayerState(db, seasonId, StateStore.StageEnd)!;
        Assert.Equal(1, end.SeasonsCompleted);
        Assert.Equal(1, end.CampaignProgressionPlan!.MasterySeason);
        Assert.Equal(CharacterLevelProgression.Level300Max, end.Level);
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
            int pointsBeforeSpend = session.AvailableCharacterCp();
            Assert.True(pointsBeforeSpend > 0);
            session.ApplySkillPlan(["pace_rhythm"]);
            Assert.Equal(pointsBeforeSpend - 1, session.AvailableCharacterCp());
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

        var seasons = CareerStore.ReadSeasons(db);
        var firstStart = StateStore.ReadPlayerState(db, seasons[0].Id, StateStore.StageStart)!;
        var firstEnd = StateStore.ReadPlayerState(db, seasons[0].Id, StateStore.StageEnd)!;
        var secondStart = StateStore.ReadPlayerState(db, seasons[1].Id, StateStore.StageStart)!;
        var plan = Assert.IsType<CampaignProgressionPlan>(firstStart.CampaignProgressionPlan);

        // Every v2 XP row exposes the pinned rational audit trail. Replaying the rows as a simple
        // carry chain reaches the stored season-end state exactly, and rollover carries that state
        // into the next pre-pinned occurrence without consulting the mutated pack catalog.
        long expectedXp = firstStart.Xp;
        long expectedRemainder = firstStart.XpScaleRemainder;
        var xpRows = JournalStore.ReadSeason(db, seasons[0].Id)
            .Where(row => row.Phase == JournalPhases.PlayerXp)
            .ToArray();
        Assert.NotEmpty(xpRows);
        foreach (var row in xpRows)
        {
            using var delta = JsonDocument.Parse(row.DeltaJson);
            var root = delta.RootElement;
            long signedRawXp = root.GetProperty("signedRawXp").GetInt64();
            var normalized = CharacterProgressionV2Math.NormalizeXpAward(
                signedRawXp, isEligible: true, expectedRemainder, plan);

            Assert.Equal(expectedXp, root.GetProperty("from").GetInt64());
            Assert.Equal(normalized.EligibleRawXp, root.GetProperty("eligibleRawXp").GetInt64());
            Assert.Equal(normalized.AppliedXp, root.GetProperty("appliedXp").GetInt64());
            Assert.Equal(expectedRemainder, root.GetProperty("remainderBefore").GetInt64());
            Assert.Equal(normalized.RemainderAfter, root.GetProperty("remainderAfter").GetInt64());

            expectedXp = checked(expectedXp + normalized.AppliedXp);
            expectedRemainder = normalized.RemainderAfter;
            Assert.Equal(expectedXp, root.GetProperty("to").GetInt64());
        }
        Assert.Equal(expectedXp, firstEnd.Xp);
        Assert.Equal(expectedRemainder, firstEnd.XpScaleRemainder);
        Assert.Equal(firstEnd.Xp, secondStart.Xp);
        Assert.Equal(firstEnd.XpScaleRemainder, secondStart.XpScaleRemainder);
        Assert.Equal(plan, secondStart.CampaignProgressionPlan);
        Assert.Equal(1, secondStart.Character!.SkillPointsSpent);
        Assert.Equal(0, secondStart.Character.CpSpent);
        Assert.Contains("pace_rhythm", secondStart.Character.AcquiredSkillIds!);
        Assert.Empty(secondStart.Character.SkillNodeIds);

        var rules = Environment().Rules;
        var report = ReplayService.Resimulate(db, unchecked((ulong)seed), new ReplaySimInputs
        {
            AgingCurves = rules.AgingCurves,
            Archetypes = rules.Archetypes,
            Headlines = rules.Headlines,
            PlayerDriverId = "driver.hulme",
            PlayerAge = 22,
            CharacterRules = rules.Character,
            MasterySkills = rules.MasterySkills,
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
        // racingPassport was REMOVED: it is a creatable mode now (the 2026-07-18 pure-racing
        // decision). A Passport request carrying a v2 character is rejected as contradictory
        // input instead, covered in RacingPassportTests.
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
    public void ConditionalPlayerCarPhysicsPerk_CreatesThenPersistsInferredWeatherOnApply()
    {
        var pack = SyntheticPack(1967);
        var wetRound = pack.Season.Rounds[0] with
        {
            SetupGuide = new PackSetupGuide
            {
                Session = new PackSessionSettings
                {
                    Opponents = 1,
                    WeatherSlots = ["Rain"],
                },
            },
        };
        pack = pack with
        {
            Season = pack.Season with
            {
                Rounds = [wetRound, pack.Season.Rounds[1]],
            },
        };
        TestPackBuilder.Write(pack, Path.Combine(PacksRoot, "1967"));
        string careerPath = CareerPath("conditional-car-scalar");
        var original = VersionTwoCharacter();
        var character = original with
        {
            PerkIds = ["rain_man"],
            CreationPerkIds = ["rain_man"],
            CreationBaseline = original.CreationBaseline! with { TraitIds = ["rain_man"] },
        };

        using var session = CareerSessionService.CreateCareer(
            Request(careerPath, character, CareerExperienceModes.GrandPrixDynasty),
            Environment());
        Assert.True(File.Exists(careerPath));

        using var db = CareerDatabase.Open(careerPath);
        long seasonId = CareerStore.ReadSeasons(db).Single().Id;
        Assert.Empty(PlayerRoundConditionsStore.ReadSeason(db, seasonId, session.Pack));
        Assert.True(session.CurrentRoundIsWet());

        var grid = session.CurrentGrid();
        var player = Assert.Single(grid, seat => seat.IsPlayer);
        Assert.True(player.PlayerCarScalarsAuthoritative);
        Assert.Equal(1.020, player.PowerScalar, 6);

        session.Apply(new ResultDraft
        {
            Classified = grid.Select(seat => seat.DriverId).ToList(),
            DidNotFinish = new Dictionary<string, string>(),
            Disqualified = [],
            IsWet = true,
        });

        var persisted = Assert.Single(PlayerRoundConditionsStore.ReadSeason(db, seasonId, session.Pack));
        Assert.Equal(1, persisted.Key);
        Assert.True(persisted.Value.IsWet);
    }

    [Fact]
    public void ConditionalPlayerCarPhysicsPerk_IsHiddenAndRejectedForV2()
    {
        WritePack(1967);
        string careerPath = CareerPath("conditional-car-spend");
        using (CareerSessionService.CreateCareer(
                   Request(
                       careerPath,
                       VersionTwoCharacter(),
                       CareerExperienceModes.GrandPrixDynasty),
                   Environment()))
        {
        }

        // Give the profile a real v2 bank through its level and campaign phase gates. The old
        // prerelease test wrote CpUnspent here, but that legacy field is intentionally inert in v2.
        using (var db = CareerDatabase.Open(careerPath))
        {
            long seasonId = CareerStore.ReadSeasons(db).Single().Id;
            var start = StateStore.ReadPlayerState(db, seasonId, StateStore.StageStart)!;
            StateStore.UpsertPlayerState(
                db,
                seasonId,
                StateStore.StageStart,
                start with
                {
                    Level = CharacterLevelProgression.Level300Max,
                    Xp = 14_951,
                    SeasonsCompleted = 1,
                });
        }

        using var session = CareerSessionService.OpenCareer(careerPath, Environment());
        Assert.DoesNotContain(
            session.SkillTree()!.Branches.SelectMany(branch => branch.Nodes),
            node => node.Id == "rain_man");
        Assert.Empty(session.PurchasablePerks());
        var exception = Assert.Throws<InvalidOperationException>(() =>
            session.SpendCharacterPoint(CharacterSpend.Perk("rain_man", 1)));
        Assert.Contains("ApplySkillPlan", exception.Message, StringComparison.Ordinal);
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
            Name = "Campaign Driver",
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
