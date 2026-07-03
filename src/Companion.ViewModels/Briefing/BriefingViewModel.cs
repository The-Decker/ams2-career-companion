using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Companion.ViewModels.Services;

namespace Companion.ViewModels.Briefing;

/// <summary>
/// The Race Day briefing screen (app-shell contract): the setup guide as a check-once panel
/// — every in-game setting as an exact string with a per-line copy action, the Stage-grid
/// button with a backup-first outcome banner, and a "modified outside the app" flag driven
/// by an injected <see cref="IFileWatcher"/> on the staged XML. No WPF types: copy raises
/// <see cref="CopyRequested"/> for the view to put on the clipboard, and the watcher's real
/// FileSystemWatcher implementation lives in the App layer.
/// </summary>
public sealed partial class BriefingViewModel : ObservableObject
{
    private readonly ICareerSession _session;
    private readonly IFileWatcher? _watcher;

    public BriefingViewModel(ICareerSession session, IFileWatcher? stagedFileWatcher = null)
    {
        _session = session;
        _watcher = stagedFileWatcher;
        if (_watcher is not null)
            _watcher.Changed += OnWatchedFileChanged;
        Refresh();
    }

    /// <summary>Raised with the exact text the view should copy to the clipboard.</summary>
    public event EventHandler<string>? CopyRequested;

    // ---------- briefing content ----------

    [ObservableProperty]
    private BriefingModel? _briefing;

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string _venueDisplayName = "";

    [ObservableProperty]
    private bool _isPlaceholder;

    [ObservableProperty]
    private string? _setupNotes;

    /// <summary>True once every round has an applied result — there is nothing to brief.</summary>
    public bool SeasonComplete => Briefing is null;

    public ObservableCollection<CopyableSetting> Settings { get; } = [];

    /// <summary>Re-reads the current round's briefing from the session (call after Apply).</summary>
    public void Refresh()
    {
        Briefing = _session.CurrentBriefing();

        Settings.Clear();
        if (Briefing is { } briefing)
        {
            foreach (var setting in briefing.Settings)
                Settings.Add(setting);
            Title = BriefingComposer.ComposeTitle(briefing);
            VenueDisplayName = briefing.VenueDisplayName;
            IsPlaceholder = briefing.IsPlaceholder;
            SetupNotes = briefing.SetupNotes;
        }
        else
        {
            Title = "";
            VenueDisplayName = "";
            IsPlaceholder = false;
            SetupNotes = null;
        }
        OnPropertyChanged(nameof(SeasonComplete));
    }

    // ---------- copy ----------

    [RelayCommand]
    private void Copy(CopyableSetting? setting)
    {
        if (setting is not null)
            CopyRequested?.Invoke(this, setting.Value);
    }

    // ---------- staging ----------

    [ObservableProperty]
    private StageOutcome? _lastStageOutcome;

    [ObservableProperty]
    private bool _stageSucceeded;

    [ObservableProperty]
    private string _stageBanner = "";

    /// <summary>⚠ state: the staged XML changed on disk after we wrote it (the user or
    /// another tool touched it) — re-stage before racing.</summary>
    [ObservableProperty]
    private bool _stagedFileTouchedExternally;

    public IReadOnlyList<string> StageMessages => LastStageOutcome?.Messages ?? [];

    /// <summary>True when the session supports the explicit force-stage escape hatch (staging
    /// over a file the app did not generate, e.g. a curated community NAMeS file).</summary>
    public bool CanForceStage => _session is IForceStaging;

    [RelayCommand]
    private void StageGrid() => RunStage(force: false);

    [RelayCommand]
    private void ForceStageGrid()
    {
        if (_session is IForceStaging forceStaging)
            ApplyOutcome(forceStaging.StageCurrentGrid(force: true));
    }

    private void RunStage(bool force)
    {
        if (force && _session is IForceStaging forceStaging)
            ApplyOutcome(forceStaging.StageCurrentGrid(force: true));
        else
            ApplyOutcome(_session.StageCurrentGrid());
    }

    private void ApplyOutcome(StageOutcome outcome)
    {
        LastStageOutcome = outcome;
        StageSucceeded = outcome.Success;
        StagedFileTouchedExternally = false;
        StageBanner = ComposeBanner(outcome);
        OnPropertyChanged(nameof(StageMessages));

        if (outcome.Success && outcome.WrittenPath is { Length: > 0 } path)
            _watcher?.Watch(path);
        else
            _watcher?.Stop();
    }

    private static string ComposeBanner(StageOutcome outcome)
    {
        if (!outcome.Success)
            return outcome.Messages.Count > 0
                ? $"Staging failed — {outcome.Messages[^1]}"
                : "Staging failed.";

        string written = Path.GetFileName(outcome.WrittenPath) ?? outcome.WrittenPath ?? "";
        return outcome.BackupPath is { Length: > 0 } backup
            ? $"Staged {written} — previous file backed up to {backup}"
            : $"Staged {written} — no previous file, nothing to back up";
    }

    private void OnWatchedFileChanged(object? sender, string path)
    {
        if (LastStageOutcome is { Success: true, WrittenPath: { } written } &&
            string.Equals(path, written, StringComparison.OrdinalIgnoreCase))
        {
            StagedFileTouchedExternally = true;
        }
    }
}
