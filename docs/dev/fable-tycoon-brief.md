# Fable mission — Grand Prix Dynasty tycoon economy

_Hand this whole file to Fable as the mission. It is self-contained: Fable starts fresh and must
read the repo before building. Written 2026-07-17 by Claude (Head of Coding). Lane: Fable owns the
new economy DOMAIN in `src/Companion.Core/**` + its data in `data/rules/**` + its tests; the GUI
(`src/Companion.App/**`) is a separate lane and is briefed separately — build the bindable
ViewModel surface, not the XAML._

---

## 0. What you are building

**The driver-owner tycoon economy for Grand Prix Dynasty mode** — the "required product pillar" of
`grandPrixDynasty` (see `docs/dev/career-modes-alpha1.md` §3). In Dynasty the player is *both the
driver and the team owner*, running a constructor chronologically through the historical World
Championship timeline (product horizon 1960–2020, playable where a faithful pack exists — today the
earliest is `packs/f1-1967`).

The driver half already exists and is DONE: progression v2 (L300, Racing DNA, 90 mastery skills,
499-SP pool), accidents/injury/mortality, news/history. **You build the OWNER half:** the team
ledger — money in, money out, decisions that compound over a multi-decade career, and the failure
state (bankruptcy) that makes those decisions matter.

This is a career-defining subsystem. Build it to the same bar as the rest of this codebase:
deterministic, replay-verified, data-driven, fully tested.

---

## 1. Read the repo FIRST (do not skip)

Before designing anything, read and internalize:

- `docs/PROJECT.md` — whole-project onboarding.
- `docs/dev/career-modes-alpha1.md` §2–3 — the three-mode contract; Dynasty's exact owner rules.
- `CLAUDE.md` — the layering rules (Core = no I/O, no WPF, no DB; pure data rules; exact rational
  arithmetic; envelope-versioned folds; the f1db oracle is NEVER touched).
- `docs/dev/character-system.md` §3 (determinism) and `docs/dev/newsroom-history-overhaul.md` D1/D4
  — the "pure function of journaled results" contract every fold obeys.
- `src/Companion.Core/Career/CareerStates.cs` — `PlayerCareerState`, `TeamCareerState`,
  `DriverCareerState`. **`TeamCareerState` is your seed** (it currently carries `Tier`/`LineageId`;
  extend it, additively, with the ledger).
- `src/Companion.Core/Career/RoundUpdate.cs` + `SeasonEndPipeline.cs` — the round fold and the
  season-end pipeline. Your economy folds alongside these, same discipline.
- `src/Companion.Core/Career/TeamArchetypes.cs` + `data/rules/career-team-archetypes.json` — team
  identity/strength inputs.
- `src/Companion.ViewModels/Services/CareerSessionService.cs` `SmgpTeamDashboard()` — the existing
  **read-only** team dashboard (SMGP). It is display-only with NO economy fold. Your work turns the
  Dynasty side of this into a real folded economy; reuse its projection patterns, do not copy its
  read-only-ness.
- `src/Companion.Data/ReplayService.cs` — `ImportAndFoldRound`, `Resimulate`, `WipeDerived`. Your
  economy state is DERIVED and must survive `Resimulate` byte-identically.
- `src/Companion.Core/Numerics/Rational.cs` — use exact rational money; never `double` for currency.

