# Level 1–300 progression system — implemented specification

_Authored 2026-07-16 against the shipped code on `hub/increment-4`. This documents what the build
actually does; every claim cites the implementing type or test. The durable design contract is
`docs/dev/character-progression-v2.md`; where the SMGP-300 mission spec asked for something the
repo deliberately does differently, see **Accepted deviations** at the end._

Progression v2 ("the Level-300 system") is selected per character by
`CharacterProfile.ProgressionVersion == 2`. Versions 0 and 1 keep their shipped geometric curve and
Character-Point economy byte-for-byte behind the same dispatcher
(`src/Companion.Core/Character/CharacterLevelProgression.cs`). All three Alpha 1.0 modes create v2
profiles.

---

## 1. What a level means

A character level is **not a speed number**. AMS2's Custom AI personality fields (`race_skill`,
`qualifying_skill`, `wet_skill`, …) affect AI drivers only — they never make the human-driven car
faster. The only Custom AI values that alter the human-driven livery are `weight_scalar`,
`power_scalar`, and `drag_scalar`, each valid in 0.900–1.100. This verified capability boundary is
§2 of `docs/dev/character-progression-v2.md` (primary sources: Reiza's Custom AI guide and the June
2026 dev update) and it is load-bearing for every effect classification below.

What a level actually buys:

- **Skill Points** for the 90-node mastery tree and the seven attribute rails (§5).
- **A higher expectation bar.** Talent stats write the player seat's `PackDriverRatings`; `pace`
  (→ `raceSkill`) feeds `SeatStrengthModel.ExpectedFinish`, which sets the OPI/reputation/XP bar —
  a better driver is *expected* to finish better, so growth self-balances rather than granting a
  hidden buff (`data/rules/perks.json` `stats.$comment`).
- **Career levers**: marketability/durability feed offers, salary, injury hazard, and aging.
- **CAR effects only through explicit CAR-classified mastery nodes**, clamped to the 0.900–1.100
  scalar envelope (`MasterySkillCatalog.CarScalarBounds`, `MasterySkillCatalog.cs:31`).

Display never lies about this: the dossier's modifier lines label rating deltas with friendly
expectation language ("race pace", "composure") and car scalars as explicit "car weight/power/drag"
lines (`CharacterDossier.ProjectModifierLines`, `CharacterLabels.Rating`).

## 2. The locked integer curve

`CharacterLevelProgression.Level300XpForLevel` (`CharacterLevelProgression.cs:87`):

```text
xpForLevel(n) = 40 + floor(21 * (n - 2) / 298),  n = 2..300
```

Checked 64-bit integer arithmetic, no floating point, no RNG. Levels above 300 throw
(`CharacterLevelProgression.cs:84`); `LevelForTotalXp` walks the cumulative thresholds and can
never return more than 300.

| Level | Step cost | Cumulative XP | Notes |
|---:|---:|---:|---|
| 2 | 40 | 40 | curve floor |
| 30 | 41 | 1,174 | tier-2 gate |
| 90 | 46 | 3,793 | tier-3 gate |
| 100 | 46 | — | design-doc checkpoint |
| 150 | 50 | — | |
| 165 | 51 | 7,422 | tier-4 gate |
| 200 | 53 | — | |
| 240 | 56 | 11,446 | tier-5 gate |
| 250 | 57 | — | |
| 285 | 59 | 14,050 | mastery-override level gate |
| 300 | 61 | **14,951** | hard cap |

Every step is positive and non-decreasing; every cumulative threshold is strictly increasing; the
L300 total is pinned at exactly 14,951 by `CharacterLevelProgressionTests` (see
`CharacterLevelProgressionTests.cs:37`) with boundary coverage in
`CharacterLevelProgressionMidBoundaryTests`. Legacy v0/v1 stay on the geometric
`100 × 1.35^(n−2)` curve to max level 30 (`data/rules/perks.json` `levels.xpCurve`), with the v1
`softCapByEra` table dispatched **only** for `ProgressionVersion == 1`
(`CharacterLevelProgression.MaxLevel`, `CharacterLevelProgression.cs:19`).

