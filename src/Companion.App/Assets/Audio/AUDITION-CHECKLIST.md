# Audio audition and acceptance checklist

Use this checklist to approve the manual music player and the deliberately sparse Pitwall 98
interaction effects. It is a source-side QA procedure: **do not publish, deploy, copy files into `dist`, or
replace the shipping executable while following it.** Run fault-injection cases only from a
disposable copy of a local build output; never rename, truncate, or delete source assets or files in
`dist`.

The acceptance contract is:

- Music is controlled only by the persistent top-bar player. Screens, menus, and career state never
  choose, start, pause, or replace music.
- Only explicitly opted-in deliberate button actions make SFX. Media controls and routine data entry
  stay silent.
- Audio is decorative and fail-safe. A missing, unreadable, or rejected file must never block app
  startup, navigation, result entry, save/replay behavior, or shutdown.

## 1. Test setup

Record the Windows output device, system volume, spatial-audio/enhancement state, and app source
commit. Disable any loudness-normalization enhancement for the reference pass, then optionally repeat
with the tester's normal setup.

Complete the full default-mix pass on both:

- [ ] Desktop speakers at an ordinary seated listening distance.
- [ ] Headphones at a comfortable, previously established Windows volume. Do not raise the system
      volume merely to make the quiet Navigate cue prominent.

For a fresh settings file, the current defaults are Sound enabled, Master **80%**, Menu effects
**70%**, top-bar Music **40%**, and Mute when unfocused enabled. Existing settings persist, so set
these values manually rather than using a global reset that could alter unrelated preferences.

Before judging a cue, confirm that Windows is using the intended output device and that another app
is not applying communications ducking.

## 2. Manual music-player transport

Start once with default audio values and repeat the key cases on speakers and headphones.

- [ ] Every app launch selects **The Long Climb** and starts paused, even if the previous session was
      closed while another track was playing.
- [ ] Play starts the selected track; Pause stops audible progress; repeated Play/Pause clicks remain
      responsive and do not produce menu SFX.
- [ ] Previous and Next wrap at both ends of the 23-track list.
- [ ] Previous, Next, and direct ComboBox selection preserve pause state when paused and preserve play
      state when playing.
- [ ] Letting a selected track finish advances to the next playlist item and continues playing.
- [ ] A natural end on **Injury / Death** wraps to **The Long Climb** and continues playing.
- [ ] Moving between Start, Settings, the career hub, tabs, dialogs, result entry, injury/death, and
      finale views does not change the selected track, restart it, or alter Play/Pause state.
- [ ] The top-bar Music slider changes only the music bus, moves smoothly through 0-100%, and creates
      no ticks, zipper noise, or interaction cue.
- [ ] The Music value survives close/relaunch. Track selection and play state intentionally do not:
      relaunch still returns to The Long Climb, paused.
- [ ] The ComboBox uses the 16 audited display titles and stable order in `README.md`; embedded MP3
      title tags never leak into the UI.

### Playlist continuity

At default mix, audition at least 20 seconds from every track, including one quiet and one dense
section where available.

- [ ] No track is startlingly louder or disappears relative to its neighbors.
- [ ] No decoded clipping, crackle, unexpected channel imbalance, or obvious codec failure is heard.
- [ ] Track changes are clean; a short authored head/tail silence is not mistaken for transport
      failure. The known edges are about 1.33 seconds of trailing silence on **The Long Climb** and
      0.15 seconds of leading silence on **Telemetry at Twilight**.
- [ ] **Injury** and **Injury / Death** remain ordinary user-selected playlist items. Opening those
      screens does not select them.

## 3. Mix matrix

Run the following rows with music playing, then press Settings > Audio > Menu effects > Preview and
exercise one real Navigate, Back, Warning, and Destructive action plus a result-entry car drag where
a reversible test path is available. Do not perform a destructive action against valuable data
merely to audition it; use a disposable test career/save.

| Pass | Master | Menu effects | Top-bar Music | Acceptance |
|---|---:|---:|---:|---|
| Default | 80% | 70% | 40% | Music supports the UI; common cues are legible but unobtrusive. |
| All high | 100% | 100% | 100% | No clipping, pain, harsh transient, or runaway overlap. Critical cues remain controlled. |
| Effects only | 80% | 70% | 0% | Music is silent; cues retain their intended balance and tails. |
| Music only | 80% | 0% | 40% | Every menu cue is silent and music never ducks. Raising effects allows the very next Preview click to sound. |
| Master mute | 0% | 100% | 100% | Total silence, no ducking artifact, and controls remain functional. Restoring Master returns cleanly. |
| Low level | 5% | 100% | 100% | No pop or zipper noise while entering/leaving the near-silent range; audibility is not required. |

