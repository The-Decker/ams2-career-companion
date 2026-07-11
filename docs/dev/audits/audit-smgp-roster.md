# SMGP roster audit — pack data vs the installed skins (2026-07-10)

A verify pass over `packs/smgp-1/{teams,drivers,entries}.json` against the **source of truth**:
the installed skin overrides (`Overrides\formula_classic_g3m{1..4}\*.xml` + the Kobra Fleetworks
`Overrides\mclaren_mp45b\mclaren_mp45b.xml`), mirrored in-repo at
`data/ams2/skin-seasons/smgp/`. The link field is `entries.json.ams2LiveryName`, which the staged
AI file carries verbatim as `livery_name` — if it doesn't byte-match a skin `LIVERY_OVERRIDE
NAME`, the game pool-fills that car with a random driver.

Method: mechanical comparison (hash the mirrors vs the live install; parse every override NAME;
join against entries/drivers/teams on livery string, number-in-name, team prefix, and driver
initial+surname). Guarded forever by `tests/Companion.Tests/Packs/SmgpRosterDriftTests.cs`.

## Verified clean

- All four `formula_classic_g3m*.xml` repo mirrors are **byte-identical** to the live install.
- **34 livery names** installed (32 SMGP universe + Iris/Azalea McLarens); every name unique.
- 24 of 26 entries bound verbatim; numbers embedded in every bound name match `entries.number`;
  every livery name leads with its team's display name.
- All **8 reserves** (Blume #8, White #10, Gould #12, Dehehe #14, Alfven #19, Nono #21,
  Chardin #27, Jones #5) are authored in `drivers.json` with skin-matching names and no entry —
  including Millions #5 N. Jones, which the megaprompt asked to confirm (he was deliberately
  dropped to reserve with Dehehe so the two McLarens fit the class 26-livery cap).

## Findings & resolutions

### 1. Both McLaren entries bound to livery names that don't exist (FIXED — the real bug)

`entries.json` had `Iris #1 B. Salgado` / `Azalea #8 M. Larssen` (numbers "1"/"8"), but the
installed Kobra Fleetworks overrides (v1.1+) are **`Iris #33 B. Salgado` / `Azalea #34
M. Larssen`** — the liveries were renumbered because the 32-car SMGP universe uses every number
1–32 (#1 = Senna, #8 = Blume). Neither McLaren could bind → both pool-filled at staging (this is
the "2 McLaren MP4/5B mod cars pool-filled" contamination from the playtest). Fixed in
`entries.json`, `pack.json` notes, `tools/author_smgp.cs` (regen tool kept in sync), and
`docs/dev/smgp-design.md`. Pack `2.0.0 → 2.0.1`; pinned careers are untouched (new careers only).

### 2. The smgp skin-season set was missing its McLaren pointer file (FIXED)

`data/ams2/skin-seasons/f1-1990/` carries a `mclaren_mp45b.xml` (Marlboro Senna #27/Berger #28),
but the smgp set did not carry one. Sequence: race an f1-1990 round (Skin Season Manager writes
the 1990 set, McLaren → Marlboro) → race smgp (manager writes only the four g3m files) → the
McLaren override **stays Marlboro** and Iris/Azalea vanish even with correct entries. Added
`data/ams2/skin-seasons/smgp/mclaren_mp45b.xml`, a byte-copy of the installed v1.2 file, so the
season swap covers all five models.

### 3. Firenze #3 "Elsser" vs "Felipe Elssler" (NO CHANGE — deliberate)

The skin NAME reads `Firenze #3 F. Elsser`; `drivers.json` says `Felipe Elssler`. The
manual-verified design doc (`docs/dev/smgp-design.md`, roster section) records **Elssler** as the
Sega-canon spelling and "Elsser" as the skin pack's typo. Treatment matches Klinger (below):
`ams2LiveryName` stays verbatim `F. Elsser` (it binds), display name stays canon `Felipe
Elssler`. The megaprompt's "skin wins" note was written without the design doc in hand — binding-
wise the skin already wins; display follows the manual.

### 4. Zeroforce #32 "P. Kilnger" vs "Paul Klinger" (NO CHANGE — deliberate)

The skin NAME embeds the typo `Kilnger`. `ams2LiveryName` keeps the typo verbatim (anything else
breaks binding); the driver display name is the corrected `Paul Klinger`. Correcting the skin
file instead was rejected: it would drift from the distributed skin pack and break on reinstall.

### 5. Stale rule text in pack notes (FIXED in passing)

`pack.json` / `author_smgp.cs` still described the retired "Zeroforce career-over" rule; updated
to the current LEVEL D four-loss career-over (rules v2, commit `79fbe02`).

## Drift guard

`SmgpRosterDriftTests` (runs on every suite pass):

- the mirrored skin set carries exactly the 34-name universe, all unique;
- every `entries.json.ams2LiveryName` exists **verbatim** as an active override NAME;
- every entry's `number` equals the number embedded in its livery name;
- every livery name leads with its team's display name;
- every unbound skin name belongs to exactly one authored reserve driver (no roster holes).

If Mike updates the installed skins (new pack version, renames), re-mirror the changed override
XMLs into `data/ams2/skin-seasons/smgp/` and the tests will point at every entry that drifted.
