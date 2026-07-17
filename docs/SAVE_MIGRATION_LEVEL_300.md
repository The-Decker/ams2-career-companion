# Save Integrity & Migration — the Level-300 Era

> How a career file survives app upgrades, re-simulation, snapshots, and corruption in the
> progression-v2 (L300 / 499-SP / 17-season) era. Everything here documents **implemented**
> behavior — every claim cites the code or test that enforces it. Companion docs:
> `docs/LEVEL_300_SYSTEM_SPEC.md` (the progression math itself), `docs/CAREER_GAME_OVER_FLOW.md`
> (what a terminal career looks like), `docs/dev/character-progression-v2.md` (the design record).

A career is **one SQLite file** (`*.ams2career`), opened in WAL mode with foreign keys on
(`CareerDatabase.Open`, `src/Companion.Data/CareerDatabase.cs`). Three independent versioning
mechanisms protect it, and they must not be confused:

| Mechanism | Keyed on | Governs |
|---|---|---|
| **Schema migrations** | `PRAGMA user_version` (`Migrations.cs`) | Tables and columns — the file's shape |
| **Result-envelope version** | `RoundResultEnvelope.Version` per stored round (`ResultStore.cs`) | What a raw result payload carries — the fold's inputs |
| **Character progression version** | `CharacterProfile.ProgressionVersion` per career (`CharacterLevelProgression.cs`) | Which XP curve and level cap the character folds under |

The first upgrades in place. The second and third **never** upgrade: old payloads parse with
defaults and old characters keep their curve forever, because both feed the byte-identical
replay contract.

---

## 1. The schema migration chain (v1 → v6)

`Migrations.Apply` (`src/Companion.Data/Migrations.cs`) runs a **forward-only** ordered script
array; index + 1 is the resulting `user_version`, each script runs in its own transaction with
the version bump committed atomically. `Migrations.CurrentVersion` is **6**
(`MigrationsV2Tests.V1CareerFileUpgradesInPlaceToCurrent` asserts it).

| v | Added | Nature |
|---|---|---|
| 1 | `career`, `pinned_pack`, `season`, `round_result_raw`, `journal` | The skeleton: identity + master seed, immutable hashed pack copies, verbatim raw results, the append-only journal |
| 2 | `driver_state`, `team_state`, `player_state` (stage `start`/`end`), `offer`, journal index | Career-sim state: `start` rows are sim INPUTS, `end` rows and offers are DERIVED season-end output |
| 3 | `round_player_state` | The unified per-round fold output (`ReplayService.FoldRound`) — DERIVED, wiped and rebuilt by re-simulation |
| 4 | `staging_override` | Cosmetic grid-editor overrides — user input, never read by the fold, untouched by `WipeDerived` |
| 5 | `career.mortality_mode INTEGER NOT NULL DEFAULT 0` | The career-wide `MortalityMode {0 Off, 1 Normal, 2 Hardcore}`, chosen once at creation; `DEFAULT 0` gives every pre-existing career Off in place (`MigrationsV2Tests.V4CareerFileGainsMortalityColumnDefaultingOff`) |
| 6 | `news_reading_state` | Per-story read/bookmark flags keyed by the story's stable dedupe key — user preference, never journaled, never a fold input; articles themselves are never stored (they re-render deterministically from the master seed) |

Invariants, each covered by `tests/Companion.Tests/Data/MigrationsV2Tests.cs`:

- **In-place upgrade preserves every row.** A genuine v1 file with rows in every v1 table opens
  at v6 with career/journal/results intact and the new tables live
  (`V1CareerFileUpgradesInPlaceToCurrent`, `V2CareerFileGainsTheRoundPlayerStateTable`).
- **Reopening is a no-op** (`ReopeningAnUpgradedFileIsANoOp`).
- **A newer file than the app is refused loudly**, never partially read:
  `"Career file schema v{N} is newer than this app understands (v6) — update the app instead of
  opening the file."` (`Migrations.Apply`; `NewerSchemaThanTheAppUnderstandsIsRefusedLoudly`).
- There is **no downgrade path** and no data-rewriting migration in the chain — every script to
  date is purely additive (new tables, or a new column with a backward-neutral default).

