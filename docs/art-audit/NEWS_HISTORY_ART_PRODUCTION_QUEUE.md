# News and History Art Production Queue

Mission: `SMGP-NEWS-HISTORY-ART-001`  
State: `AUDIT_ONLY`  
Status: production plan only; no image generation or integration is authorized.

Every queue row points to stable IDs and current paths in `news_history_art_inventory.csv` or `news_history_physical_asset_inventory.csv`. Wildcards below are selectors over exact inventory rows, not invented runtime IDs.

## P0: Visibly broken or missing

### ART-P0-001: Rich newsroom templates

- Exact scope: 264 rows where `ContentSubtype=NEWSROOM_TEMPLATE`.
- Stable IDs: the exact JSON template IDs in `StableId`, including `ai-streak.watch`, `best-finish.improved`, and 262 others.
- Expected asset IDs: `newsroom:<StableId>`.
- Current asset path: empty.
- Current visible path: explicit placeholder frame in the active News lead, secondary-card, and reader surfaces.
- Required first action after approval: add a versioned, persisted editorial-art key contract before producing assets.

### ART-P0-002: Legacy era/body mappings

- Exact scope: 70 rows where `ContentSubtype=LEGACY_BODY_FAMILY`.
- Stable IDs: `legacy-news:<era>:<family>` for 7 era corpora and 10 body families.
- Expected asset IDs: `legacy:<era>:<family>`.
- Current asset path: empty.
- Current visible path: explicit News placeholder.
- Dependency: stable selector must preserve era and journal event identity.

### ART-P0-003: Legacy headline-only families

- Exact stable IDs:
  - `legacy-headline-only:driver.retirement.foreshadow|considering-future`
  - `legacy-headline-only:driver.retirement|age-performance`
  - `legacy-headline-only:driver.retirement|canon`
  - `legacy-headline-only:team.tier|promoted`
  - `legacy-headline-only:team.tier|relegated`
- Expected path: none exists.
- Current visible path: explicit News placeholder on a headline-only unified-wire record.
- Dependency: define a body-less article placement and deterministic era-aware art role.

### ART-P0-004: Clean-publish omissions

- Stable affected content selectors: `three-litre-dfv`, `v8-era`, year-resolved History/Start cards, and any dynamic era resolver request for the listed keys.
- Current source paths:
  - `data/ams2/era-art/1967.jpg`
  - `data/ams2/era-art/1969.jpg`
  - `data/ams2/era-art/1974.jpg`
  - `data/ams2/era-art/1978.jpg`
  - `data/ams2/era-art/1985.jpg`
  - `data/ams2/era-art/1986.jpg`
  - `data/ams2/era-art/1988.jpg`
  - `data/ams2/era-art/1990.jpg`
  - `data/ams2/era-art/1991.jpg`
  - `data/ams2/era-art/1992.jpg`
  - `data/ams2/era-art/1993.jpg`
  - `data/ams2/era-art/1995.jpg`
  - `data/ams2/era-art/1997.jpg`
  - `data/ams2/era-art/2000.jpg`
  - `data/ams2/era-art/2005.jpg`
  - `data/ams2/era-art/2006.jpg`
  - `data/ams2/era-art/2008.jpg`
  - `data/ams2/era-art/2016.jpg`
  - `data/ams2/era-art/2020.jpg`
  - `data/ams2/era-art/smgp.jpg`
- Current publish path: absent from `scratchpad/art-audit-publish-final/data/ams2/era-art`.
- Required action: add and test explicit publish-copy metadata. This is integration work, not art generation.

## P1: Placeholder or universal fallback

### ART-P1-001: Verified historical seasons

- Exact scope: 60 IDs `historical-season:1967` through `historical-season:2026`.
- Expected asset IDs: `history-season:<year>`.
- Expected paths: `data/ams2/history-art/<year>.(jpg|jpeg|png)`.
- Current path: no file exists; the History panel collapses.
- Required action after approval: establish a provenance-safe visual policy before creating any historical-looking imagery.

### ART-P1-002: Seventeen SMGP season identities

- Exact stable IDs: `smgp-season-lore:1` through `smgp-season-lore:17`.
- Source identity: production `ordinal=1..17`, shared `packId=smgp-1`, runtime years 1990-2006.
- Expected asset IDs: `smgp-season:1` through `smgp-season:17`.
- Current asset path: empty; season lore is text-only.
- Required action after approval: one season-specific identity per ordinal with preview/timeline-safe crops and explicit fictional provenance.

### ART-P1-003: SMGP dispatch families

- Exact stable IDs: the 24 `smgp-dispatch:*` rows in the inventory, from `smgp-dispatch:milestone.arrived` through `smgp-dispatch:world.title-tightens`.
- Current dynamic paths: `data/ams2/smgp/teams/<team>.jpg`, `data/ams2/portraits/<driver>.jpg`, and car identity art when a subject exists.
- Current fallback: explicit News placeholder when a usable subject/team key is absent.
- Required action after approval: contextual pools by category, season, subject, team, injury/severity, result, and championship state. Replace the order-sensitive selection key first.

## P2: Wrong context or serious quality problem

