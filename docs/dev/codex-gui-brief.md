# Codex Brief — GUI + Art Expert (SMGP screens)

**Date:** 2026-07-11 · **Author:** Claude · **For:** Codex · **Branch base:** `hub/increment-4`

Mike wants you (Codex) to take the **GUI-polish + art** lane for a while — enlarging and
beautifying the SMGP screens and producing the pixel-art assets — while Claude keeps building SMGP
**features + logic**. This brief is everything you need to work independently without colliding.

Short answer to Mike's question ("can Codex do GUI work? it can make art?"): **yes.** The screens are
WPF/XAML (declarative, well-structured, DataContext already wired), so resizing/reflowing is
self-contained XAML work. Art is drop-in files on disk (no code) — the app already loads them by key.
The one rule: **stay in the View/art lane** (below), because Claude is simultaneously changing the
ViewModels + data behind these same screens.

---

## 1. Lane boundaries — READ FIRST (avoid collisions)

Two agents, one repo. Ownership for this phase:

| You (Codex) own | Claude owns (do NOT edit) |
|---|---|
| `src/Companion.App/Views/**.xaml` (layout, sizes, fonts, colours, styles) | `src/Companion.App/Views/**.xaml.cs` code-behind logic *(ask before touching)* |
| `src/Companion.App/**/Styles*.xaml`, resource dictionaries, converters styling | `src/Companion.ViewModels/**` (all ViewModels, records, keys) |
| `dist/data/ams2/portraits/**`, `dist/data/ams2/cars/**`, `dist/data/ams2/smgp/**` (canonical ART files) | `src/Companion.Core/**`, `src/Companion.Data/**` (domain/sim/fold) |
| `docs/` GUI notes | `data/rules/smgp/**` (driver-profiles, team-profiles, stats — *data*) |

- If a GUI change needs a **new bound property or a rename**, DON'T add it yourself — leave a note in
  this file's "Requests to Claude" section (bottom) and Claude wires it. This keeps the VMs single-owner.
- Work on a branch off `hub/increment-4` and PR/hand back; Claude will be committing to the same base,
  so keep your diffs to Views/art and rebase before merge.
- **Do NOT touch:** `StartingGridViewModel.cs`, `HomeViewModel.cs`, `CareerSessionService.cs`,
  `RivalScreenViewModel.cs`, `BriefingViewModel.cs`, `RoundGridResolver.cs`, `TeamPalette` — all in flight.

---

## 2. The task list

### Task A — Rival screen: make it BIG (Mike: circled the whole card, wrote "BIGGER")

File: **`src/Companion.App/Views/RivalScreenView.xaml`**. It's a shared card that also renders inside
`BriefingView.xaml` — change RivalScreenView only. Current → target (tune to taste, these are floors):

| Element | XAML anchor | Now | Target |
|---|---|---|---|
| Rival **portrait** | `Grid Width="200" Height="200"` (~L69) | 200×200 | **~360–440**, a hero image |
| **Car preview** | `Image Height="50"` (~L82) | 50 | **~150 (300%)** |
| Driver **name** | `FontSize="17"` (~L87) | 17 | **~26–30** |
| MACHINE line / quote / ladder line | ~L92–112 | ~13 | **+40–50%** |
| **Stat panel** (car spec) | `<views:CarSpecCard MaxWidth="280"/>` (~L102) | 280 | **~400–460** |
| Team **banner** | `Image MaxHeight="52"` (~L65) | 52 | **~90–110** |
| Round card art | `Image MaxHeight="120"` (~L35) | 120 | ~180 |

Notes: it lives in a `ScrollViewer` (L22) so growing it is safe. The card is `Border Style="{StaticResource
Panel}"` — consider a heavier hero treatment (more padding, larger corner radius) now that it's the
screen's centrepiece. The CarSpecCard bar rows can scale up too (`Views/CarSpecCard.xaml`).

