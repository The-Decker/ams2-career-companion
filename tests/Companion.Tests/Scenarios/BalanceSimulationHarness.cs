using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Companion.Core.Career;
using Companion.Core.Character;
using Companion.Core.Smgp;
using Companion.Data;
using Companion.Tests.ViewModels;
using Companion.ViewModels.Services;
using Xunit.Abstractions;

namespace Companion.Tests.Scenarios;

/// <summary>
/// The SMGP-300 balance-simulation harness: full synthetic 17-season careers over the REAL
/// packs/smgp-1 pack (16 rounds × 34 cars) through the REAL creation/fold/rollover machinery,
/// across result-profile archetypes, reporting level/SP/injury/fatality distributions — the
/// evidence source for docs/LEVEL_300_BALANCE_REPORT.md and the release-evidence sweep
/// (coordinator ledger blocker 4).
///
/// DORMANT by default: the sweep runs only when COMPANION_BALANCE_SIM=&lt;careers-per-archetype&gt;
/// is set (and the evidence run when COMPANION_BALANCE_EVIDENCE=1), so the ordinary suite never
/// pays for it. Reproducible: every career is fully determined by its master seed + archetype
/// (the synthetic results use a per-career seeded RNG, mirroring FullSeasonE2ETests' stance —
/// result synthesis is test scaffolding standing in for the human importing AMS2 results; the
/// product sim never invents results).
/// </summary>
public sealed class BalanceSimulationHarness(ITestOutputHelper output)
{
    private const string PlayerLivery = "Minarae #21 J. Nono";
    private const string PlayerId = Companion.Core.Grid.RoundGridResolver.SyntheticPlayerDriverId;

    /// <summary>A result-profile archetype: where the synthetic careers' finishes centre. The
    /// mission's "competent / strong / exceptional" pacing bands, plus the grid's tail.</summary>
    private sealed record Archetype(string Name, double MeanFinish, double Sigma, bool AcceptsOffers);

    private static readonly Archetype[] Archetypes =
    [
        new("exceptional", 1.8, 1.4, AcceptsOffers: true),
        new("strong", 3.6, 2.2, AcceptsOffers: true),
        new("competent", 7.5, 3.5, AcceptsOffers: true),
        new("midfield", 11.5, 4.0, AcceptsOffers: false),
        new("backmarker", 17.0, 5.0, AcceptsOffers: false),
    ];

    private sealed record SeasonSample(int Ordinal, int Level, long Xp, int SkillPoints, int? Position, bool Champion);

    private sealed record CareerSample
    {
        public required string Archetype { get; init; }
        public required long Seed { get; init; }
        public required IReadOnlyList<SeasonSample> Seasons { get; init; }
        public required int FinalLevel { get; init; }
        public required long FinalXp { get; init; }
        public required int FinalSkillPoints { get; init; }
        public required int InjuryCount { get; init; }
        public required int RacesMissed { get; init; }
        public required bool Died { get; init; }
        public required bool CareerOver { get; init; }
        public required int SeasonsCompleted { get; init; }
        public required int Championships { get; init; }
        public required double WallSeconds { get; init; }
    }

