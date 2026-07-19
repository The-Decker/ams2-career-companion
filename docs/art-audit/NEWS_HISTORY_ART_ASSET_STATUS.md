# News and History Art Asset Status

Mission: `SMGP-NEWS-HISTORY-ART-001`  
State: `AUDIT_ONLY`  
Audit date: 2026-07-18  
Branch: `hub/increment-4`

This report is the Stage A factual record. No image was generated, edited, recolored, upscaled, renamed, deleted, downloaded, or remapped. No News or History canon or fallback behavior was changed. No commit or push was made.

## 1. Executive summary

The audit found two release-blocking system gaps.

1. Rich newsroom and legacy journal articles have no article-specific art field at the model/projection seam. The active lead, card, and reader surfaces therefore display the explicit News placeholder for 339 finite production mappings: 264 newsroom templates, 70 era/body mappings, and 5 headline-only families.
2. A fresh self-contained publish succeeds, but 20 of the 25 loose `data/ams2/era-art` files are absent from the publish output. The project items declare build copying but do not carry a reliable publish-copy contract.

The History system has 60 expected year-keyed image files and none of them exist. Seventeen SMGP season-lore records also lack an art field. The 16 fixed SMGP round images are valid for the authored season-one almanac, but their reuse for 256 later-season generated race slots is context-unknown because the archive binds by round position rather than the actual shuffled venue.

No production career or demo database ships in the repository. Consequently, runtime-created article instances cannot be counted globally. The News total in this report is the finite, reproducible set of shipping template, family, and thread art requirements, not a fabricated count of unbounded generated instances.

At audit start the tree already contained unrelated work in:

- `src/Companion.ViewModels/Start/StartViewModel.cs`
- `tests/Companion.Tests/ViewModels/CareerModeMenuTests.cs`
- `tests/Companion.RenderHarness.Tests/StartViewCommandRailRenderTests.cs`

Those files were not touched. During the audit, another process advanced `HEAD` from `cbc5e94` to `eee17c9` and the three unrelated paths became clean. The branch remained `hub/increment-4`.

## 2. Exact global totals

| Metric | Exact total |
|---|---:|
| Content-to-art rows | 2,559 |
| News records audited | 370 |
| History records audited | 2,189 |
| Procedural article families audited | 107 |
| Newsroom templates | 264 |
| Newsroom event kinds | 68 |
| Newsroom event kinds with templates | 66 |
| Frozen headline families / variants | 14 / 605 |
| Legacy article family union | 15 |
| Legacy era/body mappings | 70 |
| SMGP dispatch families | 24 |
| Story-thread families | 7 |
| Physical art files audited | 335 |
| Source files with unique SHA-256 values | 335 |
| Valid unique assets after publish validation | 315 |
| Missing physical files | 60 |
| Missing asset fields | 356 |
| Broken configured references | 0 |
| Explicit placeholder mappings | 339 |
| Suspected placeholder files | 0 |
| Generic fallback mappings | 645 |
| Exact duplicate groups | 0 |
| Suspected near-duplicate groups | 7 |
| Packaging failures | 20 |
| Orphan files in the audited roots | 0 |
| Corrupt or undecodable files | 0 |
| Extension/payload format mismatches | 79 |
| Assets requiring provenance or licensing review | 332 |
| News mappings requiring new art | 363 |
| History mappings requiring new or revised art | 103 |

Definitions:

- A News record is one finite shipping template, era/body mapping, headline-only family, dispatch family, or thread-container art requirement. It is not one runtime article instance.
- A missing physical file is an explicitly expected shipping slot with no source file. A missing asset field is a content model with no configured art reference.
- A broken reference is a configured path that is corrupt, wrong-case, URI-invalid, or unloadable. Missing paths and publish omissions are counted separately.
- A valid unique asset is a unique SHA-256 image that decodes, is referenced, has no placeholder signal, and is present byte-for-byte in the checked publish or is a verified embedded resource. This is not an editorial or provenance approval.

## 3. Seventeen-season coverage matrix

The only source-defined SMGP season identity is `ordinal`. Every campaign season reuses production `packId=smgp-1`. The audit displays `ordinal:N` and the runtime year derived from the 1990 base. It does not invent 17 production pack IDs.

### 3.1 Coverage and reuse

