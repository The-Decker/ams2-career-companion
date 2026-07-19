# Track Accuracy Checklist: Lore Locks for the Eight Prototypes

Mission: `SMGP-ART-LORE-PREFLIGHT-001`
State: `RESEARCH_AND_PLANNING_ONLY`
Date: 2026-07-18

Every track scene in the eight prototype packets is locked here. A visible feature is
either VERIFIED (repository citation given) or UNKNOWN (no evidence; must not be drawn).
UNKNOWN never means creative freedom. Six of the eight packets use five venues; packets 7
and 8 are deliberately venue-free.

Evidence sources used throughout:

- `[PACK]` = `packs/smgp-1/season.json` (round name, realVenue, AMS2 layout id, laps,
  placeholder declarations).
- `[LIB]` = `data/ams2/tracks.json` (the driven AMS2 layout record; provenance:
  "extractedFrom AMS2 build 23820506, 2026-07-02 + 37 mod tracks refreshed 2026-07-08").
  This file carries no direction, surface, city, or pit fields.
- `[H89]` = `data/history/1989.json` (f1db-derived, CC BY 4.0: real 1989 circuit records
  with layoutId, type, direction, length, turns).
- `[CANON]` = `data/rules/smgp/what-really-happened.json` (SMGP fiction venue descriptors).
- `[PRM]` = `src/Companion.App/Assets/TrackBanners/prompts.json` (existing banner scene
  cues; none names a landmark).
- `[MAN]` = `src/Companion.App/Assets/TrackBanners/manifest.json` (layout id to 1920 x 440
  embedded master).

## Global rules (all track scenes)

1. Banner-family rules apply to new editorial art: no text, no logos, no HUD, no borders,
   no baked gradients (`[PRM]`, every sceneCue).
2. No sceneCue in the existing 173-master banner library names a landmark; no landmark may
   be treated as verified from that library.
3. `[LIB]` has no direction or surface fields. Direction and circuit type below come from
   `[H89]` and describe the real venue; driven-layout lengths come from `[LIB]`.
4. Pit and start-area configuration: UNKNOWN for every venue. No repository source
   describes pit buildings, pit-lane geometry, or grid furniture. Only race-format facts
   exist (lap counts in `[PACK]`; Monaco grid capped at 25).
5. Surface type: asphalt is implicit in the `[H89]` RACE/STREET classification; no source
   describes surface texture, so specific tarmac character is UNKNOWN.
6. Era-correct safety equipment is canon-dated: standing pace-car law, the medical corps'
   silver car, rebuilt barriers, and pit-lane speed limits exist only from the 1998
   Monaco Safety Charter onward (`data/rules/smgp/seasons.json:537,582,584,1036`). Scenes
   framed before the Charter must show none of them. Monaco's tunnel is relit only in the
   Charter years (`seasons.json:582,1036`).
