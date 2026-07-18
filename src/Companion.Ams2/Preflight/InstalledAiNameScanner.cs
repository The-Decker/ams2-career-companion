using Companion.Ams2.CustomAi;

namespace Companion.Ams2.Preflight;

/// <summary>The livery names an installed CustomAIDrivers class XML defines, the AUTHORITY
/// for which livery names are valid for that class (Mike's requirement: "the names mod and ai
/// changes must always be primary if found; it has to be found before overwritten"). Every
/// <c>livery_name</c> the installed file declares (base AND per-track entries) is a name the
/// user can already bind in-game, so the preflight must never warn that it "won't bind".</summary>
public sealed record InstalledAiNameSet
{
    /// <summary>The vehicle class this set was scanned for (the CustomAIDrivers filename base,
    /// case-sensitive), e.g. F-Classic_Gen3.</summary>
    public required string VehicleClass { get; init; }

    /// <summary>Every distinct <c>livery_name</c> found in the installed class file, ordinal —
    /// the exact strings the game matches (case- and whitespace-sensitive).</summary>
    public required IReadOnlyCollection<string> LiveryNames { get; init; }

    /// <summary>The class file that was read (null when none was installed for this class).</summary>
    public string? SourceFile { get; init; }

    /// <summary>True when an installed class file was found and parsed (even leniently). When
    /// false the AI file contributed no authoritative names, the preflight falls back to the
    /// skin-override + stock name sets alone.</summary>
    public bool Found => SourceFile is not null;

    public static InstalledAiNameSet Empty(string vehicleClass) => new()
    {
        VehicleClass = vehicleClass,
        LiveryNames = [],
        SourceFile = null,
    };
}

/// <summary>
/// Scans the user's INSTALLED CustomAIDrivers class XML for the livery names it defines, the
/// NAMeS/AI file the game actually runs, and therefore the PRIMARY source of truth for which
/// livery names are valid (locked decision #7, "found before overwritten"). Reuses the same
/// lenient reader as the baseline import (<see cref="CommunityAiReader"/>): community NAMeS
/// files are routinely not well-formed XML, yet the game reads them, so must we. A missing or
/// unreadable file is not an error, it just means the AI file contributes no names and the
/// scan degrades to <see cref="InstalledAiNameSet.Empty"/>.
/// </summary>
public static class InstalledAiNameScanner
{
    /// <summary>Reads <c>&lt;customAiDriversDirectory&gt;\&lt;vehicleClass&gt;.xml</c> and
    /// collects every declared <c>livery_name</c> (base + per-track entries, a per-track-only
    /// name is still a name the game binds). Never throws: an absent or unreadable file yields
    /// an empty, <see cref="InstalledAiNameSet.Found"/>=false set.</summary>
    public static InstalledAiNameSet Scan(string customAiDriversDirectory, string vehicleClass)
    {
        string path = Path.Combine(customAiDriversDirectory, vehicleClass + ".xml");
        var installed = CommunityAiReader.TryReadFile(path);
        if (installed is null)
            return InstalledAiNameSet.Empty(vehicleClass);

        return FromFile(installed, vehicleClass, path);
    }

    /// <summary>Collects the names from an already-parsed installed file, for callers that
    /// have the <see cref="CommunityAiFile"/> in hand (e.g. staging, which reads it anyway).</summary>
    public static InstalledAiNameSet FromFile(CommunityAiFile installed, string vehicleClass, string? sourceFile)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in installed.BaseEntries)
            names.Add(entry.LiveryName);
        foreach (var entry in installed.TrackEntries)
            names.Add(entry.LiveryName);

        return new InstalledAiNameSet
        {
            VehicleClass = vehicleClass,
            LiveryNames = names,
            SourceFile = sourceFile,
        };
    }
}
