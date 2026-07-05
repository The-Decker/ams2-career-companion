# Character system — design spec (synthesized, 2026-07-05)

**Status:** design artifact. DESIGN ONLY — this describes a later build. Nothing here is code
committed; the deliverables are this spec + `data/rules/perks.json` (**42 perks** as data — well
over Mike's 33 minimum, so the balance audit can cut any weak ones and still leave 33+). Grounded
in the shipped deterministic sim (`src/Companion.Core/Career/**`), PLAN.md (8 locked decisions),
`docs/dev/career-sim.md`, and the career-hub design. Three independent design angles (sim-lever-first,
RPG-progression-first, immersion-archetype-first) were reconciled into the one model below.

> This is a "design together" artifact. The **Open questions** at the end are the taste/scope calls
> for Mike. Everything above them is a proposal, steerable.

---

## 0. The thesis in one paragraph

A character is **7 stats + a perk build + a level**. Stats and perks are nothing but **typed
patches onto deterministic inputs and output-weights that already exist** in
`src/Companion.Core/Career`. Five stats map 1:1 onto the player seat's stored `PackDriverRatings`;
two are career meta-stats with no rating analog. Every perk is a data object whose **benefit and
drawback are both machine-readable effects** on named levers, so the net is arithmetic and a CI
audit can prove no perk is strictly-dominant or strictly-trap. Balance is a **hybrid**: a
character-point budget prices each perk, drawback-heavy perks refund points, and — independently —
every perk carries a paired quantified drawback so free choice is genuinely viable. Everything folds
through the existing named PCG32 streams + append-only journal, so **the same master seed + the same
entered results reproduce the career byte-for-byte**. No perk decides a race; no hidden dice.

---

## 1. Why this synthesis (reconciling the three angles)

| Angle | Kept | Dropped / merged |
|---|---|---|
| **A — Sim-lever-first** | The spine: every effect is a typed patch onto a named lever, journaled once at creation, replayed for free. The `PlayerPerkModifiers` struct threaded as identity-defaulting optional params. The arithmetic balance audit against exact coefficients. | Its 7-stat naming and 18-perk roster folded into the shared set below. |
| **B — RPG-progression-first** | The XP-as-pure-function-of-results curve, era-aware aging as the antagonist leveling fights, the respec discipline, the "flavor stats are round-conditional car-scalar nudges" honesty. | Its separate stat layering merged with A's; its 20 perks merged. |
| **C — Immersion-archetype-first** | Archetype presets as **pre-spent point templates**, the era-voiced descriptions, the "spend the pool" creation flow, the per-lever delta caps enforced on load, the news/fiction framing. | Its 8-stat model collapsed to the shared 7; its injury/form/superstition streams reconciled to two new streams. |

All three independently reached the same non-negotiables (map stats onto `PackDriverRatings`; flavor
ratings are inert for the human seat so model them as the player's own car scalars; hybrid budget +
paired drawback; new streams for injury/form). Those are load-bearing and kept verbatim.

---

## 2. Stats

**Seven stats, each `0.0–1.0`**, authored at creation, stored on an extended `PlayerCareerState`,
folded from a `player.character` journal row like every other state change. Creation clamps every
stat to **`0.15–0.85`** so nothing is degenerate; level points and aging can push a stat outside
that band later (cap `0.99`, floor `0.0`).

### 2.1 The five talent stats (map 1:1 onto the player seat's `PackDriverRatings`)

Each talent stat writes one or two fields of the player entry's `PackDriverRatings` via a fixed
mapping. The mapping is authored in `data/rules/character-stats.json` and validated on load exactly
like ratings are range-validated today.

| Stat | Writes rating field(s) | What it moves in the sim |
|---|---|---|
| **Pace** | `raceSkill` | `SeatStrengthModel.Strength` (0.3 weight) → `ExpectedFinish` → OPI/rep bar; `PaceAnchorMath` yardstick + `MedianAiRaceSkill`; `DifficultyModel.RecommendSlider` target. |
| **OneLap** | `qualifyingSkill` | Grid-pace identity + briefing narrative; the pace-anchor split from Pace when a perk buys it. |
| **Craft** | `avoidanceOfMistakes` (+ `consistency` avg) | Fewer driver-error DNFs in fiction; pairs with the OPI blame-softening perks. |
| **Racecraft** | `aggression` + `defending` (avg) | Overtaking/defending narrative + the "beat-teammate" and "overperformed" headline causes. |
| **Adaptability** | `wetSkill` + `tyreManagement` (avg) | Round-conditional edge on wet / long-distance rounds (see §2.3). |

**Mapping rule (deterministic, in `character-stats.json`):**
`writtenRating = clamp(base + span * stat + Σ perkDeltas, 0, 1)` with `base = 0.35`, `span = 0.55`
by default — so a flat `0.5` character writes `~0.625` ratings (a plausible midfield rookie) and a
maxed talent stat writes `~0.90`. Per-field `base`/`span` are authored, so a pack can retune.

**The honesty constraint (all three angles flagged it — keep it loud in the UI).** AMS2 ignores the
player seat's AI skill fields *while the player is driving that livery* (per the `GridSeat`/`PlayerSeat`
docs: "the AI skill fields stay: they are inert while the player is in the car"). So these five stats
**do not secretly drive the race.** They set the sim's **expectation** of the player (`expectedFinish`),
the **difficulty recommendation**, and the **fiction** — then the player's real driving is judged
against that expectation. This is the built-in self-balancer: higher Pace → higher `expectedFinish`
→ a **harder** OPI/reputation/XP bar (you must beat a better-expected finish to gain), and the pace
anchor recommends a **stiffer** slider. Raw talent is taxed automatically.