| Ordinal | Year | Exact title | News | Authored News | Generated templates | History | Art slots | Valid images | Approved reuse | Generic | Placeholder |
|---:|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 1 | 1990 | The Tenth Summer | 0 | 0 | 0 | 17 | 17 | 16 | 16 | 0 | 0 |
| 2 | 1991 | The Protest Year | 0 | 0 | 0 | 17 | 17 | 16 | 0 | 16 | 0 |
| 3 | 1992 | The Wet Season | 0 | 0 | 0 | 17 | 17 | 16 | 0 | 16 | 0 |
| 4 | 1993 | The Closing Door Opens | 0 | 0 | 0 | 17 | 17 | 16 | 0 | 16 | 0 |
| 5 | 1994 | The Horsepower Spring | 0 | 0 | 0 | 17 | 17 | 16 | 0 | 16 | 0 |
| 6 | 1995 | The Temple Wars | 0 | 0 | 0 | 17 | 17 | 16 | 0 | 16 | 0 |
| 7 | 1996 | The Spending War | 0 | 0 | 0 | 17 | 17 | 16 | 0 | 16 | 0 |
| 8 | 1997 | The Boiling Point | 0 | 0 | 0 | 17 | 17 | 16 | 0 | 16 | 0 |
| 9 | 1998 | The Reckoning | 0 | 0 | 0 | 17 | 17 | 16 | 0 | 16 | 0 |
| 10 | 1999 | The Charter Season | 0 | 0 | 0 | 17 | 17 | 16 | 0 | 16 | 0 |
| 11 | 2000 | The Craftsman's Year | 0 | 0 | 0 | 17 | 17 | 16 | 0 | 16 | 0 |
| 12 | 2001 | The Frost Blooms | 0 | 0 | 0 | 17 | 17 | 16 | 0 | 16 | 0 |
| 13 | 2002 | The Veterans' Autumn | 0 | 0 | 0 | 17 | 17 | 16 | 0 | 16 | 0 |
| 14 | 2003 | The Jewel Formula | 0 | 0 | 0 | 17 | 17 | 16 | 0 | 16 | 0 |
| 15 | 2004 | The Insurgent's Last Climb | 0 | 0 | 0 | 17 | 17 | 16 | 0 | 16 | 0 |
| 16 | 2005 | The Silver Jubilee | 0 | 0 | 0 | 17 | 17 | 16 | 0 | 16 | 0 |
| 17 | 2006 | The Crown of Crowns | 0 | 0 | 0 | 17 | 17 | 16 | 0 | 16 | 0 |

### 3.2 Defects and completion

| Ordinal | Missing | Broken | Not published | Resolution/aspect | Exact/near duplicate mappings | New art | Complete coverage |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 1 | 0 | 0 | 0 | 0 | 0 | 1 | 94.1% |
| 2 | 0 | 0 | 0 | 0 | 0 | 1 | 0.0% |
| 3 | 0 | 0 | 0 | 0 | 0 | 1 | 0.0% |
| 4 | 0 | 0 | 0 | 0 | 0 | 1 | 0.0% |
| 5 | 0 | 0 | 0 | 0 | 0 | 1 | 0.0% |
| 6 | 0 | 0 | 0 | 0 | 0 | 1 | 0.0% |
| 7 | 0 | 0 | 0 | 0 | 0 | 1 | 0.0% |
| 8 | 0 | 0 | 0 | 0 | 0 | 1 | 0.0% |
| 9 | 0 | 0 | 0 | 0 | 0 | 1 | 0.0% |
| 10 | 0 | 0 | 0 | 0 | 0 | 1 | 0.0% |
| 11 | 0 | 0 | 0 | 0 | 0 | 1 | 0.0% |
| 12 | 0 | 0 | 0 | 0 | 0 | 1 | 0.0% |
| 13 | 0 | 0 | 0 | 0 | 0 | 1 | 0.0% |
| 14 | 0 | 0 | 0 | 0 | 0 | 1 | 0.0% |
| 15 | 0 | 0 | 0 | 0 | 0 | 1 | 0.0% |
| 16 | 0 | 0 | 0 | 0 | 0 | 1 | 0.0% |
| 17 | 0 | 0 | 0 | 0 | 0 | 1 | 0.0% |

