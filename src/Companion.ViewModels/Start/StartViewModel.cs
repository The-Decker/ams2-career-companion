using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Companion.Core.Career;
using Companion.Data;
using Companion.ViewModels.Services;
using Companion.ViewModels.Settings;

namespace Companion.ViewModels.Start;

/// <summary>
/// The start screen (app-shell contract): recent careers (JSON MRU in
/// %APPDATA%\AMS2CareerCompanion\recent.json), continue, and new-career entry points.
/// Navigation is event-based, the shell decides what a "continue" or "new career" request
/// means; this viewmodel only owns the MRU list.
/// </summary>
public sealed partial class StartViewModel : ObservableObject
{
    private static readonly IReadOnlyList<CareerModeEntry> AlphaCareerModes = Array.AsReadOnly(
    [
        new CareerModeEntry
        {
            Id = CareerExperienceModes.GrandPrixDynasty,
            DisplayName = "Grand Prix Dynasty",
            Tagline = "Build a legacy through racing history.",
            Description =
                "Begin the faithful historical championship path. The current driver career is " +
                "playable now; owner and team management will expand in later Alpha passes.",
            PersistenceSummary =
                "One save follows one chronological timeline. Its installed faithful seasons are pinned when you begin.",
            AvailabilityLabel = "PLAYABLE FIRST PASS",
            IsAvailable = true,
        },
        new CareerModeEntry
        {
            Id = CareerExperienceModes.Smgp,
            DisplayName = "Super Monaco GP",
            Tagline = "Climb the grid. Beat your rival. Take the crown.",
            Description =
                "Play the complete SEGA-inspired rival and seat-swap campaign across 17 seasons.",
            PersistenceSummary = "One save follows the full authored Super Monaco GP campaign.",
            AvailabilityLabel = "PLAYABLE NOW",
            IsAvailable = true,
        },
        new CareerModeEntry
        {
            Id = CareerExperienceModes.RacingPassport,
            DisplayName = "Racing Passport",
            Tagline = "Choose a season. Take a seat. Go racing.",
            Description =
                "Choose any installed faithful historical series, replace one driver, and race " +
                "the complete season.",
            PersistenceSummary =
                "Each Passport season is an independent save focused on racing, results, standings, " +
                "and championship history.",
            AvailabilityLabel = "PLAYABLE NOW",
            IsAvailable = true,
        },
    ]);

    private readonly IRecentCareersStore _store;
    private readonly ISettingsService? _settings;
    private readonly Func<string, bool> _careerFileExists;
    private readonly Action<string> _deleteCareerFile;
    private readonly Action<string, string, string> _renameCareerFile;
    private readonly Action<string, string, string> _duplicateCareerFile;

    public StartViewModel(
        IRecentCareersStore store,
        ISettingsService? settings = null,
        Func<string, bool>? careerFileExists = null,
        Action<string>? deleteCareerFile = null,
        Action<string, string, string>? renameCareerFile = null,
        Action<string, string, string>? duplicateCareerFile = null)
    {
        _store = store;
        _settings = settings;
        _careerFileExists = careerFileExists ?? File.Exists;
        _deleteCareerFile = deleteCareerFile ?? DeleteCareerFileFromDisk;
        _renameCareerFile = renameCareerFile ?? CareerFileStore.Rename;
        _duplicateCareerFile = duplicateCareerFile ?? CareerFileStore.Duplicate;
        if (_settings is not null)
            _settings.Changed += OnSettingsChanged;
        Refresh();
    }

    /// <summary>The immersion master switch (career-hub-design.md decision 7): when off, the
    /// career gallery hides its era-medium labels and falls back to neutral cards. Reads live
    /// from settings; defaults on when no settings service is wired.</summary>
    public bool EraThemingEnabled => _settings?.Current.EraThemingEnabled ?? true;

    private void OnSettingsChanged(object? sender, AppSettings settings) =>
        OnPropertyChanged(nameof(EraThemingEnabled));

    public ObservableCollection<RecentCareer> RecentCareers { get; } = [];

    /// <summary>
    /// The three locked Alpha 1.0 experiences in main-menu order. The Passport card is deliberately
    /// visible but unavailable until its one-database activity ledger and thread-local state exist.
    /// </summary>
    public IReadOnlyList<CareerModeEntry> CareerModes => AlphaCareerModes;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private RecentCareer? _selectedCareer;

    /// <summary>Failure banner for the "Open career…" picker (a path that is empty, missing, or
    /// would not open). Null when there is nothing to report. The shell surfaces its own
    /// <c>StatusError</c> for open failures once the file reaches the open flow; this covers the
    /// pre-flight cases the VM can answer itself (no dialog, testable).</summary>
    [ObservableProperty]
    private string? _openError;

