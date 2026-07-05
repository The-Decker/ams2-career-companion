# AMS2 Local Install Inventory (this machine)

Snapshot date: 2026-07-02
Machine: Windows 11, Steam at `C:\Program Files (x86)\Steam`, AMS2 (app 1066890, single depot 1066891) at `Y:\SteamLibrary\steamapps\common\Automobilista 2`.

## Method notes (DLC ownership)

AMS2 DLC is **license-gated, not depot-gated** — all content ships in the single depot to every owner, so neither the app manifest (`appmanifest_1066890.acf` lists only depot 1066891) nor `content_log.txt` mentions any DLC appid. `HKCU:\Software\Valve\Steam\Apps` has a key for the base game (1066890, Installed=1) but **zero per-DLC keys**, so the registry method also fails.

The method that DID work: **Steam library asset cache**. `C:\Program Files (x86)\Steam\appcache\librarycache\<appid>\` folders are created for apps in the user's library, and the `steamui_librarycache.previous.txt` log (cache-format conversion on 2026-06-12) enumerates the same set via "New app &lt;id&gt;" lines. Of the 22 official AMS2 DLC appids (from the Steam store API `appdetails`/`dlcforapp` for 1066890), **21 have librarycache folders and conversion-log entries; exactly 1 does not**. `webhelper_js` logs additionally show library header-asset fetches for several of these ids. This is a strong ownership signal but not an authoritative license check — confidence **medium-high**. Definitive confirmation: in-game Content page or Steam account licenses page.

## DLC owned (21 of 22, evidence-based)

Evidence key: **LC** = `appcache\librarycache\<appid>` folder present now; **LOG** = "New app" entry in `steamui_librarycache.previous.txt` (2026-06-12); **WH** = library asset fetch in `webhelper_js` logs.

| AppID | Name | Evidence |
|---|---|---|
| 1392090 | Historical Track Pack Pt1 | LC, LOG |
| 1392091 | Supercars Pack Pt1 | LC, LOG |
| 1648110 | Racin´ USA Expansion Pack | LC, LOG, WH |
| 2238190 | Brazilian Racing Legends Pack | LC, LOG |
| 2518750 | Adrenaline Pack Pt1 | LC, LOG |
| 2518760 | Adrenaline Pack Pt2 | LC, LOG |
| 2697770 | Historical Track Pack Pt2 | LC, LOG |
| 2697780 | Formula HiTech | LC, LOG |
| 2697790 | Circuit des 24 Heures du Mans | LC, LOG |
| 2697800 | Endurance Pack Pt1 | LC, LOG |
| 3022980 | IMSA Track Pack | LC, LOG |
| 3022990 | Endurance Pack Pt2 | LC, LOG |
| 3023000 | Lamborghini Dream Pack Pt1 | LC, LOG |
| 3532160 | Endurance Pack Pt3 | LC, LOG, WH |
| 3532180 | Lamborghini Dream Pack Pt2 | LC, LOG, WH |
| 3895690 | Nürburgring 2025 | LC, LOG, WH |
| 4044660 | Historical Endurance Pack Pt1 | LC, LOG, WH |
| 4044670 | Historical Track Pack Pt3 | LC, LOG, WH |
| 4256590 | Supercars Pack Pt2 | LC, LOG, WH |
| 4674610 | Historical Track Pack Pt4 | LC, LOG |
| 4674620 | Hungaroring | LC, LOG, WH |

## DLC undetermined / likely not owned

| AppID | Name | Status |
|---|---|---|
| 1392100 | Automobilista 2 Premium Track Pack | No librarycache folder, no log entries anywhere. Likely **not owned**. This is the bundle-style track meta-pack; the individually owned track packs above cover the same content, so its absence is expected for someone who bought packs piecemeal. |

If a hard guarantee is needed, verify on the in-game Content page — every filesystem/registry source available without touching localconfig.vdf/licensecache is exhausted above.

## Skin packs / custom liveries: EXTENSIVE (managed by a mod manager)

Earlier assumptions were wrong in both directions:

- `Documents\Automobilista 2` contains **no** skin content: no `Vehicles`/`CustomLiveries`/`Overrides` dirs, no DDS files, and its 6 XMLs are graphics configs (no `LIVERY_OVERRIDE`). All customization lives in the **install directory**.
- `<install>\TextureOverrides` is **not** empty: it contains one small track-texture mod — `spa_francorchamps_2005_ec` trackside flag textures (2 DDS). Not a car skin pack.
- The real skin payload is `<install>\Vehicles\Textures\CustomLiveries\Overrides\`: **8,270 loose DDS files**, custom skins present for **124 of 287 vehicle folders**, organized into named pack subfolders.

### Mod manager

`<install>\mods\` has `enabled\` (41 archives), `Disabled\` (1 archive), and `state.json` (top-level key `Install`, tracks deployed files) — the layout of AMS2 Content Manager. Enabled archives (the installed skin/AI packs):

- **jusk / Alain Fry style F1 season packs:** `[AMS2]F1_1967_Season1 .32`, `F1_1969 1.23`, `F1_1978 1.45`, `F1_1986 1.43`, `F1_1988 1.75`, `F1_1991 2.13`, `F1_1995 3.00`, `F1_2000 2.21`, `F1_2020 1.01`, `F-Retro-G1 - 1974 v1.1`, `F1_1985`, `F1_1997HC_251114`, `F1 1997 Helmet & Suits Pack 1.1`, `Formula V10.G1 - Mastercard Lola and McLaren MP4-12 (2.1)`, `formula_v10_g1`
- **[IMG] packs:** F1 1990 v1.4, F1 1992 v1.3, F1 1993 v1.1, GT World Challenge Europe 2026 v1.2.1, IndyCar 1995 v2.0
- **US open-wheel / stock:** `CART-1998_Overides v1.0`, `CART2000 with Helmet from Vuzz Voom`, `F-USA 2023`, `Cup_1992HC` (NASCAR Cup 1992), Stock_g3 content
- **GT / tin-top:** `JTN Race Art Works - GT4 Skin Pack v1.68`, `FIA GT Championship v1.1` (2005 sets), `GT Endurance Legacy Pack 0.8` (GT97), `DTM 1991 1.41`, `BMW Procar 1980 1.1`, `Chevrolet Corvette - Variety Livery Pack`, `GTOpen`, `super_v8`, `TC70s`, `F-Junior FB`, `[AMS2]F301_BritishF3_1999`, `[AMS2]FF_Festival_DuratecChampions 1.20`
- **AI/name packs:** `! - NAMeS Real Drivers for AMS2 v5.9`, `NAMeS+AI for AMS2 Mods`, `F1 1997 Custom AI`
- **Track content:** `01_Track_Patches.7z`, `Zolder.7z`

Disabled: `jusk's CART 1998 Custom AI v0.2.zip` (CART 1998 *skins* are deployed, but this AI-name set is switched off).

