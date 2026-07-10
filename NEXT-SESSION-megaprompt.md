# NEXT SESSION — finish the SMGP mode, then beautify

Resume the AMS2 Career Companion (`Z:\Claude Code\ams2-career-companion` — WPF/.NET 10, single
self-contained exe). **READ MEMORY FIRST** (`MEMORY.md`, then `ams2-hub-build-progress.md` TOP —
the ⭐⭐⭐⭐ 2026-07-10 SECOND-session block is current), then `docs/dev/smgp-design.md` (the
manual-verified mode design this plan implements).

## STATE (verify with `git log`)

Branch `hub/increment-4`, head **`1b3c7c6`**, pushed, 208 commits ahead of `main`. Suite **1690 +
38 render green; oracle 77/77**. RC `dist/AMS2CareerCompanion.exe` = **0.6.0+6d276ce**. M1 (skins
foundation) + M2 (max grids + skinpack rosters, all 14 packs + juppo scalar schema) are DONE and
shipped. **SMGP FOUNDATION is done:** `packs/smgp-1` plays as a normal season now; the mode's pure
rules `Companion.Core/Smgp/SmgpRules.cs` (22 tests) and the envelope v6 `SmgpRivalCall` block are
in. This session WIRES the mode through the fold + UI, then starts M4.

## BUDGET DISCIPLINE (read this — it is the point of this session)

The Fable usage budget is LIMITED this session. The rule: **work sequentially, commit AND push
every slice, and DO NOT launch large parallel agent workflows** (a 28-agent fan-out is what burned
the budget last time). All of the work below is ordinary sequential coding — the `SmgpRules` and
envelope-v6 slices already done are the exact cadence. Each slice is independently valuable and
lands on its own, so when the budget runs low, STOP after the current slice's commit+push — the
branch is always in a clean, green, shipped state. Only if budget clearly remains at the very end:
one SMALL (≤4-agent) adversarial review of the session's diff, nothing larger. No `dotnet publish`
until the app is confirmed closed (and only near the end).

## MISSION 3 — wire the SMGP replica mode (do these IN ORDER, one commit each)

The rules already exist in `SmgpRules.cs`; the raw inputs already ride the envelope
(`RoundResultEnvelope.SmgpRival`, v6). What remains is folded STATE + the resolver + the UI. The
gating precedent is **RATINGS PHASE 3 / FormAware** (a per-career flag, NOT "pack has the style",
so existing careers stay byte-identical) and the **called-shot gamble** (versioned envelope row +
determinism gate). Keep the oracle UNTOUCHED — it never resolves grids or folds these rows.

1. **Folded `SmgpState` on `PlayerCareerState`** (`[JsonIgnore(WhenWritingNull)]`, so non-smgp
   careers serialize byte-identically). Fields: `CurrentSeatLivery` (the player's car this season —
   changes on a swap), `Tallies` (rivalDriverId → `SmgpBattleTally`), `Titles`, `CareerOver`, and
   `AiSeatOverrides` (driverId → livery, for the displaced-driver reshuffle). Seed it at career
   creation ONLY when `Pack.Manifest.CareerStyle == "smgp"` **and** a new
   `CareerCreationRequest.SmgpMode` flag (wizard sets it for an smgp pack) — mirror `FormAware`
   exactly, including the `with`-carried-forward path so rollover/season-end re-derive identically.
   Default absent → the whole mode is inert. Test: an smgp career seeds state; a normal career
   (and any existing career) has null state and folds unchanged.

2. **Fold the battle** (`ReplayService.ComputeRoundFold` / `RoundUpdate`). When
   `envelope.SmgpRival` is present AND the career carries `SmgpState`, compute the battle outcome
   from the stored result (player finish vs the named rival's finish — both derivable from the
   `RoundResult` classification; DNF = null position), apply it via `SmgpRules.ApplyBattle`, and on
   a `SeatSwapOfferToPlayer` trigger with `SeatSwapAccepted == true`, apply `PlayerSeatSwap` into
   `SmgpState` (update `CurrentSeatLivery` + `AiSeatOverrides`); on `PlayerSeatForfeit` demote (or
   set `CareerOver` when `IsCareerOver`). Emit journal events (a Why?-inspectable row per battle).
   Gate: no `SmgpRival` → no event → byte-identical. **Determinism test** (the load-bearing one): a
   career carrying rival calls folds the battles AND re-simulates byte-identically via `Resimulate`.