## 3. XP sources and eligibility

XP is a pure function of the results the player enters — no dice, no PCG stream — so replay
reproduces every level byte-for-byte (`XpMath` header comment; `docs/dev/character-system.md`
§3.1). Raw award numbers are author-editable data in `data/rules/perks.json`
(`levels.xpSources`), consumed by `XpMath`:

**Per round** (`XpMath.PerRound`, fold consumer `RoundUpdate.Apply`,
`src/Companion.Core/Career/RoundUpdate.cs:176`):

```text
xpRound = clamp((expectedFinish − effectiveFinish) × 6, −30, +60) + resultBonus
```

| Source | Raw value | Notes |
|---|---:|---|
| Finish vs expected | ±6/place, clamp [−30, +60] | relative performance — overperforming a weak car is the richest XP, so a backmarker career levels |
| Win | +40 | result bonuses are mutually exclusive by outcome |
| Podium | +20 | |
| Points finish | +10 | uses the round's RESOLVED points positions, never a hard-coded top-6 |
| Beat teammate | +8 | stacks only on a genuine classified finish |
| DNF (driver error) | −15 | |
| DNF (mechanical) | 0 | not the driver's fault |
| Setup Gamble hit | `CalledShotMath.XpBonus` | added to the same round award (`RoundUpdate.cs:187`) |

`effectiveFinish` is the OPI effective finish (expected finish for a mechanical DNF, grid size for
a driver-error DNF), so XP and OPI agree on "how the round went" (`XpMath.RoundInputs`).

**Per season** (`XpMath.PerSeason`, consumer `SeasonEndPipeline`,
`src/Companion.Core/Career/SeasonEndPipeline.cs:241`): champion +300 / top-3 +150 / top-10 +60
(mutually exclusive, best applicable) plus a flat +40 season-completed grant.

**Championship-round eligibility (v2 only).** A v2 round award is eligible only when
`IsChampionshipRound && IsPrimaryRace` (`RoundUpdate.cs:191`); the v2 season-scale denominator is
authored per championship round, so non-championship events and secondary weekend races stay
fold-visible but journal `eligibleRawXp = 0` and contribute no XP — the award population and the
denominator are the same population. Season-end XP is always eligible
(`SeasonEndPipeline.cs:248`). v0/v1 ignore this gate.

**Perk/mastery XP-rate effects** scale per-cause gains through `PlayerPerkModifiers.XpMult`. The
legacy path preserves the shipped operation order exactly; the v2 mastery path clamps every
composed rate into 0.00–1.40 (`XpMath.cs:84`) so stacked mastery nodes have a hard aggregate
ceiling.

### Campaign-normalized XP (v2)

A 61-season Dynasty career has vastly more races than 17-season SMGP, so raw XP is scaled through
the career's pinned `CampaignProgressionPlan`
(`src/Companion.Core/Career/CampaignProgressionPlan.cs`):

```text
referenceSeasonXp   = 40 × championshipRoundCount + 340        (CampaignProgressionPlan.cs:226)
plannedReferenceXp  = Σ referenceSeasonXp over every season before the final one
                      (the sole season for a one-season plan)   (CampaignProgressionPlan.cs:223)
xpScale             = 15,680 / plannedReferenceXp, stored as a GCD-reduced rational
```

15,680 is the 16-season SMGP champion reference (`16 × (16 × 40 + 340)`,
`CampaignProgressionPlan.MasteryReferenceXp`, line 36) — SMGP therefore runs at scale 1/1. Each
award flows through exact rational carry (`CharacterProgressionV2Math.NormalizeXpAward`):

```text
eligibleRawXp   = isEligible ? max(0, signedRawXp) : 0
scaledNumerator = eligibleRawXp × xpScaleNumerator + xpScaleRemainder     (checked)
appliedXp       = scaledNumerator / xpScaleDenominator                    (integer floor)
nextRemainder   = scaledNumerator % xpScaleDenominator
```

The remainder persists in `PlayerCareerState.XpScaleRemainder` and is journaled before/after with
every award, so the aggregate is exact — no rounding drift, and an ineligible round passes the
carry through untouched. Because `eligibleRawXp` floors at zero, **a bad v2 round can award
nothing but can never erase experience or delevel the driver** (`CharacterProgressionV2MathTests`).

