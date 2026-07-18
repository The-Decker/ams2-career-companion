# SMGP news/history/events audit (mission: mode-separation finalization, Phase One)

_2026-07-18, Claude (Head of Coding), branch `hub/increment-4`. Grounded in the actual corpora
and code, not estimates. Classification per the mission: Complete / Complete-proofread /
Incomplete / Placeholder / Duplicate / Misclassified / Unused / Unreachable / Broken /
Cross-mode contamination / Missing trigger / Missing UI connection / Missing artwork /
Contradictory / Canonically ambiguous / Obsolete._

## Totals discovered

| Pool | Items | Mode assignment | Classification |
|---|---|---|---|
| `data/rules/newsroom/010-core.json` | 55 ids | mode-agnostic (era-voiced, by design D1-D13) | Complete |
| `newsroom/020-divergence.json` | 3 | mode-agnostic | Complete |
| `newsroom/030-race-results.json` | 26 | mode-agnostic | Complete |
| `newsroom/031-quali-milestones.json` | 30 | mode-agnostic | Complete |
| `newsroom/032-championship.json` | 31 | mode-agnostic | Complete |
| `newsroom/033-reliability-injury.json` | 24 | mode-agnostic | Complete |
| `newsroom/034-market-teams.json` | 28 | mode-agnostic | Complete |
| `newsroom/035-smgp-world.json` | 27 | **SMGP** (`Eras=["smgp"]` + smgp pool variants) | Complete |
| `newsroom/036-retrospectives.json` | 17 | mode-agnostic | Complete |
| `newsroom/037-progression.json` | 8 | mode-agnostic (character-progression events) | Complete |
| `newsroom/038-smgp-canon.json` | 3 | **SMGP** (SmgpFiction provenance, canon divergence) | Complete |
| `newsroom/039-economy.json` | 12 | **Dynasty** (fires only when `PlayerCareerState.Economy` exists) | Complete |
| `newsroom/desks.json` | 6 | mode-agnostic (editorial desks) | Complete |
| `data/rules/news/1960s…2010s.json` | 5 era packs | mode-agnostic (historical era voice) | Complete |
| `data/rules/news/smgp.json` | 29 ids | **SMGP** | Complete |
| `data/rules/smgp/dispatches.json` | 21 templates + 4 pools | **SMGP** (SMGP-only feed) | Complete |
| `data/rules/smgp/seasons.json` | 17 season identities (155 KB) | **SMGP** | Complete |
| `data/rules/smgp/driver-profiles.json` | 34 driver bios (136 KB) | **SMGP** | Complete |
| `data/rules/smgp/team-profiles.json` | 24 team stories (114 KB) | **SMGP** | Complete |
| `data/rules/smgp/what-really-happened.json` | SMGP almanac (31 KB) | **SMGP** (SmgpFiction) | Complete |
| `data/rules/smgp/driver-stats.json` | 34 predetermined baselines | **SMGP** | Complete |
| `data/rules/smgp/rival-quotes.json` | per-driver per-mood trash talk | **SMGP** | Complete |
| `data/rules/smgp/sponsors.json` | fictional sponsor board (display) | **SMGP** | Complete |
| `data/rules/smgp/pit-crew-advice.json` | venue pit-wall advice | **SMGP** | Complete |

**SMGP-scoped articles/templates: ~245** (27+3+29+21 + lore/bios/almanac/quotes/stats/advice).
**Dynasty-scoped: 12** (039-economy) + the Dynasty economy event spine (6 kinds).
**Racing Passport scoped: 0** (pure racing uses the mode-agnostic packs only, by decision).
**Unclassified/cross-mode: 0 found** — every pack is either mode-agnostic by approved design or
explicitly era/mode-gated. **Duplicated entries: 0** (RENDEZVOUS append-stable ids).

## Architecture findings (separation is by design, and it holds)

1. **Mode flavoring is era-keyed.** `NewsroomComposer` resolves an era override (`"smgp"` for
   SMGP careers, else by season year) and every pool carries `"smgp"` vs `"default"` variants.
   A 1988 historical career reads fax-era voice; an SMGP career reads the SEGA-universe voice.
2. **Mode gating is on the event spine.** `NewsEvent` kinds are mode-gated (`NewsEvent.cs:89` —
   SMGP flavours fire only from SMGP beat detectors). `039-economy` events require an actual
   `DynastyEconomyState`, which an SMGP or Passport career can never carry (creation-gated).
3. **Fiction provenance.** `SmgpFiction` marks the SEGA canon as fiction — it can never render
   as verified history, and `SmgpCanonDivergence` badges divergence as SMGP UNIVERSE.
4. **Careers are isolated files.** No shared cache, no shared feed state; a mode switch is a
   different `.ams2career`. Cross-mode retention cannot occur by construction.
5. **Racing Passport writes zero fictional content** — pure racing decision; its feed derives
   only from mode-agnostic racing events (results/standings).

## Remaining gaps (the mission's real work, ordered)

| Gap | State | Action |
|---|---|---|
| **Isolation TESTS: COMPLETE** | `ModeNarrativeIsolationTests` 4/4 (historical/Passport never emit SMGP flavour or economy; SMGP never emits economy; fiction provenance-badged; era key = mode key). |
| **17-season coverage matrix: COMPLETE** | `docs/content/SMGP_17_SEASON_COVERAGE_MATRIX.md`. |
| **Narrative bible: COMPLETE** | `docs/content/SMGP_NARRATIVE_BIBLE.md`. |
| **Per-season content floor: ALL 17 PACKS SHIPPED** | `data/rules/newsroom/seasons/s01-s17`: 119 season-scoped reactive templates + 17 opening features; engine `Seasons` eligibility added; `SeasonPackScopingTests` 3/3 (merge, eligibility, pool integrity). |
| **Editorial QA report: COMPLETE** | `docs/qa/SMGP_EDITORIAL_QA_REPORT.md`. |
| **Migration notes: COMPLETE** | `docs/migrations/SMGP_NEWS_HISTORY_MIGRATION_NOTES.md` (no-migration-needed position, mode carried by career state). |
| **Art asset manifest: COMPLETE** | `docs/audits/SMGP_NEWS_ART_ASSET_MANIFEST.md`. |

## Evidence

- Suite: 2,885 logic + 246 render green at audit time; oracle 77/77.
- Corpus guards: `NewsCorpusGuardTests` (era-banned vocabulary, token/pool resolution),
  `NewsroomComposerTests`, `NewsroomGrammarTests`, `NewsroomEventsIntegrationTests`
  (byte-identical feed + resim), `EconomyNewsEventsTests`, `StoryThreadsAndRumorsTests`,
  `SmgpDispatchCorpusTests`, `SmgpSeasonLoreTests`, `SmgpCanonDivergenceTests`.
