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

## ▶ In progress — M6: era transition + content growth

- Era-transition engine: lineage carryover (`team.lotus`, `driver.j_clark`), age/rep/OPI
  carry, prestige + Budget-Unit rescale across packs, bridge-or-block for gap years.
- **f1-1969 pack** generated from your installed jusk F-Vintage_Gen2 set (skinpack already
  deployed) — the first real transition target: 1967 → bridge 1968 → 1969.
- Season-review "sign & continue" flow; release build v0.2.

## ⏭ Next (Phase 1 wrap)

- **F1 pack fleet:** mass-generate packs for every season your installed AI/skin sets
  already cover — 1974, 1978, 1985, 1986, 1990, 1991, 1992, 1993, 1995, 1997, 2000, 2012,
  2020, 2024 (the generator is proven; each pack is mostly data verification work). Seasons
  without installed sets fall back to pack-authored data — the format supports every era
  regardless of what's installed.
- Season-readiness score in the wizard (which seasons are fully playable with your content).
- Distribution: GitHub releases + OverTake.gg page.

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