Reaching L300 is a *can-reach*, not a survival guarantee: a great SMGP career reaches it during
season 16; a completion-only run finishes below it (design §5.2; distributions in
`docs/LEVEL_300_BALANCE_REPORT.md` from `BalanceSimulationHarness`).

## 4. Anti-exploit architecture

The whole system rides the determinism spine (`docs/PROJECT.md` §3: `state = fold(journal)`).

1. **`player.xp` is a DERIVED row, never an INPUT.** The fold emits it
   (`JournalPhases.PlayerXp`, `RoundUpdate.cs:201`, `SeasonEndPipeline.cs:258`); replay wipes and
   rebuilds every derived row from the journaled inputs through the same pure functions
   (`ReplayService` in `Companion.Data`). Editing your XP in the save is not a cheat, it is a
   replay divergence.
2. **Per-award audit fields.** Every v2 XP row journals `signedRawXp`, `eligibleRawXp`,
   `appliedXp`, `remainderBefore`, `remainderAfter` (`RoundUpdate.cs:211`), so any award can be
   re-derived and checked by hand.
3. **Fail-closed pinned plan.** A v2 round without a valid plan throws:
   `RoundUpdate.RequireVersionTwoPlan` (`RoundUpdate.cs:412`) demands a present plan, runs
   `CampaignProgressionPlan.Validate()` (which rejects wrong version, wrong mode, unordered or
   out-of-range seasons, a mismatched reference sum, or an unreduced scale —
   `CampaignProgressionPlan.cs:99`), and requires the player's `ExperienceMode` to match the plan.
   The plan is built once at creation and never rediscovered from the mutable pack catalog; later
   pack installs affect new saves only (`CampaignCreationPlanner.ResolveBoundedPlan`,
   `CampaignProgressionPlanTests`).
4. **SP cannot be manufactured.** `CharacterProgressionV2Math.SkillPoints` throws on a negative
   spend; skill plans are validated sequentially and applied all-or-nothing
   (`CharacterSkillPlan`, `SkillPlanSessionTests`); a committed reset revalidates the persisted
   level against the level derived from lifetime XP and rejects any mismatch
   (`CharacterSkillReset.cs:145`), rejects ownership that doesn't sum to `SkillPointsSpent`
   (`CharacterSkillReset.cs:177`), and rejects spend above the campaign-earned pool
   (`CharacterSkillReset.cs:201`).
5. **No new RNG.** XP, levels, and SP are pure functions of journaled results plus the pinned
   plan. Fold determinism over the real SMGP pack is proven end-to-end by the
   `BalanceSimulationHarness` byte-identical `Resimulate` evidence run.

## 5. The 499-SP dual-gated economy

`CharacterProgressionV2Math` (`CharacterProgressionV2Math.cs:28`,
`LifetimeSkillPoints = 499`) maps 299 level-ups proportionally onto 499 lifetime Skill Points,
gated twice:

```text
levelPool(level)            = floor(499 × clamp(level − 1, 0, 299) / 299)
seasonPool(completedSeasons)= floor(499 × clamp(completedSeasons, 0, masterySeason) / masterySeason)
earnedSp  = min(levelPool, seasonPool)
availableSp = max(0, earnedSp − skillPointsSpent)
```

`masterySeason = max(1, totalSeasons − 1)` (`CampaignProgressionPlan.cs:74`) — 16 for SMGP, 60 for
a complete 1960–2020 Dynasty. The season gate stops a hot start from finishing the tree early
while guaranteeing an L300 driver holds all 499 points before the final season.

| Level | Level pool | | SMGP seasons done | Season pool |
|---:|---:|---|---:|---:|
| 30 | 48 | | 1 | 31 |
| 90 | 148 | | 5 | 155 |
| 165 | 273 | | 10 | 311 |
| 240 | 398 | | 15 | 467 |
| 285 | 473 | | 16 | 499 |
| 300 | 499 | | | |

