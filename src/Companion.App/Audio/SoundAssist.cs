using System.Threading;
using System.Windows;
using System.Windows.Controls.Primitives;

namespace Companion.App.Audio;

/// <summary>
/// Opt-in presentation bridge for short interaction sounds. Buttons use the attached
/// <c>SoundAssist.Cue</c> property; explicitly audited non-button behaviors use <see cref="Play"/>.
/// Both routes request semantic cues from the App-owned audio controller.
/// </summary>
/// <remarks>
/// There is intentionally no app-wide click handler. Tables, sliders, text fields, and other
/// high-frequency tools stay quiet; result-entry bucket gestures are the current non-button opt-in.
/// </remarks>
public static class SoundAssist
{
    private static Action<SoundEffectCue, object?>? _player;

    public static readonly DependencyProperty CueProperty = DependencyProperty.RegisterAttached(
        "Cue",
        typeof(SoundEffectCue),
        typeof(SoundAssist),
        new FrameworkPropertyMetadata(SoundEffectCue.None, OnCueChanged));

    /// <summary>Suppresses an attached cue when the click is intentionally a no-op. The command
    /// still runs normally; this only keeps decorative audio tied to a meaningful state change.</summary>
    public static readonly DependencyProperty SuppressWhenProperty = DependencyProperty.RegisterAttached(
        "SuppressWhen",
        typeof(bool),
        typeof(SoundAssist),
        new FrameworkPropertyMetadata(false));

    public static void SetCue(DependencyObject element, SoundEffectCue value) =>
        element.SetValue(CueProperty, value);

    public static SoundEffectCue GetCue(DependencyObject element) =>
        (SoundEffectCue)element.GetValue(CueProperty);

    public static void SetSuppressWhen(DependencyObject element, bool value) =>
        element.SetValue(SuppressWhenProperty, value);

    public static bool GetSuppressWhen(DependencyObject element) =>
        (bool)element.GetValue(SuppressWhenProperty);

    /// <summary>Connects the presentation behavior to the current App-owned audio lifetime.</summary>
    internal static void Connect(Action<SoundEffectCue, object?> player) =>
        Interlocked.Exchange(ref _player, player);

    /// <summary>Compatibility seam for tests and explicitly source-free presentation callers.</summary>
    internal static void Connect(Action<SoundEffectCue> player) =>
        Connect((cue, _) => player(cue));

    /// <summary>Clears the playback target before the controller and MediaPlayers are disposed.</summary>
    internal static void Disconnect() =>
        Interlocked.Exchange(ref _player, null);

    /// <summary>Requests a semantic cue from an explicitly opted-in non-button interaction.</summary>
    internal static void Play(SoundEffectCue cue)
    {
        if (cue == SoundEffectCue.None)
            return;

        try
        {
            Volatile.Read(ref _player)?.Invoke(cue, null);
        }
        catch
        {
            // Audio is decorative. Drag/drop and other presentation behavior must remain usable
            // when the backend is absent or rejects a file.
        }
    }

    private static void OnCueChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not ButtonBase button)
            return;

        // Read the current attached value in the event handler, so changing one non-None cue to
        // another cannot leave a stale delegate or attach twice.
        button.Click -= OnButtonClick;
        if ((SoundEffectCue)e.NewValue != SoundEffectCue.None)
            button.Click += OnButtonClick;
    }

    private static void OnButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not DependencyObject element || GetSuppressWhen(element))
            return;

        SoundEffectCue cue = GetCue(element);
        if (cue == SoundEffectCue.None)
            return;

        try
        {
            // The weak source identity lets the controller keep anti-chatter protection for one
            // control without suppressing a different button clicked in the same instant.
            Volatile.Read(ref _player)?.Invoke(cue, element);
        }
        catch
        {
            // Audio remains decorative and must never interrupt the command behind this click.
        }
    }
}
