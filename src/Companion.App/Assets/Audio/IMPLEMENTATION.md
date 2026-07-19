# Audio implementation handoff

This document records the sound system that is implemented in the App layer. The code is the final
authority if this handoff and the source ever disagree. The product contract is deliberately narrow:

- Music is a manual, app-lifetime player in the persistent top bar.
- Screens, navigation, career state, results, injuries, deaths, promotions, and finales never select,
  start, pause, or replace music.
- Interface SFX are sparse and opt in from specific button clicks. There is no global click listener
  and no automatic event/milestone sound system.
- Audio is decorative. Missing media, an unavailable Windows media backend, or a playback exception
  must never block startup or a career action.

## Source map and ownership

| File | Responsibility |
|---|---|
| `Audio/AppAudioController.cs` | App-lifetime owner. Applies persisted mix settings, owns the player/backend, enforces cue cooldown/dedupe, accepts explicit SFX requests, and receives the pushed era skin. |
| `Audio/MusicPlayerViewModel.cs` | Binding contract for track selection, play/pause, previous/next, natural-end advance, recovery, and persisted music volume. It has no shell/navigation dependency. |
| `Audio/SoundscapeCatalog.cs` | Canonical playlist, playback trims, semantic cue enum, asset paths, per-medium era variant sets, cue gains, cooldowns, dedupe groups, and duck policy. |
| `Audio/WpfAudioEngine.cs` | WPF `MediaPlayer` backend: one music transport, four one-shot SFX channels, safe path resolution, focus/sound gates, and duck timing. |
| `Audio/SoundAssist.cs` | Attached XAML behavior that converts an explicitly opted-in `ButtonBase.Click` into a semantic cue request, carries weak source identity, and can suppress an explicitly bound no-op. |
| `Behaviors/ListDragDropBehavior.cs` | Shared qualifying/race-result drag graph. Requests BucketPickup after the drag threshold and BucketPlace only after a successful mutation. |
| `App.xaml.cs` | Fail-soft composition, `SoundAssist` connection, the one-way era-skin push from the shell token, app activation/deactivation bridge, and disposal. |
| `MainWindow.xaml` / `MainWindow.xaml.cs` | Persistent header host and separate `MusicPlayerDataContext`; the player does not replace the shell data context. |
| `Views/MusicPlayerControl.xaml` | Previous, Play/Pause, Next, track selector, and the only music-volume slider. Media controls are intentionally silent. |
| `Views/SettingsView.xaml` | Sound enabled, Master, Menu effects, Preview, and mute-when-unfocused controls. There is no duplicate music slider here. |
| `Companion.App.csproj` | Copies `Assets/Audio/**` to build/publish output as loose content and excludes it from the single-file executable. |
| `Assets/Audio/README.md` | User-facing catalog and interaction policy. |
| `Assets/Audio/MASTERING.md` | Measured music loudness/peak record and mastering target. |
| `Assets/Audio/LICENSES.md` | Music ownership and generated-SFX provenance. |
| `tools/generate_sfx.ps1` | Deterministic source generator for the eight WAV masters. |
| `Audio/Generation/generate-seat-confirm.ps1` | Deterministic original FM source for the dedicated SMGP seat-choice master. |
| `Audio/Generation/generate-era-sfx.ps1` | Deterministic source generator for the twelve era-medium voicings of the four immersive cues. |
| `tests/Companion.RenderHarness.Tests/SettingsAudioRenderTests.cs` | Executable contract for playlist, media files, player behavior, mix policy, ducking, failure handling, and the complete XAML cue map. |

`App.xaml.cs` constructs audio after the shell, but inside a fail-soft `TryInitializeAudio` boundary.
If backend construction or connection fails, the rest of the app still opens and the top-bar player
is hidden by its null data context. On normal startup, `AppAudioController` connects to
`SoundAssist`, and `Application.Activated` / `Deactivated` are forwarded to the engine. On exit the
attached behavior is disconnected before the player/backend are disposed.

## Loose-file runtime layout

WPF `MediaPlayer` cannot stream these MP3/WAV assets from the single-file bundle. Source assets live
under:

```text
src/Companion.App/Assets/Audio/Music/*.mp3
src/Companion.App/Assets/Audio/Sfx/*.wav
```

The project copies them with `CopyToOutputDirectory="PreserveNewest"`,
`CopyToPublishDirectory="PreserveNewest"`, and `ExcludeFromSingleFile="true"`. At runtime the exact
layout must therefore be:

