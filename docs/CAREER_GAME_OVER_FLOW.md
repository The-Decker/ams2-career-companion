# Career game-over flow — fatality & terminal states

> How a career ENDS. Two terminal states exist, each with its own trigger, screen, and persistence
> contract: a **fatal accident** (the mortality system, `docs/dev/character-death-injury.md`) and the
> **SMGP LEVEL-D floor knock-out** (`SmgpState.CareerOver`). Both are folded state — they survive a
> force-close, reopen onto the same terminal screen, and refuse further rounds at the service layer.
> Everything here documents shipped behavior; every claim cites the code or test that carries it.

---

## 1. The two terminal states

| | Fatal accident | SMGP floor knock-out |
|---|---|---|
| Trigger | `AccidentFold.Apply` resolves a d500 roll to `AccidentOutcomeKind.Death` → `PlayerCareerState.Deceased = true` (`src/Companion.Core/Career/AccidentFold.cs`) | 4th rival-battle loss while seated in a LEVEL D team → `SmgpState.CareerOver = true` (`src/Companion.Core/Smgp/SmgpBattleFold.cs:67-72`, `SmgpRules.FloorLossLimit = 4`) |
| Scope | Any career with `MortalityMode` Normal/Hardcore + a character | SMGP careers only |
| Screen | `DeathScreenView` bound to `HomeViewModel.DeathScreen` (a `DeathScreenModel`) | `SmgpCareerOverView` bound through `BriefingViewModel.SmgpCareerOver` |
| File afterwards | Normal: kept (viewable archive). Hardcore: physically deleted | Kept (viewable archive) |
| Undo | Normal: restore a save slot. Hardcore: none, ever | None |
| Gallery badge | `"deceased"` → **IN MEMORIAM** | `"careerOver"` → **CAREER OVER** |

Navigation treats them uniformly: `HomeViewModel.IsCareerTerminal => CareerOver is not null ||
Briefing.SmgpCareerOver` (`src/Companion.ViewModels/Shell/HomeViewModel.cs:268`) — one additive
predicate over the two purpose-built projections; each ending keeps its own view.

---

## 2. Eligibility — who can die at all

The accident fold runs under a **quadruple gate** in the round fold
(`src/Companion.Data/ReplayService.cs:1079-1083`):

1. `player.Mortality != MortalityMode.Off` — the career opted into mortality at creation;
2. `!player.Deceased` — not already dead;
3. a character is present (`character is { }` + `inputs.CharacterRules is not null`) — durability
   and injury perks must exist to compute the safety offset;
4. `envelope.PlayerAccidentSeverity is { }` — this round's raw result marked the player's own DNF
   as an **accident** with a captured severity (envelope v7 field; a mechanical DNF or a finish
   never rolls).

A round failing any gate draws **zero** from the `accident` RNG stream and emits no row, so an
Off / character-free / non-accident career stays byte-identical to a pre-feature save — this gating
is what makes `Off` replay-safe (see Accepted deviations).

Within an eligible roll, severity bounds the worst case
(`AccidentModel.DefaultRules`, `src/Companion.Core/Character/AccidentModel.cs:74-103`):

| Severity | Bands (effective d500, 1 unit = 0.2%) | Can kill? |
|---|---|---|
| **Light** | none ≤490 · minorInjury(1 race) ≤500 | **No — Light has no death band and no season-ending band.** Worst case is one missed race, even for a maximally reckless build (the clamp can only pile overflow onto the last band, which is a 1-race injury). |
| **Medium** | none ≤410 · minor(1) ≤470 · minor(2) ≤490 · seasonEnding ≤497 · **death ≤500** | Yes (0.6% baseline) |
| **Heavy** | none ≤250 · minor(1) ≤380 · minor(2) ≤450 · seasonEnding ≤485 · **death ≤500** | Yes (3% baseline) |

The roll is one draw from a fresh keyed stream (`CareerStreams.Accident`, keyed
`(accident, year, round, "player")` — `AccidentFold.Apply`), shifted by a deterministic integer
`AccidentModel.SafetyOffset` (durability + injury perks; never a second draw), clamped to `[1,500]`,
then bucketed. Bands are tunable data (`data/rules/perks.json` accident block; `DefaultRules` is the
fallback). Determinism: `AccidentFoldDeterminismTests` (`MortalityCareer_SurvivesAnAccident_EmitsDerivedRow_AndReplaysByteIdentically`,
`OffCareerWithCharacter_Accident_DrawsNothing_AndReplaysByteIdentically`).

---

## 3. The fatal transaction — exact order

`CareerSessionService.Apply(ResultDraft)` (`src/Companion.ViewModels/Services/CareerSessionService.cs:4224-4309`):

