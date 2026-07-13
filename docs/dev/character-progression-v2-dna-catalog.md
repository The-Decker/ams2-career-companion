# Character progression v2 — Racing DNA catalog

_Draft catalog, 2026-07-12. Companion to `character-progression-v2.md`._

Racing DNA is one immutable, zero-cost identity selected at creation. It is not a skill node, cannot be respeced, and remains mechanically visible when the mastery tree is complete. The starting build must still pass the creation stat/trait budget.

Passive percentages are tuning targets, not yet frozen constants. All passives derive from journaled career facts and consume no random stream. Context choices such as a circuit family, rival, season objective, or nationality affinity are explicit creation inputs and must be persisted.

| # | Stable ID | Racing DNA | Primary / secondary | Persistent identity | Permanent tradeoff / guardrail |
|---:|---|---|---|---|---|
| 1 | `dna_prodigy` | The Prodigy | Pace / Media | Exceptional results amplify reputation and offer-score movement; elite teams treat the driver as having a higher ceiling. | Expectations are permanently harsher and failures lose reputation just as quickly. |
| 2 | `dna_pole_sitter` | The Pole Sitter | Pace / Mental | Qualifying versus expectation contributes additional XP and reputation. | Sunday results produce slightly less progression; qualifying failures are more damaging. |
| 3 | `dna_engineer` | The Engineer | Pace / Team | Clean classified finishes generate a development credit for the current team. | Personal fame and sponsor gains are reduced; banked team affinity is lost on departure. |
| 4 | `dna_circuit_specialist` | The Circuit Specialist | Pace / Era | One persisted track family (street, power, high-speed, technical) grants enhanced progression. | Other track families provide reduced progression. |
| 5 | `dna_hard_charger` | The Hard Charger | Racecraft / Physical | Positions gained and aggressive overperformance pay enhanced XP and reputation. | Lost positions, driver-error DNFs, and crash injury exposure carry larger penalties. |
| 6 | `dna_giant_killer` | The Giant Killer | Racecraft / Team | Beating stronger team tiers scales OPI, XP, and reputation upward. | Equal/superior machinery pays less; a top seat is less rewarding. |
| 7 | `dna_closer` | The Closer | Racecraft / Mental | Results in the final third of a season receive stronger progression and reputation weighting. | The opening third advances more slowly and a late collapse is especially costly. |
| 8 | `dna_duelist` | The Duelist | Racecraft / Media | One persisted rival's head-to-head result is heavily weighted for XP, reputation, and headlines. | The rest of the field matters less; losing the rivalry hurts disproportionately. |
| 9 | `dna_comeback_veteran` | The Comeback Veteran | Physical / Era | After injury, missed races, or unemployment, recovery is shorter and return offers retain an age-risk floor. | Continuous uninterrupted seasons generate progression more slowly. |
| 10 | `dna_survivor` | The Survivor | Physical / Mental | The first serious accident each season receives a deterministic safety downgrade in severity. | Conservative perception reduces elite-team offer score and aggressive-result fame. |
| 11 | `dna_iron_athlete` | The Iron Athlete | Physical / Pace | Long-distance events and dense calendars avoid recovery erosion; minor injuries heal faster. | Sprint and qualifying achievements grant less XP and media value. |
| 12 | `dna_all_rounder` | The All-Rounder | Mental / Pace | Conditional weather/circuit/machinery bonuses and penalties are damped. | Specialist peaks are capped with specialist weaknesses. |
| 13 | `dna_points_machine` | The Points Machine | Mental / Physical | Consecutive classified finishes build a capped reliability dividend to XP and contract patience. | A DNF resets the streak; wins and poles create less sponsor/headline value. |
| 14 | `dna_ice_man` | The Ice Man | Mental / Racecraft | Negative form/reputation movement is damped in title deciders and high-pressure rounds. | Ordinary early/mid-season success provides less progression. |
| 15 | `dna_strategist` | The Strategist | Mental / Team | One persisted season objective (points, teammate, classification) grants relationship/development credit. | Individual-race XP is reduced; missing the declared objective forfeits the completion dividend. |
| 16 | `dna_pay_driver` | The Pay Driver | Business / Media | Portable sponsor backing opens offer shortlists and can bypass a normal reputation floor. | Salary, sporting reputation, and team patience are reduced by the stigma. |
| 17 | `dna_privateer` | The Privateer | Business / Team | Keeps more start/prize income and pays lower independent operating/repair costs. | Works-team interest and factory-development benefits are reduced. |
| 18 | `dna_mercenary` | The Mercenary | Business / Pace | Team switches grant a signing premium and remove the usual first-season adaptation penalty. | Loyalty never rises above neutral, long contracts are scarce, retention offers are weaker. |
| 19 | `dna_rain_master` | The Rain Master | Weather / Racecraft | Wet-round overperformance grants strongly enhanced XP, OPI, and reputation. | Dry-round progression is reduced, especially when merely meeting expectation. |
| 20 | `dna_storm_chaser` | The Storm Chaser | Weather / Physical | Mixed/changing-condition rounds damp penalties and reward successful adaptation. | Fully stable wet or dry rounds provide reduced progression. |
| 21 | `dna_sunshine_specialist` | The Sunshine Specialist | Weather / Pace | Dry/hot qualifying and race results earn enhanced progression and sponsor value. | Wet results carry a larger expectation and reputation penalty. |
| 22 | `dna_journeyman` | The Journeyman | Team / Era | Lower-tier teams, unfamiliar classes, and emergency vacancies use a reduced reputation floor for one-year offers. | Top-team salary ceiling, celebrity growth, and long-term elite interest are reduced. |
| 23 | `dna_loyalist` | The Loyalist | Team / Business | Consecutive seasons with one team stack relationship, development, and contract-patience benefits. | A team change erases the stack and imposes a first-season relationship penalty. |
| 24 | `dna_team_leader` | The Team Leader | Team / Media | Constructor objectives and teammate success contribute to reputation, relationship, and development. | Individual glory, salary bonuses, and win-focused sponsor value are reduced. |
| 25 | `dna_showman` | The Showman | Media / Racecraft | Comebacks, upsets, and dramatic results amplify headlines and sponsor growth. | Routine points pay less; visible failure causes larger reputation swings. |
| 26 | `dna_quiet_professional` | The Quiet Professional | Media / Mental | Media, scandal, and sponsor-health movement is damped; team patience improves. | Celebrity ceiling, sponsor upside, and breakout-weekend gains are lower. |
| 27 | `dna_national_hero` | The National Hero | Media / Era | Home-country races, teams, and compatible sponsors enhance reputation and commercial value. | Foreign teams/sponsors apply a modest offer-score penalty. |
| 28 | `dna_late_bloomer` | The Late Bloomer | Era / Mental | Early XP is reduced, later XP accelerates, and age-risk onset is deferred. | The opening career is weaker and slower to develop. |
| 29 | `dna_old_school_racer` | The Old School Racer | Era / Physical | Mechanical DNFs/reliability-poor machinery damage reputation less; dangerous-era teams value resilience. | Modern commercial/media/technology-led teams score the driver less favorably. |
| 30 | `dna_era_chameleon` | The Era Chameleon | Era / Weather | The first season after a class/era transition avoids unfamiliarity penalties and grants adaptation XP. | Repeated seasons in unchanged machinery build mastery and loyalty more slowly. |

## Coverage and preservation

The 13 shipped names are preserved conceptually: Prodigy, Late Bloomer, All-Rounder, Pay Driver, Rain Master, Journeyman, Hard Charger, Privateer, Comeback Veteran, Storm Chaser, Pole Sitter, Points Machine, and Showman. Seventeen identities are added.

Primary-family coverage is intentionally broad: Pace 4, Racecraft 4, Mental 4, and three each for Physical, Business, Weather, Team, Media, and Era.

## Creation data contract

Each DNA definition will eventually author:

```json
{
  "id": "dna_example",
  "version": 1,
  "name": "The Example",
  "primaryFamily": "pace",
  "secondaryFamily": "team",
  "startingStats": {},
  "startingMeta": {},
  "startingTraitIds": [],
  "persistentEffects": [],
  "tradeoffEffects": [],
  "choice": null
}
```

The creation screen may present a complete valid preset and still allow advanced stat/trait customization. V2 customization spends a separate versioned Creation Budget; unused budget never becomes Skill Points. Customization does not erase DNA. Random creation journals the rolled DNA and final normalized profile; replay never rolls again.
