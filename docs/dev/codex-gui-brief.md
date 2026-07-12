# Codex — Standing Brief: GUI + Art Expert (SMGP)

**Updated:** 2026-07-12 · **Author:** Claude · **For:** Codex · **Base branch:** `hub/increment-4`

Mike has made you the **GUI + art lead** for the SMGP mode — you've been doing a good job, so this is now
a standing role, not a one-off. Claude keeps building SMGP **features + logic**; you own how it all
**looks** and the **art assets**. This brief is the mission; work through it, and keep it + the asset
inventory current as you go.

Your four jobs, in priority order:
1. **Fill the missing art** — see `docs/dev/asset-inventory.md` (133 assets missing; **cars/** is the big one).
2. **Refresh the starting-grid view** — make it the cinematic screen from Mike's AAA mockup.
3. **Enhance the Paddock view** — make the driver/team preview beautiful.
4. **Rival screen + a consistency/optimization pass** across every screen.

---

## 0. How to work (read once)

- Branch off `hub/increment-4`; keep diffs to **Views XAML + styles + art**; rebase before handing back
  (Claude commits to the same base). Run `dotnet build` / `dotnet test` from the repo root
  (`Companion.slnx`, .NET 10 — no `.sln`). Floor to stay green: **1943 unit + 51 render**.
- Verify visually by running the app (the RC in `dist/`, or `dotnet run`) on an **SMGP career** — that's
  where all these screens live. San Marino · Round 1 is the canonical test screen.
- The **render-harness tests** (`tests/Companion.RenderHarness.Tests`) snapshot these screens
  (`StartingGridRenderTests`, `PaddockRenderTests`, `RivalRenderTests`/`BriefingSmgpRenderTests`,
  `PromotionRenderTests`). Run them after XAML changes; update expected renders where the change is
  intentional.

### Lane boundaries — CRITICAL (Claude is editing the code behind these screens)

| You (Codex) own | Claude owns — do NOT edit |
|---|---|
| `src/Companion.App/Views/**.xaml` (layout, size, font, colour, style) | `src/Companion.ViewModels/**` (ALL ViewModels, records, keys) |
| resource dictionaries / `Styles*.xaml` / converters styling | `src/Companion.Core/**`, `src/Companion.Data/**` |
| `data/ams2/**` (ALL art files) | `data/rules/smgp/**` (bios, stats, profiles — data) |
| `docs/dev/asset-inventory.md` (keep it current) | `TeamPalette`, `SmgpPaddock`, live-stats, DNQ code |

If a visual change needs a **new bound property / rename / a data field**, DON'T add it — drop a line in
"§6 Requests to Claude" at the bottom and Claude wires it. Single-owner VMs prevent collisions.

---

## 1. Missing art — `docs/dev/asset-inventory.md`

That file has the full audited list (what the app expects, what's present, what's missing) + drop paths,
sizes and style. **Style = 16-bit SEGA "Super Monaco GP" arcade** — the 34 driver portraits already in
`data/ams2/portraits/` are the reference. Priority: **cars/ (34, all missing)** → `player.<team>` (11) →
team logos (24) → team photos (24) → round cards (16) → banners (24). Drop files into
`data/ams2/<kind>/` **and** `dist/data/ams2/<kind>/`; tick them off in the inventory.

---

## 2. Refresh the STARTING GRID — `src/Companion.App/Views/StartingGridView.xaml`

State now: a staggered two-row carousel of team-coloured driver+car cards (264px wide), a top conditions
bar, a bottom conditions strip, and a new **"DID NOT QUALIFY"** chip strip (the dynamic DNQ field — 8-9
cars rotate out each race; fuel gauge already removed). Mike wants this to become the **cinematic** screen
from his mockup. Do:
- **Bigger, richer cards** — card ~264→**~380-440** wide; portrait+car row ~112→**~180-210** tall;
  position box 44→~60; driver font 15→~20, team 11.5→~14. Scale the back-row stagger offset (`Margin="139…"`)
  to ≈ half the new width.
- **Make the player unmistakable** — their card already gets an accent border (`IsPlayer` trigger); add a
  **"YOU"** badge, and **auto-scroll the carousel to the player's card** on load (code-behind
  `OnScrollLeft/Right` exists). *If you need the player's grid index for the scroll, request a VM property.*
- **Polish the DNQ strip** — it's functional grayed chips; make it read as "these didn't make the cut"
  (subtle, secondary), and make the rotation feel meaningful.
- Cinematic chrome: darker hero background, stronger team-colour accents (via the existing
  `{Binding TeamColor, Converter={StaticResource HexBrush}}` — see §5, don't hardcode).

## 3. Enhance the PADDOCK — `src/Companion.App/Views/PaddockView.xaml`

State now: a DRIVERS/TEAMS toggle over a master list (left) + a dossier detail (right). Driver dossier =
portrait + car + epithet + a live "THIS SEASON" line + a **CAREER** stat-tile row (TITLES/WINS/PODIUMS/
POLES/TOP-5/POINTS/STARTS) + 3-paragraph bio + quotes; the **player leads the roster** with their own
card. Team dossier = logo + motto + roster + history + quotes. It's **functional but plain** — make it
beautiful:
- Team-colour the driver/team cards + the stat tiles + the selected-row highlight (via `TeamColor`, §5).
- Make the stat tiles feel like a **record book** (bigger numbers, iconography, maybe a small bar/sparkline).
- Richer master list rows (portrait + a hint of their record), a stronger dossier hero (big portrait + car).
- The team logo (`smgp/logos`) becomes clickable/prominent per Mike's "click the team icon" ask.

## 4. Rival screen + consistency pass

**Rival screen** (`RivalScreenView.xaml`) — still small. Portrait 200→**~400**, car preview 50→**~150**,
fonts + the car-spec stat panel up, whole card a hero. Move the **Continue** button to sit **centred
directly below "YES — name him as my rival"** (keep it reachable when no rival is picked). The top-right
readout is now the player's live **SEASON / CAREER stats** (Claude replaced "D.P.") — you may style that
container, but leave its text/bindings to Claude.

**Consistency / optimization** — a pass across all SMGP screens (rival, grid, paddock, promotion, hub):
shared type scale + spacing, consistent card/stat-tile styling, light/dark theming sanity, and trimming
any layout that's heavy to render. Fold repeated card/stat-tile markup into shared styles/templates.

---

## 5. Team colours — `data/ams2/smgp/team-colors.json` (coming)

Card accents come from `TeamPalette.For(teamId)` (curated hexes + a hue fallback) via
`{Binding TeamColor, Converter={StaticResource HexBrush}}`. **Never hardcode colours in XAML** — keep
binding `TeamColor`. Mike is sending exact per-team hexes; Claude will externalise `TeamPalette` to
`data/ams2/smgp/team-colors.json` so they flow in without a rebuild. Your cards pick them up automatically.

---

## 6. Requests to Claude (leave notes; Claude wires the VM/data side)

- _e.g. "need `StartingGridViewModel.PlayerIndex` to auto-scroll the carousel to the player"_
- _e.g. "need a `bool IsChampion` on the driver card to gild the title tile"_
