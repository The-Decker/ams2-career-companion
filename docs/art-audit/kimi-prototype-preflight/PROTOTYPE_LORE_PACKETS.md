# Prototype Lore Packets: Eight Lore-Locked Editorial Art Briefs

Mission: `SMGP-ART-LORE-PREFLIGHT-001`
State: `RESEARCH_AND_PLANNING_ONLY`
Date: 2026-07-18
Branch at research time: `hub/increment-4`

No image was generated, no image-generation prompt was written, no application, data,
asset, or canon file was changed, and no commit or push was made. This document contains
research briefs only.

## How to read these packets

- Every stable content ID below is copied verbatim from
  `docs/art-audit/news_history_art_inventory.csv`. The CSV line number is cited per packet.
- Every canon quotation is cited by file and JSON pointer or line. Quoted canon strings are
  normalized to the owner punctuation rule (an em-dash becomes a comma in prose, a colon in
  headings); the cited pointers carry the verbatim source strings. Note that
  `data/rules/smgp/seasons.json` stores its dashes as U+2014 escape sequences, so its parsed
  strings contain real em-dashes even though the raw file passes the literal-character guard.
- `UNKNOWN` means the repository contains no evidence. UNKNOWN never means creative freedom.
- Provenance classes use the production enum `ContentProvenance`
  (`src/Companion.Core/Newsroom/NewsroomTaxonomy.cs:72-79`): `VerifiedHistorical`,
  `CareerUniverse`, `EditorialAnalysis`, `SmgpFiction`, `SystemGenerated`.
- UI badge labels per `src/Companion.ViewModels/Hub/NewsStoryViewModels.cs:418-425`:
  `SmgpFiction` renders "SMGP UNIVERSE", `CareerUniverse` renders "CAREER UNIVERSE".

## Shared art-direction and UI slot specifications

Global rules inherited from the existing art direction (all cited):

- Track banner family rules (`src/Companion.App/Assets/TrackBanners/prompts.json`, every
  `sceneCue`): panoramic trackside establishing views emphasizing racing surface, terrain,
  and recognizable venue character; no text, no logos, no HUD, no border, no baked gradient.
- Approved visual direction: period-authentic minimalism, muted period palettes, one
  signature accent per era (`docs/dev/career-hub-design.md:229-231`;
  `docs/art-audit/NEWS_HISTORY_ART_ASSET_STATUS.md:326-337`).
- Parody-only rule: no real sponsor identities; Madonna yellow/red is deliberately not
  Marlboro (`docs/dev/smgp-design.md:116-117`).
- No external photographs, logos, liveries, helmets, manufacturer art, or mod assets may be
  downloaded or redistributed (`NEWS_HISTORY_ART_ASSET_STATUS.md:324`).

News editorial slot spec (verified XAML, applies to packets 4-8):

| Slot | Geometry (px) | Stretch | Citation |
|---|---|---|---|
| News lead story | full content width (max 1500, 0.94 x viewport) x 280 LEAD / 210 FEATURED / 120 STANDARD | UniformToFill | `src/Companion.App/Views/NewsView.xaml:1098-1107,1118-1123` |
| News secondary card | 280-480 wide x 190 LEAD / 154 FEATURED / 104 STANDARD | UniformToFill | `NewsView.xaml:536,550,618-633` |
| Article reader hero | up to 980 wide x 250; right portrait layer 320 wide; left car cutout 390 wide (Uniform) | UniformToFill | `NewsView.xaml:1410,1514-1530` |
| History dispatch card | about 320-490 wide x 92 | UniformToFill | `src/Companion.App/Views/HistoryView.xaml:496-520` |
| Archive/bookmarks column | 132 wide, near-square; team/driver art only, not editorial art | UniformToFill | `NewsView.xaml:673-685` |

Every News art slot stacks over a muted glyph placeholder and carries a flat 70 percent
black bottom scrim with date and importance chip (`Theme.xaml:254-265`,
`Theme.Dark.xaml:34`). Headlines never sit on the art.

- Recommended editorial master: 3.2:1 (for example 1920 x 600), single master serving all
  slots; card crop 2.4:1 and reader crop 3:1 fall inside it.
- Safe zone: focal subject inside the central 40 percent of width and central 50 percent of
  height. Bottom 12 percent expendable (scrim). Right 15 percent and left 18 percent
  expendable (reader portrait and car layers). Nothing load-bearing outside that core.

Season identity slot spec (applies to packets 1-3):

- Planned consumers per the inventory row: Briefing season header, Rival screen, campaign
  timeline, future season preview. No render surface exists today; the art field itself is
  missing (`MISSING_ASSET_FIELD`).
- Timeline slots: full card 168 x 104, locked chip 88 x 46
  (`src/Companion.App/Controls/CampaignTimelineStrip.xaml:118-153`).
- Recommended master: 16:9 (1920 x 1080) with a centered subject that survives a 168 x 104
  (1.62:1) card, an 88 x 46 (1.91:1) chip, and a wide Briefing hero band crop.
- Safe zone: subject in the central 40 percent of width and central 50 percent of height;
  nothing load-bearing in the outer 20 percent on any edge.

## The eight selected prototypes

| # | Stable content ID | Expected asset ID | Category | Season / year | Decision |
|---|---|---|---|---|---|
| 1 | `smgp-season-lore:1` | `smgp-season:1` | Season identity, early | Ordinal 1, 1990 | READY |
| 2 | `smgp-season-lore:9` | `smgp-season:9` | Season identity, middle | Ordinal 9, 1998 | READY |
| 3 | `smgp-season-lore:17` | `smgp-season:17` | Season identity, final | Ordinal 17, 2006 | READY |
| 4 | `smgp-dispatch:milestone.first-win` | `smgp-dispatch:milestone.first-win` | Race triumph | Almanac memory | READY |
| 5 | `ch.showdown.smgp` | `newsroom:ch.showdown.smgp` | Rivalry / championship pressure | Almanac memory + ordinal 8 | READY |
| 6 | `dnf.mechanical` | `newsroom:dnf.mechanical` | Mechanical failure | Ordinal 5, 1994 (reference scene) | READY |
| 7 | `mt.transfer.smgp` | `newsroom:mt.transfer.smgp` | Transfer / contract | Ordinal 8, 1997 (reference story) | READY |
| 8 | `smgp-dispatch:setback.died` | `smgp-dispatch:setback.died` | Restrained fatality coverage | Sim career, opt-in only | READY |

