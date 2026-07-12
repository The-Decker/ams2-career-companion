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
