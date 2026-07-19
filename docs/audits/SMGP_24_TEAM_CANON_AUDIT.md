# SMGP-024 Initial Canon Audit

Mission: SMGP-024, complete 24-team canon lock + driver-swap alignment.
Date: 2026-07-18. Status: IN PROGRESS (inventory lanes running).

This audit is the mission's required starting inventory (brief §10). It records the
authoritative canon, the taxonomy used to classify every finding, and the complete
inventory of identity sources, consumers, and contamination points discovered in the
repository. Implementation follows this document; the completion status column is
kept current as lanes finish.

## 1. Authoritative canon (mission-supplied source of truth)

24 teams x 17 seasons = 408 team-season identity combinations. Team name, car name,
and engine name are permanent across all 17 seasons. Only the season/year changes.

| # | Team | Permanent car | Permanent engine | Engine shared with |
|---|---|---|---|---|
| 1 | MADONNA | MADONNA 456 | PALM 190 V10 | |
| 2 | FIRENZE | FIRENZE 500 | FIRENZE 99 V12 | |
| 3 | MILLIONS | MILLIONS 189 | DICK MD V10 | |
| 4 | BESTOWAL | BESTOWAL 167 | VAPOR DN | |
| 5 | BLANCHE | BLANCHE 582 | DELTA 103 V10 | |
| 6 | TYRANT | TYRANT 548 | LIZZIE 24 V8 | BULLETS, COMET |
| 7 | LOSEL | LOSEL 125 | VAPOR DNPQ V8 | DARDAN |
| 8 | MAY | MAY 555 | LORRY 32 V8 | LINDEN, RIGEL, ZEROFORCE |
| 9 | BULLETS | BULLETS 560 | LIZZIE 24 V8 | TYRANT, COMET |
| 10 | DARDAN | DARDAN 700 | VAPOR DNPQ V8 | LOSEL |
| 11 | LINDEN | LINDEN LN198 | LORRY 32 V8 | MAY, RIGEL, ZEROFORCE |
| 12 | MINARAE | MINARAE 594 | SEGA SG1000 V8 | |
| 13 | RIGEL | RIGEL 3000 | LORRY 32 V8 | MAY, LINDEN, ZEROFORCE |
| 14 | COMET | COMET 323 | LIZZIE 24 V8 | TYRANT, BULLETS |
| 15 | ORCHIS | ORCHIS 056 | MISFIRE 50 V8 | |
| 16 | ZEROFORCE | ZEROFORCE 231 | LORRY 32 V8 | MAY, LINDEN, RIGEL |
| 17 | JOKE | JOKE 777 | POND V8 | |
| 18 | LARES | LARES 92 | RAM V12 | MOON |
| 19 | FEET | FEET 13 | YOUGEN V10 | |
| 20 | SERGA | SERGA 1000 | SC3000 F12 | |
| 21 | COOL | COOL 05 | CORSE V8 | |
| 22 | MOON | MOON 292 | RAM V12 | LARES |
| 23 | IRIS | IRIS 717 | PRISM 90 V10 | |
| 24 | AZALEA | AZALEA 808 | BLOOM 88 V8 | |

Exactness rules (mission §5): ORCHIS 056 and COOL 05 keep leading zeros; VAPOR DN
carries no architecture suffix; SC3000 F12 is not SC3000 V12; DICK MD V10, MISFIRE
50 V8, POND V8, YOUGEN V10, LINDEN LN198, RIGEL 3000, SERGA 1000, ZEROFORCE 231,
SEGA SG1000 V8 are intentional canon, never normalized.

Alias policy: LOTUS -> IRIS only where an SMGP-scoped legacy record clearly means
IRIS (never real-world Lotus data, never the packs/f1-* historical packs). AZELIA,
AZALIA, AZALEAH, TEAM AZALEA, AZALEA RACING, AZALEA MOTORSPORT -> AZALEA on load,
import, migration, search, and repair. Aliases are never displayed as canon.

## 2. Finding taxonomy

Severity classes used in the inventory tables:

- C1 Critical canon violation (wrong/duplicated/missing canonical identity)
- C2 Persistence risk (display names or stale identities stored where ids belong)
- C3 Cross-mode leak (SMGP/Dynasty/Racing Passport content crossing)
- C4 Incorrect visible text (lore contradicting current seats or canon)
- C5 Missing content (dossier/capsule/season identity absent or placeholder)
- C6 Duplicate source of truth (same identity maintained in two+ places)
- C7 Test-only issue
- C8 Documentation issue

Completion states: Open / In lane / Fixed / Verified (test pinned).

## 3. Driver-swap alignment rule (the mission's triggering bug)

The winter standings reshuffle (SmgpGridReshuffle.ForNextSeason, shipped design per
docs/dev/smgp-design.md) moves drivers between physical cars for season 2+. Every
driver-facing surface must therefore resolve team identity from the CURRENT seat:

- Paddock cards: team label, team accent, car art (car art fixed 2026-07-18,
  a52e6b9), bio/epithet/quotes prose.
- News: article team references for a moved driver must use the current team.
- History: per-season records persist the team the driver raced THAT season;
  career pages show the current team as current.
- Events/story threads: rivalries and team references resolve against current seats.
- Season lore: opening canon describes season 1 seats; season 2+ prose generated
  from state must not assert season-1 seats as current fact.

## 4. Inventory

Filled from the four audit lanes (data files, code consumers, lore content,
docs/tests). Tables use: Location | System | Current value | Correct canonical
value | Problem type | Severity | Required action | Migration risk | Status.

PENDING: lane results.
