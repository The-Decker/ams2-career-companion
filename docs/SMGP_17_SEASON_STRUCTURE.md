# SMGP — the 17-season campaign structure

> How the Super Monaco GP replica career runs as a fixed seventeen-season campaign: the shape,
> the per-season lifecycle, the authored season identities, the locked finale, and the terminal
> summit. Everything here is implemented and cited; design history lives in
> `docs/dev/smgp-17-seasons.md` and `docs/dev/smgp-design.md`.

---

## 1. The campaign shape

| Property | Value | Where |
|---|---|---|
| Campaign length | **17 seasons**, fixed | `SmgpRules.CampaignSeasons` (`src/Companion.Core/Smgp/SmgpRules.cs`) |
| Rounds per season | **16** (the shipped `packs/smgp-1` calendar; the code reads the pack, it never hardcodes 16) | `packs/smgp-1/season.json` |
| Finale venue | Monaco, every season — the venue shuffle never moves it | `SmgpSeasonVariety.ForSeason` ("the finale (n-1) stays Monaco") |
| Field vs grid | 34 painted cars, 26-car grids (25 at Monaco) → ~8 DNQ each round | `packs/smgp-1/season.json` `grid.size`; `SmgpDnqField` |
| Season years | Pack year 1990; each carryover must move forward exactly one year (1990 → 2006) | `PackDiscovery.PlanNextSeason` (`nextYear = currentYear + 1`), enforced by `CareerStore.StartCarryoverSeason` |
| Completion milestone (legacy) | 2 titles = "the replica is beaten" — **a milestone, not the summit** | `SmgpRules.IsComplete` |
| Campaign completed | reached the end of season 17 without `CareerOver` | `SmgpRules.CampaignComplete(seasonOrdinal, careerOver)` |
| Campaign flawless | completed **and** champion in all 17 | `SmgpRules.CampaignFlawless(seasonOrdinal, titles, careerOver)` |

**The season ordinal is derived, never stored.** A season's campaign ordinal is its 1-based
position among the career DB's season rows (`CareerStore.ReadSeasons` — `SELECT … FROM season
ORDER BY id`), computed in the `CareerSessionService` constructor
(`src/Companion.ViewModels/Services/CareerSessionService.cs`, the `_seasonOrdinal` loop). Because
the ordinal is positional over rows that only ever append atomically
(`CareerStore.StartCarryoverSeason` inside one transaction), gaps and duplicate ordinals are
structurally impossible — there is no counter to drift.

The only way to *fail* the campaign is the existing SMGP hard-stop: `SmgpState.CareerOver` at
`SmgpRules.FloorLossLimit` (4) rival-battle losses on the LEVEL D floor. Losing titles along the
way does not fail anything (Mike's resolved decision, `docs/dev/smgp-17-seasons.md` §2).

---

## 2. The season lifecycle, end to end

```
create (pin pack + season-1 DNQ roll + gates)
  → open season N (variety → reshuffle → DNQ transforms on the runtime pack)
    → 16-round race loop (battles / swaps / accidents / auto-sim sit-outs)
      → season-end fold (title bank, defense arming, champion rollover)
        → finale (ordinal 17 only)  →  season review + offers
          → StartNextSeason (carryover rollover)  →  open season N+1 …
