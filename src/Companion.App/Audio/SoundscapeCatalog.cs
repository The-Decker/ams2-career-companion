namespace Companion.App.Audio;

internal enum AudioBus
{
    Effects,
    Music,
}

/// <summary>
/// Stable identifiers for the deliberately sparse, opt-in interface sounds. The enum is used by
/// <see cref="SoundAssist"/> so views request a meaning instead of knowing an asset filename.
/// Nothing in this catalog observes navigation or career state.
/// </summary>
public enum SoundEffectCue
{
    None,
    Navigate,
    Confirm,
    SeatConfirm,
    Back,
    BucketPickup,
    BucketPlace,
    Warning,
    Destructive,
    SkillUnlock,
}

/// <summary>A track exposed by the manual player. The path remains an implementation detail while
/// the human title is intentionally public and binding-friendly.</summary>
public sealed class MusicPlaylistTrack
{
    internal MusicPlaylistTrack(string title, string relativePath, double gainDecibels)
    {
        Title = title;
        RelativePath = relativePath;
        GainDecibels = Math.Min(0, gainDecibels);
        PlaybackGain = Math.Clamp(Math.Pow(10, GainDecibels / 20.0), 0, 1);
    }

    public string Title { get; }

    /// <summary>Non-positive mastering trim applied only at playback; the source MP3 is unchanged.</summary>
    public double GainDecibels { get; }

    /// <summary>Linear form of <see cref="GainDecibels"/> used by the audio engine.</summary>
    public double PlaybackGain { get; }

    internal string RelativePath { get; }

    public override string ToString() => Title;
}

/// <summary>A loose, exe-adjacent media asset. WPF MediaPlayer streams these files because it
/// cannot read MP3s or WAVs from the single-file bundle.</summary>
internal sealed record SoundscapeTrack(
    string RelativePath,
    AudioBus Bus,
    double Gain = 1.0);

/// <summary>Mix and anti-chatter policy for one SFX cue. Cooldown suppresses repeated requests for
/// the same cue and the dedupe group prevents related clicks raised together from stacking.</summary>
internal sealed record SoundEffectDefinition(
    IReadOnlyList<SoundscapeTrack> Variants,
    TimeSpan Cooldown,
    string DedupeGroup,
    TimeSpan DedupeWindow,
    double MusicDuck = 1.0,
    TimeSpan DuckDuration = default);

/// <summary>The canonical manual playlist and opt-in interaction-SFX map. Music order is stable,
/// starts with The Long Climb, and has no relationship to the current screen. Per-song playback
/// trims preserve the authored MP3s while bringing the audited program near -14.5 LUFS and keeping
/// decoded peaks at or below the mastering ceiling.</summary>
internal sealed class SoundscapeCatalog
{
    private const string MusicRoot = "Assets/Audio/Music";
    private const string SfxRoot = "Assets/Audio/Sfx";

    private readonly Dictionary<SoundEffectCue, int> _nextEffectVariant = [];

    internal static IReadOnlyList<MusicPlaylistTrack> Playlist { get; } =
    [
        Song("The Long Climb", "the-long-climb.mp3", -1.49),
        Song("Pitwall at Dusk", "pitwall-at-dusk.mp3", -0.99),
        Song("Amber Pitlane", "amber-pitlane.mp3", -1.62),
        Song("Telemetry at Twilight", "telemetry-at-twilight.mp3", -1.34),
        Song("Night Shift", "night-shift.mp3", -2.13),
        Song("Race Control", "race-control.mp3", -1.37),
        Song("Grid Locked", "grid-locked.mp3", -0.26),
        Song("Formation Hold", "formation-hold.mp3", -2.01),
        Song("After the Flag", "after-the-flag.mp3", -0.60),
        Song("Cooling Lap", "cooling-lap.mp3", -0.90),
        Song("Empty Grandstands", "empty-grandstands.mp3", -1.30),
        Song("Super Monaco Grand Prix Intro", "intro-smgp.mp3", -1.70),
        Song("First Light Briefing", "first-light-briefing.mp3", -0.20),
        Song("Morning Question", "morning-question.mp3", -1.60),
        Song("Lights in the Distance", "lights-in-the-distance.mp3", -1.60),
        Song("Open Table", "open-table.mp3", -2.10),
        Song("Strategy Room", "strategy-room.mp3", -1.50),
        Song("Open Ledger", "open-ledger.mp3", -1.10),
        Song("Rain Before Rhythm", "rain-before-rhythm.mp3", -1.50),
        Song("Wet Line Reverie", "wet-line-reverie.mp3", -0.70),
        Song("Golden Lap", "golden-lap.mp3", -1.60),
        Song("Injury", "injury.mp3", -0.50),
        Song("Injury / Death", "injury-death.mp3", -2.10),
    ];

