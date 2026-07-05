# Career hub design — LOCKED spec (design round + 23-question elicitation, 2026-07-05)

**Status:** decisions locked with Mike via a 23-question elicitation on 2026-07-05. This
supersedes the "Open Questions" version (2026-07-03) — every taste/scope fork below is now
decided. Prose only; no code committed. The companion build plan is `docs/dev/career-hub-build.md`.
Grounded in PLAN.md (8 locked decisions), `career-sim.md` (the shipped deterministic sim),
`app-shell.md` (the `ICareerSession` seam), `character-system.md` + `data/rules/perks.json` (the
driver-character layer), and `career-hub-vision.md` (Mike's "fully immersive" direction).

> **Design-together artifact, now resolved.** Everything here reflects Mike's answers. Where a
> decision opens new build scope (the race weekend model, the procedural news engine, the
> Normal/Hardcore modes, the dynamic life-sim layer), that scope is called out and sequenced in
> the build plan.

---

## 0. The vision in one paragraph

**A Super Monaco GP–style climb-the-grid career, told with real names, real teams, and real
historical eras.** You start in a backmarker seat; results and rivalries promote you up the budget
tiers toward the front of the grid (realistically paced — no arcade jumps). Underneath sits a deep
**character-RPG + dynamic life-sim**: a driver with stats, perks, levels, morale/form and life
events that — safely and reversibly — shape your *real AMS2 car* and the sim's expectations of you.
The whole thing is presented in **era-authentic skin** (telegram → fax → email), narrated by a
**procedural news engine** capable of thousands of generated dispatches, and preserved forever in a
**total-recall career scrapbook**. Two modes: a protected **Normal** and a full-stakes **Hardcore**.
Every hard constraint from PLAN.md holds unchanged: **the sim never decides an on-track finishing
position; everything is deterministic, journaled and byte-replayable; everything is data-driven;
every action has mouse + keyboard parity (decision 8); every increment is additive to the shipped
loop and the `ICareerSession` seam.**

---

## 1. The 23 locked decisions

| # | Axis | **Decision** | Notes |
|---|---|---|---|
| 1 | Hub navigation shape | **Persistent left tab rail + pinned header + collapsible right News dock; News/Scrapbook can tear off into always-on-top companion windows** (read-only mirrors, zero state cost) | Honors the literal "own windows" vision; great on a 2nd monitor while racing in AMS2 |
| 2 | Tab reveal | **Progressive unlock** — core tabs from day one; each other tab appears when its data first exists. No empty/greyed tabs ever | Drives a `TabAvailability()` projection |
| 3 | Race-day loop home | **Dedicated Race tab (flow verbatim) + always-visible header button** ("Next: <round> — <session>" / "Enter Result"); Race auto-selects on open and after every Apply | The anti-burial mechanism |
| 3b | **Race weekend model** (Mike-raised) | **Data-driven weekend: Practice → Qualifying → Race 1 → optional Race 2**, 1 or 2 races per round, era-correct session names | See §3 — new scope |
| 4 | "Why?" inspector centrality | **First-class, phased in** — Why? chip in Increment 1, clickable-numbers-everywhere walk-back inspector in the History increment | Pure UI over byte-identical replay data |
| 5 | Why? explains perks too | **Yes — design the contribution-breakdown format now**, wire live when the character layer ships | e.g. "expected P8 = tier-4 car −3, Pace +1, Rain Man +2 (wet)" |
| 6 | Era depth for v1 | **One `EraTheme` swap now** (telegram/fax/email) in Increment 1 | Presentation primitive Contracts/Scrapbook reuse |
| 7 | Whole-UI re-skin vs partial | **Immersive docs, legible tools** — era typography/accent/chrome everywhere, but dense data surfaces (standings, result entry) keep a legible base face with era accent only; **plus user immersion settings** | Protects the <90s entry + glanceability |
| 8 | Character-creation CP budget | **10 CP** (the audited default; +6 refund headroom → net spend [0,16]) | JSON knob; 12 would need a re-audit |
| 9 | Injury system | **Opt-in: global default OFF, auto-enabled for any character taking an injury perk, ON in Hardcore** | The audit depends on it being live for the 7 injury perks |
| 10 | Talent-stat honesty | **Pure-expectation by default + a Hardcore honest-nudge** that also applies a tiny bounded car scalar so talent bites your real car | The car-scalar mechanism is real, bounded and reversible — see §8.4 |
| 11 | Archetype presets | **13 presets** (expand the audited 7 with 6 more), all balance-audited ("nothing overpowered"), editable + data-extensible | 6 new templates must pass the same audit |
| 12 | Respec strictness | **Mode-scaled with a real penalty:** Normal = milestone token + hefty CP penalty, equal-or-lower-cost swaps only; Hardcore = least forgiving (perks locked / stat-only or extreme cost) | Keeps the creation audit valid career-long |
| 13 | Character dossier | **Dedicated Driver/Character hub tab, full 3-tier depth** (archetype → free customize → advanced); the between-seasons CP-spend home | Distinct from the read-only Career/History lens |
| 14 | First minigame | **Setup Gamble** (pre-race Safe/Balanced/Aggressive → your own car scalars for that round) | Proves the load-bearing INPUT-shaping path; slots before qualifying |
| 15 | Minigame agency / randomness / skip | **Seeded randomness (a "D100 modified by your skills"), always replayable; minor input-minigames skippable (skip = default), story events mandatory; no roll ever sets a finishing position** | See §9 — the reconciliation with determinism |
| 16 | Contracts depth v1 | **Offers as era-correct documents + one-click Accept** (the skip path); negotiation minigame is Phase 2 | Same `PlayerOffer` data, era `DataTemplate` |
| 17 | News prominence + tone | **Ticker by default; clicking a ticker headline expands into a full immersive period article**; immersion is user-configurable | See §7 |
| 18 | Scrapbook / history depth | **Total recall in v1**: season cards + lineage timeline + Why? home **+ records book + every race's archived news article, viewable forever** | Why meticulous manual entry matters |
| 19 | Team / finances surfacing | **Deferred to Phase 2** (the Team lens *and* the ledger economy) | v1 keeps only the minimal tier/car context already in the briefing/HQ |
| 20 | Multi-career / saves | **Picture-rich career gallery** on Start — one DB per career, effectively unlimited saves, per-era historical imagery | Beats AMS2's 4-save cap; user supplies era images |
| 21 | Art direction | **Period-authentic minimalism** — each era's real medium rendered clean/vector, muted period palette, one accent per era | Keeps the single exe lean |
| 22 | Game mode (Mike-raised) | **Normal (protected) + Hardcore (full stakes)** as a first-class, data-driven pillar | See §8.5 table |
| 23 | Career fantasy framing (Mike-raised) | **Super Monaco GP ladder** — rivalry- and results-driven promotion/relegation up the tiers, real names/teams, realistically paced | The organizing metaphor for offers + tiers + reputation + (Phase-3) rivalries |

**Everything below elaborates these decisions.** Nothing new is introduced that isn't traceable to
a row above.

---

## 2. Navigation shape (locked)

A **single WPF window: persistent left tab rail + central content region + a collapsible
era-styled News dock on the right** — a shell *around* the shipped Home, not an MDI or a dashboard
that replaces the loop.

- **`HubViewModel`** owns the `ICareerSession` and an `ObservableCollection<HubTabViewModel>` with a
  `SelectedTab`. Today's `Home/Briefing/ResultEntry/Confirm/Standings/SeasonReview` content moves
  into the **Race** and **Standings** tabs **unchanged** (Increment 1). `ShellViewModel`'s
  Start→Wizard→Home→Settings conducting is preserved; Home becomes the Hub.
- **Persistent header** (glanceable from every tab): season year, round, **current weekend session**,
  player standing, rep/OPI trend — lifted from fields already on `HomeViewModel`.
- **Always-visible primary action button** in the header: **"Next: <round> — <session>" / "Enter
  Result"** — one click back to the loop from anywhere. On career open and after every Apply, the
  Race tab is auto-selected. *This is the anti-burial mechanism the constraints demand.*
- **Progressive tab unlock (decision 2):** the core set (**HQ, Race, Standings, News**) is present
  from day one; **Driver, Contracts, History** appear the moment their data first exists, driven by a
  `TabAvailability()` projection. No empty/greyed tabs, ever.
- **Right-side News dock (decision 17):** a **one-line era-skinned ticker by default**; clicking a
  headline expands it into a full immersive period **article** (telegram/fax/email). Tear-off to an
  always-on-top companion window is the "own windows" answer.
- **Tear-off windows (decision 1):** News and Scrapbook can pop out into borderless always-on-top
  companion windows, reusing the briefing checklist's existing always-on-top mechanism. Read-only
  mirrors → zero parity/state cost.
- **Full parity (decision 8):** number keys **1–9** jump to tab N; arrows navigate the rail;
  `Ctrl+Home` snaps to the current session card; `Esc` backs out of any minigame to the Race tab
  writing no state. Every rail entry is a mouse target with a tooltip. Accelerators + Why?-link focus
  order are baked into `HubViewModel` from the first tab.
- **Immersion settings (decision 7):** a settings surface controls immersion intensity — era-theme
  on/off/per-surface, news verbosity, ticker density, animation gates — replacing the old binary
  minimal-narrative toggle with a spectrum. The extreme "minimal" preset still collapses the hub to
  **HQ + Race + Standings** with plain chrome.

---

## 3. The race weekend model (NEW — decision 3b, 22-sim)

Today a "round" records **one race**. Mike's requirement: model AMS2's real weekend — **one
practice, one qualifying, up to two races** — and *consider* qualifying/practice in the sim.

### 3.1 Structure (data-driven, per round)

The pack's `season.json` round gains a **`weekend` block** declaring the sessions:

```jsonc
"weekend": {
  "practice":   { "present": true, "label": "Practice" },
  "qualifying": { "present": true, "label": "Qualifying", "format": "single" },
  "races": [
    { "id": "race", "label": "Grand Prix", "pointsTable": "primary" }
    // optional 2nd entry for sprint/doubleheader eras:
    // { "id": "race2", "label": "Sprint", "pointsTable": "sprint", "gridFrom": "race1Reverse" }
  ]
}
```

- **1 or 2 races**, declared per round. A single-race weekend simply labels its race with the
  **era-correct name** ("Grand Prix", "Feature", etc.) — no "Race 1/2" chrome when there's one race.
- **Session labels are era-correct data** — packs name their own sessions.
- Defaults: absent `weekend` block → today's single-race round (**back-compatible**; all 13 bundled
  packs load unchanged).
