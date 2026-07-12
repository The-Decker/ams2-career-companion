# Codex — Standing Brief: GUI + Art Expert (SMGP)

**Updated:** 2026-07-12 · **Author:** Claude + Codex · **For:** Codex · **Base branch:** `hub/increment-4`

Mike has made you the **GUI + art lead** for the SMGP mode. Claude keeps building SMGP **features +
logic**; you own how it all **looks** and the **art assets**. Keep this brief and the asset inventory
current as the work advances.

**Round 1 merged (`bd5b01c`, 2026-07-12):** ✅ SMGP pixel starting straight and ✅ hero-sized rival
screen with Continue below “YES”. The next grid refinement places each portrait beside its exact car
and adds the driver’s national flag, name, team, position, number, and `YOU` state.

Your jobs now, in priority order:

1. **Fill the remaining art** — see `docs/dev/asset-inventory.md`. Portraits, car previews, player
   images, and SMGP driver flags are complete in canonical `dist`; team logos are the next large gap.
2. **Enhance the Paddock view** (§3) — make the driver/team preview beautiful.
3. **Run a consistency / optimization pass** (§4) across every SMGP screen.

---

## 0. How to work (read once)

- Branch off `hub/increment-4`; keep diffs to **Views XAML + styles + art**; rebase before handing back
  because Claude commits to the same base. Run `dotnet build` / `dotnet test` from the repo root
  (`Companion.slnx`, .NET 10 — no `.sln`). Current green floor: **1943 unit + 52 render**.
- Verify visually on an **SMGP career**. San Marino · Round 1 is the canonical test screen.
- The render harness (`tests/Companion.RenderHarness.Tests`) exercises these screens. Run it after XAML
  changes and strengthen structural/layout assertions when the change is intentional.
- **`dist/data/ams2/` is the canonical art source. Anything there is law.** Never overwrite it from
  tracked `data/ams2`; any archive or reference copy must flow outward from `dist` byte-for-byte.

### Lane boundaries — CRITICAL (Claude is editing the code behind these screens)

| You (Codex) own | Claude owns — do NOT edit |
|---|---|
| `src/Companion.App/Views/**.xaml` (layout, size, font, colour, style) | `src/Companion.ViewModels/**` (ALL ViewModels, records, keys) |
| resource dictionaries / `Styles*.xaml` / converters styling | `src/Companion.Core/**`, `src/Companion.Data/**` |
| `dist/data/ams2/**` (canonical art) | `data/rules/smgp/**` (bios, stats, profiles — data) |
| GUI/art docs and inventories | `TeamPalette`, `SmgpPaddock`, live-stats, DNQ code |

If a visual change needs a **new bound property / rename / data field**, do not add it. Leave a line in
“§6 Requests to Claude” and let the VM owner wire it.

---

## 1. Remaining art — `docs/dev/asset-inventory.md`

That file is the audited list of what the app expects, what canonical `dist` contains, and what remains.
The current completed SMGP fundamentals are:

- 34/34 driver portraits
- 34/34 side-profile car previews
- 24/24 per-team player images
- 34/34 driver-keyed national flags

Next priority: team logos, team photos, round cards, and optional banners. Purpose-built overhead/three-
quarter grid cars remain an optional upgrade because the complete canonical side previews are the live
fallback.

---

## 2. SMGP STARTING GRID — `src/Companion.App/Views/StartingGridView.xaml`

The SMGP-only surface is a vertically scrolling pixel starting straight. Historical careers retain the
original compact two-row carousel.

- Gate from the canonical identity:
  `HomeView.DataContext.Session.Pack.Manifest.CareerStyle == SmgpRules.CareerStyle`.
- Preserve the pixel scenery: asphalt, grass verges, red/white kerbs, fence rails, checker line, and
  two grid bays per row.
- Each bay shows position, car number, portrait **directly beside** the exact car, national flag,
  driver name, team name, team-colour edge, and an unmistakable `YOU` state.
- Prefer `smgp/grid-cars/<driverId>.png`; fall back to `cars/<driverId>.png` so the 34 canonical car
  previews work immediately.
- Driver flags resolve from `smgp/flags/<driverId>.png`. The synthetic player hides the flag because
  the character model does not author nationality; never show the replaced AI driver’s flag as theirs.
- Do not invent qualifying times or deltas: the result model stores qualifying order only.
- Keep the seeded DNQ strip, live conditions, global fuel removal, and historical grid behavior intact.

---

## 3. Enhance the PADDOCK — `src/Companion.App/Views/PaddockView.xaml`

State now: a DRIVERS/TEAMS toggle over a master list (left) + dossier detail (right). Driver dossier =
portrait + car + epithet + a live “THIS SEASON” line + a CAREER stat-tile row + bio + quotes. Team
dossier = logo + motto + roster + history + quotes.

- Team-colour driver/team cards, stat tiles, and selected rows through the existing `TeamColor` binding.
- Make stat tiles feel like a record book: stronger numbers and hierarchy.
- Enrich master rows and strengthen the dossier hero with portrait + car.
- Make the team logo (`smgp/logos`) prominent once the final logo/palette art lands.

---

## 4. Rival screen + consistency pass

The rival hero sizing and Continue placement landed in Round 1. Preserve `SmgpSeasonLine` and
`SmgpCareerLine` while refining the surrounding presentation.

Run a consistency pass across rival, grid, paddock, promotion, and hub: shared type scale + spacing,
consistent card/stat-tile styling, light/dark theme sanity, and no unnecessarily expensive layout.

---

## 5. Team colours — `data/ams2/smgp/team-colors.json` (coming)

Card accents currently come from `TeamPalette.For(teamId)` via
`{Binding TeamColor, Converter={StaticResource HexBrush}}`. Never hardcode team colours in XAML. Mike’s
exact palette should flow through Claude’s eventual externalized data file without view changes.

---

## 6. Requests to Claude (leave notes; Claude wires the VM/data side)

- **SMGP player auto-scroll:** after `SmgpGridScroll` lays out, scroll to the existing slot whose
  `IsPlayer` flag is true. No new VM property is required; keep historical carousel behavior unchanged.
- **Player nationality, only if Mike wants it:** add nationality to the character/profile projection and
  then expose it to the starting-grid slot. Do not reuse the donor/replaced AI driver’s nationality.
