using System.Collections.ObjectModel;
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

    public StartViewModel(IRecentCareersStore store, ISettingsService? settings = null)
    {
        _store = store;
        _settings = settings;
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

        _store.Touch(career.Path, career.CareerName);
        Refresh();
        ContinueRequested?.Invoke(this, career.Path);
    }

    [RelayCommand]
    private void NewCareer() => NewCareerRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void RemoveRecent(RecentCareer? career)
    {
        if (career is null)
            return;
        _store.Remove(career.Path);
        Refresh();
    }

    /// <summary>Records a career in the MRU (called by the shell after a create or open).</summary>
    public void RecordCareer(string path, string careerName)
    {
        _store.Touch(path, careerName);
        Refresh();
    }
}