## 2. The pre-upgrade backup convention

Before the migration chain runs, `CareerDatabase.Open` takes a **one-time consistent sibling
snapshot** of any genuine old-format file:

- Trigger: `0 < user_version < Migrations.CurrentVersion` — i.e. a real file created by an older
  app, never a freshly created empty DB and never an already-current file.
- Mechanism: `VACUUM INTO` — a consistent single-file copy even under WAL.
- Name: **`<name>.ams2career.pre-v<N>.bak`** where `N` is the version the file was at *before*
  this upgrade.
- Idempotent: if that backup already exists it is not overwritten, so the *oldest* pre-upgrade
  state for a given version is what survives.

This is the safety net for a future destructive migration or a power loss mid-chain: there is
always a restorable pre-upgrade copy sitting next to the working file. (It is also why
`career.app_version` not being re-stamped on upgrade is acceptable — see Accepted deviations.)

## 3. Result-envelope version history (payload v1 → v9)

Raw round results are archived **verbatim** in `round_result_raw` and never touched by
re-simulation (`ResultStore`, `src/Companion.Data/ResultStore.cs`). The payload is the versioned
`RoundResultEnvelope` (`RoundResultEnvelope.CurrentVersion = 9`). The rule for every version
bump is identical: the new field is a **raw input the sim cannot re-derive**, its absence (every
older save) means "feature dormant", and a round without it folds byte-identically to the
pre-feature app. `RoundResultEnvelope.Parse` accepts every historical shape.

| v | Added field(s) | Dormant default |
|---|---|---|
| 1 | (bare `RoundResult`, pre-envelope) | Parsed with slider unknown → fold substitutes the last recommendation; DNF cause unknown → no-blame default |
| 2 | The envelope itself: `SliderUsed`, `PlayerDnfCause` | nulls = the v1 defaults above |
| 3 | `QualifyingOrder` | null = no qualifying; never reaches the standings engine or the f1db oracle |
| 4 | `IsWet` | null = neither `wetRound` nor `dryRound` perk condition fires |
| 5 | `CalledShot` (Setup Gamble bet) | null = no bet resolved |
| 6 | `SmgpRival` (rival call + seat-swap answer) | null = no battle folds |
| 7 | `PlayerAccidentSeverity` (Light/Medium/Heavy) | null = no accident roll drawn; shipped **capture-only** first, consumed by the d500 fold one slice later |
| 8 | `PlayerDidNotStart` (injured sit-out marker) | false = ordinary player fold; true skips the player update OPI-neutrally and emits the derived DNS row |
| 9 | `AiDnfCauses` (AI retirement cause letters) | null = team-level newsroom phrasing; **capture-only forever** — display readers only, never a fold input |

There is deliberately **no payload migration**: a v1 payload stays v1 bytes on disk for the life
of the career, because those bytes are the replay contract's ground truth (`ResultStore.Append`
stores verbatim; a corrected re-import replaces bytes and leaves an audit journal row).

## 4. Additive JSON state — legacy blobs stay byte-identical

Career state blobs (`player_state`, `round_player_state`, …) serialize through `CoreJson` with
every progression-v2 field marked `[JsonIgnore(Condition = WhenWritingDefault)]`
(`src/Companion.Core/Career/CareerStates.cs`: `Level`, `Xp`, `ExperienceMode`,
`CampaignProgressionPlan`, `XpScaleRemainder`; `src/Companion.Core/Character/CharacterProfile.cs`:
`ProgressionVersion`, `RacingDna*`, `AcquiredSkillIds`, `MasteryEffectsVersion`,
`SkillPointsSpent`, `XpSpentOnResets`, `SkillResetCount`, …).

The consequence is the save-format guarantee the whole v2 rollout rests on: **a legacy career's
state blobs serialize to exactly the same bytes as before v2 existed.** This is pinned by
`CharacterProgressionV2StateTests.LegacyStateStillOmitsEveryVersionTwoKey`
(`tests/Companion.Tests/Career/CharacterProgressionV2StateTests.cs`), which serializes a legacy
state and profile and asserts that *every* v2 key (`experienceMode`, `campaignProgressionPlan`,
`xpScaleRemainder`, `racingDna`, `skillPointsSpent`, …) is absent from the JSON.

