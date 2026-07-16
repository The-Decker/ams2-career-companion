# SMGP History + News finish contract

_Recorded 2026-07-15 before shared-code work, per the GUI lane handoff rule._

## Safety boundary

This finish is read-only. It may project existing raw results, pinned packs, folded state,
journal rows, SMGP career beats, and deterministic dispatches. It must not change career folds,
result envelopes, scoring, replay, f1db fixtures, RNG consumption, or deterministic news
generation. Missing source facts remain empty; the UI never invents career history.

## Existing seams to reuse

- `CareerTimeline()` supplies seasons, per-round player/rival/leader lines, and the records book.
- `SmgpPaddock()` supplies the player identity, current team, portrait/car keys, career and season
  totals, narrative intro, and the structured SMGP career-beat timeline.
- `SmgpDispatches()` supplies the career-wide deterministic SMGP world wire.
- `ReadFeed()` supplies resolved journal articles and Why text.
- `HubViewModel.SelectTabCommand` already provides ordinary History-to-News tab navigation.

## Smallest exact shared contract gaps

### Unified news story

Publish one display-only, career-wide `NewsStoryViewModel` projection without changing either
source generator. Each story needs:

- stable `Key`;
- `SeasonOrdinal`, `SeasonYear`, optional `Round`, `DateLabel`, `RoundLabel`, `VenueName`, and
  `TrackArtKey`;
- source-derived `Category` and `Importance`;
- `Headline`, `Standfirst`, `Body`, and `WhyText`;
- optional driver/team names and portrait/team/car art keys;
- optional stable `HistoryEventKey`.

Categories are limited to facts the source can prove: championship, rivalry, paddock, team
movement, injury, promotion, records, and race report. A source that cannot distinguish the exact
category keeps a broader truthful category; it is never classified by guessing from prose.

`NewsViewModel` additionally owns presentation state only: all/filtered stories, lead and secondary
stories, available categories, selected category, search text, selected article, reader open state,
loading/empty/filtered-empty/legacy states, and open/close/clear commands.

### History archive

Extend the read-only History projection with stable typed data needed by the archive:

- a career summary using the existing player Paddock card plus current career summary;
- structured career beats already produced by `SmgpCareerBeats`;
- race archive rows with season/round key, venue/track art, player finish/status, points earned,
  rival identity, current team/car identity, and qualifying/pole/fastest-lap only when stored;
- season cards with historical team stints, teammates, standings rows, car keys, and typed defining
  moments only when recoverable from pinned career data;
- explicit fresh/loading/legacy state.

Older seasons must resolve identity from their pinned season data inside the session projection;
the App must not join them against the current mutable pack.

### Cross-navigation

Publish stable commands that can open an exact story and an exact History event. A plain tab jump
may remain App-owned; exact deep links must use the stable keys above. Tear-off windows must invoke
the same commands through their existing Hub tag.

## Compatibility fallback

Legacy careers may lack newer optional metadata. Their existing results and headlines still render,
with missing imagery/details collapsed and a visible legacy explanation. No migration is required
for display-only omissions.

## Exact omissions retained by this finish

The current read seams do not expose an actual calendar date, track id per archived dispatch,
historical per-race team/car/teammate identity, fastest laps, DNQ versus DNS classification, or a
typed subject for coarse SMGP milestone/setback dispatches. The finished GUI therefore:

- uses the exact stored season year as its date label and exact round-line venue where available;
- leaves track artwork and historical seat identity collapsed instead of joining mutable pack data;
- labels a missing classification only as `Not classified`;
- shows poles only when the SMGP career card supplies them; and
- keeps coarse milestone/setback stories in the truthful `Paddock` category.

Adding the omitted details later requires only additive fields on these read-only projections; it
does not justify a fold, scoring, replay, fixture, or generator change.
