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
| `data/ams2/portraits/**`, `data/ams2/cars/**`, `data/ams2/smgp/**` (ART files) | `src/Companion.Core/**`, `src/Companion.Data/**` (domain/sim/fold) |
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

### Task C — Starting grid: make cards MUCH bigger + remove the fuel gauge

File: **`src/Companion.App/Views/StartingGridView.xaml`**.

| Element | XAML anchor | Now | Target |
|---|---|---|---|
| **Grid card** width | `Border Width="264"` (~L12) | 264 | **~380–440** |
| Portrait+car row height | `Grid Height="112"` (~L42) | 112 | **~180–210** |
| Position box | `Width="44" Height="44"` (~L27) | 44 | **~60** |
| Position font | `FontSize="21"` (~L30) | 21 | ~28 |
| Driver name | `FontSize="15"` (~L33) | 15 | ~20 |
| Team name | `FontSize="11.5"` (~L35) | 11.5 | ~14 |
| Back-row stagger offset | `Margin="139,4,0,0"` (~L144) | 139 | **≈ half the new card width** |
| Card margins / carousel arrow hit-size | L12, L149–154 | — | scale to match |

**Fuel gauge removal — ✅ DONE by Claude** (while restructuring the bottom strip for the DNQ display,
commit after `f1f4ec2`). The fuel readout is gone; the `FuelLabel`/`FuelPct` on `GridConditions` are now
unused (harmless — leave the model alone). Nothing for you to do here.

**Player card is now correct data-side.** Claude fixed the bug where the player's card showed a blank car
(it keyed art off the synthetic `driver.player-entrant` id). The player's card now resolves the **actual
car they took over**, so once you enlarge the car preview it will render. Two GUI asks while you're here:
- Make the player's card unmistakable — it already gets an accent border (`IsPlayer` trigger, ~L17–20);
  consider a "YOU" tag on the position box or a glow.
- Consider **auto-scrolling the carousel to the player's card** on load (code-behind `OnScrollLeft/Right`
  pattern exists) so the player isn't hunting for themselves down a 20-car grid. *(If this needs a VM
  signal for "which index is the player", request it below — don't reach into the VM.)*

### Task D — "0 D.P." readout → becoming a STATS readout (Claude owns; don't restyle the content yet)

The rival screen's top-right `"0 D.P."` (bound `SmgpPointsLine`) is being **replaced by Claude** with a
driver-stats readout (career wins / points / poles / top-5s, per Mike). **Leave the text/binding to
Claude.** You may style the *container* (it'll grow into a small stat strip) once Claude lands the new VM
shape — coordinate before restyling this specific region.

---

## 3. Art assets — how the app loads them (drop-in, no code)

The app resolves images by **key** from `{exe}\data\ams2\<kind>\<key>.{jpg|jpeg|png}` (the
`KeyedAssetImageConverter`, `ConverterParameter=<kind>`; multiple folders may be `|`-separated for
fallback, e.g. `portraits|cars`). Drop a correctly-named file and it appears — no rebuild.

| Asset | Folder (`<kind>`) | Key | Example file | Used by |
|---|---|---|---|---|
| Driver **portrait** | `portraits` | `<driverId>` | `portraits/driver.ayrton_senna.jpg` | grid, rival, dossier, driver tab |
| Driver **car** (side profile) | `cars` | `<driverId>` | `cars/driver.ayrton_senna.png` | grid, rival |
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
- **Priority for art:** (1) the 34 driver portraits, (2) the 34 car side-profiles, (3) the 24 team
  logos, (4) `player.<team>` images (Mike noted ~11 still missing), (5) team photos for the promotion
  screen. `smgp/banners` optional.

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

- _(e.g. "need `StartingGridViewModel.PlayerIndex` so the carousel can auto-scroll to the player")_
- _(e.g. "need a bound `bool ShowFuel` if the fuel removal should be a toggle instead of a delete")_
