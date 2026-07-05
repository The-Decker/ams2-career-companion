# Career hub build plan (2026-07-05)

The incremental, **additive** build sequence for the immersive Career Hub, derived from the locked
decisions in `docs/dev/career-hub-design.md` (23-question elicitation, 2026-07-05). Companion to that
spec — read it first.

**Current shipped state:** v0.3.4, suite **1032/1032**, 13 season packs, the full single-race career
loop working and user-verified. Ctrl+Z-after-mech/acc **confirmed** (render-harness 15/15, live key
path). Per-class grid cap: **none exists** — the participant limit is a per-track `.trd` property,
already extracted (`tracks.json → maxAiParticipants`) and enforced by `GridPreflight`.

## Iron rules for every increment (non-negotiable)

1. **Additive behind the `ICareerSession` seam.** New members are **default-implemented** (read
   projections). *A non-default seam addition is the red line* — with **one scoped, gated exception**:
   Increment 2's weekend model gives `Apply`/`Preview` an optional `SessionId` (back-compat default =
   the sole race session), a contract change to existing members covered by a byte-identical
   seam-fidelity replay test. No other increment touches an existing member's contract.
2. **The shipped loop and the sacred tests never regress** — keyboard grammar, keystroke budget,
   drag-and-drop, parity, and the f1db oracle suite are the acceptance gate. Existing tests are the
   contract.
3. **Determinism is CI-enforced** — every new stream/phase is covered by the byte-identical replay
   test *before* it ships. The 13 bundled packs must still reproduce byte-identical after any stream
   is reserved.
4. **Mouse + keyboard parity (decision 8)** on every new surface — click target + tooltip AND keybind.
5. **Version-bump + publish each increment** (0.3.x continues for patches; **v0.4.0 is tagged at the
   Gate** after Mike playtests the hub end-to-end). Later milestones bump onward (character system ≈
   v0.5.0).

---

## The increment ladder

```
v0.3.4  ── shipped: single-race loop, 13 packs, 1032 tests
  │
  ├─ Increment 1  The Hub Shell + News ticker + EraTheme + Career Gallery      [zero sim, zero migration]
  ├─ Increment 2  The Race Weekend model (Practice→Qualy→Race1→opt Race2)      [first sim + migration]
  ├─ Increment 3  Total-recall History + clickable-everywhere Why? inspector    [read-only over state]
  │        ── GATE: Mike playtests → tag v0.4.0
  │
  ├─ Increment 4  Character system + Setup Gamble + Contracts-as-documents + Normal/Hardcore  ≈ v0.5.0
  │
  └─ Phase 2+     Team lens + ledger economy + qualy→tycoon signals + negotiation + auto-capture
                  + Super-Monaco rivalry/promotion + Paddock + Owner-Driver
```

**Cross-cutting workstream (all increments):** the **procedural news engine** — starts as a thin
grammar over the existing 1960s `HeadlineBank` in Increment 1 and grows corpus-by-corpus per era.

---

### Increment 1 — The Hub Shell + News ticker + EraTheme + Career Gallery  *(v0.4.0 base; zero sim, zero migration)*

Re-home the loop **verbatim** and ship the first visible immersion payoff. Nothing here touches the
sim or the schema.

- **`HubViewModel`** owns the existing `ICareerSession`; move today's
  `Home/Briefing/ResultEntry/Confirm/Standings/SeasonReview` into the **Race** and **Standings** tabs
  **unchanged** (existing tests = the gate; the loop cannot regress).
- **Persistent header** + **always-visible primary loop button** ("Next: <round> — <session>" /
  "Enter Result") above the rail; Race auto-selects on open and after every Apply.
- **News tab + right-dock ticker (scoped to what the shipped journal backs):** `ReadFeed(seasonId)`
  re-renders the **existing** `news.headline` rows (one per race today) through `EraTheme` into an
  era-styled **ticker**; clicking a headline expands the **full period article** layout. Per-item
  **Why? chip** (contained scope — `deltaJson` → plain sentence). *The rich generative multi-slot
  grammar (winner/gap/rival/team facts) is a LATER slice* — the thin `race.result` row lacks those
  facts, so they come from an `AllSnapshots()` projection (free) or a new journaled field (migration).
  Increment 1 does **not** ship "thousands of dispatches."
- **`EraTheme` resource-dictionary swap** (telegram/fax/email) keyed off the pack decade + `era-themes.json`;
  **immersive docs, legible tools** (skin documents/chrome/accent; keep standings + result-entry on a
  legible base face). Wire the **immersion-settings** surface (spectrum, replacing the binary toggle).
- **Career gallery** on Start: recent careers as cards (era/driver/season/standing), rename/duplicate/
  delete, per-era image slot (`data/ams2/era-art/`, generated placeholder when absent).
