# SKINS + AI ROLLOUT AUDIT — `Z:\SKINS 4 AI MUCH LOVE` (through 2026-07-11)

Ground truth for the per-season skins/AI rollout. Built by extracting every archive's XML metadata
(no textures) into `scratchpad/skins-study/` and diffing against the live install
(`Y:\SteamLibrary\steamapps\common\Automobilista 2`).

## (a) Archive → season/class map + install state

| Archive | Season | ams2Class | Car models (override XMLs) | Installed? | Notes |
|---|---|---|---|---|---|
| F-Retro-G1 - 1974 v.1.1.zip | 1974 | F-Retro_Gen1 | formula_retro, _v12, _v8, lotus_72e, mclaren_m23 | YES (`F1_1974` subdirs) | |
| F-Retro-Gen1-1975-v1.1.7z | 1975 | F-Retro_Gen1 | SAME five models | no — CONFLICTS with 1974 | parked per Mike; identical override paths |
| F-Retro-Gen3_TAMS2SP_1983 V2-2.zip | 1983 | F-Retro_Gen3 | formula_retro_g3, _te, mclaren_mp4_1c, brabham_bt52 | YES (`TAMS2SP` subdirs) | **ROLLED OUT 2026-07-11**: f1-1983 pack + skinSeason + 24 active pointers + Humpty AI parity |
| F1_1985.zip | 1985 | F-Retro_Gen3 | formula_retro_g3, _te, mclaren_mp4_1c (NO bt52) | no — CONFLICTS with 1983 | see (e) |
| F1_1996HC_260707.rar | 1996 | F-V10_Gen1 | formula_v10_g1, mclaren_mp4_12 (+per-race variants, +Equal/Realistic/Team AI XMLs) | no — CONFLICTS with 1997 | NEW-season opportunity |
| F1_1997HC_260707.rar | 1997 | F-V10_Gen1 | same models (+per-race variants +AI variants) | YES (`F1_Season_1997`) | our f1-1997 pack exists |
| F1_1998HC_260114.rar | 1998 | F-V10_Gen2 | formula_v10 (+per-race variants +AI) + camaro_ss_safetycar | YES (`F1_1998HC`) | NEW-season opportunity (we have 2000 on this class) |
| F1_2010HC_260627.rar | 2010 | F-Reiza | formula_reiza (+per-race +Equal/Realistic AI); bundled SC pointer excluded as foreign content | YES (`F1_Season_2010`) | **ROLLED OUT 2026-07-11**: f1-2010 pack + skinSeason + AFry source parity + selector/DNS/weather guards |
| F1_2016HC_260628_3.rar | 2016 | F-Ultimate_Gen1 | formula_ultimate_2016 (+per-race +AI) | YES (`F1_Season_2016`) | our f1-2016 pack exists |
| [AMS2]F1_1978_Season 1.45.rar | 1978 | F-Retro_Gen2 | formula_retro_g2, lotus_79, brabham_bt46, bt49 | YES (`F1_1978_Season`) | ours exists |
| [AMS2]F1_1986_Season 1.43.rar | 1986 | F-Classic_Gen1 | g1m1, g1m2, lotus_98t (+per-RACE variants!) + own AI XML | YES (`F1_Season_1986`) | ours exists |
| [AMS2]F1_1991_Season 2.13.rar | 1991 | F-Classic_Gen4 | g4m1–m3, mclaren_mp46 (+per-race variants incl. 00Alesi) | YES (`F1_Season_1991`) | ours exists; **Juppo AI imported 2026-07-10** |
| [AMS2]F1_1995_Season 3.05.rar | 1995 | FE-G1 | formula_edge_g1m1–m3 (+per-race variants +Equal/Realistic AI) + bmw_m3_e30 SC | YES (`F1_Season_1995`) | ours exists |
| [AMS2]F1_2012_Season 2.08.rar | 2012 | F-Reiza | formula_reiza (+per-race +AI) + mercedes_amg_sc | YES (`F1_Season_2012`) — NOTE shares formula_reiza with 2010 pack; both installed?? formula_reiza dir shows only `F1_Season_2010` textures + 22 xmls — VERIFY which season's main xml is live | NEW-season opportunity |
| [AMS2]F1_2020_Season 1.01.rar | 2020 | F-Ultimate | formula_ultimate_2019 (+per-race) + mercedes_amg_sc | YES (`F1_Season_2020`) | ours exists |
| [IMG] F1 1990 v1.4.zip | 1990 | F-Classic_Gen3 | g3m1–m4, mclaren_mp45b (+per-race variants +what-ifs) | YES (`F1_Season_1990`) | ours exists |
| [IMG] F1 1992 v1.3.zip | 1992 | F-Hitech_Gen1 | g1m1–m4, mclaren_mp47 (+per-race variants) | YES (`F1_Season_1992`) | ours exists |
| [IMG] F1 1993 v1.1.zip | 1993 | F-Hitech_Gen2 | g2m1–m3, mclaren_mp48 (+per-race variants, Sen/Man specials) | YES (`F1_Season_1993`) | ours exists |
| SMGP SKINS V1.rar | Super Monaco GP | F-Classic_Gen3 | g3m1–m4 (SMGP-team liveries: Madonna/Firenze/Millions/Bestowal/…) + OWN CustomAIDrivers roster (32 drivers, 17 fictional teams, G. Ceara 0.99) | no — CONFLICTS with 1990 | the SMGP replica mode's content |
| Juppo's AI F1 1991.rar | 1991 AI | F-Classic_Gen4.xml | — | imported into packs/f1-1991 (`ae8276f`) | schema: 13 std dims + drag/power/weight scalars + setup_downforce(+_randomness); 26 base drivers + 49 per-track blocks |

