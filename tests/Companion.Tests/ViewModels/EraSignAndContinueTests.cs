using Companion.Core.Career;
using Companion.Core.Packs;
using Companion.Core.Scoring;
using Companion.Data;
using Companion.ViewModels.Review;
using Companion.ViewModels.Services;
using Companion.ViewModels.Shell;

namespace Companion.Tests.ViewModels;

/// <summary>
/// The M6 sign-and-continue flow proven END TO END on the REAL session service with
/// synthetic era packs (a 1967-style two-round season transitioning into a 1969-style pack,
/// so 1968 is a bridged gap year): next-pack discovery follows the v1 smallest-later-year
/// rule, signing executes EraTransition + CareerStore.StartNextSeason, reopening the career
/// lands in the NEW season's round 1 (the MRU/continue contract), the transitioned season
/// folds results normally, the plan's validation errors surface on the review screen, and
/// the no-next-pack state explains what season packs are.
/// </summary>
public sealed class EraSignAndContinueTests : IDisposable
{
    private const string PlayerLivery = "Mid #4";
    private const string Season2Livery = "Next69 #4";

    private readonly string _root = Directory.CreateTempSubdirectory("companion-era-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // SQLite WAL sidecars can outlive the connection briefly on Windows.
        }
    }

    private string PacksRoot => Path.Combine(_root, "packs");

    private string CareerPath => Path.Combine(_root, "career.ams2career");

    private CareerEnvironment Environment() => new()
    {
        ContentLibrary = TestPackBuilder.Library(),
        LocateInstall = static () => null,
        DocumentsDirectory = Path.Combine(_root, "docs"),
        RulesDirectory = ViewModelTestData.RulesDirectory,
        PackSearchRoots = () => [PacksRoot],
    };

    // ---------- synthetic era packs ----------

    /// <summary>The 1967-style source season: two teams across the tier range, four drivers
    /// with Born years (the transition ages them through the 1968 gap), two rounds.</summary>
    private static SeasonPack FromPack1967() => new()
    {
        Manifest = new PackManifest
        {
            PackId = "era-test-1967",
            Name = "Era Test 1967",
            Version = "1.0.0",
            FormatVersion = 1,
        },
        Season = new SeasonDefinition
        {
            Year = 1967,
            SeriesName = "Era Test Series",
            Ams2Class = TestPackBuilder.VintageClass,
            PointsSystem = new CatalogSeason
            {
                RacePoints = [new(9), new(6), new(4), new(3), new(2), new(1)],
                Constructors = new CatalogConstructors { BestCarOnly = false },
            },
            Rounds =
            [
                TestPackBuilder.Round(1, "1967-01-02"),
                TestPackBuilder.Round(2, "1967-05-07"),
            ],
        },
        Teams =
        [
            Team("team.apex", "Apex Racing", tier: 5),
            Team("team.mid", "Mid Racing", tier: 3),
        ],
        Drivers =
        [
            Driver("driver.a", born: 1938, race: 0.85, quali: 0.85),
            Driver("driver.b", born: 1941, race: 0.78, quali: 0.77),
            Driver("driver.p", born: 1940, race: 0.72, quali: 0.72), // the player's seat
            Driver("driver.d", born: 1943, race: 0.66, quali: 0.67),
        ],
        Entries =
        [
            Entry("team.apex", "driver.a", "1", "Apex #1"),
            Entry("team.apex", "driver.b", "2", "Apex #2"),
            Entry("team.mid", "driver.p", "4", PlayerLivery),
            Entry("team.mid", "driver.d", "5", "Mid #5"),
        ],
    };

    /// <summary>A 1969-style target pack whose team list is chosen per test: the accepted
    /// team's lineage carries (or is deliberately missing for the validation-error test).
    /// driver.a carries across; driver.next is the seat the player takes at
    /// <paramref name="playerTeamId"/> when it is present.</summary>
    private static SeasonPack ToPack(int year, string packId, params string[] teamIds)
    {
        var teams = new List<PackTeam>();
        var drivers = new List<PackDriver>
        {
            Driver("driver.a", born: 1938, race: 0.83, quali: 0.84),
            Driver("driver.next", born: 1946, race: 0.70, quali: 0.71),
            Driver("driver.new_era", born: 1945, race: 0.68, quali: 0.68),
        };
        var entries = new List<PackEntry>();

        for (int i = 0; i < teamIds.Length; i++)
        {
            teams.Add(Team(teamIds[i], $"{teamIds[i]} Mk2", tier: 3));
            // First team gets driver.next ("Next69 #4" — the seat the player takes when this
            // is the accepted team), the rest spread over the remaining carried/new drivers.
            string driverId = i == 0 ? "driver.next" : i == 1 ? "driver.a" : "driver.new_era";
            string livery = i == 0 ? Season2Livery : $"Rest69 #{i + 10}";
            entries.Add(Entry(teamIds[i], driverId, (i + 1).ToString(), livery));
        }

        return new SeasonPack
        {
            Manifest = new PackManifest
            {
                PackId = packId,
                Name = $"Era Test {year}",
                Version = "1.0.0",
                FormatVersion = 1,
            },
            Season = new SeasonDefinition
            {
                Year = year,
                SeriesName = "Era Test Series Mk2",
                Ams2Class = TestPackBuilder.VintageClass,
                PointsSystem = new CatalogSeason
                {
                    RacePoints = [new(9), new(6), new(4), new(3), new(2), new(1)],
                    Constructors = new CatalogConstructors { BestCarOnly = false },
                },
                Rounds =
                [
                    TestPackBuilder.Round(1, $"{year}-01-02"),
                    TestPackBuilder.Round(2, $"{year}-05-07"),
                ],
            },
            Teams = teams,
            Drivers = drivers,
            Entries = entries,
        };
    }

    private static PackTeam Team(string id, string name, int tier) => new()
    {
        Id = id,
        Name = name,
        CarVehicleIds = [TestPackBuilder.VintageCar],
        Reliability = 0.9,
        BudgetTier = tier,
    };

    private static PackDriver Driver(string id, int born, double race, double quali) =>
        TestPackBuilder.Driver(id) with
        {
            Born = born,
            Ratings = TestPackBuilder.Driver(id).Ratings with
            {
                RaceSkill = race,
                QualifyingSkill = quali,
            },
        };

    private static PackEntry Entry(string teamId, string driverId, string number, string livery) => new()
    {
        TeamId = teamId,
        DriverId = driverId,
        Number = number,
        Rounds = "1-2",
        Ams2LiveryName = livery,
    };

    // ---------- helpers ----------

    /// <summary>Creates the career on the 1967 pack (written into the packs root) and plays
    /// every round through the REAL Apply path (grid order = finishing order).</summary>
    private CareerSessionService CreateAndPlaySeason(
        Companion.Core.Character.CharacterProfile? character = null)
    {
        var fromPack = FromPack1967();
        string fromDirectory = Path.Combine(PacksRoot, fromPack.Manifest.PackId);
        TestPackBuilder.Write(fromPack, fromDirectory);

        var session = CareerSessionService.CreateCareer(new CareerCreationRequest
        {
            PackDirectory = fromDirectory,
            CareerFilePath = CareerPath,
            CareerName = "Era Career",
            MasterSeed = 20260703,
            PlayerLiveryName = PlayerLivery,
            Character = character,
        }, Environment());

        while (!session.Summary.SeasonComplete)
        {
            var grid = session.CurrentGrid();
            Assert.NotEmpty(grid);
            session.Apply(new ResultDraft
            {
                Classified = grid.Select(s => s.DriverId).ToList(),
                DidNotFinish = new Dictionary<string, string>(),
                Disqualified = [],
            });
        }
        return session;
    }

    // ---------- character development across the season boundary (depth 4) ----------

    [Fact]
    public void SpendingBetweenSeasons_EvolvesTheCharacterIntoTheNewSeason()
    {
        var character = new Companion.Core.Character.CharacterProfile
        {
            Name = "Dev Driver",
            Stats = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["pace"] = 0.50, ["oneLap"] = 0.50, ["craft"] = 0.50, ["racecraft"] = 0.50,
                ["adaptability"] = 0.50, ["marketability"] = 0.50, ["durability"] = 0.50,
            },
            PerkIds = ["sunday_driver"],
            CreationPerkIds = ["sunday_driver"],
            ProgressionVersion = 1,
            CpUnspent = 3,
        };

        string acceptedTeam;
        using (var session = CreateAndPlaySeason(character))
        {
            var review = session.SeasonReview(); // runs the season-end XP fold before development
            Assert.NotNull(review);
            // Spend a development point on pace + bank a new perk before signing.
            int before = session.AvailableCharacterCp();
            Assert.True(before >= 2);
            session.SpendCharacterPoint(Companion.Core.Character.CharacterSpend.Stat("pace", 1));
            session.SpendCharacterPoint(Companion.Core.Character.CharacterSpend.Perk("rain_man", 1));
            Assert.Equal(before - 2, session.AvailableCharacterCp()); // pending spends reduce the pool now

            acceptedTeam = review!.Offers[0].TeamId;
            // A dedicated NEXT-YEAR (1968) pack → a real era changeover carries the spends across.
            TestPackBuilder.Write(
                ToPack(1968, "era-test-1968", acceptedTeam, "team.fresh"),
                Path.Combine(PacksRoot, "era-test-1968"));

            var vm = new SeasonReviewViewModel(session);
            vm.AcceptOfferCommand.Execute(vm.Offers.First(o => o.TeamId == acceptedTeam));
            vm.SignAndContinueCommand.Execute(null);
            Assert.Null(vm.TransitionError);
        }

        // Reopen into season 2: the spends applied at the transition — pace raised a step and the
        // banked perk is now on the driver.
        using (var reopened = CareerSessionService.OpenCareer(CareerPath, Environment()))
        {
            var dossier = reopened.CharacterDossier();
            Assert.NotNull(dossier);
            Assert.Equal("Dev Driver", dossier!.Name);
            Assert.Equal(0.55, dossier.Stats.First(s => s.Id == "pace").Value, 6); // 0.50 + one step
            Assert.Contains(dossier.Perks, p => p.Id == "rain_man");
            var paceNode = reopened.SkillTree()!.Branches.SelectMany(branch => branch.Nodes)
                .Single(node => node.Id == "raise_pace_1");
            Assert.Equal(Companion.Core.Character.SkillNodeState.Owned, paceNode.State);
        }

        using var replayDb = CareerDatabase.Open(CareerPath);
        var replayRules = Environment().Rules;
        var report = ReplayService.Resimulate(replayDb, unchecked((ulong)20260703), new ReplaySimInputs
        {
            AgingCurves = replayRules.AgingCurves,
            Archetypes = replayRules.Archetypes,
            Headlines = replayRules.Headlines,
            PlayerDriverId = "driver.p",
            PlayerAge = 1967 - 1940,
            CharacterRules = replayRules.Character,
        });
        Assert.True(report.Identical, report.FirstDivergence?.Reason);
    }

    private static Companion.Core.Character.CharacterProfile DevCharacter() => new()
    {
        Name = "Dev Driver",
        Stats = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["pace"] = 0.50, ["oneLap"] = 0.50, ["craft"] = 0.50, ["racecraft"] = 0.50,
            ["adaptability"] = 0.50, ["marketability"] = 0.50, ["durability"] = 0.50,
        },
        PerkIds = ["sunday_driver"],
        CreationPerkIds = ["sunday_driver"],
        ProgressionVersion = 1,
        CpUnspent = 3,
    };

    /// <summary>Starts one year before the 1960s peak-age boundary. A 1968 same-pack carryover
    /// must advance the live fold to age 28, matching replay's season-row calculation, so
    /// wonderkid switches from its young XP branch to its veteran branch on both paths.</summary>
    private static Companion.Core.Character.CharacterProfile CarryoverAgeWindowCharacter() =>
        DevCharacter() with
        {
            Age = 27,
            PerkIds = ["wonderkid"],
            CreationPerkIds = ["wonderkid"],
        };

    [Fact]
    public void Spend_DerivesTheCostFromTheRules_IgnoringACraftedCheapCost()
    {
        using var session = CreateAndPlaySeason(DevCharacter());
        int before = session.AvailableCharacterCp();

        // A crafted spend claims rain_man is free (real cost 1). The service must ignore the claim,
        // charge the real cost, and journal the real cost — otherwise the exploit replays byte-for-byte.
        session.SpendCharacterPoint(Companion.Core.Character.CharacterSpend.Perk("rain_man", 0));
        Assert.Equal(before - 1, session.AvailableCharacterCp());
    }

    [Fact]
    public void Spend_RejectsADrawbackPerk_AndDoesNotChargeOrMintPoints()
    {
        using var session = CreateAndPlaySeason(DevCharacter());
        int before = session.AvailableCharacterCp();

        // glass_cannon costs -2: taking it must not refund/mint spendable points mid-career.
        Assert.Throws<InvalidOperationException>(() =>
            session.SpendCharacterPoint(Companion.Core.Character.CharacterSpend.Perk("glass_cannon", -2)));
        Assert.Equal(before, session.AvailableCharacterCp());
    }

    [Fact]
    public void Spend_RejectsAnUnknownStatOrPerk()
    {
        using var session = CreateAndPlaySeason(DevCharacter());
        int before = session.AvailableCharacterCp();

        Assert.Throws<InvalidOperationException>(() =>
            session.SpendCharacterPoint(Companion.Core.Character.CharacterSpend.Stat("notAStat", 1)));
        Assert.Throws<InvalidOperationException>(() =>
            session.SpendCharacterPoint(Companion.Core.Character.CharacterSpend.Perk("notAPerk", 1)));
        Assert.Equal(before, session.AvailableCharacterCp());
    }

    [Fact]
    public void Spend_SoftCapPerk_CapsInCareerStatRaisesLower()
    {
        var softCapped = new Companion.Core.Character.CharacterProfile
        {
            Name = "Iron Man",
            Stats = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["pace"] = 0.85, ["oneLap"] = 0.50, ["craft"] = 0.50, ["racecraft"] = 0.50,
                ["adaptability"] = 0.50, ["marketability"] = 0.50, ["durability"] = 0.55,
            },
            PerkIds = ["iron_constitution"], // statPoints softCap −0.10 → in-career ceiling 0.89
            CpUnspent = 3,
        };
        using var session = CreateAndPlaySeason(softCapped);
        Assert.True(session.AvailableCharacterCp() >= 1);

        // pace is at 0.85; a step (0.90) would exceed the perk's 0.89 ceiling → rejected.
        Assert.Throws<InvalidOperationException>(() =>
            session.SpendCharacterPoint(Companion.Core.Character.CharacterSpend.Stat("pace", 1)));
        // a low stat is still raisable (well under the ceiling).
        session.SpendCharacterPoint(Companion.Core.Character.CharacterSpend.Stat("craft", 1));
    }

    [Fact]
    public void PurchasablePerks_AreAffordableUnownedPositiveCost_CheapestFirst()
    {
        using var session = CreateAndPlaySeason(DevCharacter());
        int available = session.AvailableCharacterCp();
        Assert.True(available >= 2);

        var offered = ((ICareerSession)session).PurchasablePerks();
        Assert.NotEmpty(offered);
        Assert.All(offered, p => Assert.InRange(p.Cost, 1, available));   // affordable + positive
        Assert.DoesNotContain(offered, p => p.Id == "sunday_driver");     // already owned
        Assert.DoesNotContain(offered, p => p.Id == "glass_cannon");      // drawback (<=0 cost) perk
        var costs = offered.Select(p => p.Cost).ToList();
        Assert.Equal(costs.OrderBy(c => c).ToList(), costs);              // cheapest first

        // Buy one: it drops off the offer list, and a now-unaffordable perk is filtered out.
        string bought = offered[0].Id;
        session.SpendCharacterPoint(Companion.Core.Character.CharacterSpend.Perk(bought, offered[0].Cost));
        var after = ((ICareerSession)session).PurchasablePerks();
        Assert.DoesNotContain(after, p => p.Id == bought);
        int remaining = session.AvailableCharacterCp();
        Assert.All(after, p => Assert.True(p.Cost <= remaining));
    }

    [Fact]
    public void CharacterAge_IsTheDriversOwn_ShownInTheDossier_AndDrivesTheSim()
    {
        // driver.p (the seat) is really 27 in 1967 (Born 1940). The character is a 41-year-old
        // veteran — a REAL age, unlike the historical driver's — so if the age is used at all it
        // MUST be 41, never the borrowed 27.
        var veteran = new Companion.Core.Character.CharacterProfile
        {
            Name = "Old Hand",
            Age = 41,
            Stats = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["pace"] = 0.50, ["oneLap"] = 0.50, ["craft"] = 0.50, ["racecraft"] = 0.50,
                ["adaptability"] = 0.50, ["marketability"] = 0.50, ["durability"] = 0.50,
            },
            PerkIds = [],
            CpUnspent = 0,
        };

        using (var session = CreateAndPlaySeason(veteran))
        {
            var dossier = ((ICareerSession)session).CharacterDossier();
            Assert.NotNull(dossier);
            Assert.Equal(41, dossier!.Age); // the driver's OWN age, shown — not the seat's 27
        }

        // Reopen: the age persists and still reads as the driver's own.
        using (var reopened = CareerSessionService.OpenCareer(CareerPath, Environment()))
        {
            Assert.Equal(41, reopened.CharacterDossier()!.Age);
        }

        // It is the age the SIM ran on, not the historical 27: re-simulating with 41 is byte-identical;
        // with the seat driver's 27 it diverges — proof the character's age really drives the career.
        using var db = CareerDatabase.Open(CareerPath);
        var rules = Environment().Rules;
        ReplaySimInputs Inputs(int age) => new()
        {
            AgingCurves = rules.AgingCurves,
            Archetypes = rules.Archetypes,
            Headlines = rules.Headlines,
            PlayerDriverId = "driver.p",
            PlayerAge = age,
            CharacterRules = rules.Character,
        };
        Assert.True(ReplayService.Resimulate(db, unchecked((ulong)20260703), Inputs(41)).Identical,
            "the career must re-simulate on the character's real age (41)");
        Assert.False(ReplayService.Resimulate(db, unchecked((ulong)20260703), Inputs(27)).Identical,
            "using the historical driver's age (27) must diverge — the character age is what counts");
    }

    [Fact]
    public void SeatChangeAcrossTransition_ResimulatesByteIdentically()
    {
        // The player is driver.p (livery "Mid #4") in 1967 and, on signing the accepted team, takes
        // the driver.next seat (livery "Next69 #4") in the next-year 1968 pack — the seat driver id
        // CHANGES across the changeover. The multi-pack Resimulate must find the player per season
        // from their livery (fold + season end), not a single career-global id, or it falsely diverges.
        string acceptedTeam;
        using (var session = CreateAndPlaySeason())
        {
            var review = session.SeasonReview();
            Assert.NotNull(review);
            acceptedTeam = review!.Offers[0].TeamId;
            TestPackBuilder.Write(
                ToPack(1968, "era-test-1968", acceptedTeam, "team.fresh"),
                Path.Combine(PacksRoot, "era-test-1968"));

            var vm = new SeasonReviewViewModel(session);
            vm.AcceptOfferCommand.Execute(vm.Offers.First(o => o.TeamId == acceptedTeam));
            vm.SignAndContinueCommand.Execute(null);
            Assert.Null(vm.TransitionError);
        }

        // Play 1968 to completion.
        using (var s2 = CareerSessionService.OpenCareer(CareerPath, Environment()))
        {
            Assert.Equal(1968, s2.Summary.SeasonYear);
            while (!s2.Summary.SeasonComplete)
            {
                var grid = s2.CurrentGrid();
                Assert.NotEmpty(grid);
                // The player's new seat really is driver.next — the seat change we are exercising.
                Assert.Contains(grid, seat => seat.IsPlayer && seat.DriverId == "driver.next");
                s2.Apply(new ResultDraft
                {
                    Classified = grid.Select(seat => seat.DriverId).ToList(),
                    DidNotFinish = new Dictionary<string, string>(),
                    Disqualified = [],
                });
            }
        }

        // Re-simulate the whole two-pack career from raw results. PlayerDriverId here is the season-1
        // id and now only a fallback — the fold and season end re-resolve the seat per season, so the
        // 1969 rows reproduce byte-for-byte instead of dropping every player.* row.
        using var db = CareerDatabase.Open(CareerPath);
        var rules = Environment().Rules;
        var report = ReplayService.Resimulate(db, unchecked((ulong)20260703), new ReplaySimInputs
        {
            AgingCurves = rules.AgingCurves,
            Archetypes = rules.Archetypes,
            Headlines = rules.Headlines,
            PlayerDriverId = "driver.p",
            PlayerAge = 1967 - 1940, // driver.p Born 1940, first season 1967
            CharacterRules = rules.Character,
        });

        Assert.True(report.Identical,
            $"diverged: {report.FirstDivergence?.Reason} season={report.FirstDivergence?.SeasonId} " +
            $"stored={report.FirstDivergence?.StoredDeltaJson} regen={report.FirstDivergence?.RegeneratedDeltaJson}");
        Assert.Null(report.FirstDivergence);
    }

    // ---------- the tests ----------

    [Fact]
    public void SignAndContinue_ChangesOverToANextYearPack_AndReopensIntoIt()
    {
        string acceptedTeam;
        using (var session = CreateAndPlaySeason())
        {
            var review = session.SeasonReview();
            Assert.NotNull(review);
            Assert.NotEmpty(review.Offers);
            acceptedTeam = review.Offers[0].TeamId;

            // With no dedicated next-year pack yet, the career would CARRY OVER on the same car
            // (it never dead-ends) — into 1968.
            var carry = ((ICareerSession)session).NextSeason();
            Assert.NotNull(carry);
            Assert.True(carry.IsCarryover);
            Assert.Equal(1968, carry.SeasonYear);

            // Install a real 1968 pack: next year now has its own car → a real CHANGEOVER. A later
            // 1974 pack is ignored — the career advances ONE year at a time to the next-year pack.
            TestPackBuilder.Write(
                ToPack(1968, "era-test-1968", acceptedTeam, "team.fresh"),
                Path.Combine(PacksRoot, "era-test-1968"));
            TestPackBuilder.Write(
                ToPack(1974, "era-test-1974", acceptedTeam, "team.fresh"),
                Path.Combine(PacksRoot, "era-test-1974"));

            var next = ((ICareerSession)session).NextSeason();
            Assert.NotNull(next);
            Assert.False(next.IsCarryover);
            Assert.Equal("era-test-1968", next.PackId);
            Assert.Equal(1968, next.SeasonYear);
            Assert.Empty(next.BridgedYears);

            // The review screen drives the whole flow: accept, then sign.
            var vm = new SeasonReviewViewModel(session);
            Assert.True(vm.HasNextSeason);
            Assert.Equal("Sign & start 1968", vm.SignButtonText);
            Assert.Null(vm.BridgeNote); // year-by-year — nothing is bridged
            Assert.False(vm.SignAndContinueCommand.CanExecute(null)); // no acceptance yet

            vm.AcceptOfferCommand.Execute(vm.Offers.First(o => o.TeamId == acceptedTeam));
            Assert.True(vm.SignAndContinueCommand.CanExecute(null));

            bool signed = false;
            vm.SeasonSigned += (_, _) => signed = true;
            vm.SignAndContinueCommand.Execute(null);
            Assert.True(signed);
            Assert.Null(vm.TransitionError);
        }

        // The career file now has two seasons and the era.transition journal rows.
        using (var db = CareerDatabase.Open(CareerPath))
        {
            var seasons = CareerStore.ReadSeasons(db);
            Assert.Equal(2, seasons.Count);
            Assert.Equal(1967, seasons[0].Year);
            Assert.Equal(SeasonStatus.Complete, seasons[0].Status);
            Assert.Equal(1968, seasons[1].Year);
            Assert.Equal(SeasonStatus.Active, seasons[1].Status);

            var journal = JournalStore.ReadSeason(db, seasons[1].Id);
            Assert.Contains(journal, r => r.Phase == DataJournalPhases.EraTransition);
            // A consecutive-year changeover bridges nothing.
            Assert.DoesNotContain(journal, r => r.Phase == JournalPhases.EraBridge);
        }

        // Reopen = the MRU/continue path: the career opens into the LATEST season, at its
        // round 1 briefing, with the player in the seat the changeover resolved.
        using var reopened = CareerSessionService.OpenCareer(CareerPath, Environment());
        var summary = reopened.Summary;
        Assert.Equal(1968, summary.SeasonYear);
        Assert.Equal("Era Test Series Mk2", summary.SeriesName);
        Assert.False(summary.SeasonComplete);
        Assert.Equal(1, summary.CurrentRound);
        Assert.Equal(2, summary.RoundCount);
        Assert.Equal(Season2Livery, summary.PlayerLiveryName);
        Assert.Equal("driver.next", summary.PlayerDriverId);

        var briefing = reopened.CurrentBriefing();
        Assert.NotNull(briefing);
        Assert.Equal(1, briefing.Round.Round);

        // Home over the reopened session lands on the new season's round-1 briefing and
        // headlines the year.
        using var home = new HomeViewModel(reopened);
        Assert.True(home.IsBriefingState);
        Assert.Equal("1968", home.SeasonYearText);
        Assert.Equal("Round 1 of 2", home.RoundText);

        // The transitioned season plays through the SAME fold as season 1.
        var grid = reopened.CurrentGrid();
        Assert.Contains(grid, s => s.IsPlayer && s.DriverId == "driver.next");
        reopened.Apply(new ResultDraft
        {
            Classified = grid.Select(s => s.DriverId).ToList(),
            DidNotFinish = new Dictionary<string, string>(),
            Disqualified = [],
        });
        Assert.NotNull(reopened.Summary.Reputation);
        Assert.Equal(2, reopened.Summary.CurrentRound);
    }

    [Fact]
    public void SeasonScopedJournalFor_OverAMultiSeasonCareer_WalksAFinishedEarlierSeason()
    {
        string acceptedTeam;
        using (var session = CreateAndPlaySeason()) // plays the whole 1967 season
        {
            var review = session.SeasonReview();
            Assert.NotNull(review);
            acceptedTeam = review.Offers[0].TeamId;

            TestPackBuilder.Write(
                ToPack(1968, "era-test-1968", acceptedTeam, "team.fresh"),
                Path.Combine(PacksRoot, "era-test-1968"));

            var vm = new SeasonReviewViewModel(session);
            vm.AcceptOfferCommand.Execute(vm.Offers.First(o => o.TeamId == acceptedTeam));
            vm.SignAndContinueCommand.Execute(null);
            Assert.Null(vm.TransitionError);
        }

        // Reopen: the session now points at the CURRENT (1968) season — 1967 is a finished
        // earlier season living in the same career file, keyed by its own season id.
        using var reopened = CareerSessionService.OpenCareer(CareerPath, Environment());
        Assert.Equal(1968, reopened.Summary.SeasonYear);
        ICareerSession seam = reopened;

        // The season-scoped seam reaches the FINISHED 1967 season's player journal (the gap the
        // History-card follow-up closes) — the current-season walk cannot, because 1967 is not
        // this session's season.
        Assert.True(seam.JournalFor("player").IsEmpty); // 1969 has no applied round yet
        var finished = seam.JournalForSeason("player", 1967);
        Assert.False(finished.IsEmpty);
        Assert.Equal("player", finished.Entity);
        Assert.Null(finished.Round);
        // The 1967 title carries that season's year (resolved from that season's pinned pack).
        Assert.Contains("1967", finished.Title);
        // The finished season chained real per-round player rows (race results + folded state),
        // ordered ascending by journal seq — the deterministic walk.
        var seqs = finished.Contributions.Select(c => c.SourceSeq).ToList();
        Assert.NotEmpty(seqs);
        Assert.All(seqs, s => Assert.True(s > 0));
        Assert.Equal(seqs.OrderBy(s => s).ToList(), seqs);
        Assert.Contains(finished.Contributions, c => c.Label == "Expected finish");

        // Narrowing to a single round of the finished season is a strict subset of the season walk.
        var round1 = seam.JournalForSeason("player", 1967, 1);
        Assert.False(round1.IsEmpty);
        Assert.True(round1.Contributions.Count < finished.Contributions.Count);
        Assert.Contains("Round 1", round1.Title);

        // Season-scoped for the CURRENT year equals the current-season walk (byte-identical once a
        // round is applied) — here both are empty because 1968 has no applied round.
        Assert.True(seam.JournalForSeason("player", 1968).IsEmpty);

        // A year with no season row in the career is a graceful no-op, never a throw.
        Assert.True(seam.JournalForSeason("player", 1955).IsEmpty);

        // Deterministic: a repeat call re-derives the identical chain (pure over the stored journal).
        var again = seam.JournalForSeason("player", 1967);
        Assert.Equal(
            finished.Contributions.Select(c => (c.Label, c.Value, c.SourceSeq)),
            again.Contributions.Select(c => (c.Label, c.Value, c.SourceSeq)));
    }

    [Fact]
    public void Sign_WhenTheAcceptedTeamIsMissingFromTheNextPack_SurfacesThePlansValidationError()
    {
        using (var session = CreateAndPlaySeason())
        {
            var review = session.SeasonReview();
            Assert.NotNull(review);
            string acceptedTeam = review.Offers[0].TeamId;
            session.AcceptOffer(acceptedTeam);

            // The next-year (1968) pack deliberately lacks the accepted team's lineage.
            TestPackBuilder.Write(
                ToPack(1968, "era-test-1968", "team.somebody_else"),
                Path.Combine(PacksRoot, "era-test-1968"));

            var vm = new SeasonReviewViewModel(session);
            Assert.True(vm.HasNextSeason);
            Assert.True(vm.SignAndContinueCommand.CanExecute(null));

            bool signed = false;
            vm.SeasonSigned += (_, _) => signed = true;
            vm.SignAndContinueCommand.Execute(null);

            // The plan's validation error (EraTransition) reaches the screen; no navigation.
            Assert.False(signed);
            Assert.NotNull(vm.TransitionError);
            Assert.Contains($"team '{acceptedTeam}'", vm.TransitionError);
            Assert.Contains("does not exist in", vm.TransitionError);
        }

        // Nothing was started: the career still has exactly its 1967 season.
        using var db = CareerDatabase.Open(CareerPath);
        var seasons = CareerStore.ReadSeasons(db);
        Assert.Single(seasons);
        Assert.Equal(1967, seasons[0].Year);
    }

    [Fact]
    public void NoNextYearPack_CarriesOverOnTheSameCar_AndResimulatesByteIdentical()
    {
        using (var session = CreateAndPlaySeason(CarryoverAgeWindowCharacter()))
        {
            var review = session.SeasonReview();
            Assert.NotNull(review);
            session.SpendCharacterPoint(Companion.Core.Character.CharacterSpend.Stat("pace", 1));
            session.AcceptOffer(review!.Offers[0].TeamId);

            // The packs root only has the 1967 car, so the career carries it into 1968 — a
            // carryover, never a dead-end.
            var next = ((ICareerSession)session).NextSeason();
            Assert.NotNull(next);
            Assert.True(next.IsCarryover);
            Assert.Equal(1968, next.SeasonYear);
            Assert.Equal(session.Pack.Manifest.PackId, next.PackId); // the same car

            var vm = new SeasonReviewViewModel(session);
            Assert.True(vm.HasNextSeason);
            Assert.True(vm.OfferAccepted);
            Assert.True(vm.CanSign);
            Assert.Equal("Sign & start 1968", vm.SignButtonText);
            Assert.Null(vm.BridgeNote);
            Assert.Contains("same car", vm.EraTransitionText, StringComparison.OrdinalIgnoreCase);

            vm.SignAndContinueCommand.Execute(null);
            Assert.Null(vm.TransitionError);
        }

        // The carryover is a second season on the SAME pinned pack, one year later, and writes no
        // era-transition journal rows (a rollover has none).
        using (var db = CareerDatabase.Open(CareerPath))
        {
            var seasons = CareerStore.ReadSeasons(db);
            Assert.Equal(2, seasons.Count);
            Assert.Equal(1967, seasons[0].Year);
            Assert.Equal(1968, seasons[1].Year);
            Assert.Equal(seasons[0].PackId, seasons[1].PackId);
            Assert.DoesNotContain(
                JournalStore.ReadSeason(db, seasons[1].Id),
                r => r.Phase == DataJournalPhases.EraTransition);
        }

        // Reopen into 1968 on the same car and play it out.
        using (var s2 = CareerSessionService.OpenCareer(CareerPath, Environment()))
        {
            Assert.Equal(1968, s2.Summary.SeasonYear);
            Assert.Equal("Era Test Series", s2.Summary.SeriesName); // the SAME 1967 pack's series
            Assert.Equal(
                Companion.Core.Character.SkillNodeState.Owned,
                s2.SkillTree()!.Branches.SelectMany(branch => branch.Nodes)
                    .Single(node => node.Id == "raise_pace_1").State);
            while (!s2.Summary.SeasonComplete)
            {
                var grid = s2.CurrentGrid();
                Assert.NotEmpty(grid);
                s2.Apply(new ResultDraft
                {
                    Classified = grid.Select(seat => seat.DriverId).ToList(),
                    DidNotFinish = new Dictionary<string, string>(),
                    Disqualified = [],
                });
            }
        }

        // The whole carried-over career re-simulates byte-identically (same-pack rollover path).
        using var replayDb = CareerDatabase.Open(CareerPath);
        var rules = Environment().Rules;
        var report = ReplayService.Resimulate(replayDb, unchecked((ulong)20260703), new ReplaySimInputs
        {
            AgingCurves = rules.AgingCurves,
            Archetypes = rules.Archetypes,
            Headlines = rules.Headlines,
            PlayerDriverId = "driver.p",
            PlayerAge = 1967 - 1940, // driver.p Born 1940, first season 1967
            CharacterRules = rules.Character,
        });
        Assert.True(report.Identical,
            $"diverged: {report.FirstDivergence?.Reason} season={report.FirstDivergence?.SeasonId} " +
            $"stored={report.FirstDivergence?.StoredDeltaJson} regen={report.FirstDivergence?.RegeneratedDeltaJson}");
    }
}
