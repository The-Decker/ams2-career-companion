# Codex GUI brief — newsroom + history archive editorial polish

_For the Head of GUI (permanent lane: `src/Companion.App/**` + render stand-ins). The living
newsroom and history archive backends are SHIPPED (design: `newsroom-history-overhaul.md`;
authoring: `newsroom-authoring.md`). A first functional XAML pass landed with the feature
(badges, rails, encyclopedia sections, divergence panel, archive search — kept deliberately
restrained). This brief is the editorial-design ceiling: take News from "a feed with badges"
to "opening a motorsport publication", and History to "a premium encyclopedia"._

## Bind contract (all shipped, all display-only)

**NewsViewModel** (`NewsViewModel.Unified.cs`): Stories/FilteredStories/LeadStory/
SecondaryStories (existing) — every `NewsStoryViewModel` now also carries: `Deck`,
`DeskName`/`DeskMonogram` (six desks: Grid Wire, The Slipstream, Apex Technical Review,
Paddock Whispers, The Archive Desk, Title Watch), `StatusLabel` (CONFIRMED/REPORTED/
DEVELOPING/RUMOUR/ANALYSIS/OPINION/RETROSPECTIVE), `ProvenanceLabel` (CAREER UNIVERSE /
HISTORICAL RECORD / SMGP UNIVERSE — the load-bearing separation badge), `CategoryDetail`,
`TierLabel` (LEAD/FEATURED/STANDARD/BRIEF — size cards by THIS, never randomly),
`ReadingTimeLabel`, `IsUnread`, `IsBookmarked`, `ThreadKey`, plus Has* flags for all.
New rails: `Threads` (ThreadCardViewModel: Title/TypeLabel/StateLabel/LatestSummary/Entries/
IsActive), `Rumors` (RumorCardViewModel: Claim/StatusLabel/ResolutionNote/HasResolution —
speculation must LOOK speculative), `BookmarkedStories`, `UnreadCount`/`HasUnread`,
`ToggleBookmarkCommand(story)`.

**HistoryViewModel** (`HistoryViewModel.Encyclopedia.cs`): `Eras`/`Subjects`/`TimelineEntries`/
`TopDrivers`/`TopTeams`/`TopCircuits` (card records with labels prebuilt), `FeaturedEra`/
`FeaturedDriver`/`FeaturedTeam` (deterministic daily rotation — do not re-shuffle),
`ArchiveSearchText` + `ArchiveSearchResults` (`MatchedOn` = why it matched),
`DivergenceRows`/`DivergenceChampionLine`/`HasDivergence` — the REAL HISTORY vs THIS
UNIVERSE two-column comparison; the two columns must never be visually mergeable.

## Design direction

- Card sizes come from `TierLabel` only: Lead = hero card w/ deck; Featured = large;
  Standard = row card; Brief = one-liner. No other size variation.
- Desk monograms as small mastheads (era-appropriate restraint — no caricature).
- Provenance is the one badge that must survive every layout: a reader glancing at any card
  knows instantly whether it is the career universe or the historical record.
- Rumour desk styles as a column (Paddock Whispers voice), never as news cards.
- History wants readability over decoration: the encyclopedia sections are reference
  surfaces; the divergence table is the showpiece.
- Fallback art: KeyedAssetImageConverter chains (category/era keyed) — data/ams2/era-art
  per year; history-art/<year> slots exist and are empty (art lane opportunity).
- Everything virtualized; no horizontal page scroll; chips always carry text.

## Lane notes

- Theme.xaml additions are yours if needed (the first pass deliberately used existing
  brushes only). Core/ViewModels/Data are the coding lane — request contract changes via
  a note in this file rather than editing.
- Render stand-ins: NewsViewRenderTests + HistoryArchiveRenderTests were extended with the
  first pass; keep them green and grow them with what you build.
