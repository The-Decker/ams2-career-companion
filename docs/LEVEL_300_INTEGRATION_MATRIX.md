# Level-300 integration matrix — feature → window traceability

> Companion doc to `LEVEL_300_SYSTEM_SPEC.md` / `ACCIDENT_INJURY_MEDICAL_SYSTEM.md` /
> `CAREER_GAME_OVER_FLOW.md`. One row per SMGP-300 mission feature (§14): who owns the logic in
> `Companion.Core`, where it crosses the `ICareerSession` seam
> (`src/Companion.ViewModels/Services/ICareerSession.cs`), what persists it, which window binds it,
> and the test class that pins it. Everything here describes **implemented** behavior — cited to the
> actual member or test. Where the repo deliberately differs from the mission spec, the row says
> DEVIATION and the final section carries the rationale.

## How to read the Status column

| Status | Meaning |
|---|---|
| **SHIPPED** | Core → seam → ViewModel → XAML all wired; tests green. |
| **VM READY, GUI PENDING** | Core/seam/ViewModel complete and tested; the XAML binding is queued for the GUI lane (`docs/dev/codex-gui-smgp300-brief.md` — the section number is cited per row). The lane boundary is strict: the CODE lane never edits `src/Companion.App/**`. |
| **PARTIAL** | Behavior shipped, but with a named gap (stated in the row). |
| **DEVIATION** | The repo deliberately does something different from the mission spec — see "Accepted deviations". |

Persistence vocabulary (all in `src/Companion.Data/Migrations.cs`): tables `career`, `pinned_pack`,
`season`, `round_result_raw`, `journal`, `driver_state`, `team_state`, `player_state`,
`round_player_state`, `offer`, `staging_override`, `news_reading_state` (schema v6). Journal phase
strings are the byte-stable save vocabulary in
`src/Companion.Core/Career/CareerStreams.cs` (`JournalPhases`); DERIVED rows are byte-compared on
replay, INPUT rows are provenance-excluded.

---

## 1. Progression (XP / levels / Skill Points)

