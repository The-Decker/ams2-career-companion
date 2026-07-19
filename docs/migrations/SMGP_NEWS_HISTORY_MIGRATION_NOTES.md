# SMGP news/history migration notes (mode-separation finalization)

_2026-07-18, Claude (Head of Coding). The save-compatibility position for the narrative
separation work. Short version: **no migration is needed, and nothing was migrated.**_

## Why no migration is needed

1. **Mode ownership was never stored per article, and never needed to be.** Narrative content
   derives from journal rows and career state; it is never persisted as standalone
   mode-tagged records. A career file's mode lives on its start player state
   (`PlayerCareerState.ExperienceMode` / the pack's `careerStyle`), so an old save's mode is
   inferred exactly as before, byte-identically.
2. **The separation is enforced at generation time, not at rest.** Era-key resolution
   (`PreferredEra = "smgp"` only inside SMGP careers), mode-gated event kinds (economy kinds
   require an actual `DynastyEconomyState`, which non-Dynasty careers can never carry), and
   fiction provenance (`SmgpFiction`) are all evaluated when articles render, against the
   career's own state. An old save renders under the correct mode automatically.
3. **The new per-season packs are display-only corpus data.** `data/rules/newsroom/seasons/`
   loads through the tolerant `LoadDirectory` merge; absent files degrade to today's behavior
   (era-voiced generic templates). The `Seasons` eligibility field is absent-tolerant
   (empty = any season), so pre-existing templates need no change.
4. **Old saves replay byte-identically.** Nothing in this wave is a fold input: no envelope
   change, no journal phase change, no state-field change. Resimulate proofs remain green
   (newsroom integration resim test + the 17-season release-evidence run).
5. **Published copy is persistent by construction** (deterministic seeded rendering), so an
   old save's news reads exactly as it did before, and new packs only ADD eligible candidates.

## Compatibility guarantees

- SMGP saves: unchanged behavior; new season packs add season-flavored variants where eligible.
- Dynasty saves: unchanged; economy events still require `DynastyEconomy` (opt-in at creation).
- Racing Passport saves: unchanged; pure racing, no fictional layer.
- Pre-season-pack saves: byte-identical feed (the packs are additive eligibility, not a change
  to existing templates).

## If a future schema tags narrative rows with an explicit mode id

Should a later schema persist articles with an explicit `mode` column, migration will be:
additive nullable column (WhenWritingNull), backfilled from the career's start-state mode on
first open, idempotent by `null`-guard, with a pre-upgrade `VACUUM INTO` backup (the existing
migration-chain contract).
