using Companion.Data;
using Companion.Tests.ViewModels;
using Companion.ViewModels.Services;

namespace Companion.Tests.Data;

/// <summary>
/// Structural fix (integration report): the app pins packs as the five-file
/// <see cref="PinnedPackEnvelope"/> blob, while replay verification used to demand the
/// legacy canonical-serialization blob — so a career created through the real wizard path
/// could never re-simulate. Both formats must verify, through the ONE shared loader.
/// </summary>
public sealed class PinnedPackVerificationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("companion-pinned-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // SQLite WAL sidecars can outlive the connection briefly on Windows.
        }
    }

    /// <summary>THE regression: create a career through the app's real service (five-file
    /// envelope pinning), apply a round through the live path, then re-simulate — the pack
    /// verification must accept the envelope and the whole career must replay identically.</summary>
    [Fact]
    public void VmCreatedCareer_PassesResimulatePackVerification_AndReplaysIdentical()
    {
        string packDirectory = Path.Combine(_root, "pack");
        TestPackBuilder.Write(TestPackBuilder.TwoRoundPack(), packDirectory);
        string careerPath = Path.Combine(_root, "careers", "envelope.ams2career");

        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs"),
            library: TestPackBuilder.Library());

        const long seed = 424242;
        Companion.Core.Packs.SeasonPack pack;
        using (var session = CareerSessionService.CreateCareer(
                   new CareerCreationRequest
                   {
                       PackDirectory = packDirectory,
                       CareerFilePath = careerPath,
                       CareerName = "Envelope Career",
                       MasterSeed = seed,
                       PlayerLiveryName = TestPackBuilder.StockLivery2,
                   },
                   environment))
        {
            // One round through the LIVE path (raw import + unified fold, atomically).
            session.Apply(new ResultDraft
            {
                Classified = ["driver.brabham", "driver.hulme"],
                DidNotFinish = new Dictionary<string, string>(),
                Disqualified = [],
                SliderUsed = 100.0,
            });
            pack = session.Pack;
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using var db = CareerDatabase.Open(careerPath);

        // The same deterministic inputs CareerSessionService derives for the live fold.
        var rules = CareerRulesData.Load(ViewModelTestData.RulesDirectory);
        var inputs = new ReplaySimInputs
        {
            AgingCurves = rules.AgingCurves,
            Archetypes = rules.Archetypes,
            Headlines = rules.Headlines,
            PlayerDriverId = "driver.hulme",
            PlayerAge = 30, // pack drivers carry no Born year → season.Year − (Year − 30)
        };

        var report = ReplayService.Resimulate(db, pack, unchecked((ulong)seed), inputs);

        Assert.True(report.Identical,
            $"diverged: {report.FirstDivergence?.Reason} stored={report.FirstDivergence?.StoredDeltaJson} " +
            $"regenerated={report.FirstDivergence?.RegeneratedDeltaJson}");
        Assert.True(report.ComparedRows > 0);
    }

    /// <summary>A tampered pack (any content difference) must still be rejected — accepting
    /// the envelope format must not weaken the pinned-bytes guarantee.</summary>
    [Fact]
    public void DifferentPackContent_IsStillRejected()
    {
        string packDirectory = Path.Combine(_root, "pack2");
        TestPackBuilder.Write(TestPackBuilder.TwoRoundPack(), packDirectory);
        string careerPath = Path.Combine(_root, "careers", "tamper.ams2career");

        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs"),
            library: TestPackBuilder.Library());

        using (CareerSessionService.CreateCareer(
                   new CareerCreationRequest
                   {
                       PackDirectory = packDirectory,
                       CareerFilePath = careerPath,
                       CareerName = "Tamper Career",
                       MasterSeed = 1,
                       PlayerLiveryName = TestPackBuilder.StockLivery2,
                   },
                   environment))
        {
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using var db = CareerDatabase.Open(careerPath);

        var tampered = TestPackBuilder.TwoRoundPack() with
        {
            Drivers =
            [
                TestPackBuilder.Driver("driver.brabham") with { Name = "Somebody Else" },
                TestPackBuilder.Driver("driver.hulme"),
            ],
        };

        var rules = CareerRulesData.Load(ViewModelTestData.RulesDirectory);
        var inputs = new ReplaySimInputs
        {
            AgingCurves = rules.AgingCurves,
            Archetypes = rules.Archetypes,
            PlayerDriverId = "driver.hulme",
            PlayerAge = 30,
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ReplayService.Resimulate(db, tampered, 1UL, inputs));
        Assert.Contains("differs from the pinned copy", ex.Message);
    }

    /// <summary>The Data-layer loader accepts both pinned formats (the unification point).</summary>
    [Fact]
    public void LoadSeasonPack_AcceptsBothBlobFormats()
    {
        var pack = TestPackBuilder.TwoRoundPack();

        // Legacy canonical blob (CareerStore.PinPack format).
        byte[] canonical = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            pack, Companion.Core.Json.CoreJson.Options);
        Assert.False(PinnedPackEnvelope.IsEnvelope(canonical));
        Assert.Equal(pack.Manifest.PackId, PinnedPackEnvelope.LoadSeasonPack(canonical).Manifest.PackId);

        // Five-file envelope blob (the app's format).
        string packDirectory = Path.Combine(_root, "pack3");
        TestPackBuilder.Write(pack, packDirectory);
        byte[] envelope = SeasonPackFiles.Read(packDirectory).ToPinnedEnvelope().ToBytes();
        Assert.True(PinnedPackEnvelope.IsEnvelope(envelope));
        Assert.Equal(pack.Manifest.PackId, PinnedPackEnvelope.LoadSeasonPack(envelope).Manifest.PackId);
    }
}