- **Tear-off** News/Scrapbook to always-on-top companion windows (reuse the briefing mechanism).
- **New seam members (default-implemented, read-only):** `ReadFeed`, `TabAvailability`.
- **Tests/gate:** all existing ViewModel + render-harness + oracle tests green; new tests for tab
  re-home fidelity, ticker→article expansion, EraTheme swap, gallery CRUD, parity accelerators.
- **Why first:** highest visible value for the least new surface, zero sim risk. Lays the two
  load-bearing foundations (era skin + narrative read-seam) the whole hub reuses; a tangible,
  steerable artifact on day one.

### Increment 2 — The Race Weekend model  *(first sim-touching increment; needs a migration)*

Evolve the loop from one race to the real AMS2 weekend, once the shell is proven.

- **Pack format:** add the round `weekend` block (`practice`/`qualifying`/`races[]`, era-correct
  labels, per-race `pointsTable`); **default → single-race** so all 13 packs load unchanged.
- **Loop + result entry:** session-aware briefing and result capture — **Practice (info) →
  Qualifying (grid + order) → Race 1 → optional Race 2** — **reusing the existing keyboard/drag
  grammar per session** (grammar, undo, DNF/DSQ, parity transfer wholesale).
- **Sim wiring (name the real engine work — this is NOT "no engine change"):**
  - **Qualifying pace anchor = NET-NEW math.** Today `PaceAnchorMath` is single-sided (race-finish +
    `raceSkill`). Add a `QualifyingAnchor` (2nd EWMA, own alpha) + `ImpliedPlayerQualiPace` over the
    grid's `qualifyingSkill` + a `player.qualiAnchor` phase (+ reserve the Phase-2 tycoon hook). *Can
    be a later slice — the weekend structure doesn't depend on it.*
  - **Per-session scoring.** `AlternateRaceTableId` is one per-*round* field, so it cannot pay Race 1
    and Race 2 different tables; a genuine sprint uses the existing `SessionKind.Sprint → SprintPoints`
    path, and a 2nd full race on its own table needs a **per-session points-table on `SessionResult`**.
  - **Per-session fold.** Independent per-race OPI/rep/XP needs `ImportAndFoldRound` restructured into
    a **per-session fold** (`ImportAndFoldSession`, `RoundUpdate` per session).
- **Persistence + seam (a real save-format change):** qualifying order is raw INPUT the sim can't
  derive → add `SessionKind.Qualifying` / an envelope `QualifyingOrder` field + **bump
  `RoundResultEnvelope` v2→v3** (read-with-defaults); `Apply`/`Preview` gain an **optional `SessionId`**
  (default = sole race, so legacy packs replay byte-identically); add `CurrentWeekend()`.
- **Determinism:** extend the replay CI test to a **qualy + 2-races** career (byte-identical); 13
  packs still reproduce byte-identical.
- **Why second:** it's the loop change Mike explicitly asked for; doing it right after the verbatim
  re-home keeps the re-home clean and evolves from a proven shell.

### Increment 3 — Total-recall History + clickable-everywhere Why? inspector  *(read-only over state)*

Prove the "legible sim + total recall" thesis with tabs that need no new sim math.

- **`CareerTimeline()`** (default-implemented read projection) + progressive-unlock via
  `TabAvailability()`.
- **History / Scrapbook tab:** per-season review cards, lineage timeline, **records book**
  (bests/streaks/milestones), **every race's archived news article** (re-derived from the journal —
  free), era-correct paper.
- **The clickable-everywhere Why? inspector** lives here: click any number → walk back the journal
  rows; the **contribution-breakdown format is built to accept perk/stat rows** (wires live when the
  character layer ships).
- **Why third:** all data already exists — pure OUTPUT rendering, no sim additions, no migration — so
  it ships immediately and delivers the "relive any race years later" payoff.
- **── GATE:** Mike playtests the hub end-to-end (shell + weekend + history) → **tag v0.4.0**.

### Increment 4 — Character system + Setup Gamble + Contracts + Normal/Hardcore  *(≈ v0.5.0; the RPG milestone)*

**Hard-split into three shippable, independently determinism-gated slices** (not "may split" — it
bundles 4–6 units, each with its own replay gate, and Setup Gamble is meant to prove the minigame
pattern *in isolation*):

- **4a — Creation + progression:** the `WizardStep.Character` (3-tier), stats/levels/XP (`XpMath`,
  `player.xp`/`level`/`statSpend` phases), `PlayerPerkModifiers` threaded into the five pure functions
  + `OfferScore` + grid-resolve, injury opt-in/auto-enable, the **13-archetype balance audit**. Own
  determinism gate.
- **4b — First minigame + documents:** **Setup Gamble** (`setup.choice` INPUT table + `setup` stream +
  the scalar-delta channel; choice provenance-excluded, re-applied on replay) + **Contracts-as-era-
  documents**. *Setup Gamble's scalar-delta channel is independent of perks, so it can even ship as its
  own tiny increment ahead of 4a if you want the minigame primitive proven first.*
