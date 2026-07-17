# Build brief — App-wide Developer Debug Menu

_Paste-ready mission for a fresh Opus / Claude Code session running in
`Z:\Claude Code\ams2-career-companion`. Self-contained: every anchor below is verified against the
current tree so you don't have to re-derive it. Author: Claude, 2026-07-17._

---

## 0. Mission

Build the **app-wide developer debug menu** — roadmap **Piece 2** of
`docs/dev/dynasty-passport-roadmap.md`. It is the enabler for everything after it: it lets us
preview/unlock every screen, mode, and season while we build Dynasty gating, the Fable tycoon
economy, and Racing Passport — without grinding through content each time.

Deliver the **whole vertical slice this session**: the ViewModel + the hidden App pane + the keybind
+ the settings gate + tests, then build, publish, and deploy into `dist/`. Follow the house style:
build maximally, don't stop halfway, adversarially review your own fold-safety before you commit.

---

## 1. Read these first (in order)

1. `docs/dev/dynasty-passport-roadmap.md` — **Piece 2 is the spec.** (Pieces 1 & 3 are the follow-on
   work this menu unblocks; don't build them now.)
2. `docs/dev/career-modes-alpha1.md` — the three career modes and their contracts.
3. `docs/PROJECT.md` §2 (architecture), §3 (determinism spine), §11 (GUI shell). ⚠ §7.7 says the
   progression-v2 code is "design only" — **that is stale, the v2 types exist in `src`** (see §5).
4. `CLAUDE.md` — lane boundary + build/test commands + "locked directions" (don't re-litigate SMGP
   or the deterministic-replay contract).

---

## 2. What the menu must do (Piece 2 + Mike's original ask)

- **Hidden + dev-only + gated** so it never reaches players (see §3 — gating detail matters).
- **Jump into any of the 3 modes** — including **Racing Passport**, which is `IsAvailable=false` in
  the menu today and additionally *throws* at creation (unbuildable — preview it via a fake session, §6).
- **Preview any season / year pack** and **all 17 SMGP ordinal seasons**, bypassing normal flow.
- **Force character state** — level (1–300), SP, Racing DNA, mastery — for previewing progression screens.
- **Force terminal / injury states** — injury, season-ending injury, death, SMGP career-over,
  promotion/demotion, sit-out — to preview those screens.
- **Money / tycoon economy** — stub hooks only; Fable is building that economy in parallel
  (`docs/dev/fable-tycoon-brief.md`). Leave a seam, don't invent the economy.
- **Reveal SMGP future lore** (the spoiler-hidden seasons) and **dump the journal / inspect
  determinism state** (master seed, projection fingerprint) for debugging.

---

## 3. Gating — get this right for OUR deploy model

We run the **Release** RC (the user launches `dist/AMS2CareerCompanion.exe`, a Release single-file
build). So:

- **The core preview capability MUST survive in Release**, gated by a **runtime** flag
  `AppSettings.DeveloperMode` (default `false`, **not** surfaced in the normal Settings UI). If you
  gate the whole menu behind `#if DEBUG` it compiles OUT of the RC and is useless to us. Use
  `#if DEBUG` **only** for genuinely dangerous dev-only extras, never for the previewing itself.
- **Unlock path** (pick a sensible default, note it in your delivery): a hidden key chord
  (recommend **Ctrl+Shift+D**) that flips `DeveloperMode` for the session and persists it; also honor
  an env var (recommend `AMS2_DEVMODE=1`) read at startup as an alternate unlock. With the flag off,
  the keybind is a no-op and nothing renders.
- A shipped Release with the flag off must show **nothing** and cost nothing (no extra RNG draws, no
  extra journal rows, no DB writes).

---

## 4. THE core design decision (violate this and you corrupt saves)

Progression, standings, injuries, and death are **DERIVED outputs of the one deterministic fold**, and
`ResimulateCore` **byte-compares** every derived row on replay (`src/Companion.Data/ReplayService.cs`,
`ResimulateCore` ~line 474, pure fold `ComputeRoundFold` ~line 826). There is **no** "set level / force
death / jump to season N" mutator anywhere, by design. So split the menu into **two tiers**:

**Tier 1 — Real-career debug (replay-safe, produces honest saves).** Routes through the *same* seams
the normal app uses:
- Construct throwaway careers via `CareerCreationRequest` → `ICareerFactory.Create` (any pack, any
  `ExperienceMode`, any `MortalityMode`, any seed, any character).
- Drive only the **provenance-excluded INPUT mutators** on `ICareerSession`
  (`SpendCharacterPoint`, `ApplySkillPlan`, `ApplySkillReset`/respec, `Apply(draft)`,
  `ResolveSmgpOffer`, `AcceptOffer`, `StartNextSeason`). These are re-derived on replay and are the
  ONLY legitimate injection points.
- A career built this way still resimulates byte-identical. Good for "advance N seasons", "spend all
  SP", "play a scripted result set".

**Tier 2 — Preview / inspect (non-persistent, never touches a real DB).** For states you *cannot*
reach through real seams — Racing Passport (unbuildable), an arbitrary level 250, a death screen
without a fatal fold: feed the View a **fake `ICareerSession`** returning canned projections
(`CharacterDossier`, `SkillTreeSnapshot`, `DeathScreenModel`, `SitOutStatus`, etc.). This is exactly
the established **render-harness stand-in pattern** (`tests/Companion.RenderHarness.Tests/*` — each
test hosts a real View against a lightweight fake exposing just the bound members). Promote that
pattern into a small shippable-but-dev-only preview host. **Never resimulated, never serialized to a
`.ams2career`, never shipped-on.**

**Hard rules (state them in code comments):** never write `Level`/`Xp`/`Reputation`/standings/
`Deceased`/`Smgp*` derived state directly; never inject a journal row outside the provenance-excluded
INPUT phases (`src/Companion.Core/Career/CareerStreams.cs`, `JournalPhases` ~lines 41-166); never
touch the f1db oracle (`tests/Companion.Tests/Oracle/`).

---

## 5. Verified anchors (current tree — don't re-derive)

**Solution / entry / build**
- Solution `Companion.slnx` (XML, no `.sln`). App project `src/Companion.App/Companion.App.csproj`
  (`WinExe`, `AssemblyName=AMS2CareerCompanion`, win-x64 self-contained single-file).
- Composition root: `src/Companion.App/App.xaml.cs` `OnStartup` (~line 33) — builds `SettingsService`,
  `CareerEnvironment.CreateDefault(<baseDir>/data/ams2)`, `TrackingCareerFactory`, `ShellViewModel`
  (~line 62), shows `MainWindow{DataContext=_shell}`. Data dir from `AppContext.BaseDirectory`.

**Navigation — where the debug pane hooks in**
- Window key routing: `src/Companion.App/MainWindow.xaml.cs` `OnPreviewKeyDown` (~lines 46-94). **Add
  the debug keybind here** (respect the terminal-state guard at ~54-56).
- Outer state machine: `src/Companion.ViewModels/Shell/ShellViewModel.cs` — `Current` (~line 65) swaps
  Start → Wizard → Hub, plus the **Settings overlay** `ToggleSettings()` (~lines 197-210) that stashes
  `_beforeSettings` and restores on close. **Mirror this for `ToggleDebug()` — it's the app-wide
  overlay template** (works over any current screen). Current career path tracked at `_currentCareerPath`.
- Inner rail (alternative host): `src/Companion.ViewModels/Hub/HubViewModel.cs` builds `Tabs`
  (~lines 60-77); a `HubTabViewModel` with `showInRail:false` (like the Race tab, ~line 62) is a
  reachable-but-hidden tab. Prefer the Shell overlay for an *app-wide* menu.
- VM→View wiring: type-keyed `DataTemplate`s in `src/Companion.App/App.xaml` (`Application.Resources`,
  ~lines 22-89). **Add `Views/DebugMenuView.xaml` + one `<DataTemplate DataType="{x:Type ...}">` here.**

**Settings (home for the flag)**
- `src/Companion.ViewModels/Settings/AppSettings.cs` — versioned record (`CurrentVersion=1`,
  `Normalized()` ~line 223). **Add `DeveloperMode` bool here; bump `CurrentVersion`; clamp in
  `Normalized()`.** Live via `SettingsService.cs` (`Changed` event, consumed in `App.xaml.cs`).
  Persisted at `%APPDATA%\AMS2CareerCompanion\settings.json` (`SettingsStore.cs` ~lines 36-38).

**Career modes + the two gates to bypass**
- Mode discriminator is a **string**: `src/Companion.Core/Career/CampaignProgressionPlan.cs` ~lines
  9-18, `static class CareerExperienceModes` (`grandPrixDynasty` / `smgp` / `racingPassport`). Stored
  as `PlayerCareerState.ExperienceMode`.
- Menu gate: `src/Companion.ViewModels/Start/StartViewModel.cs` `AlphaCareerModes` (~lines 20-59);
  `IsAvailable` — Racing Passport `false` (~line 57). `StartCareerMode` refuses `IsAvailable!=true`.
- Creation gate: `src/Companion.ViewModels/Services/CampaignCreationPlanner.cs` `Prepare` **throws**
  for RacingPassport (~lines 90-92). ⇒ Racing Passport is genuinely unbuildable → **Tier-2 preview only**.

**Creation + session seam**
- `src/Companion.ViewModels/Services/ICareerFactory.cs` — `CareerCreationRequest` (~lines 7-85):
  `PackDirectory, CareerFilePath, CareerName, MasterSeed, PlayerLiveryName, ExperienceMode, Character,
  GridSelection, FormAware, SmgpMode, UseModdedField, UseAlternateTracks, Mortality`. Factory iface
  ~lines 91-96; App wrapper `src/Companion.App/Services/TrackingCareerFactory.cs`.
- `src/Companion.ViewModels/Services/ICareerSession.cs` (~1690 lines) — **the full state contract**:
  every read-projection the hub renders + `Apply(draft)` + the INPUT mutators listed in §4. Impl
  `CareerSessionService.cs` (`CreateCareer` ~line 196, `OpenCareer` ~line 442).

**Progression (v2 exists — §7.7 of PROJECT.md is stale)**
- `src/Companion.Core/Character/CharacterLevelProgression.cs` — `Level300Version=2`, `Level300Max=300`,
  `LevelForTotalXp` (~line 63). Level/XP are **derived** (`PlayerCareerState.Xp/.Level`,
  `CareerStates.cs` ~lines 94-98). `CharacterProgressionV2Math.cs` (499-SP pool),
  `RacingDnaCatalog.cs` (+`data/rules/racing-dna-v2.json`), `MasterySkillCatalog.cs`/`SkillTree.cs`
  (+`data/rules/mastery-skills-v2.json`), `CharacterDossier.Build`.

**Fold safety**
- `src/Companion.Data/ReplayService.cs` — `ComputeRoundFold` (~826, pure), `ImportAndFoldRound`
  (~266, the only live path), `Resimulate`/`ResimulateCore` (~450/474, byte-compares & rolls back on
  divergence). INPUT vs DERIVED split: `src/Companion.Core/Career/CareerStreams.cs` `JournalPhases`
  (~41-166) — provenance-excluded INPUT phases are `player.character/gridSelection/statSpend/skillPlan/
  skillReset/roundConditions/respec` + `smgp.swap`; everything else is byte-compared DERIVED.

**Mortality / injury / game-over (screens already built — you preview them)**
- `src/Companion.Core/Career/MortalityMode.cs` (`Off/Normal/Hardcore`); fold fields on
  `CareerStates.cs` (`Mortality`, `RaceSuspensionRemaining`, `SeasonEndingInjury`, `Deceased`).
- Session reads: `ICareerSession` `PlayerMortality()`, `DeathScreen()→DeathScreenModel`,
  `CurrentSitOut()→SitOutStatus`. Shell wiring: `src/Companion.ViewModels/Shell/HomeViewModel.cs`
  (`CareerOver`, `DeathScreen`, `IsCareerTerminal`, `IsSitOutStep`, `IsPromotionStep`, `IsFinaleStep`).
- Existing views: `src/Companion.App/Views/DeathScreenView.xaml`, `SmgpCareerOverView.xaml`,
  `SitOutView.xaml`; VMs `SitOutViewModel.cs`, `SmgpFinaleViewModel.cs`.

**Seasons / packs / gating reality**
- `packs/` = 21 faithful `f1-1967 … f1-2020` + `smgp-1`. Immutable, sha256-pinned at creation
  (`PinnedPackEnvelope`). SMGP count is code (`SmgpRules.CampaignSeasons=17`) over one pack;
  per-ordinal lore `data/rules/smgp/seasons.json` via `SmgpSeasonLore.cs`.
- ⚠ **The "preview years but can't play until 1967" chronological gating DOES NOT EXIST yet.** There
  is no per-year play-lock; `CampaignTimeline()`'s `Locked/Current/Completed` is a display projection.
  So there is **nothing to bypass** — the debug menu only needs to *preview around* it. (Implementing
  that gate is Piece 1, later.)

**Build / test / deploy**
- Build `dotnet build Companion.slnx`. Test `dotnet test tests/Companion.Tests` (~2100+ xunit) +
  `tests/Companion.RenderHarness.Tests` (Windows-only render stand-ins, self-skips elsewhere).
- App reads loose content from `AppContext.BaseDirectory\data\...` + `packs\...`; heavy art trees are
  `ExcludeFromSingleFile` and read loose. `dist/` (git-ignored) is the deployed RC.
- Deploy = watch-then-swap: stop `AMS2CareerCompanion.exe`, back up `dist\...exe` to `.exe.old-<ts>`,
  copy the freshly-published exe in, relaunch. **No `.claude/launch.json` exists** — create one if you
  want to run the app in-loop (`runtimeExecutable: dotnet`, `runtimeArgs: [run, --project,
  src/Companion.App]`).

---

## 6. Recommended architecture

- `src/Companion.ViewModels/Debug/DebugMenuViewModel.cs` — the dev surface, `[RelayCommand]`s over
  `ICareerFactory` + `ICareerSession`. Pure VM, fully unit-testable (no WPF ref in that project).
- A small **preview host** implementing `ICareerSession` with canned projections (Tier 2), factored
  from the render-harness stand-in shape. Keep it in ViewModels (or a `Debug` namespace) so both the
  app and tests can use it.
- `ShellViewModel.ToggleDebug()` overlay mirroring `ToggleSettings()`; `DebugMenuView.xaml` +
  `DataTemplate` in `App.xaml`; keybind in `MainWindow.xaml.cs`; `AppSettings.DeveloperMode` gate.
- **Lane note:** this spans both lanes (VM logic = Coding lane; View/keybind/DataTemplate = GUI lane).
  You own the full slice this session — but keep the layering clean so it merges with Codex's GUI work.

---

## 7. Acceptance criteria

- [ ] Flag **off** (default): keybind is a no-op, nothing renders, zero extra RNG/journal/DB activity.
      A test asserts the no-op.
- [ ] Flag **on**: the overlay opens over any screen and closes back to exactly where you were.
- [ ] **Tier-1** commands (throwaway career create; advance season; spend SP/skill plan) route through
      the real factory/session seams, and a debug-created career **resimulates byte-identical**
      (add/extend a resim test).
- [ ] **Tier-2** previews (Racing Passport, arbitrary level, death/career-over/sit-out screens) render
      from a fake session and **never create or write a `.ams2career`** (a test asserts no DB file).
- [ ] Racing Passport is reachable **only** as a Tier-2 preview (never through real creation).
- [ ] SMGP future-lore reveal + journal/determinism dump work.
- [ ] `dotnet build Companion.slnx` clean; full unit suite + render harness green.
- [ ] Published win-x64 single-file, deployed into `dist/`, app relaunches and the menu opens via the
      documented unlock. Delivery report states the unlock method and every fold-safety guard you added.

---

## 8. Guardrails (house rules)

- Never break byte-identical replay (§4). Adversarially review your own fold-safety before committing.
- Don't touch the f1db oracle. Don't hard-code era logic. Serialize with `CoreJson.Options`.
- Report truthfully: if a test fails, say so with output; if something's a stub (tycoon economy), say so.
- Build maximally — ship the whole slice, then deploy. Don't stop at "designed it."
