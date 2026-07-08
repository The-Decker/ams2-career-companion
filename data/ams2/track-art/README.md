# Track art — circuit-layout thumbnails

Drop a track-layout image here and it appears automatically at the top of the **Race Day
briefing** for any round run at that track. No code, no config: the app resolves an image for
each round by the round's AMS2 **track id** and shows it under the venue line. When no matching
image is present, the briefing simply omits the thumbnail (nothing else changes).

This is the same drop-in **user-asset convention** as `era-art/` — a folder, a key, and a
resolver with a clean fallback (`UserImageResolver`). Nothing here is committed to git or compiled
into the exe; the folder is refreshable content copied beside the app at build/publish time.

## File naming

Name each file after the **track id** from `data/ams2/tracks.json` (the `"id"` field), with a
`.jpg`, `.jpeg`, or `.png` extension. `.jpg` is preferred at the same key.

Examples (ids from `tracks.json`):

- `imola_88.jpg`
- `adelaide_historic.png`
- `interlagos.jpg`

To find a round's track id, open `data/ams2/tracks.json` and match the `trackName` / `location`
to its `id`. Placeholder rounds use the id of the AMS2 track actually driven, not the historical
venue — so the thumbnail shows the circuit you'll really race.

## Recommended

- A clean **circuit map / layout diagram** reads best (transparent or dark background suits the
  app's dark theme). Photographs work too.
- Any aspect ratio is fine — the image scales down to fit the briefing width and is never upscaled.
- Larger sources stay crisp on high-DPI / 4K displays.

## Notes

- Images are read once with `OnLoad` caching, so a file is **never left locked** — add, replace or
  delete art while the app is running (reopen the briefing to see a swap).
- Provide only the tracks you have; every other round just omits the thumbnail.
