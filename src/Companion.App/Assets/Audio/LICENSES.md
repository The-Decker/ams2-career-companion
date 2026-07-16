# Audio credits and provenance

All MP3 files in `Music/` are project-original tracks composed and supplied directly by Mike
(embedded artist tag: `kobra42`), the AMS2 Career Companion project owner, for inclusion in the
application. The initial library was supplied on 2026-07-12; nine additional tracks were supplied
on 2026-07-15.

- Source: direct contribution from the composer; no third-party audio source.
- Permitted use: AMS2 Career Companion application, its builds, and its distributable packages.
- Editing performed during import: filenames normalized only; audio bytes are unchanged.

The duplicated source names were given distinct catalog titles and matching normalized filenames:
`Morning Question(1)` became `First Light Briefing`; `Open Table(1)` became `Strategy Room`;
`Open Table(2)` became `Open Ledger`; and `Rain Before Rhythm(1)` became `Wet Line Reverie`.

The earlier numbered groups were normalized the same way: the first tracks now use the unnumbered
titles `Pitwall at Dusk`, `Grid Locked`, and `After the Flag`; the remaining tracks became
`Amber Pitlane`, `Telemetry at Twilight`, `Night Shift`, `Formation Hold`, `Cooling Lap`, and
`Empty Grandstands`. Every catalog title has a matching kebab-case MP3 filename.

Before a public release, replace this project-level credit with the composer's preferred display
name and any desired copyright notice. Do not add AMS2 audio, Formula 1 broadcasts or team radio,
SEGA/Super Monaco GP recordings, or other third-party material without a documented compatible
license and a new entry in this file.

## Pitwall 98 sound effects

The original eight WAV files in `Sfx/` were generated specifically for this project on 2026-07-13
by `tools/generate_sfx.ps1`. `seat-confirm.wav` was generated on 2026-07-15 by the tracked
`Audio/Generation/generate-seat-confirm.ps1` source. All contain only mathematical oscillators and
original decaying FM synthesis shaped into a restrained retro interaction language.

On 2026-07-15, `navigate.wav` was re-synthesized in the same tracked generator as a quiet two-stage
mechanical mouse-like click. Its relay texture is seeded deterministic noise plus oscillators, not a
mouse recording, sample-library asset, or copied interface sound.

- Source: repository generator; no recordings, third-party samples, or sample libraries.
- Style reference: general late-1990s desktop UI chimes only; no Microsoft/Windows sound was sampled,
  traced, recreated, or used as source material.
- Permitted use: AMS2 Career Companion application, its builds, and its distributable packages.
- Reproduction: run `.\tools\generate_sfx.ps1`, then
  `.\src\Companion.App\Audio\Generation\generate-seat-confirm.ps1`, from the repository root.
- Editing performed after generation: none.