The same default-omitted-flag pattern gates SMGP fold behavior per career
(`PerSeasonDnq`, `PerSeasonVariety`, `StandingsReshuffle` on the season START state — read in
`ReplayService.ResimulateCore`), so a legacy career replays against the authored grid forever.

## 5. Progression versions are immutable — no migration ever rewrites one

`CharacterLevelProgression` (`src/Companion.Core/Character/CharacterLevelProgression.cs`)
dispatches every curve operation on the profile's `ProgressionVersion`:

| Version | Curve | Cap |
|---|---|---|
| 0 `LegacyVersion` | The shipped geometric curve (`rules.Levels.XpCurve`) | `XpCurve.MaxLevel` |
| 1 `EraCappedVersion` | Same curve + era soft cap | min(curve max, `SoftCapForYear`) |
| 2 `Level300Version` | Deterministic integer L300 curve (`Level300XpForLevel`: `40 + 21·(L−2)/298` per level) | `Level300Max = 300` |

The version is set **exactly once, at character creation**
(`src/Companion.ViewModels/Wizard/CharacterViewModel.cs` — the legacy path stamps 1, the alpha
path stamps `CharacterLevelProgression.Level300Version`), and no code path anywhere rewrites it:
there is no schema migration, no fold transition, and no upgrade hook that touches
`ProgressionVersion`. An unknown version **throws** (`NotSupportedException`,
`CharacterLevelProgression.Unsupported`) rather than guessing a curve.

Why: total XP is journaled and the level is *derived*. Recalculating a v0/v1 career onto the v2
curve would change every historical level-up event and break byte-identical replay. Existing
careers therefore keep their curve **and their total XP** — preservation by *not* migrating.
(Design record: `docs/dev/character-progression-v2.md`; ledger position:
`docs/dev/smgp-alpha-finish-status.md`, Accepted deviations.)

The v2 arithmetic on top is exact by construction (`CharacterProgressionV2Math`,
`src/Companion.Core/Character/CharacterProgressionV2Math.cs`): XP awards scale through the
pinned campaign plan's rational `XpScaleNumerator/XpScaleDenominator` with an integer
`XpScaleRemainder` carried award-to-award (floor division, no floats), and Skill Points are the
dual-gated `min(LevelPool, SeasonPool)` out of `LifetimeSkillPoints = 499` — overspend clamps
availability to zero, a negative persisted spend throws.

## 6. Why level 301 and season 18 cannot exist in a save

**Level 301.** The persisted `PlayerCareerState.Level` is never written directly by any input
path — every writer derives it from the journaled XP total through
`CharacterLevelProgression.LevelForTotalXp` (`RoundUpdate.cs:196/225`,
`SeasonEndPipeline.cs:253/281`, `CharacterSkillReset.cs:145`). For v2 that derivation
(`Level300ForTotalXp`) is a loop that terminates at `Level300Max = 300`, and the curve functions
refuse to even *price* a level above it (`Level300XpForLevel` and `CumulativeXpToLevel` throw
`ArgumentOutOfRangeException` past the cap). A capped driver's further XP banks as lifetime/reset
XP, "never as progress toward a level 301" (`CharacterDossier.XpIntoLevel`,
`src/Companion.Core/Character/CharacterDossier.cs`). So a save carrying level 301 can only be a
tampered file — and a tampered level diverges the journal byte-compare on the next re-simulation.

**Season 18.** New season rows are only created from a continuation plan, and for SMGP the
single planner is `PackDiscovery.PlanNextSeason`
(`src/Companion.ViewModels/Services/PackDiscovery.cs`): `if (seasonOrdinal >=
SmgpRules.CampaignSeasons) return null;` — at ordinal 17 (`SmgpRules.CampaignSeasons = 17`,
`src/Companion.Core/Smgp/SmgpRules.cs`) there is no next-season pack, ever. The campaign
terminates into the locked finale (`SmgpRules.CampaignComplete`), it does not roll over. The
ordinal itself is not a stored counter that could drift: it is derived identically on the live
path and in replay as the season row's 1-based index in `CareerStore.ReadSeasons` order
(`ReplayService.ResimulateCore`).

