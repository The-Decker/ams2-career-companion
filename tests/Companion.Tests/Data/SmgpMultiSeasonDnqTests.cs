using Companion.Core.Career;
using Companion.Core.Character;
using Companion.Core.Packs;
using Companion.Core.Scoring;
using Companion.Core.Smgp;
using Companion.Data;
using Companion.Tests.ViewModels;
using Companion.ViewModels.Review;
using Companion.ViewModels.Services;

namespace Companion.Tests.Data;

/// <summary>
/// The PER-SEASON DNQ RE-ROLL (17-season campaign): a career gated on <see cref="SmgpState.PerSeasonDnq"/>
/// re-rolls its backmarker DNQ field every season 2+ (each season a fresh seeded field). The starter set
/// is a FOLD INPUT (grid membership → seat-strength → the byte-compared player rows), so the SAME ordinal-
/// keyed transform must be applied on BOTH the live-fold pack (CareerSessionService ctor) and the replay
/// pack (ReplayService.ResimulateCore). These tests drive a real two-season carryover DNQ career over the
/// actual machinery and assert: the flag is seeded + carried; season 2's runtime field is exactly the
/// ordinal-2 re-roll; and the whole multi-season career RE-SIMULATES BYTE-IDENTICALLY (the locked invariant).
/// </summary>
public sealed class SmgpMultiSeasonDnqTests : IDisposable
{
    private const string SeatC = "Stock Livery #3"; // team.c LEVEL C, the player's start
    private const long Seed = 20260712;
    private const string PlayerId = Companion.Core.Grid.RoundGridResolver.SyntheticPlayerDriverId;

