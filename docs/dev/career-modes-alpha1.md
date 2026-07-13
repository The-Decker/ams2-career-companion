# Alpha 1.0 career modes

_Product contract, 2026-07-12. Mike's three-mode direction supersedes the earlier two-mode menu and the statement that historical tycoon play is necessarily post-alpha. Display names remain tunable; stable IDs and save boundaries do not._

## 1. The three modes

| Stable ID | Working display name | Player promise | Career shape |
|---|---|---|---|
| `grandPrixDynasty` | **Grand Prix Dynasty** | Be the driver-owner across the historical World Championship era. | One chronological historical timeline, product horizon 1960–2020. |
| `smgp` | **Super Monaco GP** | Play the authored SEGA-inspired rival/seat-swap career. | One separate 17-season SMGP campaign. |
| `racingPassport` | **Racing Passport** | One driver, any era, multiple live careers. | One persistent character plus independently saved historical or SMGP career threads. |

The display names are working names. Alternatives can be tested without changing serialized IDs:

- `grandPrixDynasty`: World Championship, Grand Prix Legacy, World Championship Legacy.
- `racingPassport`: Open Paddock, Driver's World, Career Atlas.

Do not use “Racing Life”; the founding market audit already identifies a separate discontinued product with that name.

The currently shipped semi-historical driver career is implementation scaffolding for these modes, not a fourth Alpha 1.0 mode. A faithful historical season belongs either to the sequential Dynasty timeline or to a Passport thread.

## 2. Mode matrix

| Rule | Grand Prix Dynasty | Super Monaco GP | Racing Passport |
|---|---|---|---|
| Root character | One | One | One shared progression profile |
| Career entities | One sequential timeline | One SMGP entity | Many independent threads |
| Thread styles | Historical only | SMGP only | Historical or SMGP |
| Owner/team economy | Driver-owner; required product pillar | No historical tycoon economy | Driver career in Alpha 1.0 |
| Calendar | Chronological historical seasons | 17 ordinal seasons | Local to each thread |
| Progression horizon | Start through 2020 | Seasons 1–16 mastery, season 17 finale | 16 credited season-equivalents across the portfolio |
| Switching | No | No | At atomic race checkpoints |
| Injury/death | Root-career state | Root-career state | Thread-local alternate timelines |
| AMS2 staging | Current next round | Current next round | Active thread's next round only |

All three modes use progression version 2 for new characters: L300, 30 Racing DNA identities, 90 mastery skills, and the 499-SP lifetime mastery pool.

### 2.1 Exact legacy gate

The new serialized mode discriminator is optional/default-omitted for compatibility. Dispatch is exact:

```text
if progressionVersion < 2 OR experienceMode is absent:
    internal behavior = legacySingleCareer
else:
    experienceMode must be grandPrixDynasty | smgp | racingPassport
```

`legacySingleCareer` is an internal compatibility path, not a fourth Alpha mode and never a value written into an old save. It retains the current discovery, fold, character, continuation, and GUI behavior byte-for-byte. An absent field must never infer `grandPrixDynasty`, because that would activate owner/timeline rules in existing historical careers.

## 3. Grand Prix Dynasty

`grandPrixDynasty` is the historical driver-owner simulator. The product horizon is 1960 through 2020, but content availability remains honest: a year is playable only when a faithful season pack exists. Missing years are unavailable, never synthesized by mixing grids or borrowing a neighboring season.

Alpha contract:

- the character is both driver and team owner;
- the timeline advances chronologically through pinned historical packs;
- standings, seats, team ledger, staff/second driver, sponsors, development, repair costs, prize income, and bankruptcy all belong to this save;
- character age, injury, mortality, contracts, reputation, and progression are global to the timeline;
- the campaign progression plan contains an ordered `pinnedSeasonSequence` of the faithful packs available from the chosen start through 2020 at creation;
- `totalSeasons`, XP scale, and mastery timing derive only from that sequence and never recalculate when later packs are installed;
- SMGP packs are excluded from discovery and can never appear as a Dynasty year.

The complete 1960–2020 pack library is a content horizon, not permission to fabricate data. Alpha UI may show unavailable years/eras with an explicit coverage label and must show how many seasons the new save will actually pin. New faithful packs are available to new saves only; they never splice themselves into an existing Dynasty timeline.

## 4. Super Monaco GP

`smgp` retains the existing separate-career identity and every locked SMGP rule:

- 17 seasons and 16 races per season;
- A. Senna remains the permanent OP benchmark;
- rival challenges, clean two-win seat swaps, DNQ field, promotion/demotion, Paddock, dispatches, and finale state remain SMGP-gated;
- seasons 1–16 continue within `smgp-1`; season 17 terminates the campaign;
- historical pack discovery never consumes an SMGP pack.

The L300 progression plan releases the complete 499-SP pool at the season-16 review for a driver who has reached L300, allowing the final season to be driven with the complete build.