## 7. No-reroll guarantees under re-simulation

`ReplayService.Resimulate` (`src/Companion.Data/ReplayService.cs`) is the integrity audit:

- **What is wiped**: derived rows only — `offer`, `round_player_state`, stage-`end`
  driver/team/player states (`StateStore.WipeDerived`). **What is never wiped**: raw results,
  pinned packs, the stored journal, stage-`start` states, `staging_override`,
  `news_reading_state`.
- **One transaction, commit only on byte-identity.** Every season refolds through the *same*
  `ComputeRoundFold`/`SeasonEndPipeline` code the live path ran, and the regenerated journal
  sequence byte-compares against the stored one (phase, entity, deltaJson, cause, round; seq/utc
  and provenance-excluded input rows aside). Any divergence rolls the whole transaction back —
  a divergence report never costs data.
- **Accident and fatality rows cannot re-roll.** The d500 injury/death roll is a *derived*
  journal row: its inputs are the stored envelope severity + the master-seed stream, and the roll
  **and** its outcome are inside the byte-compare. `AccidentFoldDeterminismTests`
  (`tests/Companion.Tests/Data/AccidentFoldDeterminismTests.cs`) prove it end-to-end:
  `MortalityCareer_SurvivesAnAccident_EmitsDerivedRow_AndReplaysByteIdentically`,
  `NormalAccidentDeath_SetsDeceased_KeepsTheFile_RefusesRounds_AndReplaysByteIdentically`, and
  `OffCareerWithCharacter_Accident_DrawsNothing_AndReplaysByteIdentically` (Off-mode careers
  draw zero from the accident stream, which is what keeps pre-mortality saves byte-identical).
- **XP rows cannot re-roll.** Progression events are pure functions of (stored payload, folded
  state, seed) — the v2 rational carry (`XpScaleRemainder`) rides in the folded state, so the
  same award sequence reproduces the same integer XP forever.