```text
<AppContext.BaseDirectory>/Assets/Audio/Music/*.mp3
<AppContext.BaseDirectory>/Assets/Audio/Sfx/*.wav
```

Catalog paths always use forward slashes, for example
`Assets/Audio/Music/the-long-climb.mp3`. The backend converts separators, resolves the full path
against `AppContext.BaseDirectory`, rejects paths outside that directory, and checks that the file
exists before asking WPF to open it. Never treat `dist` as an audio source-of-truth; change the
tracked source assets and catalog first.

## Settings and mix

The persisted settings are versioned JSON at
`%APPDATA%\AMS2CareerCompanion\settings.json`. Their current defaults and use are:

| Setting | Default | Runtime contract |
|---|---:|---|
| `SoundEnabled` | `true` | Master gate for music transport and SFX. Turning it off retains the user's play intent and bus values. |
| `MasterVolumePercent` | `80` | Multiplies both active buses. |
| `EffectsVolumePercent` | `70` | Multiplies interaction SFX. |
| `MusicVolumePercent` | `40` | Multiplies music and is persisted directly by the top-bar slider. |
| `MuteWhenUnfocused` | `true` | Legacy persisted name for the effects-only focus gate. Music continues while inactive; pending and active SFX stop. |
| `AmbienceVolumePercent` | `35` | Legacy persisted field; the current App audio engine has no ambience bus and does not read it. |

All percentage settings normalize to `0..100`; engine factors and asset gains clamp to `0..1`.
Ignoring the sound/focus gate, the formulas are:

```text
music volume = Master * Music * per-track linear trim * current duck
effect volume = Master * Effects * per-cue gain
```

The default untrimmed music factor is `0.80 * 0.40 = 0.32`; the default pre-cue SFX factor is
`0.80 * 0.70 = 0.56`. A zero Master/Effects bus rejects SFX, clears active one-shots, and resets
ducking. Music at zero volume keeps its loaded transport and manual play intent.

The four WPF one-shot players do not feed a shared App-layer limiter. Objective analysis keeps the
default mix conservatively below full scale, but a constructed four-cue overlap plus music at
100/100/100 can theoretically exceed aggregate headroom. Keep the all-high mixed-cue stress pass in
`AUDITION-CHECKLIST.md`; do not describe the extreme mix as clip-safe until that pass is signed off.

The Settings screen owns Master and Menu effects sliders plus a `Confirm` Preview button. The music
slider belongs only to `MusicPlayerControl`. Audio settings apply immediately through
`ISettingsService.Changed`. Settings-schema changes are outside the App/GUI lane and require a
coding-lane handoff; App-only work may consume the existing contract but must not silently extend it.

## Manual playlist and trims

Every launch selects **The Long Climb** and remains paused. Direct selection preserves the current
playing/paused state. Previous/Next wrap. A natural track end advances forward and starts the next
loadable track. Playlist order has no relationship to the visible screen.

The MP3 bytes are unchanged. `MusicPlaylistTrack` converts each non-positive dB trim with
`10^(dB/20)` and the engine applies that linear gain at playback:

| # | Display title | Runtime file under `Assets/Audio/Music/` | Trim |
|---:|---|---|---:|
| 1 | The Long Climb | `the-long-climb.mp3` | -1.49 dB |
| 2 | Pitwall at Dusk | `pitwall-at-dusk.mp3` | -0.99 dB |
| 3 | Amber Pitlane | `amber-pitlane.mp3` | -1.62 dB |
| 4 | Telemetry at Twilight | `telemetry-at-twilight.mp3` | -1.34 dB |
| 5 | Night Shift | `night-shift.mp3` | -2.13 dB |
| 6 | Race Control | `race-control.mp3` | -1.37 dB |
| 7 | Grid Locked | `grid-locked.mp3` | -0.26 dB |
| 8 | Formation Hold | `formation-hold.mp3` | -2.01 dB |
| 9 | After the Flag | `after-the-flag.mp3` | -0.60 dB |
| 10 | Cooling Lap | `cooling-lap.mp3` | -0.90 dB |
| 11 | Empty Grandstands | `empty-grandstands.mp3` | -1.30 dB |
| 12 | Super Monaco Grand Prix Intro | `intro-smgp.mp3` | -1.70 dB |
| 13 | First Light Briefing | `first-light-briefing.mp3` | -0.20 dB |
| 14 | Morning Question | `morning-question.mp3` | -1.60 dB |
| 15 | Lights in the Distance | `lights-in-the-distance.mp3` | -1.60 dB |
| 16 | Open Table | `open-table.mp3` | -2.10 dB |
| 17 | Strategy Room | `strategy-room.mp3` | -1.50 dB |
| 18 | Open Ledger | `open-ledger.mp3` | -1.10 dB |
| 19 | Rain Before Rhythm | `rain-before-rhythm.mp3` | -1.50 dB |
| 20 | Wet Line Reverie | `wet-line-reverie.mp3` | -0.70 dB |
| 21 | Golden Lap | `golden-lap.mp3` | -1.60 dB |
| 22 | Injury | `injury.mp3` | -0.50 dB |
| 23 | Injury / Death | `injury-death.mp3` | -2.10 dB |