| Feature | Domain owner (Core) | Service seam | Persistence | Window / VM binding | Test coverage | Status |
|---|---|---|---|---|---|---|
| **XP award** | `XpMath.PerRound` + `CharacterProgressionV2Math.NormalizeXpAward` (exact-rational campaign scale + remainder carry) + `CharacterLevelProgression.LevelForTotalXp`, applied inside `RoundUpdate` (`src/Companion.Core/Career/RoundUpdate.cs:170-240`) | derived state read via `CharacterDossier()`; per-round via `RoundProgression(round)` | DERIVED journal row `JournalPhases.PlayerXp` (`player.xp` — carries `from/to/signedRawXp/eligibleRawXp/appliedXp`); folded `player_state` fields `Xp`, `XpScaleRemainder`, `Level` | `DossierViewModel` → `DossierView.xaml` XP bar (`XpIntoLevel` / `XpForNextLevel`, lines 273-277) | `XpMathTests`, `CharacterProgressionV2MathTests`, `CharacterProgressionV2StateTests` | SHIPPED |
| **Level-up (multi-level grouped + persisted acknowledgment)** | level derivation as above; multi-level gains grouped into one count | `CharacterDossier()` + `ReadingState()` / `MarkStoryRead` (the ack store) | `news_reading_state` rows keyed `character:levelup:<level>` (schema v6 user preference — survives replay, never a fold input) | `DossierViewModel.LevelUpPending` / `LevelsGained` / `AcknowledgeLevelUpCommand` (`DossierViewModel.cs:180-212, 397-423`) → `DossierView.xaml` `LevelUpBanner` (lines 206-222) | `LevelUpAcknowledgmentTests` (restart-survival + marker persistence), `CharacterDossierHubTests`, `DossierViewRenderTests` | SHIPPED |
| **Level cap (MAX state)** | `CharacterLevelProgression.Level300Max` (= 300); `CharacterDossier.LevelCap` / `IsAtLevelCap` (`CharacterDossier.cs:45-49`); at the cap `XpIntoLevel`/`XpForNextLevel` read 0 while `LifetimeXp` / `AvailableResetXp` keep counting | `CharacterDossier()` | same `player.xp` rows + `player_state.Xp` (banked past the cap) | `DossierViewModel.Dossier` carries the flags; the MAX badge replacing the "N / 0 XP" line is GUI-brief §2 | `CharacterLevelProgressionTests`, `CharacterLevelProgressionMidBoundaryTests`, `CharacterDossierCapAndModifiersTests` | VM READY, GUI PENDING |
| **Stat/SP spending (atomic plans, rollback)** | `CharacterSkillPlan` + `CharacterSkillPlanTransition` (ordered, dependency-checked, dual-gated costs from `MasterySkillCatalog`/`MasterySkillGraph`); reset via `CharacterSkillReset` | `PreviewSkillPlan` / `ApplySkillPlan` (one all-or-nothing INPUT), `PreviewSkillReset` / `ApplySkillReset`, legacy `SpendCharacterPoint` | atomic INPUT rows `player.skillPlan` / `player.skillReset` (provenance-excluded; the carried next-season state stays replay-checked); applied at the next season transition | `DossierViewModel` queue/confirm/reset commands (`QueueSkillNode`…`ConfirmSkillPlan`, `ConfirmSkillReset`) → `DossierView.xaml` skill graph (`ProgressionV2SkillGraphHost`, line 1001). Rollback: a failed Apply throws and changes nothing — the VM retains the local plan and surfaces the authoritative error (`DossierViewModel.cs:355-360, 379-383`) | `SkillPlanSessionTests`, `CharacterSkillPlanReplayTests`, `CharacterSkillPlanBoundaryTests`, `MasterySkillPlanTests`, `SkillResetSessionTests`, `CharacterSkillResetTests`, `CharacterSkillResetReplayTests` | SHIPPED |
| **Temporary modifiers (`DossierModifierLine`)** | `CharacterModifierResolver` (condition-gated rating/car deltas) projected by `CharacterDossier.ProjectModifierLines` into `ActiveModifiers` (`DossierModifierLine{Effect, Condition}`, `CharacterDossier.cs:188-225, 301`) | `CharacterDossier()` | none — pure projection over the folded character + rules | `DossierViewModel.Dossier.ActiveModifiers`; the "ACTIVE EFFECTS" block is GUI-brief §3 | `CharacterModifierResolverTests`, `CharacterModifierThreadingTests`, `CharacterDossierCapAndModifiersTests` | VM READY, GUI PENDING |
| **Round progression announcement** | projection of the round's journaled `player.xp` row — never recomputed (`RoundProgressionSummary`: `XpGained`, `LevelBefore/After`, grouped `LevelsGained`, `SkillPointsAvailable`) | `RoundProgression(round)` (`CareerSessionService.cs:5984`) | reads the stored `player.xp` journal row | `HomeViewModel.LastProgression`, set right after every successful Apply (`HomeViewModel.cs:797`); the post-result strip is GUI-brief §1 | `RoundProgressionTests` | VM READY, GUI PENDING |

## 2. Seasons and campaign

