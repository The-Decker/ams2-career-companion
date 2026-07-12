# Codex — Mission: Full Theming, Polish & Custom Art (app-wide)

**Updated:** 2026-07-12 · **Author:** Claude · **For:** Codex · **Base branch:** `hub/increment-4`

This extends your standing GUI/art role (`docs/dev/codex-gui-brief.md`) with a bigger mission Mike wants:
**a real theming system, a polish pass over every SMGP screen, custom art, and a font upgrade.**

Your four missions:
1. **A user-selectable THEME SYSTEM** — light + dark + a set of accent colours (the headline).
2. **Polish EVERY SMGP screen** — rival, grid, paddock, promotion, hub, briefing, wizard, settings.
3. **Custom art** — finish the inventory (`docs/dev/asset-inventory.md`) with your own art.
4. **Fonts** — a proper type system (see §4; Claude supplies the recommended open-license set).

Same lanes as always (Views XAML + **resource dictionaries** + styles + art). Build + test green floor:
**1943 unit + 52 render.** Verify on an SMGP career.

---

## 1. The THEME SYSTEM — light / dark / accents (headline)

Today `src/Companion.App/Themes/Theme.xaml` is a single dark palette. Every screen binds to its brush
**keys** — `BgBrush`, `SurfaceBrush`, `SurfaceAltBrush`, `AccentBrush`, `AccentDimBrush`, `EdgeBrush`,
`TextMutedBrush`, `SuccessBrush`, `HexBrush`, etc. Those keys are the **theme contract**.

**Design goal:** the player picks a **base theme** (Light / Dark, maybe "System") **and an accent colour**
from a set, in Settings, and the whole app recolours live.

**Your part (design + resources):**
- Author **`Themes/Theme.Dark.xaml`** and **`Themes/Theme.Light.xaml`** — each defines the FULL set of
  brush keys above (same keys, different values) so any screen works under either. Make both genuinely
  good (a real light theme, not an inverted dark one — proper contrast, elevation, legibility).
- Author an **accent set** — e.g. `Themes/Accents/Accent.<name>.xaml`, each overriding just the accent
  brushes (`AccentBrush`, `AccentDimBrush`, and anything accent-derived). Give Mike a strong spread:
  **SMGP Red, Gold, Teal, Royal Blue, Green, Magenta, Orange** (name them, pick great hues that read on
  BOTH light and dark bases). Keep them accessible (WCAG-ish contrast on text-on-accent).
- Keep everything **binding-driven** — never hardcode a colour in a screen; if a screen needs a new
  semantic colour, add a KEY to all theme dicts, don't inline it.

**Claude's part (infra — request it, don't build it):** the runtime dictionary swap (merge selected
base + accent into `App.Resources`) + the **Settings** theme/accent picker + persistence. Leave a note in
§5 with the exact key contract you settle on (the list of brush keys each theme must define) and I'll wire
the selector + swap service against it. Design first; I'll make it switchable.

**Team colours** stay separate: they come from `TeamPalette` / the coming `team-colors.json`, not the
theme accent — a Madonna card is red regardless of the app accent.

## 2. Polish every SMGP screen

A consistency + beauty pass across **rival, starting grid, paddock, promotion, hub, briefing, wizard,
settings**. Shared type scale + spacing, consistent card / stat-tile / chip styling, hover/selected
states, and everything must look right under **all** themes + accents (test light + dark + 2 accents).
Fold repeated markup into shared styles/templates. Watch render cost.

## 3. Custom art — finish the inventory

Work `docs/dev/asset-inventory.md` to zero. Current top gaps: **team logos (24)**, team photos (24),
round cards (16), banners (24). Style = 16-bit SEGA arcade (see the inventory for paths/sizes).
⚠ **Art-location decision is pending with Mike** — several assets currently live only in `dist/data/ams2/`
(gitignored = not version-controlled). Don't add more art until Claude + Mike settle whether art is
committed to tracked `data/ams2/` (safe, reproducible) or kept in `dist` (e.g. for licensing of
AMS2-derived files). Hold new art drops on that call.

## 4. Fonts — the type system (researched; all OFL, safe to bundle)

The recommended **primary stack** (a sim-racing / esports-HUD system):
- **Display** → **Orbitron** — the closest match to the SEGA Super Monaco GP arcade marquee: wide,
  geometric, caps-first. Owns the wordmark, screen titles, standings/leaderboard banners, card headers.
- **Body / UI** → **Inter** — carries every stat, table and label; tall x-height, tabular figures that
  align number columns at 11-13px, disambiguated I/l/1. (Upgrade over the current Roboto body.)
- **Pixel** → **Press Start 2P** — authentic 16-bit coin-op flavour for badges + grid/position numbers
  ONLY, at 8px multiples, ≤3 glyphs (never body). Complements the existing `MicrosportFont`.
- **Mono / tabular** → **JetBrains Mono** — lap times, gaps, telemetry; fixed-width digits, slashed zero.
  Replaces the scattered `FontFamily=Consolas` number cells.

**Alternative combos** (Mike may prefer one): *clean-modern* = **Chakra Petch** (display) + **Saira** (body)
— F1-broadcast timing-tower, and Chakra Petch ships static weights (no WPF caveat); *hardcore-pixel* =
**Press Start 2P** + **Silkscreen** for an optional "arcade mode" (pixel-on-pixel — reserve for chrome,
not dense tables).

⚠ **WPF gotcha:** Orbitron / JetBrains Mono / Saira are **variable** fonts — classic WPF/DirectWrite only
renders the default ~400 weight. Claude will bundle **static instances** (specific Bold/Black weights, or
OFL static builds) into `src/Companion.App/Fonts/`. Once the files land + the picks are locked, you:
declare `FontFamily` keys in the theme dicts (like `MicrosportFont` → `/Fonts/#Microsport`) and apply the
hierarchy across screens. Fonts are **theme-agnostic** (they don't change with light/dark). Confirmed:
Claude can fetch these (test-downloaded Press Start 2P + Chakra Petch OK).

---

## 5. Requests to Claude (leave notes here)

- **Theme contract:** the final list of brush (+ font) keys each theme dict must define, so I can build
  the swap service + Settings picker against it.
- **Anything else** needing a VM/settings/property.
