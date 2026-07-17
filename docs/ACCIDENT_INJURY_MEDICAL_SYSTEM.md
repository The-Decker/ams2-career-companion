# Accident, injury & medical system

> What the shipped accident → injury → recovery pipeline actually does, grounded in the code.
> Design decisions and build history: `docs/dev/character-death-injury.md` (the plan this implements,
> including Mike's resolved decisions A–D). Everything here cites the implementing type or test.

---

## 1. The pipeline at a glance

```
player marks own accident DNF + severity        (raw input — envelope, not re-derivable)
        │
        ▼
quadruple gate in the round fold                (ReplayService.ComputeRoundFold)
  mortality ≠ Off · not already deceased ·
  character present · severity present
        │
        ▼
ONE d500 draw from a fresh keyed stream         (AccidentFold.Apply — CareerStreams.Accident)
        │
        ▼
safety offset + severity bands → outcome        (AccidentModel.Resolve)
  none | minorInjury(1–2) | seasonEnding | death
        │
        ▼
structured journal row (byte-compared)          (JournalPhases.PlayerAccident + optional headline)
        │
        ▼
folded state                                    (PlayerCareerState.RaceSuspensionRemaining /
        │                                        SeasonEndingInjury / Deceased)
        ▼
availability everywhere                         (PlayerMortalityStatus, CurrentSitOut,
        │                                        CharacterDossier.Availability, calendar)
        ▼
auto-simulated sit-out rounds                   (AutoSimulateRound → AutoRaceModel, player DNS)
        │
        ▼
healing                                         (suspension −1 per DNS round; season-end reset)
        │
        ▼
truthful history                                (InjuryHistory medical record, newsroom events,
                                                 InjuryRecovery thread, calendar SatOut)
```

Every stage is deterministic and replay-byte-identical; every stage is display-truthful (an absence
is never rewritten as participation).

---

## 2. Opt-in: the mortality axis

`MortalityMode { Off, Normal, Hardcore }` (`src/Companion.Core/Career/MortalityMode.cs`), seeded
once at career creation and mirrored into the start `PlayerCareerState` so the fold reads it without
a DB hop.

| Mode | Injury/death | Saves | On death |
|---|---|---|---|
| `Off` (default 0) | none — zero accident-stream draws, byte-identical to a pre-feature save | n/a | n/a |
| `Normal` | on | full file-level save/restore (`CareerSessionService.SaveToSlot`/`RestoreSlot`, gated by `SavesEnabled`) | career refuses rounds; the death screen offers a restore |
| `Hardcore` | on | none (`SavesEnabled == false`, `SaveSlots()` returns `[]`) | the career file **and every snapshot are physically deleted** (`SaveSlotStore.DeleteCareerAndAllSaves` in `CareerSessionService.Apply`) |

Verified by `AccidentFoldDeterminismTests.OffCareerWithCharacter_Accident_DrawsNothing_AndReplaysByteIdentically`
and `...HardcoreAccidentDeath_PhysicallyDeletesTheCareerFileAndSaves`
(`tests/Companion.Tests/Data/AccidentFoldDeterminismTests.cs`).

---

## 3. The severity input (raw, never re-derived)

`AccidentSeverity { Light, Medium, Heavy }` (`src/Companion.Core/Career/AccidentSeverity.cs`) is
captured **only** when the player marks their **own** DNF as accident (`"a"`); it rides the
versioned result envelope as `RoundResultEnvelope.PlayerAccidentSeverity`
(`src/Companion.Data/ResultStore.cs`), null on every non-accident DNF and every pre-feature save.
Severity never changes scoring or OPI — it feeds only the injury roll.

---

## 4. The d500 roll and the safety offset

`AccidentFold.Apply` (`src/Companion.Core/Career/AccidentFold.cs`) draws **exactly one** integer
1..500 from a fresh per-round stream keyed `(accident, year, round, "player")`
(`CareerStreams.Accident`) off the career's master seed. The stream is independent of every other
stream, so re-creating it on replay rolls the same d500 and no other subsystem shifts.

`AccidentModel.Resolve` (`src/Companion.Core/Character/AccidentModel.cs`) then shifts the roll by a
deterministic **integer** safety offset — never a second draw, so the draw count stays fixed:

```
offset    = round( (durability + InjuryDurabilityDelta − 0.5) × SafetyDurabilityScale
                   − InjuryBaseAdd × SafetyBaseAddScale )        // AwayFromZero
effective = clamp(roll − offset, 1, 500)
```

High durability and protective injury perks lower the effective roll (toward the safe bands);
a reckless injury `baseAdd` (e.g. `hot_head`) raises it toward death. Default scales: 80 / 200
(`AccidentModel.DefaultRules`). The effective roll is bucketed against the severity's bands —
inclusive upper bounds, first band whose `UpTo` the roll does not exceed wins.

### Default bands (tunable data)

The bands live in the `accident` block of `data/rules/perks.json` (parsed onto
`CharacterRules.Accident`; `AccidentModel.DefaultRules` is the code fallback — both asserted by
`AccidentFoldDeterminismTests.PerksJson_ShipsTheAccidentBands` and
`CharacterRules_RejectsAnUnknownAccidentBandOutcome_AtLoad`). One d500 unit = 0.2%.

| Severity | none | minorInjury (miss 1) | minorInjury (miss 2) | seasonEnding | death |
|---|---|---|---|---|---|
| **Light** | 1–490 (98%) | 491–500 (2%) | — | — | — |
| **Medium** | 1–410 (82%) | 411–470 (12%) | 471–490 (4%) | 491–497 (1.4%) | 498–500 (0.6%) |
| **Heavy** | 1–250 (50%) | 251–380 (26%) | 381–450 (14%) | 451–485 (7%) | 486–500 (3%) |

**Light never kills and never ends a season** (Mike, decision B, 2026-07-12 — recorded in
`docs/dev/character-death-injury.md` §3.4/§9). Because Light's top band is a one-race minor injury,
the clamp — which piles a reckless driver's overflow rolls onto the last band — can only ever cost a
fragile driver one race on a light shunt, never their life. The glass-cannon amplification is
deliberate and lives on Medium/Heavy only.

---

## 5. The outcome ladder

`AccidentOutcomeKind { None, MinorInjury, SeasonEnding, Death }`
(`src/Companion.Core/Character/AccidentModel.cs`), with `MissRaces` on a minor injury and the
effective roll journaled for the Why? inspector.

| Repo outcome | Effect on state (`AccidentFold.Apply`) | Mission-spec vocabulary |
|---|---|---|
| `none` | no change (a scare; no headline) | "no injury" |
| `minorInjury` (miss 1–2) | `RaceSuspensionRemaining = max(current, MissRaces)` | "minor/moderate injury — misses races" |
| `seasonEnding` | `SeasonEndingInjury = true` (returns next season) | "major/season-ending injury" |
| `death` | `Deceased = true` — terminal | "fatal accident" |

The mission spec's **career-ending-but-alive** tier has no repo band — see Accepted deviations.

---

## 6. Journal row and news headline

Every resolved accident (including a harmless `none`) emits one byte-compared derived row,
`JournalPhases.PlayerAccident` (`player.accident`), with delta
`{ severity, roll, effectiveRoll, outcome, missRaces }` and a cause of
`accident-none|accident-injury|accident-season-ending|accident-death` (`AccidentFold.Apply`).
A consequential outcome also emits a `news.headline` row ("…ruled out of the next race",
"…season ends in the barriers", "Tragedy: … killed in a racing accident"); a harmless scare stays
silent. This journal row is the single source of truth every downstream surface reads back —
death screen, medical record, newsroom.

---

## 7. Folded state and the availability surfaces

State lives on `PlayerCareerState` (`src/Companion.Core/Career/CareerStates.cs`), all
`[JsonIgnore(WhenWritingDefault)]` so an Off career serializes byte-identically:
`int RaceSuspensionRemaining`, `bool SeasonEndingInjury`, `bool Deceased`.

Display surfaces are pure projections of that folded state — never a second rules engine:

| Surface | Where | Shape |
|---|---|---|
| `PlayerMortalityStatus` | `CareerSessionService.PlayerMortality()` (`src/Companion.ViewModels/Services/CareerSessionService.cs`) | mode + the three fields + `CareerFileDeleted`; `IsFit => !Deceased && !SeasonEndingInjury && RaceSuspensionRemaining == 0`. After a Hardcore delete it answers without touching the (gone) DB. |
| `SitOutStatus` | `CareerSessionService.CurrentSitOut()` | the sit-out banner: `"INJURED — auto-simulating round (N remaining)"` or `"SEASON OVER — recovering"`; null when fit, dead, or season complete. |
| `CharacterDossier.Availability` + `AvailabilityLabel` | `CharacterDossier.Build` (`src/Companion.Core/Character/CharacterDossier.cs`) | `AvailabilityStatus { Fit, Injured, SeasonOver, Deceased }` with the same precedence as `IsFit` (deceased > season-ending > suspended > fit). Off careers carry all-default fields, so they read Fit. |
| Death screen | `CareerSessionService.DeathScreen()` → `DeathScreenModel` | built from the fatal `player.accident` row (severity + venue) + the whole career record; on Hardcore captured from the intact DB **before** deletion (the `DeathScreenHandoffTests` contract). |

---

## 8. Entry-path gates — ALL of them

An unfit player cannot be scored by **any** caller; the rule holds at the service layer, not merely
in the shipped UI (`CareerSessionService`, "results" section):

| Path | Gate |
|---|---|
| `Preview(draft)` | throws on deleted file, `Deceased`, SMGP `CareerOver`, season complete, and **injured** (`RaceSuspensionRemaining > 0 || SeasonEndingInjury`) — "this round is auto-simulated" |
| `Apply(draft)` | the same five gates, then (only here) the Hardcore alive→dead transition deletes the file |
| `AutoSimulateRound()` | the exact **inverse** fit-check: throws when the driver is fit ("enter this round's result manually"), plus the deleted/deceased/CareerOver/season-complete guards |

So an injured driver's round folds through exactly one door, and a fit driver's through exactly the
other. Verified end-to-end by
`InjuryAvailabilityGateTests.InjuredRound_RefusesApplyAndPreview_AutoSimHeals_ThenApplyWorksAgain`
(`tests/Companion.Tests/Data/InjuryAvailabilityGateTests.cs`) and
`AccidentFoldDeterminismTests.NormalAccidentDeath_SetsDeceased_KeepsTheFile_RefusesRounds_AndReplaysByteIdentically`.

---

## 9. Auto-simulated sit-out rounds

AMS2 cannot spectate a single-player race, so a round the injured player misses is simulated by the
app (decision D, `docs/dev/character-death-injury.md` §5):

- **Field result:** `AutoRaceModel.ClassifiedOrder` (`src/Companion.Core/Career/AutoRaceModel.cs`)
  ranks every **non-player** seat by resolved `SeatStrengthModel.Strength` plus a seeded ±0.25
  jitter from `CareerStreams.AutoRace` keyed `(auto-race, year, round, driverId)`, ties broken by
  driver id — a pure function of `(masterSeed, year, round)` + the resolved grid
  (`AutoSimFoldTests.AutoRaceModel_IsDeterministic_ExcludesThePlayer_AndCoversTheField`).
- **Storage:** `AutoSimulateRound` stores the generated classification as the round's envelope with
  `PlayerDidNotStart = true` and a neutral slider, provenance cause `"auto-simulated"` — so the
  championship genuinely advances for the AI and replay is unambiguous about which rounds were
  auto-sims.
- **Fold:** on `PlayerDidNotStart` the fold (`ReplayService.ComputeRoundFold`, DNS branch) carries
  the player state forward **verbatim** — OPI-neutral, no `player.opi`/reputation/pace rows — heals
  one race of a minor suspension, and emits a derived `player.dns` row
  (`JournalPhases.PlayerDidNotStart`) with `{ round, reason: injury|season-ending, suspensionRemaining }`.

---

## 10. Healing

Recovery is measured in **rounds**, mechanically:

- **Minor injury:** `RaceSuspensionRemaining` decrements by 1 per auto-simulated DNS round
  (`ReplayService.ComputeRoundFold` DNS branch); at 0 the gates flip and manual entry works again
  (`AutoSimFoldTests.MinorInjury_SkipsARound_HealsToFit_AndReplaysByteIdentically`,
  `...MissesTwoRaces_ThenReturns_AndReplaysByteIdentically`).
- **Season-ending:** every remaining round auto-sims; the season-end pipeline resets
  `SeasonEndingInjury = false` and `RaceSuspensionRemaining = 0` over the break
  (`SeasonEndPipeline`, `src/Companion.Core/Career/SeasonEndPipeline.cs:358-373`) — the driver
  returns next season (`AutoSimFoldTests.SeasonEndingInjury_SkipsTheRestOfTheSeason_AndClearsAtTheReset`).
- **`Deceased` is terminal** and deliberately **not** reset at season end — it carries verbatim,
  exactly like the SMGP `CareerOver` floor (same pipeline code, commented in place).

A second, older injury mechanism coexists: the **season-end injury roll**
(`src/Companion.Core/Character/InjuryModel.cs`), perk-gated (only a character carrying an
`injury`-stream perk rolls — `InjuryModel.HasInjuryPerk`), hazard
`clamp(0.10 + (0.5 − durability)·0.4 + baseAdd + seasonInjuryLoad, 0, 0.85)`, costing
`RepPenalty = 8.0` reputation. It is an off-season **reputation** setback, OPI-neutral, and does not
touch availability; the dossier's `InjuryRisk` ("Low/Moderate/High") projects this hazard.

---

## 11. Calendar projection

`CareerSessionService` schedule building (around `:1321-1386`) assigns each round a
`SchedulePlayerStatus { Upcoming, Raced, SatOut, WillMiss }`
(`src/Companion.ViewModels/Services/ICareerSession.cs:587`):

- **Applied rounds** read the stored envelope's `PlayerDidNotStart` flag → `SatOut` vs `Raced` — an
  injury absence is never rewritten as participation.
- **Future rounds** project the ACTIVE suspension (`RaceSuspensionRemaining`, or every remaining
  round for season-ending/deceased) → `WillMiss`, so "you will miss the next 2 rounds" is visible
  on the calendar before it happens.

The campaign timeline likewise flags injury seasons: `CampaignTimelineEntry.MissedRounds` comes from
the newsroom event spine's `SatOutRound` events (`CareerSessionService.CampaignTimeline()`).

---

## 12. The medical record

`CareerSessionService.InjuryHistory()` walks every season's journal for `player.accident` rows with
a consequential outcome (`minorInjury`/`seasonEnding`/`death`) and projects them verbatim into
`InjuryHistoryEntry { SeasonOrdinal, SeasonYear, Round, Outcome, MissRaces, Label, Description }` —
the persisted fold outcome, never recomputed
(`InjuryAvailabilityGateTests.InjuryHistory_ProjectsTheForcedMinorInjury_Verbatim`,
`InjuryHistory_IsEmptyForAnUninjuredCareer`).

`Description` comes from `InjuryFlavor.Describe` (`src/Companion.Core/Career/InjuryFlavor.cs`):
a **non-graphic, deterministic** description ("bruised ribs", "a broken leg") picked by an FNV-1a
hash of the outcome's own identity (`injury-flavor|season|round|outcome`) — **no RNG stream**, so it
can never perturb the fold or replay, and reopening the career always describes the same injury the
same way. It is in-game simulation flavour only, never a medical claim; `none` and `death` outcomes
carry no description (a fatality is never captioned with clinical detail).

---

## 13. News coverage

The newsroom event spine (`src/Companion.Core/Newsroom/CareerNewsEvents.cs`) detects, per round:

| Event kind | Trigger |
|---|---|
| `PlayerInjured` | `player.accident` outcome `minorInjury` |
| `SeasonEndingInjury` | outcome `seasonEnding` |
| `PlayerDied` | outcome `death` (also sets `memory.CareerEnded`) |
| `SatOutRound` | a `PlayerDidNotStart` round (carries `MissRaces`) |
| `ReturnedFromInjury` | the first genuine start after sit-out rounds — the comeback, once per absence, with the misses count and the comeback finish |

`StoryThreads.BuildInjuryRecovery` (`src/Companion.Core/Newsroom/StoryThreads.cs`) folds those beats
into an `InjuryRecovery` thread ("The road back from injury"): `Developing` while out, `Dormant`
for a season-ending absence, `Resolved` on the comeback (the explicit `ReturnedFromInjury` event, or
— for older careers predating that detection — any later classified result), `Historic` once the
season completes. Progression-side, `ReturnedFromInjury` is one of the v2 progression event kinds
alongside `LevelMilestone`/`Level300Reached`/`CareerCompleted`.

---

## 14. Determinism & no-reroll guarantees

1. **One draw, fixed key.** The only randomness is the single d500 from
   `(accident, year, round, "player")`; the safety offset is a pure integer function; auto-sim
   jitter uses its own `(auto-race, ...)` keys. No outcome is ever re-rolled — replay re-derives the
   identical roll, outcome, and journal bytes
   (`AccidentFoldDeterminismTests.MortalityCareer_SurvivesAnAccident_EmitsDerivedRow_AndReplaysByteIdentically`,
   every `AutoSimFoldTests` replay assertion).
2. **Raw vs derived split.** Severity and `PlayerDidNotStart` are raw envelope inputs (excluded from
   the byte-compare by mechanism); the roll, outcome, state change, DNS row, and headlines are
   derived, byte-compared rows.
3. **Zero-draw gating.** The quadruple gate (`ReplayService.ComputeRoundFold` accident block:
   mortality ≠ Off ∧ not deceased ∧ character + rules present ∧ envelope severity present) means an
   Off / no-character / non-accident round draws nothing and stays byte-identical to a pre-feature
   save.
4. **Display never folds.** Flavor text hashes persisted identity; availability, calendar, medical
   record, and news are all projections of journaled state.
5. **The one destructive op** — Hardcore file deletion — fires only on a genuine alive→dead
   transition in `Apply`, never on replay (replay runs `ReplayService` directly and never that
   path). Post-death reads are covered by `DeathScreenHandoffTests` and `PostDeathArchiveTests`.

---

## Accepted deviations

Where the SMGP-300 mission spec asked for something the repo deliberately does differently. The
decided positions live in `docs/dev/character-death-injury.md` (decisions A–D, resolved with Mike
2026-07-12).

| Spec asked for | Repo does | Rationale |
|---|---|---|
| **Replacement driver** fills the player's seat during an absence | No replacement driver: the sit-out round is auto-simulated with the player **excluded** (DNS), so the constructor fields one car short during the absence — documented, not hidden (`AutoRaceModel.ClassifiedOrder` excludes `GridSeat.IsPlayer`; the calendar shows `SatOut`) | Decision D (`character-death-injury.md` §5/§9): AMS2 cannot spectate a single-player race, so the app simulates the field; inventing a stand-in driver would fabricate results for a person who never existed in the pack and complicate the replay contract for zero fold value. The absence is surfaced truthfully instead of papered over. |
| A **career-ending (alive)** outcome band between season-ending and death | No such band — the ladder is `none / minorInjury / seasonEnding / death` (`AccidentOutcomeKind`) | The design collapses "career over" into the two states that already have honest mechanics: death (terminal, `Deceased` mirrors SMGP `CareerOver`) and season-ending (returns next year). A forced alive-retirement tier would need a whole retired-career surface the product does not have; the band table stays tunable data if it is ever wanted (`AccidentRules`). |
| **Per-injury stat/pace penalties** while recovering or after return | None — availability IS the effect. An injured player's rounds are skipped OPI-neutrally (no `player.opi`/reputation/pace rows in the DNS fold); the only stat cost in the system is the separate season-end injury roll's `RepPenalty = 8.0` reputation hit (`InjuryModel`) | The fold's invariant is that every derived number is auditable from journaled inputs; a lingering hidden debuff would make post-injury results unexplainable in the Why? inspector. Missing races (points, streaks, milestones) is already a real, legible cost. |
| Recovery measured in **calendar dates / weeks** | Recovery measured in **rounds**: `MissRaces` → `RaceSuspensionRemaining`, decremented one per auto-simulated round; season-ending heals at the season reset (`ReplayService` DNS branch; `SeasonEndPipeline:358-373`) | The career clock is the round sequence — packs have dates but the sim advances round-by-round, and a rounds-based countdown is exact, deterministic, and visible on the calendar (`WillMiss` projection). A wall-clock model would be cosmetic at best and non-deterministic at worst. |