```

### 2.1 Creation pinning

`CareerSessionService.CreateCareer` pins the pack bytes into the career DB before anything runs.
For SMGP that includes the **season-1 DNQ roll**: `SmgpDnqField.Generate(pack, masterSeed)` rolls
each capped round's starters (top `size − 6` by base qualifying skill guaranteed; the bubble
competes on a ±0.12 seeded jitter per `(round, driver)` via `StreamFactory`, subsystem
`"smgp-dnq"`) and `SmgpDnqField.ApplyToSeasonJson` writes them into the pinned `season.json` —
the fold *reads* pinned starters, so replay needs no seed threading. The career-level gates
`SmgpState.PerSeasonDnq` / `PerSeasonVariety` / `StandingsReshuffle` are seeded `true` at
creation for new SMGP careers and serialized `WhenWritingDefault`, so every pre-feature save
parses them as `false` and keeps its original bytes (`src/Companion.Core/Smgp/SmgpState.cs`).

### 2.2 Per-season transforms (season 2+)

On every open, the `CareerSessionService` constructor derives the runtime pack from the pinned
pack through three pure, gated, ordinal-keyed transforms — applied **identically** on the replay
path (`ReplayService.ResimulateCore`), so live and replay agree by construction:

| Order | Transform | Gate | What it does | Fold input? |
|---|---|---|---|---|
| 1 | `SmgpSeasonVariety.ForSeason(pack, ordinal, masterSeed)` | `PerSeasonVariety` | Seeded Fisher–Yates over every venue except the Monaco finale + fresh per-session weather (a per-round dry/showery/wet "character", ~55/30/15) | Yes — race names reach derived headlines, weather reaches character-effect conditions |
| 2 | `SmgpGridReshuffle.ForNextSeason(pack, previousFinal, playerSeat)` | `StandingsReshuffle` (season 2+, previous final standings available) | Previous championship order re-seats drivers into cars from highest prestige down; the player's car is reserved and A. Senna keeps his authored Madonna seat | Yes — entry assignment |
| 3 | `SmgpDnqField.ForSeason(pack, ordinal, masterSeed)` | `PerSeasonDnq` | Re-rolls the backmarker DNQ field for this season: seasons 2+ fold the ordinal into the stream entity (`"{ordinal}\|{driverId}"`), so each season draws an independent field; season 1 keys by driver id alone and stays byte-identical to the pinned creation roll | Yes — grid membership → seat strength |

All three return the pack **verbatim** for season 1, non-SMGP packs, or an absent gate — the
legacy byte-compat contract. Verified by `SmgpSeasonVarietyTests`
(`SeasonOne_ReturnsThePackVerbatim`, `SeasonTwo_KeepsMonacoAsTheFinale`,
`SameSeasonAndSeed_ReDerivesIdentically`, `DifferentSeasons_RunDifferentCalendars`,
`TheResolvedGrid_IsIdentical_BeforeAndAfterTheVariety`) and `SmgpMultiSeasonDnqTests`.

### 2.3 The race loop

Sixteen rounds of the standard SMGP weekend: briefing (with `SeasonOrdinal`/`SeasonsTotal` —
the "SEASON n / 17" header, `CurrentSmgpBriefing()`), rival naming under the challenge-tier rule,
result entry, and the battle fold (two-wins seat swaps, title-defense force-challenges at rounds
1–2, floor losses). Round mechanics are specified in `docs/dev/smgp-design.md`; injury sit-outs
in §7 below.

### 2.4 Season end

`SeasonReview()` calls `EnsureSeasonEnd()` → `ReplayService.RunSeasonEnd` →
`SeasonEndPipeline` (`src/Companion.Core/Career/SeasonEndPipeline.cs`, "SMGP season fold"):

- `SmgpState.WithSeasonReset()` — rival streaks, the defense scratchpad, `FloorLosses`, and any
  unanswered `PendingSwap` clear; **seats, `Titles`, `CareerOver`, and the career-level gates
  carry**.
- Championship win → `Titles + 1`, `TitleDefense = true`, and `SmgpSchedule.ChampionRollover`
  moves the champion into the Madonna seat (the clean model: its authored driver benches, no
  cascade); a `JournalPhases.SmgpTitle` row records it. G. Ceara force-challenges rounds 1–2 of
  the defense season (`SmgpSchedule.IsTitleDefenseRound`); losing both fires the player to
  Dardan (`SmgpRules.TitleDefense`).
- A season-end held by an unanswered two-phase swap offer stays held until the offer is resolved
  (`EnsureSeasonEnd` returns early while `CurrentSmgpPendingOffer()` is non-null).

### 2.5 Review, offers, rollover

`SeasonReview()` returns the review model (final position, headlines, offer letters);
`AcceptOffer` journals the choice as a provenance row. **In SMGP the accepted offer is the
season-advance trigger only** — `SeasonRollover.Derive`
(`src/Companion.Core/Career/SeasonRollover.cs`) explicitly never reseats a player whose
`playerEnd.Smgp` is non-null, because the rival ladder and title defense already decided the
seat in the fold. `StartNextSeason(teamId)` then rolls the end states forward through
`SeasonRollover.Derive` (including journaled character spends/respecs/skill development) and
appends the new season row via `CareerStore.StartCarryoverSeason` — SMGP always carries its own
pinned pack (`NextSeasonInfo.IsCarryover == true`), never an era changeover
(`PackDiscovery.NextHistoricalAfter` excludes the `smgp` careerStyle in both directions).

---

## 3. Season identity — the authored lore layer

Procedural variety makes each season *play* differently; the authored lore makes each season
*read* differently. `data/rules/smgp/seasons.json` (loaded by `SmgpSeasonLore.Load`,
`src/Companion.Core/Smgp/SmgpSeasonLore.cs`) ships all 17 seasons' canon: title, subtitle, era,
overview, preseason/technical/safety context, themes (4+), timeline beats (6+), story arcs (4+),
newsroom hooks (8+), contenders (3+), and milestone opportunities (2+). It is **display-only and
outcome-agnostic** — the sim decides every result; the lore never asserts a campaign champion
(`SmgpSeasonLoreTests.TheLoreNeverAssertsACampaignChampion`). An absent file resolves to
`SmgpSeasonLore.Empty` and the UI falls back to the plain "SEASON n / 17" header.

Surfaces: `CurrentSeasonLore()` (the open season's lore header) and `CampaignTimeline()` (every
slot carries its lore title + era — locked seasons show title/era only, spoiler-light).

### The four eras and seventeen seasons

| # | Era | Title | One line |
|---|---|---|---|
| 1 | The Iron Circus | The Tenth Summer | The old order at full weight; a new name posted, without ceremony, to the Minarae garage |
| 2 | The Iron Circus | The Protest Year | The circus fights itself — hearing room, scrutineering bay, pit wall |
| 3 | The Iron Circus | The Wet Season | Rain follows the circus around the world; the ladder learns to swim |
| 4 | The Iron Circus | The Closing Door Opens | Madonna unchains its lieutenant for one summer; the frozen spec takes its final bow |
| 5 | The Horsepower War | The Horsepower Spring | Gen-4 arrives screaming; the forest straights become the centre of the racing world |
| 6 | The Horsepower War | The Temple Wars | The slipstream season — the tow is law, the brave trim their wings to nothing |
| 7 | The Horsepower War | The Spending War | Money itself becomes the story; the paddock proves it has a heart anyway |
| 8 | The Horsepower War | The Boiling Point | The king and the insurgent at full flame, all season long |
| 9 | The Horsepower War | The Reckoning | The bill for five years of horsepower comes due |
| 10 | The Safety Reckoning | The Charter Season | Racing under new law — slower on the stopwatch, closer on the road, safer than ever |
| 11 | The Safety Reckoning | The Craftsman's Year | Muscle regulated away, the season belongs to hands — the tyre-whisperers |
| 12 | The Safety Reckoning | The Frost Blooms | Fire against frost — the sister houses war for the family's crown jewel |
| 13 | The Safety Reckoning | The Veterans' Autumn | The old champions rally one last time under the Charter sun |
| 14 | The Golden Circus | The Jewel Formula | The most beautiful machines the circus ever built open the golden age |
| 15 | The Golden Circus | The Insurgent's Last Climb | The backstreet garage bets everything on one final assault at the crown |
| 16 | The Golden Circus | The Silver Jubilee | Twenty-five seasons of the circus honoured at full speed; the tightest title field in memory |
| 17 | The Golden Circus | The Crown of Crowns | Seventeen summers of climbing end where the circus was named: the last flag falls at Monaco |

Content contracts are test-enforced: exactly 17 seasons with ordinals 1–17 and no duplicates
(`SmgpSeasonLoreTests.ShipsExactlySeventeenSeasons_Ordinals1Through17`; duplicate ordinals throw
in `SmgpSeasonLore.Parse`), unique identities, four contiguous era blocks
(`ErasFormFourContiguousBlocksInCampaignOrder`), per-season content minimums, no placeholder
text, and Senna headlining season 1
(`SennaHeadlinesTheOpeningSeason_TheBenchmarkIsNeverNerfed` — the benchmark is never nerfed,
locked direction).

---

## 4. Season 18 is impossible — three independent locks

1. **The planner returns null.** `PackDiscovery.PlanNextSeason` (`src/Companion.ViewModels/
   Services/PackDiscovery.cs`): for an SMGP pack, `if (seasonOrdinal >= SmgpRules.CampaignSeasons)
   return null;` — `NextSeason()` is null at the summit.
2. **The starter throws.** `CareerSessionService.StartNextSeason` throws
   `InvalidOperationException("This career has no next season — the campaign is complete.")`
   when `NextSeason()` is null.
3. **The UI can't ask.** `SeasonReviewViewModel.CanSign => OfferAccepted && HasNextSeason`
   (`src/Companion.ViewModels/Review/SeasonReviewViewModel.cs`) — `HasNextSeason` is false, so
   the sign-and-continue command never enables.

All three are asserted in one test that drives a real career through all seventeen seasons —
`SmgpMultiSeasonDnqTests.FullCampaign_StopsAfterSeventeenSeasons_AndReplaysByteIdentical`
(`tests/Companion.Tests/Data/SmgpMultiSeasonDnqTests.cs`): per-season ordinal checks, the
flawless finale at 17, `NextSeason()` null, `CanSign`/`SignAndContinueCommand.CanExecute` false,
the `StartNextSeason` throw, plus a byte-identical replay of the whole 17-season career.

---

## 5. The finale — the locked `special.jpg` / `ultimate.jpg` screen

`CareerSessionService.SmgpFinale()` returns the finale model **only** when the current SMGP
season is a completed campaign summit (`SeasonComplete` + `SmgpRules.CampaignComplete`), else
null. It is a pure display read over folded state — no fold change, no journal row, no seed —
so the byte-identical replay gate is untouched. The current season's title is banked by
`SeasonEndPipeline` in the `stage=end` player state, which `CurrentPlayerState()` prefers once
the season completes, so a champion-of-season-17 run correctly sees all 17 titles.

- **Tier 1 — completed** (`CampaignComplete`): headline "SEVENTEEN SEASONS CONQUERED",
  `HeroImageKey = "special"`.
- **Tier 2 — flawless** (`CampaignFlawless`, titles ≥ 17): headline "THE FLAWLESS EMPEROR",
  `HeroImageKey = "ultimate"`. At the 17-season summit 17 titles can only mean 17-from-17.

The secret hero key is emitted **only** by `SmgpFinale()` and only when unlocked — no gallery,
converter, or inspector can surface the art early (the images live in the `dist` data tree,
resolved by `SmgpFinaleView`/`SmgpFinaleViewModel`,
`src/Companion.App/Views/SmgpFinaleView.xaml`).

**Routing** (`src/Companion.ViewModels/Shell/HomeViewModel.cs`): the finale is its own
full-immersion step (`IsFinaleStep`), shown once at the fold that completes the campaign,
*before* the final season review; its Continue advances into the review (`ShowFinale`). On
**reopen** of a beaten-summit career the shell leads with the finale again — closing the app
right after the final fold must not be the only chance to see the celebration (the
`SeasonComplete` branch in the content-selection constructor path).

---

## 6. Missed races and seasons under injury

There are no replacement drivers (deviation 1 below). An injured player **sits out**, and the
history stays truthful:

- `CareerSessionService.AutoSimulateRound()` runs a round the player must miss: a deterministic
  AI classification with the player **excluded (DNS)** via `AutoRaceModel.ClassifiedOrder`, the
  envelope carrying `PlayerDidNotStart = true` so the fold keeps the player OPI-neutral and
  ticks recovery. The result is stored — the championship advances honestly for the AI. The
  provenance row joins the fold's atomic transaction (`cause: "auto-simulated"`).
- The shell routes an injured round to the sit-out step (`HomeViewModel.IsSitOutStep` /
  `SitOutViewModel`), never manual entry; the service layer also hard-gates Apply/Preview on an
  active injury (`InjuryAvailabilityGateTests`).
- A `seasonEnding` outcome auto-simulates the rest of the season the same way; recovery spans
  roll into the following season's rounds where applicable. The full accident/injury model is
  `docs/ACCIDENT_INJURY_MEDICAL_SYSTEM.md` + `docs/dev/character-death-injury.md`.
- History surfaces never rewrite an absence as participation: the newsroom emits
  `NewsEventKind.SatOutRound` / `ReturnedFromInjury` events
  (`src/Companion.Core/Newsroom/CareerNewsEvents.cs`), `CampaignTimeline()` flags
  `MissedRounds` per season from that event spine, and `InjuryHistory()` is the per-career
  medical record (season ordinal, round, outcome, races missed).

Death is the other terminal path and out of scope here — see `docs/CAREER_GAME_OVER_FLOW.md`.
Within SMGP, `CareerOver` (the D-floor knock-out) both fails the campaign and blocks
`AutoSimulateRound`/further play; a floor knock-out on the final round keeps projecting the
terminal briefing so reopening still reaches the fired ending (`CurrentSmgpBriefing()`).

---

## 7. Campaign completion state

Completion is a **derived predicate, never a stored flag** — `SmgpRules.CampaignComplete` /
`CampaignFlawless` are pure reads over folded state (season ordinal + `Titles` + `CareerOver`),
so no migration, no counter, and no way for stored state to disagree with the journal. Surfaces:

| Surface | What it shows | Where |
|---|---|---|
| Briefing header | "SEASON n / 17" (`SeasonOrdinal`, `SeasonsTotal`) | `CurrentSmgpBriefing()` |
| Campaign timeline | 17 slots — completed / current / locked, each with lore title + era, player position/champion, missed-rounds flag | `CareerSessionService.CampaignTimeline()` |
| Finale | the locked celebration (§5) | `SmgpFinale()` |
| Career narrative | per-season `CampaignComplete` / `CampaignFlawless` flags on the season cards (title banked at rollover, so the completing season adds its own title when won) | `SmgpNarrativeSeason` build in `CareerSessionService` |
| Newsroom / history | a `CareerCompleted` retrospective feature at the finale season's end (`IsCampaignFinale`), joining the progression events (`LevelMilestone`, `Level300Reached`, `ReturnedFromInjury`) in the archive | `CareerNewsEvents` (`src/Companion.Core/Newsroom/`) |

---

## Accepted deviations

Where the SMGP-300 mission spec asked for more (or different), the repo's decided positions —
carried in `docs/dev/smgp-alpha-finish-status.md` ("Accepted deviations") and the design docs —
stand:

1. **No replacement/substitute drivers during injury.** The spec's substitute model would change
   standings for existing careers unless envelope-versioned and new-career gated. The decided
   model (`docs/dev/character-death-injury.md`) auto-simulates injured rounds with the player
   DNS; the constructor fields one car short and the calendar/history say so honestly (§6).
2. **Campaign completion is survival, not domination.** "Beat all 17" = play through all
   seventeen without `CareerOver`; a flawless 17-title run is a *second, deeper* unlock, not the
   bar (Mike's resolved two-tier decision, `docs/dev/smgp-17-seasons.md` §2). The old 2-title
   `SmgpRules.IsComplete` is deliberately retained as a mid-campaign milestone.
3. **The campaign is terminal — no New Game+, no post-summit carry-on.** Season 18 is
   structurally impossible (§4); the beaten career remains openable (the finale + review lead on
   reopen) but never advances. This resolves `smgp-17-seasons.md`'s open decision 3 in favor of
   a hard summit.
4. **Per-season variety, DNQ re-roll, and grid reshuffle are new-career gated, not universal.**
   Legacy SMGP careers keep season-1 behavior across all seasons forever, because the gates
   (`SmgpState.PerSeasonDnq`/`PerSeasonVariety`/`StandingsReshuffle`) are omitted-false in old
   saves. Byte-identical replay of existing careers outranks uniform behavior — the project's
   save-format contract (`docs/PROJECT.md` §3).
5. **The season ordinal is positional, not persisted.** Rather than the spec's explicit progress
   counter, the ordinal is derived from the append-only season rows (§1) — a counter that cannot
   exist cannot drift, and the campaign length itself lives in exactly one constant
   (`SmgpRules.CampaignSeasons`).
6. **Season years are synthetic.** SMGP is SEGA fiction on one pinned 1990-shaped pack; seasons
   advance +1 year (1990–2006) purely to satisfy the carryover invariant and the archive's
   chronology. There is no historical-calendar evolution — the authored lore eras (§3) carry the
   passage-of-time storytelling instead, and real-world divergence is handled by the separate
   SMGP canon layer (`SmgpCanonDivergence`, SmgpFiction provenance).
