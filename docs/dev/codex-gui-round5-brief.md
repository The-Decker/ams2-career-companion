# Codex GUI round 5 — the character death & injury SCREENS

Written 2026-07-12, after the whole Claude death/injury backend shipped (`89ed505`): Slices 1–6 are
live and tested. This round renders the SCREENS. **Everything below is ALREADY built + tested + green
(2122 logic + 67 render-harness) — you BIND it, you do NOT change it.** If you need a member that
doesn't exist, flag it (it's Claude's lane). Grounded in `docs/dev/character-death-injury.md` §2/§4/§5/§6.

**YOUR LANE = `src/Companion.App/**` ONLY** (XAML / Views / Themes / Converters). Build in the tactile
theme — NO inline hex, use theme brushes. Reuse the SMGP finale full-immersion pattern (`SmgpFinaleView`)
for the big screens. Branch `codex/gui-round5` off the current `hub/increment-4` tip and merge back.

> Note: `Companion.App.csproj` gained one line this round (Claude) — it now copies `data/rules/smgp/*.json`
> to the app output (it was previously dist-only). Non-colliding with your Views/Themes work; just take the tip.

---

## 1. Wizard — the mortality choice (`WizardView.xaml`)
Bind a 3-way radio / segmented control to `NewCareerWizardViewModel.MortalityMode`
(enum `Companion.Core.Career.MortalityMode {Off, Normal, Hardcore}`, default `Off`) over its
`MortalityOptions` list. Below it show the honest `MortalityModeSummary` string — it already spells out
the Hardcore permadeath reality. **⚠ Hardcore MUST read as dangerous/unmistakable** (amber/red accent +
the warning) — Mike's §2 requirement.

## 2. Normal save / reload panel
Gate the whole surface on `ICareerSession.SavesEnabled` (true = Normal → show it; Off/Hardcore → hide
entirely). A Start-gallery card action AND an in-session button.
- `SaveSlots()` → `IReadOnlyList<Companion.Data.SaveSlotInfo> {SlotId, Label, SeasonYear, Round, CreatedUtc, IsAutosave}`, newest-first — autosaves get a distinct chip via `IsAutosave`.
- "Save" → `SaveToSlot(label)` (prompt for a label). "Restore" → `RestoreSlot(slotId)`. "Delete" → `DeleteSlot(slotId)`.
- **⚠ After `RestoreSlot` the session's DB is CLOSED — the shell must REOPEN the career file** (same contract as an era transition / the ShellViewModel reopen path). Restore reverts the whole career, incl. un-doing a death.

## 3. Result-screen accident severity picker (`ResultEntryView.xaml`)
When `ResultEntryViewModel.PlayerHasAccidentDnf` is true (the player marked their OWN retirement reason
as accident), REVEAL a Light/Medium/Heavy picker bound to `ResultEntryViewModel.PlayerAccidentSeverity`
(enum `Companion.Core.Career.AccidentSeverity`, defaults `Medium` the moment an accident is marked).
Hidden otherwise. The existing free-text DNF detail still rides alongside.

## 4. The injured SIT-OUT screen
The shell ALREADY routes to it: `HomeViewModel.IsSitOutStep` true ⇒ `CurrentContent` is a `SitOutViewModel`
(the injured player is never shown manual result entry). Style it as a full-immersion card:
- `SitOutViewModel.Status` → `SitOutStatus {Headline, RaceSuspensionRemaining, SeasonEnding}` —
  e.g. `"INJURED — auto-simulating round (2 remaining)"` / `"SEASON OVER — recovering"`.
- A single Continue bound to `SitOutViewModel.ContinueCommand` (folds the auto-simulated round + advances;
  chains through consecutive injured rounds one Continue at a time). AMS2 can't spectate a single-player
  race — that's WHY the round is auto-simulated.

## 5. The DEATH / PERMADEATH screen  ← the rich model has LANDED
The shell ALREADY routes: after a round, when a fatal accident ends the career,
`HomeViewModel.CareerOver` (`PlayerMortalityStatus? {Mode, Deceased, SeasonEndingInjury,
RaceSuspensionRemaining, CareerFileDeleted, IsFit}`) is NON-NULL, and **`HomeViewModel.DeathScreen`
(`DeathScreenModel?`) is set alongside it** — bind the rich model for the content, `CareerOver` for the
bare gate/flags. Add a full-immersion death screen (a new `Is*Step` / a converter on `CareerOver`):

Bind from `HomeViewModel.DeathScreen` (`Companion.ViewModels.Services.DeathScreenModel`):
- `DriverName`, `Age` (nullable), `CauseOfDeath` (e.g. "Killed in a heavy accident at Monaco (round 6)."),
  `Obituary` (an in-world paragraph), `Venue`, `Round`, `Severity`.
- `Record` (`CareerRecordsBook {BestFinish, Wins, Podiums, TotalPoints, Championships, SeasonsRaced,
  LongestWinStreak, LongestPodiumStreak}`) and `Seasons` (`IReadOnlyList<CareerSeasonCard>`) — the career recap.
- `IsPermadeath` (Hardcore — no restore, ever) vs `CanRestore` (Normal + at least one save).
- `RestoreSlots` (`IReadOnlyList<SaveSlotInfo>`) — for the Normal "Restore last save" affordance
  (`RestoreSlot(slotId)`, then the shell reopen path from §2).

Two modes:
- **NORMAL death** (`CanRestore` / `!IsPermadeath`): an in-world "career over" screen that OFFERS RESTORE from `RestoreSlots`.
- **HARDCORE death** (`IsPermadeath` / `CareerOver.CareerFileDeleted`): the FINAL permadeath screen — the
  career + all saves are already physically DELETED; NO restore.
  **⚠⚠ The session's DB is DISPOSED here — bind ONLY `CareerOver` + `DeathScreen` (both DB-free by design,
  captured before the file was deleted). NEVER read `Summary` or any other session member on this screen.**

## 6. Dossier availability line (`DossierView.xaml`)
Bind `CharacterDossier.AvailabilityLabel` (string: "Fit" / "Injured — out 2 races" / "Season over —
recovering" / "Deceased") beside the existing `InjuryRisk`, and use `CharacterDossier.Availability`
(enum `AvailabilityStatus {Fit, Injured, SeasonOver, Deceased}`) for the accent/colour. `DataContext`
is already the dossier; mirror the `InjuryRisk` null-collapse `DataTrigger` pattern if you want to hide it
when Fit.

---

**Test:** `dotnet build` + the render-harness suite green (it validates every binding). Merge onto the
current `hub/increment-4` tip.
