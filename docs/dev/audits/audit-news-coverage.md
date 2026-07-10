# News corpus coverage audit (2026-07-10) — the deepening worklist

Mission B baseline over `data/rules/news/{1960s..2010s}.json` (the generative article grammar,
`NewsArticleBank`). Guarded by `tests/Companion.Tests/News/NewsCorpusGuardTests.cs`: era-banned
vocabulary (word-boundary — "backers" once false-flagged "KERS"), full token/pool resolution,
era-neutral defaults, and a ≥6 variety floor per body key. Composer determinism was already
pinned by `NewsArticleBankTests`.

## Article types today

Two phases only: `race.result` (8 causes: win / podium / points / overperformed /
underperformed / dnf-mechanical / dnf-driver-error / midfield) and `season.digest`
(player-champion / season-complete). Bodies attach read-side to `news.headline` journal rows —
nothing folded, so corpus growth changes display copy only and needs no determinism gate.

## Volume snapshot (era-keyed variants per body key / pools)

| decade | bodies (min–max per key) | pools | signature pools | state |
|--------|--------------------------|-------|-----------------|-------|
| 1960s | 7–10 | 6 | (period print voice) | healthy |
| 1970s | 7–8 | 8 | sponsorLine, techNote (wings/ground-effect) | **thinnest bodies — next deepen** |
| 1980s | 7–9 | 8 | boostLine (turbo), mediaLine | healthy |
| 1990s | 7–8 | 8 | techNote, rivalLine | healthy |
| 2000s | 8–9 | 7 | sectorNote | healthy; defaults thin (1 per body key) |
| 2010s | 10–11 (was 6–8) | 9 | techNote, punditTake, **sillySeason (new)** | **deepened 2026-07-10** |

2010s pass added: +3 variants on every race cause, +2 on both season digests, the `sillySeason`
pool (driver-market beats woven into points/overperformed/midfield), and top-ups to
expectationBeat/champLine/techNote/punditTake/seasonClose.

## Known coarse-grain (accepted)

- Era windows are DECADE-wide: the 2010s file (2010–2029) voices DRS/hybrid from 2010 though
  DRS is 2011+ and hybrid PU 2014+ — a 2010 career reads slightly ahead of its year. Fixing
  properly means splitting eras (e.g. 2010–2013 / 2014+), which doubles authoring cost; deferred
  until a pack sits awkwardly in a window.
- `default` strings are era-neutral by test, but 2000s defaults are a single variant per body
  key — invisible today (every bundled pack falls inside an authored era) yet worth padding.

## The grind queue (one slice per session, commit each)

1. **1970s deepen** — thinnest bodies (7s). Add ~3 variants per cause in the established
   glamour-and-grit voice + a `rivalLine`-style pool (the era's feuds) if it earns its place.
2. **1960s / 1990s / 1980s / 2000s** — same treatment, thinnest first.
3. **New article TYPE: title watch** (the megaprompt's championship-permutation beat). Wiring:
   read-side in `CareerSessionService.ReadFeed` — when late-season facts qualify (player top-2,
   tight gap), compose a second body via a distinct stream discriminator (`"title"`, precedent:
   `"season"`) against new `race.result|title-watch` templates and append. No fold change.
4. **New TYPE: qualifying report** — blocked on facts: the envelope stores grid data, but no
   `news.headline` row exists for qualifying; would need either a fold change (gated) or
   attaching a quali paragraph to the race body read-side (cheap version — do this first).
5. **Transfer rumors as standalone silly-season articles** — the pool exists (2010s); a
   standalone TYPE wants an offers/market journal source; revisit when the career-sim
   team/sponsor arc lands (PLAN.md).
