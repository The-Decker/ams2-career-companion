using Companion.Ams2.ContentLibrary;
using Companion.Core.Packs;

namespace Companion.Ams2.Preflight;

/// <summary>One mod track a pack's alternates require, and whether it is installed (its loose folder
/// <c>&lt;install&gt;\Tracks\&lt;tag&gt;\</c> exists, base/DLC tracks are packed, mods are loose).</summary>
public sealed record RequiredModTrack(string Tag, string DisplayName, bool Installed);

/// <summary>
/// The pre-season "RockyTM track switch" check: which community MOD tracks a pack's OPT-IN alternates
/// need, and whether each is installed. The rule (Mike): alternates apply ONLY when the player ticks
/// them on AND every required mod is present; otherwise the season generates on its base/DLC defaults
/// and no mod is touched. This surfaces the missing list for the wizard and gates the creation-time
/// <see cref="AlternateTrackTransform"/>.
/// </summary>
public static class AlternateTrackPreflight
{
    /// <summary>The distinct mod tracks the pack's <c>track.alternate</c> entries reference, each with
    /// a display name (from the library) and whether its install folder exists. A null/empty
    /// <paramref name="installDirectory"/> can verify nothing, so every entry reports not-installed.</summary>
    public static IReadOnlyList<RequiredModTrack> RequiredModTracks(
        SeasonPack pack, Ams2ContentLibrary library, string? installDirectory)
    {
        var byTag = new SortedDictionary<string, RequiredModTrack>(StringComparer.Ordinal);
        foreach (var round in pack.Season.Rounds)
        {
            if (round.Track.Alternate is not { } alt || byTag.ContainsKey(alt.Id))
                continue;

            string name = library.Tracks.TryGetValue(alt.Id, out var track) && track.TrackName is { Length: > 0 } tn
                ? tn
                : alt.Id;
            bool installed = installDirectory is { Length: > 0 } dir
                && Directory.Exists(Path.Combine(dir, "Tracks", alt.Id));
            byTag[alt.Id] = new RequiredModTrack(alt.Id, name, installed);
        }
        return byTag.Values.ToList();
    }

    /// <summary>The required mod tracks that are NOT installed (empty = all present).</summary>
    public static IReadOnlyList<RequiredModTrack> MissingModTracks(
        SeasonPack pack, Ams2ContentLibrary library, string? installDirectory) =>
        RequiredModTracks(pack, library, installDirectory).Where(t => !t.Installed).ToList();

    /// <summary>True only when the pack HAS alternates AND every required mod track is installed —
    /// the condition under which the creation-time transform may swap to the alternates.</summary>
    public static bool CanApplyAlternates(
        SeasonPack pack, Ams2ContentLibrary library, string? installDirectory)
    {
        var required = RequiredModTracks(pack, library, installDirectory);
        return required.Count > 0 && required.All(t => t.Installed);
    }
}
