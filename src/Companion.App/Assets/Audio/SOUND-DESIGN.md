# AMS2 Career Companion sound design bible

**Creative direction:** Mechanical Memory / Pitwall 98

**Scope:** music, interaction SFX, mix hierarchy, and future audio decisions

**Runtime contract:** music is a manual top-bar player; SFX respond only to deliberate button
clicks and explicit result-entry bucket drags

This is the durable creative brief for app audio. The implementation remains authoritative for exact behavior: `Audio/SoundscapeCatalog.cs`, `Audio/MusicPlayerViewModel.cs`, `Audio/AppAudioController.cs`, `Audio/SoundAssist.cs`, `Audio/WpfAudioEngine.cs`, `Views/MusicPlayerControl.xaml`, `tools/generate_sfx.ps1`, and `Audio/Generation/generate-era-sfx.ps1`. Asset provenance lives in `LICENSES.md`; measured music trims live in `MASTERING.md`.

## 1. Mechanical Memory / Pitwall 98

Mechanical Memory is the sound of a career being operated, recorded, and remembered from the pit wall. It belongs to the companion app, not to the simulated cockpit.

### Pillar 1: pit wall, not racetrack

Use an original late-1990s desktop palette: compact FM bells, glassy two-note motifs, soft digital
impacts, and restrained low supporting tones. Evoke the period without copying Microsoft system
audio. Do not compete with AMS2 using engines, crowds, broadcasts, race starts, or team radio.

### Pillar 2: warm precision

Short attacks communicate that an action happened; pitch direction communicates meaning. Rising
motifs pick up, advance, confirm, and unlock. Falling motifs go back, place, warn, or signal greater
consequence. Decaying FM partials keep the result recognizably digital without becoming a bare beep.

### Pillar 3: deliberate action earns sound

Sound follows a user's meaningful click or an explicit result-entry drag. It does not follow the
pointer, keyboard focus, a bound property, navigation state, or a career outcome. Cue names describe
intent rather than a screen, so the same action carries the same meaning everywhere. The era skin
changes none of this: the audio layer is told the active career's period medium as a one-way push
and never observes state to learn it, so era color affects how a cue is voiced, never when or
whether it fires.

### Pillar 4: silence is part of the design

Most interaction remains silent. Browsing, reading, typing, and repeated adjustments need room to
breathe. Result entry sounds only when a car genuinely crosses the drag threshold and when a
successful drop changes a bucket or order.

### Pillar 5: player agency over authored drama

The player chooses if and when music plays. A title may suggest tension, reflection, triumph, or loss, but the app never assigns that track to the matching event. Music accompanies the user's session; it does not score the app's screens.

### Pillar 6: one original identity, era color per medium

The app spans historical careers and the separate SMGP replica career. Mechanical Memory supplies one original identity across them, tinted per period medium. Inside a career the immersive cues (Navigate, Confirm, Back, SeatConfirm) are voiced for that career's medium: a telegram relay/telegraph-key tick with a small bell, a fax thermal-print chirp with a handshake warble, or a soft email FM chime. Menus, the gallery, and the cross-era consequence (Warning, Destructive, SkillUnlock) and result-entry tooling (BucketPickup, BucketPlace) cues stay on the era-neutral base set. The skin is received, never observed: the shell pushes it one-way to the audio controller, like a theme. Period color is welcome; copied game audio, broadcasts, team radio, and unlicensed third-party material are not.

*Decision record (signed by Mike, 2026-07-18): this amendment is the gate for era-aware interaction SFX (era-theming-assets-brief.md, Workstream B). Era color is timbre only: meanings, trigger rules, mix, ducking, anti-chatter, and the silence zones are exactly as before, and asset or playback failure still degrades to silence.*

## 2. Broad-appeal music composition brief

The target is **melodic, instrumental cinematic electronica with restrained racing momentum**: approachable for a long management session, memorable, and spacious enough to sit behind reading and decisions.

### Writing and palette

