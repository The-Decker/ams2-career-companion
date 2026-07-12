# AMS2 Career Companion — Project Guide

> The single onboarding doc for the whole project. Read this before touching any part.
> House rule: **ground everything in the actual repo** — real types, real files, real docs. Do not invent.
> Repo root: `Z:/Claude Code/ams2-career-companion` · Solution: `Companion.slnx` (XML solution, **no `.sln`**) · .NET 10 · Windows/WPF.

---

## 0. Orientation for whoever is taking over

You (Codex) may be holding **both** lanes: Head of Coding (`Companion.Core` / `Companion.ViewModels` / `Companion.Data` + tests + `data/rules`) **and** Head of GUI (`src/Companion.App/**` + Themes + art). During the 2026‑07‑16 handoff window Codex runs both as two parallel instances; when Claude resets it takes back Head of Coding and Codex keeps Head of GUI.

**Lane boundary (strict):** the CODE lane never edits `src/Companion.App/**`; the GUI lane never edits `Core`/`ViewModels`/`Data`/`tests` **except** `tests/Companion.RenderHarness.Tests/**` (specifically the render‑test stand‑in hosts). The parallel‑safe mechanism is the **Slice‑0 stub commit**: the CODE lane publishes every bind‑contract member returning empty/default first, so the GUI lane can bind real names and lay out before the logic lands.

The four things you must never break, in priority order:
1. **The f1db oracle** (77/77 season fixtures) — never touched.
2. **Byte‑identical re‑simulation** — live fold == replay fold, always.
3. **`Companion.Core` purity** — no I/O, no WPF, no DB.
4. **Save‑format stability** — determinism primitives + journal/stream string vocabularies are byte‑stable forever.

Everything below explains how to honor those while changing anything.

---

## 1. What the app IS

A **Windows desktop app (WPF, .NET 10, single self‑contained exe `AMS2CareerCompanion.exe`, v0.6.0)** that runs **historical + replica career seasons around Automobilista 2 (AMS2) single‑player custom races**. AMS2 has no career mode for old grids; this app is the career layer *around* the game. You race a weekend in AMS2, type the result into the app, and the app folds it into a deterministic, journaled, replayable career: standings, championship, driver progression, seat changes, news, the works.

**Product vision (`PLAN.md`, founding 2026‑07‑02):** faithful single historical seasons (never fantasy/mixed‑year grids — see locked directions), a real RPG career layer, and total determinism so a career is a reproducible artifact. The app OWNS the AMS2 staging (custom‑AI XML, liveries) so a mod manager can't undo it.

### Two career modes

- **Semi‑historical F1 careers** — pick a real season pack (`packs/f1-1967` … `packs/f1-2020`), take a seat, race the calendar. Scoring is 100% data‑driven per era.
- **SMGP replica mode** — a SEGA *Super Monaco GP* replica career (`packs/smgp-1`, `careerStyle:"smgp"`). Rival ladder, two‑wins seat swaps, title defense, a 17‑season campaign, DNQ field, the Paddock, dispatches, tycoon dashboard. **A. Senna is the permanent OP benchmark, never nerfed or dropped.** SMGP is a *separate career entity*, not a pack in the historical gallery.

---

## 2. Architecture — 5 projects, strict inward dependency flow

| Project | Target | Owns | Never |
|---|---|---|---|
| **Companion.Core** | `net10.0` | Pure domain: points/standings engine, career sim, pack loading, determinism primitives, character/SMGP logic, fold math | **NO I/O, NO WPF, NO DB, no ambient state** |
| **Companion.Data** | `net10.0` | SQLite‑per‑career persistence (Microsoft.Data.Sqlite, WAL), forward‑only migrations, the **ONE fold/replay engine** (`ReplayService`), pinned packs | — |
| **Companion.Ams2** | `net10.0-windows` | Steam detect, custom‑AI XML writer + backup/restore, class/livery/track content library, skin staging, pack content validation, preflight | — |
| **Companion.ViewModels** | `net10.0` (**no WPF ref**) | MVVM (CommunityToolkit.Mvvm), `CareerSessionService` (the `ICareerSession` impl), all VMs | No WPF — fully unit‑testable |
| **Companion.App** | `net10.0-windows` | WPF shell, Views, converters, themes, composition root | No domain logic |

Dependency direction is **inward**: App → ViewModels → (Data, Ams2, Core); Data → Core; Ams2 → Core. **Core depends on nothing.** Core's purity is load‑bearing: everything deterministic lives in Core with no ambient state, so Data can re‑execute it verbatim for replay.

Docs: `docs/dev/app-shell.md` (M4 layering contract).

---

## 3. THE DETERMINISM / REPLAY SPINE — the single most important system

Everything in a career is derived from journaled inputs via a **pure fold function**. `state = fold(journal)`. Live entry and replay call the **same** pure function with the **same** inputs+seed, so replay regenerates the stored journal *by construction*. Divergence = tampering, changed payload, or changed engine.

Design docs: `docs/dev/career-sim.md`, `docs/dev/m5-fix-integration.md`.

### 3.1 Determinism primitives (byte‑stable FOREVER)

Four classes in `src/Companion.Core/Determinism/`. Each carries an explicit "changing this is a breaking save‑format change" doc comment.

- **`StableHash.Fnv1a64`** — hashes strings over UTF‑8. **Never `string.GetHashCode`** (per‑process randomized).
- **`SplitMix64`** — canonical seed expander.
- **`Pcg32`** — faithful port of O'Neill's PCG‑XSH‑RR (`pcg32_random_r` + bounded‑rand threshold rejection).
- **`StreamFactory`** — ties them together. Stream seed = `SplitMix64(Fnv1a64(subsystem|year|round|entityId) XOR masterSeed)`, expanded into the generator's `(initState, initSeq)`. **Each `CreateStream` returns a FRESH generator at the start of its stream**, so consuming one stream never shifts another and re‑creating a stream replays it from the beginning — this is exactly what makes "re‑simulate from round 1" byte‑identical. EntityIds are backslash‑escaped so the 4‑part key is injective.

### 3.2 The named RNG streams + journal phases (the save‑format vocabulary)