- [ ] Rapidly repeat Navigate/Confirm/Back actions for 30-60 seconds. Same-control cooldown and
      dedupe prevent duplicate chatter, while rapidly alternating two different meaningful buttons
      produces one audible response per button. Tails do not form a loud drone or become fatiguing.
- [ ] At the all-high mix, rapidly alternate different reversible cue categories. Listen for shared
      endpoint distortion: the four one-shot players have no common App-layer limiter.
- [ ] Trigger a critical cue over music. Its attenuation arrives without a click and returns smoothly
      rather than snapping or leaving a silent hole.
- [ ] Warning uses an 0.80 music multiplier for up to 500 ms; Destructive uses 0.68 for up to 700 ms;
      Skill Unlock uses 0.70 for up to 800 ms. Each release ramp is a smooth 260 ms and may begin
      sooner when the one-shot finishes. A long cue ending after release began may restart a ramp
      from its current attenuation. Confirm, Seat Confirm, Navigate, Back, Bucket Pickup, and Bucket
      Place do not duck music.

## 4. Safe Preview and cue semantics

The Settings **Preview** button is a safe audition control. It requests the ordinary Confirm cue at
the current Master and Menu-effects values; it has no command and must not change settings, career
data, navigation state, or music transport.

- [ ] Preview plays exactly one Confirm cue when app audio and the effects bus can sound.
- [ ] Preview is silent when Sound is disabled, Master is 0%, Menu effects is 0%, or focus-mute is
      active. Returning to an audible state lets the next click sound immediately; a rejected silent
      request has not consumed its cooldown.
- [ ] Repeated Preview clicks respect anti-chatter and never stack into a louder composite.

Judge each meaning before judging personal taste:

| Cue | Semantic acceptance | Fatigue/tonal acceptance |
|---|---|---|
| Navigate | A very short two-stage mechanical mouse-like click for major navigation. | Close/release detail is satisfying on headphones, subtle on speakers, and never piercing. |
| Confirm (`commit.wav`) | Warm, resolved ordinary affirmation. | Distinct from Back without demanding attention. |
| Seat Confirm | A bright original FM C-G-C lock-in only when the visible SMGP car card becomes selected. | Clearly 16-bit inspired, distinct from ordinary Confirm, and controlled enough for repeated seat browsing. |
| Back | Clearly recessive/cancelling rather than successful or alarming. | Tail does not blur repeated dialog use. |
| Bucket Pickup | A light rising digital lift when a valid car drag begins. | Very short and quiet enough for repeated qualifying/result entry. |
| Bucket Place | A soft downward-settled landing only after a successful bucket/order change. | Distinct from pickup without sounding like an error. |
| Warning | Attention is required, but it is not an error siren. | Audible over default music without startling. |
| Destructive | Heavier and unambiguously higher consequence than Warning. | Serious, controlled, and safe at the all-high mix. |
| Skill Unlock | A restrained positive progression signature. | Rewarding once, not a long fanfare; used only by a genuine Unlock button. |

- [ ] Confirm and Back are distinguishable without watching the screen.
- [ ] Warning and Destructive communicate different consequence levels.
- [ ] Navigate remains the quietest and shortest frequent cue; do not fail it solely because it does
      not dominate music at an intentionally effects-low mix.
- [ ] On the Hub rail, selecting a different destination sounds once; clicking the already-selected
      destination is a visual no-op and stays silent. Rapidly choosing two different destinations
      never loses the second click.
- [ ] Skill Unlock is not heard on screen entry, level-up presentation, promotion, championship, or
      any action other than the explicit skill-tree Unlock button.
- [ ] Qualifying and race-result entry use the same Pickup/Place pair. Pickup occurs once after the
      drag threshold; Place occurs only for a successful Order/DNF/DSQ/Remaining change. A cancelled,
      invalid, or unchanged drop has no Place cue.
- [ ] On SMGP Team & Car, each arrow plays one Navigate cue without choosing a car. Right from
      Zeroforce wraps to Rigel, and left from Rigel wraps to Zeroforce.
- [ ] Clicking the visible car card plays one Seat Confirm cue and selects it. Clicking the empty
      viewport gutter selects nothing and plays nothing; hover/selected chrome stays inside the card.

