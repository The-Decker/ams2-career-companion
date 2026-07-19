# SMGP-024 Alias and Migration Notes

Date: 2026-07-18. Mission: SMGP-024 canon lock.

## What exists to migrate: nothing

The full-repository scan (audit lane 1, `docs/audits/SMGP_24_TEAM_CANON_AUDIT.md` §4.2)
found ZERO occurrences of LOTUS, AZELIA, AZALIA, or AZALEAH anywhere in SMGP scope
(`data/rules/smgp/**`, `packs/smgp-1/**`, `data/ams2/**`). Every Lotus hit in the
repository is real-world F1 content (`packs/f1-*`, `data/ams2/liveries.json`), which
the alias policy never touches. The shipped SMGP pack spells `team.iris` and
`team.azalea` exactly once each, matching the canon.

## The normalization primitive (shipped, tested)

`Companion.Core.Smgp.SmgpCanon.NormalizeTeamName` resolves any casing of a canonical
team id or display name, plus the registered aliases, to the canonical pack team id:

- `AZELIA`, `Azalia`, `azaleah`, `Team Azalea`, `AZALEA RACING`, `Azalea Motorsport`
  -> `team.azalea`
- `lotus` -> `team.iris` (scoped: only meaningful for an SMGP record that clearly
  means IRIS; real-world Lotus data is never rewritten)
- unknown names (Ferrari, Williams, ...) resolve to null, never to a new team

The alias table lives in `data/rules/smgp/canon.json` (the single registry);
`SmgpCanonLockTests.IrisAndAzalea_ExistExactlyOnce_AndAliasesNormalize` pins the
behavior. Aliases normalize, they are never displayed.

## Why no pack-blob rewrite exists (and must not)

Persisted SMGP saves store team IDS (`team.azalea`), never display strings, in
`SmgpState` (livery strings keyed by driver id) and round envelopes (`ConstructorId`).
The one place display names persist is season-end headline prose, which is safe
because team-id -> name is pack-authored and immutable.

The pinned pack blob is sha256-verified and byte-compared on replay. A migration
that rewrote a pack blob would invalidate every existing save's replay, so no such
migration is offered. Had a legacy pack spelled AZELIA, the correct handling would
have been: load-time normalization of the DISPLAY name only (the blob's hash is
computed before normalization), plus a journal note. With zero occurrences, that
path is documented here and left unbuilt rather than speculative.

## Import/export

Exports carry pack team ids; an import encountering a display-name team reference
resolves it through `SmgpCanon.NormalizeTeamName` first (ids win, snapshots are
validated, spelling differences never create teams). No importer today accepts
display-name team keys, so no code change was required; this note is the contract
for any future importer.