### Deployed pack labels seen under Overrides

`F1_Season_1967/1969/1986/1988/1990/1991/1992/1993/1995/1997/2000/2020`, `F1_1974`, `F1_1978_Season`, `F1_1985`, `CART 1995`, `Cart1998`, `Cart2000`, `NASCAR_Cup_1992`, `Stock_g3`, `DTM1991`, `BMW Procar 1980`, `GTWC`, `GT97`, `2005` (FIA GT), `jtn`/`JTN` (GT4 pack), `BritishF3`, `F3_WCP`, `FJunior_FB`, `FF_Festival_DuratecChampions`, `RSR_74`, `C3 RC`/`C3 RCC`, `ultimagtr`, `Ad Personam`, plus cosmetic sets (Helmets, Suits, Rims, Interior, Vinyls, ...).

**⚠ Safety-car-only labels (verified 2026-07-03 — do NOT count as deployed season skin packs):**
`F1_Season_2001` exists ONLY under `Overrides\camaro_ss_safetycar`, and `F1_Season_2012` +
`F1 2024 Skinpack` exist ONLY under `Overrides\mercedes_amg_sc` — all three are cosmetic
safety-car skins. The actual class model dirs (`formula_v10\formula_v10_m`/`mclaren_mp416`
for 2001, `formula_reiza` for 2012, `formula_ultimate_2024` for 2024) contain no season pack
subfolders, just their `<model>_dist.xml`. Season-pack readiness for 2001/2012/2024 is
therefore **ai-only at best**, not skin-deployed.

### The 1967 / Alain Fry question: INSTALLED and binding

`UserData\CustomAIDrivers\F-Vintage_Gen1.xml` (header: "Custom AI by jusk - F1 1967 Season ... to match Alain Fry's skinpack") has a live counterpart on disk:

