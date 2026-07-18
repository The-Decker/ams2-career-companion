# History art, season story images

Drop an image here and it appears at the top of the **"What really happened"** panel for that
season on the History tab (expand a season card to see it). This is the same drop-in **user-asset
convention** as `era-art/` and `track-art/`, a folder, a key, and a resolver with a clean fallback
(`UserImageResolver`). When no image is present, the panel simply omits it.

The History tab shows the real F1 results of each of your career's years (f1db-derived); a period
photo here makes that reference content feel alive and historical, without changing anything about
your own season.

## File naming

Name each file after the **season year**, with a `.jpg`, `.jpeg`, or `.png` extension:

- `1988.jpg`
- `1967.png`

## Recommended

- A period photo or a season poster reads well. Any aspect ratio is fine, the image scales down to
  fit the panel width and is never upscaled.
- Larger sources stay crisp on high-DPI / 4K displays.

## Notes

- Nothing here is committed to git or compiled into the exe; the folder is refreshable content copied
  beside the app at build/publish time. Add, replace or delete images freely.
- Images are read once with `OnLoad` caching, so a file is **never left locked**, swap art while the
  app runs (reopen the panel to see the change).