`src/Companion.Core/Career/CareerStreams.cs`:
- **`CareerStreams`** — the ONLY subsystem stream names the sim may consume: offers, aging, retirement, form, events, headlines, tier‑drift, plus **opt‑in** injury/accident/auto‑race. **Never rename.**
- **`JournalPhases`** — every phase string. Tagged **DERIVED** (byte‑compared) vs **INPUT/provenance‑excluded** (player choices the fold can't re‑derive: `player.character`, `player.gridSelection`, `player.statSpend`, `smgp.swap`). **Never rename.**

**Gating is what preserves old saves.** Opt‑in streams (injury/accident/auto‑race) are drawn ONLY under a gate (mortality != Off, has character, this round is an accident, …). A default career draws **ZERO** from them and stays byte‑identical to a pre‑feature save.

### 3.3 `RoundResultEnvelope` versioning

`src/Companion.Data/ResultStore.cs`. The raw per‑round payload in `round_result_raw`, **`CurrentVersion = 8`**. Wraps the engine's `RoundResult` plus context that's otherwise un‑re‑derivable: `SliderUsed`, `PlayerDnfCause`, and version‑gated additions:

| Ver | Added |
|---|---|
| v3 | `QualifyingOrder` |
| v4 | `IsWet` |
| v5 | `CalledShot` |
| v6 | `SmgpRival` |
| v7 | `PlayerAccidentSeverity` |
| v8 | `PlayerDidNotStart` |

**Every new field is nullable/defaulted so older payloads parse unchanged and fold to the SAME journal** (e.g. `IsWet` null → neither `wetRound` nor `dryRound` perk fires). `Parse()` also accepts the v1 bare‑`RoundResult` shape. Grid, teammate finish, expected finish are **deliberately NOT stored** — re‑derived from pack+seed+round every fold. `QualifyingOrder` is deliberately **outside `RoundResult`** so it never reaches the standings engine (oracle untouched).

**Two independent versioning axes:** SQLite schema `user_version` (`Migrations.cs`, currently 5) governs table shape; `RoundResultEnvelope.Version` governs payload shape. Both designed so old data reads forward without changing folded output.

### 3.4 The ONE fold code path

`src/Companion.Data/ReplayService.cs` is the single fold implementation.
- **`ComputeRoundFold`** — a PURE function of `(pack, masterSeed, inputs, startTeams, roundsSoFar, envelope, round, playerAge, previousState)`. Emits standings events, then (when the player is on the grid) the `RoundUpdate` events (OPI/reputation/pace‑anchor/XP/headline).
- **`FoldRound`** wraps it with DB reads/writes in one transaction; refuses to fold a round twice.
- **`ImportAndFoldRound`** — the live path's atomic "store envelope + fold". **The live path CANNOT bypass the fold.**
- **`Resimulate`** — re‑executes the identical `ComputeRoundFold` over stored raw results.

### 3.5 Byte‑identical re‑sim (report‑only, transactional)

`ResimulateCore`: wipes **DERIVED state only** (never raw results, pinned packs, stored journal, or `start` states — those are inputs), then in **ONE transaction** refolds every season round‑by‑round, re‑runs season ends, re‑derives follow‑on start states (`SeasonRollover` same‑pack or `EraTransition` pack‑changeover), and **byte‑compares** the regenerated journal against the stored one (seq/utc excluded, provenance rows excluded).

- **Commits ONLY when fully byte‑identical; ANY divergence rolls back.** A divergence report never costs data.
- `CompareSeason` walks rows checking phase→entity→deltaJson→cause→round, reports the first mismatch.
- Player choices (accepted offers, `smgp.swap`) are re‑applied when consistent; an accepted team missing from the regenerated offer set is a **divergence**, never a silent drop.
- Two overloads: single pinned pack, and multi‑pack era transitions (each season resolves its own sha256‑verified pinned pack).

### 3.6 The f1db oracle — SACRED

`tests/Companion.Tests/Oracle/F1DbOracleTests.cs` + 77 fixtures in `tests/Companion.Tests/Fixtures/f1db/*.json` (1950–2026, generated from the f1db SQLite release CC BY 4.0 via `tools/Companion.FixtureGen`). Each fixture replays its raw results through `StandingsEngine.ComputeSeason` using the real `f1-points-systems.json` catalog and asserts the Final snapshot equals official f1db standings. **77/77, never touched** (`AGENTS.md`). Because it exercises the exact same engine + rules data the career mode scores with, era‑scoring changes are caught immediately. Career context (qualifying order, etc.) is kept out of `RoundResult` specifically so the oracle path is unaffected.

### 3.7 Per‑career gating for grid changes

To alter the grid/field without breaking replay, apply the change as a **deterministic pack transform pinned at career creation** — the fold just reads pinned bytes, no seed threading. Canonical: `SmgpDnqField` (rolls each capped round's qualifiers, pins into `season.json`), like `AlternateTrackTransform` / `ModddedFieldTransform`. The trickier case is when the starter set IS a fold input (grid membership → seat strength → byte‑compared player rows): it must be applied **identically on live‑fold pack (`CareerSessionService`) AND replay pack (`ResimulateCore`)**, both fed the same season ordinal, a pure function of `(pack, ordinal, seed)`. Gated on `SmgpState.PerSeasonDnq` so legacy careers never re‑roll.

### 3.8 THE INVARIANTS — rules any change must follow

1. **Core stays pure** — no I/O/WPF/DB, re‑executes identically for replay.
2. **Determinism primitives + `CareerStreams`/`JournalPhases` strings are byte‑stable FOREVER.** Changing any = breaking save‑format change.
3. **New fold behavior is envelope‑versioned AND opt‑in gated** so default/legacy careers draw zero new RNG and emit the identical journal sequence.
4. **DERIVED journal rows are byte‑compared; player‑choice INPUT rows are provenance‑excluded and re‑applied.**
5. **Pack/grid changes affect NEW careers only** (pinned at creation) or, if a fold input, applied identically on live+replay as a pure function of `(pack, ordinal, seed)`.
6. **The f1db oracle is never touched** — scoring quirks stay data; career context stays out of `RoundResult`.
7. **Replay is report‑only and transactional** — commit only on byte‑identity, rollback on any divergence, never lose data.

Fold outcomes are **Round4‑quantized** before comparison. New `PlayerCareerState` fields must be `[JsonIgnore(WhenWritingDefault)]`.

---

## 4. Points / standings engine

`src/Companion.Core/Scoring/`. **Pure, data‑driven, no hard‑coded era logic** — every scoring quirk is DATA (`CLAUDE.md` locked).

- **`StandingsEngine.ComputeSeason`** — pure static: replays `RoundResult` classifications through a `PointsSystem`, emits one `StandingsSnapshot` per round with `GrossPoints` vs `CountedPoints` and explicit `Dropped` lists.
- **Exact rational arithmetic** via `Companion.Core.Numerics.Rational` — a readonly struct over 64‑bit ints, normalized (binary GCD), denominator stored **offset by one** (`_denominatorMinusOne`) so `default(Rational)` is valid zero. All ops `checked`; comparisons cross‑multiply in `Int128`. Serializes as `"3"` / `"1/7"`. **No floating point ever touches championship points** (this is what represents 1/7‑point fastest‑lap splits and half‑points exactly).
- **Scoring quirks, all as data:** era points tables (`RacePoints`/`SprintPoints`/`AlternateRaceTables`), fastest‑lap fractional split‑on‑tie (`FastestLapRule`, 1/7), best‑N dropped incl. 1967–1980 split‑season segments (`BestNRule.Segments`), shared‑drive Split‑vs‑Zero (pre/post 1958), constructors best‑car‑only 1958–78, 1961 constructors‑only race table, half/double via `RoundResult.PointsFactor`, Indy constructors exclusion (`CountsForConstructors`), per‑position redirects (`PointsPosition`, 1967 German GP F2 cars), excluded drivers/constructors (1997 Schumacher keeps points/no position; 2007 McLaren zeroed), flat `PointsAdjustments`.
- **Positions:** standard competition ranking (ties share) + lexicographic **countback** tiebreak on finish‑position histograms.
- **Championship‑round domain ≠ calendar.** `ChampionshipCalendar` (in Data) is the single mapping: `RoundCount` counts `Championship==true`, `Ordinal` = 1‑based championship position, `IsChampionshipRound` gates which stored results feed the engine. Non‑championship events are folded (player state carried) + journaled but never enter `StandingsEngine`.

Serialization: everything Core owns goes through **`CoreJson.Options`** (camelCase, case‑insensitive read, enums as camelCase strings, `WriteIndented`, `RationalJsonConverter`). Journal deltaJson uses the same conventions single‑line. Canonical serialization is what makes byte‑comparison meaningful.

---

## 5. Packs / seasons + the season‑pack format

Design: `docs/dev/season-pack-format.md` (v1.1, M2).

A **`SeasonPack`** (`src/Companion.Core/Packs/SeasonPack.cs`) is five JSON files as one aggregate:

| File | Holds |
|---|---|
| `pack.json` | `PackManifest` — packId, version, formatVersion, `requires.dlc`/`skinPacks[]`, notes[]; SMGP adds `skinSeason` + `careerStyle` |
| `season.json` | `SeasonDefinition` — year, `ams2Class` (EXACT xmlName), pointsSystem, `rounds[]` (track{realVenue,id,isPlaceholder,fallbacks}, laps=100% distance, weekend/setupGuide, guestEntries, aiOverrides) |
| `teams.json` | `PackTeams` — carVehicleIds, performance scalars, reliability, prestige, budgetTier |
| `drivers.json` | `PackDrivers` — custom‑AI rating vocabulary 0.0–1.0, trackForm nudges |
| `entries.json` | `PackEntries` — teamId+driverId+number+rounds range (mid‑season swaps) + **`ams2LiveryName`** (the load‑bearing exact‑match binding string) |

- **`PackLoader.Parse` is pure** (Core has no I/O — callers hand in strings); rethrows `JsonException` prefixed with the file‑part name.
- **Packs are IMMUTABLE and PINNED:** copied + sha256‑hashed into the career DB at season start (`pinned_pack` table). Careers rehydrate from the **pinned blob**, never the mutable `packs/` folder. `PinnedPackEnvelope` stores all five parts verbatim with a SHA‑256; `LoadSeasonPack` accepts the five‑file envelope and a legacy canonical blob. `VerifyPackIsThePinnedOne` re‑serializes both to canonical CoreJson bytes and requires exact equality before replay.
- Packs **REFERENCE** community skin packs by name/URL, **never ship textures**. Placeholder‑venue rules preserve distance, not lap count.
- **Never build a fantasy/mixed‑year pack** — faithful single historical seasons only (a real‑grid year carryover is fine).

**Pack validation on import (two halves):**
- `Companion.Core.Packs.PackStructuralValidator` — I/O‑free structural checks (id integrity, calendar, points‑system parse, coverage, double‑binding, placeholder rules).
- `Companion.Ams2.Packs.PackContentValidator` — content‑dependent half needing the extracted library + installed‑skin scan: `ams2Class` exists with EXACT casing; every track id+fallback exists and grid ≤ venue `MaxAiParticipants`; every `ams2LiveryName` binds (AI‑file PRIMARY, override scan, or stock) else proceed‑anyway warning; team `carVehicleIds` exist and belong to the class. `GridPreflight.Check` does the same at stage time.

---

## 6. Career hub loop

The weekend loop is a **content‑driven step machine in `HomeViewModel`** (`src/Companion.ViewModels/Shell/HomeViewModel.cs`, ~887 lines — the biggest VM). `CurrentContent` (an `ObservableObject`) cycles:

```
Briefing → [SMGP RivalScreen] → [qualifying ResultEntry] → StartingGrid
        → race ResultEntry → Confirm → Standings
        → [Promotion/Demotion] / [SmgpFinale] / [SitOut]
        → SeasonReview   (when Summary.SeasonComplete)
```

Each step is a bool `Is*State`/`Is*Step` computed from the runtime type of `CurrentContent` (IsBriefingState, IsResultEntryState, IsConfirmState, IsQualifyingStep, IsStartingGridState, IsRivalStep, IsPromotionStep, IsFinaleStep, IsSitOutStep, IsSeasonReview). `ConfirmButtonText`/`CanConfirmResult` adapt per step. Injured rounds route to a **SitOut auto‑sim** screen; a fatal accident sets `CareerOver` + `DeathScreen`.

Results fold through `ReplayService.ComputeRoundFold`; season‑end runs `SeasonEndPipeline` (aging, offers, XP/experience rows, the season‑end injury roll). `CareerSessionService` is the `ICareerSession` impl the hub binds. The weekend model (practice/qualy/1–2 races) from `docs/dev/career-hub-design.md` §3 is partially realized (qualifying‑order + starting‑grid steps exist; still round‑centric, no generalized per‑session fold restructure).

Design: `docs/dev/career-hub-design.md` (LOCKED, 23‑question elicitation), `docs/dev/career-hub-build.md` (build ladder, header stale), `docs/dev/ux-round.md` (result‑entry grammar).

---

## 7. Character / RPG system

Design: `docs/dev/character-system.md` (§12 = shipped reconciliation), `docs/dev/character-death-injury.md`.

### 7.1 Character state + creation (built)

- **`CharacterProfile`** (`src/Companion.Core/Character/`): `Stats` (7 — 5 talent: pace/oneLap/craft/racecraft/adaptability + 2 meta: marketability/durability), `PerkIds`, `Name`, `Age` (16–45, default 23, independent of the seat's historical driver), `ChosenFlavor` (One‑Trick), `CpUnspent`, `CpSpent`. Journaled once at creation as INPUT (`player.character`), survives WipeDerived, has **by‑value Equals/GetHashCode** so a re‑derived season‑start state doesn't false‑diverge.
- **Creation wizard** (`CharacterViewModel`): 3 tiers — archetype preset (one‑click) → free‑customize (7 `StatSlider` + perk shelf grouped by `PerkCategory` + live CP meter) → advanced (raw stat→rating). One‑Trick specialism picker (`EligibleFlavors`, raceSkill excluded).

### 7.2 Levels + XP + one currency (built)

- XP is a **pure function of journaled results** via `XpMath`. Curve `CharacterRules.XpCurve.XpForLevel(n) = round(baseXpToLevel2 * growth^(n-2))`, default base 100 / growth 1.35 / maxLevel 30.
- **ONE spendable currency: Character Points (CP).** `CharacterProgress.AvailableCp = CpUnspent + CharacterPointsPerLevel*max(0,level-1) - CpSpent` (`CharacterPointsPerLevel=3`). Each CP buys one **+0.05 stat step** (`StatStepValue=0.05`, cap `StatCapPerRating=0.99`) OR banks toward a perk.

### 7.3 Perks → `PlayerPerkModifiers` fold (built; FLAT list, not a tree)

- **42 perks across 9 categories** (`data/rules/perks.json`), each an id‑keyed priced node with machine‑readable `PerkEffect[]` benefit/drawback on named levers.
- **`PerkResolver.Resolve`** folds a character's `PerkIds` into an **identity‑defaulting `PlayerPerkModifiers`** struct (talent deltas, car scalars, OPI/rep/anchor/aging/offer/xp/injury coefficients), threaded into the pure sim functions as an optional param defaulting to `Identity` → a character‑free career is byte‑identical. Round‑conditional effects via `ConditionalPerkEffect`.
- **There is NO tier/requires/unlockLevel/branch in the built `Perk` record** — the skill‑tree graph is unbuilt (see 7.7).

### 7.4 Between‑season CP spend (built)

The spend UI lives in **`SeasonReviewViewModel`** (`RaiseStat`/`BuyPerk` commands, `AvailableCp`, `DevelopmentPerks`) — **NOT** on the Driver dossier. Each spend is journaled as a provenance‑excluded `player.statSpend` INPUT (cause `development`) and re‑applied at the next season transition via `CharacterProgress.ApplyAll`. **`CareerSessionService.SpendCharacterPoint` derives the AUTHORITATIVE cost server‑side** (never trusts the caller's `Cost`), rejects ≤0‑cost/owned perks, enforces One‑Trick `lockToOne` (`LockedFlavorRating`) and the `iron_constitution` softCap (`StatSoftCapDelta`).

### 7.5 Dossier (built; thin, read‑only)

`CharacterDossier.Build` is a pure projection (Name, Age, Level, Xp, XpIntoLevel, XpForNextLevel, CpUnspent, Stats as `DossierStat`, Perks as `DossierPerk` with Cost/Benefits/Drawbacks, InjuryRisk, Availability, LevelProgress). `DossierViewModel` is a thin read‑only wrapper (Refresh once per applied round) — it currently has **no Level/XP/CP/spend/skill‑tree members** (the rework adds them).

### 7.6 Death / injury system — FULLY BUILT (all 6 slices)

`docs/dev/character-death-injury.md`. **`MortalityMode{Off=0 default, Normal, Hardcore}`** chosen at creation, carried like the SMGP gates, `[JsonIgnore(WhenWritingDefault)]` so pre‑feature careers stay byte‑identical.
- Own‑accident DNF → **`AccidentSeverity{Light,Medium,Heavy}`** (raw envelope v7 input `PlayerAccidentSeverity`) → `AccidentFold.Apply` draws ONE d500 from `CareerStreams.Accident` → `AccidentModel.Resolve` buckets `effective = roll − SafetyOffset(durability+injury‑perk mods)` against tunable `AccidentRules` bands → **None / MinorInjury(missRaces) / SeasonEnding / Death**, emitting a derived `player.accident` row + optional headline.
- State fields `RaceSuspensionRemaining` / `SeasonEndingInjury` / `Deceased` on `PlayerCareerState`. Skipped rounds **auto‑simulate** (`AutoRaceModel.ClassifiedOrder` ranks non‑player seats by `SeatStrengthModel.Strength` + seeded jitter, player DNS) via `SitOutViewModel`/`IsSitOutStep`.
- Death → `CareerOver` (`PlayerMortalityStatus`, DB‑free) + `DeathScreenModel` obituary. **Normal** offers save‑slot restore; **Hardcore physically deletes the career file** (the one destructive op — guarded to a real alive→dead transition, never on replay). **Light crashes are never fatal** (Mike decision B).
- Second injury layer: the original **season‑end injury roll** (`InjuryModel.Hazard` in `SeasonEndPipeline`, reputation‑only, auto‑enabled for any character with an `injury`‑stream perk).

### 7.7 IN‑FLIGHT: skill‑tree / talent‑points rework — DESIGN ONLY, ZERO code

`docs/dev/character-rpg-rework.md` (2026‑07‑12) — **the active immediate priority.** Specs:
- Rename in‑career currency **CP → Skill Points** (numerically identical to `AvailableCp`, so replay stays byte‑identical).
- Turn the flat 42‑perk list into a **graph** via additive `perks.json` `tier`/`requires[]`/`unlockLevel`/`branch` + a `skillTree` block (branchOrder/metaBranches/statNodes).
- Pure Core projection **`SkillTree.Build → SkillTreeSnapshot{ SkillBranch{ SkillNode{ State: Owned/Unlockable/Locked } } }`**.
- Expanded `DossierViewModel` (`LevelUpPending`, `SkillPointsAvailable`, `RespecTokens`, `SkillTree` VM, `UnlockNodeCommand`, `TalentStatsView`/`MetaStatsView`).
- Ships as a **two‑Codex lane split** (CODE backend + GUI screens) via a Slice‑0 stub bind contract.
- **Grep confirms NONE of these types/members exist in `src` yet** — only in the doc.

**Authored‑but‑dead data the rework must finish** (all in `perks.json`, zero code consumers today):
- `softCapByEra` (era level ceiling — `LevelForTotalXp` has no era overload).
- The full `respec` block + `milestoneEveryLevels:5` + `milestoneGrant:'respecToken'` (no respec code anywhere).
- `statPoints/perLevel` — `PerkResolver` SETS `PlayerPerkModifiers.StatPointsPerLevelBonus` but nothing READS it.

(Two related levers ARE live: `statPoints/softCap` via `iron_constitution`, `statPoints/lockToOne` via `one_trick`.)

The rework must preserve the exact determinism contract: unlocks ride the existing `player.statSpend` (or a new provenance‑excluded `player.respec`) input, cost re‑derived server‑side.

---

## 8. SMGP replica mode (in full)

Design: `docs/dev/smgp-design.md` (adversarially verified), `docs/dev/smgp-17-seasons.md`, `docs/dev/upcoming-race-loop.md`, `docs/dev/smgp-finish-roadmap.md` (live roadmap). All fold state hangs off `PlayerCareerState.Smgp` (null for non‑SMGP careers).

- **Gating seam:** `pack.json careerStyle:"smgp"` (`SmgpRules.CareerStyle`) gates every replica mechanic. Team tier from authored prestige (A=5,B=4,C=3,D=2 via `SmgpRules.Tier`). Pack = `packs/smgp-1` (F‑Classic_Gen3, `skinSeason:"smgp"`, v2.1.0).
- **Season structure:** 16 country‑named rounds in the game's order (R1 San Marino … R16 Monaco finale), points 9‑6‑4‑3‑2‑1 top‑six, NO dropped scores, weather always Clear (season 1). Weekend = 60‑min Warm Up (practice) + 30‑min "Preliminary Race" (qualifying) + full‑distance GP. **A. Senna** (Madonna #1, raceSkill 0.99) = permanent base entry + OP benchmark, never dropped. Field = 34 painted cars across 22 teams + two McLaren MP4/5B Level‑A teams (Kobra Fleetworks mod), now permanent base entries.
- **17‑season campaign:** `SmgpRules.CampaignSeasons=17`. End of season 17 alive = `CampaignComplete` → unlocks `smgp/finale/special.jpg`; champion in all 17 = `CampaignFlawless` (titles≥17) → `ultimate.jpg`. Surfaced via `SmgpFinale()` → `SmgpFinaleViewModel`. Briefing shows "SEASON n / 17". (2‑title `IsComplete` marker = "replica beaten".)
- **Rival ladder — two‑wins seat swap:** before each race name a rival (or be force‑challenged); beat the SAME rival twice without losing → `SeatSwapOfferToPlayer`. `SmgpRules.ApplyBattle` tracks per‑side streaks (a loss resets); `BattleOutcome` decides by finishing ahead (classified beats DNF; both‑out = Void). Challenge targeting `SmgpRules.CanChallenge`: own tier + one above + any below (`Rank(rival) ≤ Rank(player)+1`), display‑only.
- **Clean seat‑swap (no cascade, Mike's anti‑chaos rule):** the player races as their OWN synthetic driver, so an accepted swap simply MOVES them into the rival's car (`SmgpState.CurrentSeatLivery`); the rival benches and returns when the player moves on; the vacated car reverts to its authored driver; NOBODY else moves (`AiSeatOverrides` stays empty). Same for relegation and the title‑defense drop.
- **Two‑phase promotion/demotion screens** (careers after seam 3c‑2, `SmgpState.TwoPhasePromotion`): a two‑wins offer is DEFERRED to a post‑race screen. Battle fold records `SmgpState.PendingSwap`; `CurrentSmgpPromotion()` returns `SmgpPromotionModel` (team photo/motto/history/quotes + player image + car preview + accept/decline); `ResolveSmgpOffer(accept)` journals the provenance‑excluded `smgp.swap` input and re‑persists the round so replay re‑derives byte‑identically (`SmgpBattleFold.ResolvePendingOffer`, shared live+replay). Forced DROP shows `CurrentSmgpDemotion()` (acknowledge‑only). Routed via `HomeViewModel.IsPromotionStep`.
- **Madonna title defense (Ceara event):** winning the title auto‑seats the player in MADONNA next season (`SmgpSchedule.ChampionRollover`). **G. Ceara** (the Senna analogue) force‑challenges at rounds 1 AND 2 (`IsTitleDefenseRound`). Win ≥1 → Madonna kept; lose both → fired to DARDAN (`SmgpRules.TitleDefense`). Defense battles OWN their rounds — the ordinary ladder never runs there and never touches tallies.
- **Level‑D Zeroforce floor + CareerOver hard‑stop:** at Level D there's nowhere to relegate, so EVERY lost battle counts (`SmgpState.FloorLosses`) and the 4th (`FloorLossLimit`) ends the career (`SmgpState.CareerOver=true`). Promoting out of D wipes the count. **`CareerSessionService.Apply` and `AutoSimulateRound` THROW once `Smgp.CareerOver` (or `Deceased`)** — so a floored/dead player can't enter results.
- **Seeded per‑race DNQ field:** ~26 liveries show but 34 painted cars exist, so each round the slowest sit out ("DID NOT QUALIFY 8", or 9 at Monaco cap 25). `SmgpDnqField.Generate`: top `(size−churn=6)` by qualifying pace always qualify; the backmarker bubble competes on per‑race PCG32‑jittered pace (±0.12) off the master seed. Rolled at CreateCareer, pinned into `season.json` (the player's car never DNQs). Season 2+ re‑rolls an independent field per ordinal (`ForSeason`, gated `PerSeasonDnq`).
- **Season‑to‑season variety** (`SmgpSeasonVariety.ForSeason`): season 2+ gets a deterministic Fisher‑Yates shuffle of every venue except the finale (Monaco stays) + fresh per‑round weather. **DELIBERATELY FOLD‑INERT** — only fields the fold never reads move (venue name/track/laps/history/weather); the round POSITION keeps everything the fold reads, so replay stays byte‑identical.
- **The Paddock** (`SmgpPaddock()` → `SmgpPaddockModel`): every grid driver as `SmgpDriverCard` (bio/epithet/quotes from `driver-profiles.json`, predetermined stats from `driver-stats.json` + live accrual, head‑to‑head vs player, per‑track best, recent‑form sparkline, gender pronouns via `SmgpPronouns`) and every team as `SmgpTeamCard`. 34 driver profiles authored. All DISPLAY‑ONLY. Rendered by `PaddockView` (rail tab, master‑detail, DRIVERS/TEAMS toggle).
- **Live stats — player builds from zero:** `SmgpLiveStats.Accrue` tallies wins/podiums/poles/top‑5s/starts from actual classifications — pure display‑only. AI drivers carry a predetermined pre‑history baseline grown by live results; the PLAYER starts from ZERO. Surfaced on the briefing dossier + Paddock.
- **Living‑world dispatches + world stories + career beats:** `SmgpDispatches()` newest‑first feed blends the player's own beats (`SmgpCareerBeats.Detect`) with reactive AI‑world stories (`SmgpWorldStories.Detect`: rival win streaks, Senna reasserting, leader change, title tightening — player excluded as subject). Bodies voiced through `SmgpDispatchCorpus` (`dispatches.json` templates, deterministic PCG32). All display‑only.
- **SMGP news outlet + rival trash‑talk + almanac:** its OWN news outlet (`data/rules/news/smgp.json`, `NewsFacts.PreferredEra="smgp"`). Rival lines from `SmgpRivalQuotes` (per‑driver, per‑mood, deterministic seed, deadpan default "IT'S INTERESTING."). History tab's counterpart is `SmgpWorldHistory()` (What Really Happened almanac, venue‑keyed, unlocked once raced).
- **Tycoon team‑mode read‑only spine:** `SmgpTeamDashboard()` — display‑only projection for the reserved top‑header team mode (player's team + every team ranked by a derived constructors' standing + flavour "team of the season" + budget tiers). NO fold mechanics. Seed of the future 1967+ Tycoon economy (P2/post‑SMGP, **not alpha**).
- **Determinism / fold spine:** all SMGP fold state is `SmgpState` (sealed record). Dictionaries kept in ordinal key order + STRUCTURAL Equals/GetHashCode (replay byte‑compares the serialized blob). New fields `[JsonIgnore(WhenWritingDefault)]`. Rival call is a versioned envelope input; battles fold to DERIVED `SmgpBattle`/`SmgpSeat` rows; oracle never touched.
- **Art collection COMPLETE** (`data/ams2/ART-INVENTORY.md`, 2026‑07‑12): 34 portraits/cars/grid‑cars/flags, 24 team logos/photos/player‑images, 16 round cards, 27 sponsor logos, era‑art, both finale secrets. Optional track‑art/history‑art are the only gaps (clean fallbacks exist).

**SMGP GUI state / the P0 gap:** shipping via App.xaml DataTemplates: RivalScreenView, PromotionView, SmgpFinaleView, BriefingView SMGP panel, cinematic StartingGridView, PaddockView, StandingsView rival highlight, Calendar/History. **The ONE remaining P0 alpha blocker = the character death/injury SCREENS** (GUI round 5, `docs/dev/codex-gui-round5-brief.md`): the wizard MortalityMode radio, Normal save/reload panel, ResultEntry Light/Medium/Heavy severity picker, injured sit‑out auto‑sim screen, death/permadeath screen. **Backend + VMs are shipped and tested** (`SitOutViewModel`, `DeathScreenModel`, `HomeViewModel.IsSitOutStep`/`CareerOver`/`DeathScreen`); the XAML is in the unmerged `codex/gui-round5` worktree. `docs/dev/smgp-finish-roadmap.md`: SMGP‑1.0 = alpha = a fresh career playable end‑to‑end with every shipped mechanic visible in the RC exe.

---

## 9. AMS2 integration + skins/liveries + content library

`src/Companion.Ams2/`. Everything here is **cosmetic staging — the sim always scores the capped resolved grid, never the staged file.**

### 9.1 Detection + the two contract paths

`SteamLocator` finds AMS2 by AppId **1066890** / folder "Automobilista 2" (registry Steam root → `libraryfolders.vdf` → check each lib). `FindAms2()` → `Ams2Installation` or null (UI keeps a manual folder picker fallback). The two paths used everywhere:
- `CustomAiDriversDirectory = InstallDirectory\UserData\CustomAIDrivers`
- `InstallOverridesDirectory = InstallDirectory\Vehicles\Textures\CustomLiveries\Overrides`

### 9.2 Custom‑AI XML + backup contract

- **`CustomAiXmlWriter`** — serializes `CustomAiFile` to the exact AMS2 dialect (`<VehicleClass>.xml`: root `<custom_ai_drivers>`, one `<driver livery_name= [tracks=]>` per entry with 25 rating child‑elements, `'0.0###'` InvariantCulture). Writes **UTF‑8 WITHOUT BOM** via a custom `Utf8StringWriter` so the declaration truthfully says `encoding=utf-8` (a plain StringBuilder writer would lie `utf-16`). **The class NAME casing IS the binding** — filename and `livery_name` must match the game exactly (case‑sensitive).
- **Backup convention (never overwrite without a snapshot):** a sibling `_companion-backups\` folder, timestamped `<stem>.yyyyMMddTHHmmssZ.xml`, same‑second collisions get `-2`/`-3`. `CustomAiBackup`, `LiveryOverrideWriter.Backup`, `ScenarioApplier.BackUp` all share it. `RestoreLatest` parses the embedded timestamp+sequence to order newest‑first.

### 9.3 The load‑once‑at‑launch constraint (critical)

**AMS2 reads a car model's custom liveries ONCE at launch, only the active (numeric‑slot) ones.** So a per‑race rotation (park non‑qualifiers, switch this round's in) BREAKS — the just‑switched‑on skins aren't in the already‑loaded pool, cars pool‑fill with random stock drivers, and it takes a full restart every round. This is why **per‑race livery staging was superseded** — see `RoundLiveryActivator`, which for SMGP activates EVERY pack livery that fits each model's slot cap ONCE (`roundLiveries==packLiveries` → park nothing), giving a STABLE active set AMS2 loads at launch. Staging messages tell the user to fully close/reopen AMS2 launched **DIRECTLY** (not via a mod manager) once.

### 9.4 The staging layers

- **`GridStager`** — builds a `CustomAiFile` from a resolved `GridPlan` (`Build`), stages diff‑aware backup‑first (`StageOrRefuse`). Header marker "AMS2 Career Companion" (`GeneratedMarker`); staging over an unmarked file (the user's own community file) requires force. `MergeInstalledPrimary` keeps the installed foreign file PRIMARY and applies only the career/round delta ("found before overwritten"). `CustomAiEquivalence` no‑op writes nothing when already matching. Force‑gate refusal returns `RequiresForce` (calm UI state), not an exception.
- **`LiveryOverrideWriter`** — turns a community skin ON/OFF by editing the vehicle's USER_OVERRIDES XML like a community "livery selector". Custom slots start at `FirstCustomSlot=51`; edits are **minimal in‑place TEXTUAL replacement of just the one LIVERY attribute** (never re‑serialize — community override files are often not well‑formed). Comment‑aware via `LenientXml.CommentSpans`.
- **`SkinSeasonManager`** — swaps which season a model shows when two packs collide on the active `<model>.xml` pointer (textures coexist in per‑pack subfolders). Backup‑first, **all‑or‑nothing** (else a half‑swapped grid mixes years). Conflicting families: 1974/1975, 1983/1985, 1990/SMGP, 1996/1997, 2010/2012. Library: `data/ams2/skin-seasons/<key>/<model>.xml`.
- **`BatScenarioReader` + `ScenarioApplier`** — parse a pack's scenario‑selector `.bat` (round→section→livery‑override COPY swaps, following the `goto INSTALL_*` confirm‑hop, excluding the pack's own CustomAIDrivers copy). Takes a `seasonLabel` so a multi‑year selector (:1996/:1997) picks the right menu. **"2012" is a phantom** — no such bat/pack exists.
- **`VariantOverrideBinder`** — packs with per‑race change‑point variants but no `.bat` (`AnchorRound` resolves each file via a venue/country/nickname `KnownTokens` vocabulary, guarded by a livery‑name ownership check so e.g. F1‑1990's variants don't hijack the SMGP grid).
- **`ActiveSetRewriter`** — 1985‑style files with alternates parked inside one giant comment.
- **`BaseGameLiveryBinder`** — the "nothing shows in game" root‑cause fix: AMS2 silently rejects a custom‑AI file referencing skins the player hasn't installed. `RebindToBaseGame` rebinds each AI driver onto a REAL base‑game livery (from `official-liveries.json`), keeping community paint where INSTALLED AND ACTIVE, flooring everyone else onto distinct base‑game names, guaranteeing the file loads.

### 9.5 The full staging pipeline (`CareerSessionService.Services`)

The sole consumer that wires all Ams2 staging into one ordered, opt‑in ("Apply grid to AMS2", `baseGameLiveries` flag) pipeline per round: (1) `GridStager.Build` (+form nudge); then when baseGameLiveries: (2) `SkinSeasonManager.Activate` the declared skinSeason; (3) `ApplyScenarioForRound` (.bat); (4) else `VariantOverrideBinder.BindRound`; (5) `ActiveSetRewriter`; (6) `RoundLiveryActivator` fixed‑full‑set for SMGP; (7) `BubbleCarGraft` (player bubble car); (8) scan installed+active liveries and `BaseGameLiveryBinder.RebindToBaseGame`; (9) zero‑stock naming of extra active liveries; finally `GridStager.StageOrRefuse` writes backup‑first.

### 9.6 Content library + extraction

- **`Ams2ContentLibrary.Load(dataDirectory)`** reads the machine‑extracted JSON: `classes.json`, `vehicles.json`, `tracks.json`, `liveries.json` (required) + optional `livery-caps.json` (per‑class slot CAP; absent==unknown) + `official-liveries.json` (per‑class base‑game names, the enum.gg dump). Keyed **case‑SENSITIVELY (Ordinal)**. Records: `Ams2Vehicle` (Id=.crd basename, Dir, VehicleClass, PerformanceIndex, IsOpenWheeler), `Ams2Track` (Id=folder name, `MaxAiParticipants` grid cap as low as 5, IsMod), `OfficialLivery`. `DeduplicateVehicles` resolves genuine duplicate .crd basenames (dir‑named copy wins).
- **`tools/Companion.ContentExtract`** regenerates `vehicles.json` + `classes.json` from a local install (a `.crd` is plain XML — reads `<data class="VehicleDetails">` props). Aborts writing nothing if any `.crd` fails to parse. Command: `dotnet run --project tools/Companion.ContentExtract -- "<AMS2 install>" data/ams2`. (`tracks.json`/`liveries.json` have separate sources, NOT touched by this tool.)

Reference: `docs/dev/ams2-custom-race-reference.md`, `docs/dev/ams2-season-coverage.md`, `docs/research/extraction-verification.md`, `docs/research/local-install-inventory.md`.

---

## 10. The data trees

- **`data/ams2/`** — machine‑extracted, **refreshable** content (never compiled in; re‑extraction not rebuild): `classes/vehicles/tracks/liveries.json`, `livery-caps.json`, `official-liveries.json`, plus keyed **art/asset subtrees** (`cars`, `portraits`, `smgp`, `era-art`, `history-art`, `track-art`, `circuits`, `skin-seasons`) and `ART-INVENTORY.md` manifest. Assets resolve by the **keyed‑asset convention** (first of `.jpg`/`.jpeg`/`.png` wins; absent = slot hidden).
- **`data/rules/`** — engine + flavour rules: `f1-points-systems.json`, `f1-class-season-map.json`, `car-specs.json`, `perks.json`, `career-aging-curves.json`, `placeholder-venues.json`, `news/`, and the SMGP subtree `data/rules/smgp/` (`dispatches`, `driver-profiles`, `driver-stats`, `rival-quotes`, `sponsors`, `team-profiles`, `what-really-happened`).
- **`packs/`** — 21 `f1-<year>` season packs (1967…2020) + `smgp-1`.

All loose data files copied beside the exe. **PowerShell‑authored JSON must be written UTF8Encoding(false) + validated via `System.Text.Json`** — a mojibake lesson (`SmgpTextQualityTests` guards it).

---

## 11. The GUI / app (WPF)

`src/Companion.App/`. Design: `docs/dev/app-shell.md`.

### 11.1 Composition + shell

- **`App.xaml.cs`** — the ONLY composition root. `OnStartup` builds `CareerEnvironment.CreateDefault(<baseDir>/data/ams2)`, `SettingsService`, `CareerSessionFactory`, `RecentCareersStore`, `ShellViewModel`, shows `MainWindow{DataContext=_shell}`. `ApplyTheme`+`ApplyAppearance` live‑apply on every `settings.Changed` (no restart). A `DispatcherUnhandledException` handler writes `%APPDATA%/AMS2CareerCompanion/last-crash.txt` and shows a MessageBox — reports instead of tearing down mid‑career.
- **Two‑tier state machine:** `ShellViewModel.Current` = outer (Start → Wizard|OpenCareer → HubViewModel, + Settings overlay); `HomeViewModel` = inner race loop (§6). Both WPF‑free.
- **`HubViewModel`** — the persistent left‑rail tab shell. Wraps `HomeViewModel` verbatim as the always‑present "Upcoming Race" tab + lens tabs (Standings/Calendar/Skins/History/News always; Driver inserted only if `Dossier.HasCharacter`; Paddock only if `Paddock.HasPaddock`). Anti‑burial rule: `OnHomePropertyChanged` listens for `Summary` changing (once per applied round) and re‑projects every read‑only lens THEN snaps back to the Race tab.
- **`MainWindow.xaml.cs`** — the only shell code‑behind: window‑level key routing (1‑9 → tab, Esc → `TryEscapeBack`, both yielding to a focused editable TextBox and to the modal Team HQ), window placement persistence, and the root `LayoutTransform ScaleTransform` bound to `AppUiScale` (font‑scale 90–130% as ONE global transform, no double‑scaling).

### 11.2 VM→View mapping + Views

**Every screen is a type‑keyed `DataTemplate` in `App.xaml` Application.Resources** — no explicit View instantiation anywhere. 22 templates. To add a screen: add `Views/<X>View.xaml` + a DataTemplate. `Views/` has 34 xaml+cs; code‑behind is trivial by design (focus/keyboard bridging only) — all state in `Companion.ViewModels`. Tear‑off: read‑only lens tabs (Standings/History/Driver/Skins) can pop into an always‑on‑top `TabWindow` bound to the STABLE `HubTabViewModel`.

### 11.3 Theme system

- **`Theme.xaml`** = a FACADE merging `Theme.Dark.xaml` (base palette) + `Accents/Dark/Accent.RoyalBlue.xaml` (default accent) + `Smgp.Track.xaml` (invariant SMGP art brushes). At runtime `App.ApplyTheme` swaps ONLY the first two. `Theme.xaml` also holds the stable layer: fonts (Orbitron display, Inter body, Press Start 2P pixel, JetBrains Mono, Segoe MDL2 icons), `AppFontSize`/`AppUiScale`, motion easings, ~40 converters, all control styles/templates + typography.
- **`ThemeContractRenderTests` is the strongest GUI guardrail.** It pins: (1) every base theme defines EXACTLY the 32‑key semantic contract, Dark+Light identical key sets; (2) each of 7 accents × {Dark,Light} overrides EXACTLY 6 accent brushes; (3) switchable brushes MUST be consumed via **`DynamicResource`** (a regex scan fails on `StaticResource` of a switchable key); (4) Views/MainWindow/Theme.xaml must NOT inline hex paint; (5) WCAG 4.5:1 contrast asserted for every base+accent pair.

### 11.4 Render harness

`tests/Companion.RenderHarness.Tests` — a real off‑screen STA WPF host (`WpfRenderHarness.RunSta` hops onto a fresh STA thread with a live Dispatcher + Application merging the real `Theme.xaml`). Each test constructs a View with `DataContext = a lightweight stand‑in host` exposing exactly the bound members, Measure/Arrange/UpdateLayout, asserts `ActualWidth/Height>0` — so **a binding to a member the host lacks fails the render test.** ~24 render test files. Tracked "render‑harness green" count in memory ≈ **67**.

### 11.5 DataContext footguns (respect on every GUI change)

- **`DossierView.xaml`'s ScrollViewer sets `DataContext="{Binding Dossier}"`** (a `CharacterDossier`). So everything inside binds against the INNER `CharacterDossier`, NOT the `DossierViewModel`. Bindings to VM‑level members (`TeamLine`, `PlayerImageKey`, `Timeline`, and the coming `SkillTree`/`SkillPointsAvailable`/`LevelUpPending`/`TalentStatsView`) MUST use `RelativeSource={RelativeSource AncestorType=UserControl}` then `DataContext.<prop>`. Get it wrong → silently binds against `CharacterDossier` and shows nothing. The render harness catches it because the `DossierHost` stand‑in only exposes actually‑bound members — so when adding VM members, **extend `DossierViewRenderTests`' `DossierHost` (the ONLY test the GUI lane may edit) or the render test won't lay out.**
- **`SmgpBindingProjectionCache`** (`ConditionalWeakTable<ICareerSession,State>` in `Converters.cs`) lets Views read expensive SMGP read‑side projections (`SmgpDispatches`/`SmgpPaddock`/`SmgpTeamDashboard`) straight off the session via MultiValueConverters WITHOUT adding wrapper properties to the shared ViewModel lane. Convention: bind `[session, RoundText-as-refresh-token, …fallbacks]`; `RoundText` changes once per fold and re‑runs the read. This is how the GUI lane surfaces late‑landing SMGP data without crossing into ViewModels.

### 11.6 MVVM conventions

All VMs are CommunityToolkit.Mvvm partial `ObservableObject` with `[ObservableProperty]` source‑gen fields and `[RelayCommand]` methods. `[NotifyPropertyChangedFor(...)]` chains derived props. `Companion.ViewModels` targets `net10.0` with **NO WPF reference** (fully unit‑testable); `Companion.App` is `net10.0-windows` and holds only XAML/converters/MotionAssist/composition root.

`MotionAssist` supplies two fail‑safe attached behaviours (Ripple on the base Button style; Entrance fade+slide on screen change), both wrapped in try/catch so a failed animation can never break navigation.

**GUI rounds history:** Codex has been GUI/art lead across 5 numbered brief docs (`docs/dev/codex-gui-brief.md`, `codex-theming-brief.md`, `codex-gui-round3-brief.md`, `codex-grid-rework-brief.md`, `codex-gui-round5-brief.md`). The active priority is the character/RPG skill‑tree screens (`docs/dev/character-rpg-rework.md` §5 GUI‑Codex prompt).

---

## 12. Build / test / oracle / packaging

### Commands
- Build: `dotnet build Companion.slnx`
- Full test suite (Core/Data/Ams2/ViewModels): `dotnet test tests/Companion.Tests` — the tracked count is ~**2100+** unit tests. Plus ~**67** render‑harness tests (`tests/Companion.RenderHarness.Tests`, Windows‑only, self‑skips elsewhere).
- Oracle: exercised inside `Companion.Tests` (`F1DbOracleTests`, 77/77). Regenerate fixtures via `tools/Companion.FixtureGen`.
- Content extraction: `dotnet run --project tools/Companion.ContentExtract -- "<AMS2 install>" data/ams2`.
- Publish single‑file exe: publish `Companion.App` (SelfContained, PublishSingleFile, win‑x64).

> Suite‑flake note: 1–2 SQLite‑open tests can flake under the parallel run; all pass isolated — not a regression.

### Packaging / dist model
- **`Companion.App.csproj`** publishes ONE self‑contained exe `AMS2CareerCompanion` (net10.0‑windows, win‑x64, PublishSingleFile, EnableCompressionInSingleFile, **v0.6.0**). Fonts + two key‑art PNGs are embedded as WPF `<Resource>`. All content ships as **loose `<None CopyToOutputDirectory=PreserveNewest>`** linked under `data\...` beside the exe. **Heavy art trees (`data/ams2/cars`, `portraits`, `smgp`, ~95MB) carry `ExcludeFromSingleFile="true"`** — the app always reads them loose from `AppContext.BaseDirectory`, so embedding was dead weight.
- **`dist/`** (git‑ignored) = the deployed RC: `AMS2CareerCompanion.exe` + timestamped `.exe.old-*` rollback backups + the loose `data/`+`packs/` trees. `dist/data/ams2/` is canonical (never overwritten from the tracked tree). Deploy = watch‑then‑swap (`scratchpad/deploy-on-close.sh` polls tasklist, backs up the old exe, copies the freshly published one when AMS2CareerCompanion.exe is no longer running).
- **Known gap:** `dist/data/ams2/venue-photos` exists on disk but has NO copy item in `Companion.App.csproj` — a clean publish won't populate it; it survives only because `dist` is hand‑maintained.

---

## 13. Conventions + LOCKED directions (do not relitigate)

- **Faithful single historical seasons only** — never a fantasy/mixed‑year pack (a real‑grid year carryover is fine).
- **A. Senna is always the OP SMGP benchmark** — never nerfed or dropped.
- **The f1db oracle is never touched.** Scoring quirks stay data; career context stays out of `RoundResult`.
- **Byte‑identical replay is non‑negotiable.** Envelope‑versioned + per‑career gated + provenance‑excluded inputs are the tools.
- **The app OWNS the AMS2 staging** (custom‑AI XML, liveries) so RCM/mod managers can't re‑strip it. Launch AMS2 DIRECT, not via a mod manager.
- **Per‑race livery rotation is dead** — AMS2 loads a model's liveries once at launch; the fixed‑full‑set `RoundLiveryActivator` is the SMGP answer.
- **The clean seat‑swap model** (player = own synthetic driver, nobody cascades) is the locked anti‑chaos rule.
- **CoreJson canonical serialization** (camelCase, Rational as strings) is what makes byte‑comparison meaningful — don't change it.
- **PowerShell‑authored JSON:** write `UTF8Encoding(false)` + validate via `System.Text.Json` (mojibake lesson).
- **Lane discipline:** CODE never edits `src/Companion.App/**`; GUI never edits `Core`/`ViewModels`/`Data`/`tests` except the render‑harness stand‑ins. Slice‑0 stub commits unblock the parallel lane.
- **"Done = alpha"** = SMGP‑1.0 = a fresh career playable end‑to‑end with every shipped mechanic visible in the RC exe. Mike's default: **build maximally, don't stop to ask.**
- **Explicitly NOT alpha (P2/post‑SMGP):** Tycoon economy, life‑sim event deck, morale/form, negotiation minigame, Formula Junior 1960 prologue, shared‑memory auto‑capture (`docs/dev/auto-capture.md` — manual entry stays first‑class).

---

## 14. Current state, what's in flight, and the doc map

### Current state (as of ~2026‑07‑12)
- Full SMGP loop, 16‑race season, 17‑season campaign + finale, clean seat‑swap, promotion/demotion, rival ladder, DNQ field, Paddock, live stats, dispatches, news — **all shipping and byte‑identical.** CareerOver hard‑stop closed. Death/injury backend + VMs shipped and tested.
- Suite ~2100+ unit tests + ~67 render green; oracle 77/77.

### In flight
- **P0 alpha blocker:** the character death/injury SCREENS (GUI round 5, `codex/gui-round5` worktree) — then RC rebuild+deploy+push (deliberately un‑rebuilt today, no GUI consumer yet).
- **Immediate priority:** the character/RPG skill‑tree rework (`docs/dev/character-rpg-rework.md`) — design only, zero code; two‑Codex lane split ready.
- **Roadmap tail (P1, not blockers, clean fallbacks):** living‑flavour data corpora, CampaignFlawless celebration hook, skin‑install ownership vs RCM.

### Doc map — where each design doc lives

**Standing / evergreen:** `CLAUDE.md` (standing instructions + locked decisions + the dual‑role handoff), `AGENTS.md` (Codex guide — points to the two role charters), `PLAN.md` (founding vision). The two Codex charters live at `docs/dev/codex-head-of-coding.md` and `docs/dev/codex-head-of-gui.md`.

**Live design (`docs/dev/`):** `character-rpg-rework.md` (active priority), `smgp-finish-roadmap.md` (live roadmap), `character-death-injury.md`, `character-system.md`, `career-hub-design.md` (LOCKED) + `career-hub-build.md`, `smgp-design.md`, `smgp-17-seasons.md`, `upcoming-race-loop.md`, `ux-round.md`, `season-pack-format.md`, `career-sim.md`, `m5-fix-integration.md`, `app-shell.md`, `auto-capture.md`, `codex-gui-round5-brief.md` (the ONE live GUI brief — screens pending).

**Reference / research:** `docs/dev/oracle-fixtures.md`, `docs/dev/season-coverage.md`, `docs/dev/ams2-season-coverage.md`, `docs/dev/ams2-custom-race-reference.md`, `docs/dev/wet-weather-research.md`, `docs/dev/asset-inventory.md`; `docs/dev/audits/*` (per‑season/roster/news/funfacts/skins/responsive); `docs/research/*` (RESEARCH.md, extraction‑verification, local‑install‑inventory, 1967/1969/1983/2010 source‑parity).

**Data READMEs:** `data/ams2/ART-INVENTORY.md`, `data/ams2/{era-art,history-art,portraits,skin-seasons,track-art}/README.md`, `data/ams2/skin-seasons/README.md` (skin family table), `src/Companion.App/Fonts/LICENSES.md`.

**Archive (`docs/archive/`):** superseded continue‑prompts + briefs (CHARACTER‑CONTINUE, SMGP‑CONTINUE, CODEX‑1967‑BRIEF, the `codex-gui-*` early briefs, the `continue-prompts-round*` bundles, old ROADMAP/PIPELINE/vision docs). Read for history only; the living docs above supersede them.

**Living log (outside repo):** `C:/Users/KOBRA/.claude/projects/Z--Claude-Code/memory/MEMORY.md` (index) + `ams2-hub-build-progress.md` (**TOP block = current state, read first**) + the other memory files. This is the fastest way to learn "what happened last".
