# 1969 Custom-AI source parity audit

Audit date: 2026-07-11

## Source and grain

The authoritative community source is the installed jusk file:

`Y:\SteamLibrary\steamapps\common\Automobilista 2\UserData\CustomAIDrivers\F-Vintage_Gen2.xml`

- Header: `Custom AI by jusk - F1 1969 Season`, changelog v0.1.
- SHA-256: `E52C3C35FA237A80013C520E93B27C3B18BAE4BE888D2BE5C93A9DF0E9AAE341`.
- 26 base livery blocks, each with 13 authored driver-rating fields and one
  `vehicle_reliability` value; no weight, power, or drag scalars.
- Three track blocks: Beltoise at Monaco, Surtees at Monaco/Silverstone, and the complete
  Ferrari #11 Amon-to-Brambilla Monza name/rating/reliability swap.
- Canonical SHA-256 of the 26 sorted base-driver rating vectors:
  `9F3F28B7755BFC431F84235E673DF2DA44C6598EC6E933D80A96327ADF31C878`.

The source's `_05Full` file is byte-identical to the base XML. The other variants are
rating-identical roster subsets; they add no rating evidence and remain documentation-only.

| installed variant | SHA-256 | livery blocks |
|---|---|---:|
| `F-Vintage_Gen2_01Kyalami.xml` | `27D1D4F519780A57ABC9291E0D8C381EEFB95EEEFD9BB23D110CBFC09BA6F0A9` | 19 |
| `F-Vintage_Gen2_02Silverstone.xml` | `12338B77BCCE66B092C9D38E2C7F62F962008354CA0032DA1613516BF8FB3E56` | 19 |
| `F-Vintage_Gen2_03Nordschleiffe.xml` | `D2F210DD17F05B0A0DEE5E1F5F4C74C7535061E69FBA729A15E1FA9F0383CAA8` | 22 |
| `F-Vintage_Gen2_04WatkinsGlen.xml` | `B44556C51F22203902BE7D3E9A54C2BADE05692746F3EAFA9EF919C205A6B848` | 20 |
| `F-Vintage_Gen2_05Full.xml` | `E52C3C35FA237A80013C520E93B27C3B18BAE4BE888D2BE5C93A9DF0E9AAE341` | 26 |
| `F-Vintage_Gen2_06RegularF1.xml` | `3A90FA700DF4ECDB6E9A07A914FEF432CE7719C51B6CD17E452FEE36859B0C37` | 18 |

## Installed skin bindings

The 26 source livery names also appear verbatim and without duplication across the installed
CustomLiveries overrides. Their 4 + 8 + 10 + 4 union is exactly the pack's 26 distinct
`ams2LiveryName` values, and each owning vehicle folder is allowed by its entry's team.

| vehicle override | SHA-256 | matching names |
|---|---|---:|
| `brabham_bt26/brabham_bt26.xml` | `7A2BE92C8CDF9D6966223D31D227FEAD7B5E4F1B592DE712C64E2E56CAD21D28` | 4 |
| `formula_vintage_g2m1/formula_vintage_g2m1.xml` | `5F49C704F09D0DE33A08188FB532CE881141485A828B30AF775610E18D8FFA70` | 8 |
| `formula_vintage_g2m2/formula_vintage_g2m2.xml` | `E4C40CDEFCA07D9BE6FCEB61E6CC280D385ED2A87BC3293E7AECE816C868F14C` | 10 |
| `lotus_49c/lotus_49c.xml` | `47737963905AE2CA31D74E0C8C19718FE880B6B50F3A782B559474FDF79AA33A` | 4 |

## Findings and remediation

The original pack imported complete source ratings for only 14 drivers. Ten matching optional
drivers retained f1db pace only, Piers Courage and Richard Attwood had no pack entries despite
their dedicated source liveries, and every source reliability value was absent. The Monza block
was also reduced to a ratings patch on Chris Amon even though the source explicitly renames the
slot to Ernesto "Tino" Brambilla.

The parity pass now carries:

- all 26 base source liveries and their complete 13-field rating/reliability blocks;
- explicit Piers Courage #32 and Richard Attwood #29 entries in the full-base roster;
- an explicit round-eight Brambilla entry with the complete Monza source block, while Amon owns
  the same #11 livery outside that round;
- Bill Brack as the sole f1db-pace proxy, using the source Eaton #22 livery reliability; and
- the Monaco and Silverstone rating patches exactly as authored.

That produces 28 pack drivers, 29 entry stints, 26 unique source liveries, and 28 staged car
blocks. The 26-slot full-base policy is playability-oriented: Piers appears in the regular source
subsets, while Attwood appears only in the Nürburgring subset but remains available throughout a
full-base career. Monaco exposes 26 candidates but its 25-car track cap still governs the staged
field.

The historical result records Brambilla as DNS because Pedro Rodríguez raced the Ferrari. The
round-eight slot therefore reproduces the community source's full-grid driver swap; it is not a
claim that Brambilla started the 1969 Italian Grand Prix. The source interpretation was
cross-checked against the f1db-derived `data/history/1969.json` record and
<https://www.oldracingcars.com/f1/results/1969/italy/>.

## History and circuit audit

`data/history/1969.json` has all 11 championship rounds, correct champions, result tables, facts,
and parseable circuit-map payloads. Every round has at least one era-capped fact. Mosport and
Mexico now use their 1969 names (`Mosport Park`, `Autódromo Ricardo Rodríguez`) rather than later
commercial/post-1971 names. The already-completed weather research remains unchanged: all 11
rounds are dry.

## Regeneration warning

A plain rerun of `tools/import_jusk_ai.cs` is not the whole regeneration path. The importer binds
a duplicated livery to its first entry: Ferrari #11 therefore maps to Amon, and BRM #22 maps to
Eaton. Run grid regeneration after any entry-range change, then preserve the explicit Brambilla
swap and Bill Brack reliability proxy. The pack note and this audit record the external source
hash; `F11969SourceParityTests` pins the derived canonical rating hash, full-rating split, livery
set, reliability per starter, track patches, swaps, exits, and Monaco cap.
