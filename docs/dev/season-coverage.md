# Season coverage ledger — every pack, every content dimension

The durable "work every season" worklist (megaprompt Mission C, 2026-07-10). **Re-derive the
countable columns any time with `powershell -File tools/coverage_matrix.ps1`** (CSV, one row per
pack) instead of re-reading 21 packs; this doc adds the judgment columns the script can't know
(ratings provenance, research state, per-season gotchas). Sibling docs: class↔season map =
`ams2-season-coverage.md`, wet-race evidence = `wet-weather-research.md`, custom-race variables =
`ams2-custom-race-reference.md`.

Status legend: ✔ done · ◐ partial (see notes) · ✗ todo · — not applicable.

## The matrix (verified against the repo 2026-07-11, branch `era/1967`)

Countable state (from `coverage_matrix.ps1`): **all 22 packs** have weekend authoring on every
round (durations + 4 weather slots), refuel flags correct (`true` only 1983/1995/1997/2000/2005/
2006/2008), history docs + era-capped fun facts + parseable circuit maps on **every round**, and a
news-era corpus. Era art is 22/22 in tracked `data/ams2/era-art/`; the script's `eraArt` column
still checks the publish-only `dist/` mirror, so it remains false until the next shared publish.

| pack | ratings source | car scalars | per-track AI | form | wet races | alternates | skin season | notes |
|------|----------------|-------------|--------------|------|-----------|------------|-------------|-------|
| f1-1967 | ✔ jusk XML + f1db proxy pace | ✔ reliability 20/20 source liveries (28 staged drivers; no scalars in source) | ✔ 7 rds (6 ratings + 1 livery) | — dropped (jusk) | ✔ 1 (Canadian/Mosport; 10 verified dry) | ✔ 2 (0 gaps) | — | 1960s news = 11 variants/key + 9 pools; 11/11 history circuits use era names; 20-car class cap (2×10 liveries); Bandini/Parkes/Ginther exits authored |
| f1-1969 | ✔ jusk XML + Brack proxy pace | ✔ reliability 26/26 source liveries (28 staged drivers; no scalars in source) | ✔ 3 source rds (2 ratings + 1 swap) | — dropped (jusk) | ✔ verified all-dry | ✔ 4 (0 gaps) | — | 26-livery full-base roster; Piers/Attwood restored; Brambilla Monza swap + Mitter/Hill exits authored; 11/11 history circuits use era names; subset XMLs are rating-identical |
| f1-1974 | ✔ f1db + Realistic skinpack | ✔ 26 | ✔ 5 rds | — dropped (import) | ✔ 3 | ✔ 5 (0 gaps) | ✔ f1-1974 | |
| f1-1978 | ✔ f1db + skinpack | ✔ 22 | ✗ none | ✔ 16 rds | ✔ 1 | ✔ 5 (0 gaps) | — | |
| f1-1983 | ✔ Humpty/TAMS2SP XML | ✔ reliability 24/24 (no scalars in source) | ✗ none in source | — dropped (import) | ✔ 1 wet/drying race; 4 wet qualifying composites | ◐ 2 (1 street gap) | ✔ f1-1983 | 24 active pointers exactly match entries; #33 Guerrero/#36 Giacomelli are source-only optional skins; Cecotto/Boutsen/Johansson windows authored; refuelling enabled |
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
| f1-2010 | ✔ AFry Realistic XML | ✔ 27 | ✔ 13 rds | — dropped (import) | ✔ 5 wet races + 3 wet qualifying composites | ◐ 7 (2 street gaps) | ✔ f1-2010 | 27 drivers/28 selector entries; 309 active-livery source patches; Yamamoto #21→#20 swap + five DNS grids authored; external late-season Heidfeld visor source defect documented |
| f1-2016 | ✔ f1db + Realistic skinpack | ✔ 24 | ✔ 13 rds | — dropped (import) | ✔ 3 (Interlagos Storm) | ◐ 9 (1 gap) | — | |
| f1-2020 | ✔ f1db + Realistic skinpack | ✔ 23 | ✔ 10 rds | — dropped (import) | ✔ 3 (Istanbul) | ✔ 7 (0 gaps) | — | Hülkenberg/Fittipaldi/Aitken subs authored; news = the 2010s era [2010-2029] |
| smgp-1 | ✔ SMGP CustomAIDrivers | ✔ 34 | — by design | — | — always ideal (verified) | — stand-ins by design | ✔ smgp | roster drift pinned by `SmgpRosterDriftTests`; 1989 history pointers on all 16 rounds |

"Street gap" = a placeholder round with **no sensible AMS2 alternate** (Detroit ×3, Phoenix ×2,
Marina Bay ×3, Valencia ×2) — audited twice, legitimately none; not a todo.

## The open work this table surfaces

1. ~~Wet-race research~~ — DONE. f1-1967 was completed 2026-07-10 (only the Canadian GP at
   Mosport was wet; the other 10 rounds verified dry, including a caught false-positive at
   Zandvoort); f1-1983 adds a 15-round deep pass and the new f1-2010 pack adds a 19-round pass.
   See `wet-weather-research.md`.
   Every shipped pack's weather is researched.
2. **2000s ratings depth (2000/2005/2006/2008)** — still f1db-only. A genuine GTIDustin/Alain
   Fry 2006 community release now exists on OverTake, but its pristine archive/XML is not local
   and the live `F-V8_Gen1.xml` is absent, so there is no non-circular import source yet. Acquire
   and hash the original archive before running `tools/import_jusk_ai.cs`; do not synthesize from
   a staged app output. No correct-class source is currently available for the other three.
3. **Skin-season backlog** — ~~1983~~ and ~~2010~~ DONE 2026-07-11. f1-1983 binds the four active
   TAMS2SP pointers; f1-2010 adds the 27-driver/28-entry Formula Reiza pack, generic source pointer,
   and monotonic race variants. Remaining unassigned pointer sets are 1996 and 2012; 1998 has no
   committed skin-season set. Keep 1975 parked until the season-conflict manager.

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
   1983 and 1995–2008) → wet-race research → `tools/author_weather.cs` (cite in
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
