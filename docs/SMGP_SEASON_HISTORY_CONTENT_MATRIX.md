# SMGP season & history content matrix

What content the SMGP replica campaign ships per season, which of the three content layers each
piece belongs to, and exactly where each layer surfaces in the app. Everything documented here is
implemented and cited to code/tests; the whole content stack is **display-only** — none of it is
ever a fold input, so replay stays byte-identical by construction
(`docs/dev/newsroom-history-overhaul.md` D1/D4).

Companion docs: `SMGP_17_SEASON_CAMPAIGN.md` (campaign mechanics), `LEVEL_300_SYSTEM_SPEC.md`
(progression v2). This doc owns the *content inventory* and the *layer/surface map*.

## 1. The 17-season lore matrix

Source of truth: `data/rules/smgp/seasons.json` (version 1, 17 seasons), loaded by
`SmgpSeasonLore.Load` (`src/Companion.Core/Smgp/SmgpSeasonLore.cs`). One `SmgpSeasonLoreEntry` per
campaign ordinal carries: `Title`, `Subtitle`, `Era`, `Overview`, `Preseason`, `Technical`,
`Safety`, plus the five list fields counted below (and `Milestones`). An absent file resolves to
`SmgpSeasonLore.Empty` and every surface degrades to the plain "SEASON n / 17" header.

| # | Title | Era | Themes | Timeline beats | Arcs | Hooks | Contenders | Milestones |
|---|-------|-----|-------:|---------------:|-----:|------:|-----------:|-----------:|
| 1 | The Tenth Summer | The Iron Circus | 6 | 8 | 6 | 12 | 5 | 5 |
| 2 | The Protest Year | The Iron Circus | 6 | 7 | 6 | 12 | 6 | 4 |
| 3 | The Wet Season | The Iron Circus | 6 | 8 | 6 | 12 | 4 | 4 |
| 4 | The Closing Door Opens | The Iron Circus | 5 | 8 | 6 | 12 | 6 | 5 |
| 5 | The Horsepower Spring | The Horsepower War | 6 | 9 | 6 | 12 | 5 | 4 |
| 6 | The Temple Wars | The Horsepower War | 6 | 7 | 6 | 12 | 5 | 4 |
| 7 | The Spending War | The Horsepower War | 6 | 9 | 6 | 12 | 6 | 5 |
| 8 | The Boiling Point | The Horsepower War | 6 | 8 | 6 | 12 | 5 | 4 |
| 9 | The Reckoning | The Horsepower War | 6 | 9 | 6 | 12 | 5 | 4 |
| 10 | The Charter Season | The Safety Reckoning | 6 | 8 | 6 | 12 | 6 | 5 |
| 11 | The Craftsman's Year | The Safety Reckoning | 6 | 8 | 6 | 12 | 5 | 5 |
| 12 | The Frost Blooms | The Safety Reckoning | 6 | 8 | 6 | 12 | 5 | 5 |
| 13 | The Veterans' Autumn | The Safety Reckoning | 6 | 8 | 6 | 12 | 6 | 5 |
| 14 | The Jewel Formula | The Golden Circus | 6 | 8 | 6 | 12 | 6 | 5 |
| 15 | The Insurgent's Last Climb | The Golden Circus | 5 | 9 | 6 | 12 | 5 | 4 |
| 16 | The Silver Jubilee | The Golden Circus | 6 | 9 | 6 | 12 | 6 | 5 |
| 17 | The Crown of Crowns | The Golden Circus | 6 | 11 | 6 | 13 | 6 | 5 |

Contender rosters (the file authors full roster names, e.g. "Ayrton Senna"; abbreviated here to
initials for width — order as authored):

| # | Contenders |
|---|------------|
| 1 | A. Senna, F. Elssler, G. Alberti, B. Salgado, G. Ceara |
| 2 | A. Senna, F. Elssler, G. Alberti, B. Salgado, N. Jones, G. Ceara |
| 3 | A. Senna, G. Ceara, B. Salgado, F. Elssler |
| 4 | A. Senna, F. Elssler, A. Asselin, B. Salgado, G. Alberti, G. Ceara |
| 5 | A. Senna, M. Blume, A. Picos, B. Salgado, F. Elssler |
| 6 | A. Senna, F. Elssler, I. Germi, N. Jones, B. Salgado |
| 7 | A. Senna, F. Elssler, G. Alberti, N. Jones, J. Herbin, B. Salgado |
| 8 | A. Senna, G. Ceara, B. Salgado, F. Elssler, M. Blume |
| 9 | A. Senna, M. Blume, A. Picos, F. Elssler, B. Salgado |
| 10 | A. Senna, F. Elssler, M. Larssen, J. Herbin, B. Salgado, E. Pacheco |
| 11 | A. Senna, F. Elssler, J. Herbin, E. Pacheco, B. Salgado |
| 12 | A. Senna, B. Salgado, M. Larssen, F. Elssler, J. Herbin |
| 13 | A. Senna, F. Elssler, B. Salgado, G. Alberti, J. Herbin, M. Larssen |
| 14 | A. Senna, J. Herbin, G. Ceara, F. Elssler, B. Salgado, M. Larssen |
| 15 | A. Senna, G. Ceara, J. Nono, J. Herbin, M. Larssen |
| 16 | A. Senna, G. Ceara, M. Larssen, J. Herbin, J. Nono, B. Salgado |
| 17 | A. Senna, G. Ceara, M. Larssen, J. Herbin, J. Nono, B. Salgado |

