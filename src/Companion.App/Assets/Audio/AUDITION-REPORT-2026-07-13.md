# Objective audio QA report - 2026-07-13

> Historical baseline: the music findings remain current. The six-cue SFX measurements below were
> superseded later on 2026-07-13 by the eight-cue Pitwall 98 redesign. Use
> `PITWALL-98-REPORT-2026-07-13.md` for the current interaction pack.

**Status:** technical pass; human listening gate remains open

**Release boundary:** this pass changed source policy only. It did not build, publish, copy, or
replace an executable, and `dist` remained untouched.

This report records the objective phase of `AUDITION-CHECKLIST.md`. Measurements can identify
decode, level, spectral, phase, timing, and routing risks; they cannot decide whether a track or cue
is pleasant. The focused listening targets below therefore remain required before release.

## Decision summary

- Keep all 16 music masters and every current playback trim.
- Keep all six generated SFX masters, cue gains, cooldowns, dedupe windows, and duck values.
- Retag the two direct **Overwrite anyway (backup first)** actions in `SkinsView.xaml` from
  `Warning` to `Destructive`; they execute the force write immediately and now match the final
  action in the Briefing confirmation flow.
- Treat the constructed 100% music + 100% effects + four-voice overlap as a human stress-test gate.
  The default mix has conservative headroom, but the WPF backend has no shared output limiter.
- Do not claim subjective acceptance until the speakers/headphones pass is signed off.

## Music audit

All 16 catalog items, mastering rows, and MP3 assets agree in title, order, file, and trim. Every
file decoded successfully through FFmpeg with `-xerror`; all are 48 kHz stereo MP3s at roughly
178-210 kb/s. Durations range from 9.96 to 201.62 seconds.

| Objective check | Result |
|---|---|
| Effective integrated loudness | -14.44 to -14.70 LUFS-I |
| Effective true-peak ceiling | -1.546 dBTP or lower |
| Source peak-to-loudness ratio | 10.80 to 12.91 dB |
| Sample crest range | 11.3 to 13.6 dB |
| Left/right level balance | Within 0.40 dB on every track |
| Median stereo phase correlation | Positive on every track, 0.452 to 0.795 |
| Edge silence | Existing record confirmed: Long Climb 1.33 s tail; Pitwall III 0.15 s lead |
| Decode faults, NaN/Inf, clipping signature | None found |

The measured integrated-loudness values agree with `MASTERING.md` within 0.059 LU and true peak
within 0.051 dB. Direct analyzer LRA readings differ from some recorded values by up to 0.55 LU;
that is a method/version detail and does not change any player trim.

### Focused human checks

1. **Pitwall at Dusk - mono compatibility.** It is the widest track: median phase correlation
   0.452, fifth percentile -0.157, and 11.34% negative-correlation frames. This warrants a mono
   fold-down listen, not an automatic master change.
2. **SMGP Intro and Championship Finale - repeat brightness.** They are among the brightest
   full-length programs and need a low-volume, repeated headphone pass. The brighter
   `Challenge Accepted` is only 9.96 seconds.
3. **Injury and Injury / Death - small-speaker translation.** They are the darkest programs by a
   clear margin. Confirm that their important material remains readable on ordinary speakers.

No isolated sub-bass outlier or evidence of destructive limiting was found.

## Interaction-SFX audit

All six WAVs match the generator contract: mono signed 16-bit PCM at 48 kHz, exact frame counts and
declared peaks, zero first/last samples, negligible DC, and clean tails. A temporary regeneration
produced byte-identical SHA-256 results for every file.