    /// <summary>Failure banner for the gallery's file operations, delete/rename/duplicate on a
    /// locked or permission-blocked .ams2career, or an invalid rename target. Null when there is
    /// nothing to report; cleared whenever the user moves on to another action. Distinct from
    /// <see cref="OpenError"/> so a file-operation failure never hides behind (or clobbers) an
    /// open-picker message.</summary>
    [ObservableProperty]
    private string? _galleryError;

    public bool HasRecentCareers => RecentCareers.Count > 0;

    /// <summary>Raised with the career file path the user wants to open.</summary>
    public event EventHandler<string>? ContinueRequested;

    /// <summary>Raised when the user wants the new-career wizard.</summary>
    public event EventHandler? NewCareerRequested;

    /// <summary>Raised with the stable mode id selected from <see cref="CareerModes"/>.</summary>
    public event EventHandler<CareerModeRequestedEventArgs>? CareerModeRequested;

    public void Refresh()
    {
        RecentCareers.Clear();
        foreach (var career in _store.Load())
            RecentCareers.Add(career);
        OnPropertyChanged(nameof(HasRecentCareers));
    }

    private bool CanContinue(RecentCareer? career) => (career ?? SelectedCareer) is not null;

    [RelayCommand(CanExecute = nameof(CanContinue))]
    private void Continue(RecentCareer? career)
    {
        career ??= SelectedCareer;
        if (career is null)
            return;

        OpenError = null;
        GalleryError = null;
        // Preserve the stored year so an entry re-touched by "Continue" keeps its era art; the
        // shell re-records it with the authoritative summary year once the session opens anyway.
        _store.Touch(career.Path, career.CareerName, career.SeasonYear);
        Refresh();
        ContinueRequested?.Invoke(this, career.Path);
    }

    /// <summary>"Open career…": route an arbitrary <c>.ams2career</c> path (chosen in the view's
    /// file dialog) through the same open-career flow the gallery cards use. The VM only validates
    /// what it can without touching the career database, a blank or non-existent path, and reports
    /// it in <see cref="OpenError"/>; a file that exists but will not open is handled downstream by
    /// the shell's own open-failure banner. Takes the path (not a dialog) so it stays testable.</summary>
    [RelayCommand]
    private void OpenCareer(string? path)
    {
        GalleryError = null; // a stale file-operation failure shouldn't outlive the user's next action
        if (string.IsNullOrWhiteSpace(path))
        {
            OpenError = "No career file was selected.";
            return;
        }
        if (!_careerFileExists(path))
        {
            OpenError = $"That career file no longer exists:\n{path}";
            return;
        }

        OpenError = null;
        ContinueRequested?.Invoke(this, path);
    }

    [RelayCommand]
    private void NewCareer() => NewCareerRequested?.Invoke(this, EventArgs.Empty);

    private bool CanStartCareerMode(object? parameter) =>
        ResolveCareerMode(parameter)?.IsAvailable == true;

    /// <summary>
    /// Starts an available mode card. Accepts either the bound <see cref="CareerModeEntry"/> or its
    /// stable id so Views can use the whole card or a simple string command parameter. The method
    /// repeats the availability guard because commands can be invoked directly in tests/code even
    /// when a UI would already have disabled the button.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStartCareerMode))]
    private void StartCareerMode(object? parameter)
    {
        var mode = ResolveCareerMode(parameter);
        if (mode?.IsAvailable != true)
            return;

        CareerModeRequested?.Invoke(this, new CareerModeRequestedEventArgs(mode.Id));
    }

    private static CareerModeEntry? ResolveCareerMode(object? parameter)
    {
        string? id = parameter switch
        {
            CareerModeEntry entry => entry.Id,
            string value => value,
            _ => null,
        };
        return AlphaCareerModes.FirstOrDefault(mode =>
            string.Equals(mode.Id, id, StringComparison.Ordinal));
    }

    [RelayCommand]
    private void RemoveRecent(RecentCareer? career)
    {
        if (career is null)
            return;
        GalleryError = null; // the entry the banner referred to may be leaving the gallery
        _store.Remove(career.Path);
        Refresh();
    }

