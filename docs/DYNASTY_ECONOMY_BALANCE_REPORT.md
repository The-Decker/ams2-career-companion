# Dynasty economy — balance report

_2026-07-17. Evidence from `tests/Companion.Tests/Scenarios/DynastyEconomyBalanceHarness.cs`
(`COMPANION_ECONOMY_SIM=15`, 135 full multi-decade careers through the REAL
creation/decision/fold/rollover/era-transition machinery; seeds 910,000+, fully reproducible).
System doc: `docs/dev/dynasty-tycoon-economy.md`. These are measured distributions, not claims._

## Method

- **Catalog**: a synthetic pinned Dynasty sequence 1967–1980 — 14 seasons × 8 rounds, five teams
  across the tier range (5/4/3/2/1), ten cars; the player owns the tier-3 team (45,000 opening).
  The sequence crosses the 1960s→1970s era boundary, so every table triples mid-career.
- **Result profiles** (the on-track variable; synthetic results stand in for the human importing
  AMS2 results): `strong` (mean P2.2), `midfield` (P5.0), `backmarker` (P8.3), with 3.5%
  mechanical-DNF and 3% accident-DNF rates (severity-sampled → real repair bills).
- **Management strategies** (the owner variable; every decision goes through the REAL validated
  `DeclareEconomyDecision` path — a strategy can only do what a player could):
  `frugal` (no staff, pay-driver second seat, no development),
  `balanced` (staff 1, retained, one development attempt per window),
  `overhire` (staff 3, retained, development to the cap whenever cash allows).
- Development feeds back into the synthetic finish (−0.25 places per stage) mirroring the sim's
  own expectation-channel feedback (economy §6).

## Findings (n=15 per cell)

| Profile / strategy | Bankrupt | Median fold season | Final balance (median) | Titles avg | Front seasons avg |
|---|---|---|---|---|---|
| strong / balanced | 0% | — | 1,075,121 | **10.1** | 13.3 |
| strong / overhire | 0% | — | 785,396 | **11.2** | 13.3 |
| strong / frugal | 0% | — | 5,350,590 | 2.1 | 8.5 |
| midfield / balanced | 0% | — | 530,688 | 0.1 | 0.2 |
| midfield / frugal | 0% | — | 2,790,100 | 0 | 0 |
| midfield / overhire | **73%** | S2 | −4,677 | 0 | 0 |
| backmarker / frugal | 0% | — | 333,100 | 0 | 0 |
| backmarker / balanced | **100%** | S3 | −10,288 | 0 | 0 |
| backmarker / overhire | **100%** | S1 | −18,817 | 0 | 0 |

Balance trajectories (per-season p10/p50/p90) are in the harness summary; raw per-career JSONL
ships with every run.

## What the numbers say

1. **Bankruptcy is a real risk for bad play and avoidable with good play** (the mission's tuning
   target). Over-hiring — top staff tier + maximum development on a mid-budget income — folds
   73% of midfield teams within ~2 seasons; at a tail team it is near-certain death inside the
   first season. Matching spending to income (frugal at the tail, balanced at midfield) produced
   **zero** bankruptcies in 45 careers.
2. **Development wins races.** With identical driving, the balanced/overhire strong teams took
   ~10–11 titles in 14 seasons versus ~2 for the frugal one (which banks 5.3M but concedes the
   front). Money is a second lever on the same result, exactly as the brief demands.
3. **A tail team must run lean** — full salary + staff at backmarker results is a structurally
   losing configuration (−15–20k/season operating margin), while the pay-driver/no-staff
   configuration survives indefinitely on ~330k reserves. This is deliberate and era-authentic
   (privateers folded constantly); the Team Ledger's statement makes the bleed visible round by
   round, and the grace window (4 deficit rounds) plus the season-settlement cheque give a
   struggling owner real levers before the end.
4. **The era boundary bites**: crossing into the 1970s triples both income and costs, so teams
   with a healthy margin accelerate and teams bleeding nominal reserves die faster — inflation
   punishes stagnation without any special-case code.
5. **Hoarding is viable but trophyless.** The frugal strategies accumulate large nominal
   fortunes (up to ~5.3M by 1980) because nothing forces reinvestment. Accepted for Alpha: the
   fortunes buy nothing but security, and every title-metric favours the spenders. A future
   pass could add wealth sinks (facilities, regulation-change write-offs).

## Reproduce

```
COMPANION_ECONOMY_SIM=15 dotnet test tests/Companion.Tests --filter EconomySweep_RunsWhenConfigured
```

Optional: `COMPANION_ECONOMY_SIM_OUT=<path.jsonl>`, `COMPANION_ECONOMY_SIM_DOP=<n>`. The suite
never pays for the sweep — it is dormant without the environment variable, like the SMGP-300
harness beside it.
