# Roadmap — AMS2 Career Companion

Updated 2026-07-03. PLAN.md holds the approved product plan and the eight locked decisions;
this file tracks where we are against it. Test suite at last checkpoint: **785/785**.

## ✅ Done (2026-07-02 → 07-03)

| Milestone | What shipped |
|---|---|
| Phase 0 — toolchain | .NET 10 solution (Core/Data/Ams2/ViewModels/App/Tests + 4 tools), single-exe publish pipeline |
| Machine ground truth | Full content extraction from the local install (540 vehicles / 194 classes / 294 tracks with AI caps / 3,850 liveries), DLC + skin-pack inventory, v1.6.9 class-rename corrections |
| M1 — points engine | Data-driven scoring (era tables, split best-N, shared drives, FL splits, half/double points, eligibility redistribution, penalties/exclusions), exact rational math, **oracle-verified against all 77 F1 seasons 1950–2026** |
| M2 — season packs | Pack format v1.1 (100% race distance, per-round setup guides, first-class placeholder venues), loader + structural/content validators, **1967 + 1988 reference packs** generated from f1db + the installed community AI sets, exhaustively verified |
| M3 — grid generation | Pack + round → custom-AI XML: rounds ranges, swaps, per-round overrides, player seat; preflight (class casing, livery bindings, AI caps); backup-first staging; proven 0-error against the real install |
| M5 — career sim | Deterministic PCG32 streams, OPI/reputation/pace-anchor, 7-step season end (aging, retirements, seat market, era-correct offers, tier drift), 1960s headline bank, **byte-identical replay** |
| Integration | Unified per-round fold (rep/OPI actually price offers), lossless replay divergence, **NAMeS-first** (installed AI files as baseline, diff-aware staging, one-click restore) |
| M4 + UX — the app | Wizard → checklist briefing (always-on-top mode) → result entry (keyboard grammar ≤120 keys for 26 cars **and** full drag-and-drop sharing one undo stack) → confirm with generated headlines → standings/round matrix → season review with offers; settings, coach marks, crash hardening. **User-verified working 2026-07-03.** |

## 🏁 Test gates (you)

- **Gate 1 — First Race Weekend (ready NOW):** new 1967 career → stage the grid (your
  NAMeS file backs up automatically; restore is one click) → follow the briefing checklist
  in AMS2 (drop the laps for a smoke test — 100% distance is the career default, not a test
  requirement) → race → type/drag the result in → check the standings feel right.
- **Gate 2 — Full Season & Era Transition (after M6):** finish a season → review + offers →
  sign for 1969 → verify carryover (age, rep, team lineage) and the new season's grids.
- **Gate 3 — Auto-capture (Phase 2):** race with shared memory on; the result screen
  pre-fills itself.

## ✅ M6 — era transition + first content growth (2026-07-03, v0.2.0, 822/822)

- Era-transition engine: lineage carryover, age/rep/OPI carry, 1968 bridged (deterministic
  aging/retirement per gap year; Jim Clark departs, f1db-verified), cross-transition
  byte-identical replay.
- **f1-1969 pack**: 26/26 liveries bound to the installed jusk set, per-track overrides
  carried, every f1db 1969 entrant included or coverage-noted.
- Season review "Sign & start 1969" flow with the bridge note; reopen lands in the latest
  season. **→ Gate 2 is ready to test.**

## ✅ v0.3.0 — UX fixes + F1 pack fleet (2026-07-03, 895/895)

- **Fixes from your manual test:** lenient livery scan (the warning flood → one summary
  line; 84 quirky community files now recover), staging force-gate is an amber choice not a
  red failure, historical constructor names everywhere.
- **Pack fleet — 8 seasons now bundled:** 1967, 1969, **1986, 1990, 1992, 1993, 1997**, 1988.
  Each verified: liveries 100% verbatim in your installed sets, era-transition-ready lineage
  ids. (1974/1978/1991/1995/2000 attempts hit a usage limit mid-run — no partial garbage
  written; they regenerate cleanly next round.)
- **Schema correctness:** only raceSkill + qualifyingSkill are required now; the other stats
  are optional, matching the game format (jusk's 1986 set omits start_reactions) — which
  keeps NAMeS-first no-op staging honest.
- Directory-driven pack tests: every future pack is auto-held to the exemplar bar.

## ▶ In progress / next

- **Career hub design round** — immersive management hub (tabs, minigames, era presentation)
  per docs/dev/career-hub-vision.md; produces a spec for Mike to steer ("create together").
- **Remaining fleet:** 1974, 1978, 1991, 1995, 2000 (installed sets exist; just needs a
  re-run). 1985-on-F-Retro_Gen3 and 1975 are `skip` (off the class's realSeasons / wrong
  season set). 2001/2012/2024 are ai-only (safety-car-only skins — would warn on liveries).
- Season-readiness score in the wizard; distribution (GitHub releases + OverTake.gg).

## 🔭 Later (per PLAN.md phases)

- **Phase 2:** shared-memory auto-capture (`$pcars2$` end-of-session snapshot pre-filling
  result entry), Second Monitor import; full team ledger economy → bankruptcy crisis ladder;
  living world (mid-season driver market, personal sponsors, rumors).
- **Phase 3:** Team Owner-Driver mode (budget allocation, sponsor negotiation, second-seat
  hiring, bankruptcy fail state), privateer flavor, rivalries, hardcore aging toggle.
- **Phase 4 — "spans everything":** season-pack creator GUI + in-app pack browser (the
  community fills the long tail); non-F1 series careers — F-USA/CART 1995/98/2000 (your AI
  sets are already installed), Group C, GT1, DTM Group A, stock car; junior ladder
  (karts → F-Vee/F-Trainer → F-3 → F1); guest drives (Indy 500, Le Mans); AMS2CM interop;
  localization.

## Content coverage philosophy

Everything is data: rules, calendars, ratings, placeholder venues, aging curves. The app
never gates on content — any class in `data/ams2/classes.json` (all 194) can host a season
pack today. Coverage grows three ways: bundled reference packs (us), generated packs from
installed community sets (automated), and community-authored packs via the Phase-4 creator.
