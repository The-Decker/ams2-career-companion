using Companion.Ams2.ContentLibrary;
using Companion.Ams2.Preflight;
using Companion.Core.Packs;

namespace Companion.Ams2.Packs;

/// <summary>
/// Content-dependent season-pack validation (season-pack-format.md "Validation on import"
/// items 2-4 and 6's content half): the checks that need the extracted AMS2 content library
/// and an installed-skin scan, which Core must never see. Structural checks (id integrity,
/// calendar, points system, coverage, double-binding) live in
/// <c>Companion.Core.Packs.PackStructuralValidator</c> — run both on import.
/// </summary>
public static class PackContentValidator
{
    public static PreflightReport Validate(
        SeasonPack pack,
        Ams2ContentLibrary library,
        IReadOnlyCollection<InstalledLivery> installedLiveries,
        InstalledAiNameSet? installedAiNames = null)
    {
        var issues = new List<PreflightIssue>();

        bool classExists = CheckClass(pack.Season, library, issues);
        CheckTracksAndAiCaps(pack.Season, library, issues);
        CheckLiveries(pack, library, installedLiveries, installedAiNames, issues);
        CheckTeamCars(pack, library, classExists, issues);

        return new PreflightReport { Issues = issues };
    }

    // ---------- item 2: ams2Class exists with exact casing ----------

    private static bool CheckClass(SeasonDefinition season, Ams2ContentLibrary library, List<PreflightIssue> issues)
    {
        if (library.Classes.ContainsKey(season.Ams2Class))
            return true;

        var caseInsensitive = library.Classes.Keys.FirstOrDefault(k =>
            string.Equals(k, season.Ams2Class, StringComparison.OrdinalIgnoreCase));

        issues.Add(Error(caseInsensitive is not null
            ? $"ams2Class '{season.Ams2Class}' does not match the game's casing '{caseInsensitive}' — " +
              "class names are case-sensitive (the CustomAIDrivers filename IS the binding)."
            : $"ams2Class '{season.Ams2Class}' is not in the content library " +
              $"(extracted from {library.ExtractedFrom})."));
        return false;
    }

    // ---------- item 3: track ids + fallbacks exist; opponents + 1 <= venue AI cap ----------

    private static void CheckTracksAndAiCaps(
        SeasonDefinition season, Ams2ContentLibrary library, List<PreflightIssue> issues)
    {
        foreach (var round in season.Rounds)
        {
            string where = $"Round {round.Round} ({round.Name})";
            int? grid = round.SetupGuide is { } guide ? guide.Session.Opponents + 1 : null;

            // The historical grid (when present) is the authoritative total-cars figure; it must
            // also fit the venue AI cap. size = min(historical starters, cap) at authoring time,
            // so a size above the cap is an authoring/data bug.
            int? gridSize = round.Grid?.Size;

            // Primary venue: existence is an error, cap violation is an error.
            if (!library.Tracks.TryGetValue(round.Track.Id, out var track))
            {
                issues.Add(Error(MissingTrackMessage($"{where} track id", round.Track.Id, library)));
            }
            else
            {
                if (grid > track.MaxAiParticipants)
                {
                    issues.Add(Error(
                        $"{where}: setupGuide grid of {grid} (opponents + player) exceeds " +
                        $"{track.TrackName ?? track.Id}'s AI cap of {track.MaxAiParticipants} — " +
                        "the game will fill fewer cars than the entry list."));
                }

                if (gridSize > track.MaxAiParticipants)
                {
                    issues.Add(Error(
                        $"{where}: grid.size {gridSize} exceeds {track.TrackName ?? track.Id}'s AI cap of " +
                        $"{track.MaxAiParticipants} — size must be min(historical starters, the venue's Max AI)."));
                }
            }

            // Fallback venues: existence is still an error (a dangling fallback id is an
            // authoring bug); a cap violation is only a warning — the fallback is only
            // driven when the primary layout is missing locally.
            foreach (var fallbackId in round.Track.Fallbacks)
            {
                if (!library.Tracks.TryGetValue(fallbackId, out var fallback))
                {
                    issues.Add(Error(MissingTrackMessage($"{where} fallback track id", fallbackId, library)));
                }
                else if (grid > fallback.MaxAiParticipants)
                {
                    issues.Add(Warning(
                        $"{where}: fallback {fallback.TrackName ?? fallback.Id} caps AI at " +
                        $"{fallback.MaxAiParticipants}, below the setupGuide grid of {grid} " +
                        "(opponents + player) — the grid will be short if this fallback is used."));
                }
            }
        }
    }

    private static string MissingTrackMessage(string what, string trackId, Ams2ContentLibrary library)
    {
        var caseInsensitive = library.Tracks.Keys.FirstOrDefault(k =>
            string.Equals(k, trackId, StringComparison.OrdinalIgnoreCase));
        return caseInsensitive is not null
            ? $"{what} '{trackId}' differs from library id '{caseInsensitive}' in case — track ids are case-sensitive."
            : $"{what} '{trackId}' is not in the track library.";
    }

    // ---------- item 4: livery bindings against installed overrides + stock names ----------