    private readonly string _root = Directory.CreateTempSubdirectory("companion-smgp-mdnq-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    [Fact]
    public void PerSeasonDnqFlag_IsSeededAtCreation_AndCarriedToSeasonTwo()
    {
        PlaySeasonOneAndSign();

        using var db = CareerDatabase.Open(CareerPath);
        var seasons = CareerStore.ReadSeasons(db);
        Assert.Equal(2, seasons.Count);
        var s1Start = StateStore.ReadPlayerState(db, seasons[0].Id, StateStore.StageStart)!.Smgp!;
        var s2Start = StateStore.ReadPlayerState(db, seasons[1].Id, StateStore.StageStart)!.Smgp!;
        Assert.True(s1Start.PerSeasonDnq); // seeded for a DNQ pack at creation
        Assert.True(s2Start.PerSeasonDnq); // carried across the rollover
        Assert.True(s1Start.PerSeasonVariety);
        Assert.True(s2Start.PerSeasonVariety);
        Assert.True(s1Start.StandingsReshuffle);
        Assert.True(s2Start.StandingsReshuffle);
    }

    [Fact]
    public void SeasonTwo_RuntimePack_IsTheOrdinalTwoReRoll_AndReplaysByteIdentical()
    {
        SeasonPack pinnedSeasonOne = PlaySeasonOneAndSign();

        using (var s2 = CareerSessionService.OpenCareer(CareerPath, Environment()))
        {
            // The runtime Pack the live fold resolves the grid from IS the ordinal-2 re-roll of the pinned
            // pack (variety only shuffles venues, keeping the grid with the round number, so the DNQ field
            // equals ForSeason regardless of variety). This is exactly what ResimulateCore re-applies.
            var expected = SmgpGridReshuffle.ForNextSeason(
                pinnedSeasonOne, SeasonOneFinal(pinnedSeasonOne), SeatC);
            expected = SmgpDnqField.ForSeason(expected, 2, unchecked((ulong)Seed));
            Assert.Equal(expected.Entries, s2.Pack.Entries);

            // Runtime entries now carry reshuffled drivers, but the starting-grid car image must
            // remain attached to its authored physical livery. The display lookup is captured from
            // the pinned pack before the reshuffle and never participates in the fold.
            var authoredDriverByLivery = pinnedSeasonOne.Entries.ToDictionary(
                entry => entry.Ams2LiveryName,
                entry => entry.DriverId,
                StringComparer.Ordinal);
            var movedSeat = Assert.Single(
                s2.Pack.Entries.Where(entry =>
                    !string.Equals(
                        authoredDriverByLivery[entry.Ams2LiveryName],
                        entry.DriverId,
                        StringComparison.Ordinal)).Take(1));
            string fixedCarArtKey = Assert.IsType<string>(
                s2.GridCarArtKeyForLivery(movedSeat.Ams2LiveryName));
            Assert.Equal(authoredDriverByLivery[movedSeat.Ams2LiveryName], fixedCarArtKey);
            Assert.NotEqual(movedSeat.DriverId, fixedCarArtKey);

            // The Paddock tells the same story: a driver the winter reshuffle moved shows the car
            // they actually race this season (the physical livery's art), never their season-1
            // mount, while the card's team follows the new seat.
            var movedCard = Assert.Single(
                s2.SmgpPaddock()!.Drivers,
                card => string.Equals(card.DriverId, movedSeat.DriverId, StringComparison.Ordinal));
            Assert.Equal(fixedCarArtKey, movedCard.CarKey);
            Assert.Equal(movedSeat.TeamId, movedCard.TeamId);

            foreach (var round in s2.Pack.Season.Rounds)
            {
                var expectedStarters = expected.Season.Rounds.Single(r => r.Round == round.Round)
                    .Grid!.StarterDriverIds.ToHashSet(StringComparer.Ordinal);
                Assert.Equal(expectedStarters, round.Grid!.StarterDriverIds.ToHashSet(StringComparer.Ordinal));
            }

            // Play a round of season 2 so replay has a season-2 fold to re-derive against the re-rolled grid.
            ApplyRound(s2);
        }

        AssertResimulatesByteIdentically();
    }

    [Fact]
    public void MilestoneDispatches_NameTheBeatTimeTeam_NotTheCurrentTeam()
    {
        string packDirectory = Path.Combine(PacksRoot, "smgp-dnq-ladder");
        TestPackBuilder.Write(DnqLadderPack(), packDirectory);

        using (var session = CareerSessionService.CreateCareer(new CareerCreationRequest
        {
            PackDirectory = packDirectory,
            CareerFilePath = CareerPath,
            CareerName = "Dispatch Teams",
            MasterSeed = Seed,
            PlayerLiveryName = SeatC,
            SmgpMode = true,
        }, Environment()))
        {
            // The two-wins promotion this scenario needs is the LEGACY ladder: new careers run
            // the best-of-7 series (four wins), so flip the gate off like a pre-series save.
            using (var db = CareerDatabase.Open(CareerPath))
            {
                long seasonId = CareerStore.ReadSeasons(db).Single().Id;
                var start = StateStore.ReadPlayerState(db, seasonId, StateStore.StageStart)!;
                StateStore.UpsertPlayerState(db, seasonId, StateStore.StageStart,
                    start with { Smgp = start.Smgp! with { SeriesLadder = false } });
            }

            // Two wins over driver.a, then accept the deferred offer on the promotion screen:
            // the player leaves team.c for team.a (SeatA) mid-season, the clean two-wins swap.
            ApplyPlayerFirst(session, new SmgpRivalCall { RivalDriverId = "driver.a" });
            ApplyPlayerFirst(session, new SmgpRivalCall { RivalDriverId = "driver.a" });
            Assert.NotNull(session.CurrentSmgpPendingOffer());
            session.ResolveSmgpOffer(accept: true);
            Assert.Equal("team.a", session.CurrentSmgpTeamId());

            while (!session.Summary.SeasonComplete)
                ApplyRound(session);
            var review = session.SeasonReview();
            Assert.NotNull(review);
            session.AcceptOffer(review!.Offers[0].TeamId);
            var vm = new SeasonReviewViewModel(session);
            vm.SignAndContinueCommand.Execute(null);
            Assert.Null(vm.TransitionError);
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        using var s2 = CareerSessionService.OpenCareer(CareerPath, Environment());
        Assert.Equal("team.a", s2.CurrentSmgpTeamId());

        // The season-1 early beats (first start, first points, the win) keep team.c, the seat
        // raced at the time; the promotion beat names the destination, team.a. None of it is
        // rewritten to the current team on a season-2 read.
        var seasonOneMilestones = s2.SmgpDispatches()
            .Where(d => d.Kind == SmgpDispatchKind.Milestone &&
                        d.WhenLabel.Contains("Season 1", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(seasonOneMilestones);
        Assert.Contains(seasonOneMilestones,
            d => d.Body.Contains("team.c", StringComparison.Ordinal) &&
                 !d.Body.Contains("team.a", StringComparison.Ordinal));
        Assert.Contains(seasonOneMilestones, d => d.Body.Contains("team.a", StringComparison.Ordinal));
    }

    [Fact]
    public void SeasonThree_WorldFeed_CreditsTheFoldTimeTeam_ForReshuffledDrivers()
    {
        SeasonPack pinned = PlaySeasonOneAndSign();
        PlaySeasonTwoAndSign();

        using var s3 = CareerSessionService.OpenCareer(CareerPath, Environment());

        // Fold-time truth for season 2: the stored envelopes' ConstructorId per driver. At least
        // one season-2 starter must sit somewhere other than the pinned pack's authored seat,
        // or the scenario proves nothing about the reshuffle.
        var foldedTeam = new Dictionary<string, string>(StringComparer.Ordinal);
        using (var db = CareerDatabase.Open(CareerPath))
        {
            long seasonTwoId = CareerStore.ReadSeasons(db)[1].Id;
            foreach (var stored in ResultStore.ReadSeasonResults(db, seasonTwoId))
            {
                var envelope = stored.ToEnvelope();
                var race = envelope.Result.Sessions
                    .FirstOrDefault(x => x.Kind == SessionKind.Race)
                    ?? envelope.Result.Sessions.FirstOrDefault();
                foreach (var entry in race?.Entries ?? [])
                    if (entry.ConstructorId is { Length: > 0 } cid)
                        foldedTeam[entry.DriverId] = cid;
            }
        }
        var authoredTeam = pinned.Entries
            .GroupBy(e => e.DriverId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().TeamId, StringComparer.Ordinal);
        Assert.Contains(foldedTeam, kv =>
            authoredTeam.TryGetValue(kv.Key, out string? authored) && authored != kv.Value);

        var seasonTwoWorld = s3.SmgpDispatches()
            .Where(d => d.SortSeason == 2 && d.TeamArtKey.Length > 0)
            .ToList();
        Assert.NotEmpty(seasonTwoWorld);
        foreach (var dispatch in seasonTwoWorld)
        {
            string subjectId = dispatch.DriverArtKey;
            Assert.True(foldedTeam.TryGetValue(subjectId, out string? expected),
                $"world dispatch subject '{subjectId}' has no folded season-2 team");
            Assert.Equal(expected, dispatch.TeamArtKey);
        }
    }

    [Fact]
    public void SeasonTwo_SkinsRows_ShowThePhysicalCar_NotTheReshuffledDriversOldCar()
    {
        SeasonPack pinned = PlaySeasonOneAndSign();

        using var s2 = CareerSessionService.OpenCareer(CareerPath, Environment());
        var authoredDriverByLivery = pinned.Entries
            .GroupBy(e => e.Ams2LiveryName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().DriverId, StringComparer.Ordinal);
        var moved = s2.Pack.Entries.FirstOrDefault(e =>
            authoredDriverByLivery.TryGetValue(e.Ams2LiveryName, out string? authored) &&
            !string.Equals(authored, e.DriverId, StringComparison.Ordinal));
        Assert.NotNull(moved); // the winter reshuffle must really move someone

        var vm = new Companion.ViewModels.Hub.SkinsViewModel(s2);
        var row = Assert.Single(vm.Cars, c =>
            string.Equals(c.LiveryName, moved!.Ams2LiveryName, StringComparison.Ordinal));
        Assert.Equal(authoredDriverByLivery[moved!.Ams2LiveryName], row.CarKey);
        Assert.NotEqual(moved.DriverId, row.CarKey);
    }

    [Fact]
    public void LegacyCareer_WithoutStandingsReshuffle_KeepsAuthoredEntries_AndReplaysByteIdentical()
    {
        string packDirectory = Path.Combine(PacksRoot, "smgp-dnq-ladder");
        TestPackBuilder.Write(DnqLadderPack(), packDirectory);

        SeasonPack pinnedSeasonOne;
        using (var created = CareerSessionService.CreateCareer(new CareerCreationRequest
        {
            PackDirectory = packDirectory,
            CareerFilePath = CareerPath,
            CareerName = "Legacy Standings Career",
            MasterSeed = Seed,
            PlayerLiveryName = SeatC,
            SmgpMode = true,
        }, Environment()))
        {
            pinnedSeasonOne = created.Pack;
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        // Recreate a pre-reshuffle career cell: false is omitted from the serialized start state,
        // so load follows the exact same path as a save created before the gate existed.
        using (var db = CareerDatabase.Open(CareerPath))
        {
            long seasonId = CareerStore.ReadSeasons(db).Single().Id;
            var start = StateStore.ReadPlayerState(db, seasonId, StateStore.StageStart)!;
            StateStore.UpsertPlayerState(db, seasonId, StateStore.StageStart,
                start with { Smgp = start.Smgp! with { StandingsReshuffle = false } });

            Assert.False(StateStore.ReadPlayerState(db, seasonId, StateStore.StageStart)!.Smgp!.StandingsReshuffle);
            Assert.DoesNotContain("standingsReshuffle", StartPlayerJson(db, seasonId), StringComparison.Ordinal);
        }

        using (var seasonOne = CareerSessionService.OpenCareer(CareerPath, Environment()))
        {
            // Reverse every available grid so enabling the reshuffle would visibly change the entries.
            while (!seasonOne.Summary.SeasonComplete)
            {
                var reversed = seasonOne.CurrentGrid().Select(seat => seat.DriverId).Reverse().ToList();
                seasonOne.Apply(new ResultDraft
                {
                    Classified = reversed,
                    DidNotFinish = new Dictionary<string, string>(),
                    Disqualified = [],
                });
            }

            var review = seasonOne.SeasonReview();
            Assert.NotNull(review);
            Assert.NotEmpty(review!.Offers);
            string teamId = review.Offers[0].TeamId;
            seasonOne.AcceptOffer(teamId);
            seasonOne.StartNextSeason(teamId);
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        Companion.Core.Smgp.SmgpState seasonTwoStart;
        using (var db = CareerDatabase.Open(CareerPath))
        {
            var seasons = CareerStore.ReadSeasons(db);
            Assert.Equal(2, seasons.Count);
            seasonTwoStart = StateStore.ReadPlayerState(db, seasons[1].Id, StateStore.StageStart)!.Smgp!;
            Assert.False(seasonTwoStart.StandingsReshuffle);
            Assert.DoesNotContain("standingsReshuffle", StartPlayerJson(db, seasons[1].Id), StringComparison.Ordinal);
        }

        using (var seasonTwo = CareerSessionService.OpenCareer(CareerPath, Environment()))
        {
            Assert.Equal(pinnedSeasonOne.Entries, seasonTwo.Pack.Entries);

            var wouldReshuffle = SmgpGridReshuffle.ForNextSeason(
                pinnedSeasonOne, SeasonOneFinal(pinnedSeasonOne), seasonTwoStart.CurrentSeatLivery);
            Assert.NotEqual(pinnedSeasonOne.Entries, wouldReshuffle.Entries);

            ApplyRound(seasonTwo);
        }

        AssertResimulatesByteIdentically();
    }

    [Fact]
    public void FullCampaign_StopsAfterSeventeenSeasons_AndReplaysByteIdentical()
    {
        string packDirectory = Path.Combine(PacksRoot, "smgp-dnq-ladder");
        TestPackBuilder.Write(VersionTwoCampaignPack(), packDirectory);

        CareerSessionService session = CareerSessionService.CreateCareer(new CareerCreationRequest
        {
            PackDirectory = packDirectory,
            CareerFilePath = CareerPath,
            CareerName = "Seventeen Season Career",
            MasterSeed = Seed,
            PlayerLiveryName = SeatC,
            SmgpMode = true,
            ExperienceMode = CareerExperienceModes.Smgp,
            Character = VersionTwoCharacter(),
        }, Environment());

        try
        {
            for (int ordinal = 1; ordinal <= SmgpRules.CampaignSeasons; ordinal++)
            {
                Assert.Equal(ordinal, session.CurrentSmgpBriefing()?.SeasonOrdinal);

                while (!session.Summary.SeasonComplete)
                    ApplyWinningRound(session);

                if (ordinal == SmgpRules.CampaignSeasons - 1)
                {
                    var dossier = Assert.IsType<CharacterDossier>(session.CharacterDossier());
                    Assert.Equal(CharacterLevelProgression.Level300Max, dossier.Level);
                    Assert.Equal(CharacterProgressionV2Math.LifetimeSkillPoints,
                        session.AvailableCharacterCp());
                    Assert.Equal(CharacterProgressionV2Math.LifetimeSkillPoints, dossier.CpUnspent);
                }

                if (ordinal == SmgpRules.CampaignSeasons)
                {
                    var finale = Assert.IsType<SmgpFinaleModel>(session.SmgpFinale());
                    Assert.True(finale.IsFlawless);
                    Assert.Equal("ultimate", finale.HeroImageKey);
                    Assert.Contains("17 CHAMPIONSHIPS", finale.Record);

                    Assert.Null(session.NextSeason());
                    var reviewVm = new SeasonReviewViewModel(session);
                    Assert.False(reviewVm.HasNextSeason);
                    Assert.False(reviewVm.CanSign);
                    Assert.False(reviewVm.SignAndContinueCommand.CanExecute(null));

                    var terminal = Assert.Throws<InvalidOperationException>(
                        () => session.StartNextSeason("team.c"));
                    Assert.Contains("campaign is complete", terminal.Message, StringComparison.OrdinalIgnoreCase);
                    break;
                }

                var next = session.NextSeason();
                Assert.NotNull(next);
                Assert.True(next.IsCarryover);
                Assert.Equal("test-pack", next.PackId);

                var review = session.SeasonReview();
                Assert.NotNull(review);
                Assert.NotEmpty(review.Offers);
                string teamId = review.Offers[0].TeamId;
                session.AcceptOffer(teamId);
                session.StartNextSeason(teamId);

                session.Dispose();
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                session = CareerSessionService.OpenCareer(CareerPath, Environment());
            }
        }
        finally
        {
            session.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        }

        using (var db = CareerDatabase.Open(CareerPath))
            Assert.Equal(SmgpRules.CampaignSeasons, CareerStore.ReadSeasons(db).Count);

        AssertResimulatesByteIdentically(playerAge: 23);
    }

    // ---------- scaffolding ----------

    private string PacksRoot => Path.Combine(_root, "packs");
    private string CareerPath => Path.Combine(_root, "career.ams2career");

    private CareerEnvironment Environment() => new()
    {
        ContentLibrary = FiveSeatLibrary(),
        LocateInstall = static () => null,
        DocumentsDirectory = Path.Combine(_root, "docs"),
        RulesDirectory = ViewModelTestData.RulesDirectory,
        PackSearchRoots = () => [PacksRoot, Path.Combine(AppContext.BaseDirectory, "packs")],
    };

    /// <summary>Five one-driver teams down the ladder, TWO rounds, each capping the grid at 4 → one car
    /// DNQs per round (the seeded roll picks which). SMGP style. The player takes Seat C.</summary>
    private static SeasonPack DnqLadderPack()
    {
        var basePack = TestPackBuilder.TwoRoundPack();
        var grid = new PackRoundGrid
        {
            Size = 4,
            StarterDriverIds = ["driver.a", "driver.b", "driver.c", "driver.d"], // baked; the transform re-rolls
        };
        return basePack with
        {
            Manifest = basePack.Manifest with { CareerStyle = SmgpRules.CareerStyle },
            Teams =
            [
                Team("team.a", 5), Team("team.b", 4), Team("team.c", 3), Team("team.d", 2), Team("team.e", 3),
            ],
            Drivers =
            [
                TestPackBuilder.Driver("driver.a"), TestPackBuilder.Driver("driver.b"),
                TestPackBuilder.Driver("driver.c"), TestPackBuilder.Driver("driver.d"),
                TestPackBuilder.Driver("driver.e"),
            ],
            Entries =
            [
                TestPackBuilder.Entry("team.a", "driver.a", "1", "Stock Livery #1"),
                TestPackBuilder.Entry("team.b", "driver.b", "2", "Stock Livery #2"),
                TestPackBuilder.Entry("team.c", "driver.c", "3", SeatC),
                TestPackBuilder.Entry("team.d", "driver.d", "4", "Stock Livery #4"),
                TestPackBuilder.Entry("team.e", "driver.e", "5", "Stock Livery #5"),
            ],
            Season = basePack.Season with
            {
                Rounds = basePack.Season.Rounds.Select(r => r with { Grid = grid }).ToList(),
            },
        };
    }

    private static SeasonPack VersionTwoCampaignPack()
    {
        var pack = DnqLadderPack();
        var template = pack.Season.Rounds[0];
        return pack with
        {
            Entries = pack.Entries.Select(entry => entry with { Rounds = "1-16" }).ToArray(),
            Season = pack.Season with
            {
                Rounds = Enumerable.Range(1, 16).Select(round => template with
                {
                    Round = round,
                    Name = round == 16 ? "Monaco" : $"Campaign Round {round}",
                    Date = $"1990-01-{round:00}",
                }).ToArray(),
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
        return new CharacterProfile
        {
            Name = "Zeroforce",
            Age = 23,
            Stats = talent.Concat(meta).ToDictionary(pair => pair.Key, pair => pair.Value,
                StringComparer.Ordinal),
            PerkIds = [],
            CreationPerkIds = [],
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
                TraitIds = [],
            },
        };
    }

    private static PackTeam Team(string id, int prestige) => new()
    {
        Id = id,
        Name = id,
        CarVehicleIds = [TestPackBuilder.VintageCar],
        Reliability = 0.93,
        Prestige = prestige,
        BudgetTier = prestige,
    };

    private static Companion.Ams2.ContentLibrary.Ams2ContentLibrary FiveSeatLibrary()
    {
        var library = TestPackBuilder.Library();
        return new()
        {
            ExtractedFrom = library.ExtractedFrom,
            Classes = library.Classes,
            Vehicles = library.Vehicles,
            Tracks = library.Tracks,
            Liveries = new Dictionary<string, Companion.Ams2.ContentLibrary.Ams2LiveryClassEntry>(StringComparer.Ordinal)
            {
                [TestPackBuilder.VintageClass] = new()
                {
                    Name = TestPackBuilder.VintageClass,
                    StockLib1563 = ["Stock Livery #1", "Stock Livery #2", SeatC, "Stock Livery #4", "Stock Livery #5"],
                },
            },
        };
    }

    /// <summary>Creates the DNQ career, plays season 1 to completion, accepts the top offer and signs into
    /// the same-pack carryover season 2. Returns the pinned season-1 pack (the seeded creation roll).</summary>
    private SeasonPack PlaySeasonOneAndSign()
    {
        string packDirectory = Path.Combine(PacksRoot, "smgp-dnq-ladder");
        TestPackBuilder.Write(DnqLadderPack(), packDirectory);

        SeasonPack pinned;
        using (var session = CareerSessionService.CreateCareer(new CareerCreationRequest
        {
            PackDirectory = packDirectory,
            CareerFilePath = CareerPath,
            CareerName = "Multi DNQ Career",
            MasterSeed = Seed,
            PlayerLiveryName = SeatC,
            SmgpMode = true,
        }, Environment()))
        {
            pinned = session.Pack; // season 1: variety + ForSeason are both no-ops, so this is the pinned pack

            while (!session.Summary.SeasonComplete)
                ApplyRound(session);

            var review = session.SeasonReview();
            Assert.NotNull(review);

            // The full bundled root includes f1-1991, the exact historical pack that used to
            // steal SMGP season 2. The service must keep this career on its pinned SMGP pack.
            var next = session.NextSeason();
            Assert.NotNull(next);
            Assert.True(next.IsCarryover);
            Assert.Equal("test-pack", next.PackId);
            session.AcceptOffer(review!.Offers[0].TeamId);

            var vm = new SeasonReviewViewModel(session);
            vm.SignAndContinueCommand.Execute(null);
            Assert.Null(vm.TransitionError);
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        return pinned;
    }

    /// <summary>Plays season 2 of the save PlaySeasonOneAndSign leaves behind, then signs the
    /// season-3 offer, so the career reopens into season 3 with two folded seasons behind it.</summary>
    private void PlaySeasonTwoAndSign()
    {
        using (var session = CareerSessionService.OpenCareer(CareerPath, Environment()))
        {
            while (!session.Summary.SeasonComplete)
                ApplyRound(session);

            var review = session.SeasonReview();
            Assert.NotNull(review);
            Assert.NotEmpty(review!.Offers);
            var next = session.NextSeason();
            Assert.NotNull(next);
            session.AcceptOffer(review.Offers[0].TeamId);

            var vm = new SeasonReviewViewModel(session);
            vm.SignAndContinueCommand.Execute(null);
            Assert.Null(vm.TransitionError);
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }

    /// <summary>Applies one round in resolved-grid order (the DNQ field determines who is present;
    /// the player is always cap-protected).</summary>
    private static void ApplyRound(ICareerSession session)
    {
        var grid = session.CurrentGrid().Select(s => s.DriverId).ToList();
        session.Apply(new ResultDraft
        {
            Classified = grid,
            DidNotFinish = new Dictionary<string, string>(),
            Disqualified = [],
        });
    }

    /// <summary>Applies one round with the player explicitly first, independent of grid display order.</summary>
    private static void ApplyWinningRound(ICareerSession session)
    {
        var classified = session.CurrentGrid().Select(s => s.DriverId).ToList();
        Assert.True(classified.Remove(PlayerId), "The player must be present in every campaign round.");
        classified.Insert(0, PlayerId);
        session.Apply(new ResultDraft
        {
            Classified = classified,
            DidNotFinish = new Dictionary<string, string>(),
            Disqualified = [],
        });
    }

    /// <summary>Applies one round with the player first and an optional rival call attached, the
    /// two-wins ladder driver.</summary>
    private static void ApplyPlayerFirst(ICareerSession session, SmgpRivalCall? rival)
    {
        var others = session.CurrentGrid()
            .Select(s => s.DriverId)
            .Where(id => !string.Equals(id, PlayerId, StringComparison.Ordinal))
            .ToList();
        session.Apply(new ResultDraft
        {
            Classified = new List<string> { PlayerId }.Concat(others).ToList(),
            DidNotFinish = new Dictionary<string, string>(),
            Disqualified = [],
            SmgpRival = rival,
        });
    }

    private void AssertResimulatesByteIdentically(int playerAge = 30)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using var db = CareerDatabase.Open(CareerPath);
        var rules = CareerRulesData.Load(ViewModelTestData.RulesDirectory);
        var report = ReplayService.Resimulate(db, unchecked((ulong)Seed), new ReplaySimInputs
        {
            AgingCurves = rules.AgingCurves,
            Archetypes = rules.Archetypes,
            Headlines = rules.Headlines,
            PlayerDriverId = PlayerId,
            PlayerAge = playerAge,
            CharacterRules = rules.Character,
        });
        Assert.True(report.Identical,
            $"diverged: {report.FirstDivergence?.Reason} season={report.FirstDivergence?.SeasonId} " +
            $"stored={report.FirstDivergence?.StoredDeltaJson} regen={report.FirstDivergence?.RegeneratedDeltaJson}");
        Assert.True(report.ComparedRows > 0);
    }

    private StandingsSnapshot SeasonOneFinal(SeasonPack pack)
    {
        using var db = CareerDatabase.Open(CareerPath);
        long seasonId = CareerStore.ReadSeasons(db)[0].Id;
        var results = ResultStore.ReadSeasonResults(db, seasonId)
            .Where(stored => ChampionshipCalendar.IsChampionshipRound(pack, stored.Round))
            .Select(stored => stored.ToRoundResult())
            .ToList();
        return Companion.Core.Scoring.StandingsEngine.ComputeSeason(
            ChampionshipCalendar.ResolveScoring(pack), results).Final;
    }

    private static string StartPlayerJson(CareerDatabase db, long seasonId)
    {
        using var command = db.Connection.CreateCommand();
        command.CommandText =
            "SELECT state_json FROM player_state WHERE season_id = @season AND stage = 'start';";
        command.Parameters.AddWithValue("@season", seasonId);
        return (string)command.ExecuteScalar()!;
    }
}