- **This is a real engine + save-format change, not a free ride** (verified against source, 2026-07-05):
  - **Per-session points table is new.** Today `AlternateRaceTableId` is a single *per-round* field
    (the 2022+ shortened-race sliding scale) applied to every session, so it cannot give Race 1 the
    primary table and Race 2 a different table. A genuine **sprint** 2nd race routes through the
    existing `SessionKind.Sprint → SprintPoints` path; a 2nd *full* race on its own table needs a
    per-session points-table binding on `SessionResult` — an engine change, scheduled in Increment 2.
  - **Independent per-race scoring/OPI/rep/XP is new.** The shipped fold unit is one round = one race
    (`ImportAndFoldRound`, one `RoundUpdate` per round; `ScoreRound` accumulates all sessions into one
    `RoundScore`). Two independently-scoring races need the fold restructured into a **per-session
    fold** (`ImportAndFoldSession`). Also an Increment-2 engine change.
  - **The qualifying order is raw INPUT** the sim can't derive, so it is a **raw-payload addition**
    (a new `SessionKind.Qualifying` or an envelope `QualifyingOrder` field) with a
    `RoundResultEnvelope` version bump (v2→v3, read-with-defaults — the established mechanism). See §12.

### 3.2 What each session feeds the sim (decision: weekend-sim option 1 + tycoon hook)