1. **Refusal gates first.** Apply (and `Preview`, same gates at `:4105-4124`) throws
   `InvalidOperationException` if the session is spent (`_careerFileDeleted`), the driver is already
   `Deceased`, `Smgp.CareerOver` is set, the season is complete, or the driver is injured (injured
   rounds only fold through `AutoSimulateRound`). Terminal states are enforced at the **service
   layer** — no UI path can score a dead driver.
2. **One atomic unit: `ReplayService.ImportAndFoldRound`** (`:4275`). The raw-result envelope
   (carrying `PlayerAccidentSeverity`) is stored and the round folds in the same transaction — a
   stored raw result can never exist without its fold. Inside that fold, `AccidentFold.Apply`:
   - rolls the d500 and resolves the outcome;
   - on death sets `Deceased = true` on the player state;
   - emits the byte-compared `player.accident` journal row
     (`{severity, roll, effectiveRoll, outcome, missRaces}`, cause `accident-death`);
   - emits the `news.headline` row ("Tragedy: {name} killed in a racing accident" —
     `AccidentFold.Headline`). The newsroom later derives a `NewsEventKind.PlayerDied` event from
     the journal (`src/Companion.Core/Newsroom/CareerNewsEvents.cs:417`; editorial weight 100, the
     maximum — `EditorialSelection.cs:48`).
   The result-provenance journal row is appended inside the same transaction (`withTransaction`),
   so a crash can never leave a folded round with vanished provenance.
3. **Death-transition detection** (`:4292`): `justDied = beforePlayer?.Deceased != true &&
   CurrentPlayerState()?.Deceased == true` — a genuine alive→dead transition **this** round, never
   a re-read of an old death, and never on replay (replay runs `ReplayService` directly, not this
   path).
4. **Hardcore only** (`:4293-4303`), strictly in this order:
   1. capture the full `DeathScreenModel` from the **still-intact** DB (`BuildDeathScreen`);
   2. `_database.Dispose()` — releases the file (clears the SQLite pool);
   3. `SaveSlotStore.DeleteCareerAndAllSaves(CareerFilePath)` — deletes the career file, its
      WAL/SHM siblings, and every snapshot (`src/Companion.Data/SaveSlotStore.cs:250-254`). The one
      destructive file op in the app, gated on Hardcore + a real transition;
   4. `_careerFileDeleted = true`, then `return` — the season-end pipeline must not run against a
      deleted file.
5. **Normal**: the file is kept. `EnsureSeasonEnd` is skipped when `justDied` even if the fatal
   round was the season's last (`:4307`) — a dead driver banks no title and rolls no offers; the
   death screen offers a restore instead.

After Apply returns, `HomeViewModel.ApplyDraft` (`src/Companion.ViewModels/Shell/HomeViewModel.cs:759-791`)
reads the **DB-free** `PlayerMortality()` — never `Summary`/`Briefing`, which query the DB that a
Hardcore death has already disposed — and on `Deceased || CareerFileDeleted` sets `CareerOver` +
`DeathScreen` and returns. The shell takes over with the death screen; the ordinary hub content
stays inert underneath.

---

## 4. Normal vs Hardcore — a save-policy axis, not a lethality axis

`MortalityMode { Off, Normal, Hardcore }` is seeded once at creation
(`career.mortality_mode` column + mirrored into the start `PlayerCareerState` —
`CareerSessionService.CreateCareer` `:295-299`, `SeedStartStates`) and is **immutable per career**.
It is visible mid-career on the Driver tab: `DossierViewModel.MortalityLabel`
(`src/Companion.ViewModels/Hub/DossierViewModel.cs:216-221`, "MORTALITY: NORMAL" etc.).

The d500 odds are **identical** in Normal and Hardcore. What differs is the save policy:

| | Off | Normal | Hardcore |
|---|---|---|---|
| Accident roll | never (gate 1) | yes | yes |
| Save slots (`SavesEnabled`) | no | yes — manual slots + season-start autosave (`TryAutosaveSeasonStart`, `:6253`) | no — no slots, no restore, ever |
| On death | n/a | file kept; death screen offers restore; reopenable archive | `DeathScreenModel` captured pre-deletion; file + all snapshots physically deleted |
| Undo a death | n/a | restore any slot (`RestoreSlot`, `:6223`) | impossible |

`SavesEnabled => _mortality == MortalityMode.Normal` (`:5845`). Normal careers autosave the fresh
season's start (best-effort, once per season, `autosave-season-N`) so a death always has a recent
restore point.

---

## 5. The death screen

`DeathScreenModel` (`src/Companion.ViewModels/Services/DeathScreenModel.cs`) — a pure projection,
composed by `DeathScreenModel.Build` from plain values (unit-testable, DB-free by construction):

