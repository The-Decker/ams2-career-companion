# Contradictions and Blockers

Mission: `SMGP-ART-LORE-PREFLIGHT-001`
State: `RESEARCH_AND_PLANNING_ONLY`
Date: 2026-07-18

This register lists every contradiction, ambiguity, and risk found while building the
eight lore packets, how each is handled, and what would flip a packet from READY to
BLOCKED. **Current state: zero BLOCKED packets.** No contradiction below is unresolved;
each is either answered with evidence or engineered around, and the engineering is stated
openly in the packet.

## A. Resolved contradictions (evidence settled them)

### A1. Bestowal livery colors: research disagreement, resolved

- One research pass reported Bestowal has no livery color prose; another quoted colors.
  Direct verification settles it: `data/rules/smgp/team-profiles.json:62` describes the
  Bestowal machine as "a gunmetal-and-silver missile with a great red 8 on its flanks".
- Handling: colors ARE authored. Packet 6 still keeps the failing car team-neutral, so the
  point is context-only. The episode is recorded because it demonstrates why every packet
  field carries a citation.

### A2. Ordinal-16 versus ordinal-17 motif conflation

- Heritage liveries in every garage and Dufay's retirement farewell tour belong to the
  Silver Jubilee, ordinal 16 (`data/rules/smgp/seasons.json:967-1026`), not to the Crown
  of Crowns. An early draft of packet 3 borrowed them.
- Handling: packet 3 now uses only ordinal-17-native motifs: the chaptered finale
  (:1033,1038,1068), the eve declaration (:1046), the Monaco last flag (:1031,1075), the
  sealed prize (:1042,1079), and the Charter-era relit tunnel (:1036).

### A3. The almanac's Great Wall Sunday shows a pace car the timeline forbids

- The almanac memory of the (first) Great Wall Sunday includes "the pace car crawling"
  (`data/rules/smgp/what-really-happened.json:221`), but standing pace-car law arrives
  only with the 1998 Safety Charter (`seasons.json:537`), and the 1992 proposal was
  "shouted down within a fortnight" (season 3 timeline, `seasons.json` seasons[ordinal=3]).
  The two Sundays are different events (the 1998 one is explicitly the SECOND,
  `seasons.json:520`), which narrows but does not erase the tension: a pace car in a
  pre-Charter memory still contradicts the "no standing law" framing.
- Handling: packet 2 shows the 1998 scene entirely car-free. No pace car appears in any
  pre-Charter frame. The tension itself is reported here rather than smoothed over.

### A4. Tunnel lighting is era-dependent

- Monaco's tunnel is dark in the rivalry-era frame but "relit end to end" in the Charter
  years (`seasons.json:582`) and described as relit in 2006 (`seasons.json:1036`).
- Handling: packet 5 (pre-Charter frame) uses the dark tunnel; packet 3 (2006) uses a
  warm, lit tunnel exit; packet 1 avoids the tunnel motif entirely.

## B. Handled ambiguities (no evidence; engineered around, never guessed)

### B1. Senna likeness: highest risk in the set

- `driver.ayrton_senna` is a real, deceased public figure with an actively managed estate,
  named in full inside files whose `$comment` claims "Fully fictional SEGA-universe
  characters" (`data/rules/smgp/driver-profiles.json:2` versus drivers[0]). Canon forbids
  removing him (`docs/dev/smgp-design.md:28-32`).
- Handling: abstraction only, in packets 1, 3, and 5: car, number, and team palette, never
  face, helmet, or McLaren-era trade dress. Madonna's yellow/red stays parody, never
  Marlboro chevrons (`smgp-design.md:116-117`).

### B2. Sim weather is independent of lore weather

- Season 1 is always Clear (pack-authored). Seasons 2-17 receive seeded per-round weather
  characters independent of the lore (`docs/SMGP_17_SEASON_STRUCTURE.md:68`). Lore rain or
  heat is narrative, not sim truth.
- Handling: authored weather is used only as almanac/lore illustration (packets 4, 6) and
  labeled as such; every other packet marks weather UNKNOWN.

### B3. In-world Gen evolution has no visual counterpart

- Gen-3, Gen-4, Gen-4B, and Gen-5 exist only as prose; all 17 seasons drive F-Classic_Gen3
  hardware (`data/ams2/classes.json`; `packs/smgp-1/teams.json`).
- Handling: season identity is carried by scene and palette, never by evolving car shapes.
  Packet 2 is car-free; packets 1, 3, 5 use only the sanctioned g3m1/g3m4 silhouettes.

### B4. Badge fall-through on SMGP newsroom stories

- SMGP-era newsroom templates (packets 5, 6, 7) badge CAREER UNIVERSE even inside SMGP
  careers, because `ProvenanceFor` falls through
  (`src/Companion.Core/Newsroom/NewsroomComposer.cs:247-253`). The reference scenes are
  SMGP fiction.
- Handling: the manifest records `careerUniverse` with SMGP-scoped context filters and
  documents the pairing so a later audit does not misread it as an error. The badge is a
  code fact and is not "corrected" in art metadata.

### B5. The works-engine supplier is never named

- Canon says only "establishment power" (`seasons.json:454,467`).
- Handling: packet 7 shows an unbranded, tarped crate; no marque, badge, or branded prop.

### B6. Coronation versus maiden win (packet 4 caveat)

- `milestone.first-win` celebrates a maiden victory, but the reference scene is a
  documented coronation by a driver who entered season 1 with six career wins
  (`seasons.json:9`). Canon documents the win, the venue, the rain, and the dominance; it
  does not document a maiden win.