## 5. Racing Passport

### 5.1 Player experience

`racingPassport` is the freeform persistent-character mode:

1. Create one driver and Racing DNA identity.
2. Start a faithful historical career or a complete SMGP career thread.
3. Finish and commit a race.
4. Return to the Passport hub and start or resume a different thread.
5. Every thread resumes at its own exact round, seat, standings, offers, news, injury, and mode-specific state.
6. XP, level, Skill Points, attributes, mastery skills, and resets belong to the root Passport character and advance across the ordered portfolio activity.

Concurrent threads are alternate racing timelines, not a mixed-year fantasy grid. A 1967 thread and an SMGP thread may coexist, but each race still stages one faithful pack and one correct grid.

### 5.2 One database, not linked ordinary saves

Passport uses one SQLite database so a local race result and shared character progression commit atomically and replay from one ordered source of truth. Several ordinary career files pointing at one mutable external profile are forbidden: they cannot guarantee atomic XP updates, deterministic cross-thread ordering, or self-contained replay.

Recommended additive model:

```text
RacingPassportSave
  mode = racingPassport
  rootCharacter
    identity / DNA / creation baseline
    lifetimeXp / level / xp carry
    skill points / acquired skills / attribute rails
    reset spending and profile revision
  portfolioProgression
    masterySeasonEquivalents = 16
    creditedExperienceKeys[]       ordinal-sorted
    creditedReferenceProgress       exact Rational
  activityLedger[]                 authoritative order
  careerThreads{}                  ordinal-keyed by threadId
    threadId
    threadKind                     historical | smgp
    pinnedChildSeed
    pinned pack/version/calendar
    local PlayerCareerState
```

Global to the root Passport character:

- name, portrait, Racing DNA, creation baseline;
- lifetime XP, level, XP rational carry;
- SP, mastery ownership, attribute rails, full-tree reset spending;
- the ordered activity ledger and experience-credit set.

Local to each thread:

- age and seasons completed;
- seat, reputation, OPI, offers, standings, pace anchors;
- injury, mortality, career-over state;
- news and history;
- SMGP rival, seat, title, DNQ, and finale state where applicable.

Thread-local age/injury/death avoids impossible chronology. Dying in a 1967 alternate career ends that thread; it does not erase the same Passport driver's independent SMGP timeline. Global skills affect future activities in every live thread but never retroactively rewrite a committed earlier race.

Creating a thread copies the root creation age into that thread's local initial state. Age then advances only when that thread completes seasons. Age-window DNA/skill effects evaluate the active thread's journaled local age; DNA ownership and mastery ownership remain global.

### 5.3 Creating, switching, archiving

Switching is allowed only at an atomic checkpoint:

- before entering a new result, or after the current result and all derived rows commit;
- never with a pending result, incomplete season review, unresolved seat/promotion decision, destructive reset confirmation, or dirty skill purchase plan.

Unconfirmed UI work may be cancelled before switching. Selecting the active thread consumes no RNG and changes no race outcome. Only one AMS2 grid is staged at a time: activating a thread stages its next event and uses the existing full-restart warning when skin families conflict.

A thread with credited activity can be archived but never deleted, because its rows contributed to the global progression ledger. An empty thread may be deleted before its first committed activity. Archiving cannot alter XP, SP, replay, or credit keys.

## 6. Passport progression scaling

Passport has no single calendar horizon, so it must not inherit the large late-start multiplier of a short bounded historical campaign. Instead, each participating season is normalized to one standard season-equivalent:

```text
referenceSeasonXp = 40 * championshipRoundCount + 340
seasonScale = 980 / referenceSeasonXp
```

`980` is the high-performance reference for one 16-round SMGP season (`16 * 40 + 340`). Use the exact rational-carry algorithm from `character-progression-v2.md`, with carry scoped to the stable credited content-season key. Sixteen credited season-equivalents target 15,680 raw-reference XP, nearly identical to the L300 threshold of 14,951 XP.

Passport SP is the minimum of level progress and credited portfolio progress:

```text
levelPool = floor(499 * clamp(level - 1, 0, 299) / 299)
portfolioPool = floor(499 * min(creditedReferenceProgress, 15_680) / 15_680)
earnedSp = min(levelPool, portfolioPool)
```

`creditedReferenceProgress` is exact rational phase progress, not earned XP: each newly credited championship round adds `640 / championshipRoundCount`, and the first credited completion of that content season adds 340. A complete season therefore adds exactly 980 regardless of its authored round count. This gate paces mastery; performance still controls whether `levelPool` is high enough.

Non-championship events remain valid local thread activities but award zero global v2 XP and zero portfolio phase progress. The normalized denominator counts championship rounds only, so the eligible award population must match it.

Global XP credit is granted once per stable content occurrence:

