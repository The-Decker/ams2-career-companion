using Companion.Core.Career;

namespace Companion.ViewModels.Services;

/// <summary>
/// Resolves the "drop-in" era-art image for a career (career-hub-design.md §11 — the picture-rich
/// gallery's per-era image slot). Images live in <c>data/ams2/era-art/</c>; a season's card shows
/// the most specific one that exists on disk, falling back to a coloured era placeholder when none
/// does. The ordering is pure and deterministic; the only I/O is a <see cref="File.Exists"/> probe,
/// which is why this lives in the ViewModels layer and not in <c>Companion.Core</c> ("no I/O").
///
/// <para>Precedence (most specific first): a year-specific file — <c>1967.jpg</c> / <c>1967.png</c>
/// — beats the era-medium file — <c>telegram.jpg</c> / <c>fax.jpg</c> / <c>email.jpg</c> (and their
/// <c>.png</c> variants). So a hand-picked 1967 photo wins over the generic telegram-era image.</para>
/// </summary>
public static class EraArtResolver
{
    /// <summary>Accepted image extensions, in preference order (JPEG before PNG at the same
    /// specificity — real historical photos are usually JPEGs).</summary>
    private static readonly string[] Extensions = [".jpg", ".png"];

    /// <summary>The ordered candidate file names (relative to the era-art directory) for a season
    /// year, most-specific first: the year file (<c>1967.jpg</c>, <c>1967.png</c>) then the
    /// era-medium file (<c>telegram.jpg</c>, <c>telegram.png</c>). Pure — no filesystem access —
    /// so it unit-tests without any files on disk.</summary>
    public static IReadOnlyList<string> CandidateFileNames(int year)
    {
        // The medium basename is just the era enum lowercased: telegram / fax / email.
        string medium = EraThemes.ForYear(year).Medium.ToString().ToLowerInvariant();
        var names = new List<string>(Extensions.Length * 2);
        foreach (var ext in Extensions)
            names.Add(year.ToString(System.Globalization.CultureInfo.InvariantCulture) + ext);
        foreach (var ext in Extensions)
            names.Add(medium + ext);
        return names;
    }

    /// <summary>The first candidate under <paramref name="eraArtDirectory"/> that exists on disk,
    /// as a full path; <c>null</c> when the directory is missing or holds none of the candidates
    /// (the caller then shows the coloured era placeholder). <paramref name="eraArtDirectory"/> is
    /// the folder that holds the images (e.g. <c>{BaseDirectory}\data\ams2\era-art</c>).</summary>
    public static string? Resolve(string eraArtDirectory, int year)
    {
        if (string.IsNullOrEmpty(eraArtDirectory))
            return null;
        foreach (var name in CandidateFileNames(year))
        {
            string full = Path.Combine(eraArtDirectory, name);
            if (File.Exists(full))
                return full;
        }
        return null;
    }

    /// <summary>Resolves era-art for a career whose year is read out of its name (e.g. "Formula One
    /// 1967"), the same way the gallery colours a card without opening the career file. Returns
    /// <c>null</c> when the text carries no plausible year, or when no image exists for it.</summary>
    public static string? ResolveForText(string eraArtDirectory, string? careerText)
    {
        int? year = YearFromText(careerText);
        return year is int y ? Resolve(eraArtDirectory, y) : null;
    }

    /// <summary>The season year implied by any text containing a 4-digit 19xx/20xx year, or
    /// <c>null</c>. Uses the same regex contract as <see cref="EraThemes.FromText"/> so the accent,
    /// the label and the image all key off the same parsed year.</summary>
    public static int? YearFromText(string? careerText)
    {
        if (string.IsNullOrEmpty(careerText))
            return null;
        var match = System.Text.RegularExpressions.Regex.Match(careerText, @"\b(19|20)\d{2}\b");
        return match.Success && int.TryParse(match.Value, out int year) ? year : null;
    }

    /// <summary>The season year the gallery should skin an MRU entry by. The robust path is the
    /// entry's STORED <see cref="RecentCareer.SeasonYear"/> (populated when the career is created or
    /// opened). A legacy entry persisted before that field existed has a <c>0</c> stored year — for
    /// those the resolver falls back to reading a 4-digit year out of the career NAME (the old, fragile
    /// behaviour), and finally to <c>null</c> (a neutral placeholder card) when the name carries none.
    /// <c>0</c> is treated as "missing" because it is the JSON read-with-default AND never a real
    /// season year.</summary>
    public static int? YearForEntry(RecentCareer entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.SeasonYear > 0 ? entry.SeasonYear : YearFromText(entry.CareerName);
    }
}