The dossier's `CpUnspent` binds this through the version dispatcher
`CharacterProgress.AvailableSkillPoints` (`CharacterSpend.cs:69`) — legacy careers keep the exact
Character-Point calculation, v2 projects the dual gate. Creation spends a separate versioned
creation budget; nothing unused carries into the 499 pool (design §5.3; enforced for v2 profiles
by `CharacterSkillReset.ValidateContext`, which rejects any legacy CP ownership on a v2 profile,
`CharacterSkillReset.cs:165`).

**Where the points go** (`MasterySkillCatalog`, data in `data/rules/mastery-skills-v2.json`):

- **90 mastery skills** across nine families, worst-case cost 280 SP
  (`MasterySkillCatalog.DraftSkillCost`, `MasterySkillCatalog.cs:18`).
- **Seven attribute rails**, 0.05 steps to the 0.99 cap, worst-case 119 SP
  (`DraftAttributeCost`, line 19) — 399 total, safely inside 499; the loader rejects any catalog
  whose maximum legal cost exceeds 499 (`MasterySkillCatalog.cs:179`).
- **Tier gates**: tier 1 = L1, 2 = L30, 3 = L90, 4 = L165, 5 = L240
  (`RequiredTierUnlockLevels`, `MasterySkillCatalog.cs:25`; the serialized catalog must match
  exactly or fail to load).
- **Capstone exclusivity**: the two tier-5 endpoints in each family share
  `<family>.capstone` as `exclusiveGroup`. The second is purchasable only when the driver is at
  least the **mastery override level 285** (validated to be exactly 285,
  `MasterySkillCatalog.cs:181`) **and** the mode's mastery checkpoint is complete —
  `completedSeasons >= masterySeason` for bounded campaigns (`MasterySkillGraph.cs:95`,
  `CharacterSkillReset.cs:195`; tests in `MasterySkillGraphTests`). The override is a pure rule of
  persisted state, never a catalog mutation.

### The XP-funded full reset

`CharacterSkillReset` (`src/Companion.Core/Character/CharacterSkillReset.cs`) replaces v0/v1
respec tokens for v2 profiles. Two XP figures exist: **Lifetime XP** (monotonic, determines level,
never reduced) and **Available reset XP** = `LifetimeXp − XpSpentOnResets`
(`CharacterDossier.cs:162`). The cost comes from the versioned, journaled
`skillResetPolicy` in `mastery-skills-v2.json` (min 500 XP, 1/20 of the cumulative
level threshold, rounded **up** to 50, then ×(1 + resetCount) — `CharacterSkillReset.ResetCost`,
`CharacterSkillReset.cs:355`; ≈750 XP base at L300, doubling each repeat). A committed reset:

- restores stats/meta/traits exactly from the persisted immutable `CreationBaseline` (never
  reverse-subtracting clamped values), keeps Racing DNA/name/identity, clears all v2 acquisitions,
  refunds their SP, charges `XpSpentOnResets`, increments `SkillResetCount`
  (`CharacterSkillReset.Project`, line 424);
- journals one `player.skillReset` INPUT carrying the authoritative cost, policy version, and the
  canonical prior-ownership snapshot, which `Apply` revalidates field-for-field on replay
  (`CharacterSkillReset.ValidateInput`);
- never moves level or lifetime XP. Coverage: `CharacterSkillResetTests`,
  `SkillResetSessionTests`.

## 6. The three Alpha 1.0 modes

Stable ids in `CareerExperienceModes` (`CampaignProgressionPlan.cs:9`) — strings, not an enum, so
an unknown save value is rejected rather than coerced. The L300 curve and the 499 level pool are
identical in all three; only the second pacing gate differs (contract:
`docs/dev/career-modes-alpha1.md`).