```text
historical | packId | seasonYear | roundId
historical | packId | seasonYear | seasonComplete
smgp       | campaignSeasonOrdinal | roundId
smgp       | campaignSeasonOrdinal | seasonComplete
```

The exact pack version remains pinned in the thread/activity for replay but is deliberately excluded from the credit key, so installing a new version cannot re-award the same career progression.

Owning both tier-5 capstones in a family requires L285 plus the mode mastery checkpoint. Dynasty/SMGP use `completedSeasons >= masterySeason`; Passport uses `creditedReferenceProgress >= 15_680`. This holds the literal complete build for the intended mastery review even though the current 399-SP draft is cheaper than the total 499-SP pool.

Repeating the same content occurrence in a cloned thread may update that thread's local championship but grants no second global XP/SP credit. This prevents first-race farming while preserving the user's freedom to run alternate careers. The first credited result and its applied XP remain authoritative forever. All clones of one content season share the same global credit set and XP carry; local thread folds remain independent.

## 7. Activity ledger and replay

Existing database sequence numbers are not a sufficient semantic contract for cross-thread global progression. Passport adds a versioned INPUT activity ledger:

```text
PassportActivity
  ordinal
  activityId
  kind              createThread | race | skillPlan | skillReset | archiveThread
  threadId?
  payloadVersion
  payloadReference
  profileRevisionBefore
  profileRevisionAfter
  experienceCreditKey?
```

Replay processes activities in ordinal order:

1. Resolve the thread, pinned pack, pinned child seed, and profile revision.
2. Fold the local thread input with the root build that existed at that ordinal.
3. Apply global XP/SP or skill consequences exactly once.
4. Emit local and global DERIVED rows in the same transaction.
5. Reconstruct the sorted thread map and root profile.
6. Byte-compare the regenerated state/output.

Rules:

- every thread has an independently pinned child seed;
- keyed streams add `threadId` to the key; existing stream names and algorithms remain unchanged;
- switching and hub browsing consume no stream;
- SMGP behavior gates on the thread's pinned `careerStyle`, never the Passport root;
- `player.skillPlan` and `player.skillReset` are root-profile inputs in Passport;
- a skill acquired between two races affects only later activity ordinals;
- WipeDerived replays the one activity order and regenerates all thread/global derived rows;
- the f1db oracle is never touched.

## 8. GUI contract

The new-career entry presents exactly three primary cards. Each card states its persistence model before creation.

Passport adds a hub containing:

- global driver level, XP, SP, DNA, and current build;
- thread cards with style, pack/year or SMGP season, seat/team, next round, completion, local injury/death, and staged-content status;
- Continue, Create Thread, Archive, and Stage Next Event actions;
- an explicit “global progression / local timeline” explainer;
- a checkpoint blocker explaining why switching is temporarily unavailable.

Within a thread, the Driver screen shows global mastery beside that thread's local age, reputation, injury, and career history. The UI must never imply that standings or death leak between alternate threads.

## 9. Required verification

- Legacy v0/v1 careers remain byte-identical and infer their current single-career behavior when the new mode field is absent.
- An absent mode field never dispatches Grand Prix Dynasty/SMGP/Passport mechanics or serializes a replacement value.
- All new saves persist exactly one of the three stable mode IDs.
- SMGP never appears in Dynasty historical discovery.
- A Passport interleave `historical race -> skill plan -> SMGP race -> historical race` replays byte-identically.
- Reordering Passport activities is detected as divergence.
- Pre-skill races replay with the old profile revision; later races use the new revision.
- Injury, death, seats, standings, offers, and SMGP state never leak between threads.
- Switching consumes zero RNG and writes no gameplay-derived row.
- Two threads receive independent pinned child seeds.
- Local result plus global XP either commits completely or not at all under injected failure.
- A duplicate experience-credit key grants no second global XP but preserves the local result.
- Non-championship events grant zero global v2 XP/reference progress in every mode.
- L285 before the mastery checkpoint cannot purchase a second family capstone.
- Strong SMGP reaches L300/full 499 SP after season 16.
- Dynasty full and late-start campaigns use their pinned bounded horizon.
- Passport reaches the portfolio cap after 16 credited season-equivalents regardless of play order.
- Archiving a thread cannot change XP, SP, or replay.
- The 77/77 f1db oracle remains untouched.

## 10. Implementation order

1. Add an additive top-level mode discriminator and legacy inference; do not change current folds.
2. Finish progression-version-2 dispatch at L300.
3. Fix style-scoped SMGP continuation/discovery.
4. Introduce Passport container state, thread IDs, child seeds, and activity ordering behind a feature gate.
5. Reuse the existing historical and SMGP folds inside one thread at a time.
6. Add atomic local-result plus global-progression persistence/replay.
7. Add Passport hub projections and GUI.
8. Build the Dynasty owner/team economy on the same immutable historical timeline boundary.
9. Run the full deterministic, render, replay, and oracle gates before Alpha 1.0.
