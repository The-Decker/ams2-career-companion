# 1983 pack and Custom-AI source parity audit

Audit date: 2026-07-11

## Source and grain

The authoritative community artifact is the local TAMS2SP release archive:

`Z:\SKINS 4 AI MUCH LOVE\F-Retro-Gen3_TAMS2SP_1983_Skinpack_V2-2.zip`

- Archive SHA-256:
  `C893BD32E7163C0F5853E97A6D2FB18EC6585C4B75B3A45CBD77065E02819FA2`.
- Embedded `F-Retro_Gen3.xml` SHA-256:
  `542B1830ACF9BCA2777B2BA2D9A65187063E6248BA2977C88CD099CC0263F30E`.
- The embedded XML is byte-identical to the installed
  `UserData\CustomAIDrivers\F-Retro_Gen3.xml`.
- The archive readme credits Humpty (mungopark) for the AI balance. It contains 26 base profiles,
  each with all 13 supported driver-rating fields and `vehicle_reliability`; there are no
  per-track overrides or performance scalars.
- Canonical SHA-256 of the 24 sorted, active-pointer pack rating vectors:
  `C7D0A3B97E7033677261A975D1B107F9CA840E2BE423E193C4AFE332AEB6AF82`.
- Canonical SHA-256 of their 24 sorted reliability values:
  `2C252A28C17A2F857DA1D6F5105B4E81685EDDD8756ABBC4A8F56E7CFF2EE9CD`.

`tools/import_jusk_ai.cs` matched 24/24 authored pack liveries with no rating drift. The two
source-only profiles are Theodore-Ford #33 Roberto Guerrero and Toleman-Hart #36 Bruno
Giacomelli. Both have optional textures in the archive, but neither is part of the 24-livery
active pointer set, so neither can be staged alongside the committed set.

## Skin bindings

The four committed pointer files expose exactly 24 unique livery names. Their union equals the
pack's 24 `ams2LiveryName` values, and every livery belongs to a vehicle allowed by its team.

| committed pointer | SHA-256 | active names |
|---|---|---:|
| `brabham_bt52.xml` | `7A37026B8146621D117976B2C4EB371D45CEA8F5998469EED57895388C7CF63F` | 2 |
| `formula_retro_g3.xml` | `479D60633BCEF20D3DAB657BF505672AFBFF3F165513B618E9A013EC2F410C5E` | 10 |
| `formula_retro_g3_te.xml` | `C7D0CC765D0884C696ECCD10B4B023DAB4C489CE610B32E58E922A59B8FE5850` | 10 |
| `mclaren_mp4_1c.xml` | `C6EE109C2C41BD65CD07C25DE027A99A75A9EEC5DF1CCE4729CD0A75D8779427` | 2 |

All 120 referenced preview/body/helmet/visor texture paths exist in the installed TAMS2SP
folders. The committed naturally aspirated pointer deliberately corrects one source typo:
`83_Jarrier_visor_spec.dds` becomes the real asset name `83_Jarier_visor_spec.dds`.

## Roster and calendar policy

The source-backed grid contains 21 seats active for rounds 1-15 plus Johnny Cecotto for 1-13,
Thierry Boutsen for 6-15, and Stefan Johansson for 9-14. The resulting grids grow from 22 to 24
and contract again as those represented activity windows change; every grid is within the
24-livery class cap.

Other f1db entrants without an active profile/pointer intersection remain explicit omissions.
This is a semi-historical playable roster, not a claim to reproduce every entry or non-qualifier.
The full historical classifications remain in `data/history/1983.json`.

All 15 dates and distances were generated from f1db and cross-checked against the official
results archive: <https://www.formula1.com/en/results/1983/races>. Three unavailable venues use
distance-preserving primaries:

- Paul Ricard -> Le Mans Bugatti, with Mugello (`florence_gp`) as a 60-lap fictional alternate;
- Detroit -> Adelaide Historic, retained as the legitimate street-circuit no-alternate gap; and
- Zandvoort -> Spielberg Vintage, with Zolder (`Heusden`) as a 76-lap fictional alternate.

The pack enables race refuelling. Contemporary 1983 reporting documents refuelling strategy,
while the 1984 regulations prohibited in-race refuelling:
<https://www.motorsportmagazine.com/archive/article/may-1983/30/french-grand-prix-15/> and
<https://www.motorsportmagazine.com/archive/article/may-1984/82/brazilian-grand-prix-8/>.

## Weather

All 15 weekends were researched. Monaco is the only wet/drying race; Monaco, Belgium, Detroit,
and Germany carry wet qualifying composites. The evidence and exact AMS2 slot decisions are in
`docs/dev/wet-weather-research.md`.

## History and circuit audit

`data/history/1983.json` has all 15 championship rounds, correct champions and classifications,
at least one era-capped fact per circuit, and a parseable circuit-map payload for every layout.
Three f1db present-day labels were restored to their 1983 names: `Jacarepaguá`, `Autodromo Dino
Ferrari`, and `Österreichring`. No fact or circuit history references a result after 1982.

## Regeneration warning

Generate the f1db skeleton, retain only the 24 active-pointer entries, then run
`tools/import_jusk_ai.cs`, `tools/author_weekend.cs --refuel`, the researched weather authoring
pass, and `tools/max_grid.cs`. Preserve the Jarier texture correction, the three limited entry
ranges, the two fictional alternates, and the documented source-only #33/#36 policy.

`F11983SourceParityTests` pins the pointer/entry union, rating and reliability hashes, source-only
omissions, active ranges, model ownership, refuelling, weather, alternates, and the corrected
texture path.
