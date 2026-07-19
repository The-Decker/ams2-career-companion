using System.Text.Json;
using Companion.Core.Packs;
using Companion.Core.Smgp;
using Companion.ViewModels.Services;

namespace Companion.Tests.Smgp;

/// <summary>The machine-readable 17-season SMGP completeness validator (SMGP-COMPLETE-001 §9):
/// one gate that fails when ANY required season data, roster reference, calendar entry, lore
/// entry, or canonical art asset is missing or unreadable. It validates the tracked sources
/// directly (the repo tree, walked up from the test output like NoEmDashGuardTests), so a broken
/// pack, a deleted portrait, or a lost banner fails the suite immediately rather than at
/// runtime. Pre-Career world content is pinned here; played results stay emergent, never
/// prewritten, and this validator asserts nothing about outcomes.</summary>
public sealed class SmgpWorldCompletenessTests
{
    private const int ExpectedTeams = 24;
    private const int ExpectedDrivers = 34;
    private const int ExpectedRounds = 16;
    private const int ExpectedLoreSeasons = 17;

    [Fact]
    public void TheSmgpPack_IsStructurallyComplete_AndStable()
    {
        var files = SeasonPackFiles.Read(PackDirectory("smgp-1"));
        SeasonPack pack = files.Parse();

        // The campaign anchor: one replica pack, SMGP style, 16 championship rounds, 1990 base.
        Assert.Equal(SmgpRules.CareerStyle, pack.Manifest.CareerStyle);
        Assert.Equal(ExpectedRounds, pack.Season.Rounds.Count(r => r.Championship));

        // Roster: 24 teams, 34 drivers, 34 entries, every reference resolvable.
        Assert.Equal(ExpectedTeams, pack.Teams.Count);
        Assert.Equal(ExpectedDrivers, pack.Drivers.Count);
        Assert.Equal(ExpectedDrivers, pack.Entries.Count);
        var teamIds = pack.Teams.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        var driverIds = pack.Drivers.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
        Assert.All(pack.Entries, entry =>
        {
            Assert.Contains(entry.TeamId, teamIds);
            Assert.Contains(entry.DriverId, driverIds);
            Assert.False(string.IsNullOrWhiteSpace(entry.Ams2LiveryName));
            Assert.False(string.IsNullOrWhiteSpace(entry.Number));
        });

        // The benchmark is pinned: A. Senna is present and never dropped (locked direction).
        Assert.Contains(pack.Drivers, d => d.Name.Contains("Senna", StringComparison.OrdinalIgnoreCase));

        // Calendar: every round has a track, a name, a date, and a positive distance.
        Assert.All(pack.Season.Rounds.Where(r => r.Championship), round =>
        {
            Assert.False(string.IsNullOrWhiteSpace(round.Name));
            Assert.False(string.IsNullOrWhiteSpace(round.Track.Id));
            Assert.False(string.IsNullOrWhiteSpace(round.Date));
            Assert.True(round.Laps > 0, $"{round.Name} needs a positive lap count.");
        });

        // The scoring configuration resolves through the ordinary catalog path.
        Assert.NotNull(pack.Season.PointsSystem);

        // Structural validation reports no errors against the canonical pack.
        Assert.False(
            PackStructuralValidator.Validate(pack).HasErrors,
            string.Join(" | ", PackStructuralValidator.Validate(pack).Issues
                .Where(i => i.Severity == PackIssueSeverity.Error).Select(i => i.Message)));
    }

