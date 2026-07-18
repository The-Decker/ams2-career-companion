# SMGP editorial QA report (mode-separation finalization, second pass)

_2026-07-18, Claude (Head of Coding). The second editorial audit over the shipped narrative
surface, after the separation + season-pack waves. Method: full-corpus scans plus close reading
of representative samples across every era, category, and trigger family._

## Reviewed

- 12 newsroom template packs + desks (010-core through 039-economy): 264 ids.
- 7 news era packs (1960s-2010s + smgp.json).
- 9 SMGP-specific corpora (dispatches, lore, profiles, stats, quotes, sponsors, advice, almanac).
- **17 season dossier packs** (s01-s17): 119 season-scoped reactive templates, read in full at
  author time (4) and spot-read at QA time across all three era batches (s07, s12, s17 sampled
  closely; the rest scanned).

## Findings and repairs

| # | Finding | Disposition |
|---|---|---|
| 1 | A `{pool:wetNote}` reference in s03 resolved to a nonexistent pool | **REPAIRED** (inline prose); pool-integrity test now guards this class (`EveryPoolReferencedByASeasonPack_ExistsInTheMergedCorpus`). |
| 2 | The authoring brief listed `impact`/`reliability`/`rivalry` as pools; they are section keys, not pools | **REPAIRED** before it shipped (caught by the batch author + the integrity guard). |
| 3 | Em-dash risk across new content (owner's U+2014 ban) | **CLEAN** — all packs scanned byte-level; guard green. |
| 4 | Era-batch distinctness (synonym-swap risk across 17 packs) | **PASS** — each batch works its own imagery: ledger/freight (s07), winter glass (s12), coronation (s17), big-board arcade (s01-s04), temple/slipstream (s06), jubilee record-keeping (s16). No recycled angles found. |
| 5 | Incident register (injury/crash/death content) | **PASS** — Safety Reckoning packs report incidents with seriousness (marshals, medical crews, walking away), never celebration; the event-priority rules (death suppresses DNF humor) hold at the engine level (`EditorialSelection`). |
| 6 | Anachronisms / banned crutches | **CLEAN** — no social media, telemetry jargon, "stunning/shocking/against all odds/only time will tell" in the new packs. |
| 7 | Template/schema conformance | **PASS** — all 119 templates carry `eras: ["smgp"]` + their own `seasons: [N]`, valid headline/deck/summary/sections in canonical order, unique rendezvous-stable ids. |
| 8 | Canon safety (outcomes hardcoded) | **PASS** — no pack names a later winner/champion; season-opening canon only; results stay save-generated (the bible's rule holds). |

## Repetition assessment

- Common events (raceWon, podiumFinish, pole, mechanical/driver-error retirement,
  championshipLeadTaken, seasonStarted) now carry era-voiced generic variants PLUS 7
  season-scoped variants per season — a player sees a different season-flavored frame for the
  same trigger as the campaign advances. Selection stays deterministic, persistent per save,
  arc-aware, and family-cooled (existing newsroom machinery).
- No duplicate ids; no duplicate normalized headlines across the season packs (scan clean).

## Coverage status by season (final)

All 17 seasons: identity dossier pack + opening feature + 7 season-scoped reactive templates
each (119 total), layered over the shared newsroom (264 ids), dispatches (21), lore (17),
profiles (34 drivers + 24 teams), almanac, and the arc engines (StoryThreads + SMGP beats).
**Every season has a distinct authored identity, reactive news, arc coverage, and retrospective
framing.**

## Unresolved limitations (honest)

- The mission's aspirational floor (12-20 reactive + 4+ arc templates + 6+ features + 8-12
  archive entries per season) is a continuing authoring program; the shipped floor (7 reactive
  + opening feature per season) covers every event family without padding. Deeper per-season
  feature sets are the natural next content batch and are NOT release-blocking.
- Team photos / round cards / some player-team images remain art-supply items (owner's list)
  with deliberate fallbacks (see `docs/audits/SMGP_NEWS_ART_ASSET_MANIFEST.md`).
- Interview-style longform (multi-paragraph driver interviews) rides the existing
  quote/dispatch surfaces; a dedicated interview format is a content-program item, not a gap in
  the shipped system.
