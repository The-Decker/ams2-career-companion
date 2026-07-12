# Continue prompts — round 2 (after the gui-round3 merge @ `efe980a`)

State: `hub/increment-4` @ `efe980a` — Codex's tactile GUI round 3 is merged (cinematic menu, character
workbench, deep Paddock, RIVAL badge, theme polish); Claude's Task 2 (Paddock depth) + Task 3 (rival flag +
Calendar/History/Standings/Driver data) + the gender/challenge fixes are in. Suite 2030 + 67 green; RC deployed.
**Two agents, stay in lane:** Claude = `Companion.Core` / `Companion.ViewModels` / `data/**` + tests (display-only
projections; never a fold input; replay byte-identical). Codex = `src/Companion.App/**` XAML/Themes/art. Coordinate
through the auto-memory `ams2-hub-build-progress.md` and `docs/dev/codex-gui-round3-brief.md`.

---

## CODEX — GUI round 4: finish the bindings, then the ART, then the new immersion surfaces

```
You are the GUI/art lead. Round 3 (cinematic menu + workbench + deep Paddock + tactile theme) is merged into
hub/increment-4 @ efe980a. Read docs/dev/codex-gui-round3-brief.md (esp. its "Task 3 contract" section) and the
auto-memory ams2-hub-build-progress.md TOP block. Branch off the latest hub/increment-4, keep it lane-clean
(src/Companion.App/** only), keep the theme contract + render harness green (add a render test per new view/control),
both light AND dark legible. Round 4:

1. FINISH wiring every Task-3 bind target Claude already shipped (all populated, display-only):
   - Standings: StandingsRow.IsPlayer / IsRival — highlight the player row + the named-rival row (reuse ErrorBrush
     for the rival accent).
   - Calendar: SeasonScheduleEntry.{Championship,GridSize,Dnq(ScheduleDnqEntry),WeatherLabel,SetupNote,Opponents,
     Status(Done/Next/Upcoming)} — a clickable round-detail panel + a done/next/upcoming progress treatment.
   - History: CareerSeasonCard.RoundLines (CareerSeasonRoundLine: Round/Venue/PlayerFinish/RivalName/RivalFinish/
     ChampionAfter/PlayerPointsAfter) — the "my season" per-round almanac breakdown.
   - Driver/Dossier: DossierViewModel.Timeline + NarrativeIntro + HasSmgpNarrative — the evolving story-scroll.
   - Rival screen: SmgpRivalOption.HeadToHead (races met, ahead/behind, best-together, streak) — the deeper dossier.
   - Remove the stale PaddockView.xaml:514 "TODO bind: Sponsors once model.Sponsors is exposed" — PaddockViewModel
     already exposes Sponsors/SelectedSponsor + ViewSponsor/ViewDriver cross-links; wire the Sponsors master–detail.

2. ART — the fidelity ceiling (docs/dev/asset-inventory.md + data/ams2/ART-INVENTORY.md). Priority: cars/<driverId>.png
   (all 34 blank), portrait gaps, smgp/teams/<team>.jpg, smgp/rounds/<round>.jpg, smgp/logos/<teamId>.png,
   smgp/sponsors/<id>.png, and the sealed finale smgp/finale/{special,ultimate}.jpg. Drop into dist/data/ams2/
   (canonical, gitignored on purpose).

3. NEW immersion surfaces — build these when Claude's data lands (Claude round-2 tasks below):
   - an SMGP NEWS TICKER / dispatch cards (reactive per-round stories) + a "paddock rumor" line on the Paddock;
   - a TYCOON dashboard shell for the reserved top-header team mode (read-only: roster / sponsors / budget tier /
     team standing). Stub the layout with FallbackValue + a "<!-- TODO bind: X -->" until the projection ships.

4. Consistency + tooltips pass across every screen; keep it tactile in both themes.
```

---

## CLAUDE — Task 4: SMGP living-world news + between-race immersion (data/VM)

