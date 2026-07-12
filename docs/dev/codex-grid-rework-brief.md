# Codex — HIGH PRIORITY: rebuild the SMGP starting grid as a top-down track (grandstand · grid · pitlane)

**From Mike, via Claude.** This supersedes the current `StartingGridView` "pixel starting straight" for SMGP
careers. It is a GUI + ART task — 100% your lane (`src/Companion.App/Views/StartingGridView.xaml`[.cs], `Themes`,
and the car/track ART under `dist/data/ams2/`). **No Claude/VM change is needed — every value you need is already
on the view model** (see "Data you already have" below).

## The problem (current look)

The SMGP starting grid renders as a vertical strip of driver CARDS with **side-view** cars, flanked by a
repeating tiled texture on the left and pit-box shapes on the right. It reads as flat/repetitive, and the
side-view cars can't sit "on" a grid.

## The target look (Mike's reference)

A **top-down / high-angle view of an actual race-track start**, three bands left→right:

1. **LEFT — grandstands.** A pixel crowd behind catch-fencing along the left edge of the track (spectators,
   railings, a stand roofline). The energetic, packed-stand look from the reference.
2. **CENTER — the track + the grid.** Dark asphalt bordered by **red/white kerbs** on both sides, a **checkered
   start/finish line** across the top, and the **staggered two-file grid** running away from the line down the
   screen: each slot drawn as the classic white **grid-box marking** (the "⊓ / T" bracket), with a **top-down
   car sitting in its box**. P1 leads, P2 is in the other file half a box back, P3 behind P1, etc. — the real
   F1 stagger. Number + a small team-coloured tag per slot; the **player's box highlighted** (accent border,
   "YOU").
3. **RIGHT — the pitlane.** A pit road down the right edge (asphalt with the **yellow pit-lane line**, garage
   boxes / equipment / tyre stacks along the wall), taking up the right band the way the reference shows.

Keep the existing **conditions bar** (lap distance · weather · track/air temp) and the **PIT WALL · POTENTIAL
STRATEGIES** panel and the **DID NOT QUALIFY** strip — dock them above/below the track band; only the grid
straight itself and the car art change.

## The car ART — regenerate all 34 as TOP-DOWN sprites (the core of this)

The cars are currently **side-view** at `dist/data/ams2/cars/<driverId>.png`. Regenerate every car as a
**bird's-eye / top-down F1 sprite** (nose pointing UP the screen, toward the start line), team-liveried to match
each team's colour, consistent canvas size, transparent background, sized to drop into a grid box. Same key
scheme (`cars/<driverId>.png`) so nothing else changes — the view just places `CarKey` in the slot. Do the
player's car too (the grid keys the player's car via `CarKey`, already resolved to the seat they hold). 16-bit
SEGA-arcade style to match the mode. (Portraits stay side/head-on — they're only used elsewhere; the grid can
show a small portrait/flag tag on the slot if you like, but the CAR itself must be top-down.)

## Data you already have (bind to it — no Claude work needed)

`StartingGridViewModel` (`src/Companion.ViewModels/Shell/StartingGridViewModel.cs`):
- `Slots` / `TopRow` (odd positions) / `BottomRow` (even positions) — the staggered two-file layout is already
  split for you. Each `StartingGridSlot`: `Position`, `PositionLabel` ("P4"), `DriverId`, `DriverNameUpper`,
  `TeamName`, `Number`/`NumberLabel` ("#26"), `IsPlayer`, `PortraitKey`, **`CarKey`** (`cars/<driverId>.png`),
  **`TeamColor`** ("#RRGGBB" via TeamPalette — use it to tint the slot / car glow).
- `Conditions` (`GridConditions`: `LapDistanceKm`, `Weather`, `IsWet`, `TrackTempC`, `AirTempC`, …).
- `Dnq` / `HasDnq` / `DnqHeader` — the did-not-qualify strip.
- The national flags you already render stay as-is.

So this is layout + art only. If you decide you *want* a new bindable (e.g. an explicit grid-column/row or a
nationality field), tell Claude and it'll add the projection — but nothing above requires it.

## Constraints

Gate it to SMGP careers exactly as the current grid is (historical careers keep their compact card carousel).
Keep the **theme contract** green (no inline hex in Views — the track/kerb/grandstand/pit textures go in an
invariant-art resource dict or drop-in art; team tint comes from the bound `TeamColor` via `HexBrush`), legible
in **both** themes, add/refresh the `StartingGridRenderTests` render test, lane-clean, branch off the latest
`hub/increment-4` for a reviewed merge. The grandstand/pit/track can be tiled art assets under
`dist/data/ams2/` (canonical) referenced by the view.
```
