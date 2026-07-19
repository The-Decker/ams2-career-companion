# SMGP-024 Initial Canon Audit

Mission: SMGP-024, complete 24-team canon lock + driver-swap alignment.
Date: 2026-07-18. Status: IN PROGRESS (inventory lanes running).

This audit is the mission's required starting inventory (brief §10). It records the
authoritative canon, the taxonomy used to classify every finding, and the complete
inventory of identity sources, consumers, and contamination points discovered in the
repository. Implementation follows this document; the completion status column is
kept current as lanes finish.

## 1. Authoritative canon (mission-supplied source of truth)

24 teams x 17 seasons = 408 team-season identity combinations. Team name, car name,
and engine name are permanent across all 17 seasons. Only the season/year changes.

| # | Team | Permanent car | Permanent engine | Engine shared with |
|---|---|---|---|---|
| 1 | MADONNA | MADONNA 456 | PALM 190 V10 | |
| 2 | FIRENZE | FIRENZE 500 | FIRENZE 99 V12 | |
| 3 | MILLIONS | MILLIONS 189 | DICK MD V10 | |
| 4 | BESTOWAL | BESTOWAL 167 | VAPOR DN | |
| 5 | BLANCHE | BLANCHE 582 | DELTA 103 V10 | |
| 6 | TYRANT | TYRANT 548 | LIZZIE 24 V8 | BULLETS, COMET |
| 7 | LOSEL | LOSEL 125 | VAPOR DNPQ V8 | DARDAN |
| 8 | MAY | MAY 555 | LORRY 32 V8 | LINDEN, RIGEL, ZEROFORCE |
| 9 | BULLETS | BULLETS 560 | LIZZIE 24 V8 | TYRANT, COMET |
| 10 | DARDAN | DARDAN 700 | VAPOR DNPQ V8 | LOSEL |
| 11 | LINDEN | LINDEN LN198 | LORRY 32 V8 | MAY, RIGEL, ZEROFORCE |
| 12 | MINARAE | MINARAE 594 | SEGA SG1000 V8 | |
| 13 | RIGEL | RIGEL 3000 | LORRY 32 V8 | MAY, LINDEN, ZEROFORCE |
| 14 | COMET | COMET 323 | LIZZIE 24 V8 | TYRANT, BULLETS |
| 15 | ORCHIS | ORCHIS 056 | MISFIRE 50 V8 | |
| 16 | ZEROFORCE | ZEROFORCE 231 | LORRY 32 V8 | MAY, LINDEN, RIGEL |
| 17 | JOKE | JOKE 777 | POND V8 | |
| 18 | LARES | LARES 92 | RAM V12 | MOON |
| 19 | FEET | FEET 13 | YOUGEN V10 | |
| 20 | SERGA | SERGA 1000 | SC3000 F12 | |
| 21 | COOL | COOL 05 | CORSE V8 | |
| 22 | MOON | MOON 292 | RAM V12 | LARES |
| 23 | IRIS | IRIS 717 | PRISM 90 V10 | |
| 24 | AZALEA | AZALEA 808 | BLOOM 88 V8 | |

Exactness rules (mission §5): ORCHIS 056 and COOL 05 keep leading zeros; VAPOR DN
carries no architecture suffix; SC3000 F12 is not SC3000 V12; DICK MD V10, MISFIRE
50 V8, POND V8, YOUGEN V10, LINDEN LN198, RIGEL 3000, SERGA 1000, ZEROFORCE 231,
SEGA SG1000 V8 are intentional canon, never normalized.

Alias policy: LOTUS -> IRIS only where an SMGP-scoped legacy record clearly means
IRIS (never real-world Lotus data, never the packs/f1-* historical packs). AZELIA,
AZALIA, AZALEAH, TEAM AZALEA, AZALEA RACING, AZALEA MOTORSPORT -> AZALEA on load,
import, migration, search, and repair. Aliases are never displayed as canon.

## 2. Finding taxonomy

Severity classes used in the inventory tables:

- C1 Critical canon violation (wrong/duplicated/missing canonical identity)
- C2 Persistence risk (display names or stale identities stored where ids belong)
- C3 Cross-mode leak (SMGP/Dynasty/Racing Passport content crossing)
- C4 Incorrect visible text (lore contradicting current seats or canon)
- C5 Missing content (dossier/capsule/season identity absent or placeholder)
- C6 Duplicate source of truth (same identity maintained in two+ places)
- C7 Test-only issue
- C8 Documentation issue

