# SMGP narrative bible

_The authoritative writing + narrative guide for the Super Monaco GP module (mode-separation
finalization mission). Every content author and every generator change follows this document.
It is binding for tone, canon rules, and mode separation._

## 1. The world

The SMGP championship is a closed fictional Grand Prix universe: 24 teams, 34 drivers, 16 races
a season, 17 seasons across four eras. It is loud, fast, dangerous, and self-mythologizing —
the SEGA arcade's idea of Grand Prix racing written by people who love the sport. The big board
flashes names ten feet tall; the D.P. board keeps the score; the LEVEL A–D ladder is the law of
the paddock. A. Senna is the permanent benchmark, the Madonna #1: never nerfed, never dropped,
the name every career is measured against.

The press inside this world is breathless but honest. It reports what happened, it dramatizes
what mattered, and it never invents outcomes. The newsroom has six desks with their own voices;
race reports read like wire copy, features read like a monthly magazine, and the rumor desk
labels its own uncertainty.

## 2. Canon categories

### Immutable background canon (pre-career truth)

Established before the player's career and never rewritten by play: earlier championships and
their remembered rulers (the almanac in `data/rules/smgp/what-really-happened.json`), team
origins and philosophies (`team-profiles.json`), driver biographies and predetermined career
stats (`driver-profiles.json`, `driver-stats.json`), circuit legends, the 17 seasons' authored
identities (`seasons.json`). This is `SmgpFiction` provenance — presented as the world's own
story of itself, never as verified history, and divergence from it is badged SMGP UNIVERSE.

### Season-opening canon (true at season start)

The starting roster and hierarchy, the season's title/era/context/arcs (the authored identity),
preseason expectations, existing rivalries, contract situations. Canonical at the flag; the
season then writes itself from results.

### Save-generated history (the player's career)

Every race winner, podium, record, crash, injury, replacement, transfer, promotion, demotion,
rivalry swing, championship, retirement, and death produced by the actual fold. **Never
hardcode a later outcome.** The opening state may be canonical; the finished season belongs to
the save. Played results are never silently retconned; an explicit user correction keeps the
audit trail.

## 3. Mode separation (binding)

- **SMGP** tells the driver's story inside this universe: race weekends, qualifying, teammates,
  rivals, the ladder, contracts from the cockpit, injuries, replacements, mechanical drama,
  championships, career milestones. Finances exist only as the driver-facing prize/salary
  chatter of the world, never as management sim.
- **Dynasty** tells the owner's story: money, sponsors, staff, facilities, development, the
  board. Its events (sponsor signed, repair bill, near-bankruptcy, windfall, bankruptcy,
  development milestone) require an actual `DynastyEconomyState` and can never fire in SMGP.
- **Racing Passport** tells nothing but the racing: a faithful historical season with no
  fictional narrative layer beyond the era-voiced generic newsroom.
- **System notices** (migrations, recoveries, content warnings) are application notices, never
  motorsport prose.

Isolation is proven by `ModeNarrativeIsolationTests` (event-spine level) and the era-key
override (`PreferredEra = "smgp"` only inside SMGP careers).

## 4. The 17 seasons (identity map)

| # | Title | Era | Act |
|---|---|---|---|
| 1 | The Tenth Summer | The Iron Circus | I |
| 2 | The Protest Year | The Iron Circus | I |
| 3 | The Wet Season | The Iron Circus | I |
| 4 | The Closing Door Opens | The Iron Circus | I |
| 5 | The Horsepower Spring | The Horsepower War | II |
| 6 | The Temple Wars | The Horsepower War | II |
| 7 | The Spending War | The Horsepower War | II |
| 8 | The Boiling Point | The Horsepower War | II |
| 9 | The Reckoning | The Horsepower War | II |
| 10 | The Charter Season | The Safety Reckoning | III |
| 11 | The Craftsman's Year | The Safety Reckoning | III |
| 12 | The Frost Blooms | The Safety Reckoning | III |
| 13 | The Veterans' Autumn | The Safety Reckoning | III |
| 14 | The Jewel Formula | The Golden Circus | IV |
| 15 | The Insurgent's Last Climb | The Golden Circus | IV |
| 16 | The Silver Jubilee | The Golden Circus | IV |
| 17 | The Crown of Crowns | The Golden Circus | IV |

Arc I (Iron Circus): the establishment; raw speed, raw danger, the old order. Arc II (Horsepower
War): escalation; money, engines, excess. Arc III (Safety Reckoning): the cost; grief,
craft, protection. Arc IV (Golden Circus): the spectacle perfected; jewels, jubilees, the
crown of crowns the whole climb points at.

## 5. Editorial voice

- Period international motorsport journalism, easy to read, dramatic without parody. Wire-copy
  verbs, magazine nouns, zero modern anachronisms (no social media, no telemetry jargon, no
  modern broadcast culture).
- Banned crutches (sparingly at most): "stunning", "shocking", "against all odds", "only time
  will tell", "the racing world was left stunned", "fans around the world".
- Interview clichés ("we gave it everything", "the team did a great job") are seasoning, never
  the meal.
- Fatal incidents are reported with seriousness and restraint: no comedy, no celebration, no
  playful framing. Severity shapes register before anything else.
- Variables must resolve: no raw tokens, no "Unknown Driver", no placeholder headlines. An
  optional fact falls back to graceful copy, never to a blank.

## 6. Event priority and suppression

One result, one lead story. Priority rules the newsroom already enforces (EditorialSelection):

1. Death/serious injury suppresses everything else about the event (no DNF humor, no routine
   crash copy).
2. Championship clinch leads; the race win is its supporting context.
3. First-ever (win/podium/team first) outranks routine results.
4. A contender's mechanical failure is a championship-impact story, not a reliability note.
5. A final-race defeat suppresses optimistic charge-continues stories.
6. Career milestones (first points, first win, records) outrank their trigger event's routine
   coverage.
Everything else is supporting, follow-up, or archive-only.

## 7. Arc families (continuing stories)

Multi-stage arcs with entry/escalation/resolution/cancellation conditions, cooldowns, and a
historical record. Families (StoryThreads + SMGP beats today; expansion targets per season in
the coverage matrix): rivalry, teammate conflict, rookie rise, comeback, underdog, contract,
championship, team crisis, injury and safety.

## 8. Repetition control

Published copy is persistent (the same article renders the same words forever). Selection is
deterministic per save (seeded), arc-aware, season-aware, importance-weighted, with family
cooldowns and duplicate-body detection. Common events carry multiple genuinely different
variants (angle changes, not synonym swaps).
