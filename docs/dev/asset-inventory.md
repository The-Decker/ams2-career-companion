# SMGP Art Asset Inventory

**Owner:** Codex (GUI/art) · **Last audited:** 2026-07-12 · **Roster:** 34 drivers, 24 teams,
16 rounds — ids in `packs/smgp-1/{drivers,teams,entries}.json`.

This is the standing SMGP inventory. The broader cross-mode inventory remains in
`data/ams2/ART-INVENTORY.md`.

## Canonical source rule

The app resolves `{exe}\data\ams2\<kind>\<key>.{jpg|jpeg|png}` through `KeyedAssetImageConverter`.
The live files under **`dist/data/ams2/` are canonical; anything there is law**. Never copy a tracked
`data/ams2` file over its `dist` counterpart. If a secondary archive is wanted, derive it outward from
`dist` and preserve the canonical file byte-for-byte.

## Status

| Kind | Canonical path | Key | Used by | Present | Missing |
|---|---|---|---|---:|---:|
| Driver **portrait** | `portraits/<driverId>.jpg` | driver id | grid, rival, paddock, dossier, promotion | **34 / 34** ✅ | 0 |
| Driver **car** render | `cars/<driverId>.png` | driver id | grid, rival, paddock | **34 / 34** ✅ | 0 |
| **Player** image (per team) | `portraits/player.<team>.jpg` | team id without `team.` | player grid/paddock/dossier | **24 / 24** ✅ | 0 |
| Driver **national flag** | `smgp/flags/<driverId>.png` | driver id | SMGP pixel grid | **34 / 34** ✅ | 0 |
| Optional **grid car** | `smgp/grid-cars/<driverId>.png` | driver id | SMGP pixel grid | **0 / 34** | 34 optional |
| Team **logo/icon** | `smgp/logos/<teamId>.png` | team id | paddock team cards | **0 / 24** | **24** |
| Team **photo** | `smgp/teams/<team>.jpg` | team id without `team.` | promotion/demotion | **0 / 24** | **24** |
| **Round** card | `smgp/rounds/<roundNumber>.jpg` | round 1–16 | rival / briefing | **0 / 16** | **16** |
| Rival **banner** | `smgp/banners/<teamId>.png` | team id | rival dossier | **0 / 24** | **24** optional |

The core driver presentation is complete. Remaining authored art: 24 logos, 24 team photos, 16 round
cards, and 24 optional rival banners. The 34 optional grid-car sprites are an enhancement because the
canonical side previews already provide a complete fallback.

## Current grid-art contracts

- `smgp/grid-cars/<driverId>.png` is preferred over `cars/<driverId>.png`. A purpose-built sprite should
  be 384×256 transparent RGBA, consistently framed, with the nose pointing right and the authored livery
  and number preserved.
- `smgp/flags/<driverId>.png` is driver-keyed because `StartingGridSlot` intentionally remains in the
  ViewModel owner’s lane. The 34 canonical 128×128 PNGs were converted locally from the matching
  `GUI/CountryFlags/Flag_*.dds` files in Mike’s AMS2 installation.
- The synthetic player has no authored nationality, so its flag slot is hidden. Never display the donor
  AI driver’s nationality as the player’s.

## Style + sizing

Match the established 16-bit Super Monaco GP arcade treatment:

| Asset | Display | Suggested source | Notes |
|---|---:|---:|---|
| Driver portrait | 54px grid; up to 400px rival | 512×512 | painted/pixel head-and-shoulders, square crop |
| Car preview | ~100–300px grid; 150px rival | 900×281 current | transparent PNG, exact team livery |
| Grid-car sprite | ~100–300px | 384×256 | transparent, consistent three-quarter/overhead angle |
| Player image | portrait sizes | 512×512 | team-coloured driver/helmet art |
| National flag | 24×18 grid frame | 128×128 current | canonical AMS2 flag icon, driver-keyed |
| Team logo | ~46px list → larger detail | 256×256 | transparent crest/badge |
| Team photo | large hero | 1280×720 | garage/car hero shot |
| Round card | ~120–180px | 800×450 | venue/skyline |

## Priority

1. Team logos once Mike’s exact palette lands.
2. Team photos for promotion/demotion.
3. Round cards.
4. Optional rival banners and purpose-built grid-car sprites.