7. Fictional events must not resemble deceptive documentary photographs of real events.
8. No mountains, buildings, grandstand architecture, tunnels (except Monaco's, canon),
   waterfronts, skylines, corner geometry, pit complexes, vegetation detail, weather, or
   trackside furniture may be invented.

---

## Lock 1: San Marino (Imola), used by packet 1

- **Exact track and layout ID:** `imola_88` (`[PACK]:60-68`, realVenue "Autodromo
  Internazionale Enzo e Dino Ferrari"; not a placeholder; 61 laps; 1989 round 2 history
  pointer). Banner master `masters/imola-1988.jpg` (`[MAN]`).
- **Country and location:** Imola, Italy (`[H89]` layoutId `imola-1`, place Imola;
  `[LIB]` location "Italy", country "IT").
- **Historical or modern year:** 1988-spec layout (`[LIB]` year 1988, trackGrade
  Historic); runtime season 1990.
- **Circuit direction:** anti-clockwise (`[H89]`).
- **Surface type:** permanent road course, asphalt (`[H89]` type RACE; `[LIB]` trackType
  Circuit). 5.04 km, 22 turns (`[H89]`).
- **Pit and start-area configuration:** UNKNOWN (global rule 4). Packet 1 may show only a
  generic pit-straight atmosphere with no buildings.
- **Source-backed landmarks:** none. The flowing, fast character is canon (`[CANON]`
  races."San Marino": "fast, flowing, the opening flag where the campaign draws breath").
- **Source-backed surrounding terrain and skyline:** none. A grandstand that "simply
  expects it" exists in canon fiction; render as a generic full grandstand silhouette.
- **Era-correct barriers, fencing, curbs, signage, safety equipment:** no barrier or
  fencing evidence; 1990 is pre-Charter (global rule 6), so no pace car, no silver
  medical car, no rebuilt barriers.
- **Elements that must not appear:** the San Marino hills, Tosa/Acque Minerali/Variante
  corner geometry, any pit complex or grandstand architecture, fencing/curb types,
  signage, vegetation detail, any town or castle skyline, Rivazza, the Tamburello
  memorial or any real-event reference.

## Lock 2: Brazil (Jacarepaguá), used by packet 4

- **Exact track and layout ID:** `jacarepagua_historic` (`[PACK]:159-167`, realVenue
  "Autódromo Internacional do Rio de Janeiro (Jacarepaguá)"; not a placeholder; 61 laps;
  1989 round 1 history pointer). Banner `masters/jacarepagua-1988.jpg` (`[MAN]`).
- **Country and location:** Rio de Janeiro, Brazil (`[H89]` layoutId `jacarepagua-1`;
  `[LIB]` location "Brazil", country "BR").
- **Historical or modern year:** 1988 layout (`[LIB]` year 1988, Historic).
- **Circuit direction:** anti-clockwise (`[H89]`).
- **Surface type:** permanent circuit, asphalt; 5.003 km (`[LIB]`), 11 turns (`[H89]`).
  Canon adds "bumpy": "a track that bucks and blisters" (`[CANON]` races."Brazil").
- **Pit and start-area configuration:** UNKNOWN.
- **Source-backed landmarks:** none. Canon atmosphere only: "hammering sun", a home
  fortress crowd that "roars for one Bullets man", rain that turns it into "Ceara's
  private cathedral" (`[CANON]`).
- **Source-backed surrounding terrain and skyline:** none.
- **Era-correct barriers, fencing, curbs, signage, safety equipment:** no evidence;
  almanac-memory frame is pre-Charter (global rule 6).
- **Elements that must not appear:** the surrounding lagoons, any Rio skyline or
  mountains, concrete-versus-grass runoff claims, curb styles, pit buildings, palms or
  vegetation detail, Cristo Redentor or any real landmark.

## Lock 3: West Germany (Hockenheim), used by packet 6

- **Exact track and layout ID:** `hockenheim_1988` (`[PACK]:456-464`, realVenue
  "Hockenheimring"; not a placeholder; 45 laps; 1989 round 9 history pointer). Banner
  `masters/hockenheim-1988.jpg` (`[MAN]`).
- **Country and location:** Hockenheim, Germany (`[H89]` layoutId `hockenheimring-2`;
  `[LIB]` location "Germany", country "DE").
- **Historical or modern year:** 1988 layout (`[LIB]` year 1988, Historic).
- **Circuit direction:** clockwise (`[H89]`).
- **Surface type:** permanent circuit, asphalt; 6.797 km (`[LIB]`), 16 turns (`[H89]`).
- **Pit and start-area configuration:** UNKNOWN.
- **Source-backed landmarks:** the forest itself: "a screaming blast from the pines into
  the stadium bowl", "endless forest straights", "a plume of smoke at the treeline"
  (`[CANON]` races."West Germany"). The treeline is the sanctioned silhouette. The
  stadium bowl exists in canon but its architecture is UNKNOWN.
- **Source-backed surrounding terrain and skyline:** dense forest only; nothing else.
- **Era-correct barriers, fencing, curbs, signage, safety equipment:** no barrier
  evidence; the season-5 frame (1994) is pre-Charter (global rule 6). Packet 6 shows a
  coasted car on grass at the treeline: grass at the forest edge is sanctioned by canon
  ("drivers coast to the grass", `seasons.json:265`).
- **Elements that must not appear:** Motodrom grandstand architecture, Ostkurve or
  chicane geometry, concrete-versus-Armco claims, signage, exact tree density or species,
  pit buildings, any modern (post-2002) layout feature.

## Lock 4: Australia (Adelaide), used by packet 2

- **Exact track and layout ID:** `adelaide_historic` (`[PACK]:1448-1456`, realVenue
  "Adelaide Street Circuit"; not a placeholder; 81 laps; 1989 round 16 history pointer).
  Banner `masters/adelaide-1988.jpg` (`[MAN]`).
- **Country and location:** Adelaide, Australia (`[H89]` layoutId `adelaide-1`; `[LIB]`
  location "Australia", country "AU").
- **Historical or modern year:** 1988 layout (`[LIB]` year 1988, Historic); the packet's
  scene is the 1998 Second Great Wall Sunday.
- **Circuit direction:** clockwise (`[H89]`).
- **Surface type:** street circuit, asphalt; 3.78 km, 16 turns (`[H89]` type STREET).
- **Pit and start-area configuration:** UNKNOWN.
- **Source-backed landmarks:** the walls: "a canyon of concrete and Armco, the last
  street fight where titles crack before Monaco", "threads these walls, inches from the
  concrete", "the last chicane" (`[CANON]` races."Australia"). Concrete walls and Armco
  close to the racing line are sanctioned; a last chicane exists but its geometry is
  UNKNOWN.
- **Source-backed surrounding terrain and skyline:** none.
- **Era-correct barriers, fencing, curbs, signage, safety equipment:** concrete walls and
  Armco (canon). The 1998 scene predates the Charter announced at Monaco that same year
  (`seasons.json:537`): no pace car, no silver medical car, no rebuilt barriers in this
  scene.
- **Elements that must not appear:** parkland or tree cover, any Adelaide skyline, any
  waterfront (none exists at the venue; do not add one), grandstand architecture, pit
  complex, fence types, kerb profiles, wreckage of any kind (the packet's scene is
  deliberately empty: the silence after, not the crash).

## Lock 5: Monaco (Azure Circuit), used by packets 1, 3, and 5

- **Exact track and layout ID:** `azure_circuit_2021` (`[PACK]:1547-1555`, realVenue
  "Circuit de Monaco"; not a placeholder; 78 laps; grid capped at 25; 1989 round 3
  history pointer). Banner `masters/monaco-modern.jpg` (`[MAN]`). AMS2 aliases the venue
  "Azure Circuit"; SMGP drives the 2021 layout. The finale never shuffles
  (`docs/SMGP_17_SEASON_STRUCTURE.md:16`).
- **Country and location:** Monte Carlo, Monaco (`[H89]` layoutId `monaco-5`; `[LIB]`
  location "Monaco", country "MC").
- **Historical or modern year:** driven layout year 2021 (`[LIB]`, 3.337 km); the
  modeled 1989 configuration is 3.33 km, 20 turns (`[H89]`). This era shift is declared
  here, not hidden.
- **Circuit direction:** clockwise (`[H89]`).
- **Surface type:** street circuit, asphalt (`[H89]` type STREET).
- **Pit and start-area configuration:** UNKNOWN (a grid cap of 25 is a format fact, not a
  pit description).
- **Source-backed landmarks:** "Armco, the tunnel dash, and the hairpin the whole game is
  named for" (`[CANON]` races."Monaco"). A tunnel, a hairpin, and Armco throughout are
  the only sanctioned structures. Tunnel lighting is era-dated: dark before the Charter,
  relit end to end in the Charter years (`seasons.json:582,1036`). Packet 5 uses the dark
  pre-Charter tunnel; packets 1 (1990, background motif only) and 3 (2006, relit) follow
  the same rule.
- **Source-backed surrounding terrain and skyline:** none.
- **Era-correct barriers, fencing, curbs, signage, safety equipment:** Armco (canon).
  Pre-Charter frames (packets 1, 5): no pace car, no silver medical car. The 2006 frame
  (packet 3): Charter-era furniture is permitted, rebuilt barriers and the distant silver
  medical car (`seasons.json:584,1036`).
- **Elements that must not appear:** the harbor, marina, or yachts; the casino, the
  palace, or any named building; the Mediterranean waterfront; the hillside cityscape;
  tunnel architecture or portals beyond an abstract glow; grandstands architecture; pit
  complex. All have zero repository evidence.

---

## Excluded venues and motifs (lore-lock gate decisions)

| Venue / motif | Reason excluded |
|---|---|
| France (Paul Ricard) | Placeholder: driven layout is `le_mans_bugatti` (2022), `[PACK]:258-266`; neither Ricard nor Bugatti visuals are evidenced. Cost: the Asselin slipstream triumph scene was dropped. |
| U.S.A. (Phoenix) | Placeholder: driven layout is `long_beach` (2020) with an `adelaide_historic` fallback, `[PACK]:555-565`; banner would show the wrong city or the wrong country. |
| Mexico (Hermanos Rodríguez) | Placeholder: driven layout is `interlagos_1991` whose banner cue says "in Brazil, circa 1991" (`[PRM]` masters/interlagos-1990s.jpg); canon demands banked sweep and altitude. Largest venue-versus-art mismatch in the library. |
| Silverstone (Senna 150th, packet 1 candidate motif) | Era-shifted driven layout (`silverstone_1991` for a 1990 scene) and its "silent grandstand" phrase collides with packet 2's grief motif. |
| Spa (Belgium rain, packet 3 candidate motif) | Era-shifted driven layout (`spa-francorchamps_1993`); not needed once the finale identity centers on Monaco. |
| Hungaroring, Suzuka (Kansai), modern layouts | Era-shifted (2025, modern aliases); no packet requires them. |

## Completion statement

All six track-scene packets (1, 2, 3, 4, 5, 6) have a completed lore lock above. Every
lock cites `[PACK]`, `[LIB]`, `[H89]`, and `[CANON]`. No lock relies on a placeholder
venue. No major visible feature in any packet is left unverified: anything without a
citation appears on the "must not appear" list. Packets 7 (garage) and 8 (pit-lane
memorial, venue-free) require no lock by design.
