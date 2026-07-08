namespace Companion.ViewModels.Services;

/// <summary>
/// The app's ONE reusable user-image convention: drop images into a well-known
/// <c>data/ams2/&lt;kind&gt;-art/</c> folder beside the exe and the matching view resolves them by a
/// key, most-specific-first, falling back cleanly when nothing is present. It is the shared primitive
/// behind the gallery's era art (<see cref="EraArtResolver"/>, keyed by season year), track-layout
/// thumbnails (keyed by track id) and any future story/event art — all "folder + key + resolver with
/// fallback", mirroring the untracked era-art pattern (user art is never committed).
///
/// <para>Pure ordering plus a single <see cref="File.Exists"/> probe — no image is decoded here — so
/// it lives in the ViewModels layer (not <c>Companion.Core</c>, which forbids I/O) and unit-tests with
/// plain temp files. The WPF bitmap load happens in the view-layer converter.</para>
/// </summary>
public static class UserImageResolver
{
    /// <summary>Accepted image extensions, in preference order. WPF's <c>BitmapImage</c> decodes all
    /// three natively; <c>.jpg</c> is listed before <c>.png</c> because real photographs are usually
    /// JPEGs. (<see cref="EraArtResolver"/> keeps its own narrower two-extension list for its exact
    /// documented candidate order; new keyed assets use this fuller set.)</summary>
    public static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png"];

    /// <summary>The first candidate file under <paramref name="directory"/> that exists on disk, as a
    /// full path; <c>null</c> when the directory is blank/missing or holds none of the candidates (the
    /// caller then shows its placeholder). <paramref name="candidateFileNames"/> are relative file
    /// names in most-specific-first order — the first that exists wins.</summary>
    public static string? FirstExisting(string? directory, IEnumerable<string> candidateFileNames)
    {
        if (string.IsNullOrEmpty(directory))
            return null;
        foreach (var name in candidateFileNames)
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;
            string full = Path.Combine(directory, name);
            if (File.Exists(full))
                return full;
        }
        return null;
    }

    /// <summary>The candidate file names for a simple keyed asset (e.g. a track id) — <c>&lt;key&gt;.jpg</c>,
    /// <c>&lt;key&gt;.jpeg</c>, <c>&lt;key&gt;.png</c>, in preference order. Returns nothing for a blank key.</summary>
    public static IReadOnlyList<string> CandidatesForKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return [];
        string trimmed = key.Trim();
        return ImageExtensions.Select(ext => trimmed + ext).ToList();
    }

    /// <summary>Resolves a simple keyed asset (track thumbnail, story image, …) under
    /// <paramref name="directory"/> to a full path, or <c>null</c> when none exists.</summary>
    public static string? ResolveByKey(string? directory, string? key) =>
        FirstExisting(directory, CandidatesForKey(key));
}