    private static readonly IReadOnlyDictionary<SoundEffectCue, SoundEffectDefinition> Effects =
        new Dictionary<SoundEffectCue, SoundEffectDefinition>
        {
            [SoundEffectCue.Navigate] = Sfx("navigate.wav", .50, 24, "navigation", 12),
            [SoundEffectCue.Confirm] = Sfx("commit.wav", .60, 90, "action", 45),
            [SoundEffectCue.SeatConfirm] = Sfx("seat-confirm.wav", .62, 120, "seat", 60),
            [SoundEffectCue.Back] = Sfx("back.wav", .50, 90, "navigation", 45),
            [SoundEffectCue.BucketPickup] = Sfx("bucket-pickup.wav", .40, 45, "bucket", 25),
            [SoundEffectCue.BucketPlace] = Sfx("bucket-place.wav", .46, 55, "bucket", 30),
            [SoundEffectCue.Warning] = Sfx("warning.wav", .70, 420, "outcome", 140,
                musicDuck: .80, duckMilliseconds: 500),
            [SoundEffectCue.Destructive] = Sfx("destructive.wav", .72, 650, "critical", 240,
                musicDuck: .68, duckMilliseconds: 700),
            [SoundEffectCue.SkillUnlock] = Sfx("skill-unlock.wav", .72, 750, "progression", 300,
                musicDuck: .70, duckMilliseconds: 800),
        };

    internal bool TryGetEffect(SoundEffectCue cue, out SoundEffectDefinition definition) =>
        Effects.TryGetValue(cue, out definition!);

    internal SoundscapeTrack NextEffect(SoundEffectCue cue, SoundEffectDefinition definition)
    {
        int index = _nextEffectVariant.GetValueOrDefault(cue);
        _nextEffectVariant[cue] = (index + 1) % definition.Variants.Count;
        return definition.Variants[index % definition.Variants.Count];
    }

    internal static SoundscapeTrack MusicTrack(MusicPlaylistTrack track) =>
        new(track.RelativePath, AudioBus.Music, track.PlaybackGain);

    internal static IReadOnlyCollection<string> DeclaredRelativePaths => Playlist
        .Select(static track => track.RelativePath)
        .Concat(Effects.Values.SelectMany(static effect => effect.Variants)
            .Select(static track => track.RelativePath))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static MusicPlaylistTrack Song(string title, string fileName, double gainDecibels) =>
        new(title, $"{MusicRoot}/{fileName}", gainDecibels);

    private static SoundEffectDefinition Sfx(
        string fileName,
        double gain,
        int cooldownMilliseconds,
        string dedupeGroup,
        int dedupeMilliseconds,
        double musicDuck = 1.0,
        int duckMilliseconds = 0) =>
        new(
            [new SoundscapeTrack($"{SfxRoot}/{fileName}", AudioBus.Effects, Gain: gain)],
            TimeSpan.FromMilliseconds(cooldownMilliseconds),
            dedupeGroup,
            TimeSpan.FromMilliseconds(dedupeMilliseconds),
            Math.Clamp(musicDuck, 0, 1),
            TimeSpan.FromMilliseconds(duckMilliseconds));
}