- **Double-fold is structurally refused.** `FoldRound` throws if the round already has a
  `round_player_state` row ("corrected results go through a re-import plus Resimulate, never a
  second fold"); `ImportAndFoldRound` makes raw store + fold one atomic commit so the live path
  cannot bypass the fold; `RunSeasonEnd` refuses an already-complete season.
- **Player choices are inputs, not derived state.** Accepted offers and `smgp.swap` decisions
  are re-applied/replayed; an accepted team missing from the regenerated offer set is a reported
  divergence (`Reason = "accepted-offer"`), never a silent drop.

## 8. Snapshots, autosaves, and the Hardcore file contract

File-level save & reload (`SaveSlotStore`, `src/Companion.Data/SaveSlotStore.cs`) sits entirely
**outside** the fold/replay contract — each snapshot is a complete career DB, so restoring one
can never desynchronize a journal:

- **Snapshot**: SQLite online-backup API (`BackupDatabase`) from the *live* connection (WAL data
  included), written to a temp file and swapped in with a single `File.Move` so a failed backup
  never destroys a prior good slot. Layout: `Saves/<careerStem>/<slotId>.ams2save` + a
  `<slotId>.json` metadata sidecar. Autosaves are ordinary slots with `IsAutosave = true`
  (`autosave-season-N` naming).
- **Restore** (`SaveSlotStore.Restore`): wholesale copy over the closed working file, with the
  stale `-wal`/`-shm` siblings deleted *first* so no leftover WAL frame can be recovered onto
  the restored state. Covered by
  `SaveSlotStoreTests.Save_ThenMutate_ThenRestore_RevertsTheCareerWholesale`.
- **Degraded listing** (`SaveSlotStore.List`): a snapshot whose sidecar is corrupt or missing
  still lists — as a degraded entry (`IsDegraded = true`, label
  `"<slotId> (recovered — details unreadable)"`, timestamp from the file clock) — because only
  the snapshot file matters to `Restore`. Restorable data is surfaced, never silently hidden.
  A sidecar with no surviving snapshot is stale and is not offered.
- **Hardcore death** (`SaveSlotStore.DeleteCareerAndAllSaves`): physically deletes the career
  file, its WAL/SHM siblings, and the entire Saves folder. The caller gates it on a real folded
  death in a `MortalityMode.Hardcore` career
  (`AccidentFoldDeterminismTests.HardcoreAccidentDeath_PhysicallyDeletesTheCareerFileAndSaves`).
  Normal-mode death keeps the file (memorial state); only Hardcore burns it.

## 9. What happens to invalid or corrupt data

The design stance is *refuse loudly or surface degraded — never guess, never silently repair*:

| Condition | Behavior | Source |
|---|---|---|
| Schema newer than the app | `InvalidOperationException` "…newer than this app understands (v6) — update the app instead of opening the file" | `Migrations.Apply` |
| Pinned pack blob unparseable | `InvalidDataException` "The pinned copy of {pack} could not be parsed — the career file is damaged" | `ReplayService.Resimulate` (multi-pack overload) |
| Supplied pack ≠ pinned sha256 | Refused before any wipe (`VerifyPackIsThePinnedOne`) | `ReplayService.Resimulate` |
| Round already folded | Throws; corrections = re-import + `Resimulate` | `ReplayService.FoldRound` |
| Raw result missing for the round being folded | Throws (import and fold are atomic) | `ReplayService.FoldRound` |
| Imported rounds without folded state at season end | Throws with the re-simulate instruction | `ReplayService.RunSeasonEnd` |
| Missing v2 pre-race conditions on a round that requires them | Throws (`"missing the persisted pre-race conditions required by its progression-v2 conditional player-car physics"`) | `ReplayService.ValidatePlayerRoundConditions` |
| Tampered/changed journal, states, spends, resets, skill plans | Report-only divergence (`ReplayDivergence.Reason` names the first failing check), full rollback, zero data cost | `ReplayService.ResimulateCore` |
| Corrupt/missing save-slot sidecar | Degraded slot listing, still restorable | `SaveSlotStore.List` |
| Restore of an unknown slot | Throws before touching the live career (`SnapshotExists` pre-check available) | `SaveSlotStore.Restore` |
| Negative persisted SP spend / out-of-range XP remainder | Throws (`ArgumentOutOfRangeException`) — invalid state cannot manufacture Skill Points | `CharacterProgressionV2Math` |
| Unknown progression version | Throws `NotSupportedException` | `CharacterLevelProgression` |

---

## Accepted deviations

Positions where this repo deliberately differs from the SMGP-300 mission spec's persistence
demands. Both are decided in the coordinator ledger
(`docs/dev/smgp-alpha-finish-status.md`, "Accepted deviations") — do not re-litigate without
Mike.

1. **`career.app_version` records the creating app version only.** The spec wants an upgrade
   audit trail; the `career` table stores the version stamped at `CareerStore.CreateCareer` and
   nothing updates it on subsequent opens (there is no UPDATE path in `CareerStore`). Rationale:
   the schema version *is* the upgrade state (`PRAGMA user_version`), and the support case a
   per-upgrade stamp would serve is covered by the `.pre-v<N>.bak` pre-upgrade file backup
   (§2), which preserves the actual pre-upgrade bytes rather than a version string. Logged as a
   known limitation (ledger item 6).

2. **v0/v1 careers keep their original curve — they are never recalculated onto the v2 L300
   curve.** The spec reads naturally as "migrate everyone to Level 300"; the repo instead makes
   `ProgressionVersion` immutable per career (§5) and only creation can produce a v2 profile.
   Rationale: replay preservation. Levels are derived from journaled XP; rebasing the curve
   would rewrite every historical level event and break the byte-identical re-simulation
   contract (the #2 never-break invariant, `docs/PROJECT.md` §0). Total XP is preserved
   precisely *by not migrating* — the journal keeps the career's true earning history, and a
   legacy career remains a verifiable artifact on the rules it was actually played under
   (design: `docs/dev/character-progression-v2.md`; ledger item 5 records the related
   delevel-on-negative-XP retention for legacy careers).