Selection balance: three season identities span early, middle, and final chapters; five
story categories are each covered once; the set exercises the P0 newsroom seam (packets
5-7), the P1 dispatch pools (packets 4, 8), and the P1 season-lore slots (packets 1-3);
both production badges appear (SMGP UNIVERSE on packets 1-4 and 8, CAREER UNIVERSE on
packets 5-7). Ordinal 8 was deliberately not chosen as the middle season identity because
its identity is the Senna-Ceara duel, which packet 5 already owns; ordinal 9 carries a
distinct institutional identity. All track scenes resolve to fully mapped, era-correct
venues; the three placeholder venues (France, U.S.A., Mexico) and the era-shifted
Silverstone and Spa motifs were excluded by the lore-lock gate.

---

## Packet 1: `smgp-season-lore:1`, "The Tenth Summer"

- **Stable content ID:** `smgp-season-lore:1` (inventory line 1082; ContentType HISTORY,
  ContentSubtype SMGP_SEASON_LORE, Severity P1, status MISSING_ASSET_FIELD).
- **Expected asset ID:** `smgp-season:1`.
- **Content source files and JSON pointers:**
  - `data/rules/smgp/seasons.json` -> `seasons[ordinal=1]` (inventory SourceLocator);
    overview line 9, preseason line 10, technical line 11, timeline lines 22-29, hooks
    lines 41-44 and following.
  - `packs/smgp-1/season.json` round 1 lines 60-68 (`imola_88`), round 16 lines 1547-1555
    (`azure_circuit_2021`).
  - `data/rules/smgp/what-really-happened.json` -> `races."San Marino"` lines 4-17,
    `races."Monaco"` lines 229-242.
- **Season ordinal and runtime year:** ordinal 1, runtime year 1990; in-world numbering is
  the tenth World Championship (seasons.json:9). All 17 seasons share production
  `packId=smgp-1`.
- **Canon title and event category:** "The Tenth Summer", era "The Iron Circus"; History
  season-identity record.