The 17 rows contain 289 season-bound History mappings and no season-bound News mappings. Shipping News templates are shared and are materialized only when a saved career emits events. The reconciled non-season/shared row is 2,270 mappings: 370 News and 1,900 History. Therefore `289 + 2,270 = 2,559`, `0 + 370 = 370 News`, and `289 + 1,900 = 2,189 History`.

Later-season coverage is 0.0% because the 16 images are generic round-position reuse with unknown venue correctness, not because the files fail to decode. Season 1 has 16 approved canonical round mappings plus one missing season-lore art slot.

## 4. News coverage summary

| Production source | Rows | Art result |
|---|---:|---|
| Living newsroom templates | 264 | No art field; explicit placeholder on active image surfaces |
| Legacy era/body mappings | 70 | No art field; explicit placeholder |
| Legacy headline-only families | 5 | No art field; explicit placeholder |
| SMGP dispatch families | 24 | Reuses driver/team/car identity art when context exists; no editorial art |
| Story-thread containers | 7 | Intentionally text-only |
| Total | 370 | 363 require new art |

The frozen headline bank contains 14 families and 605 variants. Nine keys pair with the legacy body bank; five headline-only keys need their own rows. The legacy body bank contains 70 era declarations, 10 distinct body keys, and 794 variants. The living newsroom contains 264 stable template IDs in 12 packs and 66 event families.

The active News UI is the unified page. The old legacy feed is hard-collapsed. No shipping database stores materialized newsroom articles, so the audit does not multiply template rows into fictional instance counts.

## 5. History coverage summary

| History subtype | Rows | Current treatment |
|---|---:|---|
| Verified historical seasons | 60 | Expected year art is missing |
| Verified historical rounds | 1,008 | Intentionally text-only |
| Computed driver profiles | 460 | Intentionally text-only |
| Computed team profiles | 110 | Intentionally text-only |
| Computed circuit profiles | 139 | Intentionally text-only |
| Authored history subjects | 23 | Intentionally text-only |
| Authored eras | 8 | Generic year/medium era art; all need dedicated review |
| SMGP season lore | 17 | Missing art field |
| SMGP canonical venue histories | 16 | Unique round images, provenance review |
| SMGP generated race slots | 272 | 16 approved in season 1; 256 later-season mappings context-unknown |
| SMGP career-beat families | 18 | Generic subject portrait reuse |
| SMGP driver history | 34 | Identity portrait reuse, provenance review |
| SMGP team history | 24 | Team identity art, provenance review |
| Total | 2,189 | 103 require new or revised art |

The 103 production targets are 60 verified-season images, 17 SMGP season-lore identities, 18 career-beat family pools, and 8 dedicated era images. Another 256 generated race mappings require a truthful venue/season selector before their art can be approved; they are not counted as immediate new-image requirements because the missing input data prevents a sound production specification.

## 6. Authored or canonical article summary

There are no persisted, fully materialized authored News articles with stable article records in the shipping repository. The 264 newsroom entries are authored templates with stable IDs but generate event-specific articles at runtime. The 70 legacy rows and 5 headline-only rows are authored copy banks feeding generated journal stories. Stage B must not claim one bespoke image per runtime article until the owner chooses whether uniqueness is per template, per family/context pool, or per materialized event.

Finite authored/canonical History includes:

- 17 SMGP season records, with 142 timeline lines, 205 hooks, 102 contender arcs, 100 themes, and 78 milestones.
- 16 SMGP almanac venue records.
- 8 verified-history era essays and 23 verified-history subject essays.
- 60 verified seasons and 1,008 verified rounds from the historical dataset.

Most detailed SMGP season fields are authored and shipping but are not currently exposed as separate News or History entries. The inventory assigns one season-level art requirement per ordinal rather than pretending every hidden prose fragment is a UI placement.

## 7. Procedural article-family summary

The reproducible procedural-family total is 107:

- 68 `NewsEventKind` values.
- 15 distinct legacy journal family keys across the headline and body banks.
- 24 SMGP dispatch families.

The 264 newsroom templates cover 66 of 68 event kinds. `RivalryDeveloped` and `DnqDrama` have no templates and are not currently emitted by the main detector; if emitted, composition returns no article. Text selection is deterministic. Newsroom identity is derived from the event dedupe key. Legacy selection is deterministic by era and named RNG stream. SMGP dispatch keys include an emission counter and are deterministic for the current detector version but are not append-stable if earlier detections change.