| Session | Sets | Feeds |
|---|---|---|
| **Practice** | Informational only | Setup/reliability flavor in the briefing; no standings effect. (Later: a Setup-Gamble preview surface.) |
| **Qualifying** | **The race grid** | **The OneLap / `qualifyingSkill` side of the pace anchor** (your one-lap pace vs the field — the richest new signal), a rep/OPI qualifying signal, **and Phase-2 tycoon inputs** (qualifying performance → sponsor health, appearance/prize money, grid bonuses, team satisfaction) |
| **Race 1 / Race 2** | Championship points (per its `pointsTable`) | Each race independently computes OPI, reputation, XP and standings — *which requires the per-session fold (`ImportAndFoldSession`) + per-session points table above; not free* |

- **Pace anchor — qualifying is NET-NEW math** (not a recalibration): today `PaceAnchorMath` is
  single-sided (race-finish + `raceSkill` only). Increment 2 ADDS a qualifying calibration branch — a
  second EWMA + an `ImpliedPlayerQualiPace` over the grid's `qualifyingSkill` + a `player.qualiAnchor`
  journal phase. The character `oneLap` stat maps onto it once the character layer ships; it is not a
  pre-existing hook. *The weekend STRUCTURE (sessions, grid-from-qualy, 2nd-race scoring) can ship
  without this, so the qualifying-as-pace-signal can be a later slice if Increment 2 gets heavy.*
- **Result entry** captures the qualifying order + up to two race results, **reusing the existing
  keyboard/drag grammar per session** (one session at a time; the grammar, undo, DNF/DSQ, parity all
  transfer wholesale).
- **All pack-overridable** — a pack can declare a scored qualy, a reverse-grid sprint, or a plain
  single race.

### 3.3 Sequencing note

The weekend model **changes the loop**, so it is *not* bundled into the verbatim re-home. Increment 1
re-homes today's single-race loop unchanged (existing tests = the gate); the weekend model lands as
**Increment 2** (see the build plan), evolving the loop once the shell is proven.

---

## 4. Tab set (locked, revised)