Invariants enforced by `tests/Companion.Tests/Smgp/SmgpSeasonLoreTests.cs` against the shipped
file (SMGP-300 §8.2):

- **Exactly 17 seasons, ordinals 1–17** (`ShipsExactlySeventeenSeasons_Ordinals1Through17`); the
  campaign length is `SmgpRules.CampaignSeasons = 17` (`src/Companion.Core/Smgp/SmgpRules.cs`).
- **Unique identity per season** — title, subtitle, and overview all distinct across 17
  (`EverySeasonHasAUniqueIdentity`).
- **Four contiguous era blocks in campaign order** (`ErasFormFourContiguousBlocksInCampaignOrder`):
  The Iron Circus (1–4) → The Horsepower War (5–9) → The Safety Reckoning (10–13) → The Golden
  Circus (14–17).
- **Content minimums** (`EverySeasonMeetsTheContentMinimums`): overview ≥ 200 chars,
  preseason/technical/safety ≥ 60 chars each, themes ≥ 4, timeline ≥ 6, arcs ≥ 4, hooks ≥ 8,
  contenders ≥ 3, milestones ≥ 2. The matrix above shows the shipped file comfortably clears
  every floor.
- **No placeholder text** anywhere (`NoPlaceholderTextAnywhere`).
- **Outcome sovereignty** (`TheLoreNeverAssertsACampaignChampion`): the lore never declares a
  campaign season's champion — the sim decides every result; canon prose may only assert the
  pre-campaign baseline ("six crowns at the campaign's dawn"). A. Senna headlines season 1 and is
  still a contender at the season-17 summit (`SennaHeadlinesTheOpeningSeason...` — the benchmark
  is never nerfed, per the locked direction in `CLAUDE.md`).

## 2. The three content layers

Every article, timeline entry, and history record carries a `ContentProvenance`
(`src/Companion.Core/Newsroom/NewsroomTaxonomy.cs`) — the visible boundary between the layers:

| `ContentProvenance` | Meaning | Badge (`NewsStoryViewModels.ProvenanceLabel`) |
|---|---|---|
| `VerifiedHistorical` | Real-world fact from a sourced dataset (`data/history`, f1db CC BY 4.0) | `HISTORICAL RECORD` |
| `CareerUniverse` | An outcome of the player's simulated career | `CAREER UNIVERSE` |
| `EditorialAnalysis` | Desk-written interpretation over career facts | `ANALYSIS` |
| `SmgpFiction` | The SEGA-universe canon — fictional by definition | `SMGP UNIVERSE` |
| `SystemGenerated` | Structural/system notices (migration, legacy) | `SYSTEM` |

`NewsroomComposer.ProvenanceFor` (`src/Companion.Core/Newsroom/NewsroomComposer.cs`) is the single
assignment point: `HistoryHeld` → `VerifiedHistorical`, `SmgpCanonDiverged`/`SmgpCanonHeld` →
`SmgpFiction`, everything else → `CareerUniverse`.

### Layer 1 — SMGP canon (authored fiction, static data)

All five canon sources load once per session in `CareerRulesData.Load`
(`src/Companion.ViewModels/Services/CareerRulesData.cs`); every loader tolerates an absent file
(the surface hides or degrades — never an error):