- **4c — Modes + life-sim:** Normal/Hardcore data-driven knobs + the life-sim event deck (on the
  existing `events` stream) + `form-swing`.

Each is a version bump. Follows the full `character-system.md` §9 checklist.

- **Wizard:** one `WizardStep.Character` (archetype → free customize → advanced); write the
  `player.character` INPUT row at Confirm. **Driver dossier tab** surfaces stats/perks/level/XP/CP +
  between-season spends.
- **Data:** `perks.json` grown to **13 audited archetypes** (author + audit the 6 new ones);
  `character-stats.json` mapping; the **CI balance audit** (`|Σ cpEquivalent − cost| ≤ 0.5`, per-lever
  caps, no dominant/trap, in-budget presets).
- **Sim:** `XpMath` (pure); the identity-default `PlayerPerkModifiers` param threaded into
  `OpiMath`/`ReputationMath`/`PaceAnchorMath`/`SeatStrengthModel`/`AgeOneSeason` + the `OfferScore`
  call site; grid-resolve patches player-seat ratings/scalars from perks.
- **Streams:** newly reserve `Injury`, `FormSwing`, `CharacterGen` — all gated/opt-in; **injury
  auto-enables** when a character carries an injury perk. The life-sim event deck rides the **existing**
  `CareerStreams.Events` (`"events"`) — **not** a new stream.
- **Modes:** Normal (protected floor) vs **Hardcore** (honest car-nudge, stricter difficulty, least
  forgiving respec, injury on, harsher aging, full career stakes) — data-driven knobs.
- **Setup Gamble** minigame: `setup.choice` phase + `setup` stream + the optional per-round
  scalar-delta channel merged into grid-gen (defaults to zero = skip). **Contracts-as-documents** (era
  `DataTemplate`; Accept = the skip path).
- **Determinism:** replay CI covers a character/mode/minigame career **and** its skip-everything
  equivalent before ship.
- **The car-scalar safety contract** (timestamped backup, diff-aware, one-click restore, bounded
  deltas) is re-verified — the honest-nudge writes only through the existing staged file.

### Phase 2+ (data model reserved from day one)

Team lens **+ ledger economy** (a third deterministic fold-stage; qualifying→sponsor/prize/morale
**tycoon signals** land here); Contract **negotiation** minigame; **auto-capture** (`$pcars2$`
shared-memory: retirements/DNF states/positions pre-fill the confirm screen); **Super-Monaco rivalry
→ promotion/relegation** board; Paddock mid-season market; Sponsor Pitch / Development Allocation;
**Owner-Driver** write controls.

---

## Determinism CI matrix (what the replay test must cover before each increment ships)

| Increment | New replay coverage |
|---|---|
| 1 | **Projection stability:** `ReadFeed` re-renders byte-identically across two independent folds of the same journal (guards against dictionary/culture-sensitive ordering). No sim change, but assert it. |
| 2 | a **qualy + 2-races** career (incl. a **doubleheader/sprint** pack) reproduces byte-identical, asserting **row-count equality** not just values; the qualifying anchor + per-session fold covered; all 13 single-race packs replay byte-identically through the session-aware `Apply` path (default `SessionId`) |
| 3 | **Projection stability:** records/timeline/archived-articles re-derive byte-identically across two folds — the "relive it forever" promise made a test, not a hope |
| 4 | a **character + Hardcore + Setup-Gamble + life-sim-event** career **and** its skip-everything equivalent, both byte-identical (choice rows provenance-excluded + re-applied; new derived phases emit **zero** rows on the default path); all 13 packs unchanged after the new streams are reserved |

---

## Increment 1 — ready-to-execute checklist (on Mike's "go")

*No app code is written until Mike approves this plan. When he does, Increment 1 executes in this
order:*

1. `HubViewModel` + `HubTabViewModel` + tab rail shell; move Race/Standings content verbatim; wire the
   persistent header + primary loop button; auto-select Race on open/Apply.
2. `ReadFeed` + `TabAvailability` seam members (default-implemented); the news grammar scaffolding over
   the 1960s corpus; ticker → click-to-expand article; contained Why? chip.
3. `EraTheme` resource-dictionary + `era-themes.json`; immersive-docs/legible-tools split; immersion
   settings surface.
4. Career gallery on Start (cards, CRUD, per-era image slot + placeholder).
5. Tear-off News/Scrapbook companion window.
6. Tests: re-home fidelity, ticker/article, EraTheme swap, gallery CRUD, parity accelerators; full
   suite green; version bump + publish.

---

*Source of truth for the decisions: `docs/dev/career-hub-design.md`. This plan sequences them.*