### ART-P2-001: Later-season generated race history

- Exact scope: 256 stable IDs `race:<season>:<round>` for seasons 2-17 and rounds 1-16.
- Current paths: `data/ams2/smgp/rounds/<round>.jpg`.
- Defect: round position is treated as venue identity despite shuffled calendars. The archive also carries current rather than historical car/team context.
- Required action: persist the actual venue/layout and historical seat identity, then approve intentional reuse or create season/venue variants. Do not generate against the current ambiguous mapping.

### ART-P2-002: Authored verified-history eras

- Stable ID and current path:
  - `three-litre-dfv`: `data/ams2/era-art/1967.jpg`
  - `ground-effect`: `data/ams2/era-art/telegram.jpg`
  - `turbo-era`: `data/ams2/era-art/1983.jpg`
  - `na-return-electronics`: `data/ams2/era-art/fax.jpg`
  - `refuelling-era`: `data/ams2/era-art/email.jpg`
  - `v8-era`: `data/ams2/era-art/2006.jpg`
  - `hybrid-era`: `data/ams2/era-art/email.jpg`
  - `ground-effect-reset`: `data/ams2/era-art/email.jpg`
- Defect: a start-year or broad-medium image is reused as era editorial art; two direct year assets are also missing from the publish.
- Required action after approval: dedicated era IDs, provenance-safe sources, and History-specific crop validation.

### ART-P2-003: Provenance and canon contract

- Exact physical scope: 332 rows where `ProvenanceStatus=PROVENANCE_REVIEW`.
- Current paths: exact paths are in `news_history_physical_asset_inventory.csv`.
- Stable content selectors: all mappings resolving those files, especially `smgp-almanac:*`, `race:*`, SMGP driver/team records, and era records.
- Required action: ownership/license manifest and explicit historical, SMGP-fiction, career-universe, or hybrid classification before approval.

## P3: Duplicate, repetitive, or weak editorial art

### ART-P3-001: SMGP career-beat families

- Exact stable IDs: `smgp-history-beat:Arrived`, `Demotion`, `Died`, `Finale`, `FirstPodium`, `FirstPoints`, `FirstPole`, `FirstStart`, `FirstTop5`, `FirstWin`, `Injured`, `NearMiss`, `Promotion`, `RivalryEarned`, `RivalryLost`, `SeasonEndingInjury`, `SeasonMilestone`, and `Title`.
- Current path: dynamic `data/ams2/portraits/<subjectDriverId>.(jpg|jpeg|png)`.
- Current fallback: text-only semantic card when no subject exists.
- Required action after approval: event-specific editorial pools, with fatality/injury art held to stricter context and provenance review.

### ART-P3-002: Heuristic near-duplicate review

- Exact physical group IDs: `NEAR-001` through `NEAR-007` in the physical inventory.
- Current paths: 5 groups under `data/ams2/cars/` and 2 groups under `data/ams2/portraits/`.
- Current status: suspected only. Exact hashes differ. A stricter supplemental comparison found only six car-template components and no cross-family candidate.
- Required action: manual visual review after the sandbox issue is resolved. Do not replace files solely because a perceptual hash is close.

### ART-P3-003: Team-art crop consistency

- Stable content selectors: 24 `SMGP_TEAM_HISTORY` rows and dispatch mappings resolving team art.
- Current paths: `data/ams2/smgp/teams/<team>.jpg`.
- Defect: 23 of 24 files are approximately 4:3, while News hero/card placements are wide `UniformToFill` crops.
- Required action: crop/focal review and variants only when the owner approves the final editorial placement contract.

## P4: Optional final polish

### ART-P4-001: Intentionally image-less records

- Exact scope: 1,747 mappings with `CurrentAuditStatus=INTENTIONALLY_IMAGELESS`.
- Stable selectors: `historical-round:*`, computed driver/team/circuit profile IDs, `subject:*`, and `thread:*` rows in the inventory.
- Current asset path: none by design.
- Required action: retain text-only unless the owner explicitly expands Stage B scope. These are not release failures.

### ART-P4-002: Extension/payload cleanup

- Exact scope: 79 physical rows where `FormatMatchesExtension=False`.
- Current paths: 20 `data/ams2/era-art/*.jpg`, 56 `data/ams2/portraits/*.jpg`, and 3 `data/ams2/smgp/teams/*.jpg` files that contain PNG payloads.
- Required action: decide a migration policy only after provenance and packaging are stable. Do not rename assets during `AUDIT_ONLY`.

### ART-P4-003: Manifest completeness

- Exact scope: all 335 physical rows; 332 currently require provenance review.
- Current manifest path for track art: `src/Companion.App/Assets/TrackBanners/prompts.json`.
- Missing fields: tool/model, method, seed, owner, license, creation date, revision, approval state, final crop roles.
- Required action: introduce the versioned Stage B art manifest before batch production.

## Stage B entry gate

This queue remains locked until the owner enters exactly:

`APPROVE NEWS/HISTORY ART PASS`

After approval, resolve `ART-P0-004` and the art-key/model seam before generating images. A small owner-reviewed reference slice should precede any large batch.
