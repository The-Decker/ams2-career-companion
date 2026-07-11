# Career hub vision (user direction 2026-07-03 — design round pending)

Mike's bar: **fully immersive** — the career mode gets its own windows, tabs, and minigames;
"all the management content we can imagine and create together." This document seeds the
design round; nothing here is locked yet except the direction.

## Principles (inherit from PLAN pillars)

- The sim never decides on-track outcomes; management depth shapes INPUTS (car, team, money,
  morale) and consumes OUTPUTS (results, reputation, income).
- Everything deterministic + journaled — every minigame outcome replayable from the seed.
- Era-flavored presentation (telegram/fax/email per decade; period typography and tone).
- Depth is opt-in: the minimal loop (brief → race → result) never gets buried.

## Candidate hub tabs (v-next skeleton, Driver Career first)

| Tab | Content | Phase |
|---|---|---|
| **HQ / Home** | Season header, next round, news feed from the journal, standings ticker | now → polish |
| **Contracts** | Offer letters, negotiation minigame (counter-offers priced by rep/OPI), multi-year terms, release clauses | 2 |
| **Team** | Constructor lineage + HISTORICAL NAMES, teammate comparison, car rating/scalars, reliability trend | 2 |
| **Finances** | Ledger (Phase-2 economy): salary, sponsors with health decay, prize fund, crash bills | 2 |
| **Rivals** | Rivalry tracking, head-to-head records, seeded needle headlines | 3 |
| **History** | Career scrapbook: season reviews, records, title permutations, journal "why?" inspector | 2 |
| **Paddock** | Rumor mill, driver market, retirement watch (living-world dial) | 2–3 |

## Minigame candidates (each deterministic, each skippable)

- **Contract negotiation**: turn-based counter-offers against archetype-weighted patience.
- **Sponsor pitch**: pick the pitch angle vs sponsor personality (era-flavored brands).
- **Setup gamble** (pre-race): risk/safe setup choice nudging the AI spread ± via scalars.
- **Media moments**: post-race quote choices moving reputation/rivalries/sponsor health.
- **Development allocation** (Owner-Driver, Phase 3): budget split with diminishing returns.

## Constructor naming (near-term commitment)

Constructors standings display the pack's authored historical team names everywhere
(teams.json `name` — "Brabham-Repco", "Lotus-Ford Cosworth"), never raw ids; community pack
authors name their own. Oracle-side chassis+engine identity stays the scoring key.

## Next step

A dedicated design round (judge-panel workflow: 3 independent hub designs → scored →
synthesized) once the F1 pack fleet lands, so the hub is designed against real multi-era
careers rather than a single season.
