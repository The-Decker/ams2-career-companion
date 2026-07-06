using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Companion.ViewModels.Services;
using Companion.ViewModels.Settings;

namespace Companion.ViewModels.Start;

/// <summary>
/// The start screen (app-shell contract): recent careers (JSON MRU in
/// %APPDATA%\AMS2CareerCompanion\recent.json), continue, and new-career entry points.
/// Navigation is event-based — the shell decides what a "continue" or "new career" request
/// means; this viewmodel only owns the MRU list.
/// </summary>
public sealed partial class StartViewModel : ObservableObject
{
    private readonly IRecentCareersStore _store;
    private readonly ISettingsService? _settings;
    private readonly Func<string, bool> _careerFileExists;
    private readonly Action<string> _deleteCareerFile;

    public StartViewModel(
        IRecentCareersStore store,
        ISettingsService? settings = null,
        Func<string, bool>? careerFileExists = null,
        Action<string>? deleteCareerFile = null)
    {
        _store = store;
        _settings = settings;
        _careerFileExists = careerFileExists ?? File.Exists;
        _deleteCareerFile = deleteCareerFile ?? DeleteCareerFileFromDisk;
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

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private RecentCareer? _selectedCareer;

    /// <summary>Failure banner for the "Open career…" picker (a path that is empty, missing, or
    /// would not open). Null when there is nothing to report. The shell surfaces its own
    /// <c>StatusError</c> for open failures once the file reaches the open flow; this covers the
    /// pre-flight cases the VM can answer itself (no dialog, testable).</summary>
    [ObservableProperty]
    private string? _openError;

    /// <summary>Failure banner for "Delete career file…" (a locked or permission-blocked
    /// .ams2career). Null when there is nothing to report. Distinct from <see cref="OpenError"/>
    /// so a delete failure never hides behind (or clobbers) an open-picker message.</summary>
    [ObservableProperty]
    private string? _deleteError;

    public bool HasRecentCareers => RecentCareers.Count > 0;

    /// <summary>Raised with the career file path the user wants to open.</summary>
    public event EventHandler<string>? ContinueRequested;

    /// <summary>Raised when the user wants the new-career wizard.</summary>
    public event EventHandler? NewCareerRequested;

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
        DeleteError = null;
        // Preserve the stored year so an entry re-touched by "Continue" keeps its era art; the
        // shell re-records it with the authoritative summary year once the session opens anyway.
        _store.Touch(career.Path, career.CareerName, career.SeasonYear);
        Refresh();
        ContinueRequested?.Invoke(this, career.Path);
    }

    /// <summary>"Open career…": route an arbitrary <c>.ams2career</c> path (chosen in the view's
    /// file dialog) through the same open-career flow the gallery cards use. The VM only validates
    /// what it can without touching the career database — a blank or non-existent path — and reports
    /// it in <see cref="OpenError"/>; a file that exists but will not open is handled downstream by
    /// the shell's own open-failure banner. Takes the path (not a dialog) so it stays testable.</summary>
    [RelayCommand]
    private void OpenCareer(string? path)
    {
        DeleteError = null; // a stale delete failure shouldn't outlive the user's next action
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

    [RelayCommand]
    private void RemoveRecent(RecentCareer? career)
    {
        if (career is null)
            return;
        DeleteError = null; // the entry the banner referred to may be leaving the gallery
        _store.Remove(career.Path);
        Refresh();
    }

    /// <summary>"Delete career file…": deletes the .ams2career from disk, then drops the MRU
    /// entry. The VIEW confirms first (dialogs are view-layer, same contract as the open picker);
    /// this command is the already-confirmed action. A file that cannot be deleted (locked by an
    /// open session, permissions) reports in <see cref="DeleteError"/> and KEEPS the entry — the
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
            DeleteError = $"Could not delete '{career.CareerName}' — {ex.Message}\n" +
                "If this career is open in the app, close it and try again.";
            return;
        }

        DeleteError = null;
        _store.Remove(career.Path);
        Refresh();
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
    public void RecordCareer(string path, string careerName, int seasonYear = 0)
    {
        _store.Touch(path, careerName, seasonYear);
        Refresh();
    }
}