| Cue | Peak / RMS | Crest | Power centroid | Energy above 4 kHz | Default runtime peak | 100% runtime peak |
|---|---:|---:|---:|---:|---:|---:|
| Navigate | -11.00 / -25.86 dBFS | 14.86 dB | 921 Hz | 3.06% | -23.57 dBFS | -18.54 dBFS |
| Confirm | -9.50 / -24.03 dBFS | 14.53 dB | 268 Hz | 0.08% | -18.97 dBFS | -13.94 dBFS |
| Back | -10.00 / -21.37 dBFS | 11.37 dB | 314 Hz | below 0.01% | -21.06 dBFS | -16.02 dBFS |
| Warning | -9.00 / -21.68 dBFS | 12.68 dB | 315 Hz | 0.17% | -17.13 dBFS | -12.10 dBFS |
| Destructive | -9.00 / -23.04 dBFS | 14.04 dB | 283 Hz | 0.27% | -16.89 dBFS | -11.85 dBFS |
| Skill Unlock | -9.00 / -19.75 dBFS | 10.75 dB | 556 Hz | approximately 0% | -16.89 dBFS | -11.85 dBFS |

Navigate is objectively the brightest cue, but it is also the shortest and quietest. Skill Unlock
is the densest, but its rare one-button use and 750 ms cooldown keep that density appropriately
reserved. Minimum-cooldown same-cue trains remain below full scale at the all-high setting.

The last material above -60 dBFS occurs before every file boundary: 80, 150, 210, 440, 700, and
840 ms in cue order. Warning's 500 ms hold covers its 440 ms audible tail. Destructive and Skill
Unlock begin recovery during their quiet tails. When a long ducking voice ends after release has
already begun, the backend may restart a 260 ms ramp from its current attenuation; approximate
single-voice full recovery can therefore extend to 1.02 s for Destructive and 1.16 s for Skill
Unlock. This remains a listening item, not an objective failure.

### Extreme aggregate-headroom gate

A deliberately aligned four-channel SFX construction can reach about -0.42 dBFS before music at
100% Master and Effects. Adding full-level music can theoretically exceed full scale because the
four `MediaPlayer` voices have individual volume controls but no shared limiter. The same
construction at the default 80/70/40 mix remains conservatively below full scale, around -2.9 dBFS
with ducked music.

This is not evidence that ordinary menu use clips: the construction aligns rare, differently gated
semantics more tightly than a person can normally navigate them. It is evidence that the all-high
row in `AUDITION-CHECKLIST.md` must include rapid mixed-cue stress listening before release. If that
pass reveals clipping, reserve about 4 dB of aggregate headroom or add a shared limiter in a future
approved implementation change.

## Cue-placement audit

The final source map contains 84 explicitly tagged buttons across 27 XAML files:

| Cue | Tagged buttons |
|---|---:|
| Navigate | 30 |
| Confirm | 20 |
| Back | 24 |
| Warning | 4 |
| Destructive | 5 |
| Skill Unlock | 1 |

The map matches the pinned source expectation in `SettingsAudioRenderTests.cs`. High-frequency
editing, typing, row tools, sliders, toggles, tables, filters, screen entry, and passive career
events remain silent. `MusicPlayerControl.xaml`, `DeathScreenView.xaml`, and `SitOutView.xaml` have
zero cues. Skill Unlock remains exclusive to the genuine Dossier Unlock action.

The only correction was the two direct Skins force-overwrite buttons. Briefing remains deliberately
layered: `Warning` opens its force confirmation and `Destructive` executes the confirmed write.

## Automated evidence and build boundary

- Focused audio tests: **24/24 passed** against the existing Release test assembly with
  `--no-build`.
- Full render harness: **105/105 passed** against the existing Release test assembly with
  `--no-build`.
- The Skins semantic retag was applied after those compiled runs. A source-level XML assertion
  confirms the final five-cue Skins sequence is
  `Confirm, Destructive, Confirm, Destructive, Confirm`, the test source pins that same sequence,
  and the music player still has zero cues.
- Compiled validation of that two-line retag remains for the next approved build. No executable was
  generated to close that gate in this thread.

## Remaining sign-off

Complete the human sections of `AUDITION-CHECKLIST.md` on speakers and headphones, with particular
attention to the three music targets and the all-high mixed-cue stress case above. Until then, the
correct release decision is **technical pass / subjective acceptance pending**.
