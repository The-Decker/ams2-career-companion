# AMS2 Career Companion audio catalog

The soundtrack follows the **Mechanical Memory** direction: a restrained pit-wall soundscape that
supports the companion without competing with AMS2. Music is controlled exclusively by the player
in the persistent top bar. Screens, menus, career events, injuries, results, and finales never
select, start, pause, or replace a track.

## Sound-design handoff

- `SOUND-DESIGN.md` is the creative north star, composition brief, semantic cue matrix, and
  sounded-versus-silent policy.
- `IMPLEMENTATION.md` records the source architecture, exact runtime mix, failure behavior, and safe
  change workflow.
- `AUDITION-CHECKLIST.md` is the speakers/headphones acceptance pass and sign-off record. It keeps
  source validation separate from publishing or replacing an executable.
- `AUDITION-REPORT-2026-07-13.md` records the completed objective QA pass, its one cue-map
  correction, and the remaining human listening targets.
- `PITWALL-98-REPORT-2026-07-13.md` is the original eight-cue SFX measurement and audition record;
  the later SMGP seat-confirm master is pinned by the executable inventory tests below.
- `MASTERING.md` contains the measured soundtrack loudness, peaks, and playback trims.
- `LICENSES.md` records music ownership and deterministic SFX provenance.

## Manual playlist

| Order | Display title | File |
|---|---|---|
| 1 | The Long Climb | `Music/the-long-climb.mp3` |
| 2 | Pitwall at Dusk | `Music/pitwall-at-dusk.mp3` |
| 3 | Amber Pitlane | `Music/amber-pitlane.mp3` |
| 4 | Telemetry at Twilight | `Music/telemetry-at-twilight.mp3` |
| 5 | Night Shift | `Music/night-shift.mp3` |
| 6 | Race Control | `Music/race-control.mp3` |
| 7 | Grid Locked | `Music/grid-locked.mp3` |
| 8 | Formation Hold | `Music/formation-hold.mp3` |
| 9 | After the Flag | `Music/after-the-flag.mp3` |
| 10 | Cooling Lap | `Music/cooling-lap.mp3` |
| 11 | Empty Grandstands | `Music/empty-grandstands.mp3` |
| 12 | Super Monaco Grand Prix Intro | `Music/intro-smgp.mp3` |
| 13 | First Light Briefing | `Music/first-light-briefing.mp3` |
| 14 | Morning Question | `Music/morning-question.mp3` |
| 15 | Lights in the Distance | `Music/lights-in-the-distance.mp3` |
| 16 | Open Table | `Music/open-table.mp3` |
| 17 | Strategy Room | `Music/strategy-room.mp3` |
| 18 | Open Ledger | `Music/open-ledger.mp3` |
| 19 | Rain Before Rhythm | `Music/rain-before-rhythm.mp3` |
| 20 | Wet Line Reverie | `Music/wet-line-reverie.mp3` |
| 21 | Golden Lap | `Music/golden-lap.mp3` |
| 22 | Injury | `Music/injury.mp3` |
| 23 | Injury / Death | `Music/injury-death.mp3` |

Every launch preselects track 1 and starts **paused**. The user can play/pause, choose any track,
move previous/next, and change the persisted Music volume from the top bar. A track reaching its
natural end advances to the next playlist item; this transport behavior is unrelated to navigation.
Changing a track while paused keeps it paused. Music continues when the app loses focus; the
focus-mute setting applies only to interface effects and never changes the user's play state.
Per-track playback trims keep the playlist near -14.5 LUFS-I without re-encoding the supplied MP3s;
the measurements and exact trims are recorded in `MASTERING.md`.

All shipping music is copied beside the executable as loose content. Missing or unreadable files
must degrade to silence and must never block startup, navigation, result entry, or replay.

## Pitwall 98 interaction SFX

The `Sfx/` pack uses original FM chimes, simple interval motifs, and soft digital impacts inspired
by late-1990s desktop interfaces. It evokes that era without copying or sampling Microsoft sounds.
The cues remain short and restrained so routine result entry does not become an arcade soundboard.
No cue contains a recording, sample-library asset, voice, engine, crowd, broadcast, or AMS2 audio.

| Cue | File | Intended use |
|---|---|---|
| Navigate | `Sfx/navigate.wav` | Soft two-stage mechanical mouse click for opt-in major navigation; never hover, typing, or focus changes |
| Commit | `Sfx/commit.wav` | Ordinary affirmative action |
| Seat confirm | `Sfx/seat-confirm.wav` | Choosing the visible SMGP team-and-car card |
| Back | `Sfx/back.wav` | Back or cancel |
| Bucket pickup | `Sfx/bucket-pickup.wav` | A car crosses the drag threshold in result entry |
| Bucket place | `Sfx/bucket-place.wav` | A car successfully lands in Order, DNF, DSQ, or Remaining |
| Warning | `Sfx/warning.wav` | A menu action requiring attention |
| Destructive | `Sfx/destructive.wav` | High-consequence confirmation button |
| Skill unlock | `Sfx/skill-unlock.wav` | Skill-tree Unlock button |

All cues are deterministic 48 kHz, 16-bit mono PCM WAV files, with short tails and peaks from
-13.0 to -9.5 dBFS. The frequent navigation and bucket cues are intentionally quieter. Regenerate the
complete pack from the repository root with the tracked generators:

```powershell
.\tools\generate_sfx.ps1
.\src\Companion.App\Audio\Generation\generate-seat-confirm.ps1
.\src\Companion.App\Audio\Generation\generate-era-sfx.ps1
```

The generators produce the exact audited masters; the seat-confirm source prints its file size and
is hash-stable across repeated runs. Generated WAV files and generators must be updated together.

## Era-medium voicings

Inside a career, the four immersive cues (Navigate, Confirm, Back, SeatConfirm) are re-voiced for
the career's period medium; menus, the gallery, and all other cues stay on the era-neutral base
masters above. The era skin only changes timbre: trigger rules, gain, cooldowns, dedupe, ducking,
and the silence zones are identical in every era. The twelve era masters ship as
`Sfx/<cue>-telegram.wav`, `Sfx/<cue>-fax.wav`, and `Sfx/<cue>-email.wav`:

| Medium | Voicing | Cues |
|---|---|---|
| Telegram | Relay/telegraph-key tick with a small bell | Navigate, Confirm, SeatConfirm, Back |
| Fax | Thermal-print chirp with a handshake warble | Navigate, Confirm, SeatConfirm, Back |
| Email | Soft FM chime | Navigate, Confirm, SeatConfirm, Back |

Each voicing keeps its base cue's duration, peak, and melodic contour. The App pushes the era skin
one-way to the audio controller (`SetEraSkin`) when a career opens, closes, or moves behind a menu;
the controller is told the skin and never observes navigation or career state. All era masters are
original deterministic synthesis from `Audio/Generation/generate-era-sfx.ps1`, with no recordings
or signal captures.

Every SFX request comes from an explicitly opted-in button click, the visible seat-card choice, or
the result-entry drag behavior. Button cooldown history is scoped to the originating control, so
rapid clicks on two different meaningful buttons remain audible while one control still receives
anti-chatter protection. Buttons whose click is already a no-op can explicitly suppress their cue.
Qualifying and race-result entry share the same pickup/place language. There are no hover, focus,
typing, slider, screen-entry, passive race-result, level-up, promotion, injury, death, or
championship triggers. Cooldowns prevent chatter, and the music transport never reacts to them.