```
Continue the AMS2 Career Companion (Z:\Claude Code\ams2-career-companion, branch hub/increment-4). Read the
auto-memory ams2-hub-build-progress.md TOP block, docs/dev/codex-gui-round3-brief.md, and this file first. You own
the DATA/VM lane (Core/ViewModels/data + tests); do NOT touch src/Companion.App/** (Codex's XAML). Build in slices,
full test + commit + republish RC after each, staying replay-byte-identical (display-only projections over folded
state — never a fold input, like SmgpPaddock / the Task-2/3 projections).

Mike wants "more fun things that fill up time and create immersion." Build a LIVING SMGP WORLD that reacts to the
player's actual career:

1. REACTIVE PER-ROUND DISPATCHES. A display-only SmgpDispatches() projection (on ICareerSession, additive default
   null) that turns the folded results into short in-world news stories — the player's win / podium / first-of-
   something / DNQ, a promotion or demotion, a rivalry earned or lost, a title, a floor near-miss, plus AI-world
   stories (a rival's win streak, a title race tightening, the Senna benchmark). Reuse what exists: SmgpCareerBeats
   (the milestone beats), SmgpLiveStats, CurrentSmgpState().Tallies, the standings snapshots, driver/team profiles.
   Ground the copy in a data corpus (extend data/rules/news/smgp.json or a new smgp-dispatches.json) — templated,
   ASCII, no invented facts. NB the mojibake lesson: any PS-authored JSON must be written UTF8-no-BOM + validated
   via System.Text.Json.

2. ESCALATING RIVAL VOICE + PADDOCK RUMOR. The named rival's trash-talk should escalate with the streak state
   (rival-quotes.json already has first/playerLeads/rivalLeads moods — deepen it); add a rotating "paddock rumor"
   line (seeded, display-only) for the Paddock. A standings-movement story ("X takes P2 off Y").

3. Expose it all as clean projections the GUI binds (a dispatch = {WhenLabel, Headline, Body, Kind, optional
   driver/team art key}). Update the Codex brief's contract + the memory as each property lands.

Tests for every projection (a career with results yields dispatches in order; determinism untouched). Keep the
fold byte-identical.
```

---

## CLAUDE — Task 5: Tycoon Team Mode — the read-only DATA SPINE (data/VM)

```
Continue the AMS2 Career Companion (Z:\Claude Code\ams2-career-companion, branch hub/increment-4). Read the
auto-memory ams2-hub-build-progress.md TOP block + docs/dev/codex-gui-round3-brief.md + this file first. DATA/VM
lane only (Core/ViewModels/data + tests); do NOT touch src/Companion.App/**. Every projection is DISPLAY-ONLY
(never a fold input → replay byte-identical). Build in slices, test + commit + republish RC each.

The app's top header is RESERVED for a future TYCOON TEAM MODE (locked direction), and the sponsor system was
designed to roll into it. Build the READ-ONLY DATA FOUNDATION for it now — a "team dashboard" projection over the
SMGP world, with NO team-management fold mechanics yet (keep it a pure read so it is replay-safe and Codex can
build the dashboard shell):

1. TEAM DASHBOARD projection. A new SmgpTeamDashboard() (on ICareerSession, additive default null; SMGP-only) that,
   for the PLAYER's current team, exposes: the live roster (each driver's season + career line — reuse
   SmgpTeamRosterLine / BuildDriverStats), the team's SPONSORS (from the Task-1 sponsor board — brand colours,
   backing tier, taglines), the team's ladder TIER + palette (TeamPalette), the team's CHAMPIONSHIP standing (from
   StandingsEngine — reuse CurrentStandings + a constructors' view if present), and the team's SMGP-world history
   (SmgpTeamProfiles). 

2. RIVAL TEAMS TABLE. Every SMGP team ranked by prestige/standing with its roster + sponsors + tier colour, so the
   dashboard can show "the grid of teams" — the tycoon's competitive landscape. Reuse SmgpPaddock().Teams and the
   standings.

3. A "team of the season" / budget-tier flavour projection (display-only, derived from prestige + results) as the
   seed of the future economy — clearly labelled as flavour, no real budget model yet.

Expose clean records the GUI binds; update the Codex brief contract + memory as properties land. Tests for every
projection (the dashboard reflects the player's team + its sponsors + standing; determinism untouched). This is
the spine; the actual tycoon fold mechanics (hiring, budget spend, contracts) are a LATER increment — do not add
any fold input in this task.
```
