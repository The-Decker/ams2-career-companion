using System.Text.Json;
using Companion.Core.Career;
using Companion.Core.Character;
using Companion.Core.Dynasty;
using Companion.Core.Json;
using Companion.Core.Numerics;
using Companion.Core.Packs;
using Companion.Data;
using Companion.Tests.ViewModels;
using Companion.ViewModels.Review;
using Companion.ViewModels.Services;

namespace Companion.Tests.Dynasty;

/// <summary>THE economy replay contract (the single most important economy test, mirroring
/// AccidentFoldDeterminismTests): a Dynasty economy career with journaled decisions, sign a
/// sponsor, buy development, drives two full rounds, a season end, and an era transition into
/// the next pinned pack, then <see cref="ReplayService.Resimulate(CareerDatabase, ulong, ReplaySimInputs)"/>
/// re-derives every economy row and the carried ledger BYTE-IDENTICALLY. Also proves the ledger
/// chains exactly (every money row's balanceFrom equals the previous row's balanceTo) and that
/// the economy state survives the pack changeover.</summary>
public sealed class DynastyEconomyFoldDeterminismTests : IDisposable
{
    private const long Seed = 20260717;

    private readonly string _root = Directory.CreateTempSubdirectory("companion-dynasty-determinism-").FullName;

