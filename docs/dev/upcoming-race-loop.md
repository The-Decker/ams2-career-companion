# The "Upcoming Race" loop — GUI restructure (Mike, 2026-07-11)

Mike's vision for the race-day experience: turn the Race tab into a single, sequential, multi-part
**"Upcoming Race"** flow — each stage its own screen — and lock it out of the tab rail so it's reached
only when you're actually racing (via the header loop button). This is the SMGP career loop made
explicit, screen by screen.

## The loop (Mike's own words, sequenced)

1. **Upcoming Race tab** — renamed from "Race"; locked out of the tab rail (reached only via the header
   loop button). You *set up the race* in this tab.
2. **Race setup** — the current briefing (track / class / sessions / rules). Where you set the race up.
3. **Rival screen** — "the rival in the race moves to the next screen after the race setup." The rival
   dossier (car + person + the whole interface) gets **its own screen/window** so it can be expanded —
   the SMGP rival card is currently embedded in the briefing; pull it out as a distinct step.
4. **Qualifying screen (Preliminary Race)** — the qualifying-order entry (already exists).
5. **Race grid start screen** — the starting grid, driver + car cards, two wide (SHIPPED — `StartingGridView`).
6. **Race results** — the race result entry (already exists).
7. **Confirmation screen** — the confirm interstitial (already exists).
8. **Promotion / Demotion screen** — after confirmation: confirm whether you're **promoted** or
   **demoted**. You *choose whether to accept a promotion* (offered after you beat your rival twice); you
   **cannot decline a demotion**. A **photo shows the new team** (promoted or demoted, depending on which
   team it is).
9. **Loop** back to the Upcoming Race tab for the next round.

## Build status (slices)

- **3a — Rename + rail lock — SHIPPED** (`b23c11b`). The Race tab is "Upcoming Race" with
  `HubTabViewModel.ShowInRail=false`; the rail collapses it; the header "Race day" button is renamed
  "Upcoming Race". Both header loop buttons already select the tab, so it stays reachable.
- **Starting grid — SHIPPED** (`cb93e3f`, part of stage 5). After qualifying "Set the grid", the
  `StartingGridView` shows the grid pole-first (driver + car cards, two wide) with a "Start the race"
  button before the race entry.
- **3b — Rival screen as its own step — SHIPPED.** `RivalScreenViewModel` wraps the SHARED
  `BriefingViewModel` (so the pick / "name him" state and `BuildSmgpRival()` are unchanged);
  `RivalScreenView` holds the SMGP rival dossier moved out of `BriefingView` (expanded portrait + the
  car-spec card). `HomeViewModel` gains `IsRivalStep` + `ShowRival()`; `EnterResult` shows it FIRST for
  an SMGP career (gated on `Briefing.SmgpActive`), then "Continue" advances to qualifying —
  briefing → rival → qualifying → grid → race → confirm. Non-SMGP careers skip it (byte-identical).
  Also fixed: the starting-grid screen had no visible advance button (the confirm button lived only in
  `ResultEntryView`) — added it to both the grid and rival screens.
- **3c — Promotion / Demotion screen — TODO (biggest; determinism-sensitive).** After the confirm/apply,
  when the folded SMGP state produced a seat change this round, show a promotion/demotion screen:
  - Promotion (player beat the rival twice → `SmgpState` seat swap up a tier): the player **accepts or
    declines** (a folded choice — must be journaled as an INPUT + provenance-excluded + replay-exact, like
    an accepted offer / the called shot; declining keeps the current seat).
  - Demotion (2-loss forfeit → relegated a tier): **forced**, no decline.
  - A **team photo** of the new team (promoted/demoted) — a `player.<team>` / team image, absent-tolerant.
  This rides the existing SMGP seat-swap fold (`SmgpBattleFold` / `SmgpSchedule`); the accept/decline is a
  NEW folded input, so it needs a determinism gate (a promotion career + its skip-everything equivalent
  both byte-identical). Design the fold seam before the UI.

## Guardrails

- The whole flow is per-round, inside the Upcoming Race tab's `HomeViewModel` content switch
  (`CurrentContent`), the same seam the briefing/qualifying/grid/race/confirm states already use.
- Display-only stages (rival screen, starting grid, the team photo) never touch the fold — replay-safe.
- The promotion accept/decline is the ONE new fold input; gate it like every other SMGP input.
- Keep a non-SMGP / character-free career byte-identical: the rival + promotion steps are SMGP-gated.
