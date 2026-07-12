# Codex GUI Round 3 — deep, tactile, skinned every screen

**Status:** standing brief (Codex's GUI/art lane) · **Date:** 2026-07-12 · from Mike, via Claude

You are the GUI/art lead. Round 1 (rival hero + pixel grid) and Round 2 (race plan + flags) and the
theme system (light/dark + 7 accents) are all merged into `hub/increment-4`. This round is a big one:
make the WHOLE app feel deep, tactile, information-rich, and immersive — a place a player *wants* to
spend time between races. Mike's words: *"more compact text boxes, more clickable things, more data,
more info, more fun things that fill up time and create immersion."*

## Lane (do not cross)

- **YOURS:** everything under `src/Companion.App/**` — Views (`*.xaml` + trivial `*.xaml.cs`
  code-behind), `Themes/**` (Theme.xaml + Theme.Dark/Light + Accents + Smgp.Track), styles, control
  templates, converters that are pure presentation, art, `docs/dev/*` GUI notes.
- **NOT YOURS (Claude owns, do not edit):** `src/Companion.Core/**`, `src/Companion.ViewModels/**`,
  `src/Companion.Data/**`, `src/Companion.Ams2/**`, `data/**`, the points/standings engine, the fold.
- **The contract between us:** you BIND to view-model properties. Claude is adding a batch of new
  read-only projection properties this same night (sponsors, head-to-heads, per-track stats, a career
  milestone timeline, an evolving player narrative, a per-row rival flag). Bind to them as they land;
  where a binding target does not exist yet, stub the layout with a `FallbackValue`/placeholder and
  leave a `<!-- TODO bind: X -->` so we can wire it when Claude's property ships. Coordinate through
  the auto-memory `ams2-hub-build-progress.md`.
- Keep the **theme contract** green: every semantic brush via `DynamicResource`, no inline hex in
  Views/MainWindow/Theme.xaml (put paint in a base/accent/invariant-art dict), both Dark AND Light
  legible, contrast ≥ 4.5:1. `ThemeContractRenderTests` + the render harness must stay green — add a
  render test for any new view/control. Branch off the latest `hub/increment-4`, keep the diff
  lane-clean, and it will be reviewed-then-merged like the last rounds.

## 0. BUGS FIRST (quick, do these before the big work)

1. **Paddock selector contrast — white text on white.** In `PaddockView.xaml`, the driver/team
   master-list selection highlight renders white text on a (near-)white selected background — the
   selected row is unreadable in one or both themes. Fix the `ListBox`/`ListBoxItem` selected-state
   template so the selected row uses `SelectionBrush` (bg) + `OnAccentBrush`/`TextBrush` (fg) with
   proper contrast in BOTH themes. Audit every other selectable list (Standings, Calendar, History,
   Skins, the wizard grid) for the same trap while you are in there.
2. Anywhere else selection/hover state is illegible in Light theme — sweep and fix.

## 1. Tactile, skinned everything (the "feel like you're touching something real")

Mike: *"skin the backgrounds for the entire app, skin the buttons, skin the options, skin the X
button, make the buttons more clickable and feel like you're touching something for real."*

- **Buttons:** give every button style (Primary/Accent, Subtle, Icon, the tear-off/close ✕, radio,
  checkbox, combo, slider thumb, list rows) real interaction depth — a resting elevation, a hover
  lift (subtle brightness/scale/shadow), and a pressed state that *depresses* (translate down 1px +
  darken + inner shadow) so a click feels physical. Use the theme's `ShadowBrush`/
  `ControlHoverOverlayBrush` + the new elevation tokens (add them to the base contract if needed, in
  BOTH Dark/Light, and extend `BaseContract` in `ThemeContractRenderTests`). Add smooth, short
  (~90–120ms) `VisualStateManager` transitions.
- **The ✕ / close button** (tear-off windows, settings, dialogs): make it a proper skinned control
  with a clear hover (red-tinted) + pressed state, not a bare glyph.
- **Backgrounds:** the app background should not be a flat fill — give it depth (a subtle vignette /
  radial gradient / faint carbon-fibre or asphalt texture as an *invariant-art* brush that works on
  both themes, low-contrast so it never fights text). Panels should read as raised surfaces
  (`SurfaceBrush` + a hairline `EdgeBrush` + a soft `ShadowBrush`).
- **Options/settings, combos, sliders, radios, checkboxes:** reskin to match — custom track/thumb,
  clear checked/hover/pressed states, generous hit targets.
- **Tooltips everywhere:** a consistent, skinned tooltip style (delayed, themed surface, arrow
  optional), and ADD tooltips to every non-obvious control across all screens — stat abbreviations
  (ENG/TM/SUS/TIRE/BRA, D.P., DNQ, top-5, OPI), icons, buttons, column headers, ladder terms.

## 2. The Paddock — the centrepiece (deep, compact, clickable, immersive)

Make the Paddock the app's living hub. Claude is enriching the data; you make it sing.

- **Layout:** more compact, denser text boxes; a master–detail that uses space well (avoid the
  current big-empty-panel look). Cards with clear hierarchy, small stat chips, tidy typography.
- **DRIVERS / TEAMS / SPONSORS tabs.** Claude is adding a **Sponsors** projection (`SmgpPaddockModel`
  gains a `Sponsors` list of `SmgpSponsorCard` — id, name, industry, tier, tagline, story paragraphs,
  a `LogoKey` at `data/ams2/smgp/sponsors/<id>.png`, a brand colour hex, and the teams each backs).
  Build a Sponsors master–detail like the driver/team ones: a logo wall / grid of sponsor chips →
  click for the sponsor's story, industry, tier, brand colour accenting, and which teams carry it.
  This rolls into the future Tycoon mode, so design it to feel like a sponsor board.
- **More clickable, more data:** driver cards → click through to head-to-head vs the player, per-track
  best results, this-season form, rivalry history (Claude is exposing these). Team cards → roster,
  sponsors, tier, palette, history. Cross-link everything (click a team on a driver card → team; click
  a driver in a team roster → driver; click a sponsor's team → team).
- **The player's own card — depth + an evolving story.** Claude is making the player narrative EVOLVE
  with the career (a milestone timeline: first race, first points/pole/win/podium, promotions, titles,
  rivalries won/lost, the 17-season campaign progress). Render it as a proper **career timeline / story
  scroll** with chapter beats, not a static three-paragraph block. Show live stat tiles + the timeline.
- Small immersive time-fillers: hover flavour, quotes on hover, a "paddock rumor" line, animated stat
  bars, a "compare two drivers" toggle — whatever makes it a place to linger.

## 3. Every other screen — more in-depth + interactive + tooltips

Apply the same treatment (denser, clickable, tooltip'd, tactile, both-theme-legible) to:

- **History** — the season timeline / records book: richer per-season cards, clickable seasons, the
  "what really happened" almanac surfaced, records, streaks. (Claude may add per-season detail data.)
- **Skins** — the staging/skin view: clearer per-driver skin status, click for detail, better legibility.
- **Calendar** — the season schedule: clickable rounds → round detail (venue, laps, weather, the
  round's grid/DNQ, setup note), a nicer map treatment, progress through the season.
- **Driver** (the Dossier tab) — the player's own hub: hero + specs + the evolving story + progression.
- **Standings** — clickable rows → driver detail, better column tooltips, the round-matrix polish,
  highlight the player + the named rival.
- **The Upcoming Race loop** (Upcoming Race → Rival → Qualifying → Starting Grid → Race → Confirm →
  Promotion): tighten every step, more interactive, consistent chrome, tooltips, the pixel grid +
  race-plan polish.

## 4. Rival in the result-entry drag-and-drop (a specific ask)

Mike: *"in the rival screen, the rival is shown not only above the quali and race selection but also in
the race selection where you can drag and drop — a red 'RIVAL' text shows by their name."*

Claude is exposing a per-row rival flag on the result-entry grammar (each draggable driver row will
know if it is the named rival). In `ResultEntryView.xaml` (the `SeatLine` template + the classified /
remaining lists, used for BOTH qualifying and race), when a row is the rival, show a small red
**"RIVAL"** badge next to the name (accent/danger colour, bold, uppercase), on the draggable rows and
their placed positions. Make it obvious as you drag them into the order.

## Definition of done

Every screen legible + tactile in BOTH themes; new controls have render tests; tooltips are pervasive;
the Paddock is deep and clickable with a Sponsors tab; the rival badge shows in result entry; the theme
contract + full render suite stay green; diff stays in your lane; branched off the latest
`hub/increment-4` for a reviewed merge.

## Contract — Task 2 Paddock-depth properties (LANDED by Claude, ready to bind)

All additive, display-only, already populated by `CareerSessionService.SmgpPaddock()`. Bind directly; the
`PaddockViewModel` surfaces the cards. Nothing here is a fold input.

**`SmgpDriverCard` (in `ICareerSession.cs`):**
- `Timeline` : `IReadOnlyList<Companion.Core.Smgp.SmgpCareerBeat>` — **PLAYER card only** (empty for AI). The
  evolving story, chronological. Each beat: `WhenLabel` ("Season 3 · Monaco"), `Kind`
  (`SmgpBeatKind`: Arrived, FirstStart, FirstPoints, FirstTop5, FirstPole, FirstPodium, FirstWin, Promotion,
  Demotion, Title, RivalryEarned, RivalryLost, NearMiss, SeasonMilestone, Finale), `Headline` (ALL-CAPS,
  e.g. "FIRST WIN"), `Detail` (a sentence). Render as a **timeline / story scroll** with a per-`Kind` icon+accent.
- `NarrativeIntro` : `string` — **PLAYER card**, one-line live standing ("Season 5 of 17 · racing for Madonna
  · P2, 34 pts · 8 WINS · 2 TITLES"). The header above the timeline.
- `HeadToHead` : `SmgpHeadToHead?` — **AI cards** (null on the player's own card / before they have met):
  `RacesMet`, `PlayerAhead`, `DriverAhead`, `PlayerBestTogether` (int?), `BestTogetherVenue` (string?),
  `PlayerStreak`, `DriverStreak` (the live SMGP battle streak).
- `PerTrackBest` : `IReadOnlyList<SmgpTrackBest>` — **AI cards**: `Venue`, `DriverBest` (int?), `PlayerBest`
  (int?) — a per-venue compare (ordered by venue).
- `FormRecent` : `IReadOnlyList<int?>` — **AI cards**: the last ≤6 race finishes, oldest-first, `null` = a DNF
  (sparkline the trend).

**`SmgpTeamCard`:**
- `Tier` : `string` — "Level A".."Level D" (from prestige).
- `PaletteHex` : `string` — "#RRGGBB" team accent (`TeamPalette`).
- `Roster` : `IReadOnlyList<SmgpTeamRosterLine>` — `DriverId`, `Name`, `IsPlayer`, `SeasonLine` ("P3 · 18 PTS"
  or "—"), `CareerLine` ("12 WINS · 3 TITLES" or ""). Highlight the `IsPlayer` row.
- `Sponsors` : `IReadOnlyList<SmgpTeamSponsorRef>` — `Id`, `Name`, `Tier`, `BrandColorHex` — the sponsors that
  back this team (cross-link to the Sponsors tab by `Id`).

## Contract — Task 3 (rival flag + screen data) LANDED, and cross-lane XAML touches

**Cross-lane XAML Claude already edited (for Mike's direct bug asks — please keep, don't revert):**
- `RivalScreenView.xaml` — the subtitle, YES button and picker dropdown now bind gender-aware VM props
  (`Briefing.SmgpRivalIntro`, `SmgpNameButtonLabel`) instead of hard-coded "him/his"; the picker `ComboBox`
  `ItemTemplate` gained an **outlined coloured CLASS chip** (`Tier` + `TierColorHex`).
- `ResultEntryView.xaml` — the `SeatLine` template gained the **red RIVAL badge** (Mike's ask) via the
  existing `StringsEqualVisible` MultiBinding of `DataContext.RivalDriverId` (ItemsControl ancestor) vs the
  row's `DriverId`, `Background="{DynamicResource ErrorBrush}"`. If you want it on the DNF/DSQ templates too,
  mirror it there (`Seat.DriverId` on those rows).

**New VM data to bind (all display-only, already populated):**
- **Standings** (`StandingsRow`): `IsPlayer` / `IsRival` bools — highlight the player row and the currently-named
  SMGP rival's row (reuse `ErrorBrush` for the rival accent). Off-SMGP → no `IsRival`.
- **Calendar** (`SeasonScheduleEntry`, from `SeasonSchedule()`): `Championship` (bool), `GridSize` (int?), `Dnq`
  (`IReadOnlyList<ScheduleDnqEntry>{Name,TeamName,Number}` — the round's DNQ'd backmarkers), `WeatherLabel`,
  `SetupNote`, `Opponents` (int?), `Status` (`SeasonRoundStatus` Done/Next/Upcoming). Build the clickable
  round-detail panel + a done/next/upcoming progress treatment from these.
- **History** (`CareerSeasonCard.RoundLines`, `IReadOnlyList<CareerSeasonRoundLine>`): per applied round —
  `Round`, `Venue`, `PlayerFinish` (int?), `RivalName`/`RivalFinish` (the rival named that round + how the duel
  went), `ChampionAfter`, `PlayerPointsAfter`. Render as the "my season" almanac breakdown.
- **Driver / Dossier** (`DossierViewModel`): `Timeline` (`IReadOnlyList<SmgpCareerBeat>`) + `NarrativeIntro`
  (string) + `HasSmgpNarrative` (bool) — the same evolving story-scroll as the Paddock player card, surfaced on
  the Driver tab.
- **Rival dossier** (`SmgpRivalOption`): `HeadToHead` (`SmgpHeadToHead?`), `Tier`/`TierLabel`/`TierColorHex`,
  `Pronouns` — for the deeper rival screen + the coloured class picker.

## Contract — Task 4 (SMGP living-world dispatches) LANDED, ready to bind

All display-only projections over the folded results (never a fold input; deterministic bodies off the master
seed, replay byte-identical). This is the data spine for the NEWS TICKER / dispatch cards + the paddock rumor
line the round-4 brief (§3 immersion surfaces) calls for.

**New VM data to bind:**
- **`ICareerSession.SmgpDispatches()`** → `IReadOnlyList<SmgpDispatch>`, **newest first**, empty off-SMGP.
  Each `SmgpDispatch` = `{ WhenLabel (string, "Season 3 · Monaco"), Kind (SmgpDispatchKind), Headline (string,
  bold arcade), Body (string, 1-3 sentence story), DriverArtKey (string — a driver id for
  `portraits/<id>.jpg`, or ""), TeamArtKey (string — a team id for `smgp/teams/<team>.jpg`, or ""),
  SortSeason/SortRound/SortSeq (ints, already sorted) }`.
  - `SmgpDispatchKind`: `Milestone` (green accent — the player's firsts/promotions/titles/rivalry-won/finale),
    `RaceResult`, `Setback` (amber — DNF/demotion/rivalry-lost/near-miss/career-over), `RivalWatch` (a rival's
    win streak / the A. Senna benchmark), `TitleRace` (leader change / title tightening / P2 turnover),
    `SeasonDigest`. Key the card's accent + icon on this. Use `DriverArtKey` to show a face when present
    (rivalry + world dispatches carry one).
  - Render as a scrollable dispatch feed / ticker. `WhenLabel` is the dateline; `Headline` the bold line;
    `Body` the story. A rivalry dispatch's `Body` already ends with the rival's own quoted trash-talk.
- **Paddock rumor** (`SmgpPaddockModel.PaddockRumor`, string, from `SmgpPaddock()`): a rotating one-line
  "paddock rumor" for the Paddock tab (seeded, stable on re-open, empty when no corpus). Bind as a flavour
  strip on the Paddock.
- Corpus lives at `data/rules/smgp/dispatches.json` (art-free — pure copy). No new art keys are *required*;
  `DriverArtKey`/`TeamArtKey` reuse the existing `portraits/`/`smgp/teams/` sets.

## Contract — Task 5 (Tycoon Team Mode read-only DATA SPINE) LANDED, ready to bind

The read-only foundation for the **reserved top-header TYCOON TEAM MODE**. All display-only (no fold mechanics
yet → replay-safe); the actual economy is a later increment. This is the data the tycoon dashboard shell (the
round-4 brief §3) binds.

**New VM data to bind:**
- **`ICareerSession.SmgpTeamDashboard()`** → `SmgpTeamDashboard?` (null off-SMGP) =
  `{ PlayerTeam (SmgpTeamDashboardEntry), Teams (IReadOnlyList<SmgpTeamDashboardEntry> — EVERY team, ranked by
  the derived constructors' standing, player flagged), RivalTeams (Teams minus the player, ranked),
  TeamOfSeason (SmgpTeamOfSeasonFlavour? — null before any round) }`.
- **`SmgpTeamDashboardEntry`** = `{ TeamId, Name, IsPlayerTeam (bool), Prestige (int), Tier ("Level A".."D"),
  PaletteHex ("#RRGGBB"), Motto, LogoKey (smgp/logos/<teamId>.png), History (paragraphs),
  Roster (IReadOnlyList<SmgpTeamRosterLine> — reused from the Paddock: DriverId/Name/IsPlayer/SeasonLine/
  CareerLine), Sponsors (IReadOnlyList<SmgpTeamSponsorRef> — Id/Name/Tier/BrandColorHex),
  ChampionshipPosition (int?, null pre-race), ChampionshipPoints (int), BudgetTier (string flavour:
  Blue-chip/Well-backed/Mid-budget/Shoestring) }`.
  - Build the PLAYER dashboard (roster + sponsors + tier + standing + history) from `PlayerTeam`, and the
    "grid of teams" competitive table from `Teams` (highlight the flagged player row; colour rows by
    `PaletteHex`, chip the `Tier`/`BudgetTier`, show `ChampionshipPosition`/`ChampionshipPoints`).
- **`SmgpTeamOfSeasonFlavour`** = `{ TeamId, Name, PaletteHex, Headline ("OVERACHIEVER OF THE SEASON" /
  "TEAM OF THE SEASON"), Note (flavour sentence, already says "no economy model yet") }` — a banner card.
  Clearly a FLAVOUR seed of the future economy — do not present it as a real budget/finance number.
- No new art or data files. Reuses the Paddock's team cards + the live standings.
