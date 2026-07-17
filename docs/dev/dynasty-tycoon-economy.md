# Grand Prix Dynasty — the driver-owner tycoon economy

_Design + implemented behavior, 2026-07-17 (Fable, Head-of-Coding lane). Mission brief:
`docs/dev/fable-tycoon-brief.md`. Mode contract: `docs/dev/career-modes-alpha1.md` §3 ("standings,
seats, team ledger, staff/second driver, sponsors, development, repair costs, prize income, and
bankruptcy all belong to this save"). This doc is written to the same bar as
`docs/LEVEL_300_SYSTEM_SPEC.md`: it matches implemented behavior; deviations from the mission brief
are listed in §10._

---

## 1. Architecture at a glance

The economy is a **pure fold over journaled inputs**, exactly like every other career system:

- **State** — `Companion.Core.Dynasty.DynastyEconomyState`, a new nullable record on
  `PlayerCareerState` (`[JsonIgnore(WhenWritingNull)] public DynastyEconomyState? Economy`), the
  `SmgpState` pattern: seeded once at creation, carried forward each round via record `with`,
  canonical dictionary ordering + structural equality so the byte-identical replay gate holds.
  Null for every non-Dynasty and every pre-feature career → their `player_state` blobs are
  byte-identical and the whole system is inert.
- **Gate** — the state is seeded at creation ONLY when the campaign plan's mode is
  `grandPrixDynasty` AND the creation request opts in (`CareerCreationRequest.DynastyEconomy`,
  default false; the wizard sets it true for new Dynasty careers — the `FormAware` composition).
  A legacy career (absent `ExperienceMode`) can never gain the economy (§2.1 of the mode
  contract); an SMGP or Passport career never seeds it. Every fold gates on
  `player.Economy is { Bankrupt: false }` — the `Smgp is { CareerOver: false }` shape.
- **Rules** — all balance numbers live in `data/rules/dynasty/economy.json`, parsed by
  `Companion.Core.Dynasty.DynastyEconomyRules` (pure `Parse`, throwing validation,
  `schemaVersion` pinned — the `RacingDnaCatalog` discipline). Loaded once via
  `CareerRulesData` and threaded into folds through a nullable `ReplaySimInputs.EconomyRules`
  (the `CharacterRules` precedent — null folds byte-identically). The state records the
  `rulesVersion` it was created under; the fold resolves tables by that exact version so an old
  career never drifts onto later balance numbers.
- **Money** — `Companion.Core.Numerics.Rational` everywhere. Serialized as `"3"`/`"1/7"` strings
  by `CoreJson`. No floats, no drift; per-round accruals divide exactly (`seasonAmount / rounds`
  sums back to `seasonAmount`).
- **Decisions** — the player's economic choices are **provenance-excluded INPUT rows**
  (`JournalPhases.EconomyDecision = "economy.decision"`, registered in
  `DataJournalPhases.IsProvenance`), declared for the NEXT unfolded round (the
  `PlayerRoundConditionsStore.Declare` shape: refused once that round has a raw result), read
  back per season by `ReplayService.ReadEconomyDecisions`, and applied unconditionally by the
  round fold in seq order. The fold NEVER validates-and-skips a journaled decision (validation
  gates only the acceptance of new input rows); its DERIVED rows are byte-compared.
- **Fold order** — inside `ReplayService.ComputeRoundFold`: economy decisions apply FIRST
  (before `RoundUpdate.Apply`, so a development buy is felt in that round's expected finish),
  then the existing round fold runs unchanged, then the round **cash-flow settlement** row is
  emitted after the accident block. Season-end economy (season prize, sponsor season/title
  bonuses, statement) runs in `SeasonEndPipeline` gated the same way, after final standings.
- **No new RNG.** The whole economy is deterministic arithmetic over journaled results and
  decisions — zero stream draws, so it cannot perturb any existing sequence.
- **Terminal state** — `Bankrupt` mirrors `Deceased`/SMGP `CareerOver`: set by the fold, carried
  verbatim, never reset; fold-entry hard stop; availability gating; a DB-free
  `BankruptcyScreenModel` captured for the GUI lane.

## 2. What is DERIVED vs INPUT

| Kind | Examples | Mechanism |
|---|---|---|
| INPUT (survives resim, provenance-excluded) | sign/drop sponsor, buy development, staff hire/fire, second-seat deal | `economy.decision` journal rows |
| DERIVED (wiped + byte-compared) | every ledger movement: prize, fees, repairs, sponsor payments, salaries, the balance itself, deficit counter, bankruptcy | `economy.round` / `economy.season` / `economy.applied` / `economy.bankruptcy` journal rows + the `Economy` state on `PlayerCareerState` |

No new tables: economy state rides `round_player_state`/`player_state` (wiped + rebuilt by
`Resimulate` for free); statements are projections over journal rows.

## 3. The ledger year: what happens when

**Season start** (seeding/rollover): balance carries; development level carries at the
data-defined carryover fraction; sponsor contracts decrement seasons-remaining and expire;
staff/second-seat arrangements carry.

**Between rounds** (the decision window, any time before round N's result is imported):
the player may journal decisions for round N. Affordability is validated against
(current balance − already-pending spend); availability (sponsor slots, reputation floors,
era windows, dev cap) is validated at acceptance time only.

**Round N fold**:
1. `economy.applied` rows — each pending decision applied in seq order (dev level +1, sponsor
   contract added/removed, staff tier change, signing bonuses credited, one row per decision).
2. The unchanged existing fold (`RoundUpdate.Apply` sees the post-decision state — development
   affects this round's expected finish via the seat-strength channel, §6).
3. `economy.round` — the round settlement, one row with the full statement:
   - income: player race prize (by classified finish), second-car race prize (retained deal
     only), appearance money, sponsor per-race payments, sponsor podium/win bonuses (player
     car), pay-driver backing accrual;
   - costs: entry fee, logistics, upkeep, staff accrual, second-driver salary accrual
     (retained deal), repairs (player accident by severity / mechanical / driver-error;
     second-car flat repair when the teammate is unclassified);
   - `balance from → to`, and the deficit counter update.
4. Bankruptcy check (§7) — possibly `economy.bankruptcy`, terminal.

**Season end** (`SeasonEndPipeline`, after final standings): `economy.season` — season prize by
constructors' position, sponsor per-season payments + title bonus, then contract
season-decrement/expiry and the development carryover snapshot into the next-season start state
(via the unchanged rollover copy).

**Era transition**: the ledger crosses unchanged in nominal terms; era scaling is applied at
TABLE level per season year (§4), so purses and costs grow together — the existing
`era.economy` identity row is untouched for every career.

## 4. The tables (data/rules/dynasty/economy.json)

All values are stylized game numbers, not claims about real historical finance. Base tables are
authored in 1960s-era units; each season's effective table = base × the era index for the
season's year. Both income AND costs scale by the same index, so the game's shape is era-stable
while the numbers feel era-right.

- `eraScaling` — year-banded rational indices (1950–69: 1, 1970s: 3, 1980s: 10, 1990s: 30,
  2000s: 80, 2010–29: 150).
- `startingFunds.byTier` — opening balance by the starting team's tier (the owner starts with
  the resources of the outfit they run).
- `racePrize.byPosition[]` + `classifiedDefault` — per-round purse by classified finish; DNF
  earns nothing. `appearanceMoney` per round started.
- `seasonPrize.byConstructorPosition[]` + `default` — end-of-season constructors' money.
- `entryFee`, `logisticsPerRound`, `upkeepPerRound` — the fixed cost floor.
- `repairs` — accident Light/Medium/Heavy (consumes the envelope's
  `PlayerAccidentSeverity`), mechanical, driver-error, `secondCarDnf` flat.
- `development` — `baseCost`, escalating growth ratio per level, `maxLevel`, carryover
  fraction at season rollover, per-level expected-finish effect (§6), engineering staff
  discount per tier.
- `staff.engineering[]` — tiers with per-season upkeep (accrued per round) and their dev
  discount.
- `secondSeat` — retained-deal salary per season (accrued per round; the team then collects
  the second car's race prize) vs pay-driver deal (backing income accrued per round; the
  second car's prize is forfeit). The occupant is always the pack's authored teammate — the
  faithful grid is never touched; this is a contract-economics lever only.
- `sponsors` — slot counts by tier (title 1 / major 2 / minor 3) and the authored Dynasty
  board: id, name, tier, era window, availability floors (min reputation, best constructors'
  position), signing bonus, per-race, per-season, podium/win/title bonuses, contract length.
- `bankruptcy` — `graceRounds` (consecutive rounds allowed to END in deficit) and `hardFloor`
  (immediate game over below it).

## 5. The decision surface (INPUT rows)

Each `economy.decision` DeltaJson carries `{ kind, ...payload }`:

| kind | payload | effect (at the next round's fold) |
|---|---|---|
| `signSponsor` | sponsorId | contract added, signing bonus credited |
| `dropSponsor` | sponsorId | contract removed (no refund) |
| `buyDevelopment` | — | dev level +1, cost debited (escalating, staff-discounted) |
| `setStaff` | tier | engineering tier set; upkeep accrues from now |
| `setSecondSeat` | deal (`retained`/`payDriver`) | second-seat contract economics switch |

Acceptance validation (service layer only, never the fold): career not terminal, round has no
raw result yet, sponsor exists/era-valid/slot-free/floors met, dev below cap, affordability
against balance − pending spend. Decisions are append-only; the fold applies them in seq order.

## 6. Competitiveness feedback

Development level feeds the sim's existing expectation channel through
`SeatStrengthModel.ExpectedFinish`'s `playerStrengthBonus` parameter (inert 0.0 default —
bit-exact shipped math for every other career): the fold passes
`DevelopmentLevel × development.strengthPerLevel` (0.015; 8 levels ≈ one budget-tier step) from
the POST-decision state, so an increment bought over the break is felt the same round. The
briefing's `CurrentExpectedFinish` counts pending buy-development declarations too, preserving
the Setup-Gamble parity contract (the shown number IS the staked number), and
`SeatExpectationBreakdown.DevelopmentAdjustment` explains the term. Zero for level 0 — an
unfunded team is exactly as fast as the pack authored it. Non-Dynasty careers hit the identity
path — guard-tested.

## 7. Bankruptcy

After each round settlement: balance < 0 increments `DeficitRounds`, ≥ 0 resets it. Bankruptcy
triggers when `DeficitRounds > graceRounds` OR balance ≤ `hardFloor` (era-scaled). Terminal:
`Bankrupt = true` carried verbatim; fold-entry hard stop (the CareerOver pattern); Apply/Preview
availability gating; a `BankruptcyScreenModel` captured DB-free at the transition (the
`DeathScreenModel` pattern) for the GUI lane to render.

## 8. News

Economy moments surface through the existing display-only newsroom shaping (the newsroom reads
journal rows; folds never emit news): sponsor signed, a big repair bill, near-bankruptcy (grace
window entered), a title's prize windfall, development milestones, bankruptcy. Same pattern as
the progression events.

## 9. ViewModel surface (bind contract for the GUI lane)

`CareerSessionService` exposes a Dynasty economy projection: balance, the season cash-flow
statement (from `economy.round`/`economy.season` rows), the sponsor board with availability +
signed contracts, development state + next-increment cost, staff/second-seat state, pending
decisions, and decision commands that call the resolve service. Additive default members on
`ICareerSession` so fakes compile. No XAML in this lane.

## 10. Accepted deviations from the mission brief

1. **The ledger lives on `PlayerCareerState.Economy`, not on `TeamCareerState`.** The brief
   suggested extending `TeamCareerState`; but `TeamCareerState` is a per-AI-team list row, and
   the ledger is the OWNER's — singular, career-scoped, and required to survive seat/lineage
   changes and era transitions. Riding `PlayerCareerState` mirrors `SmgpState` exactly (the
   established whole-mode-state pattern), keeps every serialized team row byte-identical, and
   gets `Resimulate` wipe/rebuild for free via `round_player_state`.
2. **No result-envelope version bump.** Decisions are between-round inputs, so they are journal
   INPUT rows (the `smgp.swap` family), not envelope fields; the envelope stays at v9. The
   "envelope-versioned" discipline is carried by `DynastyEconomyState.Version` +
   `economy.json` `schemaVersion` + exact-version table resolution instead.
3. **No loans in v1.** The grace window IS the modeled overdraft; a first-class debt instrument
   is deferred (the brief marked it optional).
4. **The owner-driver pays themselves no salary**, and the legacy offer system continues to
   operate unchanged (scaffolding per the mode contract). The economy's income is prize +
   sponsors + backing only.
5. **Second-driver hire/fire does not change the grid occupant** — the pack teammate is
   authored (faithful-season rule); the decision is the contract-economics lever (§4). True
   seat control belongs to a future pass alongside the seat-market rework.
6. **Sign/drop sponsor churn re-credits the signing bonus** (a drop is free, a re-sign pays the
   bonus again). Left as-is for Alpha: it has no gameplay payoff — the balance report shows money
   is trophyless beyond a comfortable margin, and it would take ~20 manual decisions per race
   window to farm a few thousand units. A one-time-per-career signing bonus is the future closer
   if churn ever becomes attractive.

## 11. Balance evidence

The tables shipped in `data/rules/dynasty/economy.json` are the TUNED values (two harness
iterations): `docs/DYNASTY_ECONOMY_BALANCE_REPORT.md` carries the measured distributions from
135 multi-decade careers (profiles × strategies through the real machinery) — over-hiring folds
73–100% of mid/tail teams inside 1–3 seasons, matched spending never went bankrupt, development
converts money into titles (~10 vs ~2 at identical driving), and the era boundary accelerates
the healthy while killing the bleeding. Re-run with
`COMPANION_ECONOMY_SIM=<n> dotnet test --filter EconomySweep_RunsWhenConfigured`
(`tests/Companion.Tests/Scenarios/DynastyEconomyBalanceHarness.cs`; dormant otherwise).