    [Fact]
    public void BalanceSweep_RunsWhenConfigured()
    {
        string? config = Environment.GetEnvironmentVariable("COMPANION_BALANCE_SIM");
        if (string.IsNullOrEmpty(config) || !int.TryParse(config, NumberStyles.None, CultureInfo.InvariantCulture, out int perArchetype) || perArchetype <= 0)
        {
            return; // dormant: the sweep is opt-in tooling, not a suite cost
        }

        string outPath = Environment.GetEnvironmentVariable("COMPANION_BALANCE_SIM_OUT")
            ?? Path.Combine(AppContext.BaseDirectory, "balance-sim-results.jsonl");
        int dop = int.TryParse(
            Environment.GetEnvironmentVariable("COMPANION_BALANCE_SIM_DOP"), out int d) && d > 0 ? d : 4;

        var jobs = new List<(Archetype Archetype, long Seed)>();
        foreach (var archetype in Archetypes)
            for (int i = 0; i < perArchetype; i++)
                jobs.Add((archetype, 730_000 + Array.IndexOf(Archetypes, archetype) * 10_000 + i));

        var samples = new System.Collections.Concurrent.ConcurrentBag<CareerSample>();
        Parallel.ForEach(jobs, new ParallelOptions { MaxDegreeOfParallelism = dop }, job =>
        {
            samples.Add(RunCareer(job.Archetype, job.Seed));
        });

        var ordered = samples.OrderBy(s => s.Archetype, StringComparer.Ordinal).ThenBy(s => s.Seed).ToList();
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

    /// <summary>The release-evidence run (ledger blocker 4): ONE high-performing career over the
    /// real smgp-1 pack through all 17 seasons with per-season wall-clock, then a full reopen +
    /// byte-identical re-simulation proof and a full-scale newsroom/history read timing.</summary>
    [Fact]
    public void ReleaseEvidence_RealPackCampaign_ReopensAndResimulatesByteIdentical()
    {
        if (Environment.GetEnvironmentVariable("COMPANION_BALANCE_EVIDENCE") != "1")
        {
            return; // dormant: run explicitly for the release-evidence sweep
        }

        string root = Directory.CreateTempSubdirectory("companion-balance-evidence-").FullName;
        try
        {
            const long seed = 424242;
            var archetype = Archetypes[0]; // exceptional — exercises the L300 path end to end
            string careerPath = Path.Combine(root, "evidence.ams2career");
            var seasonClocks = new List<double>();

            var session = Create(careerPath, seed);
            var rng = new Random(unchecked((int)seed));
            try
            {
                for (int ordinal = 1; ordinal <= SmgpRules.CampaignSeasons; ordinal++)
                {
                    var clock = Stopwatch.StartNew();
                    PlaySeason(session, rng, archetype);
                    seasonClocks.Add(clock.Elapsed.TotalSeconds);
                    output.WriteLine($"season {ordinal}: {clock.Elapsed.TotalSeconds:0.00}s " +
                        $"(level {session.CharacterDossier()?.Level})");

                    if (session.PlayerMortality().Deceased || ordinal == SmgpRules.CampaignSeasons)
                        break;
                    var review = session.SeasonReview();
                    Assert.NotNull(review);
                    Assert.NotEmpty(review!.Offers);
                    string teamId = review.Offers[0].TeamId;
                    session.AcceptOffer(teamId);
                    session.StartNextSeason(teamId);
                    session.Dispose();
                    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                    session = CareerSessionService.OpenCareer(careerPath, SimEnvironment());
                }
            }
            finally
            {
                session.Dispose();
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            }

            // Reopen: the archive reads at full 17-season scale, timed (the memoized event spine).
            var readClock = Stopwatch.StartNew();
            using (var reopened = CareerSessionService.OpenCareer(careerPath, SimEnvironment()))
            {
                Assert.Equal(SmgpRules.CampaignSeasons, reopened.CampaignTimeline().Count(t =>
                    t.State == CampaignSeasonState.Completed));
                int stories = reopened.NewsroomFeed().Count;
                int threads = reopened.StoryThreads().Count;
                var timeline = reopened.CareerTimeline();
                output.WriteLine($"full-scale archive read: {readClock.Elapsed.TotalSeconds:0.00}s " +
                    $"({stories} stories, {threads} threads, {timeline.Seasons.Count} season cards)");
                Assert.True(stories > 0);
            }
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            // Byte-identical re-simulation over the whole 272-round campaign — the locked invariant.
            var resimClock = Stopwatch.StartNew();
            using (var db = CareerDatabase.Open(careerPath))
            {
                var rules = CareerRulesData.Load(ViewModelTestData.RulesDirectory);
                var report = ReplayService.Resimulate(db, unchecked((ulong)seed), new ReplaySimInputs
                {
                    AgingCurves = rules.AgingCurves,
                    Archetypes = rules.Archetypes,
                    Headlines = rules.Headlines,
                    PlayerDriverId = PlayerId,
                    PlayerAge = 23,
                    CharacterRules = rules.Character,
                });
                Assert.True(report.Identical,
                    $"diverged: {report.FirstDivergence?.Reason} season={report.FirstDivergence?.SeasonId}");
                Assert.True(report.ComparedRows > 0);
                output.WriteLine($"resimulate: byte-identical over {report.ComparedRows} rows " +
                    $"in {resimClock.Elapsed.TotalSeconds:0.00}s");
            }

            output.WriteLine($"season wall-clock: min {seasonClocks.Min():0.00}s, " +
                $"max {seasonClocks.Max():0.00}s, total {seasonClocks.Sum():0.00}s");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(root, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    // ---------- the career runner ----------

    private CareerSample RunCareer(Archetype archetype, long seed)
    {
        string root = Directory.CreateTempSubdirectory("companion-balance-sim-").FullName;
        var clock = Stopwatch.StartNew();
        var seasons = new List<SeasonSample>();
        bool died = false, careerOver = false;
        try
        {
            string careerPath = Path.Combine(root, "sim.ams2career");
            var session = Create(careerPath, seed);
            var rng = new Random(unchecked((int)seed));
            try
            {
                for (int ordinal = 1; ordinal <= SmgpRules.CampaignSeasons; ordinal++)
                {
                    PlaySeason(session, rng, archetype);

                    var mortality = session.PlayerMortality();
                    died = mortality.Deceased || mortality.CareerFileDeleted;
                    careerOver = !died && session.CurrentSmgpBriefing()?.CareerOver == true;
                    var dossier = died ? null : session.CharacterDossier();
                    if (dossier is not null)
                    {
                        seasons.Add(new SeasonSample(
                            ordinal, dossier.Level, dossier.Xp, dossier.CpUnspent,
                            session.Summary.PlayerPosition,
                            session.Summary.PlayerPosition == 1));
                    }

                    if (died || careerOver || ordinal == SmgpRules.CampaignSeasons)
                        break;

                    var review = session.SeasonReview();
                    if (review is null || review.Offers.Count == 0)
                        break;
                    // Climbing archetypes take the best offer; tail archetypes hold their seat
                    // when a same-team offer exists (else the best available).
                    var offer = archetype.AcceptsOffers
                        ? review.Offers[0]
                        : review.Offers.FirstOrDefault(o =>
                            string.Equals(o.TeamId, session.CurrentSmgpTeamId(), StringComparison.Ordinal))
                            ?? review.Offers[0];
                    session.AcceptOffer(offer.TeamId);
                    session.StartNextSeason(offer.TeamId);
                    session.Dispose();
                    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                    session = CareerSessionService.OpenCareer(careerPath, SimEnvironment());
                }
            }
            finally
            {
                session.Dispose();
            }

            int injuries = 0, missed = 0;
            if (!died)
            {
                using var reopened = CareerSessionService.OpenCareer(careerPath, SimEnvironment());
                var record = reopened.InjuryHistory();
                injuries = record.Count(e => e.Outcome is "minorInjury" or "seasonEnding");
                missed = record.Sum(e => e.MissRaces);
                died = reopened.PlayerMortality().Deceased;
            }

            var last = seasons.Count > 0 ? seasons[^1] : new SeasonSample(0, 1, 0, 0, null, false);
            return new CareerSample
            {
                Archetype = archetype.Name,
                Seed = seed,
                Seasons = seasons,
                FinalLevel = last.Level,
                FinalXp = last.Xp,
                FinalSkillPoints = last.SkillPoints,
                InjuryCount = injuries,
                RacesMissed = missed,
                Died = died,
                CareerOver = careerOver,
                SeasonsCompleted = seasons.Count,
                Championships = seasons.Count(s => s.Champion),
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

    /// <summary>Plays one season to completion (or a terminal state): injured rounds auto-simulate
    /// through the real sit-out path; raced rounds import a synthetic archetype-shaped result.</summary>
    private static void PlaySeason(ICareerSession session, Random rng, Archetype archetype)
    {
        while (!session.Summary.SeasonComplete)
        {
            var mortality = session.PlayerMortality();
            if (mortality.Deceased || mortality.CareerFileDeleted)
                return;
            if (session.CurrentSmgpBriefing()?.CareerOver == true)
                return;

            if (session.CurrentSitOut() is not null)
            {
                session.AutoSimulateRound();
                continue;
            }

            session.Apply(SynthesizeDraft(session, rng, archetype));

            if (session.CurrentSmgpPendingOffer() is not null)
                session.ResolveSmgpOffer(accept: archetype.AcceptsOffers);
        }
    }

    /// <summary>One synthetic round result: AI field ordered by rating + per-round noise, the
    /// player inserted at an archetype-sampled finish — or DNF'd (mechanical / accident with a
    /// sampled severity), which is what exercises the real injury/fatality fold.</summary>
    private static ResultDraft SynthesizeDraft(ICareerSession session, Random rng, Archetype archetype)
    {
        var seats = session.CurrentGrid();
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
                DidNotFinish = new Dictionary<string, string>(StringComparer.Ordinal) { [PlayerId] = "m" },
                Disqualified = [],
            };
        }
        if (dnfRoll < 0.065)
        {
            double severityRoll = rng.NextDouble();
            return new ResultDraft
            {
                Classified = aiOrder,
                DidNotFinish = new Dictionary<string, string>(StringComparer.Ordinal) { [PlayerId] = "a" },
                Disqualified = [],
                PlayerAccidentSeverity = severityRoll < 0.50 ? AccidentSeverity.Light
                    : severityRoll < 0.85 ? AccidentSeverity.Medium
                    : AccidentSeverity.Heavy,
            };
        }

        int finish = Math.Clamp(
            (int)Math.Round(Gaussian(rng, archetype.MeanFinish, archetype.Sigma)),
            1, aiOrder.Count + 1);
        var classified = new List<string>(aiOrder);
        classified.Insert(finish - 1, PlayerId);
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

    // ---------- environment ----------

    private static CareerSessionService Create(string careerPath, long seed) =>
        CareerSessionService.CreateCareer(new CareerCreationRequest
        {
            PackDirectory = Path.Combine(AppContext.BaseDirectory, "packs", "smgp-1"),
            CareerFilePath = careerPath,
            CareerName = $"Balance Sim {seed}",
            MasterSeed = seed,
            PlayerLiveryName = PlayerLivery,
            SmgpMode = true,
            FormAware = true,
            ExperienceMode = CareerExperienceModes.Smgp,
            Mortality = MortalityMode.Normal,
            Character = SimCharacter(),
        }, SimEnvironment());

    private static CareerEnvironment SimEnvironment() => new()
    {
        ContentLibrary = ViewModelTestData.RealLibrary.Value,
        LocateInstall = static () => null,
        DocumentsDirectory = Path.Combine(Path.GetTempPath(), "companion-balance-docs"),
        RulesDirectory = ViewModelTestData.RulesDirectory,
        PackSearchRoots = () => [Path.Combine(AppContext.BaseDirectory, "packs")],
    };

    /// <summary>The fixed v2 character every simulated career drives (the SAME build across
    /// archetypes — the RESULT profile is the experimental variable, not the character).</summary>
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
            Name = "Balance Probe",
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
        sb.AppendLine("SMGP-300 balance sweep — level/SP/injury distributions per archetype");
        sb.AppendLine($"careers: {samples.Count} · pack: smgp-1 (16 rounds × 34 cars × up to 17 seasons)");
        sb.AppendLine();

        int[] checkpoints = [1, 3, 5, 8, 11, 14, 17];
        foreach (var group in samples.GroupBy(s => s.Archetype, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var list = group.ToList();
            sb.AppendLine($"== {group.Key} (n={list.Count}) ==");
            foreach (int checkpoint in checkpoints)
            {
                var levels = list
                    .Select(s => s.Seasons.FirstOrDefault(x => x.Ordinal == checkpoint))
                    .Where(x => x is not null)
                    .Select(x => x!.Level)
                    .OrderBy(x => x)
                    .ToList();
                if (levels.Count == 0)
                    continue;
                sb.AppendLine($"  after S{checkpoint,2}: median L{Percentile(levels, 50),3} " +
                    $"(p10 L{Percentile(levels, 10)}, p90 L{Percentile(levels, 90)}) n={levels.Count}");
            }
            sb.AppendLine($"  final level: median L{Percentile(list.Select(s => s.FinalLevel).OrderBy(x => x).ToList(), 50)}" +
                $" · reach L100 {Rate(list, s => s.FinalLevel >= 100)} · L200 {Rate(list, s => s.FinalLevel >= 200)}" +
                $" · L250 {Rate(list, s => s.FinalLevel >= 250)} · L300 {Rate(list, s => s.FinalLevel >= 300)}");
            sb.AppendLine($"  final SP (unspent==earned): median {Percentile(list.Select(s => s.FinalSkillPoints).OrderBy(x => x).ToList(), 50)} / 499");
            sb.AppendLine($"  injuries/career: avg {list.Average(s => s.InjuryCount):0.00} · races missed avg {list.Average(s => s.RacesMissed):0.00}" +
                $" · deaths {Rate(list, s => s.Died)} · SMGP floor knock-outs {Rate(list, s => s.CareerOver)}");
            sb.AppendLine($"  championships/career: avg {list.Average(s => s.Championships):0.00}" +
                $" · seasons completed avg {list.Average(s => s.SeasonsCompleted):0.0}" +
                $" · wall avg {list.Average(s => s.WallSeconds):0.0}s");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static int Percentile(IReadOnlyList<int> sorted, int p) =>
        sorted.Count == 0 ? 0 : sorted[Math.Clamp(p * sorted.Count / 100, 0, sorted.Count - 1)];

    private static string Rate<T>(IReadOnlyList<T> list, Func<T, bool> predicate) =>
        list.Count == 0 ? "0%" : $"{100.0 * list.Count(predicate) / list.Count:0.#}%";
}