| Tab | v1 (Increments 1–3) | Later | Unlock trigger |
|---|---|---|---|
| **HQ / Home** | Season header, current weekend/session card, rep/OPI sparkline, top-3 news ticker, slider recommendation chip | — | Always present |
| **Race** | The briefing → **weekend sessions** → result → confirm flow (verbatim in Inc 1, weekend-aware in Inc 2) | — | Always present |
| **Standings** | Drivers/constructors/round-matrix VM, re-homed; rules-explainer chip; cells link into Why? | — | Always present |
| **News** | Era-skinned **procedural** dispatch ticker → click-to-expand full article; per-item Why? chip; filter by kind | Rivalry/market/retirement filters as phases ship | Always present |
| **Driver (dossier)** | **Dedicated character tab** — stats, perks, level/XP, CP-to-spend, respec; 3-tier creation flow; between-season growth | Dynamic life-sim state surfaced (morale/form/rivalries) | First character exists |
| **History / Scrapbook** | **Total recall** — per-season cards, lineage timeline, **records book**, **every race's archived news article**, the clickable-everywhere Why? inspector home; era-correct paper | Title-permutation math (Phase 3) | First season complete |
| **Contracts** | Offer letters as era-correct documents; one-click Accept (skip path) | Negotiation minigame (Phase 2) | First offer exists |
| **Team** | *(deferred)* | Read-only tier lens **+ ledger economy** (Phase 2) | Phase 2 |
| **Paddock / Rivals** | *(deferred)* | Driver market, rumor mill, **Super-Monaco rivalry→promotion** board | Phase 2–3 |
| **Finances** | *(deferred)* | Driver-side then full team ledger, crisis ladder | Phase 2–3 |

**Design rule enforced everywhere:** a first-timer using only the mouse finishes a full season
without opening a second tab. Every management tab and minigame only shapes **INPUTS** (car scalars,
difficulty anchor, morale, contract terms) or renders **OUTPUTS** (journal/OPI/rep/offers). **No tab
and no minigame ever touches an on-track finishing position.**

---

## 5. The "Why?" inspector (decisions 4 + 5)

**First-class, phased in.** Every number the hub shows can walk back through the exact append-only
journal rows that produced it — the same rows `Resimulate` byte-checks.

- **Increment 1:** a per-item **Why? chip** on News (and, as they arrive, other surfaces) — expands
  one headline's journal row into a plain sentence (`deltaJson` → prose).
- **History increment:** the **clickable-everywhere** walk-back inspector — click OPI, tier, salary,
  a qualifying delta, a rival gap — a shared inspector panel opens the causal chain. This is the
  hardcore obsession loop, and it's pure UI over data that already exists and already replays
  byte-identical (seam member `JournalFor(entity, round?)`).
- **Explains character/perks too (decision 5):** the inspector's **contribution-breakdown format is
  designed now** to accept perk/stat rows, e.g.
  `expected finish P8 = tier-4 car −3 · Pace +1 · Rain Man +2 (wet round)`.
  `PlayerPerkModifiers` is a **planned** identity-defaulting struct (character-system.md §6.1) — *not
  yet in code* — that threads into `OpiMath`/`ReputationMath`/`PaceAnchorMath`/`SeatStrengthModel`/
  `AgeOneSeason` when the character layer ships. Once built it is a pure function of the journaled
  creation row, so the breakdown is the same read-only-over-folded-state pattern with no new
  persistence; the inspector's format ships ready ahead of it.

---

## 6. Era immersion + art (decisions 6, 7, 21)

One **`EraTheme`** enum (Telegram / Fax / Email) selected from the active pack's decade, wired as a
**single app-wide `ResourceDictionary` swap** driving typography, news-item chrome, accent color, and
paper/transition texture. Ships in **Increment 1**.

- **Immersive docs, legible tools (decision 7):** the swap fully skins **documents** (news articles,
  offer letters, scrapbook spreads) and the **chrome + accent** everywhere; **dense functional
  surfaces (standings tables, result entry) keep a legible base face with era accent only** — so 1967
  feels unlike 1988 without slowing the <90s entry or hurting glanceability. **User immersion
  settings** let the player push the skin further or pull it back per-surface.
- **1960s = TELEGRAM** (uppercase mono, "STOP"-punctuated, ochre paper — the existing 1960s headline
  bank already matches). **1980s = FAX** (sender/date band, thermal grain). **1990s+ = EMAIL** (inbox
  rows, subject/sender, mono→sans shift).
- **Art direction (decision 21):** **period-authentic minimalism** — each era's real medium rendered
  clean and vector, muted period palette, one signature accent per era. Styles + tokens, **not**
  bitmap-heavy themes (keeps the single exe lean).
- **Data-driven:** `data/rules/era-themes.json` maps decade → `{medium, accent, fontStack,
  paperTexture, datelineFormat}` so community packs declare their own era feel.
- **Determinism preserved:** every era-flavored string is still selected via a named PCG32 stream;
  presentation introduces no un-seeded randomness.

---

## 7. The procedural news engine (NEW — decision 17)

