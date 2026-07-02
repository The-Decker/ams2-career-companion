# Season pack format v1 (M2 contract — draft)

A season pack is a plain JSON folder (Notepad-editable, zip-shareable) under
`Documents\AMS2CareerCompanion\Packs\<packId>\`. Validated against a bundled JSON Schema on
import; copied + hashed into the career DB at season start (immutable and pinned). Packs
REFERENCE OverTake skin packs by name/URL — they never ship textures.

Locked product decisions this format encodes:

- **Rounds default to 100% real race distance** — `laps` is the historical race length, and
  the whole app (difficulty recommendation, OPI, reliability expectations) assumes full-length
  races.
- **Every round carries a `setupGuide`** rendered on the Race Day briefing screen: the exact
  in-game session settings (with copy buttons) plus optional car-setup notes from the author.

## Files

| File | Purpose |
|---|---|
| `pack.json` | Manifest: identity, version, requirements, licensing/attribution |
| `season.json` | Calendar + points system + season-level rules |
| `teams.json` | Teams: car binding, performance, reliability, prestige, budget tier |
| `drivers.json` | Drivers: ratings (custom-AI vocabulary), aging anchors, track form |
| `entries.json` | Who drives what, when: team+driver+number+rounds+livery binding |

## pack.json

```jsonc
{
  "packId": "f1-1967",
  "name": "Formula One 1967",
  "version": "1.0.0",
  "formatVersion": 1,
  "gameVersionTested": "1.6.9.8",
  "license": "CC BY 4.0",
  "attribution": ["Historical data derived from f1db (github.com/f1db/f1db, CC BY 4.0)"],
  "requires": {
    "dlc": [],                                  // Steam DLC names needed for cars/tracks
    "skinPacks": [
      {
        "name": "F1 1967 Season (Alain Fry)",
        "url": "https://www.overtake.gg/downloads/...",
        "overridesFolder": "F1_Season_1967"     // folder name under CustomLiveries\Overrides\<vehicle>\
      }
    ]
  }
}
```

## season.json

```jsonc
{
  "year": 1967,
  "seriesName": "Formula One World Championship",
  "ams2Class": "F-Vintage_Gen1",               // EXACT xmlName from data/ams2/classes.json
  "pointsSystem": { /* same shape as a data/rules/f1-points-systems.json season entry:
                       racePoints, fastestLap, sharedDrivePolicy, driversBestN,
                       constructors, roundOverrides, pointsAdjustments ... */ },
  "rounds": [
    {
      "round": 1,
      "name": "South African Grand Prix",
      "date": "1967-01-02",
      "championship": true,                     // false = non-championship event
      "track": {
        "id": "kyalami_historic",               // internal id from data/ams2/tracks.json
        "fallbacks": ["kyalami_2020"]           // when the venue/era layout is missing
      },
      "laps": 80,                               // 100% historical race distance
      "setupGuide": {
        "session": {                            // exact in-game custom-race settings
          "opponents": 17,                      // grid minus player; preflight checks vs track AI cap
          "startTime": "14:30",
          "date": "1967-01-02",                 // in-game date (weather/sun position)
          "weatherSlots": ["Clear"],            // up to 4, AMS2 weather names
          "timeProgression": "1x",
          "mandatoryPitStop": false
        },
        "notes": "Kyalami rewards low wing; watch fuel load at altitude."   // author free text, optional
      },
      "guestEntries": [],                       // Indy-500-style per-round entrants
      "aiOverrides": {}                         // per-round rating tweaks (driver id -> partial ratings)
    }
  ]
}
```

## teams.json / drivers.json / entries.json

```jsonc
// teams.json
{ "teams": [ {
  "id": "team.brabham",                          // lineage id, stable across era packs
  "name": "Brabham-Repco",
  "carVehicleIds": ["formula_vintage_g1m2"],     // data/ams2/vehicles.json ids this team runs
  "performance": { "weightScalar": 1.000, "powerScalar": 1.000, "dragScalar": 1.000 },
  "reliability": 0.93,                           // maps to vehicle_reliability
  "prestige": 5, "budgetTier": 5
} ] }

// drivers.json — ratings use the custom-AI vocabulary verbatim (0.0–1.0)
{ "drivers": [ {
  "id": "driver.j_brabham", "name": "Jack Brabham", "country": "AUS", "born": 1926,
  "ratings": { "raceSkill": 0.93, "qualifyingSkill": 0.94, "aggression": 0.55, "defending": 0.42,
               "stamina": 0.79, "consistency": 0.80, "startReactions": 0.89, "wetSkill": 0.84,
               "tyreManagement": 0.79, "avoidanceOfMistakes": 0.71 },
  "trackForm": { "kyalami_historic": 0.03 }      // additive nudges, -0.05..+0.05
} ] }

// entries.json — livery binding is the load-bearing string
{ "entries": [ {
  "teamId": "team.brabham", "driverId": "driver.j_brabham", "number": "1",
  "rounds": "1-11",                              // ranges/lists for mid-season swaps
  "ams2LiveryName": "Brabham-Repco #1 J. Brabham" // EXACT livery display name (case-sensitive)
} ] }
```

## Validation on import

1. Schema-valid JSON (bundled JSON Schema, clear line-level errors).
2. `ams2Class` exists in the content library with exact casing.
3. Every `track.id` + fallback exists in the track library; `setupGuide.session.opponents + 1 ≤ maxAiParticipants` per venue.
4. Every `ams2LiveryName` checked against installed override scan + stock livery list (warning when the required skin pack is not installed — content-verification screen offers proceed-anyway).
5. `pointsSystem` parses through `PointsSystemCatalog`'s season shape; round count matches `driversBestN` split segments.
6. Entries reference existing teams/drivers; no livery double-binding per round; every championship round has ≥ 1 entry and a `setupGuide`.
