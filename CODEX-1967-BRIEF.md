# Codex brief — the 1967 (1960s) era, in parallel with Claude's SMGP work

Two agents share this repo right now: **Claude = SMGP mode only**, **Codex (you) = the 1967 F1 era**.
This brief keeps us from colliding. Mike is coordinating both.

## 0. Isolation (do this first — non-negotiable)
Work in your OWN git **worktree + branch**, NOT the same working directory Claude is editing (Claude
is in `Z:\Claude Code\ams2-career-companion` on `hub/increment-4`). Two agents editing one working
tree WILL clobber each other's uncommitted edits and build outputs.

```
cd "Z:\Claude Code\ams2-career-companion"
git worktree add "Z:\Claude Code\ams2-worktrees\era-1967" -b era/1967 hub/increment-4
```
Work, build, and commit in that worktree. Push `era/1967`. Mike (or Claude) merges it into
`hub/increment-4` at clean points. Rebase `era/1967` on `hub/increment-4` periodically to stay current.

## 1. Your mission
Bring the **1967 F1 era** up to parity with the best-covered seasons. Start by reading
`docs/dev/season-coverage.md` — it tracks every season's coverage (news depth, ratings, weather,
facts, circuit maps, era-art) and its per-season gotchas. Find 1967's (and the neighbouring 1960s
packs') open items and fill them. Likely candidates: 1960s news depth, driver-ratings accuracy for
`packs/f1-1967`, historical-facts/circuit coverage. `packs/f1-1967/pack.json` is the season pack;
`data/rules/news/1960s.json` is the era's news corpus; `data/history/1967.json` is the results the
History tab shows. (Note: 1967 wet-weather research is already DONE — don't redo it.)

## 2. Your lane — files you MAY edit
- `packs/f1-1967/**` (and, if you extend the mission, other 1960s packs `packs/f1-1969/**`)
- `data/rules/news/1960s.json`  (the 1967 era's news corpus)
- `data/history/1967.json`, `data/ams2/era-art/1967.jpg`
- `docs/dev/season-coverage.md` (your 1967 rows), `docs/dev/audits/*` (1967-specific), `docs/research/*` (1967)
- New tests you add for the above under `tests/Companion.Tests/**`

## 3. DO NOT TOUCH — Claude's lane + shared surfaces
- **SMGP (Claude's):** `data/rules/smgp/**`, `src/Companion.Core/Smgp/**`, `src/Companion.Ams2/Skins/**`,
  `SMGP-CONTINUE.md`, `packs/f1-1988` and the SMGP pack, anything skin/livery/staging.
- **Shared CODE — data-only for you; if you truly need a code change, tell Mike, don't just edit:**
  `src/Companion.Core/News/**` (NewsArticleBank/NewsFacts/NewsArticleComposer), the points/standings
  engine, `src/Companion.Data/**`, the app shell.
- **Shared docs — coordinate, don't clobber:** `MEMORY.md` and the auto-memory files, `CLAUDE.md`,
  `AGENTS.md`, `PLAN.md`. Keep your own progress notes in your commits / a `docs/dev/` file.

## 4. Discipline (same locked rules as Claude)
- Keep the whole suite green: `dotnet test Companion.slnx`. The **f1db oracle is 77/77 and is NEVER
  touched**. News changes must pass `NewsCorpusGuardTests` (era-banned vocabulary, token/pool
  resolution, ≥6 variety floor).
- News bodies are READ-SIDE (never folded) — no determinism gate. But any pack/ratings change affects
  NEW careers only and must not touch the byte-identical-replay invariant.
- Commit + push to `era/1967` in slices. No force-push to `hub/increment-4`.

Questions or a shared-code change you can't avoid → surface to Mike so Claude and you don't both edit
the same file.