There are also 7 stateful thread families and fact-backed rumor records. They are intentionally text-only containers, not added to the 107 article-family total.

Current editorial-art pool sizes are zero for newsroom and legacy families. The SMGP dispatch pool is the existing identity library, not an editorial pool. Stage B should select art deterministically from stable article ID, event key, season ordinal, category, team, driver, and venue, with a deterministic adjacent-card deconfliction step. Runtime downloading is not appropriate.

## 8. Missing-asset report

Exactly 60 physical files are missing: one `data/ams2/history-art/<year>.(jpg|jpeg|png)` slot for every verified historical season from 1967 through 2026.

Exactly 356 content mappings lack an art field:

- 264 newsroom templates.
- 70 legacy era/body mappings.
- 5 legacy headline-only families.
- 17 SMGP season-lore records.

No production demo/save database exists, so there is no additional materialized-instance inventory to scan.

## 9. Placeholder report

Exactly 339 mappings are verified explicit-placeholder cases: `264 + 70 + 5`. The placeholder is behavioral, not filename-based: the active News card, lead, and reader templates always draw a neutral placeholder frame beneath optional image overlays, while the corresponding projections supply no art keys.

No physical file triggered the filename or near-uniform placeholder heuristics. This is not a claim of zero placeholders. It means zero placeholder files and 339 confirmed placeholder mappings. SMGP dispatch instances can also expose the frame when no usable subject/team key exists, but runtime instances cannot be counted without a career database.

## 10. Generic fallback report

The 645 generic fallback mappings reconcile as follows:

- 339 News mappings with no art field and the universal placeholder.
- 24 SMGP dispatch families using identity art or the placeholder.
- 256 later-season SMGP race slots reusing fixed round-position images.
- 18 SMGP career-beat families using subject portraits when available.
- 8 historical era cards using a start-year image or Telegram/Fax/Email medium fallback.

The most serious fallback is not a single image file. It is the absence of an editorial-art contract from the article models and projection.

## 11. Broken-reference report

Broken configured references: 0. Corrupt or unreadable files: 0. Wrong-case paths found: 0. All 335 inventoried files decode. The 304 track keys resolve to 173 embedded masters with no missing or orphan masters.

`TrackArtKey` exists on the News view model but no production projection assigns it. This is an unreachable capability, not a broken configured reference. A nonempty key whose file fails to decode can also leave the placeholder visible because the `Has*` properties test key presence rather than decoder success.

## 12. Packaging failure report

A fresh Release, win-x64, self-contained publish to `scratchpad/art-audit-publish-final` omitted exactly these 20 source assets:

`data/ams2/era-art/1967.jpg`, `1969.jpg`, `1974.jpg`, `1978.jpg`, `1985.jpg`, `1986.jpg`, `1988.jpg`, `1990.jpg`, `1991.jpg`, `1992.jpg`, `1993.jpg`, `1995.jpg`, `1997.jpg`, `2000.jpg`, `2005.jpg`, `2006.jpg`, `2008.jpg`, `2016.jpg`, `2020.jpg`, and `smgp.jpg`.

The other 5 era files, all 58 portraits, all 34 cars, all 24 SMGP team files, and all 16 SMGP round files match source byte-for-byte in the clean publish. All 173 track banners and 5 era texture masters are embedded resources and the published executable exists.

`Companion.App.csproj` declares the loose art roots with `CopyToOutputDirectory` but without a dependable `CopyToPublishDirectory` contract. There is no `news-art` root or glob. A new Stage B asset folder would not ship until packaging metadata is added and tested.

## 13. Duplicate and reuse report

All 335 source files have distinct SHA-256 hashes. Exact duplicate groups: 0.

The deterministic 64-bit dHash pass produced 7 warning-only connected groups: 5 car-cutout groups and 2 player-portrait groups. A supplemental stricter pHash plus dHash review found 31 pair edges in 6 car-silhouette components, no cross-family candidates, and no strict non-car same-family candidates. The car library intentionally uses a small body-template set, so these are `SUSPECTED_NEAR_DUPLICATE` candidates rather than proof of duplication. No file is classified as an exact duplicate.

Intentional physical reuse is separate from duplicate files:

