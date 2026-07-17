# Codex GUI brief — SMGP-300 progression/career surfaces (round 6)

_Written 2026-07-16 by Claude (Head of Coding). Lane contract as always: everything below is a
**bindable ViewModel surface that already exists and is tested**; the GUI lane owns the XAML that
presents it. No Core/ViewModels/Data edits are needed or wanted — if a bind contract feels wrong,
flag it back rather than reshaping the VM._

The SMGP-300 wave finalized the Level-300 / 17-season / injury / game-over spine and added the
missing *feedback and foresight* surfaces the windows audit flagged. Every item lists the exact
member to bind and the intended feel. Ordered by impact.

## 1. Post-apply progression announcement (results flow)

- `HomeViewModel.LastProgression` (`RoundProgressionSummary?`) — set right after every successful
  Apply, null for character-free careers. Fields: `XpGained`, `LevelBefore`, `LevelAfter`,
  `LevelsGained` (multi-level gains grouped into ONE number), `SkillPointsAvailable`.
- Intended feel: a compact strip/toast on the post-result screen — "+38 XP · LEVEL 41 → 43 ·
  6 SP banked". One element, no dialog, never blocks the flow. Hide when null or `XpGained == 0`
  and `LevelsGained == 0`.

## 2. MAX LEVEL state on the Driver dossier

- `CharacterDossier.IsAtLevelCap` (bool), `LevelCap` (int). At the cap `XpIntoLevel` now reads 0
  and `XpForNextLevel` 0 — the old "N / 0 XP to next level" line must be REPLACED by a MAX badge
  when `IsAtLevelCap` ("LEVEL 300 — MAX" or the arcade equivalent). `LifetimeXp` /
  `AvailableResetXp` keep counting and stay presentable.

## 3. Base-vs-active rating effects (Driver dossier)

- `CharacterDossier.ActiveModifiers` (`IReadOnlyList<DossierModifierLine>`): `Effect` ("+0.30
  wet-weather pace", "+0.020 car power"), `Condition` (null = always on; else "Wet rounds",
  "Long races", …), `AlwaysActive`. Intended: a small "ACTIVE EFFECTS" block under the stat
  rails — always-on lines plain, conditional lines with their condition chip.

## 4. Campaign timeline (new hub surface)

- `ICareerSession.CampaignTimeline()` (`IReadOnlyList<CampaignTimelineEntry>`): 17 entries for
  SMGP — `Ordinal`, `State` (Locked/Current/Completed), `Year?`, `Title` + `Era` (the authored
  season lore titles — see §9), `PlayerPosition?`, `PlayerChampion`, `MissedRounds` (injury
  absences that season — never hidden).
- Intended: a horizontal 17-slot campaign strip (History tab or season review) — completed slots
  show outcome, current slot glows, locked slots show title + era only. This is the mission's
  "season selection and timeline" surface.

## 5. Calendar injury dimension

- `CalendarRoundViewModel.PlayerStatus` (Raced/SatOut/WillMiss/Upcoming), `PlayerStatusLabel`
  ("SAT OUT — injured" / "WILL MISS — injured"), `HasPlayerStatus`. Intended: a small amber chip
  on affected round cards; quiet when fit.

## 6. Hub header chips

- `HomeViewModel.DriverLevelText` ("LV 137", null collapses) and `DriverAvailabilityLabel`
  ("Fit" / "Injured — out 2 races" / …). Intended: two small chips in the hub header so level and
  an active injury are visible at a glance, not two tabs deep.

## 7. Medical record + mortality label (Driver dossier)

- `DossierViewModel.InjuryHistory` (`IReadOnlyList<InjuryHistoryEntry>`: `SeasonOrdinal`,
  `SeasonYear`, `Round`, `Label`, `Description` — a non-graphic deterministic flavour line like
  "bruised ribs"), `HasInjuryHistory`.
- `DossierViewModel.MortalityLabel` ("MORTALITY: NORMAL" etc.) — the career's immutable risk
  setting, finally visible mid-career. Present as a fixed chip, explicitly non-editable.

## 8. Level-up banner persistence (already wired — behavior note)

- `DossierViewModel.LevelUpPending`/`LevelsGained` now SURVIVE an app restart (persisted
  acknowledgment marker). No XAML change needed; do not add a second dismissal path — the
  existing `AcknowledgeLevelUp` command is the only correct clear.

## 9. SMGP season lore header (briefing)

- `BriefingViewModel.SmgpSeasonTitle` / `SmgpSeasonSubtitle` / `SmgpSeasonEra` /
  `HasSmgpSeasonLore` — the authored season identity ("The Tenth Summer · The Iron Circus").
  Intended: the "SEASON n / 17" header grows a title line + era tag. All 17 seasons are authored
  (data/rules/smgp/seasons.json); absent file degrades to the plain header.
- Long-form lore for richer screens: `ICareerSession.CurrentSeasonLore()` exposes `Overview`,
  `Preseason`, `Technical`, `Safety`, `Themes`, `Timeline`, `Arcs`, `Hooks`, `Contenders`,
  `Milestones` — a future season-preview page can draw on all of it.

## 10. Career gallery memorial badges (start screen)

- `RecentCareer.IsTerminal` / `TerminalBadge` ("IN MEMORIAM" / "CAREER OVER"). Intended: a
  respectful badge on finished careers' cards so a dead career never masquerades as playable.
  The card stays openable (the archive is viewable by design).

## 11. Grid Preview: legacy edits affordance

- `SkinsViewModel.HasStagingOverrides` / `StagingOverridesNote` / `ClearStagingOverridesCommand`.
  Careers that carry old grid-editor rename/rebind rows still apply them at staging; the preview
  must SAY so and offer the one-click clear. One line + one button, only when
  `HasStagingOverrides`.

## 12. Degraded save slots (save manager)

- `SaveSlotInfo.IsDegraded` — a restorable snapshot whose metadata sidecar was lost. Label reads
  "&lt;slot&gt; (recovered — details unreadable)". Present dimmed but restorable; never hide it.

## Render-test note

`SeasonReviewView` still has no dedicated render-harness coverage (flagged by the audit). While
binding the timeline/lore surfaces, please add `SeasonReviewRenderTests` (offers + final-season
hold + terminal variant) in the render stand-ins you own.
