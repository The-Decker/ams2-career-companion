# Soundtrack mastering record

Audit dates: 2026-07-13 for the original library and 2026-07-15 for the nine added tracks.
Measurements use FFmpeg EBU R128 (`ebur128=peak=true`); silence detection
uses -50 dB for at least 0.1 seconds. `TP` is decoded true peak, not an MP3 frame/header estimate.

The shipped MP3 bytes remain unchanged. The player applies conservative per-track attenuation before
the persisted Music and Master volume controls. This avoids another lossy encode while bringing the
playlist to approximately **-14.5 LUFS-I** with decoded peaks at or below **-1.5 dBTP**.

| Track | Source LUFS-I | LRA | Source TP | Player trim | Effective LUFS-I | Effective TP |
|---|---:|---:|---:|---:|---:|---:|
| The Long Climb | -13.01 | 4.2 | -0.86 | -1.49 dB | -14.50 | -2.35 |
| Pitwall at Dusk | -13.51 | 7.1 | -0.56 | -0.99 dB | -14.50 | -1.55 |
| Amber Pitlane | -12.88 | 5.3 | -1.45 | -1.62 dB | -14.50 | -3.07 |
| Telemetry at Twilight | -13.16 | 6.0 | -0.42 | -1.34 dB | -14.50 | -1.76 |
| Night Shift | -12.37 | 5.6 | -0.67 | -2.13 dB | -14.50 | -2.80 |
| Race Control | -13.13 | 2.8 | -2.28 | -1.37 dB | -14.50 | -3.65 |
| Grid Locked | -14.24 | 4.8 | -2.96 | -0.26 dB | -14.50 | -3.22 |
| Formation Hold | -12.49 | 4.1 | -1.14 | -2.01 dB | -14.50 | -3.15 |
| After the Flag | -13.90 | 1.3 | -3.10 | -0.60 dB | -14.50 | -3.70 |
| Cooling Lap | -13.60 | 2.2 | -2.40 | -0.90 dB | -14.50 | -3.30 |
| Empty Grandstands | -13.20 | 3.9 | -1.70 | -1.30 dB | -14.50 | -3.00 |
| Super Monaco Grand Prix Intro | -12.80 | 3.0 | -1.70 | -1.70 dB | -14.50 | -3.40 |
| First Light Briefing | -14.30 | 2.1 | -2.10 | -0.20 dB | -14.50 | -2.30 |
| Morning Question | -12.90 | 3.2 | -0.60 | -1.60 dB | -14.50 | -2.20 |
| Lights in the Distance | -12.90 | 5.1 | -1.40 | -1.60 dB | -14.50 | -3.00 |
| Open Table | -12.40 | 3.7 | -0.90 | -2.10 dB | -14.50 | -3.00 |
| Strategy Room | -13.00 | 2.5 | -1.40 | -1.50 dB | -14.50 | -2.90 |
| Open Ledger | -13.40 | 2.7 | -1.70 | -1.10 dB | -14.50 | -2.80 |
| Rain Before Rhythm | -13.00 | 6.8 | -0.90 | -1.50 dB | -14.50 | -2.40 |
| Wet Line Reverie | -13.80 | 5.6 | -1.60 | -0.70 dB | -14.50 | -2.30 |
| Golden Lap | -12.90 | 6.1 | -0.70 | -1.60 dB | -14.50 | -2.30 |
| Injury | -14.00 | 5.0 | -1.50 | -0.50 dB | -14.50 | -2.00 |
| Injury / Death | -12.40 | 3.8 | -0.60 | -2.10 dB | -14.50 | -2.70 |

## Format and edit notes

- All 23 files are MP3, 48 kHz, stereo; none measured as clipped.
- `The Long Climb` has about 1.33 seconds of trailing silence. `Telemetry at Twilight` has about
  0.15 seconds of leading silence. No other qualifying edge silence was found.
- Embedded title tags are not used by the app. Several source tags are duplicated or inaccurate;
  the player uses the audited display titles in `SoundscapeCatalog` instead.
- Future lossless masters should target -14.0 LUFS-I (+/-0.5 LU), no higher than -1.5 dBTP, while
  preserving their natural loudness range. When a replacement master already meets that target,
  remove its player trim rather than normalizing twice.