Mike's ask: **"thousands or more procedurally generated news articles… make more and add more easily
later."** This replaces the current one-headline-per-race `HeadlineBank` with a **data-driven
generative article system**.

- **Grammar, not a fixed bank.** An article = a **template body with typed slots** filled from race
  facts, selected and varied via the seeded **`headlines`** stream (the shipped one — `media` is a
  reserved *future* stream for the Media-Moment minigame, not consumed here). Many interchangeable
  fragments per slot → combinatorially thousands of distinct articles from a compact corpus.
- **Where the facts come from — mind the thin `race.result` row.** Today's `race.result` journal row
  carries only `{round, expectedFinish, actualFinish, dnf}` for the player, and the shipped
  `HeadlineBank` emits **one** `news.headline` row per race. So Increment 1 ships **only** (a)
  re-rendering the existing `news.headline` rows through `EraTheme` into the ticker/article, and (b)
  the `deltaJson`→sentence Why? chip. The **rich generative grammar** (winner/podium/gaps/rival/team
  facts) is a **later slice**; those facts are a read-projection over `AllSnapshots()` (free) — or, if
  a fact isn't in the snapshots, a new journaled field (a migration). "Thousands of dispatches" is a
  later-increment promise, not a zero-migration Increment-1 claim.
- **Ticker → full article (decision 17):** the dock shows a one-line era-styled **ticker**; clicking a
  headline expands the **full immersive period article** (still minimal text — no bloat).
- **Era-voiced + community-extensible:** corpora live in JSON (`data/rules/news/*.json`), keyed by
  journal phase + cause + era, so new eras/tones/events are pure content adds — **"add more content
  easily later"** by construction. Ships thin (1960s corpus first, per the existing bank) and grows
  per era pack.
- **Deterministic:** selection is seeded; the *choice of fragments* is a pure function of
  `(masterSeed, headlines-stream, journal row)`, so the same career renders the same articles on
  replay. Article text is **derived** (rebuilt by `Resimulate`), never a stored INPUT.
- **Archived forever (decision 18):** every generated article is re-derivable from the journal, so the
  Scrapbook can show any past race's article years later at zero storage cost.

The seam member: `ReadFeed(seasonId)` → `Dispatch { resolvedArticle, era, kind, sourceJournalSeqs,
whyText }`, built by resolving existing journal rows through the news grammar.

---

## 8. Character system integration

The full model lives in `character-system.md` + `data/rules/perks.json`; the hub surfaces it. Locked
answers:

### 8.1 The Driver dossier tab (decision 13)
A dedicated tab (not folded into History): **stats (7), perks, level + XP bar, CP-to-spend, respec**,
and the **between-seasons CP-spend home**. Creation keeps the **3-tier flow**: one-click **archetype**
→ **free customize** (drag 7 sliders, swap perks, live remaining-CP meter) → **advanced** (raw
stat→rating numbers + exact lever deltas). Fits the shipped wizard as one `WizardStep.Character`
between SeatPick and RulesPreset.

### 8.2 Budget, archetypes, respec (decisions 8, 11, 12)
- **CP budget 10** (audited default; +6 refund headroom → net spend [0,16]).
- **13 archetypes** — the audited 7 (Prodigy, Late Bloomer, All-Rounder, Pay Driver, Rain Master,
  Journeyman, Hard Charger) **+ 6 new** balance-audited, non-overpowered templates. All editable
  starting cards, all pure data → community can add more with zero code. *The 6 new presets must pass
  the same CI audit (`|Σ cpEquivalent − cost| ≤ 0.5`, in-budget net spend, no dominant/trap) before
  they ship.*
- **Respec — mode-scaled with a real penalty:** **Normal** = milestone respec token (every 5 levels) +
  a **hefty CP penalty**, equal-or-lower-cost swaps only (blocks laundering into a stronger build).
  **Hardcore** = least forgiving (perks locked at creation; stat-only respec or extreme cost). All
  strictness is JSON-authored.

### 8.3 Injury (decision 9)
**Opt-in:** global default OFF (default careers add zero new draws, stay replay-compatible with
pre-character saves); **auto-enabled** for any character taking an injury perk (the balance audit
depends on it being live for those 7 perks); **ON in Hardcore**. A season-end hazard on the registered
`injury` stream → a **mechanical-classed, OPI-neutral missed round**: it removes your *availability*,
never sets a finishing order.

### 8.4 The honest car-scalar mechanism + safety (decision 10)
**Yes, the app can and does affect your real AMS2 car — safely and reversibly.** This is the shipped
"killer feature" (decision 3), not new risk:

- Since **AMS2 v1.6.9.8**, Custom AI XML accepts `weight_scalar`/`power_scalar`/`drag_scalar`
  (~0.900–1.100) and **these apply to the player's own car** (PLAN.md). The app already writes this
  file every round; character/perk `carScalar` levers merge a small **bounded** delta into the
  existing grid-gen chain (pack → track form → round overrides → career form → **+character/minigame
  delta**) before normalization.
