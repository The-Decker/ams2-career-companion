using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Threading;

namespace Companion.App.Audio;

internal readonly record struct AudioMixSettings(
    bool Enabled,
    double Master,
    double Effects,
    double Music,
    bool MuteEffectsWhenUnfocused)
{
    internal double VolumeFor(AudioBus bus) =>
        Math.Clamp(Master, 0, 1) * Math.Clamp(
            bus == AudioBus.Effects ? Effects : Music,
            0,
            1);
}

/// <summary>Small test seam between the binding-facing player/controller and WPF MediaPlayer.</summary>
internal interface ISoundscapeAudioEngine : IDisposable
{
    /// <summary>Raised only when the selected song reaches its natural end.</summary>
    event EventHandler? MusicEnded;

    /// <summary>Raised when WPF asynchronously rejects an opened music file.</summary>
    event EventHandler? MusicFailed;

    void ApplyMix(AudioMixSettings settings);
    void SetApplicationActive(bool active);
    bool SelectMusic(SoundscapeTrack track, bool play);
    bool PlayMusic();
    void PauseMusic();
    bool PlayEffect(SoundscapeTrack track, double musicDuck = 1.0, TimeSpan? duckDuration = null);
}

/// <summary>
/// WPF-native playback backend for one manual music transport and a bounded SFX pool. It does not
/// know about screens, navigation, career state, or playlist order. Missing/unreadable files simply
/// produce silence and can never prevent the application from starting or progressing.
/// </summary>
internal sealed class WpfAudioEngine : ISoundscapeAudioEngine
{
    private const int OneShotPoolSize = 4;
    private const int DuckReleaseMilliseconds = 260;

    private sealed class OneShotChannel
    {
        internal MediaPlayer Player { get; } = new();

        /// <summary>Identity of the media currently OPEN on this channel. Retained after playback
        /// ends so a repeat of the same cue replays by rewinding instead of paying a synchronous
        /// MediaPlayer.Open (file + codec pipeline init) on the UI thread for every click.</summary>
        internal SoundscapeTrack? Track { get; set; }

        /// <summary>True while a play is in flight. Distinct from <see cref="Track"/>: a finished
        /// channel keeps its media open (Track stays set) but is free to reuse or rewind.</summary>
        internal bool IsBusy { get; set; }

        internal double PendingMusicDuck { get; set; } = 1;
        internal TimeSpan PendingDuckDuration { get; set; }
        internal double ActiveMusicDuck { get; set; } = 1;

        /// <summary>Playback over, media kept open for a same-cue rewind. The duck contribution
        /// ends here; the file handle and codec stay warm.</summary>
        internal void FinishRetainMedia()
        {
            IsBusy = false;
            PendingMusicDuck = 1;
            PendingDuckDuration = TimeSpan.Zero;
            ActiveMusicDuck = 1;
        }

        internal void Reset()
        {
            Player.Stop();
            Player.Close();
            Track = null;
            IsBusy = false;
            PendingMusicDuck = 1;
            PendingDuckDuration = TimeSpan.Zero;
            ActiveMusicDuck = 1;
        }
    }

    private readonly string _baseDirectory;
    private readonly MediaPlayer _music = new();
    private readonly OneShotChannel[] _oneShots = new OneShotChannel[OneShotPoolSize];
    private readonly DispatcherTimer _duckTimer;
    private readonly Stopwatch _duckClock = new();
    private AudioMixSettings _mix = new(true, .8, .7, .4, true);
    private SoundscapeTrack? _musicTrack;
    private bool _applicationActive = true;
    private bool _musicLoaded;
    private bool _playRequested;
    private bool _musicFailureNotified;
    private bool _handlingMusicFailure;
    private double _musicDuck = 1;
    private TimeSpan _duckHoldUntil;
    private TimeSpan? _duckReleaseStartedAt;
    private double _duckReleaseFrom = 1;
    private int _nextOneShot;
    private bool _disposed;

