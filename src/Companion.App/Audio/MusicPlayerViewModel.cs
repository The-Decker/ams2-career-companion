using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Companion.ViewModels.Settings;

namespace Companion.App.Audio;

/// <summary>
/// Binding contract for the app-lifetime manual music player. It deliberately has no shell or
/// screen dependency: selecting a song, pressing transport controls, or reaching a natural track
/// end are the only events that can change music.
/// </summary>
public sealed partial class MusicPlayerViewModel : ObservableObject, IDisposable
{
    private readonly ISoundscapeAudioEngine _engine;
    private readonly ISettingsService _settings;
    private MusicPlaylistTrack _selectedTrack;
    private bool _isPlaying;
    private int _volumePercent;
    private bool _disposed;

    internal MusicPlayerViewModel(
        ISoundscapeAudioEngine engine,
        ISettingsService settings,
        IReadOnlyList<MusicPlaylistTrack> tracks)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(tracks);
        if (tracks.Count == 0)
            throw new ArgumentException("The manual music playlist cannot be empty.", nameof(tracks));

        _engine = engine;
        _settings = settings;
        Tracks = tracks;
        _selectedTrack = tracks[0];
        _volumePercent = Math.Clamp(
            settings.Current.MusicVolumePercent,
            AppSettings.MinVolumePercent,
            AppSettings.MaxVolumePercent);

        _engine.MusicEnded += OnMusicEnded;
        _engine.MusicFailed += OnMusicFailed;
        // Keep the first track selected in the UI, but do not open media or touch the audio device
        // during app startup. The first explicit transport or track-selection action loads it.
    }

    public IReadOnlyList<MusicPlaylistTrack> Tracks { get; }

    public MusicPlaylistTrack SelectedTrack
    {
        get => _selectedTrack;
        set
        {
            if (_disposed || value is null || !Tracks.Contains(value) ||
                !SetProperty(ref _selectedTrack, value))
                return;

            OnPropertyChanged(nameof(TrackTitle));
            bool loaded = _engine.SelectMusic(
                SoundscapeCatalog.MusicTrack(value),
                play: IsPlaying);
            if (!loaded && IsPlaying)
                IsPlaying = false;
        }
    }

    public string TrackTitle => SelectedTrack.Title;

    public bool IsPlaying
    {
        get => _isPlaying;
        private set => SetProperty(ref _isPlaying, value);
    }

    /// <summary>Music-bus level in the persisted app settings. This intentionally excludes master
    /// volume: the compact header slider controls the music player itself.</summary>
    public int VolumePercent
    {
        get => _volumePercent;
        set
        {
            int normalized = Math.Clamp(
                value,
                AppSettings.MinVolumePercent,
                AppSettings.MaxVolumePercent);
            if (_disposed || !SetProperty(ref _volumePercent, normalized))
                return;

            _settings.Update(current => current with { MusicVolumePercent = normalized });
        }
    }

    [RelayCommand]
    private void TogglePlay()
    {
        if (_disposed)
            return;

        if (IsPlaying)
        {
            _engine.PauseMusic();
            IsPlaying = false;
            return;
        }

        if (_engine.PlayMusic())
        {
            IsPlaying = true;
            return;
        }

        // MediaPlayer unloads a selection after an asynchronous decode/transport failure. A later
        // Play click should recover the manual player in place instead of forcing the user to pick
        // another ComboBox item first. Retry the visible selection, then use the same missing-track
        // walk as Next when the current loose MP3 cannot be reopened.
        if (TrySelectAndCommit(SelectedTrack, play: true))
            return;

        int current = IndexOfSelectedTrack();
        for (int step = 1; step < Tracks.Count; step++)
        {
            MusicPlaylistTrack candidate = Tracks[(current + step) % Tracks.Count];
            if (TrySelectAndCommit(candidate, play: true))
                return;
        }

        IsPlaying = false;
    }

    [RelayCommand]
    private void Previous() => ChangeTrack(-1, forcePlay: false);

    [RelayCommand]
    private void Next() => ChangeTrack(1, forcePlay: false);

    private void OnMusicEnded(object? sender, EventArgs e) => ChangeTrack(1, forcePlay: true);

    private void OnMusicFailed(object? sender, EventArgs e) => IsPlaying = false;

    private void ChangeTrack(int offset, bool forcePlay)
    {
        if (_disposed)
            return;

        bool shouldPlay = forcePlay || IsPlaying;
        int current = IndexOfSelectedTrack();

        // A missing loose MP3 is not allowed to wedge the transport. Walk at most one complete
        // playlist cycle in the requested direction and settle on the first track the backend can
        // open. Including the current item as the final candidate lets a partially copied install
        // continue with its only available song without looping forever.
        for (int step = 1; step <= Tracks.Count; step++)
        {
            int next = (current + (offset * step)) % Tracks.Count;
            if (next < 0)
                next += Tracks.Count;

            if (TrySelectAndCommit(Tracks[next], shouldPlay))
                return;
        }

        IsPlaying = false;
    }

    private bool TrySelectAndCommit(MusicPlaylistTrack track, bool play)
    {
        if (!_engine.SelectMusic(SoundscapeCatalog.MusicTrack(track), play))
            return false;

        if (!ReferenceEquals(_selectedTrack, track))
        {
            _selectedTrack = track;
            OnPropertyChanged(nameof(SelectedTrack));
            OnPropertyChanged(nameof(TrackTitle));
        }
        IsPlaying = play;
        return true;
    }

    private int IndexOfSelectedTrack()
    {
        for (int i = 0; i < Tracks.Count; i++)
        {
            if (ReferenceEquals(Tracks[i], SelectedTrack))
                return i;
        }
        return 0;
    }

    internal void ApplyVolumePercent(int value)
    {
        int normalized = Math.Clamp(
            value,
            AppSettings.MinVolumePercent,
            AppSettings.MaxVolumePercent);
        SetProperty(ref _volumePercent, normalized, nameof(VolumePercent));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _engine.MusicEnded -= OnMusicEnded;
        _engine.MusicFailed -= OnMusicFailed;
    }
}
