# Claude overnight build queue — Paddock depth (data/VM lane)

**Date:** 2026-07-12. Three Claude tasks. **Task 1 (Sponsors) is running now** in the session that
wrote this. **Queue Task 2 then Task 3** as new Claude Code sessions (paste the prompt block). All three
are Claude's lane: `Companion.Core` / `Companion.ViewModels` / `data/**` + tests — the DATA + projections
that feed Codex's GUI Round 3 (`docs/dev/codex-gui-round3-brief.md`). Stay out of `Companion.App/**` XAML
(Codex's lane). Build + full test green + commit each slice + republish RC (data-only changes are live in
`dist/` without a rebuild; VM/Core changes need a Release republish). Read the auto-memory
`ams2-hub-build-progress.md` TOP block first.

---

## TASK 1 — SMGP Sponsor system (RUNNING NOW in this session)

Fictional SMGP sponsors with stories + logos, surfaced as a **Sponsors** tab in the Paddock; designed to
roll into the future Tycoon mode. Deliverables: `data/rules/smgp/sponsors.json` (authored via a fan-out
workflow — ~24-30 sponsors: id, name, industry, tier, tagline, 2-3 para story, `logoKey`, brand colour
hex, the SMGP teams each backs, era flavour); a `SmgpSponsors` Core loader (mirror `SmgpTeamProfiles`);
`SmgpSponsorCard` DTO + `Sponsors` list added to `SmgpPaddockModel`; `CareerSessionService.SmgpPaddock()`
populates it (join sponsors → teams roster). Coherence + drift guards + a `SmgpTextQualityTests` entry for
the new file. Logos go at `dist/data/ams2/smgp/sponsors/<id>.png` (absent-tolerant).

---

## TASK 2 — Deeper Paddock data + an EVOLVING player narrative

```
Continue the AMS2 Career Companion (Z:\Claude Code\ams2-career-companion, branch hub/increment-4). Read
the auto-memory ams2-hub-build-progress.md TOP block, docs/dev/codex-gui-round3-brief.md, and
docs/dev/claude-continue-prompts.md first. You own the DATA/VM lane (Core/ViewModels/data + tests); do
NOT touch src/Companion.App/** (Codex's XAML lane). Build in slices, full test + commit + republish RC
after each, staying replay-byte-identical (never a fold input — these are display-only projections over
folded state, like SmgpPaddock/CurrentSmgpBriefing).

Mike wants the Paddock to be deep, clickable, and immersive, and asked specifically: "will the player
paddock story evolve over time?" YES — make it so. Build:

1. EVOLVING PLAYER NARRATIVE. Replace the static BuildPlayerBio with a career milestone TIMELINE that
   grows from folded results: detect and order the beats — arrived / first start / first points / first
   top-5 / first pole / first podium / first win / each promotion + demotion (SMGP seat moves) / each
   title / title defenses / rivalries earned (two-wins offers) and lost / career-over near-misses / the
   17-season campaign progress (SEASON n/17) / the finale. Expose an ordered list of
   SmgpCareerBeat { WhenLabel (e.g. "Season 3 · Round 7"), Kind, Headline, Detail } on the player's
   Paddock card (add to SmgpDriverCard or a new player-narrative projection), computed from
   ResultStore/CareerStore + the SMGP state. Also keep a short live prose intro that reflects the
   current standing. This is the "story that evolves over time."

2. HEAD-TO-HEAD + PER-DRIVER DEPTH. For each grid driver, expose vs-the-player data the GUI can click
   into: races met, times the player finished ahead/behind, current SMGP battle streak (from
   SmgpState.Tallies), best result together, and a per-track best-finish list. Add these to the driver
   card projection (e.g. SmgpDriverCard.HeadToHead, .PerTrackBest, .FormRecent — recent-N results
   trend). Display-only, from stored results.

3. TEAM DEPTH. Team cards: surface the team's live roster with each driver's season line, the team's
   sponsors (from Task 1's SmgpSponsorCard.Teams), tier, and palette. Cross-reference data so the GUI
   can link driver<->team<->sponsor.

Tests for every projection (a multi-season career shows a growing beat list; head-to-head counts from
stored results; determinism untouched). Update the memory TOP block + the Codex brief's "bind to X" list
as each property lands so Codex can wire it.
```

---

## TASK 3 — Rival-in-result-entry + data for History/Calendar/Standings/Driver screens

```
Continue the AMS2 Career Companion (Z:\Claude Code\ams2-career-companion, branch hub/increment-4). Read
the auto-memory ams2-hub-build-progress.md TOP block, docs/dev/codex-gui-round3-brief.md, and
docs/dev/claude-continue-prompts.md first. You own the DATA/VM lane (Core/ViewModels/data + tests); do
NOT touch src/Companion.App/** (Codex's XAML lane). Build in slices, full test + commit + republish RC
after each, replay-byte-identical (display-only projections only).

Provide the DATA behind Codex's richer screens + the rival drag-and-drop badge:

1. RIVAL ROW FLAG in result entry. ResultEntryViewModel already has RivalDriverId/RivalName +
   RivalStatusLine. Expose a per-row "is this seat the named rival" signal the view can bind on each
   draggable driver row (both the qualifying + race entry, classified + remaining lists) so Codex can
   show a red "RIVAL" badge by that name. The row model the view iterates (GridSeat) is Core — either
   add a lightweight row-VM wrapper carrying IsRival, or expose a predicate/HashSet the view can consult
   via a converter. Keep it display-only, keep existing ResultEntry tests green, add tests.

2. DEEPER RIVAL DOSSIER. On the rival screen model (SmgpRivalOption / SmgpBriefingModel), add the
   player-vs-this-rival head-to-head + this-season meetings + current streak so the dossier has real
   history (reuse Task 2's head-to-head if merged; else compute from stored results).

3. SCREEN DATA. Add the projections Codex's richer History / Calendar / Standings / Driver screens need:
   - Calendar: per-round detail (venue, laps, weather label, the round's grid size + DNQ field, setup
     note, championship flag, done/upcoming) as a clickable-round projection.
   - History: per-season detail beyond the current CareerTimeline cards (the "what really happened"
     almanac text per season for SMGP, records, streaks) surfaced for click-through.
   - Standings: ensure the player + named rival are flagged in the snapshot projection for highlight;
     add per-driver click-through detail if not already there.
   - Driver/Dossier: the evolving-narrative timeline (from Task 2) + progression, if Task 2 hasn't.

Tests for each. Update the memory + the Codex "bind to X" list as properties land.
```

---

**Coordination:** each Claude task appends its new bind-able properties to the Codex brief's §"contract"
list (or the memory) so Codex knows what exists. Tasks 2 and 3 have some overlap (head-to-head) — whoever
runs first builds it; the other reuses. All three keep the fold byte-identical (pure projections).