- **Provenance:** SMGP fiction (`smgpFiction`, badge SMGP UNIVERSE).
- **Driver IDs and exact identities:**
  - `driver.ayrton_senna`: Ayrton Senna, BRA, Madonna #1, epithet "THE UNTOUCHABLE KING"
    (`data/rules/smgp/driver-profiles.json` drivers[0]); six championships, 69 wins,
    72 poles at the campaign's dawn (`data/rules/smgp/driver-stats.json` drivers[0];
    seasons.json:44). Depicted only abstracted: car, number, colors, never face or helmet
    likeness.
  - `driver.gilberto_ceara`: Gilberto Ceara, BRA, Bullets #17, "THE LOADED GUN"
    (`driver-profiles.json:258-270`); the legalized insurgent of the season's protest
    storyline.
  - Supporting names in the season prose: `driver.felipe_elssler` (Firenze #3),
    `driver.eddie_bellini` (Dardan #18, the failed protest), `driver.giorgio_alberti`
    (Millions #6), `driver.bruno_salgado` (Iris #33).
- **Team IDs, colors, and established branding:**
  - `team.madonna`, LEVEL A. Colors (prose canon, no hex exists): "deep imperial red
    bleeding into blazing gold", "the yellow-and-red garage"
    (`data/rules/smgp/team-profiles.json` teams["team.madonna"] history). Motto: "YELLOW
    AND RED, FIRST AND FLAWLESS, THE CROWN NEVER SLIPS."
  - `team.bullets`, LEVEL C. Colors: "gunmetal flanks, a brass blade down the nose, a flash
    of tropical green" (`team-profiles.json:198`). Motto: "LOADED AT LEVEL C. AIMED
    STRAIGHT AT THE CROWN."
- **Correct car and era:** Madonna #1 = `formula_classic_g3m1`, Bullets #17 =
  `formula_classic_g3m4` (`packs/smgp-1/teams.json` carVehicleIds); AMS2 class
  F-Classic_Gen3, 1986-1990 hardware (`data/ams2/classes.json`). In-world machinery is the
  final-spec Gen-3 under the homologation freeze (seasons.json:10-11, 22). This is the one
  campaign season where the in-world generation matches the physical fleet.
- **Exact venue and layout ID:** season opener San Marino = Imola, layout `imola_88`
  (round 1); season finale Monaco = `azure_circuit_2021` (round 16, fixed every season).
  Full lore locks in `TRACK_ACCURACY_CHECKLIST.md`.
- **Confirmed result or narrative event:** the Gen-3 homologation freeze becomes law
  (seasons.json:22); the stewards declare the featherweight Bullets legal and Dardan's
  protest fails (seasons.json:41); the King's arithmetic, six crowns, 69 wins, 72 poles, is
  read aloud at San Marino (seasons.json:44); the season opens at Imola and closes at
  Monaco (pack calendar). Lore never declares a campaign champion; the sim decides.
- **Confirmed weather and time of day:** weather Clear, pack-authored for every season-1
  session (`packs/smgp-1/season.json` weatherSlots, for example lines 29-43;
  `docs/dev/smgp-design.md:46`). Time of day: UNKNOWN (never authored anywhere); neutral
  clear daylight is an art choice, not a canon fact.
- **Emotional/editorial tone:** the stone-carved old order at full weight; a serene king;
  and a quiet, almost unnoticed beginning, "no press call, no fanfare, just a new set of
  overalls on the hook" (seasons.json:9).
- **Required visible elements:** the red-and-gold #1 car (identity by number and palette,
  no driver likeness); the gunmetal #17 with brass nose blade; a season-opener pit straight
  atmosphere at Imola (generic pit-straight furniture only, no invented buildings); a full
  grandstand as silhouette; clear sky; muted Iron Circus palette with one imperial
  red-and-gold accent.
- **Forbidden visible elements:** Senna's face, helmet, or any McLaren-era trade dress;
  Marlboro chevron geometry; any text, logos, or HUD; Monaco harbor, yachts, casino, or
  waterfront; the 150th-Grand-Prix Silverstone motif (era-shifted venue, and its "silent
  grandstand" phrase collides with packet 2's grief motif); pace cars, medical corps, or
  rebuilt Charter barriers (all post-date this era); any Gen-4 or Gen-5 visual claim.
- **UI surfaces (inventory):** Briefing season header; Rival screen; campaign timeline;
  future season preview.
- **Required aspect ratios and safe zones:** season identity slot spec above: 16:9 master,
  centered subject, survives 168 x 104 and 88 x 46 chips plus a wide Briefing hero band.
- **Provenance or licensing concerns:** highest likeness risk in the set (Senna is a real,
  deceased public figure with an actively managed estate); abstraction is mandatory.
  Manifest must record `SmgpFiction`, license, source method, seed, owner, creation date,
  revision, approval state. No external photographs.
- **Contradictions between sources:** none material. (The design doc's stale season-1
  roster note seating Miller at Bullets is superseded by shipped entries and all lore;
  irrelevant to this packet.)
- **Confidence level:** HIGH.
- **Decision:** READY.

---

## Packet 2: `smgp-season-lore:9`, "The Reckoning"

- **Stable content ID:** `smgp-season-lore:9` (inventory line 1090; HISTORY /
  SMGP_SEASON_LORE, P1, MISSING_ASSET_FIELD).
- **Expected asset ID:** `smgp-season:9`.
- **Content source files and JSON pointers:**
  - `data/rules/smgp/seasons.json` -> `seasons[ordinal=9]`: overview line 517, technical
    line 519, safety line 520, timeline lines 533 and 537, arcs line 541, hooks lines
    549-557 (verified lines 549, 550, 556, 557).
  - `data/rules/smgp/what-really-happened.json` -> `races."Australia"` lines 214-227 (the
    almanac's earlier, separate Great Wall Sunday memory, line 221).
  - `packs/smgp-1/season.json` round 15 lines 1448-1456 (`adelaide_historic`).
- **Season ordinal and runtime year:** ordinal 9, runtime year 1998; in-world the
  eighteenth World Championship (seasons.json:517).
- **Canon title and event category:** "The Reckoning", era "The Horsepower War" (its
  closing hinge); History season-identity record.
- **Provenance:** SMGP fiction (SMGP UNIVERSE).
- **Driver IDs and exact identities:**
  - `driver.gilles_gould`: Gilles Gould, CAN, Tyrant #12, "ALL OR THE ARMCO"
    (`driver-profiles.json`); "out-dared at last by the concrete his island bred him on,
    walked away" (seasons.json:520). Identity stays prose-only: the art must not depict him.
  - Title-fight names in prose only: `driver.michael_blume` (Bestowal #8),
    `driver.alex_picos` (Bestowal #7), `driver.ayrton_senna` imperial above it all
    (seasons.json:517).
  - Reform names in prose only: `driver.marcel_moreau` (Linden #22),
    `driver.giorgio_alberti` (Millions #6), the Circus Commission (seasons.json:520).
- **Team IDs, colors, and established branding:**
  - `team.tyrant`, LEVEL B: "gunmetal grey slabbed over oxblood red, a black iron fist
    stamped on each flank" (`team-profiles.json:130`). Referenced only as context; no car
    is shown.
  - `team.bestowal`, LEVEL A: "a gunmetal-and-silver missile with a great red 8 on its
    flanks" (`team-profiles.json:62`). Context only.
- **Correct car and era:** in-world peak Gen-4, "the final, angriest evolution of the
  horsepower formula" (seasons.json:519); the physical fleet is F-Classic_Gen3 for all 17
  seasons, and Gen evolution is prose-only. The scene is deliberately car-free, so no
  generation claim is visible.
- **Exact venue and layout ID:** Australia = Adelaide Street Circuit, layout
  `adelaide_historic` (season-1 round 15; canon pins the event at Australia regardless of
  later shuffles). Full lore lock in `TRACK_ACCURACY_CHECKLIST.md`.
- **Confirmed result or narrative event:** the Second Great Wall Sunday: half the field
  into the Armco, Gould out-dared at last, the tannoy falling silent over a standing
  grandstand, "everyone walks away, and no one pretends nothing happened"
  (seasons.json:520, 533); the photograph of the silent grandstand becomes the era's full
  stop (seasons.json:549); the Safety Charter is announced at Monaco before the finale,
  pace-car law, pit-lane speed limits, rebuilt barriers, a standing medical corps, and a
  twenty percent power cut (seasons.json:537, 557); the season's retirement column ran
  longer than its finishing order (seasons.json:556).
- **Confirmed weather and time of day:** weather UNKNOWN (none authored for the Australia
  scene); time of day UNKNOWN.
- **Emotional/editorial tone:** the somber hinge of the whole campaign: attrition, silence,
  conscience, then reform.
- **Required visible elements:** an empty concrete-walled street circuit (Adelaide walls,
  sanctioned by canon); a standing, silent grandstand rendered as a hushed mass silhouette;
  no cars on the racing surface; an institutional grey and steel palette; stillness as the
  subject.
- **Forbidden visible elements:** crash depiction, wreckage, debris field, bodies, flames,
  car smoke; any driver identity (Gould remains prose); Safety Charter furniture at the
  Australia scene (pace car, medical corps silver car, rebuilt barriers are announced only
  afterward at Monaco); any city skyline, parkland, or waterfront; Charter text or
  documents; the awe-struck "silent grandstand" of Senna's 150th at Silverstone (packet 2
  owns the silent-grandstand motif, grief register only).
- **UI surfaces (inventory):** Briefing season header; Rival screen; campaign timeline;
  future season preview.
- **Required aspect ratios and safe zones:** season identity slot spec: 16:9 master,
  centered composition, timeline-chip safe.
- **Provenance or licensing concerns:** fatality-adjacent material held to the stricter
  review the queue reserves for injury/fatality art (`NEWS_HISTORY_ART_PRODUCTION_QUEUE.md`
  ART-P3-001). Must not resemble documentary photography of any real crash. `SmgpFiction`
  manifest record.
- **Contradictions between sources:** the almanac's earlier Great Wall Sunday memory shows
  "the pace car crawling" (`what-really-happened.json:221`) while standing pace-car law
  arrives only with the 1998 Charter (seasons.json:537; the 1992 proposal was "shouted
  down within a fortnight", seasons.json season 3 timeline). Registered in
  `CONTRADICTIONS_AND_BLOCKERS.md`; handled by showing the 1998 scene car-free.
- **Confidence level:** HIGH.
- **Decision:** READY.

---

## Packet 3: `smgp-season-lore:17`, "The Crown of Crowns"

- **Stable content ID:** `smgp-season-lore:17` (inventory line 1098; HISTORY /
  SMGP_SEASON_LORE, P1, MISSING_ASSET_FIELD).
- **Expected asset ID:** `smgp-season:17`.
- **Content source files and JSON pointers:**
  - `data/rules/smgp/seasons.json` -> `seasons[ordinal=17]`: title line 1030, subtitle
    line 1031, overview line 1033, preseason line 1034, safety line 1036, themes lines
    1038-1043, timeline line 1046 and following, arcs line 1060 and following, hooks lines
    1067-1079 (verified 1068, 1075, 1079), milestones line 1090 and following.
  - `data/rules/smgp/what-really-happened.json` -> `races."Monaco"` lines 229-242.
  - `packs/smgp-1/season.json` round 16 lines 1547-1555 (`azure_circuit_2021`).
- **Season ordinal and runtime year:** ordinal 17, runtime year 2006; in-world the
  twenty-sixth World Championship (seasons.json:1033).
- **Canon title and event category:** "The Crown of Crowns", era "The Golden Circus";
  History season-identity record.
- **Provenance:** SMGP fiction (SMGP UNIVERSE).
- **Driver IDs and exact identities:**
  - `driver.ayrton_senna` (Madonna #1): "the final boss of the age", the eve-of-San-Marino
    declaration (seasons.json:1046). Abstracted depiction only.
  - `driver.alain_asselin` (Madonna #2, "THE CLOSING DOOR"), "the loyal sword at his side
    one last time".
  - `driver.gilberto_ceara` (Bullets #17): "the last fire" (seasons.json:1060).
  - `driver.mika_larssen` (Azalea #34, "THE FROST THAT BLOOMS", the roster's one authored
    female driver, `driver-profiles.json` drivers[9]).
  - `driver.julianno_nono` (Minarae #21), `driver.jean_herbin` (Blanche #9); veterans
    Alberti, Elssler, Moreau in the gravity roles.
- **Team IDs, colors, and established branding:**
  - `team.madonna`: imperial red into blazing gold, the crown's garage.
  - `team.azalea`: "deep magenta petals fanning into cold white, chrome edges"
    (`team-profiles.json`).
  - `team.blanche`: "the purest white on the grid".
  - `team.minarae`: "a big bold number on the nose and honest workshop colours", plain by
    canon.
- **Correct car and era:** in-world Gen-5 "jewel formula" at absolute peak
  (seasons.json:1034); prose-only evolution, physical fleet F-Classic_Gen3. One imperial
  Madonna look only; no multi-era heritage parade (that motif belongs to ordinal 16).
- **Exact venue and layout ID:** Monaco, layout `azure_circuit_2021`, round 16, the finale
  that never shuffles. Charter-era state applies: the tunnel is relit end to end
  (seasons.json:1036; the relit happened in the Charter years, seasons.json:582), barriers
  are rebuilt, the pace car is a fixture, and the medical corps' silver car stands at the
  end of the pit lane (Charter-era furniture per seasons.json:584). Full lore lock in
  `TRACK_ACCURACY_CHECKLIST.md`.
- **Confirmed result or narrative event:** the governing office proclaimed the twenty-sixth
  championship the Crown of Crowns, sixteen rounds staged as chapters of one finale
  (seasons.json:1033, 1038, 1068); Senna's eve declaration (seasons.json:1046); "the last
  flag falls at Monaco" (seasons.json:1031, 1075); the sealed prize beyond the last flag,
  which only the campaign's finishers unlock (seasons.json:1042, 1079). Lore declares no
  champion; the sim decides.
- **Confirmed weather and time of day:** weather UNKNOWN; time of day UNKNOWN (the relit
  tunnel is a permanent state, not a lighting cue; farewell warmth is palette, not canon).
- **Emotional/editorial tone:** valedictory, reverent, conclusive; pilgrims at the crown
  jewel; a deliberate, sealed mystery beyond the flag.
- **Required visible elements:** the crown-jewel street circuit in farewell dress: Armco,
  the hairpin, the tunnel exit glow (relit, so a warm lit portal, not darkness); pilgrim
  grandstands; one imperial red-and-gold #1 as the only car; Charter-era safety furniture
  permitted (rebuilt barriers, the distant silver medical car); muted Golden Circus palette
  with one jubilee gold accent.
- **Forbidden visible elements:** any champion or winner declaration (the sim decides);
  faces, helmets, likenesses; harbor, yachts, casino, palace, or any waterfront (zero
  repository evidence); heritage-liveries drift toward real 1990s-2000s trade dress (the
  heritage parade is ordinal 16's motif, seasons.json:967); the sealed prize itself
  depicted (`special.jpg` and `ultimate.jpg` are sealed finale assets per
  `docs/dev/smgp-17-seasons.md`); a dark tunnel (relit by 2006).
- **UI surfaces (inventory):** Briefing season header; Rival screen; campaign timeline;
  future season preview.
- **Required aspect ratios and safe zones:** season identity slot spec: 16:9 master,
  centered subject, timeline-chip safe.
- **Provenance or licensing concerns:** Senna abstraction; jubilee pageantry must stay
  inside SMGP fictional palettes; `SmgpFiction` manifest record.
- **Contradictions between sources:** ordinal-16 versus ordinal-17 motif conflation was
  caught during research (heritage liveries and Dufay's farewell tour are the Silver
  Jubilee's, seasons.json:967-1026) and corrected here; see
  `CONTRADICTIONS_AND_BLOCKERS.md`.
- **Confidence level:** HIGH.
- **Decision:** READY.

---

## Packet 4: `smgp-dispatch:milestone.first-win`, the rain coronation

- **Stable content ID:** `smgp-dispatch:milestone.first-win` (inventory line 2537; NEWS /
  SMGP_DISPATCH_FAMILY, P1, GENERIC_FALLBACK and UNINTENTIONAL_REUSE; "2 authored prose
  variants share this art behavior").
- **Expected asset ID:** `smgp-dispatch:milestone.first-win` (editorial pool art for the
  family).
- **Content source files and JSON pointers:**
  - `data/rules/smgp/dispatches.json` -> `templates."milestone.first-win"` lines 74-77
    (two variants; tokens `{player}`, `{team}`, `{pool:dateline}`, `{pool:cheer}`).
  - Reference scene: `data/rules/smgp/what-really-happened.json` -> `races."Brazil"` lines
    19-33 (champion line 22, rain coronation line 26).
  - Venue mapping: `packs/smgp-1/season.json` round 2 lines 159-167
    (`jacarepagua_historic`).
- **Season ordinal and runtime year:** the family fires in any SMGP career season; the
  reference scene is a pre-campaign almanac memory with no ordinal and no runtime year.
- **Canon title and event category:** dispatch family `milestone.first-win`; reference
  scene "BRAZIL, CEARA'S BURNING FORTRESS"; race triumph.
- **Provenance:** SMGP fiction (SMGP UNIVERSE); both the family and the almanac scene are
  `smgpFiction`.
- **Driver IDs and exact identities:** `driver.gilberto_ceara`, Gilberto Ceara, BRA,
  Bullets #17, "THE LOADED GUN"; wet master with the roster's highest wet skill
  (`driver-profiles.json:258-270`; `packs/smgp-1/drivers.json` ratings). Depicted by car
  and palette only.
- **Team IDs, colors, and established branding:** `team.bullets`, LEVEL C; "gunmetal
  flanks, a brass blade down the nose, a flash of tropical green"
  (`team-profiles.json:198`); motto "LOADED AT LEVEL C. AIMED STRAIGHT AT THE CROWN."
- **Correct car and era:** Bullets #17 = `formula_classic_g3m4`, F-Classic_Gen3. The
  in-world generation of the pre-campaign memory is not authored: UNKNOWN. The hardware
  reference is the pack's g3m4.
- **Exact venue and layout ID:** Brazil, Jacarepaguá, Rio de Janeiro, layout
  `jacarepagua_historic` (anti-clockwise, 1988). Full lore lock in
  `TRACK_ACCURACY_CHECKLIST.md`.
- **Confirmed result or narrative event:** Brazil's champion of record is "G. Ceara ·
  Bullets" (`what-really-happened.json:22`); the authored coronation: "the heavens opened
  on lap one and the Bullets simply vanished up the road, the grandees paddling in his
  spray" (:26). The dispatch family's own copy celebrates a player's first victory
  (dispatches.json:74-77).
- **Confirmed weather and time of day:** rain, authored in the almanac fiction (:26); time
  of day UNKNOWN.
- **Emotional/editorial tone:** coronation in the rain; a home fortress roaring; the wet
  master's apotheosis.
- **Required visible elements:** the gunmetal #17 with brass blade and tropical-green flash
  vanishing up a wet, bumpy straight, seen from a rear three-quarter view; pursuing
  grandees reduced to indistinct silhouettes in spray; soaked asphalt sheen; a roaring home
  grandstand as noise-silhouettes; an abstracted big-board glow.
- **Forbidden visible elements:** driver face or likeness; podium ceremony or trophy (not
  authored); any "maiden" or "first win" visual claim (Ceara entered season 1 with six
  career wins, seasons.json:9; caveat registered in `CONTRADICTIONS_AND_BLOCKERS.md`);
  identifiable Senna among the silhouettes (no red-and-gold #1); live-career result claims
  (the family binds by template, never by round instance); text, logos, flag iconography.
- **UI surfaces (inventory):** News lead; News cards; article reader; History latest
  dispatches.
- **Required aspect ratios and safe zones:** News editorial slot spec: 3.2:1 master,
  central 40 x 50 percent safe core, bottom 12 percent scrim-expendable, right 15 percent
  and left 18 percent reader-layer expendable.
- **Provenance or licensing concerns:** SMGP dispatch identity is deterministic for the
  current detector but not append-stable (`NEWS_HISTORY_ART_ASSET_STATUS.md:389`); the art
  manifest must key from stable content data, not dispatch order. `SmgpFiction` manifest
  record; no external photographs.
- **Contradictions between sources:** the documented scene is a coronation, not a maiden
  win (handled: no maiden claim); season 2 and later sim weather is seeded independently of
  lore, so the rain illustrates the almanac memory, never a live round
  (`docs/SMGP_17_SEASON_STRUCTURE.md:68`).
- **Confidence level:** HIGH for the scene; MEDIUM for the family binding semantics (caveat
  logged). Overall: READY with the caveat enforced.
- **Decision:** READY.

---

## Packet 5: `ch.showdown.smgp`, the tunnel duel

- **Stable content ID:** `ch.showdown.smgp` (ContentId `newsroom-template:ch.showdown.smgp`,
  inventory line 2294; NEWS / NEWSROOM_TEMPLATE, P0, MISSING_ASSET_FIELD and
  EXPLICIT_PLACEHOLDER).
- **Expected asset ID:** `newsroom:ch.showdown.smgp`.
- **Content source files and JSON pointers:**
  - `data/rules/newsroom/032-championship.json` -> `templates[14]`, lines 244-250: event
    `finalRoundShowdown`, eras `["smgp"]`, headline "FINAL STAGE: THE CROWN IS DECIDED AT
    THE LAST ROUND", deck "{gap} POINTS BETWEEN GLORY AND GAME OVER".
  - Reference scene: `data/rules/smgp/what-really-happened.json` -> `races."Monaco"` lines
    229-242 (champion line 232, tunnel duel line 235).
  - Corroboration: `data/rules/smgp/seasons.json:454` (ordinal-8 overview: the duel "came
    within a breath of the Armco in Monaco's tunnel").
  - Venue mapping: `packs/smgp-1/season.json` round 16 lines 1547-1555.
- **Season ordinal and runtime year:** the template fires at any SMGP final-round showdown;
  the reference scene is the pre-campaign almanac memory, corroborated by ordinal 8 (1997).
- **Canon title and event category:** template `ch.showdown.smgp`; reference scene "MONACO,
  THE JEWEL WHERE KINGS ARE CROWNED"; rivalry / championship pressure.
- **Provenance:** the inventory records `careerUniverse` for this template: SMGP-mode
  newsroom stories badge CAREER UNIVERSE because `ProvenanceFor` falls through
  (`src/Companion.Core/Newsroom/NewsroomComposer.cs:247-253`). The reference scene is
  `smgpFiction`. The manifest must record `careerUniverse` with SMGP-scoped context
  filters and must not "correct" the badge in art metadata (registered contradiction).
- **Driver IDs and exact identities:** `driver.ayrton_senna` (Madonna #1) versus
  `driver.gilberto_ceara` (Bullets #17); "the rivalry the grandstand still can't stop
  arguing about" (`what-really-happened.json:241`). Cars and numbers only, no faces.
- **Team IDs, colors, and established branding:** `team.madonna` (imperial red into blazing
  gold) versus `team.bullets` (gunmetal, brass blade, tropical green flash).
- **Correct car and era:** `formula_classic_g3m1` versus `formula_classic_g3m4`,
  F-Classic_Gen3; pre-Charter world frame.
- **Exact venue and layout ID:** Monaco, layout `azure_circuit_2021`; the tunnel. For this
  pre-Charter frame the tunnel is dark (the relit tunnel arrives only in the Charter years,
  seasons.json:582, 1036). Full lore lock in `TRACK_ACCURACY_CHECKLIST.md`.
- **Confirmed result or narrative event:** the almanac duel: "nose-to-tail in the dark with
  D.P. on the line", both kissed Armco within a breath of each other on the last lap, and
  "Senna took the flag, he always seems to" (`what-really-happened.json:235`); champion of
  record "A. Senna · Madonna" (:232). The template fires while the live showdown is
  undecided, so the art must be result-free: the breath between them, never the flag.
- **Confirmed weather and time of day:** weather UNKNOWN (the tunnel's darkness is
  structural, not meteorological); time of day UNKNOWN.
- **Emotional/editorial tone:** knife-edge; a held breath; the argument the grandstand
  never stops having.
- **Required visible elements:** two cars nose-to-tail in tunnel shadow, the red-and-gold
  #1 with the gunmetal #17 a breath off its gearbox; Armco close on both sides; the tunnel
  exit glowing ahead; a composition about tension, not outcome.
- **Forbidden visible elements:** any winner declaration, flag, or trophy imagery; faces,
  helmets, likenesses; harbor, yachts, casino, palace, waterfront; text or logos; a bright
  relit tunnel (wrong era for this frame); contact or crash depiction (the Armco kiss is
  past-tense lore; show the breath, not the kiss).
- **UI surfaces (inventory):** News lead; featured cards; story list; bookmarks; search;
  article reader. (Bookmarks and story-list columns render team/driver art only; this
  editorial asset serves the lead, cards, and reader.)
- **Required aspect ratios and safe zones:** News editorial slot spec: 3.2:1 master,
  central safe core, scrim and reader-layer expendable bands.
- **Provenance or licensing concerns:** Senna likeness abstraction; the
  badge-edge (careerUniverse metadata over an SMGP-universe scene) must be documented in
  the manifest so a later audit does not misread the pairing as an error.
- **Contradictions between sources:** badge fall-through (registered); tunnel lighting
  state is era-dependent (resolved by the pre-Charter frame).
- **Confidence level:** HIGH.
- **Decision:** READY.

---

## Packet 6: `dnf.mechanical`, the smoke plague

- **Stable content ID:** `dnf.mechanical` (ContentId `newsroom-template:dnf.mechanical`,
  inventory line 2304; NEWS / NEWSROOM_TEMPLATE, P0, MISSING_ASSET_FIELD and
  EXPLICIT_PLACEHOLDER).
- **Expected asset ID:** `newsroom:dnf.mechanical`.
- **Content source files and JSON pointers:**
  - `data/rules/newsroom/010-core.json` -> `templates[15]`, lines 223-229 and following:
    event `retiredMechanical`, headline "Machinery fails {subject}[[?venue: at {venue}]]".
    The template carries no `eras` restriction; it serves every career type.
  - Reference scene: `data/rules/smgp/seasons.json` -> `seasons[ordinal=5]`: theme line
    269, timeline line 280 ("the treeline at West Germany eats a season of engines whole in
    a single scorching weekend"), hook line 296, safety line 265 ("engines let go in smoke
    and steam, drivers coast to the grass and walk back to applause").
  - Almanac context: `data/rules/smgp/what-really-happened.json` -> `races."West Germany"`
    lines 64-77 (champion of record line 67; "More engines have expired on the forest blast
    here than at any two other rounds combined", line 75).
  - Venue mapping: `packs/smgp-1/season.json` round 5 lines 456-464 (`hockenheim_1988`).
- **Season ordinal and runtime year:** the template is era-generic; the reference scene is
  ordinal 5, runtime 1994, "The Horsepower Spring", the first Gen-4 spring.
- **Canon title and event category:** template `dnf.mechanical`; reference scene the
  season-5 smoke plague; mechanical failure.
- **Provenance:** `careerUniverse` (template) with an `smgpFiction` reference scene. The
  art must carry no SMGP-only marks, because the same template serves historical careers.
- **Driver IDs and exact identities:** none depicted: the walking driver is anonymous and
  seen from behind. Almanac context only: `driver.michael_blume` (DEU, Bestowal #8, "THE
  UNREDEEMED HEIR") is the venue's champion of record, never the failing driver.
- **Team IDs, colors, and established branding:** `team.bestowal`, LEVEL A: "a
  gunmetal-and-silver missile with a great red 8 on its flanks" (`team-profiles.json:62`),
  venue context only. The failing car is team-neutral. (A research-pass disagreement about
  whether Bestowal colors exist was resolved by direct citation; see
  `CONTRADICTIONS_AND_BLOCKERS.md`.)
- **Correct car and era:** in-world first-spring Gen-4 (the smoke-plague era); hardware
  F-Classic_Gen3; the failing car shows no generation-specific bodywork claim.
- **Exact venue and layout ID:** West Germany, Hockenheim, layout `hockenheim_1988`
  (clockwise, 1988); the treeline at the edge of the forest straights, stadium bowl in the
  distance only if desired. Full lore lock in `TRACK_ACCURACY_CHECKLIST.md`.
- **Confirmed result or narrative event:** the season-5 smoke plague: retirements at an
  all-time record, the treeline eating a season of engines in one scorching weekend
  (seasons.json:269, 280, 296); the authored mercy: failures are mechanical, not human,
  drivers coast to the grass and walk back to applause (seasons.json:265).
- **Confirmed weather and time of day:** scorching heat authored for the reference weekend
  (seasons.json:280); time of day UNKNOWN.
- **Emotional/editorial tone:** cruel luck; industrial pathos; the walked-away applause.
- **Required visible elements:** a coasted car stopped on the grass at the treeline,
  trailing smoke and steam (never fire); an anonymous driver walking away, rear view; the
  forest edge as a dark silhouette; heat shimmer; an empty track ahead.
- **Forbidden visible elements:** flames or a fireball (canon says smoke and steam); crash
  or impact; any injury narrative; an identifiable driver or team livery (the scene is
  team-neutral by design); modern barriers or pit buildings (unevidenced); SMGP-only
  marks of any kind (the template is era-generic).
- **UI surfaces (inventory):** News lead; featured cards; story list; bookmarks; search;
  article reader. (As packet 5: editorial art serves lead, cards, reader.)
- **Required aspect ratios and safe zones:** News editorial slot spec: 3.2:1 master,
  central safe core.
- **Provenance or licensing concerns:** caption vocabulary is era-capped by
  `NewsCorpusGuardTests`; the era-generic scope must be stated in the manifest context
  filters; `careerUniverse` record.
- **Contradictions between sources:** none unresolved. The Bestowal-color research
  disagreement is resolved (prose verified); the era-generic template versus SMGP reference
  scene pairing is declared, not hidden.
- **Confidence level:** HIGH for scene and venue; MEDIUM for template scope (declared).
- **Decision:** READY.

---

## Packet 7: `mt.transfer.smgp`, the works-engine winter

- **Stable content ID:** `mt.transfer.smgp` (ContentId `newsroom-template:mt.transfer.smgp`,
  inventory line 2358; NEWS / NEWSROOM_TEMPLATE, P0, MISSING_ASSET_FIELD and
  EXPLICIT_PLACEHOLDER).
- **Expected asset ID:** `newsroom:mt.transfer.smgp`.
- **Content source files and JSON pointers:**
  - `data/rules/newsroom/034-market-teams.json` -> `templates[6]`, lines 85-91: event
    `playerTeamChanged`, eras `["smgp"]`, headline "{player} JUMPS TO {team}!", deck "NEW
    COLOURS! NEW GARAGE! SAME MISSION!".
  - Reference story: `data/rules/smgp/seasons.json` -> `seasons[ordinal=8]`: timeline
    signing line 467 ("Winter: Bullets signs a one-season works engine deal, the only time
    the backstreet garage ever runs establishment power"), dissolution line 474 ("Gilberto
    Ceara shakes the engineers' hands and goes back to building his own thunder"), overview
    line 454, hooks lines 485 and 493.
  - Noted alternate seed (unused): the Millions raid on Firenze's drawing office, ordinal 2
    (seasons.json:86, 103: "Firenze calls it theft; Millions calls it Tuesday.").
- **Season ordinal and runtime year:** reference story ordinal 8, runtime 1997, "The
  Boiling Point".
- **Canon title and event category:** template `mt.transfer.smgp`; transfer / contract
  story.
- **Provenance:** `careerUniverse` badge; `smgpFiction` reference story.
- **Driver IDs and exact identities:** `driver.gilberto_ceara` (Bullets #17) appears only
  as a handshake silhouette; no face, no likeness.
- **Team IDs, colors, and established branding:** `team.bullets` (gunmetal, brass blade,
  tropical green flash). The works supplier is never named in canon: "establishment power"
  (seasons.json:454, 467). No marque, no badges, no branded props: UNKNOWN stays unknown.
- **Correct car and era:** Bullets #17 = `formula_classic_g3m4`; the brass-blade nose under
  a work lamp; in-world fourth-year Gen-4 context, not visible in a garage scene.
- **Exact venue and layout ID:** none. The scene is the Bullets backstreet garage and
  paddock, winter preseason (signing) and season's end (dissolution). No track lore lock
  required.
- **Confirmed result or narrative event:** the winter signing of the one-season
  works-engine deal (seasons.json:467) and its dissolution under establishment pressure at
  season's end (seasons.json:474; hooks 485, 493).
- **Confirmed weather and time of day:** winter is authored for the signing; the scene is
  indoors, so weather is immaterial; time of day UNKNOWN.
- **Emotional/editorial tone:** cold-war paddock politics; a deal too good to last;
  defiant craft pride.
- **Required visible elements:** the backstreet garage interior; the gunmetal #17 nose with
  its brass blade under a work lamp; an unbranded, tarped engine crate; two silhouetted
  figures in a handshake or paperwork exchange, no faces; winter light through the garage
  door; workbench and tool texture.
- **Forbidden visible elements:** any supplier marque, logo, or wordmark; contract text;
  faces or likenesses; real-world team trade dress; race-day elements (no track, no crowd);
  any red-and-white chevron geometry.
- **UI surfaces (inventory):** News lead; featured cards; story list; bookmarks; search;
  article reader.
- **Required aspect ratios and safe zones:** News editorial slot spec: 3.2:1 master; car
  nose left-of-center, handshake right-of-center, both inside the central safe core.
- **Provenance or licensing concerns:** fictional sponsors only; the unnamed supplier must
  remain unnamed in every visual and caption; `careerUniverse` record.
- **Contradictions between sources:** none material.
- **Confidence level:** HIGH.
- **Decision:** READY.

---

## Packet 8: `smgp-dispatch:setback.died`, the circus falls silent

- **Stable content ID:** `smgp-dispatch:setback.died` (inventory line 2544; NEWS /
  SMGP_DISPATCH_FAMILY, P1, GENERIC_FALLBACK and UNINTENTIONAL_REUSE; "2 authored prose
  variants share this art behavior").
- **Expected asset ID:** `smgp-dispatch:setback.died`.
- **Content source files and JSON pointers:**
  - `data/rules/smgp/dispatches.json` -> `templates."setback.died"` lines 122-125 (two
    variants) and `pools."mourning"` lines 29-35 ("Flags fly at half-mast over the pit
    lane.", "The whole paddock falls silent.", "The circus stops, and remembers one of its
    own.").
  - Restraint register (the newsroom's own authored voice for death):
    `data/rules/newsroom/033-reliability-injury.json` lines 357-358 and 371 ("It is the
    saddest duty this page carries", "quietly, together, with the engines silent and the
    flags at half mast", "Racing goes on. That has never once meant forgetting.").
  - Framing model (zero-fatality canon): `data/rules/smgp/seasons.json:520, 533` ("everyone
    walks away").
  - Mortality gating: `docs/dev/character-death-injury.md:45-65` (MortalityMode is opt-in);
    `docs/CAREER_GAME_OVER_FLOW.md:50-52` (folded d500 severity band) and :20 (IN MEMORIAM
    badge). Death, sit-out, and career-over views currently ship no editorial image element
    (`NEWS_HISTORY_ART_ASSET_STATUS.md:287`).
- **Season ordinal and runtime year:** the family fires in simulated SMGP careers in any
  ordinal, and only when the player has opted into mortality. No canon season is involved.
- **Canon title and event category:** dispatch family `setback.died`; restrained fatality
  coverage.
- **Provenance:** `smgpFiction` family over a career-instance context; badge SMGP UNIVERSE.
- **Driver IDs and exact identities:** none, by design. The subject (`{player}`) is never
  depicted. No roster driver may appear.
- **Team IDs, colors, and established branding:** none depicted.
- **Correct car and era:** no identifiable car; a covered shape or a silent engine stand
  only, identity-free.
- **Exact venue and layout ID:** none, by design: a venue-free pit-lane and garage
  memorial, which also keeps this packet visually clear of packet 2's Adelaide scene.
- **Confirmed result or narrative event:** the family's authored voice: "TRAGEDY. {player}
  has been killed in an accident here. The SMGP world loses one of its own, and the circus
  falls silent." (dispatches.json:123) with the mourning pool (:29-35).
- **Confirmed weather and time of day:** weather UNKNOWN; time of day UNKNOWN. A grey,
  stilled light is a palette choice and must be labeled as such, not as canon.
- **Emotional/editorial tone:** restrained mourning: silence, flags at half-mast, the empty
  seat; never graphic, never spectacular.
- **Required visible elements:** flags at half-mast over a pit lane (pool verbatim); an
  empty garage bay; a covered car or engine stand; a darkened big board; a muted,
  desaturated palette; stillness as the whole subject.
- **Forbidden visible elements:** crash depiction, wreckage, a body, flames, gore; any
  driver identity, portrait, or the deceased's helmet; any venue claim; any implication of
  a canon death (canon is zero-fatality: Gould walked away, seasons.json:520); any
  resemblance to a real person's memorial or a documentary photograph.
- **UI surfaces (inventory):** News lead; News cards; article reader; History latest
  dispatches.
- **Required aspect ratios and safe zones:** News editorial slot spec: 3.2:1 master; flag
  and garage bay inside the central safe core so the near-square fallback crops keep both.
- **Provenance or licensing concerns:** held to the stricter context and provenance review
  the queue reserves for fatality and injury art (ART-P3-001); context filters must gate on
  the mortality opt-in; sensitivity review is part of approval, not an afterthought.
- **Contradictions between sources:** none; the packet is engineered to respect the
  zero-fatality canon while serving the opt-in sim feature.
- **Confidence level:** HIGH.
- **Decision:** READY.
