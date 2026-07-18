# Alpha 1.0 career modes

_Product contract, 2026-07-12. Mike's three-mode direction supersedes the earlier two-mode menu and the statement that historical tycoon play is necessarily post-alpha. Display names remain tunable; stable IDs and save boundaries do not._

## 1. The three modes

| Stable ID | Working display name | Player promise | Career shape |
|---|---|---|---|
| `grandPrixDynasty` | **Grand Prix Dynasty** | Be the driver-owner across the historical World Championship era. | One chronological historical timeline, product horizon 1960–2020. |
| `smgp` | **Super Monaco GP** | Play the authored SEGA-inspired rival/seat-swap career. | One separate 17-season SMGP campaign. |
| `racingPassport` | **Racing Passport** | Choose a season. Take a seat. Go racing. | One independent faithful historical season per save; pure racing. |

The display names are working names. Alternatives can be tested without changing serialized IDs:

- `grandPrixDynasty`: World Championship, Grand Prix Legacy, World Championship Legacy.
- `racingPassport`: Open Paddock, Driver's World, Career Atlas.

Do not use “Racing Life”; the founding market audit already identifies a separate discontinued product with that name.

The currently shipped semi-historical driver career is implementation scaffolding for these modes, not a fourth Alpha 1.0 mode. A faithful historical season belongs either to the sequential Dynasty timeline or to a Passport thread.

## 2. Mode matrix

| Rule | Grand Prix Dynasty | Super Monaco GP | Racing Passport |
|---|---|---|---|
| Root character | One | One | None (pure racing) |
| Career entities | One sequential timeline | One SMGP entity | Many independent one-season saves |
| Thread styles | Historical only | SMGP only | Historical only (never SMGP) |
| Owner/team economy | Driver-owner; required product pillar | No historical tycoon economy | None |
| Calendar | Chronological historical seasons | 17 ordinal seasons | One faithful season, free choice |
| Progression horizon | Start through 2020 | Seasons 1–16 mastery, season 17 finale | None (no XP/SP/DNA/skills) |
| Switching | No | No | N/A (each save is independent) |
| Injury/death | Root-career state | Root-career state | Mortality Off, not offered |
| AMS2 staging | Current next round | Current next round | Current next round |

Grand Prix Dynasty and Super Monaco GP use progression version 2 for new characters: L300, 30
Racing DNA identities, 90 mastery skills, and the 499-SP lifetime mastery pool. Racing Passport
deliberately creates NO character and seeds NO progression at all.

### 2.1 Exact legacy gate

The new serialized mode discriminator is optional/default-omitted for compatibility. Dispatch is exact:

```text
if progressionVersion < 2 OR experienceMode is absent:
    internal behavior = legacySingleCareer
else:
    experienceMode must be grandPrixDynasty | smgp | racingPassport
```

`legacySingleCareer` is an internal compatibility path, not a fourth Alpha mode and never a value written into an old save. It retains the current discovery, fold, character, continuation, and GUI behavior byte-for-byte. An absent field must never infer `grandPrixDynasty`, because that would activate owner/timeline rules in existing historical careers.

## 3. Grand Prix Dynasty

`grandPrixDynasty` is the historical driver-owner simulator. The product horizon is 1960 through 2020, but content availability remains honest: a year is playable only when a faithful season pack exists. Missing years are unavailable, never synthesized by mixing grids or borrowing a neighboring season.

Alpha contract:

- the character is both driver and team owner;
- the timeline advances chronologically through pinned historical packs;
- standings, seats, team ledger, staff/second driver, sponsors, development, repair costs, prize income, and bankruptcy all belong to this save;
- character age, injury, mortality, contracts, reputation, and progression are global to the timeline;
- the campaign progression plan contains an ordered `pinnedSeasonSequence` of the faithful packs available from the chosen start through 2020 at creation;
- `totalSeasons`, XP scale, and mastery timing derive only from that sequence and never recalculate when later packs are installed;
- SMGP packs are excluded from discovery and can never appear as a Dynasty year.

The complete 1960–2020 pack library is a content horizon, not permission to fabricate data. Alpha UI may show unavailable years/eras with an explicit coverage label and must show how many seasons the new save will actually pin. New faithful packs are available to new saves only; they never splice themselves into an existing Dynasty timeline.

## 4. Super Monaco GP

`smgp` retains the existing separate-career identity and every locked SMGP rule:

- 17 seasons and 16 races per season;
- A. Senna remains the permanent OP benchmark;
- rival challenges, clean two-win seat swaps, DNQ field, promotion/demotion, Paddock, dispatches, and finale state remain SMGP-gated;
- seasons 1–16 continue within `smgp-1`; season 17 terminates the campaign;
- historical pack discovery never consumes an SMGP pack.

The L300 progression plan releases the complete 499-SP pool at the season-16 review for a driver who has reached L300, allowing the final season to be driven with the complete build.

