using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Companion.Ams2.ContentLibrary;
using Companion.Core.Career;
using Companion.Core.Character;
using Companion.Core.Dynasty;
using Companion.Core.Packs;
using Companion.Core.Scoring;
using Companion.Tests.ViewModels;
using Companion.ViewModels.Services;
using Xunit.Abstractions;

namespace Companion.Tests.Scenarios;

/// <summary>
/// The Dynasty tycoon-economy balance harness (docs/dev/dynasty-tycoon-economy.md, the mission
/// brief's definition of done): full synthetic MULTI-DECADE Dynasty careers (a pinned 1967–1980
/// catalog, 14 seasons × 8 rounds, spanning the 1960s→1970s era-scaling boundary) through the
/// REAL creation/decision/fold/rollover/era-transition machinery, across result profiles ×
/// management strategies, reporting balance trajectories, bankruptcy rates and front-running
/// rates — the evidence source for docs/DYNASTY_ECONOMY_BALANCE_REPORT.md.
///
/// DORMANT by default: the sweep runs only when COMPANION_ECONOMY_SIM=&lt;careers-per-cell&gt; is
/// set, so the ordinary suite never pays for it. Reproducible: every career is fully determined
/// by its master seed + cell (result synthesis is test scaffolding standing in for the human
/// importing AMS2 results; the product sim never invents results). Decisions go through the REAL
/// session validation (DeclareEconomyDecision) — a strategy can only do what a player could.
/// </summary>
public sealed class DynastyEconomyBalanceHarness(ITestOutputHelper output)
{
    private const string PlayerLivery = "Sim Mid #4";
    private const int FirstYear = 1967;
    private const int LastYear = 1980;
    private const int RoundsPerSeason = 8;

    /// <summary>Where the synthetic careers' finishes centre on the six-car grid.</summary>
    private sealed record ResultProfile(string Name, double MeanFinish, double Sigma);

    private static readonly ResultProfile[] Profiles =
    [
        new("strong", 2.2, 1.2),
        new("midfield", 5.0, 1.6),
        new("backmarker", 8.3, 1.4),
    ];

    /// <summary>A management strategy: what the owner does in every decision window. All levers
    /// go through the real validated decision path — refusals (affordability, caps, slots) are
    /// caught and simply end that window's spending, exactly like a player being told no.</summary>
    private sealed record Strategy(string Name, int StaffTier, SecondSeatDeal SecondSeat, int DevBuysPerWindow);

    private static readonly Strategy[] Strategies =
    [
        new("frugal", StaffTier: 0, SecondSeatDeal.PayDriver, DevBuysPerWindow: 0),
        new("balanced", StaffTier: 1, SecondSeatDeal.Retained, DevBuysPerWindow: 1),
        new("overhire", StaffTier: 3, SecondSeatDeal.Retained, DevBuysPerWindow: 8),
    ];

    private sealed record SeasonPoint(int Ordinal, double Balance, int DevelopmentLevel, int? Position, bool Champion);

    private sealed record CareerSample
    {
        public required string Profile { get; init; }
        public required string Strategy { get; init; }
        public required long Seed { get; init; }
        public required IReadOnlyList<SeasonPoint> Seasons { get; init; }
        public required bool Bankrupt { get; init; }
        public required int? BankruptSeason { get; init; }
        public required double FinalBalance { get; init; }
        public required int SeasonsCompleted { get; init; }
        public required int Championships { get; init; }
        public required int FrontSeasons { get; init; }
        public required double WallSeconds { get; init; }
    }

