# Living Paddock Newsroom + Motorsport History Archive — design

_Design contract, 2026-07-16 (Claude, Head of Coding). Grounded in an 8-area repo audit
(determinism spine, persistence, news bank grammar, career event surface, history system, SMGP
narrative systems, news VMs, GUI conventions). This document maps Mike's newsroom/history mission
onto THIS codebase's architecture. Companion docs: `smgp-history-news-contract.md` (the shipped
unified wire this extends), `career-hub-design.md`, `smgp-design.md`, `docs/PROJECT.md` §3.8
(the nine invariants — binding here)._

## 0. The discovered architecture in one paragraph

News today = fold-time `news.headline` journal rows (byte-compared, voiced by the frozen
`career-headline-templates.json` HeadlineBank) + read-side article bodies re-rendered
deterministically by `NewsArticleComposer` over `data/rules/news/*.json` (7 decade outlets,
794 body templates, safe to edit) + the SMGP-only dispatch feed (`SmgpCareerBeats` +
`SmgpWorldStories` detectors → `SmgpDispatchCorpus`, display-only stream `"smgp-dispatch"`) +
last night's unified wire (`NewsStoryViewModel` projection, 8 categories, lead/secondary layout).
History today = career scrapbook (`CareerTimeline()`) + side-by-side real-season files
(`data/history/1967..2026.json`, f1db CC BY 4.0: champions + every round's winner/full
classification/DNF causes/circuit facts) + the archive half (hero/events/race archive/filters) +
the SMGP fictional almanac (`what-really-happened.json`). There is NO stored article, NO read
state, NO entity pages, NO era model, NO divergence computation, NO search across both tabs,
NO first/streak/drought/clinch/lead-change detection for historical careers.

## 1. Architectural decisions (the load-bearing ones)

**D1 — Everything new is a display-only projection.** No new fold rows, no new derived journal
phases, no fold-consumed corpora. New randomness uses unregistered display streams
(`StreamFactory.CreateStream("newsroom", season, round, key)`) exactly like `"smgp-dispatch"`.
Replay byte-identity is preserved BY CONSTRUCTION; every slice still ends with a fold+resim test.
The mission's "career events produce domain events" is satisfied by pure detectors over
already-journaled facts (the `SmgpCareerBeats` pattern generalized), NOT by new fold events.

**D2 — The frozen bank stays frozen.** `career-headline-templates.json` is a fold input whose
picked text is byte-compared; even APPENDING a variant changes the `NextInt` bound and breaks
`Resimulate` for existing careers. The overhaul never touches its consumed keys. A guard test
pins its consumed variant-list lengths so nobody breaks this by accident.

**D3 — Articles are never stored; identity is the dedupe key.** An article's stable identity is
its event's dedupe key (`{kind}:{season}:{round}:{subject}:{perspective}`). Bodies re-render
deterministically from the master seed. "Articles don't rewrite themselves" is guaranteed across
app opens by seeding, and across corpus GROWTH by rendezvous selection (D5). Read/bookmark state
references story keys in new user-preference tables (D8) — the third persistence category
(like `staging_override`): never journaled, never wiped, never a fold input.

**D4 — One mode-agnostic event spine.** New pure Core detector `CareerNewsEvents.Detect(...)`
over shaped per-round facts (stored envelopes, standings snapshots, journal rows, folded states)
emits typed `NewsEvent` records with COMBINATION facts (first-ever, streak length, drought length,
lead change, clinch, final round, wet, upset magnitude, rival involvement...). It covers the
audit's verified-available trigger matrix for BOTH historical and SMGP careers; SMGP's existing
detectors keep running for SMGP-specific beats (they already work) and their output is folded into
the same feed with the same identity scheme the unified wire already uses.

**D5 — Grammar v2, additively.** The proven `{token}` + `{pool:x}` engine grows: optional
segments `[[?fact: ...]]` (drop cleanly when the fact is missing), `{a:token}` (a/an),
`{token+s}` possessive, pronoun tokens filled only when stored (SMGP `SmgpPronouns`; real
historical drivers get surname-repetition phrasing, never guessed pronouns). Multi-section
articles = a structure template (ordered named sections: lead/context/stats/impact/rivalry/
championship/reliability/next) where each section draws from its own module pool. Template selection
uses rendezvous hashing (max `StableHash.Fnv1a64(masterSeed|dedupeKey|templateId)`) so ADDING
templates barely disturbs existing picks; pool fragments keep PCG streams. Unknown token = throw
(NewsArticleBank convention) + a corpus validation test renders EVERY template against full AND
minimal fact sets. New corpora live in `data/rules/newsroom/` (own loader; the legacy
`data/rules/news/` bodies keep working for the classic per-round article until parity).