- **Reversible by construction (decision 7 / staging contract):** it's a UserData data file, never
  game physics; **timestamped backup before every write**; **diff-aware** (writes nothing when
  unneeded); **one-click season-end restore**; deleting the file reverts to stock instantly. The
  audit caps `carScalar` at ±0.015 (±0.040 weather-conditional) → a ~1.5–4% nudge, never a wrecked
  car.
- **Honesty policy:** talent stats are **pure-expectation by default** (they set `expectedFinish`, the
  difficulty recommendation, and the fiction — the sim's built-in self-balancer: more talent → a
  harder OPI/rep/XP bar). The **Hardcore honest-nudge** additionally routes talent through a tiny
  bounded car scalar so it bites the real car. Perks that carry `carScalar` levers apply in both modes
  (they're explicit, audited choices).

### 8.5 Normal vs Hardcore (decision 22) + the dynamic life-sim (decision 23/15)

Game mode is a **first-class, data-driven pillar**:

| Lever | **Normal** (default) | **Hardcore** (full stakes) |
|---|---|---|
| Talent → your car | Pure-expectation; only chosen perks nudge the car | **Honest nudge** — talent also feeds a bounded car scalar |
| Difficulty recommendation | Suggested | Stricter / enforced |
| Respec | Milestone token + hefty CP penalty | Least forgiving (perks locked; stat-only/extreme) |
| Injury | Off unless a perk enables it | On |
| Aging | Standard era curve | Harsher |
| **Career stakes** | **Floored — you can't be fired into a demotion spiral** | **Full — firing, relegation, career death** |

**The dynamic life-sim layer.** The character is *not* static: **morale, form, rivalries and life
events** evolve over the career (seeded events, exactly like the existing `events` stream) and
dynamically modulate **inputs** — your car scalars (bounded, reversible) and the sim's expectations —
never an on-track result. Hardcore surfaces the most of these effects; Normal keeps a protective
floor. This is the "you're the racing driver in a life sim" ambition, and it plugs into the
**Super Monaco GP ladder** (decision 23): rivalry + results drive **promotion/relegation** up the
budget tiers toward the front of the grid, realistically paced. Offers, tiers and reputation — all
already in the sim — are the ladder; rivalries and the living paddock (Phase 2–3) make the climb feel
alive.

---

## 9. Minigames + events (decisions 14, 15)

**First minigame: Setup Gamble** (pre-race Safe/Balanced/Aggressive → your own car scalars for that
round only, merged into the existing grid-gen scalar path). Balanced = today's exact scalars (a true
no-op skip). Honest tradeoff shown up front. Slots before qualifying.

**The agency/randomness/skip model (decision 15) — reconciled with determinism:**

- **Seeded randomness is embraced.** Outcomes can be a **"D100 modified by your skills/character"** —
  but the roll is drawn from a **named PCG32 stream** keyed `(subsystem, year, round, entity)` like
  every existing stream. So outcomes **vary between careers/seeds yet replay byte-identical** for a
  given seed. Randomness ≠ non-determinism.
- **The one hard line:** **no roll ever sets an on-track finishing position.** Rolls decide life /
  narrative / input outcomes (morale, offers, injuries, rivalries, setup deltas). The race result is
  always the one you entered.
- **Skippability:** **minor input-minigames are skippable** (skip = the default result the career
  would have had, itself journaled as the default choice); **story events are mandatory** narrative
  beats. Mandatory ≠ random-uncontrolled — story events are still deterministic and journaled.
- **Journaling — follow the shipped `AcceptOffer` precedent** (verified against `ReplayService` /
  `StateStore` / `JournalStore`, 2026-07-05). Replay regenerates the event sequence in memory and
  byte-compares it **positionally** against the stored journal, excluding only *provenance* phases
  (`import.*`, `career`, `result`). So a player CHOICE — which replay cannot re-derive — must be
  **stored in a dedicated INPUT table** (a `minigame_choice` table, pulled forward to the character
  increment, exactly like `SetOfferAccepted`), **re-applied during `Resimulate`**, and its journal row
  written under a **provenance phase** (or `IsProvenance` extended to cover the choice phases) so it is
  **excluded** from the positional compare. The **resolved outcome** is then a normal **derived** row,
  regenerated in fixed order from (seed + re-applied choice + folded state) and included in the
  compare. *(Don't describe a choice as merely "surviving `WipeDerived`" — that's true of every
  journal row and isn't the safety property; `WipeDerived` clears derived state tables, never journal
  rows.)* The replay CI must cover a minigame/event career **and** its skip-everything equivalent
  before any of this ships.

Priority order after Setup Gamble is unchanged (Media Moment → Contract Negotiation → Sponsor Pitch →
Development Allocation), now framed as the growing life-sim event deck.