- 304 track keys intentionally map to 173 venue masters; 74 masters serve more than one layout key.
- The 16 SMGP round files are approved for 16 season-one almanac/race mappings.
- The same 16 files are reused for 256 season 2-17 race slots without venue-context proof.

## 14. Resolution and aspect-ratio report

| Family | Files | Dimensions |
|---|---:|---|
| Era art | 25 | 21 at 1672x941; 3 at 1280x720; SMGP at 1800x874 |
| Portraits | 58 | 56 at 1254x1254; 2 at 1024x1024 |
| Car cutouts | 34 | 900x281 with transparency |
| SMGP rounds | 16 | 800x450 |
| SMGP teams | 24 | 1447-1672 wide by 941-1087 high |
| Track banners | 173 | 1920x440 |
| Embedded era texture masters | 5 | 128x128 or 480x96 |

No editorial/identity/panorama raster is below 800x440. There is no simple low-resolution failure. The crop risk is structural:

- 23 of 24 team images are approximately 4:3, while major News placements are wide.
- News reuses one source across art heights from 104 to 280 pixels with `UniformToFill` and no focal-point metadata.
- Car cutouts are approximately 3.20:1 and use `Uniform`, making them overlays rather than editorial heroes.
- History year art uses `Uniform` and scales down only, while News heroes crop with `UniformToFill`.

Seventy-nine files contain PNG payloads under `.jpg` names: 20 era files, 56 portraits, and 3 team images. They decode, but the mismatch undermines format policy, package-size expectations, and provenance auditing.

## 15. UI crop and presentation report

### News

- Lead story: responsive full width, art height 280/210/120, `UniformToFill`; portrait branch fixed at 330 pixels.
- Secondary cards: width 280-480, art height 104/154/190, `UniformToFill`; portrait branch 126 pixels.
- Archive and bookmarks: 132-pixel image column, team/driver only.
- Reader: 250-pixel track/team/portrait hero with `UniformToFill`; car overlay uses `Uniform` at 390 pixels.
- Tear-off window: 560x720, minimum 420x360, and hosts the same view/model, so the same crop and fallback behavior applies.
- Threads and rumor rail: text-only.
- Related stories: navigation button only, no related-story art placement.
- The retired legacy feed is hard-collapsed and not reachable.

### History

- The year-keyed historical panel uses `history-art/<year>`, `Uniform`, and collapses when no file exists.
- Snapshot hero and latest dispatches reuse current identity art.
- Timeline events can show a 66x66 SMGP subject portrait only.
- Race archive cards use `smgp/rounds/<round>` and are incorrectly visibility-gated through current-car state.
- Featured/browse era imagery is generic era art. Driver and team features are text-only.
- Driver, team, circuit, record, search, and timeline-detail results have no raster editorial art.
- Death, sit-out, and SMGP career-over views have no editorial image element. Fatality News uses the same generic placeholder behavior.
- No PDF, yearbook, season-book, print, XPS, or chronicle image export implementation exists.

Existing render tests validate geometry and layout, not decoded source selection, semantic correctness, crop focal points, or publish inclusion.

## 16. Wrong-context and canon-mismatch report

No configured file was proven to show the wrong named driver, team, or event because article-specific mappings do not exist. That absence must not be treated as approval.

Confirmed context defects:

- 256 season 2-17 SMGP race slots use round-position art despite shuffled calendars. Venue correctness is unknown and must be resolved before approval.
- Historical career race records do not preserve the historical seat/team/car identity. The History projection copies the player's current car/team context to every archived race, so truthful past identity art cannot be assigned.
- `HistoryDiverged` combines verified historical comparison with simulated outcome but is labeled only `CareerUniverse`; it is hybrid provenance.
- Legacy News, SMGP dispatch, and career History records carry no full provenance contract.

The verified-history archive and SMGP fiction have distinct UI provenance labels in the rich newsroom. That separation is a load-bearing requirement for Stage B. No direct canon rewrite or confirmed canonical-image mismatch was introduced or concealed by this audit.

## 17. Orphan-asset report

Orphan assets in the audited roots: 0. The manifest correction accounts for all 173 embedded track masters. All loose era, portrait, car, SMGP team, and SMGP round assets are reached through finite rows or documented dynamic key conventions.