Completion states: Open / In lane / Fixed / Verified (test pinned).

## 3. Driver-swap alignment rule (the mission's triggering bug)

The winter standings reshuffle (SmgpGridReshuffle.ForNextSeason, shipped design per
docs/dev/smgp-design.md) moves drivers between physical cars for season 2+. Every
driver-facing surface must therefore resolve team identity from the CURRENT seat:

- Paddock cards: team label, team accent, car art (car art fixed 2026-07-18,
  a52e6b9), bio/epithet/quotes prose.
- News: article team references for a moved driver must use the current team.
- History: per-season records persist the team the driver raced THAT season;
  career pages show the current team as current.
- Events/story threads: rivalries and team references resolve against current seats.
- Season lore: opening canon describes season 1 seats; season 2+ prose generated
  from state must not assert season-1 seats as current fact.

## 4. Inventory

Four audit lanes (data files, code consumers, lore content, docs/tests) completed
2026-07-18. The findings below drive the implementation lanes; the Status column is
kept current as lanes finish.

### 4.1 Identity sources and registries

| Location | System | Current value | Canonical value | Severity | Action | Status |
|---|---|---|---|---|---|---|
| packs/smgp-1/teams.json | Pack roster | 24 teams, ids team.<name>, display names, vehicles | Matches canon 24/24 | - | None; parity pinned by SmgpCanonLockTests | Verified |
| packs/smgp-1/entries.json | Grid assignments | 34 entries (driver/team/number/livery) | Unchanged (season-1 seats) | - | None; reshuffle permutes by design | Verified |
| data/rules/car-specs.json | Car-spec cards | "MP4/5B"/"Honda V10" row backing team.iris + team.azalea; 4 generic Type G3-Mx rows for 22 teams | Canon names via registry overlay | C1 | CarSpecCatalog.WithSmgpCanon overlay (real-F1 rows kept for real-F1 careers) | Fixed |
| data/rules/smgp/canon.json | Canon registry | Created this mission: 24 teams, 17 engines, aliases, smgp-24-v1 | Is the registry | - | SmgpCanon model + Validate() + CareerRulesData wiring | Verified |
| data/rules/newsroom/038-smgp-canon.json | Divergence cards | "Canon" in the venue-legend sense, not identity | Unrelated | - | None | Verified |
| tools/author_smgp.cs, docs/PROJECT.md:274, packs/smgp-1/pack.json notes | Authoring/docs | "22 teams" (SEGA base) wording | "22 SEGA-base + 2 Kobra Fleetworks = 24" | C8 | Reword at docs lane | Open |

### 4.2 Contamination scan (SMGP scope)

| Location | Finding | Severity | Action | Status |
|---|---|---|---|---|
| data/rules/smgp/dispatches.json:111 | "out of F1 SMGP" in user-facing copy | C4 | Reworded to "out of the SMGP World Championship" | Fixed |
| data/rules/smgp/car-specs.json consumers | Real-world MP4/5B + Honda V10 shown on SMGP dossier cards | C1 | Canon overlay | Fixed |
| Lotus / Azelia / Azalia / Azaleah in data/packs SMGP scope | ZERO hits (Lotus hits are all packs/f1-* + liveries.json real-world, untouched by policy) | - | Alias normalization still lands for loads/imports/saves | In lane |
| Real-driver surnames / Ferrari / McLaren / Williams / FIA in SMGP lore prose | ZERO hits (fictional A. Senna is canon and stays) | - | None | Verified |
| packs/smgp-1/season.json realVenue fields | Real circuit names by design (Imola etc); Imola's full name contains "Ferrari" | - | By design (track mapping, not constructor lore) | Verified |
| packs/smgp-1 teams.json mclaren_mp45b vehicle ids | Technical AMS2 binding for Iris/Azalea, by design | - | None | Verified |

### 4.3 Driver-swap alignment map (the triggering bug)

Aligned today (verified, no work): paddock card TeamId/TeamName and car art
(a52e6b9), paddock team rosters, driver + constructor standings (persisted
ConstructorId folded from the reshuffled grid), starting grid (GridCarArtKeyForLivery),
result entry, calendar DNQ, wizard (season-1 authored, correct by definition),
promotion/demotion screen, season review offers, rival option lists, depth/stats
builders (driverId-keyed), persisted SmgpState (livery strings + driver ids, no
display names), debug season advance (normal fold path), history archive player
identity (explicitly current-team by design, labelled as such).