---

## 10. Contracts (decision 16)

**v1:** offer letters render as **era-correct documents** (typed 1967 telegram / 1993 fax / email) —
same `PlayerOffer` data, different `DataTemplate` per era. **Accept = today's one-click
`AcceptOffer(teamId)`** (the skip path). The **negotiation minigame** (counter-offer volley vs
archetype-weighted patience, visible patience meter) is **Phase 2**. On the Super-Monaco ladder, the
offer you accept is your rung on the climb.

---

## 11. Career gallery + saves (decision 20)

**One SQLite file per career** is already the architecture → effectively unlimited saves. The Start
screen becomes a **picture-rich career gallery**: recent careers as cards showing **era, driver,
current season/standing**, with rename / duplicate / delete. Explicitly beats AMS2's 4-save cap (a
genuine selling point vs the built-in mode).

- **Per-era imagery (Mike supplies):** a data-driven asset slot maps **era key → image path**, with a
  recommended card size (proposed **≈ 640×360, 16:9**, PNG/JPG, with a 2× variant for crisp
  rendering); packs/users drop historical era photos into `data/ams2/era-art/` (or a pack's own
  folder). Absent an image → a clean generated era-accent placeholder. Final dimensions to confirm
  once Mike picks the card layout.

---

## 12. Additive data-model deltas (carry-over + new)

Everything additive; the discipline from `career-sim.md` / `ICareerSession.cs` / `JournalStore.cs`
holds: player **DECISIONS** are inputs that survive `WipeDerived` and replay byte-identically; sim
**RESULTS** are derived rows wiped + rebuilt by `Resimulate`.