| Content | File | Loader | Surface(s) |
|---|---|---|---|
| Season lore (the matrix above) | `data/rules/smgp/seasons.json` | `SmgpSeasonLore` | Briefing season header: `BriefingViewModel.SmgpSeasonTitle` / `SmgpSeasonSubtitle` / `SmgpSeasonEra` (`src/Companion.ViewModels/Briefing/BriefingViewModel.cs:232–238`), fed by `CareerSessionService.CurrentSeasonLore()` (ordinal = current season count). Campaign timeline: `CareerSessionService.CampaignTimeline()` puts the lore `Title`/`Era` on **every** slot, locked seasons included — spoiler-light by construction, since the lore never asserts outcomes. |
| Venue almanac ("What Really Happened") | `data/rules/smgp/what-really-happened.json` | `SmgpWhatReallyHappened` | History tab almanac panel: `CareerSessionService.SmgpWorldHistory()` → `HistoryViewModel.SmgpWorld` (`src/Companion.ViewModels/Hub/HistoryViewModel.cs:131`). Venue-keyed (not round-keyed) so the season-2+ shuffled calendar still resolves each place. A venue reveals once raced this season; once any season completes the whole almanac stays unlocked. Also the divergence baseline — see layer 2. |
| Driver bios (34 drivers) | `data/rules/smgp/driver-profiles.json` | `SmgpDriverProfiles` | Paddock driver cards: `CareerSessionService.SmgpPaddock()` → `PaddockViewModel.Refresh` (`src/Companion.ViewModels/Hub/PaddockViewModel.cs:55`); the player's card also feeds the Driver dossier (`DossierViewModel`). |
| Team histories/quotes (24 teams) | `data/rules/smgp/team-profiles.json` | `SmgpTeamProfiles` | Paddock team cards (same `SmgpPaddock()` projection). |
| Predetermined pre-history stats | `data/rules/smgp/driver-stats.json` | `SmgpDriverStats` | Paddock stat tiles; the AI baseline the player's from-zero live stats (`SmgpLiveStats`) accrue against. |