### Task B — "Continue" button: put it BELOW "YES — name him as my rival" (Mike: arrow from bottom-right up under YES)

Same file. Today the primary Continue button is:
```xml
<Button DockPanel.Dock="Bottom" ... HorizontalAlignment="Right" ...  <!-- ~L16–20 -->
```
i.e. bottom-right of the whole screen. Mike wants it **directly under the YES button**, centred.

- Move it out of the bottom dock into the content `StackPanel`, **centred, directly beneath the rival
  dossier `Border`** (after ~L131, still inside the `DataContext="{Binding Briefing}"` panel or just
  outside it — keep the same `Command`/`Content` bindings to `HomeView`, they work anywhere in the tree).
- **UX constraint:** Continue must stay reachable when **no rival is selected** (the dossier card
  collapses then). So place it in the *outer* StackPanel below the card, always visible — when a rival
  IS selected it naturally sits just under YES; when not, it sits under the pick row. Keep the "No rival"
  button working. Verify: pick a rival → Continue under YES; pick "No rival" → Continue still visible.

### Task C — Starting grid: SMGP pixel starting straight (historical grid otherwise unchanged)

**Mike update, 2026-07-11:** the earlier oversized-card direction is superseded. This treatment is
**SMGP-only**. Historical careers retain the original compact two-row card carousel.

File: **`src/Companion.App/Views/StartingGridView.xaml`**.

- Gate the alternate surface from the canonical mode identity:
  `HomeView.DataContext.Session.Pack.Manifest.CareerStyle == SmgpRules.CareerStyle`.
- Recreate the supplied overhead pixel-racing reference as a vertically scrolling starting straight:
  speckled asphalt, grass verges, red/white kerbs, fence rails, checker line, and two grid bays per row.
- Each bay shows position, number, portrait, driver/team, an unmistakable `YOU` state, and a miniature
  of the exact car that driver uses.
- Prefer `smgp/grid-cars/<driverId>.png`; fall back to the canonical side preview in
  `cars/<driverId>.png`, so all 34 cars render immediately while bespoke overhead sprites are produced.
- Leave the historical grid visuals, dimensions, arrows, and stagger unchanged.

**Fuel gauge removal — ✅ DONE by Claude** while restructuring the bottom strip for the dynamic DNQ
display. The fuel readout is gone in every mode; `FuelLabel`/`FuelPct` on `GridConditions` are now unused
and should remain untouched.

Claude's player-car fix remains the data contract: the synthetic player entrant resolves the authored
driver whose car the player took over, so both the fallback preview and a future overhead sprite represent
the player's actual machine.

### Task D — Rival live STATS readout (✅ landed by Claude)

The rival screen's former `"0 D.P."` line has been replaced by `SmgpSeasonLine` and `SmgpCareerLine`.
Preserve those bindings while styling the surrounding dossier.

---

## 3. Art assets — how the app loads them (drop-in, no code)

The app resolves images by **key** from `{exe}\data\ams2\<kind>\<key>.{jpg|jpeg|png}` (the
`KeyedAssetImageConverter`, `ConverterParameter=<kind>`; multiple folders may be `|`-separated for
fallback, e.g. `portraits|cars`). In this repo, **`dist/data/ams2/` is the canonical art root and
anything there wins**. Drop a correctly-named file there and it appears — no rebuild. Never copy a
tracked `data/ams2/` art file over its `dist` counterpart.