- Handling: the binding is art-direction-level only; no "maiden" or "first" visual claim
  is permitted in the packet.

### B7. Dispatch identity is not append-stable

- SMGP dispatch keys are deterministic for the current detector but not append-stable
  (`docs/art-audit/NEWS_HISTORY_ART_ASSET_STATUS.md:389`).
- Handling: packets 4 and 8 key art from stable content data (family, venue, subject
  class), never from dispatch order.

### B8. Numbering collision for captions

- Campaign ordinal (1-17), runtime year (1990-2006), and in-world World Championship count
  (10th-26th, offset +9) are three different numbers for the same season.
- Handling: every packet states all three where relevant; captions must pick one system
  and label it.

### B9. Em-dash escape loophole in canon JSON

- `data/rules/smgp/seasons.json` stores 542 U+2014 escape sequences that parse to literal
  em-dashes in user-visible strings while passing the literal-character guard
  (`tests/Companion.Tests/Guards/NoEmDashGuardTests.cs` scans raw text).
- Handling: packet text and any future manifest or caption text normalizes per the owner
  rule (comma in prose, colon in headings) and avoids both the literal character and the
  escape form. Canon quotations in the packets are normalized and cited by pointer.

### B10. "Silent grandstand" phrase collision

- Ordinal 1 uses the phrase for awe at Senna's 150th start at Silverstone
  (`seasons.json:27`); ordinal 9 uses it for grief at Adelaide (:520,549).
- Handling: packet 2 owns the motif (grief register); packet 1 excludes the 150th-GP scene
  (also venue-unsafe, Silverstone 1991 layout for a 1990 scene).

### B11. "Two summers" slack

- Ordinal 8's overview dates the Charter "still two summers away" from 1997
  (`seasons.json:454`), yet ordinal 9 announces it in 1998 with Gen-4B landing 1999.
- Handling: read as announcement versus era arrival. Low severity, non-blocking; no packet
  depends on it.

### B12. Stale design-doc roster note

- `docs/dev/smgp-design.md:97-101` seats B. Miller at Bullets in season 1 and instructs
  it is "not Minarae"; shipped canon seats him at Minarae (`packs/smgp-1/entries.json`;
  almanac `races."U.S.A."` champion; all 17-season lore).
- Handling: shipped entries and lore are treated as canon; the design-doc roster note is
  superseded. No packet depends on Miller.

### B13. Era README versus audit discrepancy

- A provenance statement for in-house-generated era textures exists in
  `src/Companion.App/Assets/Era/README.md`, yet the audit still marks those files
  PROVENANCE_REVIEW.
- Handling: immaterial to this set (no packet uses era textures); flagged for the owner
  because the Stage B manifest should decide whether README-level claims suffice.

## C. Blocking-class risks that were avoided by selection

These would have forced BLOCKED verdicts; the slate was chosen to avoid them.

1. **Placeholder venues.** France (driven: Le Mans Bugatti 2022), U.S.A. (driven: Long
   Beach 2020, Adelaide fallback), Mexico (driven: Interlagos 1991, banner says "in
   Brazil"). No art can satisfy both canon venue character and driven-layout reality.
   Cost: the Asselin slipstream triumph (France) and the Miller canyon scene (U.S.A.)
   were dropped; all Mexico motifs were excluded slate-wide.
2. **Era-shifted layouts for era-specific scenes.** Silverstone 1991 for a 1990 scene,
   Spa 1993, Hungaroring 2025, modern Kansai. All excluded from scene work.
3. **Any depiction of the season-9 crash itself.** Canon says everyone walked away, but
   the restraint register and the stricter ART-P3-001 review make crash depiction a
   blocking choice. Packet 2 shows the silence after, car-free.
4. **Any canon-death implication in packet 8.** Canon is zero-fatality; the dispatch
   serves the opt-in sim feature only. The packet is venue-free and identity-free, and
   its context filters must gate on the mortality opt-in.

## D. What would flip a packet to BLOCKED

- Any requirement to show a real landmark, building, harbor, skyline, or corner geometry
  (no repository evidence exists for any venue).
- Any requirement to depict Senna's face, helmet, or era trade dress.
- Any requirement to declare a campaign-season champion or a live race result (outcome
  sovereignty belongs to the sim).
- Any requirement to bind packet 4 or 5 art to a specific shuffled-season round instance
  rather than to the template family (the season 2+ calendar shuffle and the
  round-slot-versus-venue art defect, audit ART-P2-001).
- Discovery that the intended render surface requires text baked into the art (forbidden
  by the banner-family rules).

## E. Blocking-gate summary

| Packet | Track lock | Unresolved contradiction | Decision |
|---|---|---|---|
| 1 `smgp-season-lore:1` | Imola + Monaco (locks 1, 5) | none | READY |
| 2 `smgp-season-lore:9` | Adelaide (lock 4) | A3 handled (car-free) | READY |
| 3 `smgp-season-lore:17` | Monaco (lock 5) | A2 corrected | READY |
| 4 `smgp-dispatch:milestone.first-win` | Jacarepaguá (lock 2) | B6 caveat enforced | READY |
| 5 `ch.showdown.smgp` | Monaco (lock 5) | B4 declared | READY |
| 6 `dnf.mechanical` | Hockenheim (lock 3) | A1 resolved | READY |
| 7 `mt.transfer.smgp` | none required | B5 honored | READY |
| 8 `smgp-dispatch:setback.died` | none required | none | READY |