3. **Resolver seat overrides** (`RoundGridResolver`). New optional param
   (`IReadOnlyDictionary<string,string>? seatOverrides` = driverId → livery) applied AFTER the cap,
   plus the player driving `SmgpState.CurrentSeatLivery`. Default param → no change → byte-identical;
   the oracle never passes it. `CareerSessionService` threads `SmgpState` in when the mode is on.
   Test: a swap reseats the three affected cars and nobody else; off-path is identical.

4. **Forced-challenge schedule + the Ceara title defense.** A small pure helper (`SmgpSchedule` in
   Core, tested): given the season year-in-career + titles + the player's team, return this round's
   forced challenger (the Madonna title-defense season force-challenges via **G. Ceara at R1 and
   R2**; `SmgpRules.TitleDefense` resolves at R2 → keep Madonna or fired to Dardan). Wire the
   season-start seat assignment (champion → Madonna) into the carryover/rollover path, gated on the
   mode. Test the two-titles completion + the fired-to-Dardan branch.

5. **Briefing rival panel + presentation** (`BriefingViewModel`/`BriefingView`, mode-gated so
   normal packs never show it). A rival panel: pick-a-rival (any team) / decline, forced-challenge
   display, and a rival **dossier card** (team banner + a MACHINE block [engine/power from the
   pack] + portrait slot + a deadpan one-line quote). Round header format **"SAN MARINO · ROUND 1"**;
   the qualifying label is **"PRELIMINARY RACE"** (NEVER "Super License"); points readout abbreviated
   **"D.P."**; a pit-crew advice line. Vocabulary STRICTLY from `docs/dev/smgp-design.md` — invent
   nothing. New user-asset slots under **`data/ams2/smgp/`** (mode hero, rival portraits, team
   banners, round cards) via the existing `UserImageResolver`; document them. A render test hosts
   the panel with a stub smgp DataContext. This is the biggest UI slice — if the budget is tight,
   land slices 1–4 (the actual mechanics) first and treat this as the stretch goal.

After the mechanics land, note in the commit that the mode is playable end-to-end.

## MISSION 4 — beautification (only if budget remains; each its own commit)

Lower priority than finishing M3, and open-ended (Mike will want eyes on the aesthetics). Order:
(a) **main menu landing screen** before the career gallery (New career / Continue / Modes incl.
SMGP / Settings, background-art slot — the app's first "front door"); (b) **career-gallery card
parity** with the season-picker (UniformGrid + AspectHeight hero, adaptive columns — reuse the
existing converters); (c) **theme templates** (`data/ams2/themes/<name>/`, two-tone F1 background+
accent, reaching Panel/Bg brushes not just accents; a couple of complete themes, Settings-selectable);
(d) **MotionAssist extensions** (hover glows on cards, springy expanders, subtle hero parallax —
restraint over spectacle); (e) a Settings **"User art"** panel listing every asset folder.

## CONSTRAINTS (unchanged, load-bearing)

CRLF+2-space+no-BOM pack/data files; sim-inert vs determinism-gated discipline (new fold rows =
envelope version [already at v6] + per-career gate; grid/roster changes = pack data = new careers
only); NEVER touch the oracle; no `git add -A` (stage named paths); era-art/venue-photos/user
assets never committed; **republish the exe only when the app is closed** (timestamped backup;
`dotnet publish src/Companion.App -c Release -o dist`) and only near session end; commit AND push
every slice; no `gh` CLI (PR #4 is Mike's). Scratchpad `scratchpad/rosters/*.json` holds the 14
applied M2 plans (provenance) — leave them.

## LEFTOVERS (pick up only if everything above is done + budget remains)

The M1 adversarial review died on the usage limit last session (journal `wf_685d22d4-2ad` has only
starts) — a fresh ≤4-agent pass over the M1 diff `c974fc6..358591b` would close it. SkinsViewModel
season-panel tests are thin. Backlog seasons (1983/1996/1998/2010/2012) and 1975-via-manager remain
parked.