| Asset | Folder (`<kind>`) | Key | Example file | Used by |
|---|---|---|---|---|
| Driver **portrait** | `portraits` | `<driverId>` | `portraits/driver.ayrton_senna.jpg` | grid, rival, dossier, driver tab |
| Driver **car** (side profile) | `cars` | `<driverId>` | `cars/driver.ayrton_senna.png` | grid, rival |
| SMGP **grid car** (optional overhead) | `smgp/grid-cars` | `<driverId>` | `smgp/grid-cars/driver.ayrton_senna.png` | SMGP pixel starting grid; falls back to `cars` |
| **Player** image (per team) | `portraits` | `player.<team>` | `portraits/player.madonna.jpg` | player's grid/dossier card |
| Team **banner** | `smgp/banners` | `<teamId>` | `smgp/banners/team.madonna.png` | rival dossier header |
| Team **photo** (big) | `smgp/teams` | `<team>` (no `team.`) | `smgp/teams/madonna.jpg` | promotion screen |
| **Round** card | `smgp/rounds` | `<roundNumber>` | `smgp/rounds/1.jpg` | rival/briefing |
| Team **logo/icon** (NEW) | `smgp/logos` | `<teamId>` | `smgp/logos/team.madonna.png` | driver/team tab (item 6) — *new convention, use this* |

- Full roster: **34 drivers**, **24 teams** — ids in `packs/smgp-1/drivers.json`, `teams.json`,
  `entries.json` (driver↔team↔number↔livery). Names/personalities in `data/rules/smgp/team-profiles.json`
  (24 teams, motto + history + quotes) and, soon, `data/rules/smgp/driver-profiles.json` (bios Claude is
  authoring now).
- **Style:** match the existing arcade look in the screenshots — **16-bit SEGA "Super Monaco GP" pixel/
  painted portraits**, cars as clean side-profile arcade renders in team colours. Portraits ~square
  (grid/rival crop `UniformToFill`); cars transparent PNG, side-on.
- **Canonical `dist` inventory:** the 34 driver portraits, 34 car side-profiles, and all 24
  `player.<team>` images are complete. Next priority is the 24 team logos after Mike supplies the
  final palette, then 34 optional overhead grid-car sprites, team photos, and round cards.
  `smgp/banners` remain optional. Grid-car sprites should be 384×256 transparent RGBA, consistently
  framed with the nose pointing right, driver-keyed (not merely team-keyed), and preserve each
  preview's livery and car number.

---

## 4. Team colours — the data file (Mike is providing hexes "at a later prompt")

Grid + rival card accents currently come from `TeamPalette.For(teamId)` in ViewModels (curated SMGP
hexes + a hue fallback). **Claude will externalize this to `data/ams2/smgp/team-colors.json`** so Mike
can drop his exact per-team hexes in without a rebuild. **Don't hardcode colours in XAML** — keep binding
`{Binding TeamColor, Converter={StaticResource HexBrush}}`; the values will flow from that file. When Mike
sends the palette, Claude wires the file; your cards pick it up automatically.

---

## 5. Build & verify

- Solution: `Companion.slnx` (.NET 10 XML solution — no `.sln`). `dotnet build` / `dotnet test` from repo root.
- There are **render-harness tests** (`tests/Companion.RenderHarness.Tests`) that snapshot these screens —
  `StartingGridRenderTests`, `RivalRenderTests`/`PromotionRenderTests`. Run them after XAML changes; update
  the expected renders where the change is intentional. Full suite floor: **1912 unit + 49 render green.**
- Prefer verifying visually by running the app (`/run` or the RC in `dist/`) on an SMGP career, San Marino
  Round 1 — that's the screen in Mike's screenshots.

---

## 6. Requests to Claude (leave notes here; Claude wires the VM/data side)

- **SMGP player auto-scroll:** after `SmgpGridScroll` completes layout, scroll vertically to the slot
  whose existing `IsPlayer` flag is true so a player starting deep in the 34-car field is not hunting
  for the `YOU` bay. `Slots` already exposes the target; no new VM property is required. Keep the
  historical `GridScroll`/`PageStep` behavior unchanged.
- **Canonical-art preservation:** release/build work must preserve `dist/data/ams2/**` byte-for-byte.
  Do not add copy globs that overwrite canonical `dist` art from tracked `data/ams2` files. If a
  secondary archive is needed, it must be derived outward from `dist`, never synced into it.