- `mods\enabled\[AMS2]F1_1967_Season1 .32.rar` is enabled in the mod manager, and
- it is deployed to `Vehicles\Textures\CustomLiveries\Overrides\formula_vintage_g1m1\F1_Season_1967\` and `...\formula_vintage_g1m2\F1_Season_1967\` (41 DDS each: per-driver bodies like `Clark_05.dds`, `Stewart_03.dds`, helmets, previews).
- The DDS driver/number naming lines up with the XML's `livery_name` entries (e.g. "Lotus-Ford Cosworth #5 J. Clark" ↔ `Clark_05.dds`, "BRM #3 J. Stewart" ↔ `Stewart_03.dds`).

Verdict: the skinpack the AI file targets **is installed** — high confidence.

## CustomAIDrivers snapshot

`Y:\SteamLibrary\steamapps\common\Automobilista 2\UserData\CustomAIDrivers\`

- **171 files total**; 157 in the root (mostly per-class AI XMLs, plus `README.txt` and three `.orig` backups), 14 under `Alternate NAMeS\`.
- Coverage is essentially every AMS2 class (baseline NAMeS files dated 2026-06-12), with **hand-curated season sets layered on top**, e.g.:
  - Historic F1 season files matching the skin packs: `F-Vintage_Gen1` (1967), `F-Vintage_Gen2` + 6 track-specific variants (1969), `F-Retro_Gen1/2/3`, `F-Classic_Gen1/_1986`, `Gen2/_1988`, `Gen3`, `Gen4`, `F-Hitech_Gen1/2` (1992/93), `FE-G1`/`_1995`/`_1995_Simtek`, `F-V10_Gen1` + `96_97`/`97_Lola`/`97_Lotus`, `F-V10_Gen2` + `_2000`/`_2000_Audi`, `F-Ultimate`/`_2020`
  - US racing: `F-USA_Gen1` plus **17 numbered variants** (`F-USA_Gen1_1`..`_17`, an IndyCar 1995 race-by-race set), `F-USA_2023` + SO/SS/SW ovals, `Stock_USA_Gen2_Cup1992`
  - GT: `GT3_Gen2` + `_P1/_P2/_P3` + `GT3_Gen2_WIF_Verstappen`, three GT4 grid sizes (32/40/51), `GT1_05`/`GT2_05`/`LMP1_05` (FIA GT 2005 era), `Group A`, `Procar`, `TC70S`
- `Alternate NAMeS\` = 14 XMLs in paired swap sets (drop-in replacements to toggle a class between name sets):
  - `Carrera Cup Brasil 2019` vs `Carrera Cup Original` (Carrera Cup.xml)
  - `F-V10_Gen2 2001 F1` vs `F-V10_Gen2 Original` (F-V10_Gen2.xml)
  - `Formula Vee Brasil 2025` vs `Formula Vee Brasil Original` (F-Vee_Gen2.xml)
  - `P1-P4 Multi-Class` vs `P1-P4 Original` (P1.xml, P2.xml, P3.xml, P4.xml)

## Implications for the career companion

1. **Custom-AI `livery_name` values only bind if the matching skinpack is deployed.** On this machine, the big season AI sets (1967, 1969, 1986, 1988, 1990–1993, 1995, 1997, 2000, 2020, CART 1995/1998/2000, Cup 1992, GTWC, GT4, FIA GT 2005 ...) all have their skinpacks deployed under `Vehicles\Textures\CustomLiveries\Overrides`, so they will bind. A generic tool should validate: AI XML → class → vehicle folders → pack subfolder present, before assuming a livery resolves.
2. **The disabled mod matters:** CART 1998 *skins* are deployed but "jusk's CART 1998 Custom AI" is in `mods\Disabled` — that grid runs with default AI names despite custom skins.
3. **DLC ownership does not affect what is on disk.** Single-depot licensing means every car/track exists locally regardless of ownership, so content extraction from the install enumerates everything; ownership only gates in-game selection. With 21/22 DLC owned (everything except the Premium Track Pack bundle), effectively all content is selectable in-game; no content-filtering is needed for this user, but the companion should still model ownership for portability.
4. **Ownership detection is heuristic.** librarycache is the only benign local signal; a robust implementation should let the user confirm/override the list (or read the in-game Content page manually) rather than trusting it blindly.
5. Alternate NAMeS swap sets and `.orig` backups indicate the user actively swaps AI name sets per class — the companion should treat `CustomAIDrivers\*.xml` as mutable user state, not static data, and the file pairs in `Alternate NAMeS` as the canonical variants.
