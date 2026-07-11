# 2010 pack and Custom-AI source parity audit

Audit date: 2026-07-11

## Source and provenance

The authoritative community artifact is the local pristine release archive:

`Z:\SKINS 4 AI MUCH LOVE\F1_2010HC_260627.rar`

- Release page: <https://www.overtake.gg/downloads/f1-2010-skinpack-for-formula-v8-gen-3.81401/>.
- Author: LadyCroussette; the release credits AFry for Custom-AI/selector data and mungopark for
  testing and fixes.
- Archive size: 251,898,510 bytes; SHA-256
  `4CBA68519B37A97677D2B23A0E800E1B12CACB1AF25DF5B3B5840BAB43A835FC`.
- Realistic `F-Reiza.xml` SHA-256:
  `DBD7C5953C72F39D3B41C8022FF2F7599F3F349C4C3D4260750F38720ECB8BBE`.
- Equal `F-Reiza.xml` SHA-256:
  `9EB8D55F593F7ECD3017674402E1E81D55BB1F245BCAB157BB936AEFA110CCEA`.
- Generic opening pointer SHA-256:
  `4483C08D566A00473FEC5374102F79EF4E0D25C95C242FAC68A0CB6A4E197FE8`.
- Selector batch SHA-256:
  `A34A19149D2E4F4E9DCC08BFE4EEA2E0E119BD2FFF2994F84BFCA5FF41B9E3BA`.

The installed `UserData\CustomAIDrivers\F-Reiza.xml` is byte-identical to the archived Realistic
source. This is a genuine upstream source, not an AMS2 Career Companion staged output.

## Roster and selector policy

The first 28 base profiles in the source represent all 27 drivers who started a 2010 Grand Prix.
The remaining nine profiles are fictional what-if transfers and are excluded. The historical
roster therefore has 12 teams, 27 drivers, and 28 livery entries:

- Sakon Yamamoto uses Hispania #21 at R10 and #20 at R11-R14 and R16-R17;
- Karun Chandhok runs R1-R10;
- Bruno Senna runs R1-R9 and R11-R19;
- Christian Klien runs R15 and R18-R19;
- Pedro de la Rosa runs R1-R14 and Nick Heidfeld R15-R19; and
- the other 18 regular entries run R1-R19.

The five real DNS events remain absent from the starter list rather than being backfilled: Jarno
Trulli at R2, Pedro de la Rosa at R3, Timo Glock at R4, Heikki Kovalainen at R5, and Lucas di
Grassi at R16. Those rounds field 23 starters; every other round fields the Formula Reiza cap of
24.

The selector is a monotonic change-point set, so it is compatible with the existing binder:

| anchor | active rounds |
|---|---|
| `01BHR` | R1 |
| `02AUS` | R2 |
| `03MYS` | R3 |
| `04CHN` | R4 |
| `05ESP` | R5-R6 |
| `07TUR` | R7 |
| `08CAN` | R8-R9 |
| `10GBR` | R10 |
| `11DEU` | R11-R12 |
| `13BEL` | R13 |
| `14ITA` | R14 |
| `15SGP` | R15 |
| `16JPN` | R16-R17 |
| `18BRA` | R18 |
| `19UAE` | R19 |

The four `WIF` variants never anchor.

## Custom-AI parity

Every historical driver carries all 14 source ratings plus all four source car fields
(`weight_scalar`, `power_scalar`, `drag_scalar`, and `vehicle_reliability`). Canonical hashes over
driver-id-sorted pack data are:

- ratings: `4C8FFAAFEFEF7BAE9C6F682422FF36AC4D9D480A2B1CFD36D162F414366586D8`;
- car fields: `6FDC0D785B3FEB9B653783FF69E6A4D18928CA8D275A90C1CBA09CF013A6BB42`.

The source has 432 track blocks across 13 venue groups: Sakhir, Melbourne, Barcelona, Monaco,
Montreal, Silverstone, Hockenheim, Hungaroring, Spa, Monza, Kansai, Interlagos, and Yas Marina.
After entry-window and starter filtering, the pack retains 309 driver patches carrying 1,674
fields at R1, R2, R5, R6, R8, R10-R14, R16, R18, and R19. Their canonical SHA-256 is
`847F35102AF89F98852844F4189FF35976A4A6D43B2C38224F9F1EA634E31577`.

