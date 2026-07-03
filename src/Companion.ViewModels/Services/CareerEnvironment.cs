using Companion.Ams2;
using Companion.Ams2.ContentLibrary;
using Companion.Ams2.Preflight;

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

    public static CareerEnvironment CreateDefault(string ams2DataDirectory) => new()
    {
        ContentLibrary = Ams2ContentLibrary.Load(ams2DataDirectory),
        LocateInstall = static () => OperatingSystem.IsWindows() ? SteamLocator.FindAms2() : null,
        DocumentsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
    };

    /// <summary>Scans installed skin-pack livery overrides for this machine (install-side and
    /// Documents-side roots). A missing install just narrows the scan — never a failure.</summary>
    public (IReadOnlyList<InstalledLivery> Liveries, IReadOnlyList<string> Warnings) ScanInstalledLiveries(
        Ams2Installation? installation)
    {
        // A nonexistent placeholder root is simply skipped by the scanner.
        string installDirectory = installation?.InstallDirectory
            ?? Path.Combine(DocumentsDirectory, "_no-ams2-install");
        return LiveryOverrideScanner.Scan(
            LiveryOverrideScanner.CandidateOverrideRoots(installDirectory, DocumentsDirectory));
    }
}