Produce a short architecture + integration note (what you'll add, where it folds) before writing code.

---

## 2. Non-negotiable architecture constraints

These are load-bearing. Violating any one breaks the whole save/replay contract.

1. **The economy is a pure fold over journaled inputs.** Given the same career seed + the same
   journaled results and the same player decisions, every ledger value reproduces byte-for-byte.
   No `Date.Now`, no RNG stream that isn't derived from the pinned master seed via `StreamFactory`.
2. **All economy state is DERIVED** (rebuilt by `Resimulate` from inputs), EXCEPT the player's
   explicit decisions, which are **INPUT rows** (provenance-excluded, journaled once, never
   re-derived). Money you *earn/spend automatically* = derived; money you *choose to spend* (buy an
   upgrade, sign a driver, take a sponsor) = an input decision.
3. **Envelope-versioned + per-career gated.** New fold behavior rides a new envelope version and a
   creation-time career flag, so existing careers replay byte-identically and only NEW Dynasty
   careers get the economy. Mirror `FormAware` / `SmgpMode` / `MortalityMode`.
4. **Exact rational money.** `Companion.Core.Numerics.Rational` for every currency value. No floats.
5. **Data-driven, not hard-coded.** All balance constants (prize tables, cost curves, sponsor
   payouts, development costs, repair costs, inflation, entry fees) live in a new
   `data/rules/dynasty/economy.json` (+ era overrides), loaded like the other rules files. The
   engine reads tables; it never bakes an era's numbers into code.
6. **The f1db oracle is untouched.** Scoring/standings are unchanged; the economy consumes their
   OUTPUT (finishing positions, championship points), never modifies them.
7. **No fabricated history.** Real teams'/eras' economics are stylized game systems, not claims of
   fact. Keep them clearly game-mechanical; do not assert real financial figures.
8. **Core stays pure.** No I/O, no WPF, no SQLite in `Companion.Core`. Persistence (new columns or
   JSON state fields) goes through `Companion.Data` additively with a migration if a table changes;
   prefer additive JSON fields on the folded state so no schema migration is needed (see how
   progression v2 added fields to `PlayerCareerState`).

---

## 3. The economic model (design it, then build it)

Design a model that makes a *multi-decade* career a series of meaningful money decisions with a real
failure state. Cover at least these, all data-driven and era-scaled:

### 3.1 Income
- **Prize money** — per-round and end-of-season, scaled by finishing position and championship
  standing, from a data table (era-scaled; 1967 purses ≠ 2020 purses).
- **Sponsorship** — the player signs sponsors (an INPUT decision) that pay per race / per season /
  on performance triggers (a podium bonus, a title bonus). Reuse/extend the sponsor concept from
  `data/rules/smgp/sponsors.json`; author a Dynasty sponsor board with tiered deals whose
  availability depends on team reputation/results.
- **Optional**: appearance/starting money, manufacturer engine deals.

### 3.2 Costs
- **Car development** — the player spends to improve the constructor's car (which then affects the
  seat's competitiveness — the expectation/pace inputs the sim already uses). Escalating cost curve.
- **Repairs** — accidents/DNFs cost money (tie into the existing accident/DNF fold — a heavy crash
  is a big repair bill; ties the injury system to the ledger).
- **Staff / second driver** — hire a second driver (salary vs. their points contribution to the
  constructors' championship and prize money) and staff (flat upkeep improving development
  efficiency or reliability).
- **Entry fees / travel / upkeep** — per-round and per-season fixed costs, era-scaled.

### 3.3 Balance sheet, cash flow, and failure
- A running **balance** (rational), a per-round/per-season **cash-flow statement** (income − costs),
  and a **bankruptcy** terminal state: sustained negative balance past a data-defined grace ends the
  Dynasty career (a real game-over, like mortality — reuse the terminal-state pattern:
  `CareerStates`, the death/game-over flow in `docs/CAREER_GAME_OVER_FLOW.md`).
- Development and money must **feed back into competitiveness**: an under-funded team fields a slower
  car (worse expected finish → fewer points → less prize money → the death spiral), a well-run one
  climbs. The player's *driving* (progression v2) and *management* (this economy) are two levers on
  the same result.

### 3.4 The decision surface
Enumerate the player's economic decisions as INPUT rows (each journaled once, replay-stable):
sign/drop a sponsor, buy a development increment, hire/fire a second driver or staff, choose a repair
level, take a loan (if you model debt). Each has a cost, a deterministic effect, and a place in the
season/round lifecycle (most decisions happen at the season review / between rounds, not mid-race).

Write a `docs/dev/dynasty-tycoon-economy.md` design doc capturing the model, the tables, and the
decision list, matching implemented behavior — same bar as `docs/LEVEL_300_SYSTEM_SPEC.md`.

---

## 4. Integration points

- **Fold order** — the economy folds in the round/season pipeline AFTER results/standings/accidents
  are known (it consumes finishing position, DNF cause, championship points). Slot it into
  `RoundUpdate` / `SeasonEndPipeline` behind the new envelope version + career gate.
- **Competitiveness feedback** — development spend adjusts the constructor/seat strength the sim
  already reads (the expectation/pace-anchor inputs). Do this through the existing seat-strength /
  expectation channel, versioned and per-career-gated, so it never perturbs a non-Dynasty career.
- **Terminal state** — bankruptcy joins the existing terminal states (Deceased / SMGP CareerOver) in
  the availability model, so entry paths block and a Game-Over-style screen model is produced (build
  the bindable model; the GUI lane renders it).
- **News/History** — emit economy events into the existing newsroom spine (`Companion.Core/Newsroom`)
  as new `NewsEventKind`s (sponsor signed, big repair bill, near-bankruptcy, a title's prize windfall,
  bankruptcy) — display-only projections, same pattern as the progression events already there.
- **ViewModel surface** — extend the team-dashboard projection into a real economy view model
  (balance, cash-flow statement, sponsor board, development options, staff, the decision commands).
  Bindable, tested against a fake session; the App lane binds the XAML from your bind contract.

---

## 5. Testing + balance (definition of done)

- **Unit** — every table lookup, cost curve, income calc, the bankruptcy predicate, each decision's
  effect, exact-rational money with no drift.
- **Determinism** — a Dynasty career with economy decisions **re-simulates byte-identically**
  (`ReplayService.Resimulate`), decisions included. This is the single most important test — mirror
  `AccidentFoldDeterminismTests` / `CharacterFoldDeterminismTests`.
- **Legacy safety** — a non-Dynasty career (SMGP, legacy) folds byte-identically to before (the
  envelope gate proves the economy is dormant). Add the guard test.
- **Balance harness** — extend the pattern of `tests/Companion.Tests/Scenarios/BalanceSimulationHarness.cs`:
  run many synthetic multi-decade Dynasty careers across management strategies (frugal / aggressive
  development / over-hire) and report balance trajectories, bankruptcy rates, and how often a
  well-run vs. poorly-run team reaches the front. Produce a balance report with real distributions,
  not claims. Tune the tables so bankruptcy is a real risk for bad play and avoidable with good play.
- **Full suite green + release build clean.** No `double` money, no TODO-behind-primary-behavior.

---

## 6. Scope guardrails

- Dynasty only. SMGP has **no historical tycoon economy** (career-modes-alpha1.md §2) — never let the
  economy touch an SMGP or legacy career.
- Don't build the GUI XAML; build the tested ViewModel/domain and a bind contract.
- Don't invent packs or historical data; consume what exists (packs listed in `packs/`, earliest 1967).
- Don't touch scoring/standings/the oracle.
- Deliver a truthful completion report: the model, the tables, files added/changed, tests added +
  results, the balance distributions, byte-identical-replay confirmation, and any accepted deviations.