The audited result is approximately -14.5 LUFS-I with decoded peaks no higher than -1.5 dBTP. See
`MASTERING.md` for each source LUFS-I, LRA, true peak, and effective result. All current files are
48 kHz stereo MP3s and none measured as clipped. Future lossless
masters should target -14.0 LUFS-I (+/-0.5 LU) and no higher than -1.5 dBTP. Remove the catalog trim
when a replacement already meets target; do not normalize twice or add positive gain.

## Interaction-SFX policy

`SoundEffectCue.None` is the default. The nine active cues have this exact linear mix and
anti-chatter policy:

| Semantic cue | WAV | Cue gain | Same-cue cooldown | Dedupe group | Group window | Music duck | Hold |
|---|---|---:|---:|---|---:|---:|---:|
| `Navigate` | `navigate.wav` | 0.50 | 24 ms | `navigation` | 12 ms | 1.00 | 0 ms |
| `Confirm` | `commit.wav` | 0.60 | 90 ms | `action` | 45 ms | 1.00 | 0 ms |
| `SeatConfirm` | `seat-confirm.wav` | 0.62 | 120 ms | `seat` | 60 ms | 1.00 | 0 ms |
| `Back` | `back.wav` | 0.50 | 90 ms | `navigation` | 45 ms | 1.00 | 0 ms |
| `BucketPickup` | `bucket-pickup.wav` | 0.40 | 45 ms | `bucket` | 25 ms | 1.00 | 0 ms |
| `BucketPlace` | `bucket-place.wav` | 0.46 | 55 ms | `bucket` | 30 ms | 1.00 | 0 ms |
| `Warning` | `warning.wav` | 0.70 | 420 ms | `outcome` | 140 ms | 0.80 | 500 ms |
| `Destructive` | `destructive.wav` | 0.72 | 650 ms | `critical` | 240 ms | 0.68 | 700 ms |
| `SkillUnlock` | `skill-unlock.wav` | 0.72 | 750 ms | `progression` | 300 ms | 0.70 | 800 ms |

Cooldown suppresses the same semantic cue and the dedupe group suppresses related requests raised
almost together; notably `Navigate` and `Back` share `navigation`. For attached button clicks, that
history is held in a `ConditionalWeakTable` keyed by the originating control. Two different buttons
can therefore sound in the same instant, while duplicate requests from one control remain guarded
without retaining dead views. Explicit non-button requests share the unscoped history. The controller
timestamps a request only after the backend accepts it, so a disabled, muted, unfocused, missing-file,
or synchronously failed request does not consume the next audible click.

### Era skins

The four immersive cues (Navigate, Confirm, Back, SeatConfirm) carry one re-voiced master per period
medium in `SoundEffectDefinition.EraVariants`; every other cue has only its era-neutral base set.
`AppAudioController.SetEraSkin(EraMedium?)` is the one-way push seam (era-theming-assets-brief.md,
Workstream B): the App reads `ShellViewModel.ActiveCareerEraMedium` on every navigation and tells the
controller the skin, pushing the current value once at startup. A null skin (menus, gallery, no active
career) selects the base set, and any medium without its own variant falls back to base. The catalog
round-robins per cue and skin; gain, cooldown, dedupe, mix, and ducking live on the shared definition
and are identical for every voicing, so era color is timbre only and never changes triggering.

