using Companion.Ams2;
using Companion.Ams2.ContentLibrary;
using Companion.Ams2.Preflight;
using Companion.Ams2.Skins;

namespace Companion.ViewModels.Services;

/// <summary>
/// Everything the career session and wizard need from the machine: the extracted AMS2
/// content library, install detection, the user's Documents folder (livery-override scan
/// root, default pack/career locations), and a clock. Tests construct one directly with an
/// in-memory library and a fake install; the app uses <see cref="CreateDefault"/>.
/// </summary>
public sealed class CareerEnvironment
{
    public required Ams2ContentLibrary ContentLibrary { get; init; }

    /// <summary>Locates the AMS2 install; null when none is found (every consumer must
    /// degrade gracefully — a missing install never crashes a flow).</summary>
    public required Func<Ams2Installation?> LocateInstall { get; init; }

    public required string DocumentsDirectory { get; init; }

    public TimeProvider Clock { get; init; } = TimeProvider.System;

    /// <summary>Directory of the app-shipped career rules JSON (aging curves, archetypes,
    /// headline bank). Optional for flows that never fold a round; applying a result
    /// requires it (<see cref="Rules"/> throws a clear error when it is missing).</summary>
    public string? RulesDirectory { get; init; }

    /// <summary>Directory of the app-shipped historical-results JSON (<c>data/history/&lt;year&gt;.json</c>,
    /// f1db-derived, CC BY 4.0) the History tab shows as "what really happened". Reference content only —
    /// the sim/fold never reads it. Null = the feature is simply absent (the History tab shows only the
    /// player's own career).</summary>
    public string? HistoryDirectory { get; init; }

    /// <summary>Directory of the app-shipped skin-season pointer library
    /// (<c>data/ams2/skin-seasons/&lt;key&gt;/&lt;model&gt;.xml</c>) the Skin Season Manager swaps
    /// between conflicting season skin packs. Null/missing = the manager is simply absent.</summary>
    public string? SkinSeasonsDirectory { get; init; }

    private SkinSeasonLibrary? _skinSeasons;

    /// <summary>The parsed skin-season library, loaded once and cached. Empty when
    /// <see cref="SkinSeasonsDirectory"/> is null or missing.</summary>
    public SkinSeasonLibrary SkinSeasons => _skinSeasons ??=
        SkinSeasonsDirectory is null ? SkinSeasonLibrary.Empty : SkinSeasonLibrary.Load(SkinSeasonsDirectory);

    /// <summary>Season-pack search roots for era-transition discovery (M6 sign-and-continue).
    /// Null = <see cref="PackDiscovery.DefaultSearchRoots"/> (the exe-adjacent packs folder,
    /// then Documents\AMS2CareerCompanion\Packs). A Func so the app can fold in the settings
    /// screen's live custom pack folders (assigned after <see cref="CreateDefault"/>, hence
    /// settable); tests pin a fixed list.</summary>
    public Func<IReadOnlyList<string>>? PackSearchRoots { get; set; }

    /// <summary>The pack search roots to scan right now (see <see cref="PackSearchRoots"/>).</summary>
    public IReadOnlyList<string> ResolvePackSearchRoots() =>
        PackSearchRoots?.Invoke() ?? PackDiscovery.DefaultSearchRoots(DocumentsDirectory);

    private CareerRulesData? _rules;

    /// <summary>The parsed career rules data, loaded once and cached — every fold and season
    /// end consumes the same instances, so live and replay inputs are identical.</summary>
    public CareerRulesData Rules => _rules ??= CareerRulesData.Load(
        RulesDirectory ?? throw new InvalidOperationException(
            "This environment has no rules directory — the data\\rules folder (aging curves, " +
            "archetypes, headlines) is required to apply results."));

    public static CareerEnvironment CreateDefault(string ams2DataDirectory) => new()
    {
        ContentLibrary = Ams2ContentLibrary.Load(ams2DataDirectory),
        LocateInstall = static () => OperatingSystem.IsWindows() ? SteamLocator.FindAms2() : null,
        DocumentsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        RulesDirectory = Path.Combine(
            Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(ams2DataDirectory)) ?? ams2DataDirectory,
            "rules"),
        HistoryDirectory = Path.Combine(
            Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(ams2DataDirectory)) ?? ams2DataDirectory,
            "history"),
        SkinSeasonsDirectory = Path.Combine(ams2DataDirectory, "skin-seasons"),
    };

    /// <summary>Scans installed skin-pack livery overrides for this machine (install-side and
    /// Documents-side roots). A missing install just narrows the scan — never a failure.
    /// Returns the structured result: the UI reports its <see cref="LiveryScanResult.Summary"/>
    /// as one line, with the unreadable files behind a collapsed details section.</summary>
    public LiveryScanResult ScanInstalledLiveries(Ams2Installation? installation)
    {
        // A nonexistent placeholder root is simply skipped by the scanner.
        string installDirectory = installation?.InstallDirectory
            ?? Path.Combine(DocumentsDirectory, "_no-ams2-install");
        return LiveryOverrideScanner.ScanDetailed(
            LiveryOverrideScanner.CandidateOverrideRoots(installDirectory, DocumentsDirectory));
    }

    /// <summary>Scans the user's INSTALLED CustomAIDrivers class file for the livery names it
    /// declares — the PRIMARY authority for which names are valid (locked decision #7, "found
    /// before overwritten"). A missing install or class file yields an empty
    /// (<see cref="InstalledAiNameSet.Found"/>=false) set — the preflight then falls back to
    /// skins + stock alone.</summary>
    public InstalledAiNameSet ScanInstalledAiNames(Ams2Installation? installation, string vehicleClass)
    {
        if (installation is null)
            return InstalledAiNameSet.Empty(vehicleClass);
        return InstalledAiNameScanner.Scan(installation.CustomAiDriversDirectory, vehicleClass);
    }
}
