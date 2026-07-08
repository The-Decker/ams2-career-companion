using Companion.Ams2.ContentLibrary;
using Companion.Core.Packs;
using Companion.Data;
using Companion.Tests.ViewModels;
using Companion.ViewModels.Services;

namespace Companion.Tests.Data;

/// <summary>
/// The opt-in alternate-track mechanism at career creation (Mike's rule): the flagged round is
/// swapped to its mod alternate ONLY when the player ticked it on AND the mod is installed; with the
/// tick off, or the mod missing, the pinned season keeps its base default — no round ever silently
/// depends on a mod. The transformed pack is pinned, so it replays byte-identically.
/// </summary>
public sealed class AlternateTracksCreationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("companion-alt-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private const string ModTag = "modtrack";
    private const string BaseDefault = TestPackBuilder.Track; // "kyalami_historic"

    /// <summary>TwoRoundPack with round 1 given a mod alternate (filler, isRealVenue=false).</summary>
    private static SeasonPack PackWithAlternate()
    {
        var basePack = TestPackBuilder.TwoRoundPack();
        var r0 = basePack.Season.Rounds[0];
        return basePack with
        {
            Season = basePack.Season with
            {
                Rounds =
                [
                    r0 with { Track = r0.Track with { Alternate = new PackTrackAlternate { Id = ModTag, Laps = 42, IsRealVenue = false } } },
                    basePack.Season.Rounds[1],
                ],
            },
        };
    }

    private static Ams2ContentLibrary LibraryWithMod()
    {
        var b = TestPackBuilder.Library();
        var tracks = new Dictionary<string, Ams2Track>(b.Tracks, StringComparer.Ordinal)
        {
            [ModTag] = new() { Id = ModTag, TrackName = "Mod Track", IsMod = true, MaxAiParticipants = 30, LengthMeters = 4000 },
        };
        return new Ams2ContentLibrary
        {
            ExtractedFrom = b.ExtractedFrom, Classes = b.Classes, Vehicles = b.Vehicles,
            Liveries = b.Liveries, Tracks = tracks,
        };
    }

    private string MakeInstall(bool modInstalled)
    {
        string install = Path.Combine(_root, modInstalled ? "install-with" : "install-without");
        Directory.CreateDirectory(Path.Combine(install, "Tracks"));
        if (modInstalled)
            Directory.CreateDirectory(Path.Combine(install, "Tracks", ModTag));
        return install;
    }

    private CareerSessionService Create(bool useAlternates, bool modInstalled)
    {
        string packDir = Path.Combine(_root, $"pack-{useAlternates}-{modInstalled}");
        TestPackBuilder.Write(PackWithAlternate(), packDir);

        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs"),
            installDirectory: MakeInstall(modInstalled),
            library: LibraryWithMod());

        return CareerSessionService.CreateCareer(
            new CareerCreationRequest
            {
                PackDirectory = packDir,
                CareerFilePath = Path.Combine(_root, "careers", $"c-{useAlternates}-{modInstalled}.ams2career"),
                CareerName = $"Alt {useAlternates} {modInstalled}",
                MasterSeed = 7,
                PlayerLiveryName = TestPackBuilder.StockLivery2,
                UseAlternateTracks = useAlternates,
            },
            environment);
    }

    [Fact]
    public void TickOn_ModInstalled_SwapsToTheAlternate()
    {
        using var session = Create(useAlternates: true, modInstalled: true);

        var round1 = session.Pack.Season.Rounds[0];
        Assert.Equal(ModTag, round1.Track.Id);
        Assert.Equal(42, round1.Laps);
        Assert.True(round1.Track.IsPlaceholder); // filler stays a placeholder
    }

    [Fact]
    public void TickOn_ModMissing_KeepsTheBaseDefault()
    {
        using var session = Create(useAlternates: true, modInstalled: false);

        // Mike's rule: mod not installed + season still generated => NO alternate applied.
        Assert.Equal(BaseDefault, session.Pack.Season.Rounds[0].Track.Id);
        Assert.Equal(40, session.Pack.Season.Rounds[0].Laps); // TwoRoundPack authored laps
    }

    [Fact]
    public void TickOff_KeepsTheBaseDefault_EvenWhenModInstalled()
    {
        using var session = Create(useAlternates: false, modInstalled: true);

        Assert.Equal(BaseDefault, session.Pack.Season.Rounds[0].Track.Id);
    }

    [Fact]
    public void AppliedAlternate_ReplaysByteIdentical()
    {
        string packDir = Path.Combine(_root, "pack-replay");
        TestPackBuilder.Write(PackWithAlternate(), packDir);
        string careerPath = Path.Combine(_root, "careers", "replay.ams2career");
        var environment = ViewModelTestData.Environment(
            documentsDirectory: Path.Combine(_root, "docs-replay"),
            installDirectory: MakeInstall(modInstalled: true),
            library: LibraryWithMod());

        const long seed = 909;
        SeasonPack pack;
        using (var session = CareerSessionService.CreateCareer(
                   new CareerCreationRequest
                   {
                       PackDirectory = packDir, CareerFilePath = careerPath, CareerName = "Replay",
                       MasterSeed = seed, PlayerLiveryName = TestPackBuilder.StockLivery2,
                       UseAlternateTracks = true,
                   },
                   environment))
        {
            Assert.Equal(ModTag, session.Pack.Season.Rounds[0].Track.Id); // transform applied + pinned
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
        var rules = CareerRulesData.Load(ViewModelTestData.RulesDirectory);
        var report = ReplayService.Resimulate(db, pack, unchecked((ulong)seed), new ReplaySimInputs
        {
            AgingCurves = rules.AgingCurves,
            Archetypes = rules.Archetypes,
            Headlines = rules.Headlines,
            PlayerDriverId = "driver.hulme",
            PlayerAge = 30,
        });

        Assert.True(report.Identical,
            $"diverged: {report.FirstDivergence?.Reason}");
        Assert.True(report.ComparedRows > 0);
    }
}