    private string PacksRoot => Path.Combine(_root, "packs");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    [Fact]
    public void EconomyCareerWithDecisions_ResimulatesByteIdentically()
    {
        WritePack(1967);
        WritePack(1969);
        WritePack(2020);
        string careerPath = Path.Combine(_root, "careers", "economy-determinism.ams2career");

        // ---- create the economy career, then declare round-1 decisions in the open window ----
        using (CareerSessionService.CreateCareer(Request(careerPath), Environment()))
        {
        }

        long firstSeasonId;
        using (var db = CareerDatabase.Open(careerPath))
        {
            firstSeasonId = CareerStore.ReadSeasons(db).Single().Id;
            ReplayService.DeclareEconomyDecision(db, firstSeasonId, round: 1, new DynastyEconomyDecision
            {
                Kind = DynastyEconomyDecisionKind.SignSponsor,
                SponsorId = "sponsor.apex-lubricants",
            }, Utc(1));
            ReplayService.DeclareEconomyDecision(db, firstSeasonId, round: 1, new DynastyEconomyDecision
            {
                Kind = DynastyEconomyDecisionKind.BuyDevelopment,
            }, Utc(2));
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        // ---- season 1: two rounds (player wins round 1, trails round 2), season end, and the
        // era transition into the 1969 pack ----
        using (var session = CareerSessionService.OpenCareer(careerPath, Environment()))
        {
            ApplyRound(session, playerFirst: true);
            ApplyRound(session, playerFirst: false);
            Assert.True(session.Summary.SeasonComplete);

            var review = Assert.IsType<SeasonReviewModel>(session.SeasonReview());
            string teamId = review.Offers.First().TeamId;
            session.AcceptOffer(teamId);
            session.StartNextSeason(teamId);
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        // Season 2 (the 1969 pack) continues in a fresh session, the app's own reopen shape.
        using (var session = CareerSessionService.OpenCareer(careerPath, Environment()))
        {
            ApplyRound(session, playerFirst: true);
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        using var verify = CareerDatabase.Open(careerPath);
        var seasons = CareerStore.ReadSeasons(verify);
        Assert.Equal([1967, 1969], seasons.Select(s => s.Year));

        // ---- the decision INPUT rows exist and are provenance-excluded ----
        var firstJournal = JournalStore.ReadSeason(verify, seasons[0].Id);
        var decisionRows = firstJournal
            .Where(r => r.Phase == JournalPhases.EconomyDecision)
            .ToList();
        Assert.Equal(2, decisionRows.Count);
        Assert.All(decisionRows, r => Assert.True(DataJournalPhases.IsProvenance(r.Phase)));

        // ---- derived economy rows: 2 applied (round 1), one economy.round per round, one
        // economy.season at the season end ----
        Assert.Equal(2, firstJournal.Count(r => r.Phase == JournalPhases.EconomyApplied));
        Assert.Equal(2, firstJournal.Count(r => r.Phase == JournalPhases.EconomyRound));
        Assert.Equal(1, firstJournal.Count(r => r.Phase == JournalPhases.EconomySeason));
        var secondJournal = JournalStore.ReadSeason(verify, seasons[1].Id);
        Assert.Equal(1, secondJournal.Count(r => r.Phase == JournalPhases.EconomyRound));

        // ---- the ledger CHAINS exactly: every money row's balanceFrom is the previous row's
        // balanceTo, from the pinned opening balance to the carried final state ----
        var startEconomy = StateStore.ReadPlayerState(verify, seasons[0].Id, StateStore.StageStart)!
            .Economy!;
        Assert.Equal(Rational.Parse("100000"), startEconomy.Balance);
        var chain = startEconomy.Balance;
        foreach (var row in firstJournal.Concat(secondJournal))
        {
            if (row.Phase is not (JournalPhases.EconomyApplied
                or JournalPhases.EconomyRound or JournalPhases.EconomySeason))
            {
                continue;
            }
            using var delta = JsonDocument.Parse(row.DeltaJson);
            Assert.Equal(chain.ToString(), delta.RootElement.GetProperty("balanceFrom").GetString());
            chain = Rational.Parse(delta.RootElement.GetProperty("balanceTo").GetString()!);
        }

        // ---- the ledger survives the era transition into the 1969 pack ----
        var secondStart = StateStore.ReadPlayerState(verify, seasons[1].Id, StateStore.StageStart)!;
        Assert.NotNull(secondStart.Economy);
        var secondRoundState = StateStore.ReadRoundPlayerState(verify, seasons[1].Id, 1)!;
        Assert.Equal(chain, secondRoundState.Player.Economy!.Balance);
        Assert.False(secondRoundState.Player.Economy.Bankrupt);

        // ---- THE contract: byte-identical multi-pack re-simulation, decisions included ----
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
    public void DeclareEconomyDecision_RefusesARoundThatAlreadyHasAResult()
    {
        WritePack(1967);
        WritePack(1969);
        WritePack(2020);
        string careerPath = Path.Combine(_root, "careers", "economy-locked.ams2career");
        using (var session = CareerSessionService.CreateCareer(Request(careerPath), Environment()))
        {
            ApplyRound(session, playerFirst: true);
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        using var db = CareerDatabase.Open(careerPath);
        long seasonId = CareerStore.ReadSeasons(db).Single().Id;
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ReplayService.DeclareEconomyDecision(db, seasonId, round: 1, new DynastyEconomyDecision
            {
                Kind = DynastyEconomyDecisionKind.BuyDevelopment,
            }, Utc(9)));
        Assert.Contains("already has an imported result", ex.Message, StringComparison.Ordinal);

        // Round 2 is still open, the next-round declaration window.
        long seq = ReplayService.DeclareEconomyDecision(db, seasonId, round: 2, new DynastyEconomyDecision
        {
            Kind = DynastyEconomyDecisionKind.BuyDevelopment,
        }, Utc(10));
        Assert.True(seq > 0);
    }

    // ---------- harness ----------

    private static void ApplyRound(ICareerSession session, bool playerFirst)
    {
        var grid = session.CurrentGrid();
        string playerId = grid.Single(seat => seat.IsPlayer).DriverId;
        var classified = grid.Select(seat => seat.DriverId).ToList();
        Assert.True(classified.Remove(playerId));
        classified.Insert(playerFirst ? 0 : classified.Count, playerId);
        session.Apply(new ResultDraft
        {
            Classified = classified,
            DidNotFinish = new Dictionary<string, string>(),
            Disqualified = [],
        });
    }

    private static string Utc(int tick) => $"2026-07-17T00:00:{tick:00}.0000000Z";

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
        CareerName = "Dynasty economy determinism",
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