| Surface | File:line | Problem | Severity | Action | Status |
|---|---|---|---|---|---|
| Driver bios + quotes (34/34 name teams, colors, numbers, tiers, teammates as present-tense static prose; 28/34 quotes too) | data/rules/smgp/driver-profiles.json -> CareerSessionService.cs:2281-2283 -> PaddockView.xaml:152,299,319,550 | Season-1 seat asserted as current identity next to the reshuffled team label | C4 | Lore lane: time-stable rewrite (origin/personality framing, no present-tense seat claims) + card gains a live current-status line from state | In lane |
| Team histories name canon drivers as the house's drivers (Senna x24, Ceara x26 mentions) | data/rules/smgp/team-profiles.json -> SmgpTeamCard | Season-1 roster asserted as timeless | C4 | Reframe as season-1 opening canon in copy; roster block already live | In lane |
| Season lore AI pairings ("Bruno Salgado's wet-brave purple Iris") literal by design | data/rules/smgp/seasons.json + SmgpSeasonLore.cs:74-78 | Contradicts reshuffled seats wherever long-form lore surfaces | C4 | Opening-canon framing where surfaced; narrative bible already sanctions season-1 truth | In lane |
| Rival quotes name season-1 teams (12 hits) | data/rules/smgp/rival-quotes.json | Same class | C4 | Lore lane rewrite | In lane |
| Milestone dispatch {team} always names the CURRENT team, even for past-season beats (26 templates) | CareerSessionService.cs:3122,3141 | First-win-in-season-1 story in season 3 names the season-3 team | C4 | Fill {team} from the beat's per-round SeatTeamName | Open |
| World-story + newsroom AI-winner teams for PAST seasons resolve via the pinned season-1 pack | CareerSessionService.cs:3340-3347,3384,3396; CareerSessionService.Newsroom.cs:425-438 | Past-season team names/art season-1-static even when the driver moved | C4 | Resolve from the stored envelope entry's ConstructorId (folded truth) | Open |
| Skins view car art follows the reshuffled driverId (cars/<driverId>.png) | SkinsViewModel.cs:286-300 + SkinsView.xaml:318,389 | Same physical-car bug class as the paddock fix | C4 | Route through GridCarArtKeyForLivery | Open |
| Dossier TeamLine / PlayerTeamName() / PlayerCarSpec() | DossierViewModel.cs:508-511; CareerSessionService.cs:1863-1883 | CurrentTeamId updates only at season boundaries; stale after a mid-season seat swap | C4 | Resolve the team line from the live SMGP seat livery in SMGP careers | Open |

### 4.4 Content inventory (lore lane)

- 34/34 driver bios are 3-paragraph, finished, no placeholders; team-name mentions
  universal (2-8 teams per bio), colors/car numbers/tiers/teammates hard-coded.
- 24/24 team profiles: motto + 5-paragraph history (avg ~525 words) + 4 quotes; no
  roster field (joined live). Canon driver names inside histories.
- 17/17 season identities: title/subtitle/era/overview/preseason/technical/safety/
  themes/timeline/arcs/hooks/contenders/milestones; ~22.5k words; {playerTeam} token
  with WithPlayerTeam projection. Gen-3/G3Mx chassis vocabulary only; zero
  car/engine canon names (greenfield before this mission).
- what-really-happened.json: 16 venue almanac entries, pre-career historical fiction,
  aligned by design.
- News: smgp.json era corpus (8 pools, 10 body keys, smgp+default variants),
  dispatches.json (24 template groups), newsroom packs (264 templates, era-switched
  pools, no mode field; routing via PreferredEra="smgp" sentinel years 9000-9099;
  ModeNarrativeIsolationTests guard separation).
- Placeholders: none anywhere in SMGP content (only legitimate isPlaceholder round
  flags for 3 reproduced circuits).
- Car dossiers / engine dossiers / 408 team-season capsules: DO NOT EXIST yet.
  Greenfield authoring lanes.

### 4.5 Tests and docs state

- SmgpWorldCompletenessTests pins 24 teams/34 drivers/34 entries/16 rounds + art.
  SmgpCanonLockTests (this mission) pins the 408 identity lock, exactness rules,
  alias normalization, pack parity, registry Validate().
- Lane rules (docs/dev/codex-head-of-coding.md / codex-head-of-gui.md): logic in
  Core/ViewModels/Data, presentation in App. This mission runs under direct owner
  instruction with both lanes coordinated here, as with the playtest batch.
- docs/reports/ created for the final report.
