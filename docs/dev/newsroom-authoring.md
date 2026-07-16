# Newsroom + History Archive — content authoring guide

_How to add editorial templates and historical records safely. Companion to
`newsroom-history-overhaul.md` (the architecture). Everything here is read-side data: editing
these files changes DISPLAY only — never folds, never replay. That guarantee is the design;
keep it by never letting any of this feed a fold input._

## 1. Adding news templates (`data/rules/newsroom/*.json`)

Drop a new pack file beside the shipped ones (loaded additively in filename order; use an
`NNN-name.json` prefix). Shape:

```json
{
  "version": 1,
  "templates": [
    {
      "id": "myprefix.race-won.example",
      "event": "raceWon",
      "when": { "isWet": true, "minStreak": 2 },
      "desks": ["wire"],
      "eras": ["1980s"],
      "headline": "{subject} makes it {streak} in the rain",
      "deck": "optional",
      "summary": "optional standfirst",
      "sections": { "lead": "...", "context": "...", "close": "{pool:cheerClose}" }
    }
  ],
  "pools": { "myPool": { "default": ["fragment"], "1960s": ["era fragment"] } }
}
```

Rules that the validation tests enforce (`NewsroomComposerTests` — run
`dotnet test --filter NewsroomComposerTests` after every edit):

- **`id` is forever.** Unique, never renamed, never reused — it is the rendezvous-selection
  key that keeps existing careers' picks stable. Adding templates only re-picks the stories
  the newcomer wins; renaming an id reshuffles everything.
- **`event`** must be a `NewsEventKind` camelCase name. **`when`** guards:
  `isFirstEver, clinchedTitle, tookChampionshipLead, lostChampionshipLead, isWet,
  isFinalRound, isSeasonOpener, rivalInvolved` (bool); `minStreak, minDrought, minUpset,
  maxUpset` (int); `milestoneCounter` (string). The most-specific eligible guard tier always
  wins — write the RIGHT article for the situation, not a token-swapped clone.
- **Sections** come from the fixed order: lead, context, stats, impact, rivalry,
  championship, reliability, next, close.
- **Tokens** (anything else throws): `player subject team venue year season round position
  expected quali champPosition gap streak drought milestone counter winner winnerTeam rival
  reason missRaces wet finale`. Grammar: `{token}`, `{ord:token}`, `{a:token}`,
  `{a:ord:token}`, `{token's}`, `{pool:name}`, and `[[?token: optional segment]]` which drops
  cleanly when the token is empty. Every token that is not guaranteed (all except
  player/subject/team/year/season) must sit inside an optional segment unless your guards
  guarantee it.
- ASCII punctuation only. No real-person quotations, ever. Desks: `wire, slipstream, apex,
  whispers, archive, titlewatch` (roster in `desks.json`). Era keys: `1960s 1970s 1980s
  1990s 2000s 2010s smgp` (smgp is reached only by SMGP careers).

## 2. Adding historical records (`data/history/`)

- **Season files** (`<year>.json`) are BAKED from f1db (CC BY 4.0) by the derivation tool —
  do not hand-edit results. Extending the span means re-running the bake for new years.
- **`eras.json` / `subjects.json`**: authored prose. Every record needs `provenance` and
  `sources`; statistical claims must be checkable against the season files or
  `docs/research/RESEARCH.md`; anything uncertain is marked `isComplete: false` rather than
  guessed. Era boundaries must tile the documented span exactly (validated).
- **`aliases.json`**: an alias groups team strings ONLY when they are trivially the same
  constructor (engine-suffix variants). Historically connected but distinct teams stay
  separate entities with a `lineage` link (`renamed | succeeded | merged | purchased`).
  Every alias string must exist in the season files; one string, one identity (validated).
- Validation for all three lives in `HistoryArchiveTests` — run
  `dotnet test --filter HistoryArchiveTests` after editing.

## 3. What you must never do

- Touch `data/rules/career-headline-templates.json` consumed keys — that bank is a FOLD
  input (headline text is byte-compared); even appending a variant breaks re-simulation of
  existing careers.
- Store rendered articles anywhere. Identity = the event's dedupe key; text re-renders
  deterministically from the master seed.
- Blend provenance: career-universe copy never claims to be the historical record; the
  divergence templates quote the real fact via `{rival}` and label both sides.
