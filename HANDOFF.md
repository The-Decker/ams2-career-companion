# HANDOFF — resume this project on the desktop (where AMS2 is installed)

**Date:** 2026-07-02 · **From:** Mike's work laptop · **State:** plan approved, zero code written yet.

## What this project is

The AMS2 Historical Career & Team Tycoon Companion — a lightweight single-exe Windows app that runs
historical seasons (F1 1967 onward, other series later) around AMS2 single-player custom races:
it generates the in-game AI grids (real driver names/ratings via Custom AI XML files), records results,
computes era-correct standings, and runs a deterministic career/tycoon sim on top.

Read **PLAN.md** first — it is the full approved product plan (context, locked decisions, architecture,
phased roadmap, verification strategy). It was approved by Mike on 2026-07-02.

## Decisions already locked (do not re-ask)

1. **Start era:** Formula Vintage, late 1960s (F-Vintage Gen1 ≈ 1966–67).
2. **Results:** manual-first keyboard entry in v1 (<90s/race target); shared-memory auto-capture in Phase 2.
3. **AI grids:** the app generates AMS2 Custom AI XML files before every round (killer feature, in MVP).
4. **Player roles:** Driver Career AND Team Owner-Driver both exist; v1 ships Driver Career, Owner-Driver in Phase 3.

## What's in this folder

- `PLAN.md` — the approved plan. The single source of truth.
- `docs/research/research-workflow-output.json` — the complete raw output of the 9-agent research/design
  pass (5 web-research agents with sources/uncertainties + 3 design proposals). ~170 KB JSON. Verbatim
  schemas, URLs, field lists, competitor analysis. Parse with any JSON tool; content is under `.result`.
- `docs/research/RESEARCH.md` — human-readable digest of the same, with the reference tables you'll
  need constantly (Custom AI XML schema, class→season map, scoring rules per era, capture options).

## How to resume on the desktop

1. Copy this whole folder to the desktop (it's a git repo — history included).
2. Open Claude Code in the folder root.
3. Say something like:

   > Read HANDOFF.md, PLAN.md and docs/research/RESEARCH.md. This plan is already approved — continue
   > from Phase 0: install the .NET SDK (winget), scaffold the solution per the Architecture section,
   > then start Milestone M1 (points engine + f1db oracle tests). AMS2 is installed on this machine.

4. Useful extras now that AMS2 is local:
   - Verify the real install path: `<Steam>\steamapps\common\Automobilista 2\UserData\CustomAIDrivers\`
   - Extract the CURRENT class/track/livery lists from the local install (research libraries are 1.5.6.3-era;
     v1.6.9 renamed several classes — see RESEARCH.md).
   - Check which DLC / skin packs are already installed to pick the reference-pack targets.

## Machine notes

- Work laptop (this machine): NO .NET SDK installed — deliberately not installed here; project moved before Phase 0.
- git 2.55 was available here; repo initialized with the docs committed.
- Desktop: has AMS2 (Steam) — needs .NET SDK (latest LTS) via winget as first step.