**Seam (`ICareerSession`) — new default-implemented, read-only members** (all projections over data
`StateStore`/`JournalStore` already hold; no new persistence for the read tabs):
- `ReadFeed(seasonId)` → procedural `Dispatch` articles (§7).
- `JournalFor(entity, round?)` → the Why? causal chain (§5).
- `CareerTimeline()` → lineage-aware per-season cards for History.
- `TabAvailability()` → which tabs have payload yet (progressive unlock).
- *(Phase 2)* `TeamView()` → the deferred Team lens.
- **Weekend model (touches EXISTING members — confront it, don't hide it).** The shipped seam is
  single-race-shaped (`CurrentGrid()` → one grid; `Preview`/`Apply(ResultDraft)` → one draft, no
  session discriminator). A weekend needs qualifying-order entry *then* race entry (the grid is derived
  from the entered qualy order) — multiple drafts per round. Increment 2 gives `Apply`/`Preview` an
  **optional `SessionId`** (back-compatible default = the sole race session, so legacy single-race
  packs drive byte-identically) and adds `CurrentWeekend()`; a **seam-fidelity test** asserts a
  no-`weekend`-block pack replays byte-identically through the session-aware path. *This is a contract
  change to existing members — the honest, single exception to "default-add only," scoped to Increment
  2 and gated by the replay test.*

**Journal — new phases only** (save-format-stable; all derived except creation/choice INPUTs):
- `setup.choice`, `media.moment` (Increment 3); `qualifying.result`, `race.result` per session (Inc 2
  — the raw entered results are INPUTs); `player.character` (INPUT), `player.xp`, `player.level`,
  `player.statSpend`, `player.respec`, `player.injury`, `player.formSwing`, `player.event` (life-sim);
  reserved: `contract.negotiation`, `sponsor.pitch`, `dev.alloc`, `rivalry`.
  **Two invariants (see §13.2):** (1) the *choice* phases (`setup.choice`, `media.moment`,
  `player.character`) must be **provenance-excluded** from the positional replay compare and re-applied
  from their INPUT tables; (2) every new *derived* phase (`player.xp` etc. — note `player.xp` is **not**
  emitted today) must emit **zero** rows on the default-career path so the folded row sequence is
  byte-identical for a no-character/no-minigame career.

**RNG streams.** The life-sim event deck rides the **existing** `CareerStreams.Events` (`"events"`)
stream — *not* a new one. **Newly reserved** (same `SplitMix64(Fnv1a64(subsystem|year|round|entity)
XOR masterSeed)` formula, each independently keyed so reserving one perturbs nothing):
`setup`, `media`, `injury`, `form-swing`, `character-gen`, `negotiate`, `sponsor`, `development`,
`rivalry`. All gated/opt-in → a default career consumes zero new draws. **Reserving a stream is safe
(fresh generator per key); the real hazard is emitted-row order — see §13.2.**

**Data files (additive, defaulted):**
- `data/rules/era-themes.json` (era skin tokens), `data/rules/news/*.json` (news grammar corpora),
  `data/ams2/era-art/*` (gallery imagery).
- `season.json` round `weekend` block (§3.1); optional 2nd-race `pointsTable` (engine already
  supports `AlternateRaceTableId`).
- `perks.json` gains 6 audited archetypes (13 total); `character-stats.json` (stat→rating mapping).
- *(Phase 2+)* `teams.json` `sponsorSlots`/`economy`, Career DB `minigame_choice` + ledger migrations.

**Save-format + engine changes the weekend model DOES require** (Increment 2; the rest of the hub
stays additive): a qualifying representation in the raw envelope (`SessionKind.Qualifying` or an
envelope `QualifyingOrder` field) with a `RoundResultEnvelope` version bump (**v2→v3**,
read-with-defaults — the established mechanism); a **per-session** points-table binding on
`SessionResult`; a **per-session fold** (`ImportAndFoldSession`) so each race scores independently; and
the new qualifying pace-anchor branch (§3.2). The **f1db oracle suite is unaffected** — historical F1
is single-race; the sprint/doubleheader paths are new and opt-in. For any pack *without* a `weekend`
block, the single-race payload, points tables, fold and oracle are **unchanged**.

---

## 13. Determinism & the hard constraints (restated, with the seeded-randomness reconciliation)

1. **Sim never decides races.** No tab, minigame, event, or roll sets an on-track finishing position;
   management only shapes generated-XML INPUTS and consumes entered RESULTS. Guarded explicitly once
   money/life-sim exist: no code path reads a result the player didn't enter.
2. **Deterministic + journaled + byte-replayable — even with the new randomness.** Every random
   outcome draws a **named seeded stream**; `state = fold(journal)`; `Resimulate` byte-checks. New
   streams are independently keyed so reserving them perturbs nothing.
   **The load-bearing invariant (beyond value-determinism):** replay is a strict **positional,
   per-season sequence compare** — a row count/order mismatch is itself a divergence. So for a **default
   career** (no character, no minigames, injury off) the fold must emit the **identical journal-row
   sequence** (phase, entity, order, count) it emits today; every new derived phase emits **zero** rows
   on that path, and player CHOICE rows are provenance-excluded + re-applied from INPUT tables (§9).
   The **procedural news engine is the highest-risk change** — it must preserve exactly one
   dispatch-row per existing `news.headline` position. The replay CI must assert **row-count equality**
   (not just byte values) and cover: (a) a weekend career (qualy + 2 races, incl. a doubleheader/sprint
   pack), (b) a character career + its skip-everything equivalent, (c) a life-sim/minigame career + its
   skip equivalent — each byte-identical, and all 13 bundled packs still reproduce byte-identical.
3. **Data-driven** — weekend structure, era themes, news grammar, perks, archetypes, era art, mode
   knobs: all user-editable JSON, validated on load.
4. **Mouse + keyboard parity (decision 8)** — every rail entry, session, minigame choice and dossier
   control has both a click target with tooltip and a keybind; the parity ViewModel tests stay the
   gate.
5. **Additive to the shipped loop** — each increment ships behind the `ICareerSession` seam with
   default-implemented members; the loop and the sacred keyboard-grammar/keystroke-budget tests stay
   untouched.

---

## 14. Cross-cutting risks (updated)

1. **Scope creep vs "lightweight single exe / loop never buried."** Every v1 tab is a pure lens or a
   skippable minigame; the economy/Team/rivalries stay Phase 2+; the immersion settings collapse the
   hub to HQ+Race+Standings on demand.
2. **The weekend model touches the loop.** Mitigation: re-home verbatim first (Inc 1), evolve to the
   weekend in Inc 2 with the existing grammar reused per session and a replay test for qualy+2-races
   before ship.
3. **Seeded randomness eroding determinism.** Enforced by construction (named streams, choice-as-INPUT
   + outcome-as-derived, `WipeDerived` cleanup) and by the expanded replay CI matrix above.
4. **News engine thin until per-era corpora land.** The grammar ships with the 1960s corpus; other
   eras look sparse until their JSON lands — pure content, tracked per era pack.
5. **13-archetype + life-sim balance.** Every new archetype and every event effect must pass the
   arithmetic audit (`cpEquivalent`, per-lever caps, no dominant/trap) — "nothing overpowered" is a CI
   gate, not a promise.
6. **"Sim never decides races" blurring once money + life-sim exist.** Guarded explicitly (risk 1) and
   re-asserted in the Phase-2 ledger design as a third fold-stage.
7. **Multi-era total-recall Scrapbook** depends on M6 carryover across many transitions — 30-year
   careers need a stress test; article re-derivation is free (pure function of the journal).
8. **Full parity across a growing tab set + the weekend sessions.** Bake accelerators/focus order into
   `HubViewModel` and the session grammar from the first tab; existing parity tests are the gate.

---

*Companion: `docs/dev/career-hub-build.md` — the incremental build plan (v0.4.0 = Increment 1).*