Yamamoto needs an explicit identity policy because two liveries bind one historical driver. His
global base is the longer-lived #20 profile. R10 starts from the complete #21 base and car block,
then overlays the #21 Silverstone patch, exactly reproducing the source profile for that race.

Three importer traps were handled in the one-off generation pass:

1. explicit `SAKHIR`, `MELBOURNE`, and `YAS MARINA` aliases preserve source blocks where the pack
   drives placeholder tracks;
2. `modern` is a non-identity token, preventing `Montreal_Modern` from falsely matching
   `adelaide_modern`; and
3. same-driver/same-round blocks merge field-by-field rather than replacing an earlier partial
   block.

The shipped generic importer remains untouched; `F12010SourceParityTests` pins the resulting
source contract.

## Skin binding and upstream defect

`data/ams2/skin-seasons/f1-2010/formula_reiza.xml` is the archive's generic 24-livery opening
pointer with seven mixed-indent lines normalized; its committed SHA-256 is
`19CB989ED8D293A8197255F16EF9D4CE62305181A792EE70621F12A58E7C81AD`. It is the only repo-owned
model pointer for this season. The previously captured
`mercedes_amg_sc.xml` was excluded because it points to 2012/2024 safety-car assets and is not
2010 Formula content.

Race variants stay beside the installed Formula Reiza override, where the existing binder
discovers them. They must not be copied into the repo's skin-season directory: the loader treats
every XML basename there as a vehicle model.

The upstream R15, R16, R18, and R19 variants each refer to the absent path
`F1_Season_2010\Helmet\Sauber_visor_Heidfeld.dds`. The installed archive instead contains
`Sauber_Heidfeld.dds` and `Sauber_visor.dds`; the intended visor reference is
`Sauber_visor.dds`. The repo-owned generic pointer is internally complete, but automatic repair
of installed external variants would require shared skin-staging work and is deliberately not
implemented in this data-only lane.

## Calendar, tracks, and history

All 19 dates, entry windows, starters, and real distances derive from f1db and were cross-checked
against the [official 2010 results archive](https://www.formula1.com/en/results/2010/races).
Bahrain used the one-off 6.299 km Endurance Circuit, also documented by
[Formula 1](https://www.formula1.com/en/racing/2020/bahrain).

Nine unavailable venues use distance-preserving base/DLC stand-ins: Bahrain, Albert Park,
Sepang, Shanghai, Istanbul, Valencia, Marina Bay, Korea, and Yas Marina. Seven have optional
fictional mod alternates. Valencia and Marina Bay remain honest no-alternate street-circuit gaps.
Every primary/fallback supports the 24-car field.

`data/history/2010.json` covers all 19 rounds and has a parseable circuit-map payload for every
layout. Five present-day/generic labels were restored to season-appropriate names: Bahrain
International Circuit, Endurance Circuit; Albert Park Grand Prix Circuit; Circuit de Catalunya;
Suzuka International Racing Course; and Korea International Circuit. Circuit prose and facts are
era-capped and do not leak post-2010 results.

## Weather and regeneration warning

Seven weekends have researched authored weather: wet races at Australia, China, Turkey, Belgium,
and Korea, plus wet qualifying at Malaysia, Belgium, and Brazil. The exact slot sequences and
evidence are in `docs/dev/wet-weather-research.md`. Refuelling is disabled, matching the 2010
regulations.

Regeneration must start from the pristine archive and a temporary PackGen/import pass. Include
f1db entrants with non-empty round lists even when f1db marks them as test drivers (Yamamoto and
Klien), split Yamamoto into the two selector liveries, filter the five DNS events, preserve the
three explicit venue aliases and merge semantics above, then run weekend/weather/alternate
authoring. Do not import from an app-staged `F-Reiza.xml`, do not add race variants to the
skin-season library, and do not restore the unrelated safety-car pointer.