| Feature | Domain owner (Core) | Service seam | Persistence | Window / VM binding | Test coverage | Status |
|---|---|---|---|---|---|---|
| **Season initialization** | `CampaignProgressionPlan` (creation-pinned horizon: membership, order, rational XP scale — `PinnedCampaignSeason` carries pack id/sha/round count), SMGP state seeding | creation flows through the wizard into `CareerStore`; the open session exposes `Pack` / `Summary` | `career` + `pinned_pack` + `season` tables; creation INPUT rows `player.character`, `player.gridSelection` | `WizardViewModel` → `WizardView.xaml` | `CampaignProgressionPlanTests`, `CampaignProgressionCreationTests`, `SmgpStateSeedTests`, `MortalityWizardTests` | SHIPPED |
| **Season transition / rollover** | `SeasonEndPipeline` (offers off the final folded state), `SeasonRollover.Derive` (next-season start states; applies queued skill plans via `CharacterSkillDevelopmentTransition` in the same ordered sequence), `EraTransition` | `SeasonReview()`, `AcceptOffer`, `NextSeason()`, `StartNextSeason(teamId)` (session is spent after — reopen contract) | new `season` row + start states; journal `player.experience`, `era.bridge` / `era.departed` / `era.economy` | `SeasonReviewViewModel` → `SeasonReviewView.xaml` | `SeasonEndPipelineTests`, `EraSignAndContinueTests`, `EraTransitionDataTests`, `EraTransitionJournalTamperTests`, `CharacterSkillDevelopmentTransitionTests`, `ReplayServiceTests` | PARTIAL — logic + XAML shipped; `SeasonReviewView` has no dedicated render-harness test yet (flagged to the GUI lane, brief "Render-test note") |
| **Season 17 completion + finale** | `SmgpRules.CampaignSeasons` (= 17); unlock predicate over folded `SmgpState.Titles` / season count; `special.jpg` vs flawless `ultimate.jpg` key emitted only when earned | `SmgpFinale()` (`SmgpFinaleModel`) | none — pure display projection; titles ride `smgp.title` DERIVED rows | `SmgpFinaleViewModel`, shown once by `HomeViewModel` before the final review (`HomeViewModel.cs:852-861`) → `SmgpFinaleView.xaml` | `SmgpFinaleScreenTests`, `SmgpFinaleRenderTests`, `ProgressionNewsEventsTests.CareerCompletedFiresOnlyForACompleteCampaignFinale` | SHIPPED |
| **Campaign timeline** | timeline assembly over pinned horizon + folded seasons (17 slots for SMGP: `Locked/Current/Completed`, lore `Title`/`Era`, `PlayerPosition`, `PlayerChampion`, honest `MissedRounds`) | `CampaignTimeline()` (`ICareerSession.cs:489`; impl `CareerSessionService.cs:6025`) | reads pinned plan + stored seasons; nothing new | no ViewModel/window yet — the 17-slot campaign strip is GUI-brief §4 | `CampaignTimelineTests` | VM READY, GUI PENDING |
| **Season lore** | `SmgpSeasonLore` (`src/Companion.Core/Smgp/SmgpSeasonLore.cs`) over `data/rules/smgp/seasons.json` — all 17 ordinals authored, outcome-agnostic by authorship | `CurrentSeasonLore()`; titles also ride `CampaignTimelineEntry.Title`/`Era` | authored data file, absent-tolerant (degrades to the plain "SEASON n / 17" header) | `BriefingViewModel.SmgpSeasonTitle` / `SmgpSeasonSubtitle` / `SmgpSeasonEra` / `HasSmgpSeasonLore` (`BriefingViewModel.cs:226-238`); the header line is GUI-brief §9 | `SmgpSeasonLoreTests` (content minimums, no placeholders, outcome-agnostic guard) | VM READY, GUI PENDING |
| **SMGP canon divergence** | `SmgpCanonDivergence.Compare` (`src/Companion.Core/Smgp/SmgpCanonDivergence.cs`) — career venue winners vs the almanac's remembered rulers → `SmgpCanonDiverged`/`SmgpCanonHeld` events; `NewsroomComposer` maps both kinds to `ContentProvenance.SmgpFiction` (`NewsroomComposer.cs:242`) so SEGA canon never reads as verified history | folded into `NewsroomEvents()` by `CareerSessionService.Newsroom.cs:54` | none — pure projection; voiced through `data/rules/newsroom/038-smgp-canon.json` | unified News/History feed labels the provenance "SMGP UNIVERSE" (`NewsStoryViewModels.cs:423`) → `NewsView.xaml` / `HistoryView.xaml` | `SmgpCanonDivergenceTests` (held/diverged, unknown venues, SmgpFiction provenance, deterministic dedupe keys) + corpus-wide template validation via `NewsroomGrammarTests` | SHIPPED |
| **History entry (event spine)** | `CareerNewsEvents.Detect` — the mode-agnostic spine, including the progression kinds `LevelMilestone` (25…275), `Level300Reached`, `ReturnedFromInjury`, `CareerCompleted` (`NewsEvent.cs:94-101`, `CareerNewsEvents.cs:672-707`) | `NewsroomEvents()`, `NewsroomFeed()`, `HistoryArchive()`, `StoryThreads()` | none — deterministic re-render on read over stored results + journal + folded states (never stored) | `NewsViewModel` (+ `NewsViewModel.Unified`) / `HistoryViewModel` → `NewsView.xaml` / `HistoryView.xaml` | `CareerNewsEventsTests`, `ProgressionNewsEventsTests`, `NewsroomEventsIntegrationTests` (live-vs-replay identical), `MissionScenarioTests` | SHIPPED |