## 5. Racing Passport (pure racing — the 2026-07-18 decision)

**SUPERSEDED:** the shared-progression Passport (one persistent driver, portfolio XP ledger,
multi-thread saves) previously specified here is retired and must not be built. What ships
instead (implemented in `docs/dev/racing-passport-pure-racing.md`):

`racingPassport` is the open historical-season experience — pure racing:

1. Choose Racing Passport on the start screen (the card is available).
2. Choose any installed faithful historical series/season (any year; no chronological gate;
   SMGP packs never appear).
3. Choose the existing driver and team whose seat the player takes (one authored entry; the
   whole faithful field races; no grid editor, no own-entrant, no cascade).
4. Optionally customize the player driver's displayed name (the one identity field; no
   character creator, no nationality/age/portrait/DNA/traits/perks).
5. Race the complete season: staging, qualifying, results, standings, circuits, history, news,
   records, and the season review through the ordinary deterministic fold.

Absolute rules:

- **No progression.** No XP, levels, Skill Points, Racing DNA, mastery, perks, skill plans, or
  resets — not even zero-valued or dormant state. No `player.character` /
  `player.statSpend` / `player.skillPlan` / `player.skillReset` / `player.xp` rows are written.
- **No owner economy.** No Dynasty state, sponsors, staff, development, repairs, prize ledger,
  or bankruptcy. `DynastyEconomy` is rejected as contradictory input for Passport.
- **One independent save per season.** Each Passport creation is an ordinary self-contained
  `.ams2career` pinning exactly one faithful season (bytes/version/hash at creation). No
  container, no threads, no ledger, no shared profile; the career gallery is the collection.
- **One season is the campaign.** The final review crowns the champion and the career stays
  reopenable/reviewable; there is no contract offer, no market, no aging/retirement, and no
  automatic next season. Mortality is Off and not offered.

### 5.1 Superseded design (history only)

The retired shared-progression model (root character, activity ledger, credited-experience keys,
portfolio SP gate, thread switching) survives only in git history. Former sections 5.2-7 of this
document specified it and were removed on 2026-07-18; the section numbers 6 and 7 are left vacant
so existing references to sections 8-10 keep working. Any document or comment still presenting
the retired model as the active contract is outdated and must be corrected.

## 8. GUI contract

The new-career entry presents exactly three primary cards. Each card states its persistence model before creation.

Passport uses the ordinary single-career wizard reshaped into its pure-racing route (SeasonPick
→ Verification → SeatPick → Confirm): no character creator, no custom grid editor, no mortality
picker, no economy choice. The seat step carries the one identity field (optional driver display
name). The confirm states the series, year, team, replaced driver, display name, the faithful
full field, and the explicit no-progression / no-management lines. The career hub is the ordinary
historical hub; progression surfaces (Driver dossier, XP toasts, the Team Ledger) are simply
absent for a Passport career.

## 9. Required verification

- Legacy v0/v1 careers remain byte-identical and infer their current single-career behavior when the new mode field is absent.
- An absent mode field never dispatches Grand Prix Dynasty/SMGP/Passport mechanics or serializes a replacement value.
- All new saves persist exactly one of the three stable mode IDs.
- SMGP never appears in Dynasty or Passport historical discovery.
- A Passport save pins exactly one faithful season and persists `experienceMode = racingPassport`.
- A Passport save contains no XP/SP/DNA/skill/economy state and no progression journal rows.
- A Passport request carrying a character, `DynastyEconomy`, `SmgpMode`, or an SMGP pack is rejected as contradictory input.
- The Passport wizard offers every faithful year with no chronological gate, skips the character and grid steps, and validates the optional driver name.
- The replaced driver is replaced exactly once (the player wears that identity); every other entry keeps its authored seat.
- A Passport season completes without offers, market, aging, retirement, tier drift, or rollover; the review stays reopenable and replay byte-identical.
- Non-championship events grant zero global v2 XP/reference progress in every mode.
- L285 before the mastery checkpoint cannot purchase a second family capstone.
- Strong SMGP reaches L300/full 499 SP after season 16.
- Dynasty full and late-start campaigns use their pinned bounded horizon.
- The 77/77 f1db oracle remains untouched.

## 10. Implementation order

1. Add an additive top-level mode discriminator and legacy inference; do not change current folds.
2. Finish progression-version-2 dispatch at L300.
3. Fix style-scoped SMGP continuation/discovery.
4. ~~Introduce Passport container state~~ — SUPERSEDED 2026-07-18: activate pure-racing Passport
   (planner branch, wizard route, one pinned season, no progression or economy seams). DONE.
5. Reuse the existing historical and SMGP folds unchanged.
6. Build the Dynasty owner/team economy on the same immutable historical timeline boundary. DONE (2026-07-17).
7. Dynasty chronological gating + locked-season previews. DONE (2026-07-17).
8. Run the full deterministic, render, replay, and oracle gates before Alpha 1.0.
