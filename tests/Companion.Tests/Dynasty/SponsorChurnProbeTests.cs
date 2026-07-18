using Companion.Core.Career;
using Companion.Core.Character;
using Companion.Core.Dynasty;
using Companion.Core.Packs;
using Companion.Tests.ViewModels;
using Companion.ViewModels.Services;

namespace Companion.Tests.Dynasty;

/// <summary>TEMPORARY adversarial probe (verification scratch, not part of the suite): does the
/// session accept alternating sign/drop of the same sponsor in one pending window, and does the
/// fold then credit the signing bonus once per sign?</summary>
public sealed class SponsorChurnProbeTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("companion-churn-probe-").FullName;

    private string PacksRoot => Path.Combine(_root, "packs");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    [Fact]
    public void SignDropAlternation_IsAcceptedAndFarmsTheSigningBonus()
    {
        WritePack(1967);
        string careerPath = Path.Combine(_root, "careers", "churn.ams2career");
        using var session = CareerSessionService.CreateCareer(Request(careerPath), Environment());

        var fresh = session.EconomyDashboard()!;
        Assert.Equal("100,000", fresh.Balance);

        // The claimed exploit: [sign, drop] x 10 then a final sign, all in the round-1 window.
        const string sponsor = "sponsor.apex-lubricants"; // bonus 500, index 1 in 1967
        for (int i = 0; i < 10; i++)
        {
            session.DeclareEconomyDecision(new DynastyEconomyDecision
            {
                Kind = DynastyEconomyDecisionKind.SignSponsor,
                SponsorId = sponsor,
            });
            session.DeclareEconomyDecision(new DynastyEconomyDecision
            {
                Kind = DynastyEconomyDecisionKind.DropSponsor,
                SponsorId = sponsor,
            });
        }
        session.DeclareEconomyDecision(new DynastyEconomyDecision
        {
            Kind = DynastyEconomyDecisionKind.SignSponsor,
            SponsorId = sponsor,
        });

        var pendingView = session.EconomyDashboard()!;
        Assert.Equal(21, pendingView.PendingDecisions.Count);

        // Fold round 1 and read the credited balance.
        ApplyRound(session);
        var folded = session.EconomyDashboard()!;

        // Control: the same career shape with a SINGLE sign would fold 100,000 + 500 + round
        // settlement. The churn career must show 10 extra signing bonuses (+5,000) over that.
        // Rather than duplicate the settlement arithmetic, assert on the statement rows: exactly
        // 11 sign credits of +500 and 10 free drops.
        var signLines = folded.Statement.Where(l => l.Label.Contains("sign sponsor", StringComparison.Ordinal)).ToList();
        var dropLines = folded.Statement.Where(l => l.Label.Contains("drop sponsor", StringComparison.Ordinal)).ToList();
        Assert.Equal(11, signLines.Count);
        Assert.Equal(10, dropLines.Count);
        Assert.All(signLines, l => Assert.Equal("+500", l.Net));
        Assert.All(dropLines, l => Assert.Equal("", l.Net));

        // And the sponsor is STILL under contract afterwards.
        Assert.Single(folded.ActiveSponsors, s => s.Id == sponsor);
    }

    // ---------- harness (verbatim from DynastyEconomyDashboardTests) ----------

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
        CareerName = "Churn probe",
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