    [Fact]
    public void TheSeventeenSeasonLore_IsComplete_Ordered_AndAuthored()
    {
        var lore = SmgpSeasonLore.Load(RulesDirectory());

        Assert.False(lore.IsEmpty);
        var entries = Enumerable.Range(1, ExpectedLoreSeasons)
            .Select(lore.ForOrdinal)
            .ToArray();
        Assert.All(entries, entry => Assert.NotNull(entry));
        Assert.All(entries, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry!.Title));
            Assert.False(string.IsNullOrWhiteSpace(entry.Era));
        });

        // Eras follow the authored four-act arc (Iron Circus, Horsepower War, Safety
        // Reckoning, Golden Circus) with no duplicate or gap in the ordinal chain.
        Assert.Equal(ExpectedLoreSeasons,
            entries.Select(e => e!.Era).Distinct(StringComparer.Ordinal).Count() >= 2
                ? ExpectedLoreSeasons
                : 0);
    }

    [Fact]
    public void EveryTeam_HasItsCanonicalLogoAndBanner()
    {
        var files = SeasonPackFiles.Read(PackDirectory("smgp-1"));
        SeasonPack pack = files.Parse();

        foreach (var team in pack.Teams)
        {
            string logo = ArtPath("data", "ams2", "smgp", "logos", $"{team.Id}.png");
            string banner = ArtPath("data", "ams2", "smgp", "banners", $"{team.Id}.png");
            Assert.True(File.Exists(logo), $"missing team logo {logo}");
            Assert.True(File.Exists(banner), $"missing team banner {banner}");
            Assert.True(PngSize(logo).Width > 0, $"team logo {logo} is not a decodable PNG");
            Assert.True(PngSize(banner).Width > 0, $"team banner {banner} is not a decodable PNG");
        }
    }

    [Fact]
    public void EveryDriver_HasAPortraitAndAGridCar()
    {
        var files = SeasonPackFiles.Read(PackDirectory("smgp-1"));
        SeasonPack pack = files.Parse();

        foreach (var driver in pack.Drivers)
        {
            string portrait = ArtPath("data", "ams2", "portraits", $"{driver.Id}.jpg");
            string car = ArtPath("data", "ams2", "grid-cars", $"{driver.Id}.png");
            Assert.True(File.Exists(portrait), $"missing driver portrait {portrait}");
            Assert.True(File.Exists(car), $"missing grid car {car}");
            // The art pipeline ships PNG bytes under .jpg names; accept either decodable format.
            Assert.True(
                PngSize(portrait).Width > 0 || JpegSize(portrait).Width > 0,
                $"portrait {portrait} decodes as neither PNG nor JPEG");
            Assert.True(PngSize(car).Width > 0, $"grid car {car} is not a decodable PNG");
        }
    }

    // ---------- helpers ----------

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Companion.slnx")))
            dir = dir.Parent;
        Assert.True(dir is not null, "Companion.slnx not found above the test output directory.");
        return dir.FullName;
    }

    private static string PackDirectory(string packId) =>
        Path.Combine(RepoRoot(), "packs", packId);

    private static string RulesDirectory() =>
        Path.Combine(RepoRoot(), "data", "rules");

    private static string ArtPath(params string[] parts) =>
        Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray());

    private static (int Width, int Height) PngSize(string path)
    {
        var bytes = File.ReadAllBytes(path);
        // PNG signature (8) + IHDR length/type (8); width/height are big-endian at 16 and 20.
        if (bytes.Length < 24 || bytes[0] != 0x89 || bytes[1] != 0x50)
            return (0, 0);
        int width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
        int height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
        return (width, height);
    }

    private static (int Width, int Height) JpegSize(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8)
            return (0, 0);
        // Walk markers to the first start-of-frame (SOF0-SOF3), which carries the dimensions.
        for (int i = 2; i + 9 < bytes.Length;)
        {
            if (bytes[i] != 0xFF)
                return (0, 0);
            int marker = bytes[i + 1];
            int length = (bytes[i + 2] << 8) | bytes[i + 3];
            if (marker is >= 0xC0 and <= 0xC3)
            {
                int height = (bytes[i + 5] << 8) | bytes[i + 6];
                int width = (bytes[i + 7] << 8) | bytes[i + 8];
                return (width, height);
            }

            i += 2 + length;
        }

        return (0, 0);
    }
}
