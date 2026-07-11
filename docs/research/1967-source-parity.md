# 1967 Custom-AI source parity audit

Audit date: 2026-07-11

## Source and scope

The authoritative community source is the installed jusk file:

`Y:\SteamLibrary\steamapps\common\Automobilista 2\UserData\CustomAIDrivers\F-Vintage_Gen1.xml`

- Header: `Custom AI by jusk - F1 1967 Season`, changelog v0.3.
- SHA-256: `8B0D2836431F8F40EC1865A4531FCE565A7E7F24DD988DC445118E023CEB0879`.
- 20 base livery blocks, each with 13 authored driver-rating fields and one
  `vehicle_reliability` value.
- Canonical SHA-256 of the 20 sorted pack driver-rating vectors:
  `9B2F28690933CA5631BA1AD0E620081E8A3A4B933B16AF3DEA455DC3EF252E2D`.
- 21 venue-specific rating blocks resolving to six rounds in this pack.
- No `weight_scalar`, `power_scalar`, `drag_scalar`, or `fuel_management` values are authored.

The v1.2 rating import (`c798260`) happened before the repository gained per-driver car-block
support (`7018960`), so the source reliability values were never copied into `drivers.json`.
The later max-grid pass (`c5fb87a`) also made Ludovico Scarfiotti a Monza starter after his
source-authored Monza patch had previously been filtered as an orphan.

## Verified livery mapping

`vehicle_reliability` is authored by AMS2 livery. The pack changes the displayed driver in an
available livery slot when the historical roster requires a substitute, so the livery value must
follow every occupant of that slot.

| source livery | reliability | pack occupant(s) |
|---|---:|---|
| Brabham-Repco #1 J. Brabham | 0.93 | Jack Brabham (R1-11) |
| Brabham-Repco #2 D. Hulme | 0.96 | Denny Hulme (R1-11) |
| Lotus-Ford Cosworth #5 J. Clark | 0.51 | Jim Clark (R1-11) |
| Lotus-Ford Cosworth #6 G. Hill | 0.36 | Graham Hill (R1-11) |
| Cooper-Maserati #12 J. Siffert | 0.43 | Jo Siffert (R1-11) |
| McLaren-BRM #14 B. McLaren | 0.33 | Bruce McLaren (R1-11) |
| Brabham-Repco #15 G. Ligier | 0.70 | Bob Anderson (R1-5); Guy Ligier (R6-11) |
| BRM #3 J. Stewart | 0.29 | Jackie Stewart (R1-11) |
| BRM #4 M. Spence | 0.50 | Mike Spence (R1-11) |
| Honda #7 J. Surtees | 0.54 | John Surtees (R1-11) |
| Lola-BMW #17 H. Hahne | 0.19 | Hubert Hahne (R1-11) |
| Matra-Ford Cosworth #20 J-P. Beltoise | 0.38 | Jean-Pierre Beltoise (R1-6, R8-11); Jo Schlesser (R7) |
| Matra-Ford Cosworth #29 J. Ickx | 0.39 | Jacky Ickx (R1, R3-11); Johnny Servoz-Gavin (R2) |
| Ferrari #8 C. Amon | 0.93 | Chris Amon (R1-11) |
| Ferrari #18 L. Bandini | 0.89 | Lorenzo Bandini (R1-2); Mike Parkes (R3-4); Jonathan Williams (R11) |
| Ferrari #19 L. Scarfiotti | 0.90 | Ludovico Scarfiotti (R1-11) |
| Cooper-Maserati #30 J. Rindt | 0.24 | Jochen Rindt (R1-10); Jo Bonnier (R11) |
| Cooper-Maserati #11 P. Rodriguez | 0.81 | Pedro Rodriguez (R1-7, R11); Richard Attwood (R8); Jo Bonnier (R9-10) |
| Eagle-Climax #10 D. Gurney | 0.25 | Dan Gurney (R1-11) |
| Eagle-Climax #22 R. Ginther | 0.20 | Richie Ginther (R1-2); Al Pease (R8) |

Jo Bonnier is the only driver who occupies two source liveries. His base car block therefore uses
the #11 value (0.81), with a round-11 `vehicleReliability: 0.24` override for the #30 livery.

## Rating decisions

- The 20 drivers named by the XML retain its complete 13-field base rating blocks.
- The eight max-grid proxy drivers (Anderson, Parkes, Williams, Attwood, Bonnier, Pease,
  Schlesser, Servoz-Gavin) retain only their f1db-derived race and qualifying pace. No behavioural
  traits were invented for them.
- Scarfiotti receives the XML's Monza patch (`raceSkill: 0.55`, `qualifyingSkill: 0.56`) now that
  he is present in the round-nine grid.
- The reliability additions are staged Custom-AI data. They do not change the deterministic
  career simulation's team-reliability model, and existing careers retain their pinned pack bytes.

`F11967SourceParityTests` pins the complete-rating/proxy split, every active livery's reliability,
the Bonnier livery switch, Scarfiotti's Monza patch, and the Bandini/Parkes/Ginther exit boundaries.

## Regeneration warning

Do not treat a plain rerun of `tools/import_jusk_ai.cs` as a complete regeneration path for this
expanded roster. The importer resolves a duplicated livery to the first matching pack entry, so
the shared Brabham #15 slot would bind Guy Ligier's source block to Bob Anderson. Rebuilding the
round overrides would also drop Bonnier's manual round-11 livery-reliability switch. The parity
tests deliberately pin both cases; preserve them in any future importer work.