- Lead with a clear motif or harmonic idea instead of sound-design spectacle. Tonal warmth and a readable emotional direction have wider reach than abrasion, novelty, or constant suspense.
- Build motion with pulse, bass movement, ostinato, or restrained percussion—not sampled engines, tires, crowds, radio, or broadcast material.
- Favor warm synthesized timbres, rounded bass, pads, clean leads, and lightly mechanical percussion. Piano, electric keys, guitar, strings, or other organic color may join when it supports the same restrained identity.
- Leave space in the midrange and transient field so text-heavy use stays comfortable and interface cues remain distinct.
- Let intensity vary. Existing titles support reflective color (`Pitwall at Dusk`, `After the Flag`, `Wet Line Reverie`), anticipation (`Race Control`, `Grid Locked`, `First Light Briefing`), resolve (`The Long Climb`, `Golden Lap`), and ceremony (`Lights in the Distance`, `Super Monaco Grand Prix Intro`). These are composition colors, never runtime scene tags.
- Tracks should end cleanly because playback advances instead of automatically looping.

### Broad-appeal guardrails

- Keep melody, harmony, and groove more important than genre display. Avoid dependence on extreme distortion, relentless dense drums, piercing highs, or dominant sub-bass.
- Prefer instrumental storytelling. Lyrics, speech, imitation radio, and recognizable third-party material compete with reading and create licensing/localization problems.
- Retro color may acknowledge SMGP, but it must remain an original composition, not reproduced SEGA audio or a requirement that every career sound like one decade.
- Preserve dynamics. Do not solve playlist consistency with aggressive limiting; the player already supports conservative, non-positive trims.
- Judge new music at low listening levels during a text-heavy workflow, not only in isolation. It should remain pleasant when repeated and make sense without a screen-specific narrative.

### Delivery and mastering

The current 23 MP3s are 48 kHz stereo and remain byte-unchanged; player trims bring them close to -14.5 LUFS-I with decoded peaks no higher than -1.5 dBTP. For a future lossless master, target **-14.0 LUFS-I, +/-0.5 LU**, and **no higher than -1.5 dBTP**, preserving natural loudness range. If a replacement already meets that target, remove its player trim instead of normalizing twice. Measure with FFmpeg EBU R128 and record the result in `MASTERING.md`.

## 3. Locked manual-music contract

Music is controlled **only** by the persistent top-bar player.

- Controls are Previous, Play/Pause, Next, a direct track selector, and a Music volume slider.
- Every launch selects `The Long Climb`, the first catalog item, and starts paused.
- Only a transport click, direct user selection, or natural track end may change music playback or selection.
- Previous and Next wrap. Natural end advances to the next available track and keeps playing.
- Selecting while paused stays paused; selecting while playing continues with the chosen track.
- Top-bar Music volume persists. Settings supplies the master switch, Master volume, Menu effects volume, cue Preview, and focus-mute; there is no second Music slider in Settings.
- Master disable changes music audibility, not requested play state. Music continues when the app
  loses focus; focus-mute affects interface effects only and never changes manual play/pause intent.
- Missing/unreadable loose files are skipped where possible or degrade to silence. Audio failure never blocks startup, navigation, result entry, save/replay, or teardown.
- The transport is intentionally SFX-silent: Previous, Play/Pause, Next, track selection, and its volume slider request no menu cue.

There is no screen-to-track map or career-state observer. Menus, tabs, briefings, results, standings, level-ups, promotions, finales, injury, death, and championships never select, start, pause, replace, or restart music.

## 4. Nine-cue interaction language

All shipping cues are deterministic **48 kHz, 16-bit mono PCM WAV** masters from tracked generators: `tools/generate_sfx.ps1`, `Audio/Generation/generate-seat-confirm.ps1`, and `Audio/Generation/generate-era-sfx.ps1` (the era-medium voicings). Runtime gain follows Effects and Master. Button cooldown/dedupe history is source-scoped, so it prevents one control chattering without swallowing a rapid click on another control. High-attention cues reduce music temporarily, followed by a 260 ms release (or earlier release when the audible cue ends).

