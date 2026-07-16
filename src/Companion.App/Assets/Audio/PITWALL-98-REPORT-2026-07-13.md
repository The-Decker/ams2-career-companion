# Pitwall 98 SFX report - 2026-07-13

**Status:** source and asset checks pass; compiled and human audition gates remain open

**Release boundary:** no build, publish, `dist` copy, or executable replacement was performed.
During final validation, `dist/AMS2CareerCompanion.exe` was observed changing concurrently from
126,360,322 bytes (12:49 UTC) to 185,744,213 bytes (16:48 UTC). That publish came from outside this
task; this report does not assume the Pitwall 98 source changes are present in that executable.

## Outcome

The previous six Mechanical Memory WAVs were resynthesized into one original late-1990s desktop UI
language. Two quiet result-entry cues were added:

- `BucketPickup` plays once when a valid car drag crosses the WPF drag threshold.
- `BucketPlace` plays only when the drop successfully changes Order, DNF, DSQ, or Remaining.

Qualifying and race-result entry share `ResultEntryView` and `ListDragDropBehavior`, so the same pair
works in both sessions. Cancelled, invalid, and unchanged drops have no Place cue. The sounds contain
no Microsoft/Windows samples or traced melodies; all audio comes from the tracked mathematical FM
generator.

## Current masters

| Cue | Duration | Peak | RMS | Crest | Power centroid | Runtime gain |
|---|---:|---:|---:|---:|---:|---:|
| Navigate | 120 ms | -12.0 dBFS | -25.59 dBFS | 13.59 dB | 1,220 Hz | 0.42 |
| Confirm | 320 ms | -10.5 dBFS | -21.11 dBFS | 10.60 dB | 1,501 Hz | 0.60 |
| Back | 280 ms | -11.0 dBFS | -21.97 dBFS | 10.97 dB | 605 Hz | 0.50 |
| Bucket Pickup | 140 ms | -12.5 dBFS | -25.47 dBFS | 12.96 dB | 807 Hz | 0.40 |
| Bucket Place | 220 ms | -11.5 dBFS | -23.83 dBFS | 12.33 dB | 589 Hz | 0.46 |
| Warning | 520 ms | -10.0 dBFS | -22.01 dBFS | 12.01 dB | 720 Hz | 0.70 |
| Destructive | 720 ms | -9.5 dBFS | -20.43 dBFS | 10.93 dB | 542 Hz | 0.72 |
| Skill Unlock | 900 ms | -9.5 dBFS | -21.02 dBFS | 11.52 dB | 936 Hz | 0.72 |

Every file is 48 kHz, 16-bit mono PCM with zero boundary samples and negligible DC. Energy above
4 kHz is below 0.1% for every cue. The last 10 ms block above -60 dBFS ends before each file
boundary; no truncated tail or objective high-frequency harshness signal was found.

The frequent bucket pair remains conservative at full Master/Effects: Pickup peaks near -20.46 dBFS
after cue gain and Place near -18.24 dBFS. The four loudest independently aligned SFX voices sum to
approximately -1.1 dBFS before music. Default 80/70/40 operation retains comfortable aggregate
headroom; the documented all-high mixed-cue listening gate remains necessary because WPF has no
shared App-layer limiter.

## Deterministic integrity

A clean temporary regeneration produced byte-identical SHA-256 hashes:

| File | SHA-256 |
|---|---|
| `navigate.wav` | `7651f772e930905d79d3e9ccb750cf537de35e2f2c756c1d60d9235c1eae470f` |
| `commit.wav` | `3e86f9d1bf921d873a074385ea6d4a7a24104bb47f452cd7e05cb373abc1216a` |
| `back.wav` | `eb9125635cfc89dc58b7e0c49ac7b0c3cfd0fbe8e98927d12cacd1df7e76a013` |
| `bucket-pickup.wav` | `1adf3f90f8d602540f47a92ad3cf3d5a1432ad17ed4ef50cba7d85a34148d275` |
| `bucket-place.wav` | `d10b6574c93f9933e45aba4e86141f668602dbfad5efdb39860c4432567d9fb0` |
| `warning.wav` | `96c817f595a643de86bf118592cd9471af4debb0fd07440de48a1ae3f4607a61` |
| `destructive.wav` | `bc1f07ff77634d160cda9f18a2d4d8a7a73b5c82f76d2a8ccc4816856594fb5e` |
| `skill-unlock.wav` | `776625f212634d3ee53fb87a12d5f587bfdd75b4412d87a30a4aa04de53f0ada` |

## Source contract

- `SoundscapeCatalog` declares eight semantic WAVs and 24 total audio assets.
- Bucket cues share the `bucket` anti-chatter group and never duck music.
- `SoundAssist.Play` provides the fail-soft non-button bridge.
- `ListDragDropBehavior` requests Pickup after the threshold and Place behind a successful `moved`
  result. It does not observe career state or session type.
- Render-harness source expectations pin the eight formats, gains, cooldowns, catalog paths, and
  result-entry behavior calls.

Compiled validation is intentionally deferred because this thread is not authorized to build or
replace an executable.

## Human audition priorities

1. Drag cars Remaining -> Order, DNF, and DSQ in both qualifying and race entry.
2. Reorder an already-classified car and return an Out car to Remaining.
3. Cancel a drag and try an unchanged drop; confirm there is Pickup but no false Place.
4. Repeat ten fast car placements at default mix; Pickup/Place must remain light and distinct.
5. Compare Confirm, Warning, and Destructive so the new chime family still communicates consequence.
6. Run the 100/100/100 mixed-cue stress row from `AUDITION-CHECKLIST.md` and listen for endpoint
   distortion before approving release.