| Mode | Plan | Second gate |
|---|---|---|
| `grandPrixDynasty` | Bounded pin of every faithful playable pack from the chosen start through **2020** (enforced, `CampaignProgressionPlan.cs:135`), discovered once at creation (`CampaignCreationPlanner.DynastySequence`) | seasonPool over `masterySeason = totalSeasons − 1` |
| `smgp` | Exactly **17** ordinal seasons (`SmgpRules.CampaignSeasons`, `SmgpRules.cs:63`) over one pinned 16-round replica pack — same pack id/version/hash for all 17, consecutive years (`CampaignProgressionPlan.CreateSmgp` + validation, lines 137–162) | seasonPool over masterySeason 16 |
| `racingPassport` | Portfolio plan (a 15,680-reference credited-experience ledger, design §5.4) | **fail-closed at creation**: `CampaignCreationPlanner.Prepare` throws `"Racing Passport requires its portfolio activity ledger and cannot be created as a single career file yet."` (`CampaignCreationPlanner.cs:90`) |

A new v2 character **requires** an explicit mode (`CampaignCreationPlanner.cs:82`), and an
explicit mode requires a v2 character (line 93) — the legacy implicit path stays legacy. Creation
coverage: `CampaignProgressionCreationTests`, `CareerModeMenuTests`.

## 7. Balance constants registry

Every deliberately code-pinned constant, and why it is compiled rather than authored data. The
rule of thumb: **numbers that define the meaning of persisted state are code** (a data edit must
never silently re-derive a different level or SP balance for an existing save); **numbers that are
tuning** live in validated data files.

| Constant | Value | Location | Why code-pinned |
|---|---|---|---|
| Level cap | 300 | `CharacterLevelProgression.cs:11` (`Level300Max`) | version identity; echoed into every plan and re-validated (`CampaignProgressionPlan.cs:168`) |
| Curve formula | `40 + ⌊21(n−2)/298⌋` | `CharacterLevelProgression.cs:87` | replay contract — the thresholds ARE the save's meaning; pinned by `CharacterLevelProgressionTests` |
| Lifetime SP | 499 | `CharacterProgressionV2Math.cs:28`; catalog must declare the same or fail load (`MasterySkillCatalog.cs:17,179`) | economy identity; deliberately independent of the level cap |
| Level-pool mapping | `⌊499(level−1)/299⌋` | `CharacterProgressionV2Math.cs:70` | derives banked SP from folded state on every read |
| Season-pool mapping | `⌊499·s/masterySeason⌋` | `CharacterProgressionV2Math.cs:81` | same |
| Mastery reference XP | 15,680 | `CampaignProgressionPlan.cs:36` | the cross-mode normalization anchor (16-season SMGP champion) |
| Reference season XP | `40·rounds + 340` | `CampaignProgressionPlan.cs:226` | ties the denominator to the same authored award scale as the numerator population |
| `masterySeason` | `max(1, totalSeasons−1)` | `CampaignProgressionPlan.cs:74` (re-validated :166) | plan-shape invariant, not tuning |
| Dynasty horizon end | 2020 | `CampaignProgressionPlan.cs:135` | locked product decision (PROJECT.md §1) |
| SMGP campaign length | 17 seasons | `SmgpRules.cs:63` | locked SMGP design; plan validation depends on it |
| SMGP rounds/season | 16 | `CampaignProgressionPlan.cs:149` | the replica pack's shape; makes 15,680 exact |
| Tier unlock levels | 1/30/90/165/240 | `MasterySkillCatalog.cs:25`; serialized catalog must match (`:185`) | gate identity — a data edit would re-gate owned trees |
| Mastery override level | 285 | validated `MasterySkillCatalog.cs:181` (stored in `mastery-skills-v2.json:5`) | declared in data for visibility but code-clamped to exactly 285 |
| CAR scalar envelope | 0.900–1.100 | `MasterySkillCatalog.cs:31` | external AMS2 boundary — values outside are invalid in-game (design §2) |
| Mastery XP-rate clamp | 0.00–1.40 | `XpMath.cs:84` | catalog-wide aggregate ceiling on stacked rate effects |
| v2 round eligibility | championship ∧ primary race | `RoundUpdate.cs:191` | keeps award population == plan denominator |

Data-driven (validated on load, versioned where journaled):