## 3. Accident / injury / mortality

| Feature | Domain owner (Core) | Service seam | Persistence | Window / VM binding | Test coverage | Status |
|---|---|---|---|---|---|---|
| **Accident** | `AccidentSeverity` (Light/Medium/Heavy input) + `AccidentFold`/`AccidentModel` (the d500 roll, severity-scaled); drawn only under the quadruple gate — mortality ≠ Off, not deceased, has character, player accident-DNF with captured severity (`ReplayService.cs:1074-1079`); stream `CareerStreams.Accident` | severity rides `ResultDraft.PlayerAccidentSeverity` into `Preview`/`Apply` | raw envelope v7 severity field + DERIVED journal row `player.accident` `{severity, roll, effectiveRoll, outcome, missRaces}` | `ResultEntryViewModel.PlayerAccidentSeverity` (revealed only for the player's own accident DNF, default Medium — `ResultEntryViewModel.cs:179, 608`) → `ResultEntryView.xaml` severity picker (lines 20-23, 448-457) | `AccidentModelTests`, `AccidentSeverityFoldTests`, `AccidentFoldDeterminismTests` | SHIPPED |
| **Injury** | `InjuryModel`; the `minorInjury` outcome sets `PlayerCareerState.RaceSuspensionRemaining` (races to sit out); `InjuryFlavor` derives the non-graphic description ("bruised ribs") deterministically from the persisted outcome — never a reroll | `PlayerMortality()` (`RaceSuspensionRemaining`, `SeasonEndingInjury`), `CurrentSitOut()` | `player.accident` outcome token + folded player state (`round_player_state`) | `HomeViewModel` routes to the sit-out step (`HomeViewModel.cs:117-120, 354-356`); dossier `AvailabilityLabel` (`DossierView.xaml:332`) | `InjuryModelTests`, `SitOutRoutingTests`, `MortalityScreensRenderTests` | SHIPPED |
| **Recovery / healing** | auto-simulating the sat-out round heals one race of a minor suspension; AI field generated deterministically per seat off `CareerStreams.AutoRace` (`AutoRaceModel`) | `AutoSimulateRound()` (throws for a fit/deceased player), `CurrentSitOut()` | DERIVED journal row `player.dns` `{round, reason, suspensionRemaining}`; decremented counter in the folded player state | `SitOutViewModel` (single Continue) → `SitOutView.xaml`; the comeback is narrated by the `ReturnedFromInjury` event | `AutoSimFoldTests`, `SitOutRoutingTests`, `ProgressionNewsEventsTests.TheComebackRoundCarriesTheConsecutiveDnsCount_AndEachAbsenceIsItsOwnStory` | SHIPPED |
| **Medical clearance (availability gates)** | `CharacterDossier.Availability`/`AvailabilityLabel` with the same precedence as `PlayerMortalityStatus.IsFit`: deceased > season-ending > suspension > fit (`CharacterDossier.cs:146-153`) | `PlayerMortality()`; **hard service-layer gates** — `Preview` (`CareerSessionService.cs:4121-4124`) and `Apply` (`:4245-4248`) throw for an injured player, so no caller can manually score an unfit driver or stall the healing countdown | reads folded player state | `DossierView.xaml:332` Availability line; the hub routes injured rounds to `SitOutView` before result entry is reachable | `InjuryAvailabilityGateTests` (every-caller contract), `MortalityModeTests` | SHIPPED |
| **Replacement driver** | none — deliberately not modeled. The injured player's round is auto-simulated with the player DNS (OPI-neutral, zero points); no substitute takes the seat | `AutoSimulateRound()` | `player.dns` row (the absence is the record) | `SitOutView.xaml` states the round is auto-simulated | `AutoSimFoldTests` (player-DNS neutrality) | DEVIATION — see below |
| **Career-ending injury** | the fold's outcome ladder is `none / minorInjury / seasonEnding / death` — `seasonEnding` ends the season (driver returns next year); there is deliberately no permanent-invalidity outcome short of death | `PlayerMortality().SeasonEndingInjury`; season-ending rounds auto-simulate until rollover | `player.accident` outcome `"seasonEnding"`; `SeasonEndingInjury` in folded state | `SitOutViewModel.SeasonEnding` ("SEASON OVER — recovering") → `SitOutView.xaml` | `AccidentModelTests`, `SitOutRoutingTests` | DEVIATION — see below |
| **Fatality** | the `death` outcome of the same d500 fold sets `PlayerCareerState.Deceased` — terminal in every mode | `PlayerMortality().Deceased`, `DeathScreen()`; `Preview`/`Apply` refuse a deceased driver (`CareerSessionService.cs:4109-4111, 4231-4233`) | `player.accident` outcome `"death"`; Normal-mode save slots (`SaveSlotStore` file snapshots) stay restorable to un-do it | `HomeViewModel.CareerOver` / `DeathScreen` → `DeathScreenView.xaml` (obituary, career record, fatal accident, restorable slots in Normal) | `DeathScreenModelTests`, `AccidentFoldDeterminismTests`, `MortalityScreensRenderTests` | SHIPPED |
| **Game over (death screen, Hardcore deletion, archive)** | mortality contract per `docs/dev/character-death-injury.md` §2/§6; `MortalityMode {Off, Normal, Hardcore}` (`MortalityMode.cs`), immutable per career | Hardcore path in `CareerSessionService.Apply` (`:4292-4303`): on a genuine alive→dead transition it captures `DeathScreenModel` from the intact DB **first**, disposes the DB, `SaveSlotStore.DeleteCareerAndAllSaves`, marks the session spent (`_careerFileDeleted`) — never on replay. Normal death: `SaveSlots()`/`RestoreSlot()` | `career.mortality_mode` column (`Migrations.cs:155`) mirrored into the start player state; Hardcore deletion is the app's one destructive file op | `DeathScreenView.xaml` (DB-free render from the captured model), `SmgpCareerOverView.xaml` (the SMGP Level-D floor's separate terminal screen); a Normal-mode dead career stays open as a read-only archive | `DeathScreenHandoffTests` (render-after-delete contract), `PostDeathArchiveTests`, `MortalityModeTests`, `SaveSlotStoreTests` | SHIPPED |
| **Injury history / medical record** | `InjuryHistoryEntry` projection of the persisted accident rows, oldest first — history never rewrites what the fold decided; `Description` from `InjuryFlavor` | `InjuryHistory()` | reads `player.accident` journal rows | `DossierViewModel.InjuryHistory` / `HasInjuryHistory` + `MortalityLabel` (`DossierViewModel.cs:216-227`); the medical-record block is GUI-brief §7 | `InjuryAvailabilityGateTests` (projection asserted alongside the gates) | VM READY, GUI PENDING |
| **Calendar availability** | `SchedulePlayerStatus {Upcoming, Raced, SatOut, WillMiss}` on `SeasonScheduleEntry` (`ICareerSession.cs:583-600`) — a stored absence is never rewritten as participation | `SeasonSchedule()` | reads stored envelopes (`PlayerDidNotStart`) + active injury | `CalendarRoundViewModel.PlayerStatus` / `PlayerStatusLabel` ("SAT OUT — injured" / "WILL MISS — injured", `CalendarViewModel.cs:97-138`); the amber chip is GUI-brief §5 | `CalendarPlayerStatusTests` | VM READY, GUI PENDING |
| **Recent-career terminal badges** | — (app-shell concern, no Core owner) | `IRecentCareersStore.Touch(...)` badge-aware overload records `"deceased"` / `"careerOver"` at open/create; a plain Continue never un-badges | `recent.json` (app-level MRU store, outside the career DB) | `RecentCareer.IsTerminal` / `TerminalBadge` ("IN MEMORIAM" / "CAREER OVER", `RecentCareers.cs:35-51`); the gallery badge is GUI-brief §10 — the card stays openable (the archive is viewable by design) | `RecentCareersTerminalStateTests` | VM READY, GUI PENDING |