- **Cause of death** — one line from the fatal `player.accident` row's severity + the round's venue:
  "Killed in a heavy accident at Monaco (round 6)." (`BuildDeathScreen` reads the last
  death-outcome `player.accident` row back off the journal, `CareerSessionService.cs:5892-5919`;
  venue = the pack round's `Track.RealVenue`, else the round name.)
- **Obituary** — name, age at death (created age + seasons elapsed), the accident, and one
  respectful sentence tallying the record (`SummariseCareer`: seasons/wins/podiums/titles, or "A
  career ended before it truly began." for a round-1 death).
- **Record** — the whole `CareerRecordsBook` + per-season `CareerSeasonCard` recaps from
  `CareerTimeline()`.
- **Restore offer (Normal only)** — `RestoreSlots` (the career's save slots);
  `CanRestore => Mode == Normal && RestoreSlots.Count > 0`. `DeathScreenView.xaml` renders a
  "Restore this save" button per slot (`:224-227`); the confirmed restore swaps the snapshot over
  the working file, **spends the session**, and reopens the career cleanly
  (`SaveManagerWindow.ConfirmAndRestore` → `session.RestoreSlot` → dispose → `Start.OpenCareerCommand`).
- **Permadeath (Hardcore)** — `IsPermadeath => Mode == Hardcore`; the restore panel collapses and a
  final message renders instead (`DeathScreenView.xaml:235`).

**There is no Continue.** The only actions are restore (Normal) and "back to start"
(`GoToStartCommand`, `DeathScreenView.xaml:14`). Render coverage:
`MortalityScreensRenderTests.DeathScreenView_NormalDeath_ShowsRestoreAndRecap` and
`DeathScreenView_HardcoreDeath_IsFinalAndNeverOffersRestore`.

On a **Hardcore** death the model the screen binds was captured before the file was deleted —
`DeathScreen()` returns the memoised snapshot with no DB access (`:5873-5886`), and
`PlayerMortality()` short-circuits on `_careerFileDeleted` (`:5848-5859`). The shell must never
touch the DB after a permadeath; `DeathScreenHandoffTests.HardcoreDeath_RoutesToCareerOver_WithoutTouchingTheDisposedDb`
enforces it with a throwing fake.

---

## 6. Force-close / reload behavior

`Deceased` is **folded state** — it persists in the journal/state like every other fold output, so
killing the app changes nothing:

- **Reopen routes to the terminal screen.** The `HomeViewModel` constructor
  (`HomeViewModel.cs:96-106`) checks `session.PlayerMortality()` before anything else; on
  `Deceased || CareerFileDeleted` it sets `_careerOver` + `_deathScreen = session.DeathScreen()` and
  lands on the same terminal takeover as the live fatal-round handoff. The SMGP floor routes the
  same way through `Briefing.SmgpCareerOver`
  (`DeathScreenHandoffTests.ReopenedNormalDeath_RoutesExistingTerminalBinds_AndDisablesRoundCommands`,
  `ReopenedSmgpFloor_UsesExistingBriefingBind_AndDisablesRoundCommands`).
- **Hardcore cannot reopen** — the file is gone; the recents gallery prunes entries whose file no
  longer exists (`IRecentCareersStore.Load`).
- **Memorial badges.** On open/create the shell records the observed terminal state
  (`ShellViewModel.TerminalState`, `:106-112`: `"deceased"` / `"careerOver"` / null) into
  `RecentCareer.TerminalState`; the gallery card badges **IN MEMORIAM** / **CAREER OVER**
  (`RecentCareers.cs:46-51`) instead of presenting a finished career as playable. A plain re-open
  never un-badges (null carries the stored state forward — `IRecentCareersStore.Touch`).

---

## 7. The post-death archive guarantees (Normal)

A Normal death is terminal but **never destructive**. `PostDeathArchiveTests`
(`tests/Companion.Tests/Data/PostDeathArchiveTests.cs`) locks the contract in one end-to-end test:

1. the career file survives the death and **reopens** with `Deceased == true`, mode Normal;
2. the **newsroom stays readable** — events include `NewsEventKind.PlayerDied`, and the composed
   feed carries the death story;
3. the **career timeline (scrapbook) stays readable**;
4. `Apply` and `Preview` **stay refused** (`InvalidOperationException`, "died");
5. a **brand-new career remains creatable and playable** afterwards — a death never poisons the app.

---

## 8. The SMGP floor knock-out — the second game-over

The SMGP replica's own terminal, independent of mortality (an SMGP career may carry both):

- **Rule:** every rival battle **lost while seated in a LEVEL D team** increments
  `SmgpState.FloorLosses`; at `SmgpRules.FloorLossLimit` (**4**) the fold sets
  `SmgpState.CareerOver = true` (`SmgpBattleFold.cs:67-72`) — kicked out of F1 SMGP. Promoting out
  of D resets the count to 0 (`:75-76`); it also resets between seasons (`SmgpState.FloorLosses`
  doc). It is the mode's one hard-fail state, never revived (locked direction — SMGP roadmap).
- **Hard stop:** `Apply`/`Preview` refuse on `Smgp.CareerOver` (`CareerSessionService.cs:4237-4239`)
  and the season-end rollover/battle re-fold are suppressed — a floored career takes no more rounds.
- **Routing:** projected as `BriefingViewModel.SmgpCareerOver` (`:266`), folded into
  `HomeViewModel.IsCareerTerminal`; the hub is replaced by the fired-ending screen
  (`MortalityScreensRenderTests.SmgpCareerOverView_RendersTheFiredEnding`,
  `HubView_SmgpFloor_ReplacesTheLiveHubWithTheUnifiedEnding`; a live SMGP career collapses it —
  `HubView_LiveSmgpBriefing_ShowsLiveHubAndCollapsesCareerOver`).
- **Persistence:** `CareerOver` is part of the byte-compared SMGP fold state
  (`SmgpState.Equals`/`GetHashCode` include it) — replay-deterministic
  (`SmgpBattleFoldDeterminismTests`), survives reload, badges the gallery **CAREER OVER**.
- The file is never deleted (the floor is not a death); the archive stays viewable exactly like a
  Normal-mode death.

---

## 9. Test map

| Concern | Test |
|---|---|
| Roll determinism, derived row, Off-gate byte-identity | `tests/Companion.Tests/Data/AccidentFoldDeterminismTests.cs` (incl. `NormalAccidentDeath_SetsDeceased_KeepsTheFile_RefusesRounds_AndReplaysByteIdentically`, `HardcoreAccidentDeath_PhysicallyDeletesTheCareerFileAndSaves`) |
| DB-free handoff + reopen routing (both terminals) | `tests/Companion.Tests/ViewModels/DeathScreenHandoffTests.cs` |
| Post-death archive contract | `tests/Companion.Tests/Data/PostDeathArchiveTests.cs` |
| Terminal screens render (sit-out, death Normal/Hardcore, SMGP fired) | `tests/Companion.RenderHarness.Tests/MortalityScreensRenderTests.cs` |
| Floor knock-out fold determinism | `tests/Companion.Tests/Data/SmgpBattleFoldDeterminismTests.cs` |

---

## Accepted deviations

Where the SMGP-300 mission spec asked for something the repo deliberately does differently. The
decided positions live in `docs/dev/character-death-injury.md` (§2, §3.4, §9) — resolved with Mike
2026-07-12.

1. **Risk settings are `MortalityMode { Off, Normal, Hardcore }` — there is no Reduced/Authentic
   fatality-rate tier.** The spec's graded risk levels map onto a single opt-in axis: `Off` = no
   fatalities, `Normal`/`Hardcore` share one tunable band table (`data/rules/perks.json` accident
   block / `AccidentModel.DefaultRules`) and differ only in save policy. Rationale: the mode is a
   **replay gate**, not a rate multiplier — a rate tier would be a third fold input threaded through
   every band, for a distinction the severity picker + safety offset already express.
2. **`Off` skips the accident stream entirely — there is no fatal-to-nonfatal conversion.** The
   spec's "convert fatal outcomes to non-fatal in safe mode" would require *drawing* the roll and
   rewriting its outcome; here an Off career draws **zero** from `CareerStreams.Accident`
   (`ReplayService.cs:1079`), which is load-bearing for replay: a pre-feature save and an Off career
   are byte-identical, and no other stream shifts (`docs/PROJECT.md` §3.2 "Gating is what preserves
   old saves"; verified by `OffCareerWithCharacter_Accident_DrawsNothing_AndReplaysByteIdentically`).
3. **The mode is immutable per career.** No mid-career risk toggle: it is seeded once at creation
   into the `career` table and the start fold state (`character-death-injury.md` §2), because
   changing it would change which rounds draw and break byte-identical re-simulation. It is now
   *visible* mid-career (`DossierViewModel.MortalityLabel`) — readable, never writable.
4. **Death odds are per-severity data bands, not a global fatality percentage** — and **Light can
   never kill** (`AccidentModel.DefaultRules.Light` has no death band; decision B, "light crashes
   are mostly harmless"). The spec's flat fatality chance became a severity-shaped d500 table so a
   driver's build (durability + injury perks via `SafetyOffset`) matters, with the clamp
   deliberately amplifying risk only on Medium/Heavy.
5. **No spectator/ghost continuation after death.** Death is terminal by design (Mike, §0: "Death is
   terminal"); the archive (news, history, timeline) is the epilogue, and Normal's file-level
   restore — not an in-fiction revival — is the only way back (§4: reload is a snapshot restore,
   never a journal edit, because the journal is append-only).