| Constant | Location | Why data |
|---|---|---|
| Per-round/per-season XP awards | `data/rules/perks.json` `levels.xpSources` | tuning numbers; but note they are **not hash-pinned per career** — editing them changes replay for existing careers, a documented v0/v1-era limitation (design §10.2) |
| Legacy geometric curve (100, ×1.35, max 30) | `perks.json` `levels.xpCurve` | v0/v1 only; immutable by convention for the same reason |
| `softCapByEra` (26–30) | `perks.json` `levels.softCapByEra` | v1 only; never dispatched for v2 (`CharacterLevelProgression.cs:19`) |
| Skill reset policy (min 500, 1/20, round-to-50, +100 %/repeat) | `mastery-skills-v2.json` `skillResetPolicy` | tuning — but its `version` is journaled with every reset input and revalidated on replay (`CharacterSkillReset.cs:397`), so a policy edit cannot rewrite a past reset |
| Node costs, prerequisites, effects, rails | `mastery-skills-v2.json` | append-only once a released career can own them; max-legal-cost ≤ 499 audited on load |

## 8. Stat-system responsibility mapping

The mission spec speaks a per-discipline stat vocabulary. The repo deliberately concentrates that
vocabulary into **seven persisted stats** (`perks.json` `stats`, labels in
`CharacterLabels.Stat`) which write eleven `PackDriverRatings` fields on the player seat, plus
mastery/perk effects that can target any rating directly. Every effect carries a classification
(design §2): **EXPECTATION** (writes the player's AI row + raises the expected-finish bar — AI-only
in AMS2, never a human-car buff), **CAR** (weight/power/drag scalar, the only real physical
channel), **CAREER** (offers, salary, reputation, injury, aging).

| Mission vocabulary | Repo stat | Ratings written | Effect class |
|---|---|---|---|
| Pace / race pace | `pace` | `raceSkill` | EXPECTATION — the only rating that currently feeds `expectedFinish` (design §2 consequences) |
| Qualifying | `oneLap` | `qualifyingSkill` | EXPECTATION (grid-pace identity; splits from pace via perks) |
| Racecraft / overtaking / defending | `racecraft` | `aggression`, `defending` | EXPECTATION (fiction + headline causes) |
| Consistency / composure / feedback | `craft` | `consistency`, `avoidanceOfMistakes` | EXPECTATION (fewer driver-error DNFs in fiction; pairs with OPI blame-softening perks) |
| Wet weather / tire management | `adaptability` | `wetSkill`, `tyreManagement` | EXPECTATION, round-conditional (wet/long rounds) |
| Starts | — (no dedicated stat) | `startReactions` | reachable only as a perk/mastery `statDelta` target; labeled "starts" (`CharacterLabels.cs:389`) |
| Fitness / stamina | `durability` (meta) | `stamina` via effects | CAREER — injury hazard + aging shift (`ageShift = round(6·(durability−0.5))`), never a pace lever |
| Media / sponsors | `marketability` (meta) | — | CAREER — reputation multiplier + salary/offer scalar |
| Fuel management | — | `fuelManagement` via effects | EXPECTATION fiction |

Two honesty rules from the capability boundary: EXPECTATION deltas must never be presented as
"faster car" copy, and only explicit CAR nodes may touch weight/power/drag, clamped and
path-enumerated by tests (design §7 rules; `CharacterModifierResolverTests`).

## 9. Permanent vs temporary condition

**Permanent (persisted on the profile, replayed exactly):** the seven stats, creation traits,
Racing DNA identity (immutable, never respecs), acquired mastery/attribute nodes, lifetime XP and
level.

**Temporary (folded career state, moves every round):**

- **Form = OPI**, the on-performance index: `OpiMath.Update` folds each round's
  expected-vs-effective delta into `PlayerCareerState.Opi`, which feeds the next round's expected
  finish — a slump lowers the bar, a streak raises it. This is the system's rolling
  "condition/confidence" carrier.
- **Reputation** (`ReputationMath`) — the market's view, moved by results and Setup Gambles.
- **Pace/qualifying anchors** (`PaceAnchorMath`) — difficulty calibration, classified finishes only.
- **Season injury load + availability** (`AccidentModel`/`InjuryModel`; Fit / Injured /
  Season over / Deceased in `CharacterDossier.Availability`).