Where a perk needs a talent effect to bite the *actual* race (not just expectation), it is
implemented as a **round-conditional nudge to the player's own `WeightScalar`/`PowerScalar`/`DragScalar`**
— the exact lever the grid stager already writes into the generated file, which genuinely affects the
player's AMS2 car (v1.6.9.8+ scalars affect the player's car too). This is the one lever that touches
the on-track car; it never touches an AI seat and never sets a finishing position.

### 2.2 The two career meta-stats (no rating analog)

| Meta-stat | Stored | Consumed by |
|---|---|---|
| **Marketability** | `PlayerCareerState` | Pre-multiplier on `ReputationMath.RoundDelta`/`SeasonDelta` (`1.0 + 0.5·(Marketability−0.5)`, so `0.5` is neutral) and a scalar on the rep term the player carries into `OfferScore` + `SalaryOffer` interpolation. A fame lever, not a pace lever. |
| **Durability** | `PlayerCareerState` | Shifts the effective age fed to `RetirementHazard.Probability` and the `ageRisk` offer term by `ageShift = round(6·(Durability−0.5))` years (tough driver races ~3 yrs longer; fragile retires ~3 early), and scales the new opt-in season-end **injury** hazard. A longevity lever, not a pace lever. |

Marketability and Durability are the "career meta-stats like reputation/fitness/marketability/focus"
the brief asked for, collapsed to the two that have real, distinct sim consumers. (Reputation itself
is already a first-class sim output on `PlayerCareerState`; Focus/fitness are folded into Durability
+ the XP curve rather than added as dead stats.)

### 2.3 Round-conditional stats

Adaptability (and the perks that lean on it) only bite when the round's conditions invoke them, so
they can't be min-maxed into an unconditional `expectedFinish` gain:

- **Wet round** (pack flags the round wet): the wet edge applies.
- **Long race** (round distance ≥ 60% of the era's longest race): the tyre/fuel edge applies.
- **Otherwise**: the paired dry/short drawback applies.

Condition evaluation reads pack round data that already exists (weather, distance) — no new state.

---

## 3. Levels, XP & progression

### 3.1 XP is a pure function of journaled results (no dice)

XP is derived from the results the player enters, so replay from the same seed + same results
reproduces every level byte-for-byte and **no stream is consumed for XP**. A new pure `XpMath`
lives beside `OpiMath`; `RoundUpdate` emits a `player.xp` journal row per round, `SeasonEndPipeline`
step 2 emits one alongside the existing `player.experience` row (additive, same strict order).

**Per-round XP** (all terms read straight from the values `RoundUpdate` already computes —
`expectedFinish`, `effectiveFinish`, the `RaceCause`):

```
xpRound = clamp((expectedFinish - effectiveFinish) * perPlace, floor, cap)
        + resultBonus(cause)
```

Defaults (in `perks.json` → `levels.xpSources.perRound`): `perPlace 6`, `floor -30`, `cap +60`;
`win 40 / podium 20 / points 10 / beatTeammate 8 / dnf-driver-error -15 / dnf-mechanical 0`.
Overperforming in a weak car is the richest XP — it mirrors `ReputationMath`'s underdog logic, so a
backmarker career still levels.

**Per-season XP** (`levels.xpSources.perSeason`): `championship 300 / top3 150 / top10 60 /
seasonCompleted 40`.

### 3.2 The curve and what a level grants

`data/rules/perks.json → levels.xpCurve`: `xpForLevel(n) = round(baseXpToLevel2 · growth^(n-2))`,
default `baseXpToLevel2 100`, `growth 1.35`, `maxLevel 30`. Cumulative thresholds are precomputed and
validated strictly increasing on load. ~2–4 levels/season early, tapering; level 30 is only reachable
across a long multi-era career.

Each level grants **Character Points (CP)** into the **same points-buy pool used at creation**,
spendable **between seasons only** (mirrors the offer/rollover cadence). Default **2 CP/level**
(`levels.levelGrants.statPointsPerLevel`, raisable by the *Consummate Pro* perk). CP buy either a new
perk or a **+0.05 stat step** (cost in `levels.levelGrants`). Every level-up and every spend is
journaled (`player.level`, `player.statSpend`); the sim never auto-spends.

### 3.3 Era-aware aging interplay — leveling is the counterweight to aging

The shipped `AgingCurve` is the antagonist. Leveling grants CP but does **not** halt aging: past the
era's `PeakAgeEnd`, the season-end aging pass (`stream: aging`) still drifts the player's talent stats
down. The arc:

- **Early career:** rising `AgingCurve.RisePerYear` + fast XP → you climb.
- **Peak (`PeakAgeStart..End`, era-shifted 28→26 across decades via `AgingCurveSet.ForYear`):** you bank ceiling.
- **Veteran:** accelerating `DeclinePerYear + DeclineAccelPerYear·(age−peak−1)` claws ratings back; your CP are spent just holding station.

Perks like **Late Bloomer** / **Adaptable** apply a **player-local curve override** (shift peak, halve
decline-accel) — never touching AI drivers. Because the curve is looked up per calendar year, peaks
shift by decade automatically; an author-set `softCapByEra` ties max level to era so a 1967 career and
a 2022 career reach comparable ceilings. All data, never compiled.

XP never feeds a race outcome — it only unlocks CP — so determinism and "no hidden dice" hold.

---

## 4. The balance mechanism (hybrid — and why)

**Hybrid = a character-point budget + a mandatory paired drawback on every perk.** Justification: the
two mechanisms guard different failure modes, and each covers the other's gap.

1. **Points budget** prices power and lets refunds fund ambition. Default **10 CP** at creation
   (`characterPoints.creationBudget`). Positive-cost perks spend; **drawback-heavy perks refund**
   (negative cost); net spend must land in `[0, budget + maxRefundHeadroom]` with
   `maxRefundHeadroom 6` (so a fragile/greedy build can bank refunds to afford premium perks, but
   can't infinitely stack drawbacks). Leftover CP buy +0.05 stat steps.

2. **Paired drawback**, independent of cost, guarantees **no strictly-dominant and no strictly-trap
   perk** regardless of how the community reprices things. Every perk ships a benefit AND a drawback,
   both expressed as machine-readable effects on real levers, on **axes the benefit doesn't touch**.

3. **The sim is a third, automatic governor.** Talent benefits raise `expectedFinish`, which raises
   the OPI bar (`OpiMath` gain `0.2·(expected−effective)`), the `ReputationMath` `vsExpectation`
   clamp, *and* the recommended difficulty slider. So a min-maxed "fast" build is auto-taxed by the
   math — the budget doesn't have to be arithmetically perfect to stay fair.

**Why three kinds of "free choice" all stay viable:**

- **Net-neutral trait perks** (`cost 0`, e.g. *Backmarker Grinder*, *Streaky*, *Featherweight Setup*)
  are pure playstyle — great for one career shape, bad for another, dominant in neither.
- **Every paired drawback is on the axis the benefit helps** (Wonderkid +40% early XP / −25% late XP
  is lifetime-neutral; Rainmaster strong wet / weak dry; Tyre Whisperer quick long / slow short), so
  the perk "works out or not as the career unfolds" — exactly Mike's bar.
- **Refund perks** (`cost < 0`) sell a real drawback to fund a real strength; they never refund with
  a costless upside.

### 4.1 The audit (CI-testable, arithmetic against the exact coefficients)

A unit test over `perks.json` asserts, using a common **CP-equivalent coefficient table** (the single
source of truth for "what a lever-delta is worth in CP"):

- **(a)** For each perk, `Σ benefit-CP − Σ drawback-CP` is within `±0.5` of its declared net `cost`.
- **(b)** No perk has positive benefit-CP with zero drawback-CP (no free lunch).
- **(c)** No perk has negative benefit-CP with zero benefit (no pure trap).
- **(d)** Every `statDelta` per perk is within `±0.15`, every `carScalar` within `±0.015`, every
  weight/rate multiplier within `±40%` — per-lever caps so community JSON can't smuggle an outlier.
  **Two documented exceptions the loader must allow (else it rejects intended perks):** (1) a single
  **signature-specialism `statDelta`** — `wetSkill` / `tyreManagement` / `chosenFlavor` — may reach
  `±0.30` when it is the perk's defining identity and is paired with a drawback (Rain Man, Sunshine
  Specialist, One-Trick Pony); (2) a **round-conditional (weather/distance) `carScalar`** may reach
  `±0.040`, because it only bites a calendar fraction so the calendar-weighted expectation stays inside
  the unconditional `±0.015` envelope (Rain Man wet `+0.020`, Sunshine Specialist wet `−0.040`). The
  audit enforces the calendar-weighted expectation, not the raw conditional magnitude.
- **(e)** Every effect with randomness names a **registered** stream (`injury`, `form-swing`, or an
  existing `CareerStreams` name). **The `injury` stream auto-enables** for any character that takes an
  injury-stream perk (see §6.2): the injury half of a perk's cost is therefore never a disabled freebie
  or a disabled tax, so the balance holds whether or not the *global default-off* injury setting is on
  for characters that took no injury perk.
- **(f)** Replaying each authored **archetype preset** reproduces a valid, in-budget build.

The CP-equivalent table (illustrative anchors, authored alongside the audit): `raceSkill ±0.05 ≈ 1 CP`
(because it moves `expectedFinish` and is auto-taxed), a flavor `statDelta ±0.10 ≈ 0.5 CP` (conditional),
`carScalar ±0.01 ≈ 1 CP` (`CarScore ±0.1`, real on-track), `reputationGainRate ±25% ≈ 1 CP`,
`income ±2 BU ≈ 1 CP`, `injuryHazard +0.10 ≈ −1 CP` (a drawback), `xpRate ±25% ≈ 0.5 CP` (front/back
loading is ~neutral over a career).

**The load-bearing asymmetry the audit surfaced (keep it in mind when repricing).** `raceSkill` and
`carScalar` both feed `SeatStrengthModel.Strength`, but not equally: `Strength = 0.6·CarScore + 0.3·raceSkill`
with `CarScore = (power−weight−drag)·10`, so a **`carScalar power +0.01` moves Strength by +0.06** while a
**`raceSkill +0.05` moves it by only +0.015** — a **4× larger** expectation swing per stated CP. And the
player seat's AI ratings are **inert on track** (the human is driving), so `raceSkill` buys *only*
expectation + fiction, whereas `carScalar` buys the **same expectation tax plus real AMS2 lap time**.
Net: `carScalar` is strictly the more powerful lever per CP. The anchors above hold *only because* the
sim auto-taxes the bigger Strength swing (a car-fast build faces a stiffer OPI/rep/slider bar), but that
tax is not a licence to stack unconditional `carScalar` cheaply — hence the audit re-scored the
unconditional-`carScalar` perks (glass_cannon, hot_head) down and requires every `carScalar` benefit to
carry a real, *always-live* paired drawback rather than a disable-able one.

---

## 5. Creation flow

Slots into the shipped new-career wizard as **one added `WizardStep`**, AFTER seat pick (so the seat's
tier/archetype is known and the wizard previews offer-scoring implications live) and BEFORE the rules
preset confirm: `SeasonPick → Verification → SeatPick → [Character] → RulesPreset → Confirm`. Strictly
additive; `ICareerSession` gets no non-default seam member for creation (the character record is
written at Confirm, folded from a journal row). Everything is mouse-or-keyboard operable (decision 8).

**Three tiers of depth:**

1. **Archetype preset (one click, the default).** Pick one of the **era-flavored cards** — each is a
   **pre-spent point template**: a stat spread + 2–3 signature perks summing to a valid in-budget
   build, with a one-paragraph period-voice bio and a "how the press sees you" preview. Picking a card
   and clicking Start is a complete, valid character. Presets are pure data
   (`perks.json → creation.archetypes`); nothing is forced.

2. **Free customize (optional toggle).** The archetype's picks pre-fill; the player drags all 7 stat
   sliders (clamped `0.15–0.85`) and adds/removes perks from a shelf **grouped by category** (Pace /
   Racecraft / Physical / Mental / Business / Weather / Team / Media / Era-flavor). Each perk shows its
   exact benefit / drawback / net-cost; a live **remaining-CP meter** refuses Start until net spend is
   in `[0, budget+headroom]`; picking a refund perk visibly returns CP.

3. **Advanced (disclosure).** Shows the raw `stat → written-rating` numbers and each perk's exact lever
   deltas — for tinkerers and pack authors.

**A "Random balanced build" button** draws from a NAMED creation stream (`character-gen`, seeded off
`masterSeed`) so even randomized creation is replayable.

**On Confirm** the wizard writes **one journaled creation event** — a `player.character` journal row
carrying `{stats, perkIds, cpSpent}` — so `state = fold(journal)` includes character setup and re-sim
is exact. The player seat's stored `PackDriverRatings` are patched from the five talent stats + perk
deltas at this point (the same merge path the grid resolver already uses), so from round 1 the sim's
`expectedFinish` reflects the character with no special-casing.

---

## 6. How perk effects fold deterministically through the sim + journal

The **replay contract** (`state = fold(journal)`, byte-identical on re-sim) is preserved *by
construction*: perk ids + CP spend live in `player_state`, are journaled once at creation, and every
per-round / season-end effect is a **deterministic data patch applied before the same shipped code
runs**. Two implementation seams, both identity-defaulting so existing careers and the f1db oracle
stay byte-identical:

### 6.1 `PlayerPerkModifiers` — an identity-defaulting struct threaded into the pure functions

Today `OpiMath`, `ReputationMath`, `PaceAnchorMath`, `SeatStrengthModel`, and `AgeOneSeason` are static
pure functions over scalars. The additive change is a single optional `PlayerPerkModifiers`
parameter **defaulting to identity (no-op)**. It carries the small set of coefficients perks read:

| Modifier field | Default | Lever it patches |
|---|---|---|
| `talentDeltas` (per rating) | `0` | player seat `PackDriverRatings` at grid resolve |
| `carScalarDeltas` (weight/power/drag, + round-conditional variants) | `0` | player seat scalars at grid resolve |
| `opiRetention` | `0.8` | `OpiMath.Retention` (player-local) |
| `errorBlameScale` / `blameFloorBlend` | `1.0` / `0.0` | `OpiMath.EffectiveFinish` driver-error branch |
| `repRoundMult` / `repSeasonMult` / `marketability` | `1.0`/`1.0`/`0.5` | `ReputationMath` deltas |
| `underdogBonusLowTier` / `topTierRepMult` | `0.0` / `1.0` | `ReputationMath.UnderdogMultiplier` (player-local) |
| `anchorAlpha` | `0.3` | `PaceAnchorMath.Alpha` (player-local) |
| `peakShift` / `riseMult` / `declineAccelMult` | `0`/`1.0`/`1.0` | player-local `AgingCurve` override in `AgeOneSeason` |
| `offerExpMult` / `salaryAskMult` / `ageRiskMult` / `repFloorRelaxTiers` | `1.0`/`1.0`/`1.0`/`0` | `OfferScore` inputs + rep-floor gate |
| `payBudgetBu` / `salaryOfferMult` | `0` / `1.0` | seat-market pay weight + `SalaryOffer` |
| `xpMults` (per cause) / `injuryDurabilityDelta` | `1.0` / `0` | `XpMath` + injury hazard |

`PlayerPerkModifiers` is **computed once** from the folded `{stats, perkIds}` (a pure function
`PerkResolver.Resolve(character) → PlayerPerkModifiers`) and passed into each call site. Because it is
derived from journaled creation state, the fold reproduces it exactly — nothing new is journaled
per-round for a deterministic perk.

### 6.2 Named streams for the randomness perks (register in `CareerStreams`)

Only three perks families add randomness, and each names a **new registered stream** keyed
`(subsystem, year, round, entity)` exactly like the existing streams, so a new subsystem never
perturbs an existing sequence:

| Stream | Const to add | Used by | What the draw decides (never a race outcome) |
|---|---|---|---|
| `injury` | `CareerStreams.Injury` | Glass Cannon, Ironman, Hot Head, Safe Hands, Iron Constitution, Injury-Prone, Hard Charger | An **opt-in** season-end injury check vs the durability-scaled hazard (shaped like `RetirementHazard`). A hit forces a **mechanical-classed, OPI-neutral missed round** (journaled `player.injury`) — it removes your *availability*, never sets the finishing order. Keyed `(injury, year, 0, "player")`. |
| `form-swing` | `CareerStreams.FormSwing` | Streaky, Superstition | A ±jitter that only decides **which side of a borderline form threshold** a round lands on (the form window itself is folded deterministically from journal results). Keyed `(form-swing, year, round, "player")`. |
| `character-gen` | `CareerStreams.CharacterGen` | "Random balanced build" button | The randomized creation draw. Keyed `(character-gen, 0, 0, "player")`, seeded off `masterSeed`, journaled once. |

All three are **gated / opt-in** (injury behind a setting so default careers add no new draws; form-swing
only if a form perk is taken; character-gen only if the random button is used), so a default career
consumes **zero** new draws and stays replay-compatible with pre-character saves.

**Injury auto-enable (balance audit fix, 2026-07-05).** The global injury setting defaults *off* for a
character with **no** injury-stream perk (preserving zero-new-draws replay parity with pre-character
saves). But the moment a character **takes any injury-stream perk** (`glass_cannon`, `injury_prone`,
`hot_head`, `hard_charger`, `ironman`, `iron_constitution`, `safe_hands`), the injury system is
**enabled for that character by construction** — folded from the `player.character` creation row, so it
is still fully deterministic and replayable. This closes a structural exploit found in the audit: with
injury globally off, a perk whose *drawback* lived entirely on the injury stream (glass_cannon,
injury_prone, hot_head) became a pure-upside refund (strictly dominant), and a perk whose *benefit* lived
there (ironman, iron_constitution, safe_hands) became a trap. Auto-enable guarantees the injury half is
always live for the characters that priced it. As belt-and-suspenders, every one of those perks was
**also** re-authored so its **non-injury, always-live effects alone** justify its cost (refund perks keep
a live crash/rep drawback; benefit perks keep a live longevity/blame benefit) — see the Balance audit.

### 6.3 New journal phases (additive; all derived except `player.character`)

`player.character` (creation, an INPUT — survives `WipeDerived`), `player.xp`, `player.level`,
`player.statSpend`, `player.respec`, `player.injury`, `player.formSwing`. Strings are save-format-stable.
The byte-identical replay CI test must cover a character career **and** its "no character / skip
everything" equivalent before this ships.

---

## 7. JSON schema — `data/rules/perks.json`

Top-level keys: `version`, `characterPoints`, `stats`, `levels`, `respec`, `streams`, `creation`,
`perks`. (`$comment` fields are allowed anywhere and ignored by the loader.) The one machine-load-bearing
addition over the prior draft is that **every perk carries a machine-readable `effects[]` array** so the
net is computable by the audit without parsing prose.

### 7.1 The perk object

```jsonc
{
  "id": "rain_man",                 // unique, snake_case, stable (save-format key)
  "name": "Rain Man",               // display
  "category": "weather",            // one of the 9 categories (§8)
  "cost": 1,                        // net CP; negative = drawback refunds points
  "description": "…era-flavored…",  // period-voice one/two sentences (UI + fiction)
  "stream": "none",                 // "none" | a registered stream name (injury/form-swing)
  "effects": [                      // BENEFIT and DRAWBACK both here, so net is arithmetic
    {
      "kind": "benefit",            // "benefit" | "drawback"
      "lever": "statDelta",         // the machine lever (enumerated below)
      "target": "wetSkill",         // lever-specific: rating field / scalar / weight / cause
      "magnitude": 0.30,            // signed
      "condition": "wetRound",      // optional: null | wetRound | dryRound | longRace | shortRace | tierLte2 | tierGte4 | eraTransition | driverErrorDnf | ageLtPeak | ageGtePeak
      "cpEquivalent": 0.6,          // what the audit charges this at (signed)
      "note": "…"                   // optional human note
    },
    { "kind":"drawback", "lever":"statDelta", "target":"raceSkill", "magnitude":-0.05,
      "condition":"dryRound", "cpEquivalent":-0.5 }
  ]
}
```

### 7.2 The lever vocabulary (every `lever` an effect may name)

Each lever names exactly one deterministic input or output-weight in the shipped sim. This closed set
is what the loader validates and the audit prices.

| `lever` | `target` semantics | Folds through |
|---|---|---|
| `statDelta` | a `PackDriverRatings` field (`raceSkill`, `qualifyingSkill`, `wetSkill`, `tyreManagement`, `aggression`, `defending`, `consistency`, `avoidanceOfMistakes`, `startReactions`, `stamina`, `fuelManagement`), or `chosenFlavor` (the player-picked rating for One-Trick Pony, bound at creation) | player seat ratings at grid resolve → `SeatStrengthModel`, pace anchor, written file |
| `carScalar` | `weight` \| `power` \| `drag` | player seat scalar at grid resolve → `SeatStrengthModel.CarRating` + the AMS2 file (the one lever that bites the real car) |
| `opiRetention` | — (raises retention both ways) \| `gainSide` (moves only the 0.2 gain half — used to pair a retention benefit with its symmetric gain-side drawback, e.g. Metronome / Consistency King / Streaky) | `OpiMath.Retention`/`Gain` (player-local) |
| `opiErrorBlame` | `scale` \| `floorBlend` | `OpiMath.EffectiveFinish` driver-error branch |
| `reputationGainRate` | `round` \| `season` \| `both` | `ReputationMath.RoundDelta`/`SeasonDelta` multiplier |
| `underdogMultiplier` | `lowTierBonus` \| `topTierMult` | `ReputationMath.UnderdogMultiplier` (player-local) |
| `marketability` | — | Marketability meta-stat (rep + salary lever) |
| `paceAnchorAlpha` | — | `PaceAnchorMath.Alpha` (player-local) |
| `agingCurve` | `peakShift` \| `riseMult` \| `declineAccelMult` | player-local `AgingCurve` override in `AgeOneSeason` |
| `offerWeight` | `experience` \| `salaryAsk` \| `ageRisk` \| `repFloorRelax` | `TeamArchetypeCatalog.OfferScore` inputs / rep-floor gate |
| `income` | `payBudgetBu` \| `salaryOfferMult` | seat-market `PayDriverWeight` input + `SalaryOffer` |
| `injuryHazard` | `durabilityDelta` \| `baseAdd` \| `perErrorAdd` | new opt-in injury roll (`stream: injury`) |
| `xpRate` | a cause (`all` \| `finishVsExpected` \| `win` \| `podium` \| `midfield` \| `dnfMechanical`) or `ageWindow` | `XpMath` multiplier (`ageLtPeak`/`ageGtePeak` conditions front/back-load) |
| `statPoints` | `perLevel` \| `softCap` \| `lockToOne` | `levels.levelGrants` (player-local) |

### 7.3 Envelope keys (unchanged in shape from the shipped draft, retuned)

- `characterPoints`: `{ creationBudget, minBudgetAfterSpend, maxRefundHeadroom }`.
- `stats`: `{ talentStats[], metaStats[] }` — each talent stat `{ id, mapsTo, writeBase, writeSpan,
  creationRange:[0.15,0.85] }`; each meta `{ id, range, default, modulates }`.
- `levels`: `{ xpCurve, xpSources:{perRound,perSeason}, levelGrants, softCapByEra }`.
- `respec`: perks lock at creation; stat points bankable until spent; a milestone respec token swaps
  one perk for one of **equal-or-lower cost** (no token-laundering into a stronger build).
- `streams`: the new registered stream names.
- `creation.archetypes[]`: `{ id, name, description, startStats{}, startMeta{}, perkIds[] }` — each a
  pre-spent, in-budget template.
- `perks[]`: the 42 objects.

### 7.4 Validation on load

Range-clamp all magnitudes to the per-lever caps (§4.1d); reject unknown `lever`/`condition`/`stream`
tokens; assert `Σ cpEquivalent ≈ cost ±0.5` per perk; assert every archetype's `perkIds` net-spend is
in `[0, budget+headroom]`; assert the xp curve is strictly increasing. Parses with a round-trip
(`CoreJson.Options`, camelCase) — same discipline as the aging-curve and archetype files.

---

## 8. The 42 perks — category spread (free-choice-viable)

Nine categories, ≥3 perks each, so no axis is a forced monoculture. Full data in `perks.json`. Every
perk carries a `cost`, a `description`, and an `effects[]` whose `cpEquivalent` values are audited to
sum within ±0.5 of `cost`.

| Category | Perks (id) | Count |
|---|---|---|
| **pace** | sunday_driver, qualifying_specialist, glass_cannon, featherweight_setup, engineers_favorite, test_driver, late_braker | 7 |
| **racecraft** | hard_charger, hothead, ice_in_the_veins, safe_hands, opportunist | 5 |
| **physical** | ironman, iron_constitution, injury_prone, hot_head, stamina_freak | 5 |
| **mental** | consistency_king, streaky, metronome, prodigy, student_of_the_craft | 5 |
| **business** | sponsor_magnet, cheap_contract, prima_donna, mercenary, company_man | 5 |
| **weather** | rain_man, sunshine_specialist, tyre_whisperer, fuel_saver | 4 |
| **team** | underdog_hero, journeyman, works_prodigy, team_player | 4 |
| **media** | media_darling, quiet_professional, superstition | 3 |
| **era** | late_bloomer, wonderkid, adaptable, one_trick | 4 |

Cost distribution (proves refunds fund ambition without a dominant tier), **post-audit**: `cost −2 ×3`,
`−1 ×6`, `0 ×12`, `+1 ×15`, `+2 ×4`, `+3 ×2` (the audit recosted `hard_charger` from +2 to +1 — its
aggression/defending benefits are rating-only and its wheel-to-wheel style needs real always-live crash
drawbacks, which nets to +1, not +2). The twelve `cost 0` perks are pure playstyle traits — the backbone
of free choice.

**No strictly-dominant perk** — every positive-cost perk pairs its benefit with a drawback on a
different axis, and the sim auto-taxes talent (raised `raceSkill` → higher `expectedFinish` → a
stiffer OPI/rep/XP bar + stiffer recommended slider). **No strictly-trap perk** — every negative-cost
perk refunds real CP for its drawback, and every `cost 0` perk is a symmetric playstyle trait (good
for one career shape, bad for another). The audit (§4.1) verifies mechanically: 42/42 perks pass
`|Σ cpEquivalent − cost| ≤ 0.5`; zero free-lunch perks; zero pure-trap perks; every perk has ≥1
benefit AND ≥1 drawback effect; all 7 archetype presets reference real perks with net spend in
`[0, budget+headroom]`.

### 8.1 The seven archetype presets (pre-spent point templates)

| Preset | Signature perks | Net CP | Fantasy |
|---|---|---|---|
| **The Prodigy** | works_prodigy, wonderkid, glass_cannon | 1 | Generational talent, fragile, fast-levelling; leftover CP funds the high stat spread. |
| **The Late Bloomer** | late_bloomer, iron_constitution, student_of_the_craft | 5 | Long ascending veteran on craft + durability. |
| **The All-Rounder** | consistency_king, adaptable, team_player | 3 | No weakness, no towering strength; era-proof. |
| **The Pay Driver** | sponsor_magnet, media_darling, underdog_hero | 3 | Buys the seat; fame + money open midfield doors. |
| **The Rain Master** | rain_man, one_trick, tyre_whisperer | 1 | Untouchable in the wet, ordinary in the dry. |
| **The Journeyman** | journeyman, cheap_contract, safe_hands | 1 | Cheap, durable, trusted survivor. |
| **The Hard Charger** | hard_charger, opportunist, injury_prone | 0 | Fearless, crash-prone, crowd favourite. (Net fell 1→0 after hard_charger was recosted +2→+1; still in budget, more CP for the high aggression stat spread.) |

---

## 9. Wiring into the existing wizard + career sim (additive checklist)

- **Wizard:** add one `WizardStep.Character` between `SeatPick` and `RulesPreset`; a `CharacterViewModel`
  over `perks.json`; the three-tier flow (§5); write the `player.character` journal row at Confirm.
- **State:** extend `PlayerCareerState` with `Stats`, `PerkIds`, `CpUnspent`, `Level`, `Xp` (all
  defaulted so pre-character saves fold unchanged); add `driver_state`-style persistence rows later.
- **Sim:** add `XpMath` (pure); add the optional identity-default `PlayerPerkModifiers` param to
  `OpiMath`/`ReputationMath`/`PaceAnchorMath`/`SeatStrengthModel`/`AgeOneSeason` and the `OfferScore`
  call site; emit `player.xp` in `RoundUpdate` and season-end step 2; compute
  `PerkResolver.Resolve(character)` once and thread it.
- **Streams:** add `Injury`, `FormSwing`, `CharacterGen` consts to `CareerStreams`; gate injury behind
  a setting **that auto-enables when the character carries any injury-stream perk** (§6.2 / §11.1).
- **Grid resolve:** patch the player seat's ratings + scalars from `PlayerPerkModifiers` in the
  existing merge chain (pack → track form → round overrides → **+ character deltas**), before
  normalization.
- **Data:** `perks.json` + `character-stats.json` (mapping) + the audit test in `Companion.Tests`.
- **CI:** extend the byte-identical replay test to cover a character career and its skip-everything
  equivalent; add the perk-balance audit.

The red line (from the hub design, kept): a **non-default `ICareerSession` seam addition** is out of
bounds. Everything above is either a new pure function, an identity-defaulting optional parameter, a new
additive journal phase, or a new registered stream — nothing perturbs existing careers or the oracle.

---

## 10. Open questions — RESOLVED 2026-07-05 (hub-design elicitation)

All taste/scope calls were decided with Mike in the 23-question hub-design elicitation. See
`docs/dev/career-hub-design.md` §1 and §8 for full context. In short:

- **Creation budget:** **10 CP** (the audited default; +6 refund headroom → net spend [0,16]). Not 12.
- **Injury system:** **opt-in** — global default OFF (zero new draws for default careers), **auto-
  enabled** for any character taking an injury perk (the audit depends on it being live for those 7
  perks), and **ON in Hardcore mode**.
- **Talent-stat honesty:** **pure-expectation by default** (the self-balancer) **+ a Hardcore
  honest-nudge** that additionally routes talent through a tiny bounded `carScalar` so it bites the
  real AMS2 car. The car-scalar mechanism is real, bounded (±0.015 / ±0.040 conditional) and
  reversible (timestamped backup, diff-aware staging, one-click restore).
- **CP-equivalent anchors:** keep the **authored defaults** (§4.1) — the CI self-consistency audit +
  the adversarial audit (§11) stand.
- **Archetype presets:** **expand to 13** (the audited 7 + **6 new** balance-audited, non-overpowered
  templates), all editable + data-extensible. The 6 new presets must pass the same CI audit before
  they ship.
- **Respec:** **mode-scaled with a real penalty** — Normal = milestone token + a **hefty CP penalty**,
  equal-or-lower-cost swaps only; **Hardcore = least forgiving** (perks locked; stat-only/extreme).

**New direction folded in from the same session** (see the hub-design spec): a **dynamic life-sim
layer** (morale/form/rivalries/life events on a seeded `event` stream, modulating INPUTs only, never a
finishing position); **seeded "D100 modified by your skills" outcomes** that stay byte-replayable;
**Normal vs Hardcore** as a first-class mode pillar; and the **Super Monaco GP ladder** framing
(rivalry/results-driven promotion up the tiers) as the career's organizing metaphor. These are design
directions for the character/life-sim milestone (build-plan Increment 4 ≈ v0.5.0), not changes to the
shipped-perk audit.

---

## 11. Balance audit (adversarial, 2026-07-05)

An adversarial min-max pass re-derived every perk's **true** net effect against the shipped sim
coefficients (`SeatStrengthModel`, `OpiMath`, `ReputationMath`, `PaceAnchorMath`, `AgingCurve`,
`TeamArchetypeCatalog`, `career-team-archetypes.json`, `career-aging-curves.json`) rather than trusting
the authored `cpEquivalent` values. The mechanical `|Σ cpEquivalent − cost| ≤ 0.5` test is a
*self-consistency* check only — the audit's job was to catch effects whose `cpEquivalent` was itself
**wrong** against reality, which is where dominant/trap perks hide.

### 11.1 The structural exploit (the big one) and its fix

**Root cause.** Per §6.2 the `injury` stream is *opt-in behind a setting* so a default career adds no new
draws. But seven perks priced injury effects into their cost, and several put their **entire** benefit or
drawback on that stream. With the injury setting **off** (the default), the pricing broke **in both
directions**:

| Perk | cost | Original defect with injury OFF | Verdict |
|---|---|---|---|
| `glass_cannon` | −2 | Both drawbacks on `injury` → +0.015 power (**Strength +0.09, real, every round**) for a **free 2-CP refund** | **Strictly dominant** |
| `injury_prone` | −2 | −0.30 durability drawback evaporates → raceSkill/qual buff + 2-CP refund minus only ageRisk | **Dominant** |
| `hot_head` | −2 | ~2.7 of 3.7 CP of drawback on `injury` → +0.012 power real pace + 2-CP refund | **Dominant** |
| `ironman` | +2 | +0.25 durability benefit (1.8 CP) evaporates → paid 2 CP for a net **pace loss** | **Trap** |
| `iron_constitution` | +3 | +0.30 durability benefit (1.8 CP) evaporates → paid 3 CP for ~1.2 CP of value | **Trap** |
| `safe_hands` | +1 | +0.10 durability (0.6 CP) evaporates → paid 1 CP for ~0.4 CP of value | Mild trap |

**Fix (two mechanisms, belt-and-suspenders):**
1. **Injury auto-enable** (§6.2): taking *any* injury-stream perk enables the injury system **for that
   character**, folded from the creation row (still deterministic/replayable). The injury half is never a
   disabled freebie or a disabled tax. Zero-new-draws replay parity is preserved for characters that took
   **no** injury perk.
2. **Every one of the seven perks re-authored so its non-injury, always-live effects alone justify its
   cost.** Refund perks (glass_cannon, injury_prone, hot_head) now carry a live crash-exposure /
   error-blame / rep drawback; benefit perks (ironman, iron_constitution, safe_hands) now carry live
   longevity (peakShift, declineAccel halving, ageRisk) benefits. Verified with an **injury-OFF
   robustness pass**: every refund perk delivers **≤ 0** live real value (the CP refund is the reward for
   accepting real risk — a genuine gamble, never free), and every positive perk delivers **≥ cost − 0.5**
   live value (never a trap):

| Perk | cost | injury-OFF live value | reads as |
|---|---|---|---|
| glass_cannon | −2 | −0.4 | gamble (real error tax for the refund) |
| injury_prone | −2 | −0.2 | gamble |
| hot_head | −2 | −0.8 | gamble |
| hard_charger | +1 | +1.0 | fair |
| safe_hands | +1 | +0.7 | fair |
| ironman | +2 | +1.6 | fair (mild longevity overpay) |
| iron_constitution | +3 | +2.5 | fair (mild longevity overpay) |

### 11.2 The lever-asymmetry finding (why the CP table needed a caveat)

`carScalar` moves `Strength` **4× harder per stated CP** than `raceSkill` (0.06 vs 0.015 for the
table's nominal deltas) **and** bites the real AMS2 car, where the player-seat `raceSkill` is inert.
Consequence: unconditional-`carScalar` benefits are the strongest thing a perk can hand out. The audit
(a) added the caveat to the CP-equivalent table in §4.1, (b) trimmed `glass_cannon`'s unconditional
power from `+0.015 → +0.012`, and (c) requires every `carScalar` benefit to pair with an **always-live**
(not disable-able) drawback. Conditional weather/distance `carScalars` (rain_man, tyre_whisperer,
stamina_freak, fuel_saver, sunshine_specialist) were checked by **calendar-weighted expectation** and are
fair (their averages land ~neutral-to-negative), with `sunshine_specialist`'s wet penalty deepened
`−0.030 → −0.040` so its dry-majority calendar expectation is no longer mildly positive on a cost-0 perk.

### 11.3 Other fixes

- **`hard_charger`**: top-level `stream` was `"none"` while an effect used `"injury"` — a **data-validity
  bug** (the loader registers the stream per perk). Corrected to `"injury"`. Its aggression/defending
  benefits are rating-only (inert on track, drive fiction/rep/beat-teammate), so with real always-live
  crash drawbacks its honest cost is **+1, not +2** — recosted.
- **Per-lever cap exceptions documented** (§4.1d): the shipped file already had `rain_man` wetSkill
  `+0.30`, `one_trick` chosenFlavor `+0.30`, and conditional `carScalars` beyond `±0.015` — all
  intentional. The cap rule now names the signature-specialism (`±0.30`) and weather-conditional
  (`±0.040`) exceptions so the validator accepts the intended perks instead of rejecting them.

### 11.4 Post-fix audit result (all mechanical checks pass)

- **42 perks**, ids unique, all `lever`/`condition`/`stream` tokens from the documented set, JSON parses.
- **42/42** satisfy `|Σ cpEquivalent − cost| ≤ 0.5` (worst residual 0.4).
- **Zero free-lunch** (no positive benefit-CP with zero drawback-CP) and **zero pure-trap** (no negative
  benefit with no upside); every perk has ≥1 benefit **and** ≥1 drawback effect.
- **No strictly-dominant and no strictly-trap perk under either injury setting** (§11.1 robustness table).
- **All 7 archetype presets** reference real perks with net spend in `[0, budget+headroom] = [0, 16]`:
  prodigy 1, late_bloomer 5, all_rounder 3, pay_driver 3, rain_master 1, journeyman 1, hard_charger 0.
- **≥3 perks per category** (pace 7, physical 5, racecraft 5, mental 5, business 5, weather 4, team 4,
  era 4, media 3).
- **Determinism**: every randomness-bearing effect names a registered stream (`injury` / `form-swing` /
  `character-gen`); no perk reads a wall clock, Guid, or unstable hash; every effect is a pure bounded
  modifier on a named sim input — no perk sets a finishing position.

### 11.5 Viable-build proof (free choice genuinely works)

Six structurally different characters, all in budget (10 CP + up to 6 refund headroom), leftover CP buy
`+0.05` stat steps:

| Build | Perks | Net CP | Leftover for stats |
|---|---|---|---|
| **Cost-0 trait purist** | sunday_driver, featherweight_setup, metronome, mercenary, fuel_saver, wonderkid | 0 | 10 |
| **Pay-driver empire** | sponsor_magnet, prima_donna, cheap_contract, media_darling | 0 | 10 |
| **Rain master** | rain_man, one_trick, tyre_whisperer | 1 | 9 |
| **Glass min-maxer** | glass_cannon, injury_prone, hot_head, works_prodigy, ironman, adaptable | 1 | 9 |
| **Ageless veteran** | iron_constitution, late_bloomer, adaptable | 6 | 4 |
| **Works front-runner** | works_prodigy, engineers_favorite, ice_in_the_veins | 7 | 3 |

The **Glass min-maxer** is the min-maxer's dream on paper (refund three −2 perks, buy three premiums) —
but post-fix it takes three injury perks, so the injury system is **on by construction**, and each fragile
perk delivers **negative live real value** (real error/pace/rep tax) for its refund. It is a genuine
high-variance gamble that "works out or not as the career unfolds" — exactly Mike's bar — not a strictly
superior build.