    [Fact]
    public void EconomySweep_RunsWhenConfigured()
    {
        string? config = Environment.GetEnvironmentVariable("COMPANION_ECONOMY_SIM");
        if (string.IsNullOrEmpty(config)
            || !int.TryParse(config, NumberStyles.None, CultureInfo.InvariantCulture, out int perCell)
            || perCell <= 0)
        {
            return; // dormant: the sweep is opt-in tooling, not a suite cost
        }

        string outPath = Environment.GetEnvironmentVariable("COMPANION_ECONOMY_SIM_OUT")
            ?? Path.Combine(AppContext.BaseDirectory, "economy-sim-results.jsonl");
        int dop = int.TryParse(
            Environment.GetEnvironmentVariable("COMPANION_ECONOMY_SIM_DOP"), out int d) && d > 0 ? d : 4;

        var jobs = new List<(ResultProfile Profile, Strategy Strategy, long Seed)>();
        foreach (var profile in Profiles)
            foreach (var strategy in Strategies)
                for (int i = 0; i < perCell; i++)
                {
                    jobs.Add((profile, strategy,
                        910_000
                        + Array.IndexOf(Profiles, profile) * 100_000
                        + Array.IndexOf(Strategies, strategy) * 10_000
                        + i));
                }

        var samples = new System.Collections.Concurrent.ConcurrentBag<CareerSample>();
        Parallel.ForEach(jobs, new ParallelOptions { MaxDegreeOfParallelism = dop }, job =>
        {
            samples.Add(RunCareer(job.Profile, job.Strategy, job.Seed));
        });

        var ordered = samples
            .OrderBy(s => s.Profile, StringComparer.Ordinal)
            .ThenBy(s => s.Strategy, StringComparer.Ordinal)
            .ThenBy(s => s.Seed)
            .ToList();
        var lines = new StringBuilder();
        foreach (var sample in ordered)
            lines.AppendLine(JsonSerializer.Serialize(sample));
        File.WriteAllText(outPath, lines.ToString());

        string report = Aggregate(ordered);
        File.WriteAllText(Path.ChangeExtension(outPath, ".summary.txt"), report);
        output.WriteLine(report);
        output.WriteLine($"raw samples: {outPath}");
        Assert.NotEmpty(ordered);
    }

    // ---------- the career runner ----------

