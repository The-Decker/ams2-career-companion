# M5 fix + M4/M5 integration contract

Input: the M5 adversarial audit (2026-07-02). Fixes and the app wiring belong together
because the root cause of the HIGH finding is an integration design point.

## The unified fold (fixes HIGH finding)

One code path produces per-round journal events for BOTH the live path and replay:

1. `round_result_raw.payload_json` becomes an envelope: the ResultDraft-mapped raw
   classification (as today) PLUS the round context that is otherwise unre-derivable:
   `sliderUsed` (the in-game difficulty the player raced at, asked on the result screen,
   default = last recommendation) and the player's DNF reason if any. Grid, teammate finish,
   and expected finish are re-derived from pack + seed + round — never stored.
2. `ReplayService.FoldRound(seasonDb, pack, seed, round)`: recompute standings events
   (as today) AND run `RoundUpdate` with player state folded from the previous round,
   journaling its events (race.result, player.opi, player.reputation, player.paceAnchor,
   news.headline). Persist the post-round player state as a `round_player_state` row
   (season_id, round, state_json) — derived data, wiped on replay.
3. The live path (CareerSessionService.Apply) calls exactly `FoldRound`. Season end
   consumes the round-11-folded player state — per-round rep/OPI accrual now drives offers.
4. `Resimulate` regenerates the identical sequence because it calls the same fold.

## Other fixes (from the audit, in severity order)

- **Replay transactionality:** `Resimulate` runs inside one transaction; COMMIT only when
  `Identical=true`, ROLLBACK otherwise (divergence = report-only, zero data loss). The
  accepted-offer re-application failure (regenerated set lacking the accepted team) is a
  divergence, not a silent drop.
- **Multi-season starts:** replay re-derives season N+1 `start` states from season N `end`
  via the same rollover function the live path uses, and compares against stored rows —
  mismatch is a divergence. (v1 ships single-season careers; the mechanism must still be
  honest.)
- **Headline substitution:** single-pass `{token}` scan (regex), ordinal token lookup,
  substituted values never re-scanned; unknown tokens throw at selection time (template
  bugs surface in tests, not journals).
- **Offers stream key:** vacancy fills key the stream per decision:
  entityId = `vacancy.TeamId + "->" + vacatedByDriverId`.
- **Candidate reputation:** SeatCandidate gains a rep field (pack drivers start at a
  tier-derived default), scoring adds the contract's rep term.
- **Tier headline causes:** pipeline emits `promoted`/`relegated` (matching the authored
  template keys); retirement headlines stay unwired in v1 (documented), but keys must match
  when they arrive.
- **Journal/state parity:** journal the SeasonsCompleted increment; vacancy fills update
  the returned driver states (hired free agent enters `SeasonEndResult.Drivers`).
- **Points cause:** derive the "points finish" cutoff from the round's resolved scoring
  definition, not a hard-coded top-6.
- **Stream key hygiene:** escape `|` in entityIds inside StreamFactory (or throw); add a test.
- **Aging data:** soften the golden-age retirement hazard so front-line drivers plausibly
  race past 40 (Brabham won at 44) — perYearOverBase ~0.07, baseAge 35 for the 60s eras.

## NAMeS-first staging (locked decision #7 — implement in this round)

- **Career creation (wizard):** when the install has a class XML for the pack's ams2Class,
  parse it (lenient parse — community files have malformed comments) and offer
  "Use your installed AI file as the season baseline" (DEFAULT when parseable). Import =
  per-livery ratings/names/countries from the USER'S file override the pack's drivers.json
  values for matching liveries (pack keeps calendar/entries/teams/scoring). Show a summary
  diff (n drivers imported, m pack-only). The imported baseline is pinned into the career DB
  with the pack so it never drifts.
- **Diff-aware staging (GridStager):** before writing, lenient-parse the currently installed
  class XML; if every seat's livery has an entry whose effective fields match the generated
  ones (float tolerance 1e-4, same names), staging is a NO-OP with outcome
  "✔ installed file already matches — nothing written". Otherwise stage backup-first as
  today. The briefing banner always states which of the three happened: no-op / staged
  (with backup path) / aborted (preflight errors).
- **Season-end restore:** the review screen offers one-click restore of the pre-season
  backup (CustomAiBackup.RestoreLatest), with the current state re-backed-up first.
- Tests: import parses jusk's real F-Vintage_Gen1.xml shape (fixture copy) incl. per-track
  override entries (base entries only for baseline import; track-scoped entries stay
  round-level); no-op detection on an equivalent file; staging still writes when a round
  override diverges; restore round-trip.

## App wiring (the M4/M5 integration)

- Result screen asks for `sliderUsed` (prefilled with the last recommendation) and stores it
  in the envelope; the briefing shows the difficulty recommendation from the pace anchor.
- Confirm screen headline comes from `RoundUpdate` (replaces the static template).
- Season completion navigates to a **season review + offers screen**: final standings,
  journal digest (headlines), the offer letters (accept one → journaled; v1 single-season
  careers end there with era transition arriving in M6).
- Home header shows reputation + OPI trend from the folded player state.

## Acceptance

- Full suite green; the E2E season test extended: per-round events journaled, offers scored
  from FOLDED final rep (assert rep at season end ≠ start when results warrant), replay
  byte-identical INCLUDING the per-round events, divergence path proven lossless (tamper →
  rollback → stored data intact).
- Published exe walks the full loop: wizard → briefing (stage grid) → result entry
  (slider prompt) → confirm (real headline) → standings → season review with offers.
