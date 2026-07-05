# Character system vision (Mike's direction, 2026-07-03)

Verbatim ask: "When players create their character they can fully customize them, there are
perks and negatives that come with each perk to balance all perks. we want at least 33 perks
that can be done. there can be as many perks with negatives but they all must balance so people
can freely choose any and it will work out in the games or not work out. levels and stats,
everything possible for the character."

## What this means

A deep character-RPG layer on the Driver Career: creation with full customization, **stats**,
**levels/progression**, and a **perk/trait system of 33+ perks** where every perk is
**self-balancing** (a benefit paired with a drawback) so a player can pick freely and it will
"work out or not" as the career unfolds — no dominant/strictly-best choice.

## Non-negotiable constraints (inherit from PLAN.md + the sim)

- **The sim never decides races.** Perks/stats shape deterministic INPUTS (the generated
  custom-AI ratings + the player's own car scalars, offer scoring, income, aging, injury
  hazard) and consume OUTPUTS (results → OPI/reputation/XP). No hidden dice-roll races.
- **Deterministic + journaled + replayable.** Perk effects fold through the existing named
  PCG32 streams + journal; the same seed + results reproduce the same career byte-for-byte.
- **Data-driven.** Perks, stats, and level curves live in user-editable JSON packs
  (`data/rules/perks.json` etc.), validated on load — community-extensible like everything else.
- **Balance is the whole point.** Each perk's benefit and drawback must be quantified against
  the real sim levers so free choice is genuinely viable; a balance audit is part of the design.
- **Additive.** Extends the character-creation wizard + the career sim without breaking the
  existing loop, saves, or the `ICareerSession` seam.

## Deliverable (this session)

A design spec (`docs/dev/character-system.md`) + a starter `data/rules/perks.json` with 33+
balanced perks, a stats model (mapped to the custom-AI rating vocabulary + career meta-stats),
and a levels/XP/progression model — grounded in the deterministic sim, adversarially
balance-audited. Implementation (wiring into the sim + the creation UI) is a later milestone.
