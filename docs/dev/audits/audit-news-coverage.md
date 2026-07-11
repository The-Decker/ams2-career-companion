# News corpus coverage audit (2026-07-11) — the deepening worklist

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

## SMGP fictional-world outlet (2026-07-10)

`data/rules/news/smgp.json` is a DEDICATED corpus for the Super Monaco GP replica mode's SEGA
universe (its own teams, the LEVEL A–D rival ladder, the D.P. driver-points readout, the hairpin,
the big board) in a bold retro-arcade tabloid voice — much more colorful than the historical
outlet. It is selected by `NewsFacts.PreferredEra = "smgp"` (set in `CareerSessionService` for
`careerStyle: "smgp"` careers), NEVER by year: its era range is a sentinel (`9000–9099`) no real
career hits, so a historical 1990 F1 career keeps the 1990s outlet (`SmgpEra_IsSelectedByOverride
Only_NeverByYear` pins this). Covers every race cause + both digests (10/8/7-ish variants each) +
SMGP-flavor pools (`ladderBuzz`, `rivalTalk`). NEXT for it: SMGP-specific article TYPES for the
folded rival events — `smgp.battle` (rival beaten/lost), `smgp.seat` (seat swap / relegation),
title defense — wired through `ReadFeed` so a seat change becomes a NEWS HEADLINE ("PLAYER SEIZES
THE MADONNA SEAT!").

## Volume snapshot (era-keyed variants per body key / pools)

| decade | bodies (min–max per key) | pools | signature pools | state |
|--------|--------------------------|-------|-----------------|-------|
| 1960s | 11 (was 7–10) | 9 | wire-report voice, **rivalLine (new)** | **deepened 2026-07-10** |
| 1970s | 9–11 (was 7–8) | 9 | sponsorLine, techNote (wings/ground-effect), **rivalLine (new)** | **deepened 2026-07-10** |
| 1980s | 9 (was 7–9) | 10 | mediaLine, **paddockNote/workshopNote (new)** | **deepened 2026-07-11** |
| 1990s | 9–11 (was 7–8) | 8 | techNote, rivalLine | **deepened 2026-07-10** |
| 2000s | 9 (was 8–9) | 9 | webDesk, **paddockNote/reviewNote (new)** | **deepened 2026-07-11**; defaults thin (1 per body key) |
| 2010s | 8–11 (was 6–8) | 9 | techNote, punditTake, **sillySeason (new)** | **deepened 2026-07-10** |

2010s pass added: +3 variants on every race cause, +2 on both season digests, the `sillySeason`
pool (driver-market beats woven into points/overperformed/midfield), and top-ups to
expectationBeat/champLine/techNote/punditTake/seasonClose.

1970s pass added: +3–4 variants on every race cause, +2 on both season digests, and the
`rivalLine` pool (the era's front-of-field duels — woven into podium/points/midfield).

1960s pass added: brought every body key and both digests to 11 (from 7–10), plus a
wire-report `rivalLine` pool — same treatment as 1970s, in the telegram/STOP voice.

1990s pass added: every body key to 10–11 (from 8) and both digests to 9, in the telemetry/
professional-era voice (existing techNote + rivalLine pools reused, no new pool).

1980s pass added: every race cause and both digests to 9, expanded the main existing pools to
9–11 era variants, and added 8-variant `paddockNote` + `workshopNote` pools for grid-politics and
reliability/debrief beats without assuming every car is turbocharged.

2000s pass added: every race cause and both digests to 9, expanded the main existing era pools
to 9–11 variants, and added 8-variant `paddockNote` + `reviewNote` pools for driver-market and
post-race analysis beats. Era-neutral body defaults remain deliberately small but fully guarded.

## Known coarse-grain (accepted)

- Era windows are DECADE-wide: the 2010s file (2010–2029) voices DRS/hybrid from 2010 though
  DRS is 2011+ and hybrid PU 2014+ — a 2010 career reads slightly ahead of its year. Fixing
  properly means splitting eras (e.g. 2010–2013 / 2014+), which doubles authoring cost; deferred
  until a pack sits awkwardly in a window.
- `default` strings are era-neutral by test, but 2000s defaults are a single variant per body
  key — invisible today (every bundled pack falls inside an authored era) yet worth padding.

## The grind queue (one slice per session, commit each)

1. ~~**1980s / 2000s deepen**~~ — DONE 2026-07-11: every key now has 9 era bodies, with two
   additional pools per decade. All six historical corpora now clear the variety floor.
2. **New article TYPE: title watch** (the megaprompt's championship-permutation beat). Wiring:
   read-side in `CareerSessionService.ReadFeed` — when late-season facts qualify (player top-2,
   tight gap), compose a second body via a distinct stream discriminator (`"title"`, precedent:
   `"season"`) against new `race.result|title-watch` templates and append. No fold change.
3. **New TYPE: qualifying report** — blocked on facts: the envelope stores grid data, but no
   `news.headline` row exists for qualifying; would need either a fold change (gated) or
   attaching a quali paragraph to the race body read-side (cheap version — do this first).
4. **Transfer rumors as standalone silly-season articles** — the pool exists (2010s); a
   standalone TYPE wants an offers/market journal source; revisit when the career-sim
   team/sponsor arc lands (PLAN.md).