    /// <summary>"Delete career file…": deletes the .ams2career from disk, then drops the MRU
    /// entry. The VIEW confirms first (dialogs are view-layer, same contract as the open picker);
    /// this command is the already-confirmed action. A file that cannot be deleted (locked by an
    /// open session, permissions) reports in <see cref="GalleryError"/> and KEEPS the entry, the
    /// career still exists on disk. A file that is already gone still drops the entry.</summary>
    [RelayCommand]
    private void DeleteRecent(RecentCareer? career)
    {
        if (career is null)
            return;

        try
        {
            _deleteCareerFile(career.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            GalleryError = $"Could not delete '{career.CareerName}', {ex.Message}\n" +
                "If this career is open in the app, close it and try again.";
            return;
        }

        GalleryError = null;
        _store.Remove(career.Path);
        Refresh();
    }

    /// <summary>"Rename career…": renames the .ams2career on disk (filename follows the sanitized
    /// display name; skipped when only the display name changes) and rewrites the stored name
    /// inside the file, then re-records the MRU entry. The VIEW collects the new name (dialogs are
    /// view-layer); this method is the already-collected action, a plain method (not a command)
    /// because it takes two arguments. Failures report in <see cref="GalleryError"/> and keep the
    /// entry untouched.</summary>
    public void RenameRecent(RecentCareer? career, string? newName)
    {
        if (career is null)
            return;

        newName = newName?.Trim();
        if (string.IsNullOrEmpty(newName))
        {
            GalleryError = "A career name can't be empty.";
            return;
        }
        if (string.Equals(newName, career.CareerName, StringComparison.Ordinal))
        {
            GalleryError = null;
            return;
        }

        string directory = Path.GetDirectoryName(career.Path) ?? string.Empty;
        string newPath = Path.Combine(directory, SanitizeFileName(newName) + ".ams2career");
        bool samePath = string.Equals(newPath, career.Path, StringComparison.OrdinalIgnoreCase);
        if (!samePath && _careerFileExists(newPath))
        {
            GalleryError = $"A career file named '{Path.GetFileName(newPath)}' already exists here.";
            return;
        }

        try
        {
            _renameCareerFile(career.Path, newPath, newName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            GalleryError = $"Could not rename '{career.CareerName}', {ex.Message}\n" +
                "If this career is open in the app, close it and try again.";
            return;
        }

        GalleryError = null;
        _store.Remove(career.Path);
        _store.Touch(newPath, newName, career.SeasonYear);
        Refresh();
    }

    /// <summary>"Duplicate career": copies the .ams2career beside the original as
    /// "&lt;name&gt; (copy)" (then "(copy 2)"… on collision), rewrites the copy's stored name,
    /// and records it in the MRU. Non-destructive, so no confirmation. Failures report in
    /// <see cref="GalleryError"/>.</summary>
    [RelayCommand]
    private void DuplicateRecent(RecentCareer? career)
    {
        if (career is null)
            return;

        if (!_careerFileExists(career.Path))
        {
            GalleryError = $"That career file no longer exists:\n{career.Path}";
            return;
        }

        string directory = Path.GetDirectoryName(career.Path) ?? string.Empty;
        string copyName = $"{career.CareerName} (copy)";
        string copyPath = Path.Combine(directory, SanitizeFileName(copyName) + ".ams2career");
        for (int i = 2; _careerFileExists(copyPath); i++)
        {
            copyName = $"{career.CareerName} (copy {i})";
            copyPath = Path.Combine(directory, SanitizeFileName(copyName) + ".ams2career");
        }

        try
        {
            _duplicateCareerFile(career.Path, copyPath, copyName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            GalleryError = $"Could not duplicate '{career.CareerName}', {ex.Message}";
            return;
        }

        GalleryError = null;
        _store.Touch(copyPath, copyName, career.SeasonYear);
        Refresh();
    }

    /// <summary>Same filename discipline as the wizard's career creation: invalid filename
    /// characters become '_', an all-invalid name falls back to "career".</summary>
    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        string cleaned = new(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return cleaned.Length == 0 ? "career" : cleaned;
    }

    /// <summary>Default delete: the career file itself, plus best-effort cleanup of the SQLite
    /// sidecars (-wal/-shm) a crash can leave beside it. Only the main file's failure surfaces —
    /// a stuck sidecar never blocks the delete the user asked for.</summary>
    private static void DeleteCareerFileFromDisk(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
        foreach (string sidecar in new[] { path + "-wal", path + "-shm" })
        {
            try
            {
                if (File.Exists(sidecar))
                    File.Delete(sidecar);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>Records a career in the MRU (called by the shell after a create or open).
    /// <paramref name="seasonYear"/> is the career's stored season year
    /// (<see cref="Services.CareerSummary.SeasonYear"/>) so the gallery card resolves its era art
    /// from the authoritative year rather than the name.</summary>
    public void RecordCareer(string path, string careerName, int seasonYear = 0, string? careerStyle = null,
        string? terminalState = null)
    {
        _store.Touch(path, careerName, seasonYear, careerStyle, terminalState);
        Refresh();
    }

    /// <summary>"Set card image…" / "Clear card image": records (a non-blank <paramref name="imagePath"/>)
    /// or clears (a blank one) the user-chosen gallery image for a career. The VIEW picks the file
    /// (dialogs are view-layer, same contract as the open picker); this method is the already-chosen
    /// action so it stays unit-testable. Point-to-file, the image is referenced, not copied, so a
    /// moved/deleted file simply reverts the card to the year's era art.</summary>
    public void SetCareerImage(RecentCareer? career, string? imagePath)
    {
        if (career is null)
            return;
        GalleryError = null;
        _store.SetCustomImage(career.Path, imagePath);
        Refresh();
    }
}