All of it is fiction and DISPLAY-ONLY — the sim/fold never reads any of these files (stated on
every loader's doc comment; e.g. `SmgpSeasonLore.cs:11–14`).

### Layer 2 — career-universe history (computed from folded state)

The newsroom event spine: `NewsEventKind` (`src/Companion.Core/Newsroom/NewsEvent.cs`) currently
defines **62 kinds** — career/season boundaries, per-round results, career firsts, qualifying,
streaks/droughts, championship movement, records, AI-world stories, team tier moves, driver
market, mortality (`PlayerInjured`/`SeasonEndingInjury`/`PlayerDied`), real-history divergence,
SMGP flavour (`RivalryDeveloped`/`DnqDrama`), character progression (`LevelMilestone`,
`Level300Reached`), the medical comeback (`ReturnedFromInjury`), campaign completion
(`CareerCompleted`), and SMGP canon divergence (`SmgpCanonDiverged`/`SmgpCanonHeld`). Detection is
a pure projection: `CareerSessionService.BuildNewsroomSeasons` shapes per-season/per-round facts
from the immutable stored results + journal
(`src/Companion.ViewModels/Services/CareerSessionService.Newsroom.cs`), and
`CareerNewsEvents.Detect` reads them in Core. Memoized behind a stored-state fingerprint
(`NewsroomEvents()`, `CareerSessionService.Newsroom.cs:28`).

Progression events specifically (SMGP-300 wave):

- `LevelMilestone` fires at levels 25–275 in steps of 25 (`CareerNewsEvents.CharacterLevelMilestones`,
  `CareerNewsEvents.cs:28`), from the journaled per-round `player.xp` level facts
  (`PlayerLevelAfter` per round + `PlayerLevelAtSeasonEnd` at the boundary).
- `Level300Reached` is its own feature event at the cap (`CareerNewsEvents.cs:707`).
- `ReturnedFromInjury` fires on the first start after injury sit-out rounds (`CareerNewsEvents.cs:155`).
- `CareerCompleted` fires when the campaign finale closes — for SMGP that is ordinal ==
  `SmgpRules.CampaignSeasons` (`CareerSessionService.Newsroom.cs:413`).

Downstream career-universe surfaces, all built over the same spine:

| Surface | Member | Notes |
|---|---|---|
| Rendered news feed | `CareerSessionService.NewsroomFeed()` | Voiced via `NewsroomComposer` + the 241-template corpus; bodies re-render deterministically from the master seed, never stored. |
| Story threads | `CareerSessionService.StoryThreads()` | Title fight, reliability crisis, rivalry, injury recovery, driver market… |
| Rumor board | `CareerSessionService.RumorBoard()` | Fact-backed whispers with honest resolution links; the original rumour is never rewritten. |
| Weekend editorial package | `CareerSessionService.WeekendPackage()` | Importance-selected (quiet 5 / busy 8 / big 12, cap 14). |
| Season cards w/ level start/end | `CareerSessionService.CareerTimeline()` → `CareerSeasonCard.PlayerLevelAtStart`/`PlayerLevelAtEnd` (`CareerSessionService.cs:5532–5533`) | From the folded start/end player states. |
| Campaign timeline | `CareerSessionService.CampaignTimeline()` (`CareerSessionService.cs:6025`) | 17 slots (locked/current/completed), lore titles from layer 1, `MissedRounds` from `SatOutRound` events — the timeline never rewrites an injury absence as participation. |
| Injury/medical record | `CareerSessionService.InjuryHistory()` (`CareerSessionService.cs:6093`) | Per-accident `minorInjury`/`seasonEnding`/`death` from the journal, with deterministic non-graphic flavour (`InjuryFlavor`). |
| Canon divergence | `SmgpCanonDivergence.Compare` (`src/Companion.Core/Smgp/SmgpCanonDivergence.cs`) | Every raced venue's actual winner vs the almanac's remembered ruler (`SmgpRaceLore.Champion`, "who the world remembers ruling here"). Emits `SmgpCanonDiverged`/`SmgpCanonHeld`; the canon name rides `Facts.RivalName` so templates can speak both names. Badges `SmgpFiction`, never `VerifiedHistorical`. |

### Layer 3 — real history (fenced OFF for SMGP)

The real-history layer (`data/history/<year>.json`, f1db-derived, CC BY 4.0) never touches an SMGP
career, in both directions:

- **Divergence:** `CareerSessionService.NewsroomEvents()` gates on `!IsSmgpPack` before the
  real-history comparison — "the SMGP universe is fiction; it never compares against real
  history" (`CareerSessionService.Newsroom.cs:39`). The SMGP branch diverges against its OWN
  canon instead (the almanac, `Newsroom.cs:50–55`).
- **Reports:** `CareerSessionService.SeasonDivergence()` returns null outright for SMGP careers
  (`CareerSessionService.cs:66–71` of the Newsroom partial).
- **Archive:** `CareerSessionService.HistoryArchive()` is real history only — "career-universe
  records never enter this index" (`Newsroom.cs:146`), and SMGP fiction never enters it either.

## 3. Dedupe-key discipline

`NewsEvent.DedupeKey` (`src/Companion.Core/Newsroom/NewsEvent.cs:173–175`) is the stable identity
for the whole pipeline: shape `{kind}:{seasonOrdinal}:{round}:{subject}[:{discriminator}]`,
invariant-culture, lowercase kind.

- **Season** is the career-relative ordinal (not the year), so era transitions cannot collide.
- **Round 0** means a season-level event.
- **Discriminator** disambiguates same-kind same-round events on one subject (e.g. two
  `CareerMilestone`s landing together: `starts` and `wins`); empty in the common case.
- Consumers: template selection seeds off the key, generated stories carry it, read/bookmark
  state (`NewsReadingStateStore`, schema v6 user preference — survives re-simulation) references
  it, and the editorial selector drops same-key duplicates.
- `NewsEventKind` names are data-format identifiers (they key dedupe keys and corpus template
  families): **additive only, never rename** (`NewsEvent.cs:5–7`).

## Accepted deviations

Where the SMGP-300 mission spec's content demands and the repo's decided design part ways
(positions carried by `docs/dev/newsroom-history-overhaul.md`,
`docs/dev/smgp-history-news-contract.md`, and the coordinator ledger
`docs/dev/smgp-alpha-finish-status.md` — do not re-litigate without Mike):

1. **The lore is outcome-agnostic, not a scripted history.** The spec's rich per-season canon is
   shipped, but it may only assert the *pre-campaign baseline* — never a campaign season's
   champion or result. Rationale: season outcomes are the deterministic sim's alone
   (replay-byte-identical contract); scripted outcomes would either lie against the sim or leak
   into the fold. Guarded by `SmgpSeasonLoreTests.TheLoreNeverAssertsACampaignChampion`.
2. **SMGP never compares against real motorsport history.** Instead of the spec's unified
   history integration, SMGP gets its own divergence axis (career vs SEGA canon, overhaul D9) and
   the real-history path is hard-fenced (`IsSmgpPack` guard above). Rationale: the SEGA universe
   is fiction; a real-vs-SMGP comparison would be meaningless and would blur the provenance
   boundary the newsroom exists to keep visible.
3. **Canon divergence badges as `SmgpFiction` ("SMGP UNIVERSE"), never as verified history and
   never silently as career universe.** D9's layer separation: the career universe never merges
   into the canon, and the canon can never be mistaken for real history.
4. **The almanac is venue-keyed, not round-keyed.** The spec's per-round circuit lore is resolved
   by venue name so the season-2+ calendar shuffle (a fold-side feature) still finds each place's
   legend without duplicating content 17 times (`SmgpWhatReallyHappened.cs:7–13`).
5. **All canon content is optional at runtime.** Absent data files resolve to `Empty` loaders and
   plain headers/hidden panels rather than hard requirements — an un-updated data folder degrades,
   it never breaks a career.
