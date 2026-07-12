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
**1977 unit + 67 render.** Verify on an SMGP career.

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

Work `docs/dev/asset-inventory.md` to zero. The 24 team logos are complete, and Mike's first canonical
promotion-photo batch brings team photos to 15/24. Current gaps: **team photos (9)**, round cards (16),
and optional banners (24). Style = 16-bit SEGA arcade (see the inventory for paths/sizes).
✅ **Art-location decision (Mike, 2026-07-12): `dist/data/ams2/` IS canonical.** That's where Mike drops
his art, and it's where you add yours — proceed. It's gitignored on purpose (some assets are AMS2-derived).
Add art directly to `dist/data/ams2/<kind>/`, keep the inventory ticked off, and don't copy it back into
tracked `data/ams2/`.

## 4. Fonts — the type system (researched; all OFL, safe to bundle)

The recommended **primary stack** (a sim-racing / esports-HUD system):
- **Display** → **Orbitron** — the closest match to the SEGA Super Monaco GP arcade marquee: wide,
  geometric, caps-first. Owns the wordmark, screen titles, standings/leaderboard banners, card headers.
- **Body / UI** → **Inter** — carries every stat, table and label; tall x-height, tabular figures that
  align number columns at 11-13px, disambiguated I/l/1. (Upgrade over the previous Roboto body.)
- **Pixel** → **Press Start 2P** — authentic 16-bit coin-op flavour for badges + grid/position numbers
  ONLY, at 8px multiples, ≤3 glyphs (never body).
- **Mono / tabular** → **JetBrains Mono** — lap times, gaps, telemetry; fixed-width digits, slashed zero.
  Replaces the scattered `FontFamily=Consolas` number cells.

**Alternative combos** (Mike may prefer one): *clean-modern* = **Chakra Petch** (display) + **Saira** (body)
— F1-broadcast timing-tower, and Chakra Petch ships static weights (no WPF caveat); *hardcore-pixel* =
**Press Start 2P** + **Silkscreen** for an optional "arcade mode" (pixel-on-pixel — reserve for chrome,
not dense tables).

✅ **The fonts are already bundled** — Mike approved the set, and Claude downloaded them (static instances,
so `FontWeight` works despite the variable-font/WPF caveat) into `src/Companion.App/Fonts/` and added them
to the csproj as `<Resource>` (see `Fonts/LICENSES.md` — all SIL OFL). Files: `Orbitron-{Bold,Black}`,
`Inter-{Regular,SemiBold,Bold}`, `JetBrainsMono-{Regular,Bold}`, `PressStart2P-Regular`, plus the alts
`ChakraPetch-Bold`, `Saira-{Regular,SemiBold}`, `Silkscreen-Regular`.

✅ **Codex implementation:** the internal family names are verified by render tests and the hierarchy is
applied app-wide: Orbitron titles, Inter body/stats, Press Start 2P short badges, and JetBrains Mono
numbers. Fonts are **theme-agnostic** (they don't change with light/dark).

---

## 5. Requests to Claude (leave notes here)

### Theme/runtime contract settled by Codex

`Themes/Theme.xaml` remains the compatibility facade loaded by `App.xaml`. Its merged dictionaries are:

1. `Theme.Dark.xaml` (replace with `Theme.Light.xaml` for the selected base),
2. the matching `Accents/<Dark|Light>/Accent.<name>.xaml`, and
3. stable `Smgp.Track.xaml` art.

Keep the facade, fonts, converters, styles, templates, and SMGP art dictionary alive. Preload the new base
and accent, then replace the two stored palette slots together on the UI dispatcher; do not clear the whole
merged collection. All switchable consumers now use `DynamicResource`, so existing controls update live.

Every base dictionary defines this exact typed contract:

```text
BgBrush                    SolidColorBrush
SurfaceBrush               SolidColorBrush
SurfaceAltBrush            SolidColorBrush
FieldBrush                 SolidColorBrush
EdgeBrush                  SolidColorBrush
EdgeStrongBrush            SolidColorBrush
TextBrush                  SolidColorBrush
TextSecondaryBrush         SolidColorBrush
TextMutedBrush             SolidColorBrush
TextFaintBrush             SolidColorBrush
AccentBrush                SolidColorBrush
AccentHoverBrush           SolidColorBrush
AccentTextBrush            SolidColorBrush
AccentDimBrush             SolidColorBrush
SelectionBrush             SolidColorBrush
OnAccentBrush              SolidColorBrush
SuccessBrush               SolidColorBrush
SuccessDimBrush            SolidColorBrush
WarningBrush               SolidColorBrush
WarningDimBrush            SolidColorBrush
ErrorBrush                 SolidColorBrush
ErrorDimBrush              SolidColorBrush
ControlHoverOverlayBrush   SolidColorBrush
OverlaySurfaceBrush        SolidColorBrush
OverlaySurfaceAltBrush     SolidColorBrush
ScrimBrush                 SolidColorBrush
ShadowBrush                SolidColorBrush
MediaCaptionGradientBrush  LinearGradientBrush
OnMediaBrush               SolidColorBrush
OnMediaMutedBrush          SolidColorBrush
TeamColorScrimBrush        SolidColorBrush
OnTeamColorBrush           SolidColorBrush
```

Each paired accent dictionary overrides exactly six keys: `AccentBrush`, `AccentHoverBrush`,
`AccentTextBrush`, `AccentDimBrush`, `SelectionBrush`, and `OnAccentBrush`. Preset names are `SmgpRed`,
`Gold`, `Teal`, `RoyalBlue`, `Green`, `Magenta`, and `Orange`. The arbitrary custom-hex path must derive
the same six values in memory: select `AccentTextBrush` and `OnAccentBrush` by measured contrast, then
derive hover/dim/selection for the active base. The old dark-only `AccentDimBrush` blend in
`App.ApplyAppearance` is not compatible with the new light base.

Theme-agnostic font keys are `DisplayFont` (Orbitron), `BodyFont` (Inter), `PixelFont` (Press Start 2P),
`MonoFont` (JetBrains Mono), `IconFont` (Segoe MDL2 Assets), plus `AltDisplayFont` (Chakra Petch),
`AltBodyFont` (Saira), and `AltPixelFont` (Silkscreen).

### VM/settings requests

- Add the Light/Dark base picker and the seven named accent presets to Settings, persist both selections,
  and expose the current choices so the 40px swatches/base buttons can render a selected ring.
- Update `MotionAssist` ripple, the drag/drop insertion pen, and `GlyphBrushConverter` to resolve current
  semantic resources at creation time; those C# styling paths still cache literal dark-theme colors.
- Preserve `TeamPalette` as identity data. App accent must never recolor a team; the XAML now applies a
  theme-independent contrast scrim to team-colored position fills.
- Startup currently replaces the XAML `AppFontSize` value with 14. Decide whether the intended Inter body
  default is 14 or 15 and make the single persisted source authoritative.