    private CareerSample RunCareer(ResultProfile profile, Strategy strategy, long seed)
    {
        string root = Directory.CreateTempSubdirectory("companion-economy-sim-").FullName;
        var clock = Stopwatch.StartNew();
        var seasons = new List<SeasonPoint>();
        bool bankrupt = false;
        int? bankruptSeason = null;
        double finalBalance = 0;
        try
        {
            WriteCatalog(Path.Combine(root, "packs"));
            string careerPath = Path.Combine(root, "sim.ams2career");
            var session = Create(root, careerPath, seed);
            var rng = new Random(unchecked((int)seed));
            int totalSeasons = LastYear - FirstYear + 1;
            try
            {
                for (int ordinal = 1; ordinal <= totalSeasons; ordinal++)
                {
                    PlaySeason(session, rng, profile, strategy);

                    bankrupt = session.BankruptcyScreen() is not null;
                    var dashboard = session.EconomyDashboard();
                    finalBalance = ParseMoney(dashboard?.Balance);
                    seasons.Add(new SeasonPoint(
                        ordinal,
                        finalBalance,
                        dashboard?.DevelopmentLevel ?? 0,
                        session.Summary.PlayerPosition,
                        session.Summary.PlayerPosition == 1));
                    if (bankrupt)
                    {
                        bankruptSeason = ordinal;
                        break;
                    }

                    if (ordinal == totalSeasons)
                        break;
                    var review = session.SeasonReview();
                    if (review is null || review.Offers.Count == 0)
                        break;
                    // The owner keeps their own team when the market allows it.
                    var offer = review.Offers.FirstOrDefault(o =>
                            string.Equals(o.TeamId, "team.mid", StringComparison.Ordinal))
                        ?? review.Offers[0];
                    session.AcceptOffer(offer.TeamId);
                    session.StartNextSeason(offer.TeamId);
                    session.Dispose();
                    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                    session = CareerSessionService.OpenCareer(careerPath, SimEnvironment(root));
                }
            }
            finally
            {
                session.Dispose();
            }

            return new CareerSample
            {
                Profile = profile.Name,
                Strategy = strategy.Name,
                Seed = seed,
                Seasons = seasons,
                Bankrupt = bankrupt,
                BankruptSeason = bankruptSeason,
                FinalBalance = finalBalance,
                SeasonsCompleted = seasons.Count,
                Championships = seasons.Count(s => s.Champion),
                FrontSeasons = seasons.Count(s => s.Position is <= 2),
                WallSeconds = clock.Elapsed.TotalSeconds,
            };
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(root, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>One season through the real loop: the strategy makes its (validated) decisions in
    /// every between-rounds window, then the round's synthetic result folds. Bankruptcy ends it.</summary>
    private static void PlaySeason(ICareerSession session, Random rng, ResultProfile profile, Strategy strategy)
    {
        while (!session.Summary.SeasonComplete)
        {
            if (session.BankruptcyScreen() is not null)
                return;

            ApplyStrategy(session, strategy);
            // Development makes the car genuinely quicker; the synthetic HUMAN result mirrors
            // that the way the sim's own expectation does — a quarter of a place per stage.
            int developmentLevel = session.EconomyDashboard()?.DevelopmentLevel ?? 0;
            session.Apply(SynthesizeDraft(session, rng, profile, developmentLevel));
        }
    }

    /// <summary>The owner's decision window, through the REAL validation authority — refusals
    /// (slots, floors, caps, affordability) end that lever's spending like a player told no.</summary>
    private static void ApplyStrategy(ICareerSession session, Strategy strategy)
    {
        var dashboard = session.EconomyDashboard();
        if (dashboard is null || dashboard.Bankrupt)
            return;

        // Sponsors are signed by every strategy — backing is the sport's free money.
        foreach (var offer in dashboard.SponsorBoard.Where(o => o.Eligible))
        {
            TryDeclare(session, new DynastyEconomyDecision
            {
                Kind = DynastyEconomyDecisionKind.SignSponsor,
                SponsorId = offer.Id,
            });
        }

        if (dashboard.SecondSeat != strategy.SecondSeat)
        {
            TryDeclare(session, new DynastyEconomyDecision
            {
                Kind = DynastyEconomyDecisionKind.SetSecondSeat,
                SecondSeat = strategy.SecondSeat,
            });
        }

        if (dashboard.StaffTier != strategy.StaffTier)
        {
            TryDeclare(session, new DynastyEconomyDecision
            {
                Kind = DynastyEconomyDecisionKind.SetStaff,
                StaffTier = strategy.StaffTier,
            });
        }

        for (int i = 0; i < strategy.DevBuysPerWindow; i++)
        {
            if (!TryDeclare(session, new DynastyEconomyDecision
            {
                Kind = DynastyEconomyDecisionKind.BuyDevelopment,
            }))
            {
                break;
            }
        }
    }

    private static bool TryDeclare(ICareerSession session, DynastyEconomyDecision decision)
    {
        try
        {
            session.DeclareEconomyDecision(decision);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>One synthetic round result on the six-car grid: AI ordered by rating + noise,
    /// the player inserted at a profile-sampled finish — or DNF'd (mechanical / accident with a
    /// sampled severity), which is what exercises the repair-bill economics.</summary>
    private static ResultDraft SynthesizeDraft(
        ICareerSession session, Random rng, ResultProfile profile, int developmentLevel)
    {
        var seats = session.CurrentGrid();
        string playerId = seats.Single(s => s.IsPlayer).DriverId;
        var aiOrder = seats
            .Where(s => !s.IsPlayer)
            .OrderByDescending(s => s.Ratings.RaceSkill + (rng.NextDouble() - 0.5) * 0.16)
            .Select(s => s.DriverId)
            .ToList();

        double dnfRoll = rng.NextDouble();
        if (dnfRoll < 0.035)
        {
            return new ResultDraft
            {
                Classified = aiOrder,
                DidNotFinish = new Dictionary<string, string>(StringComparer.Ordinal) { [playerId] = "m" },
                Disqualified = [],
            };
        }
        if (dnfRoll < 0.065)
        {
            double severityRoll = rng.NextDouble();
            return new ResultDraft
            {
                Classified = aiOrder,
                DidNotFinish = new Dictionary<string, string>(StringComparer.Ordinal) { [playerId] = "a" },
                Disqualified = [],
                PlayerAccidentSeverity = severityRoll < 0.50 ? AccidentSeverity.Light
                    : severityRoll < 0.85 ? AccidentSeverity.Medium
                    : AccidentSeverity.Heavy,
            };
        }

        double developedMean = Math.Max(1.0, profile.MeanFinish - 0.25 * developmentLevel);
        int finish = Math.Clamp(
            (int)Math.Round(Gaussian(rng, developedMean, profile.Sigma)),
            1, aiOrder.Count + 1);
        var classified = new List<string>(aiOrder);
        classified.Insert(finish - 1, playerId);
        return new ResultDraft
        {
            Classified = classified,
            DidNotFinish = new Dictionary<string, string>(StringComparer.Ordinal),
            Disqualified = [],
        };
    }

    private static double Gaussian(Random rng, double mean, double sigma)
    {
        double u1 = 1.0 - rng.NextDouble();
        double u2 = rng.NextDouble();
        return mean + sigma * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    }

    private static double ParseMoney(string? display) =>
        double.TryParse(
            (display ?? "0").Replace(",", "", StringComparison.Ordinal),
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out double value)
            ? value
            : 0;

    // ---------- the synthetic Dynasty catalog ----------

    /// <summary>Three teams across the tier range (5/3/1), six drivers, eight rounds — one pack
    /// per year 1967–1980, so the pinned Dynasty sequence spans two eras of the scaling table.</summary>
    private static void WriteCatalog(string packsRoot)
    {
        for (int year = FirstYear; year <= LastYear; year++)
            TestPackBuilder.Write(SimPack(year), Path.Combine(packsRoot, year.ToString(CultureInfo.InvariantCulture)));
    }

    private static SeasonPack SimPack(int year)
    {
        PackTeam Team(string id, string name, int tier, double power, double weight, double reliability) => new()
        {
            Id = id,
            Name = name,
            CarVehicleIds = [TestPackBuilder.VintageCar],
            Reliability = reliability,
            Prestige = tier,
            BudgetTier = tier,
            Performance = new PackTeamPerformance { PowerScalar = power, WeightScalar = weight },
        };

        PackEntry Entry(string teamId, string driverId, string number, string livery) => new()
        {
            TeamId = teamId,
            DriverId = driverId,
            Number = number,
            Rounds = $"1-{RoundsPerSeason}",
            Ams2LiveryName = livery,
        };

        return new SeasonPack
        {
            Manifest = new PackManifest
            {
                PackId = $"economy-sim-{year}",
                Name = $"Economy Sim {year}",
                Version = "1.0.0",
                FormatVersion = 1,
            },
            Season = new SeasonDefinition
            {
                Year = year,
                SeriesName = $"Economy Sim Championship {year}",
                Ams2Class = TestPackBuilder.VintageClass,
                PointsSystem = new CatalogSeason
                {
                    RacePoints = [new(9), new(6), new(4), new(3), new(2), new(1)],
                    Constructors = new CatalogConstructors { BestCarOnly = false },
                },
                Rounds = Enumerable.Range(1, RoundsPerSeason)
                    .Select(round => TestPackBuilder.Round(round, $"{year}-{Math.Min(round, 12):00}-07"))
                    .ToArray(),
            },
            Teams =
            [
                Team("team.top", "Sim Works", 5, 1.02, 0.98, 0.95),
                Team("team.second", "Sim International", 4, 1.01, 0.99, 0.93),
                Team("team.mid", "Sim Racing", 3, 1.00, 1.00, 0.90),
                Team("team.lower", "Sim Engineering", 2, 0.98, 1.01, 0.86),
                Team("team.min", "Sim Privateers", 1, 0.97, 1.02, 0.82),
            ],
            Drivers =
            [
                SimDriver("driver.a", 0.86),
                SimDriver("driver.b", 0.82),
                SimDriver("driver.c", 0.78),
                SimDriver("driver.d", 0.74),
                SimDriver("driver.p", 0.70),
                SimDriver("driver.mate", 0.70),
                SimDriver("driver.g", 0.64),
                SimDriver("driver.h", 0.61),
                SimDriver("driver.e", 0.58),
                SimDriver("driver.f", 0.55),
            ],
            Entries =
            [
                Entry("team.top", "driver.a", "1", "Sim Top #1"),
                Entry("team.top", "driver.b", "2", "Sim Top #2"),
                Entry("team.second", "driver.c", "3", "Sim 2nd #3"),
                Entry("team.second", "driver.d", "4", "Sim 2nd #4"),
                Entry("team.mid", "driver.p", "5", PlayerLivery),
                Entry("team.mid", "driver.mate", "6", "Sim Mid #6"),
                Entry("team.lower", "driver.g", "7", "Sim Low #7"),
                Entry("team.lower", "driver.h", "8", "Sim Low #8"),
                Entry("team.min", "driver.e", "9", "Sim Min #9"),
                Entry("team.min", "driver.f", "10", "Sim Min #10"),
            ],
        };
    }

    private static PackDriver SimDriver(string id, double skill) => new()
    {
        Id = id,
        Name = id,
        Born = 1937,
        Ratings = new PackDriverRatings
        {
            RaceSkill = skill,
            QualifyingSkill = skill,
            Aggression = 0.5,
            Defending = 0.5,
            Stamina = 0.8,
            Consistency = 0.8,
            StartReactions = 0.8,
            WetSkill = 0.8,
            TyreManagement = 0.8,
            AvoidanceOfMistakes = 0.8,
        },
    };

    private static Ams2ContentLibrary SimLibrary() => new()
    {
        ExtractedFrom = "in-memory economy-sim library",
        Classes = new Dictionary<string, Ams2Class>(StringComparer.Ordinal)
        {
            [TestPackBuilder.VintageClass] = new()
            {
                XmlName = TestPackBuilder.VintageClass,
                Vehicles = [TestPackBuilder.VintageCar],
            },
        },
        Vehicles = new Dictionary<string, Ams2Vehicle>(StringComparer.Ordinal)
        {
            [TestPackBuilder.VintageCar] = new()
            {
                Id = TestPackBuilder.VintageCar,
                Dir = TestPackBuilder.VintageCar,
                VehicleClass = TestPackBuilder.VintageClass,
            },
        },
        Tracks = new Dictionary<string, Ams2Track>(StringComparer.Ordinal)
        {
            [TestPackBuilder.Track] = new()
            {
                Id = TestPackBuilder.Track,
                TrackName = "Kyalami Historic",
                MaxAiParticipants = 20,
            },
        },
        Liveries = new Dictionary<string, Ams2LiveryClassEntry>(StringComparer.Ordinal)
        {
            [TestPackBuilder.VintageClass] = new()
            {
                Name = TestPackBuilder.VintageClass,
                StockLib1563 =
                [
                    "Sim Top #1", "Sim Top #2", "Sim 2nd #3", "Sim 2nd #4", PlayerLivery,
                    "Sim Mid #6", "Sim Low #7", "Sim Low #8", "Sim Min #9", "Sim Min #10",
                ],
            },
        },
    };

    // ---------- environment ----------

    private static CareerSessionService Create(string root, string careerPath, long seed) =>
        CareerSessionService.CreateCareer(new CareerCreationRequest
        {
            PackDirectory = Path.Combine(root, "packs", FirstYear.ToString(CultureInfo.InvariantCulture)),
            CareerFilePath = careerPath,
            CareerName = $"Economy Sim {seed}",
            MasterSeed = seed,
            PlayerLiveryName = PlayerLivery,
            FormAware = true,
            ExperienceMode = CareerExperienceModes.GrandPrixDynasty,
            Character = SimCharacter(),
            DynastyEconomy = true,
        }, SimEnvironment(root));

    private static CareerEnvironment SimEnvironment(string root) => new()
    {
        ContentLibrary = SimLibrary(),
        LocateInstall = static () => null,
        DocumentsDirectory = Path.Combine(root, "documents"),
        RulesDirectory = ViewModelTestData.RulesDirectory,
        PackSearchRoots = () => [Path.Combine(root, "packs")],
    };

    private static CharacterProfile SimCharacter()
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
            Name = "Economy Probe",
            CountryCode = "BRA",
            Age = 23,
            Stats = talent.Concat(meta).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
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

    // ---------- aggregation ----------

    private static string Aggregate(IReadOnlyList<CareerSample> samples)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Dynasty economy balance sweep — balance/bankruptcy distributions per profile × strategy");
        sb.AppendLine($"careers: {samples.Count} · catalog: {FirstYear}-{LastYear} " +
            $"({LastYear - FirstYear + 1} seasons × {RoundsPerSeason} rounds, tiers 5/3/1, player = tier 3)");
        sb.AppendLine();

        int[] checkpoints = [1, 3, 5, 8, 11, 14];
        foreach (var group in samples
            .GroupBy(s => $"{s.Profile}/{s.Strategy}", StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var list = group.ToList();
            sb.AppendLine($"== {group.Key} (n={list.Count}) ==");
            foreach (int checkpoint in checkpoints)
            {
                var balances = list
                    .Select(s => s.Seasons.FirstOrDefault(x => x.Ordinal == checkpoint))
                    .Where(x => x is not null)
                    .Select(x => x!.Balance)
                    .OrderBy(x => x)
                    .ToList();
                if (balances.Count == 0)
                    continue;
                sb.AppendLine($"  after S{checkpoint,2}: median {Percentile(balances, 50),12:#,0} " +
                    $"(p10 {Percentile(balances, 10):#,0}, p90 {Percentile(balances, 90):#,0}) n={balances.Count}");
            }
            sb.AppendLine($"  bankrupt: {Rate(list, s => s.Bankrupt)}" +
                $"{MedianBankruptSeason(list)}");
            sb.AppendLine($"  final balance: median {Percentile(list.Select(s => s.FinalBalance).OrderBy(x => x).ToList(), 50):#,0}" +
                $" · seasons completed avg {list.Average(s => s.SeasonsCompleted):0.0} of {LastYear - FirstYear + 1}");
            sb.AppendLine($"  development at final season: avg L{list.Average(s => s.Seasons.Count > 0 ? s.Seasons[^1].DevelopmentLevel : 0):0.0}" +
                $" · front seasons (P1-P2) avg {list.Average(s => s.FrontSeasons):0.0}" +
                $" · titles avg {list.Average(s => s.Championships):0.00}");
            sb.AppendLine($"  wall avg {list.Average(s => s.WallSeconds):0.0}s");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string MedianBankruptSeason(IReadOnlyList<CareerSample> list)
    {
        var seasons = list.Where(s => s.BankruptSeason is not null)
            .Select(s => (double)s.BankruptSeason!.Value)
            .OrderBy(x => x)
            .ToList();
        return seasons.Count == 0 ? "" : $" · median bankruptcy season S{Percentile(seasons, 50):0}";
    }

    private static double Percentile(IReadOnlyList<double> sorted, int p) =>
        sorted.Count == 0 ? 0 : sorted[Math.Clamp(p * sorted.Count / 100, 0, sorted.Count - 1)];

    private static string Rate<T>(IReadOnlyList<T> list, Func<T, bool> predicate) =>
        list.Count == 0 ? "0%" : $"{100.0 * list.Count(predicate) / list.Count:0.#}%";
}