| Semantic cue | Asset/master and design | Runtime mix / anti-chatter |
|---|---|---|
| **Navigate** | `navigate.wav`; 90 ms; -13.0 dBFS. A soft deterministic two-stage mouse mechanism: close tick, warm body resonance, quieter release tick. | Gain 0.50; 24 ms same-control cooldown; `navigation` dedupe 12 ms; no duck. |
| **Confirm** | `commit.wav`; 320 ms; -10.5 dBFS. An open C-to-G digital chime with a quiet tonal floor. | Gain 0.60; 90 ms cooldown; `action` dedupe 45 ms; no duck. |
| **SeatConfirm** | `seat-confirm.wav`; 480 ms; -10.0 dBFS. An original bright C-G-C FM lock-in arpeggio with a restrained bass anchor. | Gain 0.62; 120 ms cooldown; `seat` dedupe 60 ms; no duck. |
| **Back** | `back.wav`; 280 ms; -11.0 dBFS. A restrained G-to-C descent. | Gain 0.50; 90 ms cooldown; `navigation` dedupe 45 ms; no duck. |
| **BucketPickup** | `bucket-pickup.wav`; 140 ms; -12.5 dBFS. A very short rising FM sweep when a car crosses the drag threshold. | Gain 0.40; 45 ms cooldown; `bucket` dedupe 25 ms; no duck. |
| **BucketPlace** | `bucket-place.wav`; 220 ms; -11.5 dBFS. A downward-then-settled two-tone landing after a successful drop. | Gain 0.46; 55 ms cooldown; `bucket` dedupe 30 ms; no duck. |
| **Warning** | `warning.wav`; 520 ms; -10.0 dBFS. Two separated descending desktop-alert chimes. | Gain 0.70; 420 ms cooldown; `outcome` dedupe 140 ms; music multiplier 0.80 up to 500 ms. |
| **Destructive** | `destructive.wav`; 720 ms; -9.5 dBFS. Three falling FM stages with a restrained low anchor. | Gain 0.72; 650 ms cooldown; `critical` dedupe 240 ms; music multiplier 0.68 up to 700 ms. |
| **SkillUnlock** | `skill-unlock.wav`; 900 ms; -9.5 dBFS. A four-stage rising digital arpeggio. | Gain 0.72; 750 ms cooldown; `progression` dedupe 300 ms; music multiplier 0.70 up to 800 ms. |

`SoundEffectCue.Confirm` deliberately resolves to `commit.wav`: **Confirm** is the semantic API; **commit** describes the asset. Views request meanings, never filenames.

### Era-medium voicings

Inside a career the four immersive cues (Navigate, Confirm, Back, SeatConfirm) select a per-medium voicing through the pushed era skin; every other cue, and every menu or gallery screen, stays on the base master. Each era master keeps its cue's duration, peak, gain, cooldown, dedupe, and duck policy; only the timbre changes. They ship as `<cue>-telegram.wav`, `<cue>-fax.wav`, and `<cue>-email.wav` from the tracked `Audio/Generation/generate-era-sfx.ps1` generator: telegram is a relay/telegraph-key tick with a small bell, fax is a thermal-print chirp with a handshake warble, email is a soft FM chime. The melodic contour of each base cue is preserved so the meaning reads identically in every era.

## 5. Sounded and silent interaction zones

### Sounded: explicit, meaningful clicks

A view opts in with `SoundAssist.Cue` on the specific `Button`. WPF's routed Click provides the
same response for mouse, touch, keyboard activation, and UI automation.

- **Navigate:** primary routes, hub rail destinations, or opening a substantial tab/window/tool.
- **Confirm:** create/start/continue/accept/save/apply/stage/submit after a choice, including final result confirmation rather than its editing tools.
- **SeatConfirm:** the visible SMGP team-and-car card becoming the chosen cockpit.
- **Back:** cancel, close, dismiss, Done, or return.
- **BucketPickup:** a valid result-entry car drag crosses the system drag threshold.
- **BucketPlace:** that drag successfully changes Order, DNF, DSQ, or Remaining. It is shared by
  qualifying and race-result entry and does not fire for an invalid or unchanged drop.
- **Warning:** reset, restore, or another explicit action needing attention without reaching destructive severity.
- **Destructive:** irreversible delete, abandon, discard, force overwrite, or another meaningfully
  destructive confirmation. A safe close that only returns to the main menu remains `Back`.
- **SkillUnlock:** the actual skill-node Unlock click and nothing else.

### Intentionally silent

