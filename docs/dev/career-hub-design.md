# Career hub design — synthesized spec (design round, 2026-07-03)

**Status:** review artifact. Prose only, no code committed. Grounded in PLAN.md (8 locked
decisions), career-sim.md (the built deterministic sim), app-shell.md (the `ICareerSession`
seam), ROADMAP.md (v0.3.0 — working loop + journal + offers + era transition), and Mike's
career-hub-vision.md. Three independent designs were scored and grafted into one direction.

> **This is a "design together" artifact.** The open questions below are the taste/scope calls
> only Mike can make. Everything under them is a proposal, steerable.

---

## OPEN QUESTIONS — decide these first (each is a crisp either/or)

| # | Question | Option A | Option B | Why it matters |
|---|---|---|---|---|
| **Q1** | **Tab reveal model.** How does the hub grow from a clean first career? | **Progressive unlock** — a fresh 1967 career shows only HQ + Race + Standings; a tab appears the moment its data first exists (first offer → Contracts; first season done → History). No empty/greyed tabs, ever. | **Always-present, lens-empty** — all v1 tabs are visible from day one; ones with no data yet show a friendly empty state ("no offers yet — win some races"). | A is maximally approachable and matches "depth is opt-in"; B is more discoverable (the player sees the roadmap of their career) but risks a wall of empty tabs on turn one. |
| **Q2** | **The "Why?" inspector — how central?** Every number in the hub can be clickable to walk back through the journal rows that produced it. | **First-class spine** — clickable numbers everywhere (OPI, tier, salary, rival delta); a shared inspector panel is the obsession loop and ships in v1. | **Contained feature** — a "Why?" chip lives only on the News feed and History, expanding one headline's journal row into a plain sentence; not every number is a hyperlink. | A is the strongest hardcore-retention hook and it's pure UI over data that already replays byte-identical — but it's a real v1 build cost. B is far cheaper and still legible. This is the single biggest scope fork. |
| **Q3** | **Era immersion depth for v1.** How much period skin ships in the first hub increment? | **One EraTheme swap now** — a single resource-dictionary swap keyed off the pack's decade (telegram 60s / fax 80s / email 90s+) drives typography + news chrome + accent across the whole hub from day one. | **Tokens later** — v1 hub is plain/fast; era skinning is a Phase-2 polish pass once the tab set is proven. | A is what makes "1967 FEEL unlike 1988" land immediately (the headline copy is already era-voiced), and it's the presentation primitive Contracts/Scrapbook reuse. B de-risks v1 velocity. The headline bank only has 1960s copy today (see Risk 5), so A's payoff is partial until later eras get copy. |
| **Q4** | **"Own windows" (Mike's vision literally said windows).** | **Tear-off panels** — News feed and Scrapbook can pop out into a borderless always-on-top companion window, reusing the briefing checklist's existing always-on-top mechanism. Read-only mirrors, so zero parity/state cost. | **Single window only** — everything stays inside the one shell; "own windows" is honored as tabs, not real OS windows. | A satisfies the literal vision cheaply (two read-only panels) and is genuinely nice for a second monitor while racing in AMS2; B keeps the surface smallest. |
| **Q5** | **First minigame to ship (the pattern-setter).** All agree the loop→journal→skippable pattern should be proven by one small minigame first. | **Setup Gamble** — pre-race Safe/Balanced/Aggressive choice that nudges the player's own car scalars for that round; writes straight into the grid-gen scalar path that already exists. | **Media Moment** — post-race quote choice (humble/bold/deflect) writing a bounded reputation delta on the confirm screen. | Both are v1-viable, deterministic, skippable, journaled. Setup Gamble proves the INPUT-shaping path (touches the XML the game races); Media Moment proves the OUTPUT-flavor path (touches only rep). Which pattern do you want validated first? |

---

## 1. Scoring the three designs

Scored against: **fidelity** to Mike's "fully immersive" direction · **respect** for the hard
constraints (sim never races, deterministic+journaled, depth opt-in, full parity, additive
seam) · **buildability** on v0.3.0 · **opt-in depth**.

| Design | Fidelity | Constraints | Buildability | Opt-in depth | Verdict |
|---|---|---|---|---|---|
| **A — Simulation Depth First** (legible causal machine; every number links into a "Why?" journal inspector; economy as a third fold-stage from day one) | High | **Excellent** — treats the ledger as another `FoldRound`/`WipeDerived` stage, guards "money only changes INPUTS" explicitly | Medium — the "Why?" inspector + universal hyperlinking is real work | High (every tab is a lens; minimal toggle collapses to HQ+Race+Standings) | The strongest **depth** thesis and the most constraint-aware. Slightly heavier v1. |
| **B — Approachability + Flow First** (hub is a frame around the shipped loop; progressive tab unlock; giant always-visible "get me back to the race" button) | Medium-High | **Excellent** — moves `HomeViewModel`'s body into tab 0 verbatim, all new seam members default-implemented | **Highest** — lowest-risk re-home, existing tests are the acceptance gate | **Highest** — hard progressive unlock + minimal toggle | The safest, most shippable framing. Best "never bury the loop" discipline. |
| **C — Era Immersion + Narrative First** (journal becomes the player-facing spine; News feed of period dispatches; Scrapbook; one EraTheme swap) | **Highest** — most directly delivers "fully immersive, feels like the decade" | Strong — read-only `INarrativeFeed` beside `ICareerSession`; choices-not-outcomes journaled | High — the News feed is pure read-only render over data that already exists | High (all gated behind the minimal-narrative toggle) | Best answer to the actual **vision word ("immersive")**. The News feed is the highest visible-value / lowest-risk single increment on the table. |

### Single best idea from each

- **From A — the "Why?" inspector as the obsession loop.** Click any number, walk back through
  the exact append-only journal rows that produced it — the same rows `Resimulate` byte-checks.
  It turns the invisible deterministic sim into a legible, browsable causal machine, and it's
  almost pure UI over data that already exists and already replays byte-identical. *(Also from A:
  designing the Phase-2/3 economy as a third fold-stage with identical `FoldRound`/`WipeDerived`
  discipline — the correct way to keep the ledger deterministic.)*
- **From B — the loop is a frame, never a destination.** Re-home `HomeViewModel`'s body into
  tab 0 **verbatim** (code, tests, keyboard grammar, drag-and-drop, Esc behavior all move
  wholesale), keep an always-visible primary "Next Briefing / Enter Result" button on every tab,
  and hard-unlock tabs by data. This retires the "depth buries the loop" risk by construction.
- **From C — the journal as the player-facing narrative spine, era-skinned.** The News feed:
  every headline the sim already generates, rendered as period dispatches (telegram/fax/email),
  newest-first, with a "Why?" chip per item. Plus the single `EraTheme` resource-dictionary swap
  that makes the whole hub visibly age across a 1967→1997 career.

---

## 2. Unified hub design

### 2.1 Navigation shape (one shape)

A **single WPF window: persistent left tab rail + central content region + a collapsible
era-styled News dock on the right.** Not MDI, not a dashboard that replaces the loop — a shell
*around* the shipped Home. (Reconciles B's rail, A's pinned-loop rail, C's single-window
+ news feed.)

- **`HubViewModel`** owns the `ICareerSession` and an `ObservableCollection<HubTabViewModel>`
  with a `SelectedTab`. Today's `Home/Briefing/ResultEntry/Confirm/Standings/SeasonReview`
  content moves into the **Race** tab **unchanged**. `ShellViewModel`'s Start→Wizard→Home→
  Settings conducting is preserved; Home becomes the Hub.
- **Persistent header** (above rail + content, glanceable from every tab): season year, round,
  player standing, rep/OPI trend — lifted from the fields already on `HomeViewModel`.
- **Always-visible primary action button** in the header: **"Next: <round> Briefing" / "Enter
  Result"** — one click back to the loop from anywhere. *This is the anti-burial mechanism the
  constraints demand.* On career open and after every Apply, the Race tab is auto-selected — you
  are never stranded in a management screen.
- **Right-side News dock** (era-skinned, collapsible to a one-line ticker): live journal
  headlines. Tear-off to an always-on-top companion window is the optional "own windows" answer
  (**Q4**).
- **Full parity (decision 8):** number keys **1–9** jump to tab N; arrow keys navigate the rail;
  `Ctrl+Home` always snaps to the briefing/result card; `Esc` backs out of any minigame to the
  Race tab writing no state. Every rail entry is a mouse click target with a tooltip. Bake the
  accelerators + Why?-link focus order into `HubViewModel` from the first tab.
- **Minimal-narrative toggle** (already in settings) collapses the hub to exactly **HQ + Race +
  Standings** and strips the News dock/era chrome — depth is opt-in at the chrome level too.

### 2.2 Tab set (phased)

Two v1 posture choices are open: **Q1** (progressive unlock vs always-present) and the reveal is
noted per tab below assuming progressive unlock.

| Tab | v1 | Phase 2 | Phase 3 | Reveal trigger (if Q1=A) |
|---|---|---|---|---|
| **HQ / Home** | Permanent spine: season header, current-round card (briefing OR enter-result, unchanged), rep/OPI sparkline, top-3 news, slider recommendation chip | — | — | Always present |
| **Race** | The existing briefing→result→confirm flow, re-homed verbatim | — | — | Always present |
| **Standings** | Existing drivers/constructors/round-matrix VM, re-homed; rules-explainer chip; cells link into Why? (Q2) | — | — | Always present |
| **News** | Era-skinned dispatch feed over the journal; per-item Why? chip; filter by kind | Rivalry/market/retirement filters populate as phases ship | — | Always present (or first race, Q1) |
| **Career (driver dossier)** | Read-only lens: age curve, rep/OPI history, seasons completed, team lineage — pure render of `round_player_state`/`player_state` | — | — | First season complete |
| **Team** | Read-only causal board: historical name, tier 1–5 + plain label, car scalars as "~X% off the fastest", reliability trend, teammate head-to-head, the INPUT→grid causal arrows | — | Owner-Driver write controls (dev allocation surface) | Round 1 (you have a seat) |
| **Contracts** | Offer letters rendered as era-correct documents; AcceptOffer is the skip path | Negotiation minigame (counter-offer loop) | — | First offer exists (season end) |
| **History / Scrapbook** | Per-season review cards + the full Why? inspector home; era-correct paper per spread | — | Title-permutation math | First season complete |
| **Paddock** | (lite) retirement-watch + who-signed-where from season-end rows | Mid-season driver market, rumor mill, living-world dial | — | Phase 2 data |
| **Finances (ledger)** | — | Driver-side ledger: salary, prize fund, personal sponsors, crash-repair bills from typed DNF causes; crisis-ladder gauge | Full team ledger + bankruptcy (Owner-Driver) | Phase 2 data |
| **Rivals** | (optional lite H2H inside Team) | — | Full rivalry board + needle headlines | Phase 3 |

**Design rule enforced everywhere:** a first-timer using only the mouse finishes a full season
without opening a second tab. Every management tab and minigame only shapes **INPUTS** (car
scalars, difficulty anchor, morale, contract terms) or renders **OUTPUTS** (journal/OPI/rep/
offers). **No tab and no minigame ever touches an on-track finishing position.**

### 2.3 Minigames (prioritized; each deterministic + journaled + skippable)

The invariant, from all three designs: **journal the CHOICE, not the outcome.** Given
`(masterSeed, named stream, stored choice)`, the fold reproduces the delta — so replay stays
byte-identical. Every minigame is written into the **same fold transaction** as the round/season
it belongs to, and its derived rows are wiped by `WipeDerived` alongside offers/`round_player_state`.
Every one has a **Skip (use default)** that yields the exact result the career would have had with
no hub, and the skip is itself journaled as the default choice.

| Priority | Minigame | Loop | Sim inputs shaped | Skip = | Phase |
|---|---|---|---|---|---|
| **1** | **Setup Gamble** (pre-race) | On the briefing: one pick — Safe / Balanced / Aggressive (era-flavored). Deterministic pick, no timing/dexterity. Honest tradeoff shown up front (aggressive = more one-lap pace, higher reliability penalty). | The player's **own** car power/reliability scalars for **that round only**, merged into the existing grid-gen chain (pack → track form → round overrides → career form → **+minigame delta**) before normalization; optionally the OPI expectation baseline. Never another car, never a position. | Balanced = the exact scalars used today (no-op) | **v1** (smallest new surface; pattern-setter — see Q5) |
| **2** | **Media Moment** (post-race) | On the Confirm screen, a dismissible card: pick one of 2–3 era-voiced quotes reacting to the just-entered result (win / DNF / beat-rival). Deterministic pick. | A bounded **reputation** delta (+ rivalry heat / sponsor health later). Rep already feeds offer scoring. Points/positions are locked by the result already entered. | "No comment" = zero-delta | **v1-light** (rep only) → deepens with rivals/ledger |
| **3** | **Contract Negotiation** | Turn-based counter-offer volley from an offer letter: Accept / Counter (salary↑, +year, release clause) / Hold, against archetype-weighted patience (works = impatient/rich, minnow = patient/poor). A visible patience meter means no surprise walk-offs. Each team response draws a `negotiate` stream keyed `(year, team, counterIndex)`; re-scores via the **existing** OfferScore formula with a raised `salaryAsk`. | The accepted **contract terms** (salary band, years, release) = next season's INPUT to the season-end offer pipeline and the Finances salary line. | "Accept as offered" = today's one-click `AcceptOffer(teamId)` | **Phase 2** |
| **4** | **Sponsor Pitch** | Between seasons: match a pitch angle (results / glamour / heritage) to a seeded sponsor personality; `sponsor` stream resolves acceptance + term + payout band. One screen, keyboard-navigable. | Sponsor **income + health-decay** inputs to the Finances ledger fold → money that later buys car scalars / second-seat quality. | "Take the standard deal" = neutral default backer | **Phase 2** (personal) → **Phase 3** (team) |
| **5** | **Development Allocation** (Owner-Driver) | Winter budget split across weight/power/drag/reliability with diminishing-returns curves shown; confirm commits. `development` stream adds bounded, replayable noise. | Next season's **team car scalars + reliability** — the strongest tycoon INPUT lever, feeding the grid the game races. | "Let the chief decide" = balanced default drift | **Phase 3** |

### 2.4 Era immersion approach

One **`EraTheme`** enum (Telegram / Fax / Email) selected from the active pack's decade, wired as
a **single app-wide `ResourceDictionary` swap** driving typography, news-item chrome, accent
color, and paper/transition texture — one switch, everything downstream reads it. Depth of the v1
commitment is **Q3**.

- **1960s = TELEGRAM** (uppercase monospace, "STOP"-punctuated, ochre paper) — the 1960s headline
  bank is *already* written in wire-report voice, so copy and skin match on day one.
- **1980s = FAX** (sender/date header band, thermal-paper grain, roll-in transition).
- **1990s+ = EMAIL** (inbox rows, subject/sender, monospace→sans shift, clean accent).
- **Contracts/offers render as period correspondence** (typed 1967 telegram vs 1993 fax vs email)
  via a `DataTemplate` selected by era — same `PlayerOffer` data, different template.
- **Pacing:** the winter is deliberately slowed into beats already in the pipeline — final
  standings → press digest → retirements/foreshadow → seat-market shuffle → offer letters →
  negotiation → sign — each a news-wire beat, each skippable via the minimal-narrative toggle
  (which collapses the winter to "here are your offers, pick one"). The per-round loop stays
  instant (startup <1s, no animation gates).
- **Determinism preserved:** every era-flavored string is still selected via a named PCG32 stream
  (`headlines`/`media`/…); presentation introduces no un-seeded randomness. Skins are
  vector/CSS-like styles + a few tokens, **not** bitmap-heavy themes (keeps the single exe lean).
- **Data-driven:** a `data/rules/era-themes.json` maps decade → `{medium, accent, fontStack,
  paperTexture, datelineFormat}` so community packs can declare their own era feel.

### 2.5 Additive data-model deltas

Everything is additive. The discipline (from career-sim.md and confirmed in `ICareerSession.cs` /
`JournalStore.cs`): player **DECISIONS** become inputs that survive `WipeDerived` and replay
byte-identically; sim **RESULTS** become derived rows wiped + rebuilt by `Resimulate`. New seam
members follow the **exact** pattern already in the file — `NextSeason() => null`,
`StartNextSeason() => throw` — so existing sessions and the ViewModels test suite compile
untouched. *A non-default seam addition is the design's red line.*

**Seam (`ICareerSession`) — new default-implemented, read-only members:**

- `INarrativeFeed`-style `ReadFeed(seasonId)` → `Dispatch { resolvedHeadline, era, kind,
  sourceJournalSeqs, whyText }`, built by resolving existing journal rows through the existing
  `HeadlineBank`. (Powers News + the Why? chip.)
- `JournalFor(entity, round?)` → the causal chain for the Why? inspector (Q2).
- `TeamView()` → read model over `team_state` + tier + scalar summary + teammate stats from
  `AllSnapshots()`.
- `CareerTimeline()` → lineage-aware per-season cards for Career/History.
- `TabAvailability()` → which tabs have payload yet (drives progressive unlock, Q1).

All are **projections over data `StateStore`/`JournalStore` already hold** — v1 tabs need **no new
persistence** and **no migration**.

**Journal — new phases only** (strings are save-format-stable; follow the
`DataJournalPhases`/`JournalPhases` convention; all are **derived** sim state so the byte-compare
covers them):

- `setup.choice` (round, chosen card id, applied scalar delta)
- `media.moment` (round, question phase|cause, chosen response id, rep/rivalry delta)
- `contract.negotiation` (team, exchange log, final terms) — Phase 2
- reserved for Phase 3: `sponsor.pitch`, `dev.alloc`

**RNG streams — reserve now so keys stay stable** (same `SplitMix64(Fnv1a64(subsystem|year|round|
entity) XOR masterSeed)` formula, one stream per `(subsystem,year,round,entity)`, so a new
subsystem never perturbs an existing sequence): `setup`, `media`, `negotiate`, `sponsor`,
`development`, `rivalry`.

**`teams.json` (additive, defaulted so all 8 bundled packs load unchanged):**

- optional `sponsorSlots` (list of sponsor personalities/brands per team) — Phase 2 ledger
- optional `engineSupplier` — Phase 2/3 cost line
- optional pack-level `economy` block: prize-fund-by-position, repair-cost bands, sponsor
  personalities, development diminishing-returns curve (sits alongside the existing `pointsSystem`)
- `name` stays the historical display key everywhere; lineage ids stay the cross-era key

**`drivers.json` (additive, defaulted):**

- optional `voice` tag (e.g. gracious / combative / neutral) — lets media/rivalry copy pick
  needling vs gracious variants; engine ignores if absent (matches the existing "only
  raceSkill/qualifyingSkill required" leniency)

**Career DB (migrations, Phase 2+; nothing for v1):**

- `minigame_choice` row keyed `(season_id, round, stream_name, choice_json)` — written in the
  fold transaction, **deleted by `WipeDerived`** with offers/`round_player_state`. Replay needs
  only the recorded CHOICE (outcome is a pure function of seed+choice).
- an optional per-round **scalar-delta channel** in grid-gen inputs (from Setup Gamble), merged in
  the existing chain, defaults to zero (== skip).
- Phase-2 economy migration (v4): a `ledger` analogue to the state tables —
  `season_ledger (season_id, stage start/end, state_json)` for opening balance + sponsor/contract
  inputs, and `round_ledger_state (season_id, round, state_json)` **folded by `FoldRound` exactly
  like `round_player_state`**, with `WipeDerived` extended to delete the derived ledger rows.
  **Every ledger line item is ALSO a journal row** (cause = salary/prize/repair/sponsor/development)
  so the News feed and Why? inspector get it free and `Resimulate` byte-checks it.
- Phase-3: `rivalry` rows; `scrapbook_pin (career_id, season_id, journal_seq)` — a user
  annotation over existing rows, excluded from the replay byte-compare like provenance.

No change to raw result payloads, the points engine, or the f1db oracle suite.

---

## 3. Build order — first 3 increments (each a shippable slice, additive to `ICareerSession`)

Starting from v0.3.0 (working loop + journal + offers + era transition, 895/895 tests).

### Increment 1 — **The Hub Shell + News feed** *(zero sim, zero migration)*
Re-home the loop and ship the first visible immersion payoff.
- Introduce `HubViewModel` owning the existing `ICareerSession`; move today's
  `Home/Briefing/ResultEntry/Confirm/Standings/SeasonReview` into the **Race** and **Standings**
  tabs **verbatim** (existing tests are the acceptance gate — the loop cannot regress).
- Lift the persistent header + always-visible primary loop button above the rail.
- Add the **News** tab: a read-only `ReadFeed(seasonId)` projection resolving existing journal
  rows through the existing `HeadlineBank` into era-skinned dispatches, newest-first, with a per-
  item **Why?** chip (`deltaJson` → plain sentence).
- Add the **`EraTheme`** resource-dictionary swap (telegram/fax/email) keyed off the pack decade.
- **Why first:** highest visible value for the least new surface, zero sim risk — the story is
  already generated and journaled every race; today it flashes by on the confirm screen and is
  lost. Surfacing it is provably safe (read-only over folded state), needs no schema change, and
  lays the two load-bearing foundations (era skin + narrative read-seam) the whole hub reuses.
  Gives Mike a tangible, steerable artifact on day one. *(Scope here flexes on Q2/Q3.)*

### Increment 2 — **The read-only management lenses: Team + Career/History + Why? inspector**
Prove the "glanceable management + legible sim" thesis with tabs that need no new sim math.
- Add `TeamView()`, `CareerTimeline()`, `TabAvailability()` as default-implemented read-only seam
  members (projections over `team_state`/`player_state`/`round_player_state`/`AllSnapshots()`).
- **Team** tab: historical name, tier + plain label, car scalars as "~X% off the fastest",
  reliability trend, teammate head-to-head, the INPUT→grid causal arrows.
- **Career/History** tab: age/rep/OPI history, per-season review cards, lineage timeline; make it
  the full-screen home of the **Why?** inspector (scope per Q2).
- Wire **progressive tab unlock** via `TabAvailability()` (or always-present, per Q1).
- **Why second:** all data already exists fully in v1 — pure OUTPUT rendering, no sim additions,
  no migration — so it ships immediately and demonstrates the depth-is-a-lens promise, while
  building the unlock scaffolding every later tab plugs into.

### Increment 3 — **The first minigame + Contracts-as-documents**
Prove the deterministic/journaled/skippable minigame pattern end to end, in a real tab.
- Ship **Setup Gamble** (or Media Moment, per Q5): new `setup.choice` journal phase + `setup`
  stream + the optional per-round scalar-delta channel merged into the existing grid-gen chain
  (defaults to zero = skip). Journal the choice; the fold reproduces the delta.
- Extend the **byte-identical replay CI test** to cover a career that used the minigame AND its
  skip-everything equivalent — *before shipping.*
- Render existing offer letters in the **Contracts** tab as era-correct documents (same
  `PlayerOffer` data via era `DataTemplate`); `AcceptOffer` stays the skip path.
- **Why third:** it's the smallest INPUT-shaper and the pattern-setter for every later minigame,
  and it drops into the proven shell from Increments 1–2 instead of forcing a rewrite. Establishes
  the determinism guard (choice-not-outcome, wiped-by-`WipeDerived`, new independent stream) that
  the whole economy later depends on.

**Deferred to Phase 2/3 (data model reserved from day one):** Finances ledger (as a third
fold-stage), Contract Negotiation, Sponsor Pitch, Paddock mid-season market, Rivals, Development
Allocation, Owner-Driver write controls.

---

## 4. Cross-cutting risks (and mitigations)

1. **Scope creep vs "lightweight single exe / loop never buried."** Every v1 tab is a pure lens
   (no new sim); economy is gated behind the phase line and the minimal toggle. HQ+Race+Standings
   is always the whole app if you want it to be.
2. **Determinism erosion via minigames.** Enforce choice-not-outcome + same fold transaction +
   new independent stream + `WipeDerived` cleanup; the byte-identical replay CI test must cover a
   minigame career and its skip equivalent before any minigame ships.
3. **New streams perturbing existing journals.** Avoided by construction (independently keyed) but
   must be covered by a replay regression test that the 8 existing packs still reproduce
   byte-identical after the streams are reserved.
4. **"Sim never decides races" blurring once money exists.** Guard explicitly: money only changes
   generated-XML INPUTS (scalars, teammate quality) and consumes typed results — there is no code
   path where the ledger reads a result the player didn't enter.
5. **Headline bank is 1960s-only today.** The News feed will look thin in 1988 until 1970s/80s/90s
   copy lands — a *content* task (pure JSON, no code), but it gates the "feels like 1988" promise,
   so it should land with each era pack.
6. **Multi-era Scrapbook depends on M6 carryover across many transitions** — byte-identical-tested
   for one transition today; 30-year careers need a stress test.
7. **Full parity across a growing tab set (decision 8).** Bake number-key/arrow accelerators and
   Why?-link focus order into `HubViewModel` from the first tab; the existing parity ViewModel
   tests stay the gate. Every rail entry and minigame choice needs both a click target with
   tooltip AND a keybind.
