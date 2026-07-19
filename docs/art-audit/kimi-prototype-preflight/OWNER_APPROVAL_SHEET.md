# Owner Approval Sheet: Eight Prototype Lore Packets

Mission: `SMGP-ART-LORE-PREFLIGHT-001`
State: `RESEARCH_AND_PLANNING_ONLY`
Date: 2026-07-18

This sheet asks the owner to approve, revise, or reject the eight lore packets in
`PROTOTYPE_LORE_PACKETS.md` (machine form: `prototype_lore_packets.json`).

**Scope of this approval.** Approving here approves the lore packets only: the selection,
the canon facts, the venue lore locks, the visible/forbidden element lists, and the
READY decisions. It does NOT open the Stage B production queue. The queue gate in
`docs/art-audit/NEWS_HISTORY_ART_PRODUCTION_QUEUE.md` still requires the owner to enter
exactly `APPROVE NEWS/HISTORY ART PASS`, and the P0 model/projection seam work still
precedes any image work. No image or image prompt exists yet.

## The eight packets

| # | Stable content ID | Expected asset ID | Category | Season / year | Confidence | Preflight decision | Owner verdict (APPROVE / REVISE / REJECT) | Owner notes |
|---|---|---|---|---|---|---|---|---|
| 1 | `smgp-season-lore:1` | `smgp-season:1` | Season identity, early: "The Tenth Summer" | Ordinal 1, 1990 | HIGH | READY | | |
| 2 | `smgp-season-lore:9` | `smgp-season:9` | Season identity, middle: "The Reckoning" | Ordinal 9, 1998 | HIGH | READY | | |
| 3 | `smgp-season-lore:17` | `smgp-season:17` | Season identity, final: "The Crown of Crowns" | Ordinal 17, 2006 | HIGH | READY | | |
| 4 | `smgp-dispatch:milestone.first-win` | `smgp-dispatch:milestone.first-win` | Race triumph: the rain coronation at Brazil | Almanac memory | HIGH scene, MEDIUM binding | READY (caveat: no maiden claim) | | |
| 5 | `ch.showdown.smgp` | `newsroom:ch.showdown.smgp` | Rivalry / championship pressure: the tunnel duel | Almanac memory + ordinal 8 frame | HIGH | READY | | |
| 6 | `dnf.mechanical` | `newsroom:dnf.mechanical` | Mechanical failure: the smoke plague at West Germany | Ordinal 5, 1994 (reference) | HIGH scene, MEDIUM scope | READY | | |
| 7 | `mt.transfer.smgp` | `newsroom:mt.transfer.smgp` | Transfer / contract: the works-engine winter | Ordinal 8, 1997 (reference) | HIGH | READY | | |
| 8 | `smgp-dispatch:setback.died` | `smgp-dispatch:setback.died` | Restrained fatality coverage: the circus falls silent | Sim career, opt-in only | HIGH | READY | | |

## What the owner is asked to confirm

1. The eight selections and their category balance (three season identities across
   early/middle/final, plus one each of triumph, rivalry, mechanical failure, transfer,
   and restrained fatality coverage).
2. The abstraction rule for Ayrton Senna: car, number, and palette only, never face,
   helmet, or trade dress (packets 1, 3, 5).
3. The restraint register for packets 2 and 8: no crash depiction, no wreckage, no
   bodies, no flames; silence, walls, flags at half-mast, the empty garage.
4. The exclusion of the three placeholder venues (France, U.S.A., Mexico) and of the
   era-shifted Silverstone and Spa motifs, at the cost of the Asselin slipstream scene.
5. The badge handling: packets 5-7 record `careerUniverse` with SMGP-scoped context
   filters, documented rather than "corrected".
6. The packet 4 caveat: the documented scene is a coronation, not a maiden win, so no
   "maiden/first" visual claim is permitted.
7. The packet 8 gating: art serves only the mortality opt-in; canon remains
   zero-fatality.

## Decisions recorded per packet

- APPROVE: the packet becomes the binding lore lock for its eventual Stage B brief.
- REVISE: note the field to change (owner notes column); the packet returns to research.
- REJECT: the packet is dropped; a replacement candidate is researched from the
  alternates already identified (`newsroom-template:first-win.feature`,
  `smgp-dispatch:world.title-tightens`, `newsroom-template:died.tragedy`,
  `newsroom-template:ri.died.remembered`, ordinal 8 as middle season).

## Sign-off

Owner: ______________________    Date: ____________

Verdict summary (counts of APPROVE / REVISE / REJECT): ______ / ______ / ______

---

LORE PREFLIGHT COMPLETE - NO ART GENERATED
Awaiting owner approval of the eight lore packets.