Also installed but NOT in the directory: `F1_Season_1967`, `F1_Season_1969`, `F1_Season_1988`
(+ `_companion-backups` — our activator's work), `F1_Season_2001` (camaro SC), `F1 2024 Skinpack`.

## (b) The CONFLICT mechanism (verified)

Two season packs for the same car model collide ONLY on
`Overrides\<model>\<model>.xml` (and `_dist.xml`) — every pack keeps its TEXTURES in its own
subfolder (`TAMS2SP\`, `F1_1985\`, `SMGP\`, `F1_Season_1990\`…). So an in-app **Skin Season
Manager** is cheap: keep every season's `<model>.xml` variant in a library
(`data/ams2/skin-seasons/<season>/<model>.xml` or read them straight from the install-side
subfolders), and at career load/stage time swap the ACTIVE `<model>.xml` backup-first — exactly
the AI-file staging contract. Textures for all seasons coexist on disk; only the pointer file
swaps. Conflicted pairs needing the swap: 1974↔1975, 1983↔1985, 1996↔1997, 1990↔SMGP,
2010↔2012 (formula_reiza + mercedes_amg_sc).

## (c) RACE-BY-RACE variant binding (Mike's "bound race by race")

The big packs ship per-race override XMLs alongside the active one (installed and verified:
`formula_classic_g4m1_03Imola.xml`, `formula_v10_g1_1997_08FRA.xml`, `formula_hitech_g2m2_15Suzuka.xml`,
`mclaren_mp48_Sen.xml` …). The game only reads `<model>.xml` — the variants exist for MANUAL
per-race swapping. The app should do it automatically: when STAGING round N, for each car model
find the variant whose suffix matches the round (tokenized like import_jusk_ai's venue matching:
`03Imola`→Imola round, `08FRA`→French GP, `01AUS`→Australian GP …), copy it over `<model>.xml`
(timestamped backup + marker, same contract as the AI writer), restore/next-swap on the next
round. No variant match → leave the base XML. This is sim-inert (pure staging).

## (d) MAX-GRID + skinpack-roster seasons (the "1988 method" rollout)

Mike's rule: every round fields the class MAXIMUM (livery-cap) grid; the season roster = the
skinpack roster (livery names in the pack's `<model>.xml` files + its CustomAIDrivers XML when
shipped); no 10-car Kyalami. Player picking a non-default car replaces the slowest seat at
staging (mechanism exists for 1988). Per season this means: (1) canonicalize entries.json to
one-driver-per-seat bound to the skinpack livery names; (2) regenerate every round's
grid.starterDriverIds to the FULL roster (cap-aware); (3) where the pack ships its own
CustomAIDrivers XML (1986, 1995 Equal/Realistic, 1996/1997/1998 incl. team variants, 2010, 2012,
2016, SMGP), import it as the ratings source (import_jusk_ai.cs works as-is; choose "Realistic"
variants). Grid-size changes are pack data → NEW careers only; existing pins untouched.

## (d.1) F1 1983 rollout — shipped

`packs/f1-1983` now binds `skinSeason: "f1-1983"` to all four installed TAMS2SP pointer models.
The source archive has 26 Humpty/mungopark Custom-AI profiles; the active pointer union has 24
unique liveries, so the pack fields exactly that stageable intersection. Guerrero #33 and
Giacomelli #36 remain documented optional-source omissions because enabling them would exceed
the active set. Every staged driver carries all 13 source ratings plus reliability.

One pointer typo was corrected against the actual texture inventory: Jarier's visor spec path is
`83_Jarier_visor_spec.dds`, not the archive XML's `83_Jarrier_visor_spec.dds`. Source hashes,
roster policy, and regeneration order are recorded in `docs/research/1983-source-parity.md` and
pinned by `F11983SourceParityTests`.

## (d.2) F1 2010 rollout — shipped

`packs/f1-2010` binds `skinSeason: "f1-2010"` to the generic Formula Reiza source pointer and the
installed monotonic race variants. All 27 real 2010 starters are represented by 28 livery entries;
Yamamoto's #21 R10 seat and #20 later seat bind one historical driver. The five real DNS events
remain 23-car grids rather than being backfilled.

AFry's Realistic source contributes 14 ratings plus four car fields for every driver and 309
starter-filtered per-track patches across 13 rounds. The repo skin-season directory intentionally
contains only `formula_reiza.xml`: installed variants are discovered beside the active pointer,
and the captured `mercedes_amg_sc.xml` was unrelated 2012/2024 content. Four upstream late-season
variants refer to missing `Sauber_visor_Heidfeld.dds`; use the real `Sauber_visor.dds` path until
the source pack is corrected. Automatic installed-file repair is outside this data-only lane.

Hashes, importer traps, selector windows, and the regeneration contract are recorded in
`docs/research/2010-source-parity.md` and pinned by `F12010SourceParityTests`.

## (e) F1_1985.zip — the three problems

1. CONFLICT: same `formula_retro_g3*` override XMLs as the installed 1983 TAMS2SP pack (textures
   don't collide — `F1_1985\` vs `TAMS2SP\` subfolders). Needs the season manager (b).
2. Only 10 ACTIVE livery slots (LIVERY 51–60) per model; ~20 more liveries are commented out with
   manual find-and-replace instructions — exactly the pattern our livery ACTIVATOR already
   automates (cap-aware activation, backup-first).
3. The XML is technically MALFORMED — the alternates comment block contains `--` sequences, which
   strict XML parsers reject ("An XML comment cannot contain '--'"). AMS2 tolerates it. Any app
   code touching these files must comment-strip with regex BEFORE parsing (import_jusk_ai.cs
   already does; the Skins lens scanner must too — verify).
4. Also: no brabham_bt52 skins in 1985 → that model can't field 1985 cars (grid = 3 models).

## (f) Juppo AI schema — the "new weights" to adopt

Beyond the 13 imported dims: `drag_scalar`, `power_scalar`, `weight_scalar` (per-driver CAR
performance — his "evolving car balancing"), `setup_downforce`, `setup_downforce_randomness`,
`fuel_management`, `vehicle_reliability`. Plan: add optional scalar fields to PackDriverRatings
(+ aiOverrides), teach the staged-XML writer to emit them, extend import_jusk_ai FIELD mapping.
All staging-side (the fold never reads them) → sim-inert, no determinism gate; ReferencePack +
writer tests. Then re-import Juppo 1991 to pick up the 49 per-track scalar blocks currently
dropped (base ratings + skill-only track blocks landed 2026-07-10, `ae8276f`).

## (g) BLUE FLAGS — options for Mike (ideas only, no code yet)

1. **Author `blue_flag_conceding` deliberately** (it's already a per-driver field the game reads
   and our importer maps): high (0.8–0.95) for backmarker-tier drivers = yield early and clean;
   moderate (0.5–0.7) midfield; low for front-runners. Today's f1db-derived packs carry defaults —
   a tier-based pass over all seasons is one derive-tool change.
2. **Lower per-driver `aggression` for backmarkers** — the "turns into the passing car" behavior
   correlates with aggression during lapping.
3. **Briefing advisory row**: recommend the in-game "Opponent Aggression" setting per track type
   (Low at Monaco/street circuits where the pile-ups happen; Medium elsewhere). Sim-inert
   checklist row; zero risk.
4. Accept the engine limit: AMS2's blue-flag logic itself isn't moddable — 1–3 are the real
   levers. Recommend trying (3) first (free), then (1)+(2) as a data pass, and iterating from
   Mike's in-game observations.

## (h) Parked / future

- **1975**: study-only until the season manager (b) exists — then it's just another swap set.
- **NEW season packs still unlocked by this directory**: 1996 (F-V10_Gen1), 1998
  (F-V10_Gen2), and 2012 (F-Reiza — shares the class with the shipped f1-2010 pack). All have per-race
  variants + curated AI files. NO FANTASY PACKS rule respected — all real seasons.
- **JGTC 500 / Ferrari 355 Challenge / 488 Challenge**: AMS2 has no Ferrari/JGTC base content —
  these need mod discovery in `Z:\RCM MODS AMS2` + RCM/OverTake (a content-discovery task, then
  ordinary pack-building; the alternate-track style install-verification applies).
- **SMGP replica mode**: dedicated design doc + megaprompt section (research workflow output).
