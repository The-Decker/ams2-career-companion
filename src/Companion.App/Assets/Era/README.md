# Era document art (telegram / fax / email)

Lean, original, vector-look masters for the era-medium document skins
(`Themes/Era.Telegram.xaml` / `Era.Fax.xaml` / `Era.Email.xaml`). All art here is generated
in-house (procedural GDI flat art, no external or copyrighted source material).

## What is live vs. what is a master

The single-exe build stays lean: **the app does not load these PNGs at runtime.** The paper
textures the documents actually render are vector `DrawingBrush` tiles authored directly in the
era dictionaries (`EraTelegramPaperBrush`, `EraFaxPaperBrush`), and the letterheads are vector
XAML data templates. These PNGs are the matching raster masters, kept so a future packaging pass
(one `<Resource Include="Assets\Era\*.png" />` line in `Companion.App.csproj`, owned by the app
packaging lane) can swap the vector twins for raster art without re-authoring anything.

| File | Twin in the app | Notes |
|---|---|---|
| `ochre-wire.png` | `EraTelegramPaperBrush` (DrawingBrush) | 128×128 tileable, transparent sepia wire grain, `PaperTextureKey` `ochre-wire` |
| `thermal-grain.png` | `EraFaxPaperBrush` (DrawingBrush) | 128×128 tileable, transparent slate thermal banding, `PaperTextureKey` `thermal-grain` |
| `letterhead-telegram.png` | `EraTelegramLetterhead` (XAML) | ring stamp + double rules, 480×96 transparent |
| `letterhead-fax.png` | `EraFaxLetterhead` (XAML) | slate sender band + stripes, 480×96 transparent |
| `letterhead-email.png` | `EraEmailLetterhead` (XAML) | inbox row + envelope glyph, 480×96 transparent |

The email medium has no paper texture by contract (`PaperTextureKey` is `""`, a clean surface).

## Font decision: OS fallback is sufficient, no era-doc fonts bundled

The contract document font stacks (`EraThemes.*.DocumentFontStack`) are deliberately conservative
OS stacks:

- Telegram: `Consolas, Courier New, monospace`
- Fax: `Cascadia Mono, Consolas, monospace`
- Email: `Segoe UI, Arial, sans-serif`

The app targets Windows only (`net10.0-windows`, self-contained single exe). On every supported
Windows 10/11 box at least one face per stack is guaranteed present (Consolas and Courier New ship
with Windows; Cascadia Mono ships with Windows 11 and falls back to Consolas on Windows 10; Segoe
UI and Arial are system faces). WPF walks the stack per-family, so period typography never
silently degrades to an unrelated face. Bundling a display typewriter/thermal face would add
single-exe weight for a marginal gain, and only SIL OFL / Apache-2.0 fonts may ship (see
`Fonts/LICENSES.md`): **no era-doc fonts are bundled, and none are needed.**

The era-medium fallback photos referenced by the career gallery live beside this art's consumers
at `data/ams2/era-art/telegram.jpg` / `fax.jpg` / `email.jpg` (see `data/ams2/ART-INVENTORY.md`).