    private static void CheckLiveries(
        SeasonPack pack,
        Ams2ContentLibrary library,
        IReadOnlyCollection<InstalledLivery> installedLiveries,
        InstalledAiNameSet? installedAiNames,
        List<PreflightIssue> issues)
    {
        string ams2Class = pack.Season.Ams2Class;

        // PRIMARY: names the installed NAMeS/AI file for this class defines ("found before
        // overwritten"). A name it declares binds in-game whatever the skin state — never warn.
        var aiNames = installedAiNames is { LiveryNames.Count: > 0 }
            ? installedAiNames.LiveryNames.ToHashSet(StringComparer.Ordinal)
            : [];

        var skins = installedLiveries.Select(l => l.Name).ToHashSet(StringComparer.Ordinal);
        var stock = library.Liveries.TryGetValue(ams2Class, out var entry)
            ? entry.StockLib1563.ToHashSet(StringComparer.Ordinal)
            : [];

        var known = new HashSet<string>(aiNames, StringComparer.Ordinal);
        known.UnionWith(skins);
        known.UnionWith(stock);

        var bound = pack.Entries.Select(e => e.Ams2LiveryName)
            .Concat(pack.Season.Rounds.SelectMany(r => r.GuestEntries).Select(g => g.Ams2LiveryName))
            .Distinct(StringComparer.Ordinal);

        if (known.Count == 0)
        {
            issues.Add(Warning(
                $"No livery reference data for class {ams2Class} (no installed AI file, no installed " +
                $"overrides scanned, no stock library entry) — livery bindings cannot be verified. {RequiredSkinPacks(pack.Manifest)}"));
            return;
        }

        foreach (var livery in bound)
        {
            // (1) The installed AI file defines this name — it binds. The skin may fall back to
            // default; that is at most an INFO note, never a warning (user manages skins).
            if (aiNames.Contains(livery))
            {
                if (!skins.Contains(livery) && !stock.Contains(livery))
                    issues.Add(Info(
                        $"Livery '{livery}' is defined by your installed {ams2Class} AI file — the name binds. " +
                        "No matching skin was scanned, so it may fall back to the default skin; " +
                        "manage skins with the pack's own selector."));
                continue;
            }

            // (2) A skin override or stock name also binds.
            if (known.Contains(livery))
                continue;

            var nearMiss = known.FirstOrDefault(k =>
                string.Equals(k, livery, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(k.Trim(), livery.Trim(), StringComparison.Ordinal));

            issues.Add(Warning(nearMiss is not null
                ? $"Livery '{livery}' does not exactly match installed/known livery '{nearMiss}' " +
                  "(case or whitespace differs — the binding is exact-match)."
                : $"Livery '{livery}' was not found in your installed {ams2Class} AI file, installed skin " +
                  $"overrides, or known stock names — the entry will not bind. {RequiredSkinPacks(pack.Manifest)}"));
        }
    }

    private static string RequiredSkinPacks(PackManifest manifest)
    {
        var skinPacks = manifest.Requires.SkinPacks;
        if (skinPacks.Count == 0)
            return "The pack manifest declares no required skin packs.";

        var names = skinPacks.Select(p => p.Url is { Length: > 0 } url ? $"'{p.Name}' ({url})" : $"'{p.Name}'");
        return $"Install the required skin pack(s): {string.Join(", ", names)}.";
    }

    // ---------- item 6 (content half): team cars exist and belong to the pack's class ----------

    private static void CheckTeamCars(
        SeasonPack pack, Ams2ContentLibrary library, bool classExists, List<PreflightIssue> issues)
    {
        foreach (var team in pack.Teams)
        {
            foreach (var vehicleId in team.CarVehicleIds.Distinct(StringComparer.Ordinal))
            {
                if (!library.Vehicles.TryGetValue(vehicleId, out var vehicle))
                {
                    var caseInsensitive = library.Vehicles.Keys.FirstOrDefault(k =>
                        string.Equals(k, vehicleId, StringComparison.OrdinalIgnoreCase));
                    issues.Add(Error(caseInsensitive is not null
                        ? $"Team '{team.Id}' car '{vehicleId}' differs from library vehicle id " +
                          $"'{caseInsensitive}' in case — vehicle ids are case-sensitive."
                        : $"Team '{team.Id}' car '{vehicleId}' is not in the vehicle library."));
                    continue;
                }

                // Only meaningful when ams2Class itself resolved — otherwise every car would
                // repeat the already-reported class error.
                if (classExists &&
                    !string.Equals(vehicle.VehicleClass, pack.Season.Ams2Class, StringComparison.Ordinal))
                {
                    issues.Add(Error(
                        $"Team '{team.Id}' car '{vehicleId}' is in class '{vehicle.VehicleClass}', " +
                        $"not the pack's ams2Class '{pack.Season.Ams2Class}'."));
                }
            }
        }
    }

    // ---------- helpers ----------

    private static PreflightIssue Error(string message) =>
        new() { Severity = PreflightSeverity.Error, Message = message };

    private static PreflightIssue Warning(string message) =>
        new() { Severity = PreflightSeverity.Warning, Message = message };

    private static PreflightIssue Info(string message) =>
        new() { Severity = PreflightSeverity.Info, Message = message };
}