---

## Accepted deviations

The SMGP-300 mission spec is an external requirements document; where the repo does something
different it is a decided position, not an omission. The design docs cited carry the reasoning.

### 1. Replacement driver → player-DNS auto-simulation

The mission asks for a replacement driver to fill the injured player's seat. The repo instead
**auto-simulates the round with the player DNS** — OPI-neutral, zero points, journaled as
`player.dns` (`ICareerSession.AutoSimulateRound`, `docs/dev/character-death-injury.md` §5).
Rationale: AMS2 cannot spectate a single-player custom race, so a round the player cannot drive
cannot be *driven by anyone* — it can only be simulated. A substitute driver would also pollute the
faithful historical grid (a locked direction: never synthesize entrants) and complicate the
byte-identical fold for zero playable surface. The absence is honestly recorded: the calendar shows
`SatOut`, the timeline sets `MissedRounds`, and the newsroom voices the comeback
(`ReturnedFromInjury`).

### 2. Career-ending injury → the none/minor/seasonEnding/death ladder

The mission's injury taxonomy includes a distinct permanently career-ending (non-fatal) outcome.
The repo's accident fold deliberately resolves to exactly four outcomes —
**none / minorInjury / seasonEnding / death** (`player.accident` outcome tokens,
`docs/dev/character-death-injury.md` §3, the mechanic as specified by the product owner: "injury —
sit out the next race or more; heavy — season-ending, or worse: death"). A season-ending injury
returns the driver next season; permanent non-fatal retirement is not modeled. Rationale: a
permanently disabled-but-alive character is a dead career with extra steps — the terminal states
that exist (death; the SMGP Level-D `CareerOver` floor) already carry the full game-over
presentation, and a fifth outcome band would dilute the d500 odds table that was tuned and reviewed
as shipped (`AccidentModelTests` pins the bands).

### 3. GUI-pending rows are lane sequencing, not scope cuts

Seven rows above are **VM READY, GUI PENDING** (level-cap badge, active modifiers, round
progression strip, campaign timeline, calendar chips, medical record, terminal badges, season-lore
header). This is the repo's standing two-lane delivery contract
(`docs/PROJECT.md` §0, `docs/dev/codex-head-of-gui.md`): the CODE lane ships the tested bind
contract first; the GUI lane owns the XAML in `src/Companion.App/**` and binds it from
`docs/dev/codex-gui-smgp300-brief.md`. Every pending row's contract is published, tested, and
frozen — the XAML binds real names.

### 4. Known coverage gap (not a behavior gap)

`SmgpCanonDivergence.Compare` ships and flows into the unified feed with `SmgpFiction` provenance,
but has no dedicated unit-test class; its templates are validated corpus-wide by
`NewsroomGrammarTests`. Flagged for the test backlog rather than hidden behind a SHIPPED stamp.