SMGP logos and rival banners were excluded from the physical union because News and History do not resolve them. They are not labeled orphans. `dist` copies were compared for byte equality but were not counted as separate source assets.

## 18. Provenance and licensing review

Exactly 332 of 335 inventoried files require provenance or licensing review. Only `telegram.jpg`, `fax.jpg`, and `email.jpg` have a repository-level statement that they are original generated flat-art compositions with no external source.

The 173 track masters have complete key and scene-cue coverage, but their prompt records lack model/tool, seed, method, owner, license, creation date, revision, and approval fields. Era art, portraits, cars, teams, rounds, and history art have no equivalent per-file provenance manifest. Embedded metadata is effectively absent.

Priority review areas:

- Historical-looking era imagery and real-person likenesses.
- Car cutouts derived from installed skins or liveries.
- Team and driver images containing sponsor, manufacturer, trademark, helmet, or livery identity.
- Track imagery and any source reference used to create it.

The real-history dataset's CC BY 4.0 data provenance does not grant rights to unrelated photographs or artwork. No external photograph, logo, livery, or mod asset was downloaded during this mission.

## 19. Existing visual-direction assessment

The documented approved direction is `period-authentic minimalism`: clean era-medium presentation, muted period palettes, one signature accent per era, and a hard provenance distinction between historical record, career universe, and SMGP universe. Telegram, Fax, and Email styling primarily comes from typography, chrome, and texture rather than bitmap-heavy themes.

The measurable asset language is mixed:

- Track banners are consistent panoramic trackside establishing views at 1920x440. Their scene cues require recognizable venue character and forbid text, logos, HUD, borders, and baked gradients.
- SMGP round art is a consistent 16:9 family at 800x450.
- Driver/player portraits are square identity art.
- Car assets are transparent, very wide cutouts with repeated body silhouettes.
- Team art is mostly 4:3 and is poorly aligned with wide editorial crops.
- Era art is mostly 16:9-ish and changes by selected historical year or broad medium, not by all 17 SMGP chapters.

Strongest technical reference families:

- `src/Companion.App/Assets/TrackBanners/masters/*.jpg` for consistent dimensions, manifest mapping, safe panoramic intent, and no baked text.
- `data/ams2/smgp/rounds/1.jpg` through `16.jpg` for a uniform 16:9 series.
- `data/ams2/era-art/smgp.jpg` as a candidate SMGP identity anchor, pending visual and provenance approval.

Assets that should not set the Stage B editorial ceiling:

- `data/ams2/era-art/telegram.jpg`, `fax.jpg`, and `email.jpg`, because they are intentional generic medium fallbacks.
- Car and player-helmet families alone, because template repetition is part of their identity role.
- Team photos without crop review, because 23 of 24 are approximately 4:3.
- `data/ams2/history-art`, because it contains no images.

A firsthand raster review could not be completed in this run. Both the Codex image viewer and the Node image-emission path failed with `windows sandbox failed: helper_unknown_error: apply deny-read ACLs`, including for unchanged temporary copies in the writable visualization directory. The assessment above is therefore grounded in dimensions, formats, manifests, prompts, UI behavior, and approved design documents, and is explicitly not a claim that every image's composition or color treatment was visually approved.

## 20. Prioritized production queue

The detailed queue is in `docs/art-audit/NEWS_HISTORY_ART_PRODUCTION_QUEUE.md`. Its exact content IDs and current paths are backed by `news_history_art_inventory.csv`.

| Priority | Exact scope | Gate |
|---|---:|---|
| P0 | 339 explicit News placeholders; 20 publish omissions | Model/projection seam and publish-copy contract first |
| P1 | 60 historical-season images; 17 season-lore images; 24 dispatch families | Owner policy and contextual pools |
| P2 | 256 later-season race mappings; 8 era mappings; provenance/context repair | Preserve venue, season, team, and driver truth |
| P3 | 18 beat families; 7 heuristic near-duplicate groups; team crop review | Repetition and editorial quality |
| P4 | 1,747 intentionally image-less mappings; 79 format mismatches | Owner-approved polish only |

## 21. Exact Stage B recommendation

Do not start bulk art generation immediately after approval. Stage B should begin with one deterministic integration slice:

