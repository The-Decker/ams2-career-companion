# SMGP news/history art asset manifest (mode-separation finalization)

_2026-07-18, Claude (Head of Coding). The visual-slot audit for SMGP narrative surfaces. Art
resolution keys are stable; nothing here falls back to Dynasty or Racing Passport art._

## Canonical SMGP art (all present, validated by `SmgpWorldCompletenessTests`)

| Slot | Key pattern | Coverage | Status |
|---|---|---|---|
| Team logo (24) | `data/ams2/smgp/logos/team.<teamId>.png` | 24/24 | FINAL (refreshed 2026-07-17) |
| Team HQ banner (24) | `data/ams2/smgp/banners/team.<teamId>.png` | 24/24 | FINAL (all re-rendered 2026-07-18; opaque, unique) |
| Driver portrait (34) | `data/ams2/portraits/driver.<driverId>.jpg` | 34/34 | FINAL (pixel-art set; PNG bytes under .jpg names, by design) |
| Top-down grid car (34) | `data/ams2/grid-cars/driver.<driverId>.png` | 34/34 | FINAL (starting-grid miniatures) |
| Sponsor board logos | `data/ams2/smgp/sponsors/corporate-batch-01/*.png` | batch 01 complete | FINAL for batch 01; more batches are an art-supply item, not a narrative-system gap |
| Team photos (P1) | `data/ams2/smgp/teams/<team>.jpg` | PARTIAL | art-supply item (Mike's list): polished fallback cards render meanwhile; tracked in `data/ams2/ART-INVENTORY.md` |
| Round cards (P1) | `data/ams2/smgp/rounds/<key>.jpg` | PARTIAL | art-supply item (Mike's list): fallback render path; tracked in `ART-INVENTORY.md` |
| Player-team images | `data/ams2/smgp/player.<team>.jpg` | PARTIAL | some present; missing ones fall back deliberately (no broken references) |

## Narrative-surface rules (binding)

- **No Dynasty/Passport art ever substitutes for SMGP art.** A missing SMGP asset renders a
  deliberate SMGP-themed fallback (team palette card), never another mode's imagery and never a
  generic "missing image" placeholder.
- **Era art is medium-keyed, not mode-mixed.** `data/ams2/era-art/` (telegram/fax/email + the
  per-year photos + `smgp.jpg`) resolves per career; the SMGP replica resolves its own
  `smgp.jpg` identity rather than a year photo.
- **The 17 season packs carry no artwork keys** (text-only by design; presentation keys off the
  canonical sets above). No broken references exist in `data/rules/newsroom/**`.
- **Missing-art handling is intentional everywhere above:** the three PARTIAL rows are the
  only remaining art-supply items (owner's list), each with a deliberate fallback and a stable
  key reserved for the later art pass. None blocks the narrative system.

## Verification

- `SmgpWorldCompletenessTests` (4/4): logos, banners, portraits, grid cars — existence,
  decodability, roster coverage.
- `SmgpRivalBannerAssetRenderTests` + `SmgpRivalBannerScaleRenderTests` (render): exact fileset,
  unique hashes, no stray alpha, theme×scale fit.
- `EraThemingRenderTests` (render): per-medium document art + tool-surface invariance.
