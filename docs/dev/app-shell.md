# App shell v1 (M4 contract)

WPF + MVVM (CommunityToolkit.Mvvm). **All logic lives in `Companion.ViewModels` (net10.0,
no WPF reference — fully unit-testable); `Companion.App` (net10.0-windows) contains XAML,
value converters, and the composition root only.** Keyboard-first everywhere; startup < 1s;
offline-first; one-screen interstitials (no scope creep).

## Screen map (v1 Driver Career)

1. **Start** — continue career (recent *.ams2career files) / new career / open pack folder.
2. **New-career wizard** (4 steps, one screen each):
   a. Season pick — bundled packs (`packs/` beside the exe) + `Documents\AMS2CareerCompanion\Packs\`.
   b. Content verification — PackStructuralValidator + PackContentValidator + livery scan
      against the detected install; errors block, warnings get **proceed-anyway**.
   c. Seat pick — the pack's entries with driver ratings; player **replaces** that driver
      (v1 locked decision). Shows team tier/reliability so the choice is informed.
   d. Confirm — career name, master seed (random default, editable), rules summary chip
      (points table, best-N, shared-drive policy read from the pack), difficulty note.
      Creates the career DB (pin pack + season row).
3. **Home (two-state)** — career header (season, round, player standing) +
   either **Race Day briefing** or **Enter result** for the current round.
4. **Race Day briefing** — the setup guide as a check-once panel: every in-game setting as
   an exact string with a per-line copy button (track name, laps, date, start time, weather
   slots, opponents count, class);
   **[SUPERSEDED 2026-07-02 — see docs/dev/ux-round.md]: AMS2 settings are arrow-steppers,
   not paste targets; the briefing is a manual check-off checklist ordered like the in-game
   screen, with an optional always-on-top compact mode. Copy buttons are dropped.** placeholder rounds labeled "<GP name> — placeholder:
   <track>" with the real venue + distance note; **Stage grid** button (backup-first staging
   via GridStager into the detected install; result banner shows backup path);
   FileSystemWatcher on the staged XML shows ⚠ "modified outside the app" if it changes
   after staging.
5. **Result entry** — see grammar below. Then **Confirm**: computed points for the round
   (StandingsEngine), standings delta (position arrows), one headline (M5 wire-up; static
   template until then), Apply writes the raw payload + journal.
6. **Standings** — drivers + constructors tabs, gross vs counted points, dropped-round
   markers, Wikipedia-style round matrix, rules-explainer chip. Season review v1 = final
   standings + journal digest.

## Result-entry keyboard grammar (research-digest spec, ~80s for 26 cars)

One text box, one list. The list starts as the round's grid (from RoundGridResolver seats),
ordered by unfilled finishing position (P1 first).

- Type a **car number** (`5⏎`) or **2–3 letters of a surname** (`cla⏎`) — matches against
  UNPLACED drivers only; unambiguous prefix auto-selects; ambiguous shows inline candidates
  (first Tab cycles). Enter assigns the next open position.
- `me⏎` — the player.
- **F8** — switch to DNF phase: remaining drivers listed, Enter marks selected as DNF in
  list order (bulk-confirm ↵↵↵ for "the rest all retired"), optional one-letter reason after
  the match (`hu m⏎` = Hulme, mechanical; letters: m=mechanical, a=accident, o=other) —
  reasons feed the M5 OPI DNF-cause rule.
- `q⏎` on a match — DSQ. Digits after a placed match adjust (penalty) its position.
- **Ctrl+Z** unlimited undo (stack of assignments). **Esc** clears the input.
- Mouse drag remains as fallback; grammar is the primary path.
- Footer shows a live timer + progress (`14/26 placed`) — the <90s target is visible.

## Services seam

`ICareerSession` (Companion.ViewModels) is the app's only gateway to career state:
create/open career, current round + briefing model, stage grid, submit result draft →
confirm model, standings snapshots, advance round. v1 implementation
(`CareerSessionService`) uses CareerDatabase v1 tables + StandingsEngine + packs + Grid
directly. M5 integration (OPI/reputation updates per round, season-end offers flow,
real headlines) extends this service — **additive, do not redesign the seam for it**.

## Verification

ViewModel unit tests (no WPF): wizard step gating, verification proceed-anyway rules,
grammar (every rule above incl. ambiguity, undo, DNF bulk, DSQ, penalties, timer),
briefing copy-string composition incl. placeholder labeling, confirm-model points equal
StandingsEngine output, staging service invoked with backup-first ordering (mock).
App project: compiles, binds (x:Name-free MVVM bindings), `dotnet publish` single exe.
