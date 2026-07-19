using System.Globalization;
using System.Text.RegularExpressions;
using Companion.Ams2.ContentLibrary;
using Companion.Ams2.Packs;
using Companion.Ams2.Preflight;
using Companion.Core.Packs;

namespace Companion.Tests.Packs;

/// <summary>
/// Living validation of the shipped reference packs (packs/f1-1967, packs/f1-1969,
/// packs/f1-1988): every pack in the repo must load through <see cref="PackLoader"/>, pass
/// <see cref="PackStructuralValidator"/> with no errors, and pass
/// <see cref="PackContentValidator"/> against the REAL extracted content library
/// (data/ams2/*.json, linked into the test output) with an empty installed-livery set.
/// Stock-library livery misses are warnings by design, the skin packs the manifests require
/// are not assumed installed, so only errors fail the suite.
/// </summary>
public class ReferencePackTests
{
    private static string PacksDirectory =>
        Path.Combine(AppContext.BaseDirectory, "packs");

    private static string Ams2DataDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "ams2");

    private static readonly Lazy<Ams2ContentLibrary> Library = new(() =>
    {
        if (!File.Exists(Path.Combine(Ams2DataDirectory, "classes.json")))
            throw new FileNotFoundException(
                $"Content library not found under '{Ams2DataDirectory}'. The test project links " +
                "data/ams2/*.json into the output, rebuild tests/Companion.Tests.");
        return Ams2ContentLibrary.Load(Ams2DataDirectory);
    });

    /// <summary>Every pack folder shipped in the repo, the living-validation suite is
    /// directory-driven, so a newly added season pack is held to the exemplar bar
    /// automatically (load, structural + content validation, v1.1 venue rules).</summary>
    public static TheoryData<string> ReferencePackIds()
    {
        var data = new TheoryData<string>();
        foreach (var dir in Directory.GetDirectories(PacksDirectory).OrderBy(d => d, StringComparer.Ordinal))
            data.Add(Path.GetFileName(dir));
        return data;
    }

    /// <summary>The three original exemplars, which every era of the format was proven against
    /// and which the placeholder mechanism is known to exercise.</summary>
    public static TheoryData<string> KnownPlaceholderPackIds() =>
        new() { "f1-1967", "f1-1969", "f1-1988" };

    // ---------- loading ----------

    private static SeasonPack LoadPack(string packId)
    {
        string dir = Path.Combine(PacksDirectory, packId);
        Assert.True(Directory.Exists(dir),
            $"Reference pack folder '{dir}' was not copied to the test output, " +
            "check the packs None-Include in Companion.Tests.csproj and rebuild.");

        return PackLoader.Parse(
            Read(dir, "pack.json"),
            Read(dir, "season.json"),
            Read(dir, "teams.json"),
            Read(dir, "drivers.json"),
            Read(dir, "entries.json"));
    }

    private static string Read(string dir, string filePart)
    {
        string path = Path.Combine(dir, filePart);
        Assert.True(File.Exists(path), $"Pack file '{path}' is missing, a season pack is five files.");
        return File.ReadAllText(path);
    }

    // ---------- the repo must actually contain every reference pack ----------

    [Fact]
    public void PacksFolder_ContainsAllReferencePacks()
    {
        Assert.True(Directory.Exists(PacksDirectory),
            $"'{PacksDirectory}' missing, the packs None-Include did not copy anything.");

        var found = Directory.GetDirectories(PacksDirectory).Select(Path.GetFileName).ToHashSet();
        Assert.Contains("f1-1967", found);
        Assert.Contains("f1-1969", found);
        Assert.Contains("f1-1988", found);
    }

    // ---------- identity: folder name is the packId ----------

    [Theory]
    [MemberData(nameof(ReferencePackIds))]
    public void ReferencePack_ManifestIdentityMatchesFolder(string packId)
    {
        var manifest = LoadPack(packId).Manifest;

        Assert.Equal(packId, manifest.PackId);
        Assert.Equal(1, manifest.FormatVersion);
    }

    // ---------- structural validation (no content library needed) ----------

    [Theory]
    [MemberData(nameof(ReferencePackIds))]
    public void ReferencePack_PassesStructuralValidationWithNoErrors(string packId)
    {
        var report = PackStructuralValidator.Validate(LoadPack(packId));

        Assert.False(report.HasErrors,
            $"{packId} failed structural validation:\n{FormatStructural(report)}");
    }

    /// <summary>Reference packs are the format's exemplars: they must not even warn.</summary>
    [Theory]
    [MemberData(nameof(ReferencePackIds))]
    public void ReferencePack_StructuralValidationIsWarningFreeToo(string packId)
    {
        var report = PackStructuralValidator.Validate(LoadPack(packId));

        Assert.True(report.Issues.Count == 0,
            $"{packId} structural validation is not clean:\n{FormatStructural(report)}");
    }

    // ---------- content validation against the real library, nothing installed ----------

    [Theory]
    [MemberData(nameof(ReferencePackIds))]
    public void ReferencePack_PassesContentValidationAgainstRealLibrary(string packId)
    {
        var pack = LoadPack(packId);

        var report = PackContentValidator.Validate(pack, Library.Value, installedLiveries: []);

        // With no skin packs installed, livery misses against the stock library are warnings
        // by design. Anything the validator calls an ERROR (unknown class/track/vehicle,
        // AI-cap overflow) is a data bug in the pack.
        var errors = report.Issues.Where(i => i.Severity == PreflightSeverity.Error).ToList();
        Assert.True(errors.Count == 0,
            $"{packId} failed content validation against the real library:\n{FormatContent(report)}");
    }

    [Theory]
    [MemberData(nameof(ReferencePackIds))]
    public void ReferencePack_NoContentErrorMentionsTheClassOrAnyTrackId(string packId)
    {
        var pack = LoadPack(packId);

        var report = PackContentValidator.Validate(pack, Library.Value, installedLiveries: []);
        var errorMessages = report.Issues
            .Where(i => i.Severity == PreflightSeverity.Error)
            .Select(i => i.Message)
            .ToList();

        // The contract's item-2/item-3 failure modes, asserted by name: the pack's ams2Class
        // and every track id (primaries and fallbacks) resolved against the real library.
        Assert.DoesNotContain(errorMessages, m => m.Contains(pack.Season.Ams2Class));

        var trackIds = pack.Season.Rounds
            .SelectMany(r => r.Track.Fallbacks.Prepend(r.Track.Id))
            .Distinct(StringComparer.Ordinal);
        foreach (var trackId in trackIds)
        {
            Assert.DoesNotContain(errorMessages, m => m.Contains($"'{trackId}'"));
        }
    }

    // ---------- historical per-round grid (the grid-cap fix) ----------

    /// <summary>Every round carries a grid block, and its size never exceeds the track's Max AI
    /// participants (the game's own cap), the core promise of the grid-cap fix.</summary>
    [Theory]
    [MemberData(nameof(ReferencePackIds))]
    public void ReferencePack_EveryRoundGridSizeFitsTheTrackAiCap(string packId)
    {
        var pack = LoadPack(packId);

        foreach (var round in pack.Season.Rounds)
        {
            Assert.True(round.Grid is not null,
                $"{packId} round {round.Round} ({round.Name}) has no grid block, the grid-cap fix " +
                "adds one to every round.");

            var grid = round.Grid!;
            Assert.True(grid.Size >= 1,
                $"{packId} round {round.Round}: grid.size {grid.Size} must be at least 1.");

            var track = Library.Value.Tracks[round.Track.Id];
            Assert.True(grid.Size <= track.MaxAiParticipants,
                $"{packId} round {round.Round}: grid.size {grid.Size} exceeds {round.Track.Id}'s " +
                $"Max AI cap of {track.MaxAiParticipants}.");
        }
    }

    /// <summary>grid.size never exceeds the number of entries covering the round: a grid cannot
    /// seat more historical starters than the pack actually provides for that round.</summary>
    [Theory]
    [MemberData(nameof(ReferencePackIds))]
    public void ReferencePack_EveryRoundGridSizeFitsItsCoveringEntryCount(string packId)
    {
        var pack = LoadPack(packId);

        foreach (var round in pack.Season.Rounds)
        {
            int covering = pack.Entries.Count(e =>
                RoundsRange.TryParse(e.Rounds, out var r) && r!.Contains(round.Round))
                + round.GuestEntries.Count;

            Assert.True(round.Grid!.Size <= covering,
                $"{packId} round {round.Round}: grid.size {round.Grid.Size} exceeds the {covering} " +
                "covering entr(y/ies).");

            // Every listed starter must be a covering entry's driver (else it would not seat).
            var coveringDrivers = pack.Entries
                .Where(e => RoundsRange.TryParse(e.Rounds, out var r) && r!.Contains(round.Round))
                .Select(e => e.DriverId)
                .Concat(round.GuestEntries.Select(g => g.DriverId))
                .ToHashSet(StringComparer.Ordinal);
            foreach (var starterId in round.Grid.StarterDriverIds)
            {
                Assert.Contains(starterId, coveringDrivers);
            }
        }
    }

    /// <summary>The setup guide's opponent count is grid.size - 1 on every round (the player
    /// replaces one historical starter, so total cars == grid.size).</summary>
    [Theory]
    [MemberData(nameof(ReferencePackIds))]
    public void ReferencePack_SetupGuideOpponentsIsGridSizeMinusOne(string packId)
    {
        var pack = LoadPack(packId);

        foreach (var round in pack.Season.Rounds)
        {
            Assert.NotNull(round.SetupGuide);
            Assert.Equal(round.Grid!.Size - 1, round.SetupGuide!.Session.Opponents);
        }
    }

    // ---------- optional alternate mod tracks (opt-in venue swaps) ----------

    /// <summary>Every authored <c>track.alternate</c> must point at a track that (a) resolves in the
    /// content library, (b) is an installed MOD track (<c>isMod:true</c>, base/DLC tracks are never
    /// offered as opt-in mod alternates), and (c) carries a positive distance-preserving lap count.
    /// Guards the curated alternate list against a typo'd tag or a non-mod target.</summary>
    [Theory]
    [MemberData(nameof(ReferencePackIds))]
    public void ReferencePack_EveryTrackAlternateResolvesToAnInstalledModTrack(string packId)
    {
        var pack = LoadPack(packId);

        foreach (var round in pack.Season.Rounds)
        {
            if (round.Track.Alternate is not { } alt)
                continue;

            Assert.True(Library.Value.Tracks.TryGetValue(alt.Id, out var track),
                $"{packId} round {round.Round}: alternate track '{alt.Id}' is not in the content library.");
            Assert.True(track!.IsMod,
                $"{packId} round {round.Round}: alternate '{alt.Id}' is not a mod track (isMod:false), " +
                "only installed mod tracks may be opt-in alternates.");
            Assert.True(alt.Laps >= 1,
                $"{packId} round {round.Round}: alternate '{alt.Id}' has laps={alt.Laps}; must be >= 1.");
        }
    }

    // ---------- v1.1: real venues + placeholder distance preservation ----------

    [Theory]
    [MemberData(nameof(ReferencePackIds))]
    public void ReferencePack_EveryRoundNamesItsRealVenue(string packId)
    {
        var pack = LoadPack(packId);

        foreach (var round in pack.Season.Rounds)
        {
            Assert.False(string.IsNullOrWhiteSpace(round.Track.RealVenue),
                $"{packId} round {round.Round} ({round.Name}) has no track.realVenue, " +
                "v1.1 keeps the historical venue on record for every round.");
        }
    }

    /// <summary>The known-placeholder exemplars must actually mark placeholders, a floor that
    /// catches the isPlaceholder flag silently breaking. (A newer pack whose venues all exist
    /// in AMS2 legitimately has none, so this is asserted only for the known set.)</summary>
    [Theory]
    [MemberData(nameof(KnownPlaceholderPackIds))]
    public void KnownReferencePack_HasPlaceholderRounds(string packId)
    {
        var pack = LoadPack(packId);
        Assert.Contains(pack.Season.Rounds, r => r.Track.IsPlaceholder);
    }

    [Theory]
    [MemberData(nameof(ReferencePackIds))]
    public void ReferencePack_PlaceholderNotesNameTheRealVenue(string packId)
    {
        var pack = LoadPack(packId);

        var placeholders = pack.Season.Rounds.Where(r => r.Track.IsPlaceholder).ToList();

        foreach (var round in placeholders)
        {
            string? notes = round.SetupGuide?.Notes;
            Assert.False(string.IsNullOrWhiteSpace(notes),
                $"{packId} round {round.Round} ({round.Name}) is a placeholder but has no setupGuide notes.");
            Assert.Contains(round.Track.RealVenue!, notes, StringComparison.Ordinal);
        }
    }

    /// <summary>Placeholder rounds preserve the REAL race distance, not the historical lap
    /// count: laps must equal round(historical distance / stand-in lap length) as stated in the
    /// notes, and must differ from the historical lap count unless the math genuinely lands
    /// there (1988 Mexico on Interlagos 1991: 67 -> 68 laps, near-identical lap lengths).</summary>
    [Theory]
    [MemberData(nameof(ReferencePackIds))]
    public void ReferencePack_PlaceholderLapsPreserveTheHistoricalDistance(string packId)
    {
        var pack = LoadPack(packId);

        foreach (var round in pack.Season.Rounds.Where(r => r.Track.IsPlaceholder))
        {
            string notes = round.SetupGuide?.Notes ?? "";
            var match = Regex.Match(notes,
                @"(?<hist>\d+) laps / (?<km>\d+(?:\.\d+)?) km reproduced as (?<laps>\d+) laps");
            Assert.True(match.Success,
                $"{packId} round {round.Round} ({round.Name}): placeholder notes must state " +
                $"'<historical> laps / <km> km reproduced as <laps> laps', got: \"{notes}\"");

            int historicalLaps = int.Parse(match.Groups["hist"].Value, CultureInfo.InvariantCulture);
            double distanceKm = double.Parse(match.Groups["km"].Value, CultureInfo.InvariantCulture);
            int statedLaps = int.Parse(match.Groups["laps"].Value, CultureInfo.InvariantCulture);

            Assert.True(round.Laps == statedLaps,
                $"{packId} round {round.Round}: laps={round.Laps} but the notes claim {statedLaps}.");

            var track = Library.Value.Tracks[round.Track.Id];
            int expected = Math.Max(1, (int)Math.Round(
                distanceKm * 1000.0 / track.LengthMeters, MidpointRounding.AwayFromZero));
            Assert.True(expected == round.Laps,
                $"{packId} round {round.Round}: {distanceKm} km over {track.Id} " +
                $"({track.LengthMeters} m) is {expected} laps, but the round has {round.Laps}.");

            Assert.True(round.Laps != historicalLaps || expected == historicalLaps,
                $"{packId} round {round.Round}: placeholder laps equal the historical lap count " +
                $"({historicalLaps}) but the distance math gives {expected}, the historical " +
                "count was copied instead of recomputed.");
        }
    }

    // ---------- formatting ----------

    private static string FormatStructural(PackValidationReport report) =>
        report.Issues.Count == 0
            ? "(no issues)"
            : string.Join("\n", report.Issues.Select(i => $"  {i.Severity}: {i.Message}"));

    private static string FormatContent(PreflightReport report) =>
        report.Issues.Count == 0
            ? "(no issues)"
            : string.Join("\n", report.Issues.Select(i => $"  {i.Severity}: {i.Message}"));
}