    internal WpfAudioEngine(string? baseDirectory = null)
    {
        _baseDirectory = baseDirectory ?? AppContext.BaseDirectory;
        var createdChannels = new List<OneShotChannel>(OneShotPoolSize);
        DispatcherTimer? timer = null;
        try
        {
            _music.MediaEnded += OnMusicEnded;
            _music.MediaFailed += OnMusicFailed;

            for (int i = 0; i < _oneShots.Length; i++)
            {
                var channel = new OneShotChannel();
                channel.Player.MediaOpened += (_, _) => OnOneShotOpened(channel);
                channel.Player.MediaEnded += (_, _) => OnOneShotFinished(channel);
                channel.Player.MediaFailed += (_, _) => OnOneShotFailed(channel);
                _oneShots[i] = channel;
                createdChannels.Add(channel);
            }

            timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(40),
            };
            timer.Tick += OnDuckTimerTick;
            _duckTimer = timer;
        }
        catch
        {
            if (timer is not null)
            {
                try { timer.Stop(); }
                catch { }
                try { timer.Tick -= OnDuckTimerTick; }
                catch { }
            }
            foreach (OneShotChannel channel in createdChannels)
                TryReset(channel);
            TryCloseMusic();
            throw;
        }
    }

    public event EventHandler? MusicEnded;
    public event EventHandler? MusicFailed;

    internal int ActiveEffectCount => _oneShots.Count(static channel => channel.IsBusy);
    internal double CurrentMusicDuck => _musicDuck;
    internal bool IsDuckActive => _duckTimer.IsEnabled;
    internal bool ShouldRunMusicTransport =>
        _musicLoaded && _playRequested && CanPlayMusic();

    public void ApplyMix(AudioMixSettings settings)
    {
        if (_disposed)
            return;

        _mix = settings;
        RefreshTransport();
        if (!CanPlayEffects())
            SilenceEffectsAndResetDuck();
        ApplyVolumes();
    }

    public void SetApplicationActive(bool active)
    {
        if (_disposed || _applicationActive == active)
            return;

        _applicationActive = active;
        RefreshTransport();
        if (!CanPlayEffects())
        {
            SilenceEffectsAndResetDuck();
            ApplyVolumes();
        }
    }

    public bool SelectMusic(SoundscapeTrack track, bool play)
    {
        if (_disposed)
            return false;

        ArgumentNullException.ThrowIfNull(track);
        TryCloseMusic();
        _musicFailureNotified = false;
        if (!TryResolve(track, out Uri? source))
        {
            _playRequested = false;
            return false;
        }

        try
        {
            _playRequested = play;
            _music.Open(source);
            if (_musicFailureNotified)
                return false;
            _music.Position = TimeSpan.Zero;
            _musicTrack = track;
            _musicLoaded = true;
            ApplyVolumes();
            RefreshTransport();
            return _musicLoaded;
        }
        catch (Exception ex) when (IsMediaFailure(ex))
        {
            HandleMusicFailure();
            return false;
        }
    }

    public bool PlayMusic()
    {
        if (_disposed || !_musicLoaded)
            return false;

        _playRequested = true;
        RefreshTransport();
        // Muted or disabled audio preserves the user's play intent and leaves the media loaded;
        // only an actual transport exception turns this into a failed play request.
        return _musicLoaded;
    }

    public void PauseMusic()
    {
        if (_disposed)
            return;

        _playRequested = false;
        try { _music.Pause(); }
        catch (Exception ex) when (IsMediaFailure(ex))
        {
            HandleMusicFailure();
        }
    }

    public bool PlayEffect(
        SoundscapeTrack track,
        double musicDuck = 1.0,
        TimeSpan? duckDuration = null)
    {
        double volume = VolumeFor(track);
        if (_disposed || !CanPlayEffects() || volume <= 0)
            return false;

        // Rewind-first channel pick: an idle channel already holding this exact media replays by
        // rewinding, no file probe, no MediaPlayer.Open. A repeat cue (rapid result entry, bucket
        // gestures) pays the open cost only on its first play of each variant.
        OneShotChannel? channel = _oneShots.FirstOrDefault(
            candidate => !candidate.IsBusy && candidate.Track == track);
        bool rewind = channel is not null;

        if (!rewind)
        {
            if (!TryResolve(track, out Uri? source))
                return false;

            channel = _oneShots.FirstOrDefault(static candidate => !candidate.IsBusy)
                ?? _oneShots[_nextOneShot++ % _oneShots.Length];
            if (channel.IsBusy)
                OnOneShotFinished(channel); // stealing a live voice: release its duck contribution

            channel.Track = track;
            channel.PendingMusicDuck = Math.Clamp(musicDuck, 0, 1);
            channel.PendingDuckDuration = duckDuration.GetValueOrDefault();
            try
            {
                channel.Player.Open(source); // implicitly closes any retained media
                channel.IsBusy = true;
                channel.Player.Volume = volume;
                channel.Player.Play();
                return true;
            }
            catch (Exception ex) when (IsMediaFailure(ex))
            {
                TryReset(channel);
                return false;
            }
        }

        try
        {
            // The media is known-good (it played from this channel before), so the duck applies
            // synchronously, MediaOpened only fires for a fresh Open.
            channel!.IsBusy = true;
            channel.Player.Position = TimeSpan.Zero;
            channel.Player.Volume = volume;
            channel.Player.Play();
            BeginDuck(channel, Math.Clamp(musicDuck, 0, 1), duckDuration.GetValueOrDefault());
            return true;
        }
        catch (Exception ex) when (IsMediaFailure(ex))
        {
            TryReset(channel);
            return false;
        }
    }

    private void OnOneShotOpened(OneShotChannel channel)
    {
        if (_disposed || !channel.IsBusy)
            return;

        double duck = channel.PendingMusicDuck;
        TimeSpan hold = channel.PendingDuckDuration;
        channel.PendingMusicDuck = 1;
        channel.PendingDuckDuration = TimeSpan.Zero;
        BeginDuck(channel, duck, hold);
    }

    /// <summary>Attenuates the music bus while a ducking one-shot is audible. Shared by the
    /// fresh-open path (deferred to MediaOpened, so a failed open never ducks into silence) and
    /// the rewind path (applied synchronously, the retained media is already proven open).</summary>
    private void BeginDuck(OneShotChannel channel, double duck, TimeSpan hold)
    {
        if (_disposed || duck >= 1 || hold <= TimeSpan.Zero)
            return;

        channel.ActiveMusicDuck = duck;
        if (!_duckClock.IsRunning)
            _duckClock.Restart();

        _musicDuck = Math.Min(_musicDuck, duck);
        _duckHoldUntil = ExtendDuckHold(_duckHoldUntil, _duckClock.Elapsed, hold);
        _duckReleaseStartedAt = null;
        _duckReleaseFrom = _musicDuck;
        if (!_duckTimer.IsEnabled)
            _duckTimer.Start();
        ApplyVolumes();
    }

    private void OnOneShotFinished(OneShotChannel channel)
    {
        bool contributedDuck = channel.ActiveMusicDuck < 1;
        // The media stays open: a repeat of the same cue rewinds instead of reopening.
        channel.FinishRetainMedia();
        ReleaseDuckIfLast(channel, contributedDuck);
    }

    private void OnOneShotFailed(OneShotChannel channel)
    {
        bool contributedDuck = channel.ActiveMusicDuck < 1;
        TryReset(channel); // a failed media is never retained for rewind
        ReleaseDuckIfLast(channel, contributedDuck);
    }

    private void ReleaseDuckIfLast(OneShotChannel channel, bool contributedDuck)
    {
        if (_disposed || !contributedDuck || !_duckClock.IsRunning ||
            _oneShots.Any(static candidate => candidate.ActiveMusicDuck < 1))
            return;

        // The only audible ducking voice ended or failed. Release from the current attenuation
        // instead of holding a silent hole for the cue's originally estimated duration.
        TimeSpan now = _duckClock.Elapsed;
        if (_duckHoldUntil > now)
            _duckHoldUntil = now;
        _duckReleaseStartedAt = null;
        _duckReleaseFrom = _musicDuck;
        if (!_duckTimer.IsEnabled)
            _duckTimer.Start();
    }

    private void OnMusicEnded(object? sender, EventArgs e)
    {
        if (_disposed || !_playRequested)
            return;

        // The playlist owner selects and starts the following item. Clearing this request first
        // means an absent listener leaves the completed player in a genuinely stopped state.
        _playRequested = false;
        RaiseFailSafe(MusicEnded);
    }

    private void OnMusicFailed(object? sender, ExceptionEventArgs e)
    {
        if (!_disposed)
            HandleMusicFailure();
    }

    private void OnDuckTimerTick(object? sender, EventArgs e)
    {
        if (_disposed)
            return;

        TimeSpan elapsed = _duckClock.Elapsed;
        if (elapsed < _duckHoldUntil)
            return;

        if (_duckReleaseStartedAt is null)
        {
            _duckReleaseStartedAt = elapsed;
            _duckReleaseFrom = _musicDuck;
        }

        TimeSpan releaseElapsed = elapsed - _duckReleaseStartedAt.Value;
        double releaseProgress = Math.Clamp(
            releaseElapsed.TotalMilliseconds / DuckReleaseMilliseconds,
            0,
            1);
        _musicDuck = InterpolateDuckRelease(_duckReleaseFrom, releaseElapsed);
        ApplyVolumes();
        if (releaseProgress < 1)
            return;

        ResetDuckState();
    }

    private void RefreshTransport()
    {
        if (!_musicLoaded)
            return;

        try
        {
            if (ShouldRunMusicTransport)
                _music.Play();
            else
                _music.Pause();
        }
        catch (Exception ex) when (IsMediaFailure(ex))
        {
            HandleMusicFailure();
        }
    }

    /// <summary>Collapses synchronous and asynchronous MediaPlayer transport failures into one
    /// stopped state and one notification for the current selection.</summary>
    private void HandleMusicFailure()
    {
        if (_handlingMusicFailure ||
            (_musicFailureNotified && !_musicLoaded && !_playRequested && _musicTrack is null))
            return;

        _handlingMusicFailure = true;
        bool notify = !_musicFailureNotified;
        _musicFailureNotified = true;
        _musicLoaded = false;
        _playRequested = false;
        _musicTrack = null;
        try { _music.Close(); }
        catch { }
        finally { _handlingMusicFailure = false; }

        if (notify)
            RaiseFailSafe(MusicFailed);
    }

    private void ApplyVolumes()
    {
        try
        {
            _music.Volume = Math.Clamp(
                _mix.VolumeFor(AudioBus.Music) * (_musicTrack?.Gain ?? 1) * _musicDuck,
                0,
                1);
        }
        catch (Exception ex) when (IsMediaFailure(ex)) { }

        foreach (OneShotChannel channel in _oneShots)
        {
            if (channel.Track is { } track)
                channel.Player.Volume = VolumeFor(track);
        }
    }

    private double VolumeFor(SoundscapeTrack track) =>
        Math.Clamp(_mix.VolumeFor(track.Bus) * Math.Clamp(track.Gain, 0, 1), 0, 1);

    private bool CanPlayMusic() => _mix.Enabled;

    private bool CanPlayEffects() =>
        _mix.Enabled &&
        (_applicationActive || !_mix.MuteEffectsWhenUnfocused) &&
        _mix.VolumeFor(AudioBus.Effects) > 0;

    private void SilenceEffectsAndResetDuck()
    {
        foreach (OneShotChannel channel in _oneShots)
            TryReset(channel);
        ResetDuckState();
    }

    private void ResetDuckState()
    {
        _musicDuck = 1;
        _duckHoldUntil = TimeSpan.Zero;
        _duckReleaseStartedAt = null;
        _duckReleaseFrom = 1;
        _duckClock.Reset();
        _duckTimer.Stop();
    }

    internal static TimeSpan ExtendDuckHold(TimeSpan currentHoldUntil, TimeSpan now, TimeSpan hold)
    {
        TimeSpan proposed = now + hold;
        return proposed > currentHoldUntil ? proposed : currentHoldUntil;
    }

    internal static double InterpolateDuckRelease(double releaseFrom, TimeSpan elapsed)
    {
        double progress = Math.Clamp(
            elapsed.TotalMilliseconds / DuckReleaseMilliseconds,
            0,
            1);
        double start = Math.Clamp(releaseFrom, 0, 1);
        return start + ((1 - start) * progress);
    }

    private bool TryResolve(SoundscapeTrack track, out Uri? source)
    {
        try
        {
            string relative = track.RelativePath.Replace('/', Path.DirectorySeparatorChar);
            string fullPath = Path.GetFullPath(Path.Combine(_baseDirectory, relative));
            string root = Path.GetFullPath(_baseDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
            {
                source = null;
                return false;
            }

            source = new Uri(fullPath, UriKind.Absolute);
            return true;
        }
        catch (Exception ex) when (IsMediaFailure(ex))
        {
            source = null;
            return false;
        }
    }

    private void TryCloseMusic()
    {
        _musicLoaded = false;
        _playRequested = false;
        _musicTrack = null;
        try { _music.Stop(); }
        catch { }
        try { _music.Close(); }
        catch { }
    }

    private void RaiseFailSafe(EventHandler? handlers)
    {
        if (handlers is null)
            return;

        foreach (EventHandler handler in handlers.GetInvocationList().Cast<EventHandler>())
        {
            try { handler(this, EventArgs.Empty); }
            catch
            {
                // Decorative transport notifications must not escape WPF media callbacks.
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        try { _duckTimer.Stop(); }
        catch { }
        try { _duckTimer.Tick -= OnDuckTimerTick; }
        catch { }
        try { _music.MediaEnded -= OnMusicEnded; }
        catch { }
        try { _music.MediaFailed -= OnMusicFailed; }
        catch { }
        TryCloseMusic();
        foreach (OneShotChannel channel in _oneShots)
            TryReset(channel);
    }

    private static void TryReset(OneShotChannel? channel)
    {
        if (channel is null)
            return;
        try { channel.Reset(); }
        catch
        {
            // Best-effort release: audio teardown must never escape into app lifetime.
        }
    }

    private static bool IsMediaFailure(Exception ex) => ex is
        InvalidOperationException or
        ArgumentException or
        IOException or
        UnauthorizedAccessException or
        NotSupportedException or
        COMException;
}
