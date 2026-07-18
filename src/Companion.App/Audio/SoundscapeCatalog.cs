using Companion.Core.Career;

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
    TimeSpan DuckDuration = default)
{
    /// <summary>Optional era re-voicings of this cue keyed by period medium (era-theming-assets
    /// brief, Workstream B: timbre only, never triggering). The base <see cref="Variants"/> list
    /// stays the era-neutral default and the fallback for any medium without its own set. Gain,
    /// cooldown, dedupe, and duck policy are shared by every voicing of the cue.</summary>
    public IReadOnlyDictionary<EraMedium, IReadOnlyList<SoundscapeTrack>> EraVariants { get; init; } =
        new Dictionary<EraMedium, IReadOnlyList<SoundscapeTrack>>();

    /// <summary>The variant set for the pushed era skin: the medium's own voicings when present,
    /// otherwise the era-neutral base set (null skin = menus, gallery, no active career).</summary>
    internal IReadOnlyList<SoundscapeTrack> VariantsFor(EraMedium? skin) =>
        skin is { } medium &&
        EraVariants.TryGetValue(medium, out IReadOnlyList<SoundscapeTrack>? variants) &&
        variants.Count > 0
            ? variants
            : Variants;
}

/// <summary>The canonical manual playlist and opt-in interaction-SFX map. Music order is stable,
/// starts with The Long Climb, and has no relationship to the current screen. Per-song playback
/// trims preserve the authored MP3s while bringing the audited program near -14.5 LUFS and keeping
/// decoded peaks at or below the mastering ceiling. The four immersive cues also carry per-medium
/// era voicings selected by the pushed era skin; the catalog never observes navigation or career
/// state to learn that skin, the controller passes it in.</summary>
internal sealed class SoundscapeCatalog
{
    private const string MusicRoot = "Assets/Audio/Music";
    private const string SfxRoot = "Assets/Audio/Sfx";

    private readonly Dictionary<(SoundEffectCue Cue, EraMedium? Skin), int> _nextEffectVariant = [];

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
            // The immersive cues carry per-medium era voicings (timbre only); the base master stays
            // the era-neutral default. Warning/Destructive/SkillUnlock are cross-era consequence
            // signals and Bucket* is result-entry tooling: they deliberately stay era-neutral.
            [SoundEffectCue.Navigate] = EraSfx(
                "navigate.wav", "navigate-telegram.wav", "navigate-fax.wav", "navigate-email.wav",
                .50, 24, "navigation", 12),
            [SoundEffectCue.Confirm] = EraSfx(
                "commit.wav", "commit-telegram.wav", "commit-fax.wav", "commit-email.wav",
                .60, 90, "action", 45),
            [SoundEffectCue.SeatConfirm] = EraSfx(
                "seat-confirm.wav", "seat-confirm-telegram.wav", "seat-confirm-fax.wav", "seat-confirm-email.wav",
                .62, 120, "seat", 60),
            [SoundEffectCue.Back] = EraSfx(
                "back.wav", "back-telegram.wav", "back-fax.wav", "back-email.wav",
                .50, 90, "navigation", 45),
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

    /// <summary>Round-robin over the cue's era-neutral base variants (the pre-era contract,
    /// equivalent to a null skin).</summary>
    internal SoundscapeTrack NextEffect(SoundEffectCue cue, SoundEffectDefinition definition) =>
        NextEffect(cue, definition, skin: null);

    /// <summary>Round-robin over the variant set for the pushed era skin, falling back to the base
    /// set when the medium has no voicing for this cue. Rotation is tracked per cue and skin, so a
    /// skin change never reshuffles another skin's rotation; mix and anti-chatter policy live on the
    /// definition and are identical for every voicing.</summary>
    internal SoundscapeTrack NextEffect(
        SoundEffectCue cue,
        SoundEffectDefinition definition,
        EraMedium? skin)
    {
        IReadOnlyList<SoundscapeTrack> variants = definition.VariantsFor(skin);
        (SoundEffectCue Cue, EraMedium? Skin) key = (cue, skin);
        int index = _nextEffectVariant.GetValueOrDefault(key);
        _nextEffectVariant[key] = (index + 1) % variants.Count;
        return variants[index % variants.Count];
    }

    internal static SoundscapeTrack MusicTrack(MusicPlaylistTrack track) =>
        new(track.RelativePath, AudioBus.Music, track.PlaybackGain);

    internal static IReadOnlyCollection<string> DeclaredRelativePaths => Playlist
        .Select(static track => track.RelativePath)
        .Concat(Effects.Values.SelectMany(static effect => effect.Variants
                .Concat(effect.EraVariants.Values.SelectMany(static variants => variants)))
            .Select(static track => track.RelativePath))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static MusicPlaylistTrack Song(string title, string fileName, double gainDecibels) =>
        new(title, $"{MusicRoot}/{fileName}", gainDecibels);

    private static SoundscapeTrack Track(string fileName, double gain) =>
        new($"{SfxRoot}/{fileName}", AudioBus.Effects, Gain: gain);

    /// <summary>An era-neutral cue definition: one base variant set and no era voicings.</summary>
    private static SoundEffectDefinition Sfx(
        string fileName,
        double gain,
        int cooldownMilliseconds,
        string dedupeGroup,
        int dedupeMilliseconds,
        double musicDuck = 1.0,
        int duckMilliseconds = 0) =>
        new(
            [Track(fileName, gain)],
            TimeSpan.FromMilliseconds(cooldownMilliseconds),
            dedupeGroup,
            TimeSpan.FromMilliseconds(dedupeMilliseconds),
            Math.Clamp(musicDuck, 0, 1),
            TimeSpan.FromMilliseconds(duckMilliseconds));

    /// <summary>An immersive cue with one re-voiced master per period medium. The base master stays
    /// the era-neutral default; every voicing shares the cue's gain and anti-chatter policy.</summary>
    private static SoundEffectDefinition EraSfx(
        string fileName,
        string telegramFileName,
        string faxFileName,
        string emailFileName,
        double gain,
        int cooldownMilliseconds,
        string dedupeGroup,
        int dedupeMilliseconds) =>
        Sfx(fileName, gain, cooldownMilliseconds, dedupeGroup, dedupeMilliseconds) with
        {
            EraVariants = new Dictionary<EraMedium, IReadOnlyList<SoundscapeTrack>>
            {
                [EraMedium.Telegram] = [Track(telegramFileName, gain)],
                [EraMedium.Fax] = [Track(faxFileName, gain)],
                [EraMedium.Email] = [Track(emailFileName, gain)],
            },
        };
}