- **Aging** — the durability-shifted aging curve is the long-run counterweight leveling spends
  against (`perks.json` `levels.$comment`).

There is deliberately **no separate fatigue or confidence meter** — see Accepted deviations.

## 10. Level-cap behavior

There is no level 301 in any form (`CharacterLevelProgression.cs:84` throws above 300). At the
cap:

- The dossier reads the MAX state: `LevelCap = 300`, `IsAtLevelCap = true`
  (`CharacterDossier.cs:45,49`), `XpForNextLevel = 0`, `LevelProgress = 1.0`, and `XpIntoLevel`
  reads 0 rather than a runaway counter (`CharacterDossier.cs:165`).
- **Lifetime XP keeps accruing** past 14,951 and keeps funding the one remaining XP sink: committed
  tree resets via `AvailableResetXp` (`CharacterDossier.cs:162`; asserted past-cap in
  `CampaignProgressionCreationTests` and `CharacterDossierCapAndModifiersTests`).
- Reaching 300 emits a `Level300Reached` newsroom/history event alongside the running
  `LevelMilestone` events (`src/Companion.Core/Newsroom/NewsEvent.cs:95`,
  `ProgressionNewsEventsTests`); each applied round surfaces its XP/level/SP delta through
  `RoundProgressionSummary`, projected from the journaled `player.xp` row, never recomputed
  (`ICareerSession.cs:693`).

## Documented limitation — legacy v0/v1 delevel

The v0/v1 fold path applies XP as `newXp = Math.Max(0, player.Xp + xpRound)`
(`RoundUpdate.cs:224`; same shape at season end, `SeasonEndPipeline.cs:280`): the *total* floors
at zero, but a negative round (driver-error DNF, heavy underperformance) subtracts from it, so a
legacy driver near a threshold **can lose a level**. This is preserved deliberately — v0/v1
formulas are an unpinned replay contract and changing them would silently change existing careers
(design §3/§10). Progression v2 removes the behavior at the source: `eligibleRawXp = max(0,
signedRawXp)` means lifetime XP is monotonic and a v2 driver never delevels.

## Accepted deviations

Positions the repo deliberately takes versus the SMGP-300 mission spec, decided and recorded in
`docs/dev/smgp-alpha-finish-status.md` ("Accepted deviations") — do not re-litigate without Mike:

1. **Per-discipline stat sheet → seven stats + rating effects.** The mission's vocabulary
   (qualifying, wet, starts, tire management, fitness, feedback, composure, …) maps onto the seven
   persisted stats and the eleven rating fields per §8, honestly classified
   EXPECTATION/CAR/CAREER because AMS2 physically ignores AI skill fields for the human seat
   (design §2). A larger stat sheet would be fiction pretending to be mechanics.
2. **Fatigue/confidence are not separate systems.** Form (the OPI adjustment), durability, and
   the aging curves carry those responsibilities (§9). Adding meters with no deterministic
   consumer would violate the fold contract for existing careers.
3. **Level ≠ driving speed.** The mission's "driver gets faster" framing is implemented as a
   rising expectation bar plus explicit CAR nodes inside AMS2's real 0.900–1.100 scalar window —
   the only faster-car channel that actually exists (design §2).
4. **The 499-SP budget is independent of the 300-level cap** (not 3 SP × level or similar):
   Mike's locked vision #4 — L300 funds a complete build while earlier levels force choices; the
   proportional mapping plus the season/portfolio gate paces it (design §1, §5.3).
5. **Legacy careers keep delevel-on-negative-XP** (limitation above) — replay preservation beats
   retroactive consistency; all three alpha modes create v2 profiles which floor at zero.
6. **Racing Passport is fail-closed at creation** until its cross-thread credited-experience
   ledger ships (`CampaignCreationPlanner.cs:90`; `docs/dev/career-modes-alpha1.md`). Shipping a
   single-file approximation would create saves the real ledger could not honor.
7. **A merely-surviving career is not guaranteed L300.** The curve is a *can-reach* for a great
   career (design §5.2 and open-decision #2's adopted recommendation); deterministic catch-up XP
   remains a possible future accessibility option, not the default.