The backend has four one-shot channels. It uses a free channel first and reuses channels round-robin
when the pool is full. Warning, Destructive, and SkillUnlock duck music; the other six do not.
Ducking begins only after the SFX `MediaOpened` callback, so a file that never opens cannot create a
silent hole. Overlapping ducks use the strongest attenuation and extend the hold to the latest end.
When the last ducking voice ends or fails early, release starts immediately rather than holding an
empty gap. The release is linear from the current attenuation to 1.0 over exactly **260 ms**, updated
by a 40 ms background dispatcher timer. Muting/disabling audio or losing focus when focus-mute is on
stops all active effects and resets ducking immediately.

For a long cue whose scheduled release has already begun, the final `MediaEnded` callback can start
a fresh 260 ms ramp from the attenuation reached at that moment. The result remains smooth, but the
total recovery from the original hold can exceed 260 ms (approximately 1.02 s from Destructive
onset and 1.16 s from Skill Unlock onset in the current single-voice timing).

### Opt-in XAML pattern

Views request meaning, never filenames:

```xml
<UserControl
    xmlns:audio="clr-namespace:Companion.App.Audio">
    <Button Content="Continue"
            Command="{Binding ContinueCommand}"
            audio:SoundAssist.Cue="Confirm" />
</UserControl>
```

For a state-dependent primary action, a style may set the attached property (the wizard uses
`Navigate` for Next and `Confirm` for Create):

```xml
<Setter Property="audio:SoundAssist.Cue" Value="Navigate" />
```

`SoundAssist` listens to WPF `ButtonBase.Click`, so mouse, touch, keyboard, and automation share the
same request. Its callback is exception-safe and cannot interrupt the actual command. Current audit
tests require direct cue attributes to live on `Button` elements.

When a command remains enabled but clicking it is already a no-op, bind
`audio:SoundAssist.SuppressWhen` to that state. The Hub rail uses `IsSelected`, so selecting a new
destination sounds once and clicking the already-active destination remains quiet.

Opt in only deliberate major navigation, affirmative, back/cancel, warning, destructive, and actual
skill-unlock buttons. The sole non-button exception is the shared result-entry drag behavior:
BucketPickup when a valid car drag begins and BucketPlace only when a drop changes Order, DNF, DSQ,
or Remaining. Keep media controls, sliders, check boxes/toggles, text input, context-menu inspection,
tables, filters, hover/focus, screen entry, automatic state changes, and injury/death/sit-out surfaces
silent. Do not add a global routed-event listener.

## Failure and focus contracts

- Audio initialization is fail-soft. The app remains fully usable without a player/backend.
- Missing or out-of-root paths are rejected before WPF is called.
- Synchronous and asynchronous music failures collapse into one unloaded/stopped state and one
  fail-safe `MusicFailed` notification for that selection. Subscriber exceptions are contained.
- After a music failure, Play first retries the visible track, then scans forward at most one full
  playlist cycle. Previous, Next, and natural-end advance also skip unloadable files and stop after
  one cycle, so a partial copy cannot wedge or loop the transport forever.
- `MusicEnded` advances only when playback was manually requested. Natural end forces the following
  loadable track to play.
- Sound disable pauses the audible music transport but retains manual play intent; re-enabling Sound
  resumes only if the user had been playing. An explicit Pause remains paused.
- Application focus never pauses music. Focus mute stops pending/active SFX and clears any duck;
  rejected effects do not consume cooldown. The persisted setting keeps its legacy
  `MuteWhenUnfocused` name for settings-file compatibility.
- SFX and attached-behavior exceptions are decorative failures and never escape into the command
  that caused the click. Disposal is best effort for the same reason.

## Deterministic SFX masters

Run `tools/generate_sfx.ps1` from the repository root for the original eight masters, then run
`src/Companion.App/Audio/Generation/generate-seat-confirm.ps1` for the dedicated SMGP seat-choice
master, and `src/Companion.App/Audio/Generation/generate-era-sfx.ps1` for the twelve era-medium
voicings. All use mathematical oscillators and seeded deterministic noise; there are no recordings,
third-party samples, Microsoft/Windows sounds, or SEGA/game audio. Do not hand-edit generated files.

The generators write RIFF PCM format 1, 48,000 Hz, 16-bit, mono masters. They remove DC, apply a
3 ms sine fade at both ends, normalizes to the declared peak, and reports duration, byte count, peak,
and RMS where applicable. The render test pins the exact 21-file directory, format headers, frame counts, zero first
and last samples, and peak within 0.02 dB:

