# SMGP Rival Series (best-of-7) — locked rules

Owner-approved 2026-07-19, supersedes the two-wins ladder for NEW careers.
Design conversation: Mike, after the 17-season campaign made the two-wins
ladder a speedrun ("it should NOT be easy to get into A cars").

## The rules

- A rivalry is a **series: first to 4 race wins** against the named rival.
  Losses are allowed; they cost races, not the streak. Void battles (both
  out) do not count.
- Series state (wins per side) is per-rival and **carries across season
  rollovers**. A 2-2 fight in November is still 2-2 at the next opener.
- **Win the series (4 race wins)** → the seat-swap offer, via the existing
  two-phase deferred promotion screen. Accept: you move into the rival's
  car (CLEAN model, no cascade). A completed series resets to 0-0.
- **Lose the series (rival reaches 4):**
  - Player above LEVEL D → **relegated** to a deterministic-random team in
    the tier below (the existing forfeit path).
  - Player at LEVEL D (not Zeroforce) → **demoted to ZEROFORCE**, like the
    original game.
  - Player at ZEROFORCE → **career over**. The floor's floor; no second
    chances inside the series.
- The old LEVEL D floor counter (4 lost races anywhere at D) is **legacy
  only**; series careers never increment it. Promotion out of D needs no
  wipe because there is nothing to wipe.
- The Madonna **title defense is untouched**: a bespoke two-race event
  (win at least one of rounds 1-2 vs the reserved challenger), never a
  series, never on the tallies.
- Challenge targeting is unchanged (your tier, the tier above, any tier
  below; never two tiers up).

## Compatibility gate

`SmgpState.SeriesLadder` (JsonIgnore WhenWritingDefault): seeded true at
creation for new SMGP careers. Legacy saves parse it false and keep the
two-wins rules byte-identically (same tally blob shape; the two streak
fields mean "series wins" only under the gate).

## Copy rule

User-facing text says "the series" and the score ("series 3-1"), never
"twice" / "two wins", for gated careers.
