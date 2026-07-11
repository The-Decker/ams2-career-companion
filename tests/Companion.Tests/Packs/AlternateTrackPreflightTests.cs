using Companion.Ams2.ContentLibrary;
using Companion.Ams2.Preflight;
using Companion.Core.Packs;
using Companion.Core.Scoring;

namespace Companion.Tests.Packs;

/// <summary>The pre-season mod-track install check: a mod alternate counts as installed only when its
/// loose folder &lt;install&gt;\Tracks\&lt;tag&gt;\ exists; alternates may be applied only when EVERY
/// required mod is present (Mike's all-or-nothing rule).</summary>
public sealed class AlternateTrackPreflightTests : IDisposable
{
    private readonly string _install = Directory.CreateTempSubdirectory("altpf-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_install, recursive: true); } catch (IOException) { }
    }

    private void InstallModFolder(string tag) => Directory.CreateDirectory(Path.Combine(_install, "Tracks", tag));

    private static Ams2ContentLibrary Library() => new()
    {
        ExtractedFrom = "test",
        Classes = new Dictionary<string, Ams2Class>(StringComparer.Ordinal),
        Vehicles = new Dictionary<string, Ams2Vehicle>(StringComparer.Ordinal),
        Liveries = new Dictionary<string, Ams2LiveryClassEntry>(StringComparer.Ordinal),
        Tracks = new Dictionary<string, Ams2Track>(StringComparer.Ordinal)
        {
            ["Heusden"] = new() { Id = "Heusden", TrackName = "Zolder", IsMod = true },
            ["moravia"] = new() { Id = "moravia", TrackName = "Brno_GP", IsMod = true },
        },
    };

    private static SeasonPack Pack(params string?[] altPerRound)
    {
        var rounds = altPerRound.Select((alt, i) => new PackRound
        {
            Round = i + 1,
            Name = $"R{i + 1}",
            Date = $"1978-0{i + 1}-01",
            Laps = 50,
            Track = new PackTrackRef
            {
                Id = "base_track",
                Alternate = alt is null ? null : new PackTrackAlternate { Id = alt, Laps = 50 },
            },
        }).ToList();

        return new SeasonPack
        {
            Manifest = new PackManifest { PackId = "p", Name = "p", Version = "1", FormatVersion = 1 },
            Season = new SeasonDefinition
            {
                Year = 1978, SeriesName = "F1", Ams2Class = "X",
                PointsSystem = new CatalogSeason { RacePoints = [new(9)] },
                Rounds = rounds,
            },
            Teams = [], Drivers = [], Entries = [],
        };
    }

    [Fact]
    public void AllRequiredModsInstalled_CanApply()
    {
        InstallModFolder("Heusden");
        InstallModFolder("moravia");
        var pack = Pack("Heusden", "moravia", null);

        var required = AlternateTrackPreflight.RequiredModTracks(pack, Library(), _install);

        Assert.Equal(2, required.Count); // distinct alt tags; the no-alternate round contributes none
        Assert.All(required, t => Assert.True(t.Installed));
        Assert.True(AlternateTrackPreflight.CanApplyAlternates(pack, Library(), _install));
        Assert.Empty(AlternateTrackPreflight.MissingModTracks(pack, Library(), _install));
    }

    [Fact]
    public void OneModMissing_CannotApply_AndReportsItByDisplayName()
    {
        InstallModFolder("Heusden"); // moravia deliberately not installed
        var pack = Pack("Heusden", "moravia");

        Assert.False(AlternateTrackPreflight.CanApplyAlternates(pack, Library(), _install));
        var missing = AlternateTrackPreflight.MissingModTracks(pack, Library(), _install);
        Assert.Single(missing);
        Assert.Equal("moravia", missing[0].Tag);
        Assert.Equal("Brno_GP", missing[0].DisplayName); // friendly name from the library
    }

    [Fact]
    public void NoInstallLocated_NothingVerified_CannotApply()
    {
        var pack = Pack("Heusden");

        Assert.All(AlternateTrackPreflight.RequiredModTracks(pack, Library(), installDirectory: null),
            t => Assert.False(t.Installed));
        Assert.False(AlternateTrackPreflight.CanApplyAlternates(pack, Library(), installDirectory: null));
    }

    [Fact]
    public void PackWithNoAlternates_CannotApply()
    {
        var pack = Pack(null, null);

        Assert.Empty(AlternateTrackPreflight.RequiredModTracks(pack, Library(), _install));
        Assert.False(AlternateTrackPreflight.CanApplyAlternates(pack, Library(), _install));
    }
}