1. Define a versioned editorial-art manifest with stable asset ID, content ID, art role, context filters, crop/focal data, provenance, license, source method, revision, and approval state.
2. Add persisted art selection to rich newsroom, legacy journal, SMGP dispatch, and the required History models. Preserve newsroom event dedupe identity. Replace the order-sensitive dispatch identity before relying on it for art.
3. Carry track/layout ID, subject ID, team ID, driver ID, season ordinal, and historical seat identity through projection. Do not infer venue from display text or past identity from current team/car.
4. Add an explicit packaged `news-art`/editorial root and `CopyToPublishDirectory`; add clean-publish validation.
5. Implement deterministic selector/fallback levels from stable content data and prevent adjacent repetition without changing an article's selected art on reopen.
6. Produce a small owner-review reference set across News lead/card/reader and History year/race/timeline crops before scaling to 363 News and 103 History production targets.
7. Rerun this audit after every approved batch. P0 and P1 must reach zero before Stage B completion.

All runtime assets must remain local and offline.

## 22. Open issues and genuine unknowns

- No production career/demo database exists, so actual emitted article, rumor, thread, beat, and race-instance counts are unknown.
- The owner must decide whether generated-art uniqueness is per template, per contextual pool, or per materialized event. The current repository does not define that policy.
- Newsroom input retains venue text but not a trustworthy track/layout ID.
- Historical career records do not retain past seat/team/car identity.
- Five headline-only legacy families can reach the unified wire without body-bank rows.
- `RivalryDeveloped` and `DnqDrama` have no newsroom templates and are currently not emitted.
- Hybrid divergence provenance has no exact production enum value.
- SMGP dispatch identity is deterministic for the current detector but not append-stable.
- Visual approval of individual rasters remains open because the local image-inspection sandbox failed.
- The branch moved concurrently from `cbc5e94` to `eee17c9`; validation was repeated after the move.
- The exact ownership/license state of 332 files remains unverified.

## 23. Audit commands and validation methods

Primary repeatable commands:

```powershell
# Generate inventories while retaining the full issue ledger.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\audit-news-history-art.ps1 `
  -PublishDirectory .\scratchpad\art-audit-publish-final -AllowIssues

# Release gate: intentionally returns nonzero while required files or package assets are missing.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\audit-news-history-art.ps1 `
  -PublishDirectory .\scratchpad\art-audit-publish-final
```

Validation performed:

- Parsed every production News/History JSON source under `data/rules` and `data/history`; excluded test fixtures and RenderHarness stand-ins from production totals.
- Tested every allowlisted physical file with System.Drawing decoding, dimensions, alpha flag, SHA-256, 64-bit dHash, source/dist comparison, reference count, and clean-publish comparison.
- Supplemental WPF decode and pHash/dHash review covered the 330 editorial/identity/panorama rasters. All 330 decoded; the 5 additional embedded era texture masters also decoded in the primary audit.
- Reconciled 2,559 content rows, 370 News rows, 2,189 History rows, 289 season-bound rows, and 2,270 shared/non-season rows.
- Reran the four generated machine files and confirmed byte-identical SHA-256 output.
- `dotnet build Companion.slnx -c Release`: passed, 0 errors, 4 existing warnings.
- `dotnet test Companion.slnx -c Release --no-build`: passed, 2,885 core tests plus 246 RenderHarness tests, 3,131 total.
- `dotnet publish src\Companion.App\Companion.App.csproj -c Release -r win-x64 --self-contained true -o scratchpad\art-audit-publish-final`: passed; package then failed the art audit on 20 omissions.
- Strict audit exit: 1, as designed, reporting 80 missing-file or packaging issues (`60 + 20`). Missing asset fields are reported but do not inflate that physical release-gate sum.
- Existing News/History render tests passed. They are geometry smoke tests and do not prove semantic art selection or raster crop quality.

## Generated deliverables

- `docs/art-audit/NEWS_HISTORY_ART_ASSET_STATUS.md`
- `docs/art-audit/NEWS_HISTORY_ART_PRODUCTION_QUEUE.md`
- `docs/art-audit/news_history_art_inventory.csv`
- `docs/art-audit/news_history_art_inventory.json`
- `docs/art-audit/news_history_physical_asset_inventory.csv`
- `docs/art-audit/news_history_art_audit_summary.json`
- `tools/audit-news-history-art.ps1`