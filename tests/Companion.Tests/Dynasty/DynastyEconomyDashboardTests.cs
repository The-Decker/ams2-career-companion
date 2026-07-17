using Companion.Core.Career;
using Companion.Core.Character;
using Companion.Core.Dynasty;
using Companion.Core.Packs;
using Companion.Tests.ViewModels;
using Companion.ViewModels.Services;

namespace Companion.Tests.Dynasty;

/// <summary>The economy session surface on real machinery (the SmgpTeamDashboardTests shape):
/// the dashboard projects the folded ledger + the pending plan, and DeclareEconomyDecision is
/// the single validation authority — refusals carry player-facing reasons and journal nothing.</summary>
public sealed class DynastyEconomyDashboardTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("companion-dynasty-dashboard-").FullName;

    private string PacksRoot => Path.Combine(_root, "packs");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    [Fact]
    public void Dashboard_ProjectsTheLedgerAndValidatesEveryDecision()
    {
        WritePack(1967);
        string careerPath = Path.Combine(_root, "careers", "dashboard.ams2career");
        using var session = CareerSessionService.CreateCareer(Request(careerPath), Environment());

        // ---- fresh career: full funds, no pending, sponsor board with eligibility ----
        var fresh = session.EconomyDashboard();
        Assert.NotNull(fresh);
        Assert.Equal("100,000", fresh!.Balance);
        Assert.False(fresh.InDeficit);
        Assert.Equal(0, fresh.DevelopmentLevel);
        Assert.Equal("8,000", fresh.NextDevelopmentCost);
        Assert.Empty(fresh.PendingDecisions);
        Assert.Equal(1, fresh.NextRound);
        // 1967 board: era-windowed deals only; a results-gated title deal is honestly refused.
        Assert.Contains(fresh.SponsorBoard, o => o.Id == "sponsor.apex-lubricants" && o.Eligible);
        var title = Assert.Single(fresh.SponsorBoard, o => o.Id == "sponsor.imperial-petroleum");
        Assert.False(title.Eligible);
        Assert.Contains("standings", title.IneligibleReason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(fresh.SponsorBoard, o => o.Id == "sponsor.quantum-digital"); // 2012+ window

        // ---- decisions accepted through the session ----
        session.DeclareEconomyDecision(new DynastyEconomyDecision
        {
            Kind = DynastyEconomyDecisionKind.SignSponsor,
            SponsorId = "sponsor.apex-lubricants",
        });
        session.DeclareEconomyDecision(new DynastyEconomyDecision
        {
            Kind = DynastyEconomyDecisionKind.BuyDevelopment,
        });

        var pendingView = session.EconomyDashboard()!;
        Assert.Equal(2, pendingView.PendingDecisions.Count);
        Assert.Contains("Apex Lubricants", pendingView.PendingDecisions[0].Description, StringComparison.Ordinal);
        Assert.Equal("+500", pendingView.PendingDecisions[0].Amount);
        Assert.Equal("-8,000", pendingView.PendingDecisions[1].Amount);
        // The next increment prices off the PENDING level (0 + 1 queued → level-1 rate).
        Assert.Equal("10,800", pendingView.NextDevelopmentCost);
        var apexOffer = Assert.Single(pendingView.SponsorBoard, o => o.Id == "sponsor.apex-lubricants");
        Assert.False(apexOffer.Eligible); // pending-signed counts against the slot

        // ---- refusals: clean reasons, nothing journaled ----
        Assert.Contains("Already under contract", Assert.Throws<InvalidOperationException>(() =>
            session.DeclareEconomyDecision(new DynastyEconomyDecision
            {
                Kind = DynastyEconomyDecisionKind.SignSponsor,
                SponsorId = "sponsor.apex-lubricants",
            })).Message, StringComparison.Ordinal);
        Assert.Contains("not on the board", Assert.Throws<InvalidOperationException>(() =>
            session.DeclareEconomyDecision(new DynastyEconomyDecision
            {
                Kind = DynastyEconomyDecisionKind.SignSponsor,
                SponsorId = "sponsor.no-such",
            })).Message, StringComparison.Ordinal);
        Assert.Contains("already runs", Assert.Throws<InvalidOperationException>(() =>
            session.DeclareEconomyDecision(new DynastyEconomyDecision
            {
                Kind = DynastyEconomyDecisionKind.SetStaff,
                StaffTier = 0,
            })).Message, StringComparison.Ordinal);

        // Affordability: 100,500 in the till after the pending plan (+500 − 8,000 = 92,500);
        // escalating buys stop the moment the next increment cannot be paid in cash.
        int accepted = 0;
        InvalidOperationException? refusal = null;
        for (int i = 0; i < 10 && refusal is null; i++)
        {
            try
            {
                session.DeclareEconomyDecision(new DynastyEconomyDecision
                {
                    Kind = DynastyEconomyDecisionKind.BuyDevelopment,
                });
                accepted++;
            }
            catch (InvalidOperationException ex)
            {
                refusal = ex;
            }
        }
        Assert.NotNull(refusal);
        Assert.Contains("cannot afford", refusal!.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(4, accepted); // buys 2-5 fit (10,800+14,580+19,683+26,572.05 on 92,500); the 6th does not

        // ---- the fold consumes exactly the accepted plan ----
        ApplyRound(session);
        var folded = session.EconomyDashboard()!;
        Assert.Equal(5, folded.DevelopmentLevel);
        Assert.Empty(folded.PendingDecisions);
        Assert.Single(folded.ActiveSponsors, s => s.Id == "sponsor.apex-lubricants");
        Assert.NotEmpty(folded.Statement);
        Assert.Equal($"Round 1 settlement", folded.Statement[0].Label);
    }

    [Fact]
    public void NonEconomyCareer_HasNoDashboardAndRefusesDecisions()
    {
        WritePack(1967);
        string careerPath = Path.Combine(_root, "careers", "plain.ams2career");
        using var session = CareerSessionService.CreateCareer(new CareerCreationRequest
        {
            PackDirectory = Path.Combine(PacksRoot, "1967"),
            CareerFilePath = careerPath,
            CareerName = "Plain",
            MasterSeed = 7,
            PlayerLiveryName = TestPackBuilder.StockLivery2,
        }, Environment());

        Assert.Null(session.EconomyDashboard());
        Assert.Throws<InvalidOperationException>(() =>
            session.DeclareEconomyDecision(new DynastyEconomyDecision
            {
                Kind = DynastyEconomyDecisionKind.BuyDevelopment,
            }));
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
        CareerName = "Dynasty dashboard test",
        MasterSeed = 20260719,
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