| WAV | Duration | Frames | Target peak |
|---|---:|---:|---:|
| `navigate.wav` | 0.090 s | 4,320 | -13.0 dBFS |
| `commit.wav` | 0.320 s | 15,360 | -10.5 dBFS |
| `seat-confirm.wav` | 0.480 s | 23,040 | -10.0 dBFS |
| `back.wav` | 0.280 s | 13,440 | -11.0 dBFS |
| `bucket-pickup.wav` | 0.140 s | 6,720 | -12.5 dBFS |
| `bucket-place.wav` | 0.220 s | 10,560 | -11.5 dBFS |
| `warning.wav` | 0.520 s | 24,960 | -10.0 dBFS |
| `destructive.wav` | 0.720 s | 34,560 | -9.5 dBFS |
| `skill-unlock.wav` | 0.900 s | 43,200 | -9.5 dBFS |

The twelve era voicings mirror their base cue's duration, frame count, and peak exactly; only the
synthesis recipe (the timbre) differs:

| WAV | Duration | Frames | Target peak |
|---|---:|---:|---:|
| `navigate-telegram.wav` / `navigate-fax.wav` / `navigate-email.wav` | 0.090 s | 4,320 | -13.0 dBFS |
| `commit-telegram.wav` / `commit-fax.wav` / `commit-email.wav` | 0.320 s | 15,360 | -10.5 dBFS |
| `seat-confirm-telegram.wav` / `seat-confirm-fax.wav` / `seat-confirm-email.wav` | 0.480 s | 23,040 | -10.0 dBFS |
| `back-telegram.wav` / `back-fax.wav` / `back-email.wav` | 0.280 s | 13,440 | -11.0 dBFS |

The generators overwrite declared outputs but do not make undeclared stale WAVs valid. When a cue
is retired, remove its source WAV as part of the same change; the exact-directory test rejects extras.
Update the appropriate generator, generated WAV, catalog, tests, README, and provenance together.

## Test coverage and safe change workflow

`SettingsAudioRenderTests` currently pins:

- the 23-item order, titles, paths, dB trims, calculated linear gains, nine typed SFX cues with their
  twelve era voicings, unique safe relative paths, and the total 44 declared assets;
- exact WAV headers, frame counts, boundary samples, peaks, cue gains, cooldowns, dedupe, and ducks;
- paused startup, play/pause, direct selection, wrapping Previous/Next, natural-end advance, retained
  pause/play intent, persisted volume, missing-track skips, async failure reset, retry, and one-cycle
  recovery bounds;
- absence of shell/navigation dependencies in App audio types;
- top-bar layout, two-way bindings, and dynamic Play/Pause automation names;
- attached-cue connection/change/detach, no-op suppression, source-scoped rapid-click/cooldown behavior, focus/effects-bus gates,
  deferred duck start, early reset, hold extension, and the full 260 ms release math;
- the era-skin contract: per-medium voicing selection with base fallback, `SetEraSkin(null)`
  restoring the base set, skin-independent cooldown/dedupe/ducking, and the one-way App push wiring;
- the complete file-by-file XAML cue map, Wizard's dynamic cue setters, and required silence for the
  music player, Sit Out, and Death screen.
- the result-entry drag behavior's explicit BucketPickup and successful BucketPlace requests.

For an intentional audio change:

1. Change the canonical App source, not copied build/publish/`dist` files.
2. For music, add the loose MP3, document provenance, audit it with FFmpeg EBU R128 true-peak
   measurement, choose a non-positive playback trim, and update `SoundscapeCatalog`, `MASTERING.md`,
   `README.md`, and the playlist assertions together.
3. For SFX, change the appropriate generator recipe/spec, regenerate its declared masters, remove
   retired outputs, and update the semantic enum/catalog/docs/tests together. Never introduce AMS2,
   broadcast, team-radio, game, or unlicensed sample audio.
4. For a cue placement, use `SoundAssist.Cue` on the smallest set of meaningful buttons and update
   the audited XAML map. Non-button cues require an equally explicit presentation behavior and a
   source contract test. Preserve the silent zones above.
5. Run the render/audio tests, then the full solution tests. A normal future verification is
   `dotnet test tests/Companion.RenderHarness.Tests/Companion.RenderHarness.Tests.csproj` followed by
   `dotnet test Companion.slnx` from the repository root.
6. Treat publishing/deployment as a separate approved release step. A single-file EXE without the
   loose `Assets/Audio` tree has no playable media; a source/test pass must not silently replace the
   canonical executable or overwrite unrelated `dist/data` content.