- Hover, pointer entry/exit, focus, screen entry, animation, loading, and application-focus changes.
- Music controls and volume; other sliders, check boxes, radio buttons, ordinary toggles, selectors,
  text fields, typing, scrolling, ordinary list selection, tables, filters, and passive standings/result rows.
- Result-entry typing, reason editing, context-menu inspection, value adjustment, and invalid drops.
  The explicit drag pickup/successful placement pair and final commit are the only result-entry cues.
- Passive news, progression, and career outcomes. Level increase, promotion/demotion, title, season transition, race result, injury, death, finale/game-over entry, errors, and validation state do not sound automatically.

An explicit button on a silent screen may still use its semantic cue. Death remains silent even if a later user-initiated Close uses `Back`: the trigger is the click, never the event.

## 6. Accessibility and fatigue control

- Audio is supplementary. No instruction, warning, selection, or outcome exists only in sound.
- Preserve master enable, Master volume, Menu effects volume, top-bar Music volume, cue Preview, and focus-mute, all immediately effective.
- Preserve separate Music and Effects buses below Master. Do not raise music merely to make cues audible.
- Silence on repetitive controls is the primary fatigue control; source-scoped cooldown and dedupe are secondary anti-chatter protection and never suppress another distinct button.
- Navigate, Confirm, SeatConfirm, Back, BucketPickup, and BucketPlace do not duck. Warning, Destructive, and
  SkillUnlock keep their limited reductions and smooth 260 ms release.
- Disabled/muted/unfocused SFX requests do not consume cooldown. Focus mute stops active effects and clears ducking.
- Playback/asset failures are decorative failures: catch them, produce silence, and never interrupt the command.
- Avoid full-scale transients, long UI tails, speech, and repeated fanfares. Existing masters establish the comparison range: 90-900 ms and -13.0 to -9.5 dBFS before runtime gain.

## 7. Future-track and new-cue decisions

### Adding a track

A track ships only when all answers are yes:

1. **Original or compatibly licensed?** Record source, permission, and edits in `LICENSES.md`. Never import AMS2 audio, Formula 1 broadcasts/team radio, SEGA/SMGP recordings, or undocumented third-party material.
2. **Mechanical Memory fit?** It must suit long reading sessions and work as a manually chosen standalone piece, not depend on a screen.
3. **Player agency preserved?** Add it only to `SoundscapeCatalog.Playlist`; add no scene tag, navigation observer, career hook, autoplay rule, or outcome trigger.
4. **Measured delivery?** Keep the source master, measure LUFS-I/LRA/true peak, update `MASTERING.md`, and use only a non-positive trim. The catalog clamps positive trims to zero.
5. **Loose integration complete?** Add it under `Assets/Audio/Music`, keep its audited display title in the catalog instead of trusting tags, and update `README.md` plus playlist/catalog render tests. WPF `MediaPlayer` requires loose exe-adjacent audio.

Keep `The Long Climb` first unless deliberately changing paused startup selection. Place additions for coherent manual listening order, but never give titles or positions automatic screen semantics.

### Adding a cue

First map the interaction to the nine existing meanings. Add a semantic only for a genuinely new
class of deliberate user action that cannot truthfully use Navigate, Confirm, Back, BucketPickup,
BucketPlace, SeatConfirm, Warning, Destructive, or SkillUnlock.

If justified:

1. Add a semantic enum member and catalog definition; views request meaning, not filename.
2. Trigger only through an opted-in click—never a global handler, screen watcher, ViewModel/career observer, or milestone trigger.
3. Add original deterministic synthesis to an appropriate tracked generator; retain 48 kHz, 16-bit mono PCM and update generator/WAV/provenance together.
4. Place it in the hierarchy against all nine masters. Define gain, cooldown, dedupe, and—only if attention hierarchy requires it—music duck/hold.
5. Verify low-volume distinction, short-session and repeated-use comfort, non-AMS2 character, and complete visual/text meaning without audio.
6. Update `README.md`, `SoundscapeCatalog`, catalog/generator tests, and the XAML coverage audit.

Do not create cues for level-up, promotion, demotion, title won, season transition, race outcome, injury, death, dispatch arrival, grid state, or screen entry. Those are events, not explicit menu interactions. Buttons within those workflows use the existing semantic matrix.