**D6 — Publications are data.** `data/rules/newsroom/desks.json`: 6 fictional desks — wire
service, weekly magazine, technical journal, paddock/rumor column, history desk, championship
analysis desk — each with tone, preferred categories, headline style, article-length band, era
availability (decade-evolved voice mirrors the existing 7 outlets; SMGP maps to its own desks).
Category enum grows to the mission's 30 (superset of the shipped 8 — additive, existing keys keep
their meaning). EditorialStatus enum: Confirmed/Reported/Developing/Rumor/Analysis/Opinion/
Retrospective. Provenance enum: VerifiedHistorical/CareerUniverse/EditorialAnalysis/SmgpFiction/
SystemGenerated — surfaced as visible badges.

**D7 — Importance + weekend packages are pure functions.** `EditorialImportance.Score(event,
context)` — documented additive weights (base kind weight, championship impact, rarity, upset,
rivalry, record, season-finale, recency, duplication penalty), no RNG. `EditorialSelector`
turns a weekend's candidate events into 4–14 stories (lead + featured + standard + briefs),
per-category caps, novelty preference. Both unit-tested with table-driven cases.

**D8 — Persistence: migration v6, user-preference category.**
`news_reading_state(story_key TEXT PRIMARY KEY, read_utc TEXT NULL, bookmarked INTEGER NOT NULL
DEFAULT 0, bookmarked_utc TEXT NULL)` in the per-career DB. Follows the exact recipe: append one
script to `Migrations.Scripts`, bump the MigrationsV2Tests hard-asserts (5→6), new store
`NewsReadingStateStore` (static, TransactionScope, like StagingOverrideStore), NOT wiped by
`WipeDerived`, migration tests build genuine v5 files via `Migrations.Apply(conn, 5)`.

**D9 — Divergence engine.** `CareerDivergence.Compare(careerTimeline+standings, HistoricalSeason)`
(pure Core-shaped, VM-layer shaping like the beats pattern): per-round winner changed / unchanged,
champion changed, leader-at-round changed, the player as a non-historical entrant, displaced
entrant. Typed records feed BOTH News (retrospective/divergence stories, Provenance =
CareerUniverse with the historical fact quoted as VerifiedHistorical) and History (a real
comparison panel per season/round). SMGP careers compare against SMGP canon
(`what-really-happened.json` + `driver-stats.json`) and are labeled SmgpFiction — the mission's
"Alternate History Events in SMGP". Real history is never overwritten by simulated results;
the two render as separate, labeled columns.

**D10 — History archive entities are COMPUTED from verified data.** The 60 season files hold
full classifications for every round 1967–2026: driver profiles (starts/wins/podiums/fastest
laps/champion seasons, teams driven for, active years), team profiles (wins, champions, drivers,
active years), circuit profiles (editions, winners, layout facts) all AGGREGATE from those files
at load (cached), with the f1db CC BY attribution carried as source metadata. Zero fabrication;
name-string identity with a small alias table (`data/history/aliases.json`) for renamed teams —
explicit relationship records, never silent merging. New authored data (all source-attributed,
incomplete-marked): `data/history/eras.json` (data-driven era definitions + characteristics),
`data/history/subjects.json` (technology/regulation/safety subjects linked to eras/seasons).
Season narrative prose beyond computable facts is NOT invented — season pages compose from
verified facts + graceful "not yet documented" states.

**D11 — Search is an in-memory unified index** (nothing is stored, so DB FTS has nothing to
index): lazily built per session over stories + history entities + timeline + subjects, token
prefix matching, match-reason surfaced, debounced in the VM, rebuilt on the archive refresh
token. Scales fine to low tens of thousands of entries; perf-tested.

**D12 — One envelope change: v9 capture-only AI DNF causes.** `ResultDraft` already captures
per-AI-driver m/a/o letters but `BuildEnvelope` drops them; v9 adds
`AiDnfCauses: IReadOnlyDictionary<string,string>?` (null = legacy). Capture-only (the v7
severity precedent) — no fold consumer, byte-identical replay, and reliability/incident stories
gain named AI retirements for NEW rounds. Old rounds degrade to team-level phrasing.

**D13 — Explicit non-goals (grounded in missing sim facts).** No practice reports (practice
captures no results by design), no grid/time penalties (no penalty system exists), no mid-season
transfer threads (seats change only at season end / SMGP swaps — driver-market coverage follows
the REAL offer/seat.market facts), no fabricated quotations ever (SMGP fictional characters may
speak via their existing authored voice corpora, labeled simulated; real drivers are never
quoted). These become graceful absences, not fake content.

## 2. New Core surface (namespaces follow existing conventions)

