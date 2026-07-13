# Continue prompts — round 4 (character death & injury: the screens + the living world)

Written 2026-07-12 after death/injury Slices 1–4 shipped (`441a700`), Codex GUI round 4 merged
(`de4657d`), the RC redeployed, and the full SMGP art collection promoted to the tracked tree
(`c1794d3`). One Codex prompt (the death/injury SCREENS) + one Claude prompt (the Slice-5 backend
projections + the Slice-6 living-world dispatch + tuning). Grounded in `docs/dev/character-death-injury.md`.

**Lane reminder:** Claude = Core/ViewModels/Data + tests (owns the SMGP/character backend). Codex =
`src/Companion.App/**` (XAML/Views/Themes/Converters) + the 1967 era. Every VM/session/Core member the
Codex prompt binds is ALREADY built, tested, and shipping in the RC — Codex binds it, never changes it.

---

## CODEX — GUI round 5: the character death & injury screens

```
Continue the AMS2 Career Companion in your own worktree (branch codex/gui-round5 off hub/increment-4).
Read docs/dev/character-death-injury.md (§2, §4, §5, §6) + the auto-memory TOP block first. YOUR LANE =
src/Companion.App/** ONLY (XAML/Views/Themes/Converters). Every VM/session/Core member below is ALREADY
built + tested + shipping in the RC — you BIND it, you do NOT change it (if you need one that doesn't
exist, flag it — it's Claude's lane). Character death/injury Slices 1-4 (backend + the shell routing) are
live; this round renders the SCREENS. Build in the tactile theme — NO inline hex, use theme brushes
(the theme contract); reuse the SMGP finale full-immersion pattern (SmgpFinaleView) for the big screens.

1. WIZARD MORTALITY CHOICE (WizardView.xaml). Bind a 3-way radio / segmented control to
   NewCareerWizardViewModel.MortalityMode (enum Companion.Core.Career.MortalityMode {Off,Normal,Hardcore},
   default Off) over its MortalityOptions list. Below it, show the honest MortalityModeSummary string — it
   already spells out the Hardcore permadeath reality ("death PERMANENTLY DELETES this career file"). ⚠
   Hardcore MUST read as dangerous and unmistakable (amber/red accent + the warning) — Mike's §2 requirement.

2. NORMAL SAVE/RELOAD PANEL. A save/load surface (a Start-gallery card action AND an in-session button).
   Gate on ICareerSession.SavesEnabled (true = Normal only → show it; Off/Hardcore → hide entirely). Bind
   SaveSlots() -> IReadOnlyList<Companion.Data.SaveSlotInfo> {SlotId, Label, SeasonYear, Round, CreatedUtc,
   IsAutosave} newest-first (autosaves get a distinct chip via IsAutosave); a "Save" button -> SaveToSlot(label)
   (prompt for a label); "Restore" -> RestoreSlot(slotId); "Delete" -> DeleteSlot(slotId). ⚠ After RestoreSlot
   the session's DB is closed — the shell must REOPEN the career file (same contract as an era transition;
   the ShellViewModel reopen path). Restore reverts the whole career (incl. un-doing a death).

3. RESULT-SCREEN ACCIDENT SEVERITY PICKER (ResultEntryView.xaml). When ResultEntryViewModel.PlayerHasAccidentDnf
   is true (the player marked their OWN retirement reason as accident), REVEAL a Light/Medium/Heavy picker
   bound to PlayerAccidentSeverity (enum Companion.Core.Career.AccidentSeverity, defaults Medium the moment an
   accident is marked). Hidden otherwise. The existing free-text DNF detail still rides alongside.

4. THE INJURED SIT-OUT SCREEN. The shell ALREADY routes to it (HomeViewModel.IsSitOutStep true =>
   CurrentContent is SitOutViewModel; the injured player is never shown manual result entry). Style the
   screen: bind SitOutViewModel.Status (SitOutStatus {Headline, RaceSuspensionRemaining, SeasonEnding}) as a
   full-immersion "INJURED — auto-simulating round (N remaining)" / "SEASON OVER — recovering" card, with a
   single Continue bound to SitOutViewModel.ContinueCommand (folds the auto-simulated round + advances; it
   chains through consecutive injured rounds one Continue at a time). AMS2 can't spectate a single-player
   race — that's WHY the round is auto-simulated.

5. THE DEATH / PERMADEATH SCREEN. The shell ALREADY routes: after a round, HomeViewModel.CareerOver (a
   PlayerMortalityStatus? {Mode, Deceased, SeasonEndingInjury, RaceSuspensionRemaining, CareerFileDeleted,
   IsFit}) is NON-NULL when a fatal accident ended the career. Add a full-immersion death screen driven off it
   (a new Is*Step / a converter on CareerOver):
   - NORMAL death (Deceased && !CareerFileDeleted): an in-world "career over" screen that OFFERS RESTORE — the
     save slots (SaveSlots()/RestoreSlot) so the player can un-do the death (§4).
   - HARDCORE death (CareerFileDeleted): the FINAL permadeath screen — the career + all its saves are already
     physically DELETED; NO restore, ever. ⚠⚠ The session's DB is DISPOSED here — bind ONLY CareerOver (it is
     DB-free by design); NEVER read Summary or any other session member on this screen (that's what the Slice-3
     review fix prevents — don't reintroduce it).
   - A RICHER death-screen model (an in-world obituary + the career record) is coming from Claude round 4
     (DeathScreen()); build against CareerOver now and swap to the richer model when it lands.

6. DOSSIER AVAILABILITY LINE (DossierView.xaml). Claude adds CharacterDossier.Availability (Fit / Injured N
   races / Season over / Deceased) beside the existing InjuryRisk — bind it there once it lands.

Test: dotnet build + the render-harness suite green (it validates every binding). Merge onto the current
hub/increment-4 tip.
```

