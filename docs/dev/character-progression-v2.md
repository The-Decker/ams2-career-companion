# Character creation and progression v2

_First-wave design, 2026-07-12. This document is the durable contract for the level-500 character rework. It supersedes the level/economy/tree direction in `character-rpg-rework.md` for new careers only. Existing careers remain on their original progression version._

Companion files:

- `character-progression-v2-dna-catalog.md` — the 30 Racing DNA identities.
- `character-progression-v2-skill-catalog.md` — the 90-node, nine-family mastery catalog.
- `character-rpg-rework.md` — the shipped v1 implementation and bind-contract history.
- `character-system.md` — the underlying deterministic character/fold architecture.

## 1. Mike's locked vision

1. The maximum character level is **500**.
2. SMGP is a 17-season campaign. A great career can reach level 500 by the end of season 16, spend the final mastery points in that review, and drive season 17 with the complete build.
3. Historical progression scales to the number of playable calendar seasons from the chosen start through 2020. A full 1960–2020 career is 61 seasons; a 1967–2020 career is 54.
4. Level 500 provides enough lifetime Skill Points to own every v2 mastery node and max every authored attribute rail. Earlier levels must still force meaningful choices.
5. Racing DNA expands from 13 presets to **30 persistent identities**. DNA is chosen at creation, is never part of a respec, and remains distinct even when the skill tree is complete.
6. The mastery tree contains nine families: **Pace, Racecraft, Physical, Mental, Business, Weather, Team, Media, Era Flavor**.
7. Every family ships with at least **10 skills**. Wave 1 targets exactly 90 skills.
8. The Driver screen presents a real graphical tree: minimal racing telemetry/wiring language, visible prerequisite lines, small icon nodes, and a details popout.
9. A single click opens details. “Acquire” queues the node. A double-click may quick-queue it. Nothing is spent until the player confirms the whole plan. Reset clears the unconfirmed plan.
10. A committed skill-tree reset costs experience. It does not erase Racing DNA or creation identity.
11. Balance is versioned and audited. This is an alpha first wave followed by deliberate balance passes; no existing replay may change silently.

## 2. Verified AMS2 capability boundary

This distinction is load-bearing.

Reiza's official Custom AI guide states that personality values such as `race_skill`, `qualifying_skill`, `consistency`, `wet_skill`, `tyre_management`, and the other driver fields affect **AI drivers only**. They do not make the human driver faster, calmer, better in rain, or less prone to mistakes.

The only Custom AI values that also alter the human-driven livery are:

- `weight_scalar`
- `power_scalar`
- `drag_scalar`

All three accept **0.900–1.100**, with `1.000` neutral. Values outside that range are invalid and AMS2 falls back to the original value. Reiza also describes `setup_downforce` and `setup_downforce_randomness` as AI setup preferences; they are not a human-driver attribute channel.

Primary sources:

- [Reiza — Information for Customizing AI drivers in AMS2](https://forum.reizastudios.com/threads/information-for-customizing-ai-drivers-in-ams2.21758/)
- [Reiza — AMS2 June 2026 Development Update, part 2](https://forum.reizastudios.com/threads/automobilista-2-june-2026-development-update-pt-2.36408/)

Local verification on Mike's machine:

- Installed AMS2 executable version: **1.6.9.91**, newer than the 1.6.9.8 scalar release.
- The installed `UserData/CustomAIDrivers` library has 32 XML files containing the new player-applicable scalars.
- The Companion writer already emits the three scalar fields and keeps the player's livery entry in the staged XML.

Consequences for v2:

- Pace, through `raceSkill`, sets Companion's expected-finish bar and difficulty guidance, which then affects OPI/reputation/XP evaluation. Marketability and Durability feed their authored career systems. The other talent/personality fields currently provide identity, UI fiction, and staged AI-row data only until an explicit versioned Companion consumer is added.
- They must never be labelled as physical human-car buffs merely because they are written into the player livery's Custom AI row.
- Only skills explicitly labelled **CAR** may change the human vehicle, and only through weight, power, or drag.
- Player-car scalar composition must clamp the final staged value to 0.900–1.100. The current additive character path does not enforce this boundary and must be hardened before v2 CAR nodes ship.
- Each CAR node must say what physically changes. AI-only rating effects must be labelled **EXPECTATION** or **CAREER**, never hidden behind “faster car” copy.

## 3. Current implementation baseline

The shipped system is a useful foundation, not something to mutate in place.

| Area | Shipped state |
|---|---|
| Racing DNA | GUI label over 13 `creation.archetypes`; the selected archetype ID is not persisted as its own identity |
| Character stats | Five talent stats plus Marketability and Durability |
| Perks | 42 immutable v1 perk definitions across the nine families |
| Purchasable perks | 21 positive-cost perks; 21 zero/negative creation-only traits |
| Skill tree | 42 perk cards plus 15 one-shot stat nodes; flat WrapPanel cards, not a graph |
| Level curve | Geometric: 100 XP to L2, ×1.35, max level 30 |
| Skill Points | 3 per level, numerically the old CP pool |
| Respec | One-node refund using milestone tokens |
| Progression gates | profile version 0 = legacy; version 1 = current tree/era-cap rules |
| Persistence | `CharacterProfile` inside player-state JSON plus INPUT journal rows; no character columns/migration |
| Replay | v1 external rules are not pinned, so existing IDs/effects and v0/v1 formulas must remain immutable |

Changing only `maxLevel` from 30 to 500 is invalid. The 1.35 geometric step at level 500 is approximately 8×10^66 XP and overflows the current `double -> long`/cumulative-long implementation far before the cap. At 3 SP per level it would also grant 1,497 SP.

## 4. Versioned v2 domain model

New creations use `CharacterProfile.ProgressionVersion = 2`. Versions 0 and 1 keep exact current behavior.

Recommended additive fields, all default-omitted and included in structural equality/hash:

```text
CharacterProfile
  progressionVersion = 2
  racingDnaId
  racingDnaVersion
  creationBaseline
    stats, meta, traitIds, chosenFlavor
  acquiredSkillIds[]
  acquiredAttributeNodeIds[]
  skillPointsSpent
  xpSpentOnResets
  skillResetCount

PlayerCareerState
  xpScaleRemainder          mutable rational carry, starts at 0
  campaignProgressionPlan
    mode                    historical | smgp
    startYear
    endYear                 2020 for the initial historical campaign
    totalSeasons
    masterySeason           totalSeasons - 1
    plannedReferenceXp
    xpScaleNumerator
    xpScaleDenominator
    maxLevel                500
```

The exact snapshot may live in the creation journal payload and player-state JSON rather than a new SQLite table. No schema migration is required if the values remain additive JSON fields.

Racing DNA is no longer inferred from a selected card after creation. `racingDnaId` and its definition version are journaled as player input. A DNA's normalized mechanical passive must be immutable for that version or snapshotted into the creation payload.

The 42 existing perk definitions, 15 v1 stat-node IDs, progression formulas, and effects remain unchanged. Every v2 mastery node uses a new stable ID; a node may reference an immutable legacy effect set, but its mastery ID, price, gates, and ownership are separate. A v1 career must never see a v2 node become purchasable merely because a catalog was appended.

`creationBaseline` is the authoritative lossless reset target. Attribute acquisition projects current values forward from that snapshot; reset never tries to subtract 0.05 increments from a clamped 0.99 value.

## 5. Level 1–500 progression

### 5.1 Integer XP thresholds

V2 uses a deterministic integer curve, not an exponential floating-point curve:

```text
xpForLevel(n) = 17 + floor(27 * (n - 2) / 498), for n = 2..500
```

Properties:

- L2 costs 17 XP.
- L100 costs 22 XP.
- L250 costs 30 XP.
- L400 costs 38 XP.
- L500 costs 44 XP.
- Cumulative L500 threshold is **14,972 XP**.
- Every step is positive and non-decreasing; every cumulative threshold is strictly increasing.
- All arithmetic is checked integer arithmetic and remains far below `Int64.MaxValue`.

The existing v0/v1 geometric implementation stays intact behind version dispatch. `RoundUpdate`, `SeasonEndPipeline`, `CharacterDossier`, replay, and every progress-bar threshold must use the same version-selected curve service.

The shipped `softCapByEra` table is a v1 rule only. Dispatch it when `ProgressionVersion == 1`, not `>= 1`. V2 has a hard level cap of 500 in its pinned campaign plan and uses the campaign-phase SP gate below; an era can never silently cap a v2 driver at L26–L30.

### 5.2 Campaign-normalized XP

A 61-season historical career has far more races than 17-season SMGP. Raw XP cannot be compared without a pinned scale.

At creation, build a `CampaignProgressionPlan` from the pinned mode and calendar horizon. Define the high-performance reference for each non-final season as:

```text
referenceSeasonXp = 40 * championshipRoundCount + 340
plannedReferenceXp = sum(referenceSeasonXp through the mastery season, excluding only the final playable season)
xpScale = 15,680 / plannedReferenceXp
```

`15,680` is the 16-season SMGP champion reference (`16 × (16 × 40 + 340)`). SMGP therefore has scale 1. Historical careers receive a smaller scale when they have many more races/seasons and a larger scale when starting late. Store the rational numerator/denominator in the creation plan; never recompute it from a later mutable pack catalog.

V2 uses exact rational carry, not floating-point or per-award nearest rounding:

```text
eligibleRawXp = max(0, signedRawXp)
scaledNumerator = checked(eligibleRawXp * xpScaleNumerator + xpScaleRemainder)
appliedXp = scaledNumerator / xpScaleDenominator       // integer floor
nextRemainder = scaledNumerator % xpScaleDenominator
```

Reduce the pinned numerator/denominator by their GCD at creation. Initialize the remainder to zero, persist it in player state, and journal its before/after value with every XP row. This carry makes the aggregate award exact for a given raw-XP total without rounding drift. A bad round may award no experience but can never erase experience already learned or delevel the driver. Journal the signed raw award, eligible award, applied award, and carry for audit. V0/v1 retain their current aggregate-XP floor behavior. XP remains a pure function of journaled results plus the pinned plan; no new RNG stream exists.

This is a **can reach**, not an automatic-survival guarantee. A high-performing SMGP career reaches L500 during season 16 and can spend before season 17. A completion-only or steady top-ten run can finish below 500. A future accessibility option may add deterministic catch-up XP, but it is not the v2 default.

### 5.3 Skill Point schedule

V2 earns one lifetime SP per level after level 1, subject to a campaign-phase cap:

```text
levelPool(level) = clamp(level - 1, 0, 499)
seasonPool(completedSeasons) =
  floor(499 * clamp(completedSeasons, 0, masterySeason) / masterySeason)
earnedSp = min(levelPool, seasonPool)
availableSp = earnedSp - skillPointsSpent
```

V2 separates the character-creation budget from in-career Skill Points. DNA and creation-baseline customization spend a versioned **Creation Budget**; unused creation budget is forfeited and never carries into the 499-SP mastery pool. V0/v1 keep their current CP semantics unchanged.

For SMGP, `totalSeasons=17` and `masterySeason=16`:

| Review after season | Maximum earned SP |
|---:|---:|
| 1 | 31 |
| 5 | 155 |
| 10 | 311 |
| 15 | 467 |
| 16 | 499 |

This prevents a fast XP build from completing the tree in the opening seasons while guaranteeing that an L500 driver has the full 499-point pool before the final season.

For a 1960–2020 campaign, `masterySeason=60`, so the cap rises roughly 8–9 SP per completed season and reaches 499 after 2019. A 1967 start pins 53 mastery seasons and scales accordingly.

### 5.4 Content budget

Wave 1 authors 90 mastery skills. The current draft family catalogs total 30–33 SP per family and **280 SP** across all nine families.

Attribute rails retain 0.05 steps. Raising all seven attributes from the lowest allowed creation value (0.15) to the 0.99 cap requires at most 119 SP. The initial combined worst-case mastery cost is therefore **399 SP**, safely inside 499.

Balance target for later passes: **430–475 total SP** for a true all-node/all-attribute build, leaving 24–69 points for future nodes or DNA-specific variance. A load-time audit must reject a v2 catalog whose maximum legal mastery cost exceeds 499.

## 6. Racing DNA v2

Racing DNA is a permanent identity, not a respecable skill and not merely a UI preset.

Each of the 30 definitions contains:

```text
id, version, name, description
primaryFamily, secondaryFamily
startingStats, startingMeta, startingTraits
persistentPassive[]
tradeoff[]
optional authored choice (track family, rival, objective, nationality affinity, etc.)
```

Creation still snapshots all selected stats/traits. The DNA ID/version remains because its passive must stay visible and distinct after the tree is maxed. DNA consumes no Skill Points and cannot be reset.

The full catalog and max-tree identity hooks are in `character-progression-v2-dna-catalog.md`.

## 7. Mastery skill catalog

The v2 tree contains exactly 10 initial nodes in each family. Every skill has:

```text
id, introducedInProgressionVersion, family
tier, order, iconKey, cost, unlockLevel, exclusiveGroup?
requires[]
effectRef? or versioned effects[]
effect classification: EXPECTATION | CAREER | CAR
benefits[], drawbacks[], mechanical effects[]
```

Rules:

- IDs and effects are append-only once a released career can own them.
- Every `requires[]` entry is a stable mastery-node ID from the same catalog, never a display name.
- Prerequisites form a validated DAG across one unified node namespace.
- Cross-family prerequisites are forbidden in wave 1; this keeps each graph readable and avoids hidden ownership rules.
- Tier-5 endpoints are mutually exclusive during ordinary progression; the mastery override lifts exclusivity at L475 so “max everything” is literally possible at L500.
- AI personality/talent deltas are EXPECTATION effects for a human player. Only `raceSkill` currently feeds expected finish; the other Custom AI personality fields are staged fiction unless an explicit CAREER lever says otherwise.
- Only explicit CAR effects write player weight/power/drag.
- All CAR paths are enumerated by tests; combined scalars clamp to 0.900–1.100 and must stay within an authored advantage envelope.
- Every node has a priced benefit and a real guardrail/tradeoff. No pure traps and no free dominant path.
- Every XP-rate effect applies to per-round XP only in wave 1; season-award XP is unchanged.

The 90-node catalog is stored in `character-progression-v2-skill-catalog.md`.

Wave-1 level gates are explicit in every serialized node: tier 1 = L1, tier 2 = L50, tier 3 = L150, tier 4 = L275, and tier 5 = L400. A node must satisfy both its own gate and all prerequisites. The two tier-5 endpoints in each family share `<family>.capstone` as `exclusiveGroup`; before L475 only one may be owned, while L475+ enables the mastery override and permits the second. The override is a pure rule of persisted level/progression version, not a catalog mutation.

## 8. Graphical tree and transaction UX

### 8.1 Visual model

Render one family at a time as a minimal racing wiring diagram:

- node buttons arranged by tier/order through a custom WPF `Panel`;
- non-hit-testable vector connector paths behind the nodes;
- compact Segoe MDL2/icon-key glyphs in wave 1;
- owned, available, pending, locked, and mastery states;
- keyboard focus and automation names on every node;
- no nested ScrollViewer inside the Driver page.

Pure XAML cross-container lines and hard-coded Canvas coordinates are rejected because they fail under window resizing and 90–130% UI scale. The App-layer graph panel measures real node presenters, arranges them, then draws connectors from `requiresIds`.

### 8.2 Interaction state machine

```text
single click node
  -> select node
  -> open detail popout

Acquire in popout
  -> validate against projected pending plan
  -> queue node locally

double click available node
  -> quick-queue node
  -> keep confirmation bar visible

Reset
  -> clear the unconfirmed local plan
  -> no journal/database write

Confirm
  -> send one ordered plan to the session
  -> authoritative sequential validation
  -> one transaction / all-or-nothing journal append
  -> refresh tree
```

Existing `UnlockNodeCommand` remains for binding compatibility but the v2 GUI stops writing immediately. Additive VM surface:

```text
SelectedSkillNode
SkillNodeDetailOpen
PendingSkillNodes
PendingSkillPointCost
SkillPointsAfterPlan
SkillPlanDirty
SkillActionError
OpenSkillNodeCommand
CloseSkillNodeCommand
QueueSkillNodeCommand
RemovePendingSkillNodeCommand
ResetSkillPlanCommand
ConfirmSkillPlanCommand
```

The session needs an atomic `ApplySkillPlan` seam. Writing several existing `player.statSpend` rows one-by-one is not acceptable because a later validation failure could leave a partial build.

## 9. Experience-funded full reset

V2 replaces milestone respec tokens for v2 profiles. V0/v1 tokens remain unchanged.

Two XP values are displayed:

- **Lifetime XP** — monotonic; determines level and is never reduced.
- **Available reset XP** — `LifetimeXp - XpSpentOnResets`.

Recommended first-wave cost:

```text
baseResetCost = roundUpTo50(max(500, cumulativeXpThreshold(currentLevel) * 0.05))
resetCost = baseResetCost * (1 + skillResetCount)
```

The cost is balance data, not compiled. A full reset:

- keeps Racing DNA, name, age, and creation baseline;
- restores current stats/meta/traits exactly from the persisted `creationBaseline`, then reapplies immutable DNA passives;
- removes every v2 acquired mastery skill and attribute raise without reverse-subtracting clamped values;
- refunds their spent SP;
- increments `xpSpentOnResets` and `skillResetCount`;
- journals one `player.skillReset` INPUT with the authoritative cost and prior acquisition set;
- applies atomically and replays without RNG.

The UI must distinguish **Reset pending plan** (free, local) from **Reset committed tree** (XP cost, destructive confirmation).

## 10. Determinism and migration contract

1. Preserve progression versions 0 and 1 byte-for-byte. V2 is selected only by profile version 2.
2. Preserve all existing 42 perk IDs/effects and 15 stat-node IDs/semantics. Rules are not pinned today; changing them changes replay.
3. Add every new profile/plan field as default-omitted and include it in manual equality/hash.
4. Extend the `player.character` provenance payload to include the complete normalized v2 profile, DNA ID/version, and pinned campaign progression plan.
5. Keep creation, skill plan, and reset as INPUT journal phases. XP/level stays DERIVED.
6. Do not rename `player.character`, `player.statSpend`, or `player.respec`. Add `player.skillPlan` and `player.skillReset` for v2.
7. Live and replay select curve, SP economy, catalog membership, and reset rules from the persisted progression version/plan.
8. New random creation helpers use a local seeded RNG and journal the resulting complete profile. Replay never redraws it.
9. The f1db points oracle is never touched.
10. Add legacy-definition snapshot/hash tests so future balance edits cannot mutate the shipped 42 invisibly.

## 11. SMGP continuation prerequisite

The level-500 SMGP schedule assumes 17 SMGP seasons. The current bundled discovery path violates that assumption: after SMGP's authored 1990 season, generic `NextAfter` can discover `f1-1991` and offer a historical changeover.

Before v2 progression ships:

- SMGP seasons 1–16 must force same-pack SMGP continuation.
- SMGP season 17 must expose no season 18.
- Historical discovery must exclude `careerStyle: smgp` packs.
- Full bundled-root tests must prove SMGP S1→S2 remains `smgp-1`, historical 1989→1990 chooses `f1-1990`, and SMGP S17 terminates.

This is a correctness prerequisite, not a balance preference.

## 12. Balance and verification gates

Every wave runs the full solution and adds targeted audits:

- XP thresholds: positive, non-decreasing steps; strictly increasing cumulative values; L500 exactly 14,972 XP.
- Campaign scale: rational, pinned, deterministic, no divide-by-zero, no mutable-pack dependency.
- SP economy: never negative; never above 499; max legal catalog cost ≤499.
- Graph: unique stable IDs, valid icons/families, acyclic prerequisites, tier/order monotonicity.
- Effects: closed lever/target/condition vocabulary, identity defaults, explicit CAR classification.
- CAR path enumeration: final values within 0.900–1.100; aggregate advantage capped and calendar-weighted for conditional weather/long-race paths.
- DNA: exactly 30 unique IDs; stat/trait budgets valid; persistent passive has a priced drawback; any authored choice is journaled.
- Transactions: Cancel/Reset-pending writes nothing; Confirm is atomic; replay reconstructs identical acquisition order/state.
- Resets: cost validation server-side; creation/DNA immutable; XP never delevels; reset replay byte-identical.
- GUI: render graph at 90/100/110/125/130% scale, keyboard navigation, popout, pending/confirm/reset states, and empty/legacy fallbacks.
- Oracle: 77/77 unchanged.

## 13. Multi-part implementation order

### Wave 0 — correctness and immutable scaffolding

1. Fix style-scoped SMGP continuation and test against the full bundled pack root.
2. Add progression-version dispatch without changing v0/v1 output.
3. Add pinned `CampaignProgressionPlan` and complete creation provenance.
4. Add player-car scalar clamping/validation and distinguish EXPECTATION/CAREER/CAR in projections.

### Wave 1 — level 500 and SP economy

1. Implement the integer v2 curve and campaign XP normalization.
2. Implement one-SP-per-level plus campaign-phase cap.
3. Update fold, replay, dossier, review, and progress bar through one shared service.
4. Ship determinism fixtures for historical and 17-season SMGP careers.

### Wave 2 — 30 Racing DNA identities

1. Author and validate the DNA catalog.
2. Persist DNA ID/version and any authored secondary choice.
3. Rework creation UI around DNA selection while preserving advanced customization.
4. Add random-build journaling and 30-card render/accessibility coverage.

### Wave 3 — 90-skill data and Core graph

1. Author v2 catalog membership, icons, tier/order, prerequisites, costs, effects, and guards.
2. Add seven complete attribute rails.
3. Strengthen unified DAG and max-cost/path audits.
4. Preserve legacy 42 via immutable snapshot tests.

### Wave 4 — graphical tree and safe acquisition

1. Add graph projection fields and App-layer wiring panel.
2. Add single-click detail popout and double-click quick queue.
3. Add transient multi-node plan and atomic confirm.
4. Add free pending reset and error recovery.

### Wave 5 — XP-funded reset and balance pass 1

1. Implement full-tree reset input/fold/replay.
2. Replace v2 token UI with XP cost/available balance.
3. Run exhaustive effect-stack, CAR-scalar, progression, and catalog simulations.
4. Reprice/reorder nodes only before v2 careers are declared stable; after release, effect versions are append-only.

## 14. Decisions still open for Mike

These do not block Wave 0, but must be frozen before their owning wave:

1. Should a historical career that starts after 1960 still be able to reach L500 by 2020? **Recommendation: yes; pin and rescale to its actual remaining horizon.**
2. Should a merely surviving SMGP career be guaranteed L500, or only a great one? **Recommendation: great-career reachable, not automatic; retain performance meaning.**
3. Should v2 DNA passives remain mechanical after all skills are maxed? **Recommendation: yes; otherwise every L500 driver becomes identical.**
