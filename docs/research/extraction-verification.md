# Extraction Verification — data/ams2/*.json vs disk + RESEARCH.md

Adversarial verification pass, 2026-07-02. Ground truth = `Y:\SteamLibrary\steamapps\common\Automobilista 2`
(`Vehicles\**\*.crd`, `Tracks\<id>\<id>.trd`, `UserData\CustomAIDrivers\*.xml`), AMS2 build 23820506.
Verifies `data/ams2/{classes,vehicles,tracks,liveries}.json` against RESEARCH.md sections 1–2 and
`local-install-inventory.md`.

**Overall verdict: NOT OK.** Tracks, liveries and encoding are solid; the F1-ladder class names are now
ground-truthed (several RESEARCH.md rename assumptions are wrong); but **vehicles.json / classes.json have a
material extraction gap: 34 player-selectable cars (entire CART F-USA grids among them) are missing** because
the extractor only reads the dir-named `.crd` per vehicle folder.

---

## Check 1 — F1 ladder class names in classes.json (v1.6.9 rename ground truth)

**PASS (all ladder classes present) with 4 discrepancy groups vs RESEARCH.md.** Every name below was verified
case-sensitively against classes.json AND against the `Vehicle Class` attribute inside a representative `.crd`
on disk (extraction is faithful to the crds).

| RESEARCH.md assumption | ACTUAL xml class name(s) in game data | Verdict |
|---|---|---|
| F-Vintage Gen1 / Gen2 | `F-Vintage_Gen1`, `F-Vintage_Gen2` | exact match |
| F-Retro Gen1/2/3 | `F-Retro_Gen1`, `F-Retro_Gen2`, `F-Retro_Gen3` | exact match |
| F-Classic Gen1–4 | `F-Classic_Gen1` … `F-Classic_Gen4` | exact match |
| F-HiTech Gen1/Gen2 | **`F-Hitech_Gen1`, `F-Hitech_Gen2`** (lowercase `t`) | **casing differs — user files were right** |
| Formula Edge (ex F-V12) | **`FE-G1`** (formula_edge_g1m1/2/3, 1995) — and **`F-V12` still exists** as its own class (formula_v12, 1995) | **name wrong AND it is not a rename: both classes coexist** |
| F-V10 Gen1/2/3 | `F-V10_Gen1`, `F-V10_Gen2`, `F-V10_Gen3` | exact match (note: Gen3 crd year = **2006**, not 2005 as in RESEARCH; Gen2 years 2001–2002) |
| F-V8 Gen1/2/3 (ex F-Reiza) | `F-V8_Gen1` (2006), `F-V8_Gen2` (2008); **no `F-V8_Gen3`**; **`F-Reiza` still exists** (formula_reiza, 2010) | **Gen3 does not exist; F-Reiza was never renamed** |
| F-Hybrid Gen1/Gen2/Gen3 | **No `F-Hybrid*` classes at all.** Actual: `F-Ultimate_Gen1` (2016 car, crd year 2019), `F-Ultimate` (2019 car), `F-Ultimate_Gen2_RET` (2022 car), `F-Ultimate_Gen2` (2024 car) | **rename never happened in xml data; 2024 gen absent from RESEARCH table** |

CustomAIDrivers filenames on disk agree with the game names: `F-Hitech_Gen1.xml`, `FE-G1.xml`, `F-Reiza.xml`,
`F-Ultimate.xml`, `F-Ultimate_Gen1.xml`, `F-Ultimate_Gen2.xml`, `F-V8_Gen1.xml` (no F-Hybrid/F-V8_Gen3 files).

**Action for RESEARCH.md / season map:** key seasons by these actual names: 1992/93 → `F-Hitech_Gen1/2`;
1995 → `FE-G1` (3 engine models) and/or legacy `F-V12`; 2011–13 → `F-Reiza` (single 2010-spec car, not three
F-V8 gens); 2016 → `F-Ultimate_Gen1`; 2019 → `F-Ultimate`; 2022 → `F-Ultimate_Gen2_RET`; 2024 → `F-Ultimate_Gen2`.

