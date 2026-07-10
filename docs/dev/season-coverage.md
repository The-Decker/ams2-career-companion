# Season coverage ledger — every pack, every content dimension

The durable "work every season" worklist (megaprompt Mission C, 2026-07-10). **Re-derive the
countable columns any time with `powershell -File tools/coverage_matrix.ps1`** (CSV, one row per
pack) instead of re-reading 20 packs; this doc adds the judgment columns the script can't know
(ratings provenance, research state, per-season gotchas). Sibling docs: class↔season map =
`ams2-season-coverage.md`, wet-race evidence = `wet-weather-research.md`, custom-race variables =
`ams2-custom-race-reference.md`.

Status legend: ✔ done · ◐ partial (see notes) · ✗ todo · — not applicable.

## The matrix (verified against the repo 2026-07-10, heads `fc8169d`)

Countable state (from `coverage_matrix.ps1`): **all 20 packs** have weekend authoring on every
round (durations + 4 weather slots), refuel flags correct (`true` only 1995/1997/2000/2005/2006/
2008), history docs + era-capped fun facts + parseable circuit maps on **every round**, a news
era corpus, and era-art present in `dist/` (uncommitted user art, Mike-managed).

| pack | ratings source | car scalars | per-track AI | form | wet races | alternates | skin season | notes |
|------|----------------|-------------|--------------|------|-----------|------------|-------------|-------|
| f1-1967 | ✔ jusk XML import | ✗ none shipped | ✔ 6 rds | — dropped (jusk) | ✔ 1 (Canadian/Mosport; 10 verified dry) | ✔ 2 (0 gaps) | — | 20-car class cap (2×10 liveries); Bandini/Parkes/Peterson exits authored |
| f1-1969 | ✔ jusk XML import | ✗ none shipped | ✔ 3 rds | — dropped (jusk) | ✔ verified all-dry | ✔ 4 (0 gaps) | — | jusk variant files = trimmed rosters, rating-identical (don't import) |
| f1-1974 | ✔ f1db + Realistic skinpack | ✔ 26 | ✔ 5 rds | — dropped (import) | ✔ 3 | ✔ 5 (0 gaps) | ✔ f1-1974 | |
| f1-1978 | ✔ f1db + skinpack | ✔ 22 | ✗ none | ✔ 16 rds | ✔ 1 | ✔ 5 (0 gaps) | — | |
| f1-1985 | ✔ f1db (rescaled) | ✗ | ✗ none | ✔ 16 rds | ✔ 2 | ◐ 3 (1 street gap) | ✔ f1-1985 | skinpack alternates live in ONE giant XML comment (ActiveSetRewriter handles) |
| f1-1986 | ✔ f1db + Realistic skinpack | ✗ | ✔ 12 rds | — dropped (import) | ✔ verified all-dry | ◐ 2 (1 gap) | — | per-track retirement scripting via deep negative reliability |
| f1-1988 | ✔ f1db composite (exemplar) | ✗ | ✗ none | ✔ 16 rds | ✔ 3 | ◐ 2 (1 gap) | — | ⚠ `F-Classic_Gen2.xml` in the install is OUR OWN staged file — importing it is circular |
| f1-1990 | ✔ f1db + skinpack | ✔ 38 | ✗ none | ✔ 16 rds | ✔ 1 | ◐ 2 (1 gap) | ✔ f1-1990 | per-track variant XMLs (15_JPN etc.) bind by change-point |
| f1-1991 | ✔ f1db + Juppo import | ✔ 26 | ✔ 13 rds | — dropped (import) | ✔ 4 (Adelaide monsoon) | ◐ 2 (1 gap) | — | Jordan #32 Gachot→Schumacher mid-season livery rename authored |
| f1-1992 | ✔ f1db + skinpack | ✔ 39 | ✗ none | ✔ 16 rds | ✔ 3 | ✔ 2 (0 gaps) | — | |
| f1-1993 | ✔ f1db + skinpack | ✔ 35 | ✗ none | ✔ 16 rds | ✔ 5 (Donington) | ✔ 1 (0 gaps) | — | |
| f1-1995 | ✔ f1db + Realistic skinpack | ✔ 33 | ✔ 14 rds | — dropped (import) | ✔ 4 | ✔ 2 (0 gaps) | — | refuel era starts |
| f1-1997 | ✔ f1db + Realistic skinpack | ✔ 26 | ✔ 16 rds | — dropped (import) | ✔ 3 | ✔ 2 (0 gaps) | ✔ f1-1997 | team.lola (Melbourne) authored |
| f1-2000 | ◐ f1db only | ✗ | ✗ none | ✔ 17 rds | ✔ 6 | ✔ 3 (0 gaps) | — | needs a community AI XML when one appears (OverTake) |
| f1-2005 | ◐ f1db only | ✗ | ✗ none | ✔ 19 rds | ✔ 1 | ✔ 6 (0 gaps) | — | ditto; livery cap < field history — deep-pass candidate |
| f1-2006 | ◐ f1db only | ✗ | ✗ none | ✔ 18 rds | ✔ 2 | ✔ 6 (0 gaps) | — | ditto |
| f1-2008 | ◐ f1db only | ✗ | ✗ none | ✔ 18 rds | ✔ 6 (Silverstone, Monza wet quali) | ◐ 8 (1 gap) | — | ditto |
| f1-2016 | ✔ f1db + Realistic skinpack | ✔ 24 | ✔ 13 rds | — dropped (import) | ✔ 3 (Interlagos Storm) | ◐ 9 (1 gap) | — | |
| f1-2020 | ✔ f1db + Realistic skinpack | ✔ 23 | ✔ 10 rds | — dropped (import) | ✔ 3 (Istanbul) | ✔ 7 (0 gaps) | — | Hülkenberg/Fittipaldi/Aitken subs authored; news = the 2010s era [2010-2029] |
| smgp-1 | ✔ SMGP CustomAIDrivers | ✔ 34 | — by design | — | — always ideal (verified) | — stand-ins by design | ✔ smgp | roster drift pinned by `SmgpRosterDriftTests`; 1989 history pointers on all 16 rounds |

"1 street gap" = a placeholder round with **no sensible AMS2 alternate** (Detroit ×3, Phoenix ×2,
Marina Bay ×2, Valencia) — audited twice, legitimately none; not a todo.

## The open work this table surfaces

1. ~~f1-1967 wet-race research~~ — DONE 2026-07-10 (the wet-1967-research workflow: only the
   Canadian GP at Mosport was wet; the other 10 rounds verified dry, incl. a caught false-positive
   at Zandvoort). See `wet-weather-research.md`. Every pack's weather is now researched.
2. **2000s ratings depth (2000/2005/2006/2008)** — f1db-only; no community Custom-AI XML existed
   at last check. Re-check OverTake occasionally; import via `tools/import_jusk_ai.cs` when one
   lands (verify the header credits a real community author first — the 1988 trap below).
3. **Skin seasons declared but unassigned** — sets exist in `data/ams2/skin-seasons/` for
   1975/1983/1996/2010/2012, whose packs are BACKLOG seasons (with 1998; 1975 parked until the
   season-conflict manager). Authoring any of them starts at parity with this pipeline.

## The repeatable per-season pipeline (bring any season to parity)

In order, all from repo root; every step dry-runs by default and each has a worked exemplar:

1. **Roster/grid**: research the real season roster → `tools/apply_roster.cs` +
   `tools/max_grid.cs` (car = team+number, per-stint livery names; real substitutes > alive-and-
   active extension > honest gap, never past a death/exit).
2. **Ratings**: community Custom-AI XML via `tools/import_jusk_ai.cs` (`--drop-form` when
   per-track blocks ship) — else `tools/derive_ratings.cs` + `derive_form.cs` from f1db.
   ⚠ Verify the XML header is genuinely community-authored — `F-Classic_Gen2.xml` on this
   machine is our own staged output (header "AMS2 Career Companion | ... | Round N").
3. **Weekend/weather**: `tools/author_weekend.cs` (durations, 4 slots, `--refuel` for
   1995–2008) → wet-race research → `tools/author_weather.cs` (cite in
   `docs/dev/wet-weather-research.md`).
4. **Tracks**: verify against `data/ams2/tracks.json` (regen: `tools/extract_tracks.cs`) —
   defaults must be base/DLC only; curate mods as opt-in `tools/author_alternates.cs` rows.
5. **History/maps/facts**: `tools/derive_history.cs` + `tools/derive_circuits.cs` (era-capped by
   construction — never leak post-season results).
6. **Skins**: map installed override pointer XMLs into `data/ams2/skin-seasons/<key>/` + set
   `pack.json.skinSeason`; per-race variant XMLs bind automatically (change-point sets).
7. **Fuel**: extend `FuelGuidance` if the class is new (tank litres + one-tank laps).
8. **Validate**: `dotnet test` — `ReferencePackTests` holds every pack to the exemplar bar
   automatically; add a pack-specific drift/guard test only for pack-specific invariants
   (`SmgpRosterDriftTests` is the model).

SMGP replica extras (careerStyle packs): per-round `history` pointers onto the modeled real
season + `tools/author_smgp.cs` full regen (keep the tool in sync with every hand fix — it has
gone stale once).