## 5. Focus, mute, and failure recovery

### Focus and master switches

- [ ] With music playing and Mute menu effects when unfocused enabled, switch to another application.
      Music continues uninterrupted, active SFX stops, and the music duck resets immediately.
- [ ] If music was paused before focus was lost, it remains paused while inactive and after returning.
- [ ] With Mute menu effects when unfocused disabled, switching applications leaves both music and
      explicitly requested interface effects eligible to play.
- [ ] Disable Sound while music is playing. Output becomes silent while the Play/Pause UI preserves
      the user's play intent; re-enabling Sound resumes cleanly without changing tracks.
- [ ] Disable Sound or lose focus during Warning/Destructive/Skill Unlock. The one-shot stops and the
      duck state resets immediately; music does not remain attenuated after audio returns.
- [ ] Set Menu effects or Master to 0%, click Preview, restore the value, and click Preview once. It
      sounds immediately, proving the muted request consumed neither cooldown nor dedupe state.

### Loose-file failures

Perform these cases only in a disposable copy of a local build output. Keep a second untouched copy
of every test asset and close the app before replacing a file.

- [ ] Temporarily remove the selected MP3. App startup and navigation still work. Play, Next, or
      Previous walks to the first readable track rather than wedging or looping forever.
- [ ] While music is playing, make the next playlist MP3 unavailable. Natural end/Next skips the
      missing item and continues with the first readable following track.
- [ ] Substitute an invalid/truncated MP3 to exercise an asynchronous media failure. The transport
      returns to a stopped **Play** state without an exception dialog. Pressing Play retries the
      visible selection, then skips forward if it still cannot be opened.
- [ ] Temporarily remove `commit.wav` and press Preview. It fails silently, settings/navigation remain
      usable, and restoring the file allows an immediate Preview without waiting for cooldown.
- [ ] Temporarily remove a ducking cue, then invoke its tagged disposable action. Music does not dip
      because a missing one-shot was never accepted.
- [ ] Restore all files and repeat default Play and Preview before ending the fault pass.

## 6. Silent-zone audit

Listen with Menu effects at 100% so an accidental route is obvious. The following must remain silent
apart from the music action the user explicitly requested:

- [ ] Top-bar Previous, Play/Pause, Next, track selection, and Music-volume movement.
- [ ] Hover, pointer entry/exit, keyboard focus, Tab traversal, and focus-ring changes.
- [ ] Typing, caret movement, text selection, reason editing, context-menu inspection, undo,
      table-cell inspection, and filters/selectors. Explicit result-entry drag pickup/successful
      placement is the documented exception.
- [ ] Slider movement, check boxes, toggles, and ordinary row selection.
- [ ] Screen entry and passive presentation of race results, level-up, promotion, injury, death,
      championship, or finale state.

An explicit opted-in button shown on one of those screens may still carry its documented semantic
cue; the state appearing by itself must never make sound. Audit the current opt-ins with:

```powershell
rg -n 'audio:SoundAssist\.Cue=' src\Companion.App -g '*.xaml'
```

- [ ] Every reported cue belongs to a deliberate major navigation, affirmative, back/cancel,
      warning, destructive, skill-unlock, result-entry drag, or SMGP seat-choice interaction.
- [ ] `MusicPlayerControl.xaml` has no `SoundAssist.Cue` attachment.
- [ ] No audio controller or player observes Shell/navigation/career state.

## 7. Objective asset and mastering checks

### Interaction WAV acceptance

Exactly these nine deterministic masters ship in source:

| File | Duration | Target sample peak |
|---|---:|---:|
| `navigate.wav` | 0.090 s | -13.0 dBFS |
| `commit.wav` | 0.320 s | -10.5 dBFS |
| `seat-confirm.wav` | 0.480 s | -10.0 dBFS |
| `back.wav` | 0.280 s | -11.0 dBFS |
| `bucket-pickup.wav` | 0.140 s | -12.5 dBFS |
| `bucket-place.wav` | 0.220 s | -11.5 dBFS |
| `warning.wav` | 0.520 s | -10.0 dBFS |
| `destructive.wav` | 0.720 s | -9.5 dBFS |
| `skill-unlock.wav` | 0.900 s | -9.5 dBFS |

- [ ] Each file is mono PCM, 48,000 Hz, 16-bit, with no clipped sample.
- [ ] File count and names are exact; no retired outcome/milestone cue is present.
- [ ] Durations and peaks match the table (allow only PCM rounding, approximately 0.01 dB).
- [ ] Headphone inspection finds no start/end click, DC thump, piercing resonance, or noisy tail.
- [ ] A fresh generation into a temporary directory produces byte-identical hashes. This command
      does not modify the tracked SFX directory:

```powershell
$audit = Join-Path $env:TEMP ("ams2-sfx-audit-" + [guid]::NewGuid())
.\tools\generate_sfx.ps1 -OutputDirectory $audit
& .\src\Companion.App\Audio\Generation\generate-seat-confirm.ps1 `
    -OutputPath (Join-Path $audit 'seat-confirm.wav')
$source = 'src\Companion.App\Assets\Audio\Sfx'
Get-ChildItem -LiteralPath $source -Filter '*.wav' | Sort-Object Name | ForEach-Object {
    $generated = Join-Path $audit $_.Name
    [pscustomobject]@{
        File = $_.Name
        Match = (Test-Path -LiteralPath $generated) -and
                ((Get-FileHash -LiteralPath $_.FullName).Hash -eq
                 (Get-FileHash -LiteralPath $generated).Hash)
    }
}
```

All nine rows must report `Match = True`. The generators' reports must also show the expected duration,
peak, and a finite RMS for every cue.

### Music-mastering acceptance

- [ ] Source contains exactly 23 MP3s; all report 48 kHz stereo and decode without error.
- [ ] Supplied MP3 bytes remain unchanged; mastering is player attenuation, not another lossy encode.
- [ ] FFmpeg EBU R128 source measurements still agree with `MASTERING.md` within normal decoder/tool
      tolerance.
- [ ] Adding each `SoundscapeCatalog` trim to its measured source values yields approximately
      -14.5 LUFS-I and a decoded true peak no higher than -1.5 dBTP.
- [ ] The exact 23 trims and ordered titles remain pinned by the render/audio regression suite.

Example read-only inspection commands, when FFprobe/FFmpeg are installed:

```powershell
$music = 'src\Companion.App\Assets\Audio\Music'
(Get-ChildItem -LiteralPath $music -Filter '*.mp3').Count
Get-ChildItem -LiteralPath $music -Filter '*.mp3' | Sort-Object Name | ForEach-Object {
    Write-Host "--- $($_.Name)"
    ffprobe -v error -select_streams a:0 `
        -show_entries stream=codec_name,sample_rate,channels,bit_rate `
        -of default=noprint_wrappers=1 $_.FullName
}
ffmpeg -hide_banner -nostats -i "$music\the-long-climb.mp3" `
    -filter_complex 'ebur128=peak=true' -f null NUL 2>&1
```

Use the final EBU R128 summary, not a momentary loudness line. Repeat the last command for every file
when a music master, catalog trim, or decoder toolchain changes.

## 8. Non-publishing regressions

From the repository root, run only the normal build and test commands below. They create local build
outputs but do not publish or deploy an executable and do not touch `dist`:

```powershell
dotnet build Companion.slnx -c Release
dotnet test tests\Companion.Tests\Companion.Tests.csproj -c Release --no-build
dotnet test tests\Companion.RenderHarness.Tests\Companion.RenderHarness.Tests.csproj -c Release --no-build
```

- [ ] Build completes with no errors.
- [ ] Logic suite passes, including the untouched 77/77 f1db oracle and news corpus guards.
- [ ] Render/audio suite passes, including playlist order/trims, exact nine-cue inventory, transport,
      missing-file recovery, focus/mute, duck timing, SoundAssist routing, silent music controls, and
      the source-level cue coverage contract.
- [ ] `git diff -- src/Companion.App/Assets/Audio src/Companion.App/Audio src/Companion.App/Views`
      contains only the intended source changes. No `dist` change is part of this acceptance pass.

## Sign-off record

```text
Date / tester:
Source commit or worktree state:
Windows output + system volume + enhancements:
Desktop speakers: PASS / FAIL — notes:
Headphones:       PASS / FAIL — notes:
Default + edge mixes: PASS / FAIL — notes:
Transport + persistence: PASS / FAIL — notes:
Cue semantics + fatigue: PASS / FAIL — notes:
Focus / mute / failures: PASS / FAIL — notes:
Silent zones: PASS / FAIL — notes:
WAV hashes + mastering: PASS / FAIL — evidence:
Build / logic / render-audio tests: PASS / FAIL — counts:
Open defects (owner / severity / repro):
Decision: ACCEPT / REJECT
No publish, dist copy, or executable replacement performed: YES / NO
```