---

## CLAUDE — death/injury Slice 5 (backend projections) + Slice 6 (living world + tuning)

```
Continue the AMS2 Career Companion (Z:\Claude Code\ams2-career-companion, hub/increment-4). Read
docs/dev/character-death-injury.md (§5, §6) + the memory TOP block first. Claude lane = Core/ViewModels/
Data + tests; do NOT touch src/Companion.App/** (Codex renders the screens from what you expose). Slices
1-4 are shipped (MortalityMode + save/reload; accident severity v7; the d500 injury/death fold + Hardcore
permadeath; auto-sim DNS rounds + shell sit-out routing). These slices are DISPLAY-ONLY projections + a
living-world dispatch — replay byte-identical (NO fold change). Test + commit per slice.

SLICE 5 — the backend the death screen + dossier need (all pure projections over folded state):
1. CharacterDossier.Availability — add an availability field (an enum + a label: Fit / Injured (miss N) /
   Season over / Deceased) built in CharacterDossier.Build from the folded PlayerCareerState
   (RaceSuspensionRemaining / SeasonEndingInjury / Deceased), beside the existing InjuryRisk. Additive; a
   healthy driver reads "Fit". Thread it through the dossier VM so DossierView can bind it (Codex).
2. A rich DEATH-SCREEN model — a DeathScreenModel projection: an in-world OBITUARY + the career RECORD
   (reuse CareerTimeline / CareerRecordsBook: seasons, wins, podiums, titles, best finish) + the DEATH
   CAUSE (the fatal round's accident severity + venue, read from the player.accident journal row) + for
   Normal, the restorable save slots (SaveSlots()). Expose it via the session (a DeathScreen() method,
   additive default null) and surface it so HomeViewModel.CareerOver's consumer can render an obituary +
   record, not just the bare PlayerMortalityStatus. Pure read over the folded journal/state — no new
   persistence, so replay stays byte-identical.

SLICE 6 — the living-world setback + the tuning:
3. ACCIDENT/INJURY/DEATH SETBACK DISPATCH — the living-world feed (ICareerSession.SmgpDispatches(), Task 4)
   already has SmgpDispatchKind.Setback (Codex's NewsView styles it). Wire an accident outcome into it: a new
   SmgpBeatKind (e.g. Injured / SeasonEndingInjury / Died) detected by SmgpCareerBeats.Detect from the
   player.accident journal row, voiced through a new dispatches.json corpus template (SmgpDispatchCorpus), so
   the news reacts — "X sidelined by a crash, out N races" / "X's season ends in the barriers" / "Tragedy: X
   killed at <venue>". DISPLAY-ONLY, deterministic off the master seed => replay byte-identical. Mirror the
   existing setback.* dispatches (rivalry-lost / career-over) exactly.
4. safetyOffset TUNING (open decision B) — the §3.4 d500 bands ship in data/rules/perks.json ("accident"
   block) + AccidentModel.DefaultRules (the code fallback). Confirm the exact numbers with Mike and expose
   them for tuning. safetyOffset already reads durability + injury-durability perks + the reckless baseAdd —
   verify the spread feels right (a glass_cannon heavy shunt is meaningfully deadlier than an ironman's) and
   adjust the scales in the data block if needed; sync data/rules -> dist/data/rules.

Determinism: Slice 5 = pure projections (no fold change, no new draws). Slice 6's dispatch is display-only
(never a fold input; deterministic body selection off the master seed); the bands are already-gated data.
Ship a byte-identity re-sim test on an injured/dead career proving the new dispatch never perturbs the fold.
Whole suite green.
```