- `Companion.Core.Newsroom`: `NewsEventKind` (~45), `NewsEvent`, `NewsEventFacts`,
  `CareerNewsEvents.Detect`, `EditorialImportance`, `EditorialSelector`, `StoryThreads` +
  `StoryThreadState` (Emerging/Developing/Escalating/Confirmed/Resolved/Dormant/Historic),
  `RumorBook` (rumor derivation + resolution linking), `NewsroomComposer`, `NewsroomCorpus`
  (grammar v2 loader/validator), `NewsDesks`, `NewsroomCategory` (30), `EditorialStatus`,
  `ContentProvenance`.
- `Companion.Core.HistoryArchive`: `HistoryEras`, `HistorySubjects`, `HistoryEntityIndex`
  (driver/team/circuit aggregation), `HistoryTimeline`, `CareerDivergence`, `HistorySource`.
- Session (`CareerSessionService` + `ICareerSession` additive defaults): `NewsroomFeed()`,
  `StoryThreads()`, `WeekendPackage(round)`, `DivergenceReport(seasonYear)`,
  `HistoryEntity(kind, key)`, `ArchiveSearch(query, filters)`, `MarkStoryRead(key)`,
  `SetStoryBookmark(key, on)`, `ReadingState()`.
- Data: `NewsReadingStateStore`, migration v6.

## 3. Slice plan (each slice = commit, suite green, replay byte-identity test included)

1. **S1 event spine**: enums/records + `CareerNewsEvents` + importance + selector + dedupe; facts
   shaping in the session (one journal walk, cached like the archive token). Tests: trigger
   matrix over fixture careers, determinism, dedupe, scoring tables.
2. **S2 grammar v2 + composer + desks**: corpus loader/validator, optional segments, a/an,
   possessive, pronouns, rendezvous selection; token-matrix validation tests (no unresolved
   tokens, minimal-facts renders, no double punctuation).
3. **S3 threads + rumors + weekend packages**: state machine, rumor lifecycle with preserved
   originals + linked resolutions, 4–14 story selection. Tests incl. thread progression + rumor
   never-silently-confirmed.
4. **S4 history archive core**: eras.json + subjects.json + aliases.json (authored, sourced),
   entity index aggregation over the 60 files, timeline, season-page model, source metadata.
   Validation tests (dup ids, refs resolve, era ranges cover 1967–2026, no empty bodies).
5. **S5 divergence**: real-vs-career comparison + SMGP canon comparison; divergence events feed
   S1's spine as retrospective/divergence stories.
6. **S6 persistence + search**: migration v6 + store + session read/bookmark surface + unified
   search index. Tests: v5→v6 upgrade in place, reopen no-op, state survives resim, search.
7. **S7 ViewModels**: NewsViewModel.Unified grows editorial modules (lead/featured/standard/
   brief tiers, threads rail, rumor desk, bookmarks/unread, desk badges, reader sections,
   prev/next); HistoryViewModel grows era browser, entity pages, timeline filters, divergence
   panel, deterministic date-aware featured rotation (seeded by career + date, stable per day).
8. **S8 XAML** (flagged cross-lane, kept in well-separated sections/templates): newsroom home,
   reader, history home, era/entity/timeline sections, provenance/status badges, fallback art by
   category/era (existing era-art + KeyedAssetImageConverter chains), virtualization on long
   lists, all polished empty/legacy/no-career states.
9. **S9 content at scale** (workflow fan-out, validated): ≥100 headline patterns, ≥100 paragraph
   modules, ≥6 variants per common trigger, rare-event specials, 6 desks fully voiced across
   eras, SMGP world/alternate-history pack (fiction-labeled), historical retrospective/anniversary
   templates grounded in the 60 season files; `newsroom-authoring.md` guide.
10. **S10 envelope v9** AI DNF capture + reliability stories.
11. **S11 final sweep**: the mission's fixture scenario (user driver in a historical season →
    7 connected stories), 25-point test matrix reconciliation, large-archive perf test, release
    publish, delivery report.

## 4. Mission requirements deliberately adapted (with reasons)

| Mission ask | Adaptation | Why |
|---|---|---|
| Store generated articles + facts + seed | Derive-don't-store + stable keys + rendezvous stability | The repo's determinism spine IS the storage; storing rendered text would create a second source of truth the replay contract can't protect |
| SQLite tables for articles/threads/templates | Tables only for user state (read/bookmarks) | Same as above; templates stay versioned JSON content packs (established strategy) |
| Practice reports | Omitted gracefully | Practice captures no results by design (`PackWeekendSession`) |
| Penalties/stewarding category | Category exists; fed only by DSQ facts | No penalty system in the sim; no invented stewarding |
| Notification eligibility | Importance tier exposed; no OS notifications | Desktop app has no notification surface; tier drives layout |
