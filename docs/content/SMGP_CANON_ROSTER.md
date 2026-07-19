# SMGP Canonical Roster (SMGP-024)

The SMGP universe contains 24 canonical teams across 17 seasons. Each team retains
the same official team name, car name, and engine name throughout all 17 seasons.
Seasonal performance, personnel, development, and stories may change, but the
canonical identity strings do not.

Machine-readable source of truth: `data/rules/smgp/canon.json` (`smgp-24-v1`).
Executable lock: `tests/Companion.Tests/Smgp/SmgpCanonLockTests.cs` (all 408
team-season identity combinations asserted). Runtime model:
`src/Companion.Core/Smgp/SmgpCanon.cs`. Identity flows: canon registry ->
`CareerRulesData` -> car-spec overlay (dossier MACHINE blocks), paddock machine
dossier, validation. No other file may maintain a team/car/engine list.

## The 24

| # | Team | Permanent car | Permanent engine |
|---|---|---|---|
| 1 | MADONNA | MADONNA 456 | PALM 190 V10 |
| 2 | FIRENZE | FIRENZE 500 | FIRENZE 99 V12 |
| 3 | MILLIONS | MILLIONS 189 | DICK MD V10 |
| 4 | BESTOWAL | BESTOWAL 167 | VAPOR DN |
| 5 | BLANCHE | BLANCHE 582 | DELTA 103 V10 |
| 6 | TYRANT | TYRANT 548 | LIZZIE 24 V8 |
| 7 | LOSEL | LOSEL 125 | VAPOR DNPQ V8 |
| 8 | MAY | MAY 555 | LORRY 32 V8 |
| 9 | BULLETS | BULLETS 560 | LIZZIE 24 V8 |
| 10 | DARDAN | DARDAN 700 | VAPOR DNPQ V8 |
| 11 | LINDEN | LINDEN LN198 | LORRY 32 V8 |
| 12 | MINARAE | MINARAE 594 | SEGA SG1000 V8 |
| 13 | RIGEL | RIGEL 3000 | LORRY 32 V8 |
| 14 | COMET | COMET 323 | LIZZIE 24 V8 |
| 15 | ORCHIS | ORCHIS 056 | MISFIRE 50 V8 |
| 16 | ZEROFORCE | ZEROFORCE 231 | LORRY 32 V8 |
| 17 | JOKE | JOKE 777 | POND V8 |
| 18 | LARES | LARES 92 | RAM V12 |
| 19 | FEET | FEET 13 | YOUGEN V10 |
| 20 | SERGA | SERGA 1000 | SC3000 F12 |
| 21 | COOL | COOL 05 | CORSE V8 |
| 22 | MOON | MOON 292 | RAM V12 |
| 23 | IRIS | IRIS 717 | PRISM 90 V10 |
| 24 | AZALEA | AZALEA 808 | BLOOM 88 V8 |

Roster arithmetic: the 22 SEGA-base teams (SMGP1's sixteen plus SMGP II's Joke,
Lares, Feet, Serga, Cool, Moon) plus Iris and Azalea, the two Kobra Fleetworks
LEVEL A entries, bound to the mclaren_mp45b mod. 22 + 2 = 24. Older notes that
say "22 teams" describe the SEGA base only; `packs/smgp-1/pack.json` keeps its
original wording because the pinned pack blob is hash-verified and must never
be rewritten (see `docs/migrations/SMGP_024_ALIAS_MIGRATION_NOTES.md`).

## Shared engines

One engine dossier per specification; the teams remain separate houses.

- LIZZIE 24 V8: TYRANT, BULLETS, COMET
- VAPOR DNPQ V8: LOSEL, DARDAN
- LORRY 32 V8: MAY, LINDEN, RIGEL, ZEROFORCE
- RAM V12: LARES, MOON

VAPOR DN (BESTOWAL) is a distinct specification from VAPOR DNPQ V8; its
architecture is deliberately unpublished in-world.

## Exactness law

ORCHIS 056 and COOL 05 keep leading zeros. SC3000 F12 is not SC3000 V12. SERGA
is not SEGA; MINARAE runs the SEGA SG1000 V8. DICK MD V10, MISFIRE 50 V8, POND
V8, YOUGEN V10, LINDEN LN198, RIGEL 3000, ZEROFORCE 231 are intentional canon.
No car or engine ever gains a year suffix, a B/C/Evo/Mk II/Spec 2/Gen 2/Turbo
increment, an architecture change, or a new supplier. Seasonal prose describes
development packages on the permanent names; the championship registers each
model name as an enduring program identity.

## Aliases

Aliases normalize on load/import/migration/search and are never displayed:
`lotus` -> IRIS (SMGP scope only; real-world Lotus data is never touched);
AZELIA, AZALIA, AZALEAH, TEAM AZALEA, AZALEA RACING, AZALEA MOTORSPORT -> AZALEA.
Primitive: `SmgpCanon.NormalizeTeamName`.

## Lore layers

- Team dossiers: `data/rules/smgp/team-profiles.json` (origin framing, era-safe).
- Car dossiers: `data/rules/smgp/car-dossiers.json` (24).
- Engine dossiers: `data/rules/smgp/engine-dossiers.json` (17).
- Team-season capsules: `data/rules/smgp/capsules/sNN.json` (408, the base
  universe's own arc; no player, no declared champions).
- Season identities: `data/rules/smgp/seasons.json` (17; season-opening canon).
- Driver lore: `data/rules/smgp/driver-profiles.json` (origin-framed for the
  winter reshuffle), `rival-quotes.json`, `driver-stats.json`.
