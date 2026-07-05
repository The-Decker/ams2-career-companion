# Era art — career gallery imagery

Drop real historical photos here and they appear automatically on the Start-screen career
gallery cards (career-hub-design.md §11, decision 20). No code, no config: the app resolves an
image for each career by the year in its name and shows it in the card's top band. When no
matching image is present, the card falls back to a clean coloured era-accent placeholder.

## File naming (most specific wins)

For a career whose name contains a year (e.g. *"Formula One 1967"*), the app looks for these
files **in this order** and uses the first that exists:

1. **Year-specific** — `1967.jpg` or `1967.png`
2. **Era-medium** — the fallback shared by every season of that era:
   - `telegram.jpg` / `telegram.png` — the telegram era (years **≤ 1979**)
   - `fax.jpg` / `fax.png` — the fax era (**1980–1993**)
   - `email.jpg` / `email.png` — the email era (**1994 onward**)

`.jpg` is preferred over `.png` at the same specificity. So a hand-picked `1967.jpg` beats the
generic `telegram.jpg`; drop a per-year photo only where you want that season to stand out, and
let the three era-medium images cover everything else.

The era boundaries match `EraThemes.ForYear` — the same mapping that colours the card and prints
its `TELEGRAM` / `FAX` / `EMAIL` label.

## Recommended size

- **≈ 640 × 360 px, 16:9** (the card band renders at 288 × 88 and crops with
  `UniformToFill`, so keep the subject roughly centred).
- `.jpg` for photographs (smaller files), `.png` for flat/vector art.
- A larger source (e.g. 1280 × 720) also works and stays crisp on high-DPI displays.

## Notes

- Images are read once with `OnLoad` caching, so a file is **never left locked** — you can
  add, replace or delete images while the app is running (reopen the Start screen to see a swap).
- Nothing here is compiled into the exe; this whole folder is refreshable content copied beside
  the app at build/publish time. Add or change images freely.
- Provide only what you have — a single `telegram.jpg` / `fax.jpg` / `email.jpg` set is enough to
  give every career an era-appropriate photo.