## Check 2 — vehicles.json spot-check vs .crd files

**PASS (5/5 exact).** Seeded random picks; exact string compare on `Vehicle Class` plus year/PI/AI-only:

| vehicles.json entry | .crd on disk | class | year | PI | aiOnly |
|---|---|---|---|---|---|
| Brabham BT44 (formula_retro) | formula_retro.crd | F-Retro_Gen1 = | 1974 = | 129 = | false = |
| VW_Polo (vw_polo) | vw_polo.crd | TSICup = | 2021 = | 129 = | false = |
| Camaro_SS (camaro_ss) | camaro_ss.crd | Street = | 2019 = | 129 = | false = |
| Chevette (chevette) | chevette.crd | CopaClassicB = | 1981 = | 107 = | false = |
| Formula_2K_SC (formula_2k_sc) | formula_2k_sc.crd | SafetyCar = | 2006 = | 107 = | true = |

What the extractor extracted is accurate. What it *covered* is not — see Check 2b.

## Check 2b — vehicle coverage census (extraction bug, MATERIAL)

**FAIL.** Disk has **540 .crd files in 277 vehicle dirs**; vehicles.json has **268 entries** — exactly one per
dir that contains a crd named `<dir>\<dir>.crd`. Every other crd was skipped (proof: `stock_corolla_23.crd`
exists in both `stock_corolla\` and its own dir `stock_corolla_23\`; only the dir-named copy was picked up).
258 crds have no vehicles.json entry. 224 of them are AI-only derivatives (`*_LD` low-drag, `*_HD` high-df,
`*_SO/_SS/_SW` oval configs, safety cars) — excluding those may be a defensible design choice, though the
included `formula_2k_sc` safety car shows the exclusion is accidental, not designed.

**34 skipped crds are player-selectable (`AI ONLY=false`):**

- **F-USA_Gen1 (1995 CART) — whole class missing:** CART_Lola_T95_Ford / _Mercedes; CART_Reynard_95i_Ford / _Honda / _Mercedes
- **F-USA_Gen3 (2000 CART) — whole class missing:** CART_Lola_B2K00_Ford / _Mercedes / _Toyota; CART_Reynard_2KI_Ford / _Honda / _Mercedes / _Toyota
- **F-USA_Gen2 (1998 CART) — 4 of 6 cars missing:** CART_Reynard_98i_Ford / _Honda / _Mercedes / _Toyota (only Lola T98 + Swift 009c present)
- **F1 ladder variant models missing:** Formula_Hitech_G1M4 (F-Hitech_Gen1), Formula_V10_M (F-V10_Gen2, 2002), Formula_V10_G3_B (F-V10_Gen3), Formula_V8_G1_B (F-V8_Gen1)
- **LMP2_Gen1 — whole class missing:** Oreca_07_G1 (2023), Ligier_JS_P217_G1 (2024)
- **P1Gen2 incomplete:** Ginetta_G58_Gen2, MetalMoro_AJR_Gen2_Chevy / _Honda / _Nissan
- **StockCarV8_2021 / StockCarV8_2022 — whole classes missing:** Stock_Corolla_21/22, Stock_Cruze_21/22
- **TSICup incomplete:** VW_Polo_GTS, VW_Virtus_GTS
- **GTCupN — whole class missing:** Porsche_991_GT3R_24h
- **Supercars incomplete:** Chevrolet_Corvette_C8_Z07

classes.json (built from vehicles.json) therefore **lacks 6 selectable-car classes entirely**
(`F-USA_Gen1`, `F-USA_Gen3`, `LMP2_Gen1`, `GTCupN`, `StockCarV8_2021`, `StockCarV8_2022`) and **undercounts**
`F-USA_Gen2`, `F-Hitech_Gen1`, `F-V10_Gen2`, `F-V10_Gen3`, `F-V8_Gen1`, `P1Gen2`, `TSICup`, `Supercars`.
The F-USA gap directly hits RESEARCH.md's career content ("Lola/Reynard/Swift, 4 engine makes" per gen).

**Fix:** enumerate all `*.crd` per dir (not just dir-named), key vehicles by the crd's internal `Name` prop, and
either include the `_LD/_HD/_SO/_SS/_SW`/SafetyCar derivatives with their `AI ONLY` flag or filter them by
explicit suffix rule — decided, not accidental.

Data quirk found on the way (game data, faithfully extracted, NOT a bug): `mclaren_mp45` (MP4/5, 1989,
selectable) sits in a one-car class literally named **`Mclaren_MP46`** in its crd — career code must not choke
on it. Some crds also carry duplicate internal names (e.g. `ford_mustang_gt3_ld.crd` has `Name=Ford_Mustang_GT3`).

## Check 3 — tracks.json

**PASS.**

- **Count:** 294 entries vs 296 dirs; the 2 unmatched dirs are `textures` and `_data` (not tracks). All 294
  ids exist as dirs; zero JSON-side orphans; zero duplicate ids. Effective coverage 294/294.
- **adelaide_historic:** present, year 1988, 3780 m, maxAI 25, grade Historic — matches its .trd exactly.
- **Monza variants:** monza_1971 (+`_10k` 10100 m banked, `_10knc`, `_junior`), monza_1991, monza_2005,
  monza_2020 (+junior/stt). Years sane.
- **Spa variants:** spa-francorchamps (2019), _1970 (+_nc, 14120 m), _1993, _2005 (+_ec), _2020, _2022, _2022_rx.
  Note: `spa-francorchamps_2022` has `year: 2020` — verified against its .trd (`Year=2020`): a **game-data
  quirk, not an extraction bug**.
- **maxAiParticipants:** range 5–47. Thirteen layouts are below 10 — all rallycross/dirt venues (DirtFish=5,
  RX=7–9). Verified `dirtfish_boneyard_course.trd` says `Max AI participants=5`: genuine game data, so the
  "10–64" expectation only holds for circuit racing; career code must not assume min 10 grid on RX/dirt.
  No track exceeds 47.
- **Historic layouts:** 47 with `trackGrade == "Historic"` vs research's "~44" — within tolerance (delta likely
  the `_nc`/no-chicane variants and 1.6.9.x additions). Research's "210 layouts" figure counts a narrower
  subset than the 294 here (which includes 21 kart, 16 RX, 4 dirt, ovals, test tracks).
- **Blank trackGrade on 11 layouts** (bologna, moravia, california_highway*, indianapolis_oval, testoval,
  bannochbrae1…): verified in .trd — `TrackGradeFilter` is genuinely empty (bologna) or absent (moravia).
  Game data, faithfully extracted; consumers need a null/empty-grade fallback.

## Check 4 — liveries.json

**PASS with a cross-file join hazard.**

- Parses cleanly; `classes` object non-empty (156 keys); **zero classes with both sources empty**.
- **F-Vintage_Gen1:** stockLib1563 = 20 names AND observedInUserFiles = `F-Vintage_Gen1.xml` (20 names, the
  jusk/Alain-Fry 1967 set, e.g. "Brabham-Repco #1 J. Brabham"). Both sources present. ✔
- **F-Classic_Gen2:** stockLib1563 = 26 names AND observed = `F-Classic_Gen2.xml` (39 names, 1988 set). ✔
- **Casing hazard (flag):** livery keys come from the stale 1.5.6.3 library, so
  `F-HiTech_Gen1`/`F-HiTech_Gen2` (capital T) and `KartCross` do **not** case-sensitively match the game's
  `F-Hitech_Gen1`/`F-Hitech_Gen2`/`Kartcross` in classes.json — the user's own AI files
  (`F-Hitech_Gen1.xml`) were merged under the wrongly-cased key. Any case-sensitive join loses HiTech liveries.
- **No livery key at all for 12 classes.json classes** (post-1.5.6.3 content with no user AI files):
  `F5`, `F-Ultimate_Gen2_RET`, `F-USA_2022`, `Group 5`, `Group 7`, `GT3N`, `GT4N`, `GTO`, `LMP3`,
  `Mclaren_MP46`, `SafetyCar`, `SST`. Expected given the sources, but the companion needs a fallback
  (and a fresh in-game/crd-level livery extraction for these).

## Check 5 — encoding

**PASS.** All four files are BOM-less UTF-8. Zero mojibake hits (searched `Ã*`, `Â*`, `â€*`, U+FFFD in all
files). classes/vehicles/tracks are pure ASCII (internal ids/names are ASCII by design). liveries.json contains
correctly encoded accents: "1995 McLaren #8 - M. Häkkinen", "1988 Minardi #24 - L. Pérez-Sala",
"1995 Pacific #16 - J-D. Delétraz" (ä é ü ç ö ø all well-formed). No `ã` occurs anywhere in the data —
nothing was stripped; none of the current names use it.

## Cross-consistency (bonus checks)

- classes.json `vehicleCount` == vehicles array length for all 116 classes; every class member resolves to a
  vehicles.json entry; every vehicles.json `vehicleClass` exists in classes.json. Internally consistent —
  which is exactly why the Check 2b omissions propagate silently.
- vehicles.json dirs all exist on disk; no phantom entries.

## Summary of required fixes

1. **Re-extract vehicles.json enumerating every `.crd`** (34 selectable cars missing; F-USA Gen1/Gen3, LMP2_Gen1,
   GTCupN, StockCarV8_2021/2022 classes lost entirely); rebuild classes.json from it.
2. **Correct RESEARCH.md section 2** with the actual names from Check 1 (`F-Hitech_*`, `FE-G1` + surviving
   `F-V12`, no `F-V8_Gen3` / surviving `F-Reiza`, `F-Ultimate*` family instead of `F-Hybrid*`, plus
   `F-Ultimate_Gen2` = 2024).
3. **Normalize or alias livery class keys** to the game's casing (`F-HiTech_*` → `F-Hitech_*`, `KartCross` →
   `Kartcross`), or make all class joins case-insensitive; add livery fallback for the 12 keyless classes.
4. Treat blank `trackGrade`, sub-10 RX `maxAiParticipants`, `spa-francorchamps_2022.year=2020`, and the
   `Mclaren_MP46` one-car class as legitimate game data in downstream validation.

---

## Addendum (2026-07-02, post-verification fixes)

The two material issues above were fixed in-place after this report was written:

1. **vehicles.json / classes.json re-extracted over ALL 540 .crd files** (was: 268 dir-named only).
   Now 540 vehicles / 194 classes, 0 parse failures. Restored: full CART F-USA_Gen1/Gen2/Gen3 grids
   (per-engine + _hd/_so/_ss/_sw track-config variants as their own class entries), LMP2_Gen1,
   GTCupN, StockCarV8_2021/2022, F1-ladder variant models. Each vehicle carries id (crd filename
   base) + dir (vehicle folder).
2. **liveries.json re-keyed to game casing**: F-HiTech_Gen1/Gen2 -> F-Hitech_Gen1/Gen2,
   KartCross -> Kartcross (keys now join case-sensitively against classes.json).

Still open (accepted, documented): 12 newer/niche classes have no livery source (F5,
F-Ultimate_Gen2_RET, F-USA_2022, Group 5, Group 7, GT3N, GT4N, GTO, LMP3, Mclaren_MP46, SafetyCar,
SST); stock livery names for them need in-game extraction (-showLiveryIDs) or a newer community
library. Runtime preflight scans installed override XMLs regardless, so generated grids for
skinpack-based seasons are unaffected.
