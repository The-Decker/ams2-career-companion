using Companion.Core.Career;
using Companion.ViewModels.Settings;
using System.Runtime.CompilerServices;

namespace Companion.App.Audio;

/// <summary>
/// App-lifetime owner for the manual player and opt-in interaction SFX. This class intentionally
/// has no ShellViewModel dependency; it cannot observe or react to navigation or career outcomes.
/// The one era input is the pushed era skin (<see cref="SetEraSkin"/>): the App TELLS the
/// controller which period medium voices the immersive cues, the same one-way push model as
/// settings, and the controller never watches navigation or career state to learn it.
/// </summary>
internal sealed class AppAudioController : IDisposable
{
    private readonly ISettingsService _settings;
    private readonly ISoundscapeAudioEngine _engine;
    private readonly SoundscapeCatalog _catalog;
    private readonly TimeProvider _clock;
    private readonly ConditionalWeakTable<object, PlaybackHistory> _sourceHistories = new();
    private readonly PlaybackHistory _unscopedHistory = new();
    private EraMedium? _eraSkin;
    private bool _disposed;

    internal AppAudioController(ISettingsService settings)
        : this(settings, new WpfAudioEngine())
    {
    }

    /// <summary>Injection seam used by render/unit tests. The controller owns the supplied engine
    /// and disposes it with the app-audio lifetime.</summary>
    internal AppAudioController(
        ISettingsService settings,
        ISoundscapeAudioEngine engine,
        SoundscapeCatalog? catalog = null,
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(engine);

        _settings = settings;
        _engine = engine;
        _catalog = catalog ?? new SoundscapeCatalog();
        _clock = clock ?? TimeProvider.System;

        try
        {
            _engine.ApplyMix(ToMix(settings.Current));
            Player = new MusicPlayerViewModel(_engine, settings, SoundscapeCatalog.Playlist);
            _settings.Changed += OnSettingsChanged;
        }
        catch
        {
            try { _engine.Dispose(); }
            catch { }
            throw;
        }
    }

    internal MusicPlayerViewModel Player { get; }

    internal void SetApplicationActive(bool active)
    {
        if (!_disposed)
            _engine.SetApplicationActive(active);
    }

    /// <summary>TOLD the era skin for the immersive cues (era-theming-assets brief, Workstream B).
    /// Null selects the era-neutral base set (menus, gallery, no active career). Timbre only, never
    /// triggering: this changes how Navigate/Confirm/Back/SeatConfirm are voiced, never when or
    /// whether any cue fires, and cooldown, dedupe, mix, and ducking are identical for every skin.</summary>
    internal void SetEraSkin(EraMedium? skin)
    {
        if (!_disposed)
            _eraSkin = skin;
    }

    /// <summary>Plays only an explicitly requested SoundAssist cue. There are no implicit state,
    /// screen, outcome, or milestone requests anywhere in this controller.</summary>
    internal void PlayEffect(SoundEffectCue cue) => PlayEffect(cue, source: null);

    /// <summary>Button-originated history is scoped to the weak source identity. This retains
    /// same-control anti-chatter while allowing two different deliberate buttons to sound even
    /// when their routed Click events arrive within the same cooldown window.</summary>
    internal void PlayEffect(SoundEffectCue cue, object? source)
    {
        if (_disposed || cue == SoundEffectCue.None ||
            !_catalog.TryGetEffect(cue, out SoundEffectDefinition definition))
            return;

        PlaybackHistory history = source is null
            ? _unscopedHistory
            : _sourceHistories.GetValue(source, static _ => new PlaybackHistory());
        DateTimeOffset now = _clock.GetUtcNow();
        if (history.LastCue.TryGetValue(cue, out DateTimeOffset lastCue) &&
            IsWithin(now, lastCue, definition.Cooldown))
            return;
        if (history.LastDedupeGroup.TryGetValue(
                definition.DedupeGroup, out DateTimeOffset lastGroup) &&
            IsWithin(now, lastGroup, definition.DedupeWindow))
            return;

        if (!_engine.PlayEffect(
                _catalog.NextEffect(cue, definition, _eraSkin),
                definition.MusicDuck,
                definition.DuckDuration))
            return;

        // A muted, unfocused, missing, or failed cue must not consume the next audible click's
        // cooldown. The backend is the authority on whether playback was actually accepted.
        history.LastCue[cue] = now;
        history.LastDedupeGroup[definition.DedupeGroup] = now;
    }

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        if (_disposed)
            return;

        _engine.ApplyMix(ToMix(settings));
        Player.ApplyVolumePercent(settings.MusicVolumePercent);
    }

    private static AudioMixSettings ToMix(AppSettings settings) => new(
        settings.SoundEnabled,
        settings.MasterVolumePercent / 100.0,
        settings.EffectsVolumePercent / 100.0,
        settings.MusicVolumePercent / 100.0,
        settings.MuteWhenUnfocused);

    private static bool IsWithin(DateTimeOffset now, DateTimeOffset previous, TimeSpan window)
    {
        TimeSpan elapsed = now - previous;
        return elapsed >= TimeSpan.Zero && elapsed < window;
    }

    private sealed class PlaybackHistory
    {
        internal Dictionary<SoundEffectCue, DateTimeOffset> LastCue { get; } = [];
        internal Dictionary<string, DateTimeOffset> LastDedupeGroup { get; } =
            new(StringComparer.Ordinal);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        try { _settings.Changed -= OnSettingsChanged; }
        catch { }
        try { Player.Dispose(); }
        catch { }
        try { _engine.Dispose(); }
        catch
        {
            // Presentation-only teardown must never interfere with app/session disposal.
        }
    }
}
