# Character death & injury — design + build plan

**Status:** proposed (2026-07-12). Owner: Claude. This finishes the character system with the
death/injury mechanic Mike specified: an accident-DNF can injure or kill the driver, via a **d500**
roll whose odds scale with **accident severity (light / medium / heavy)**; a killed character ends the
career in **Hardcore**, but **Normal** lets you **reload a save**. Grounded in the real seams the
current code exposes (see "What exists today").

---

## 0. The mechanic, in Mike's words

- An accident-DNF is expanded into a **severity**: light / medium / heavy.
- When the player's character retires *for an accident*, roll a **d500**. The roll → one of:
  1. **No injury** — the accident was not injury-sustaining.
  2. **Injury** — the driver must **sit out the next race or more**.
  3. **Heavy** — **season-ending**, or **worse: death**.
- **Death is terminal.** In **Hardcore** that is the end of the career. In **Normal** you can **reload a
  save** — so **saving is first-class**.

---

## 1. What exists today (facts the plan builds on)

| Piece | Where | State |
|---|---|---|
| Player fold state | `Companion.Core/Career/CareerStates.cs` `PlayerCareerState` | has `Character`, `Level`, `Xp`, `SeasonInjuryLoad`; **no health / alive / availability / deceased field** |
| Injury today | `Companion.Core/Character/InjuryModel.cs` + `SeasonEndPipeline.cs:261-302` | **season-end**, perk-gated (any `injury`-stream perk), **reputation-only** (`RepPenalty=8`); the "missed round" in `character-system.md` is **not implemented** |
| DNF / accident | `ResultDraft.DidNotFinish` (`"m"/"a"/"o"`), `DnfDetail{Text,DriverAttributed}`, `DnfCause{Mechanical,DriverError}` | **binary blame, no severity**; `"a"→DriverError` in `CareerSessionService.PlayerDnfCauseFrom` (`:3484`), stored in `RoundResultEnvelope.PlayerDnfCause` |
| Fold / replay | one path `ReplayService.ComputeRoundFold` (`:635`) → byte-compared on `Resimulate` | derived rows emitted in fixed order, `Round4`-quantized; raw inputs ride the **versioned** `RoundResultEnvelope` (v6) or provenance-excluded journal phases |
| Determinism streams | `StreamFactory.CreateStream(subsystem, year, round, entity)` + `Pcg32` | `CareerStreams.Injury` = `(injury, year, 0, "player")` (season-level); a fresh generator per call → re-sim byte-identical |
| Per-career gating | envelope version + `PlayerCareerState` flags `[JsonIgnore(WhenWritingDefault)]` | a feature-off career consumes **zero** new draws → byte-identical to a pre-feature save |
| Normal / Hardcore | `career-hub-design.md §8.5` (decision 22), `career-hub-build.md` Increment 4c | **designed, zero code** |
| Save / reload | SQLite `.ams2career` per career (WAL), **append-only journal**; `CareerFileStore.Duplicate` = consistent SQLite `BackupDatabase` snapshot | **no checkpoint / restore** for gameplay; Duplicate is the natural building block |

**Two invariants this plan must never break:**
1. **Replay byte-identity.** Every new *outcome* is a DERIVED fold row keyed off a named stream; every
   new *player input* rides the versioned envelope (or a provenance-excluded journal phase). Everything
   gates behind a per-career flag so a career without the feature is byte-identical to a pre-feature save.
2. **The journal is append-only.** You cannot "un-fold" a round. **Reload is therefore a FILE-level
   snapshot restore, not a journal edit** — this is the key architectural decision (§4).

---

## 2. The mortality axis: Off / Normal / Hardcore (resolved with Mike)

A single creation choice — **`MortalityMode { Off, Normal, Hardcore }`** — combines the "does this career
have death at all" opt-in (decision A) with the Normal/Hardcore save rules (decision C). Seeded once at
creation, carried forward like the SMGP `TwoPhasePromotion` / `PerSeasonDnq` flags:
`[JsonIgnore(WhenWritingDefault)]`, `Off` = default 0 → **every pre-feature career serializes byte-identically
and has no injury/death.**

- **`Off` (default)** — no injury, no death. The current behavior. The fold consumes **zero** new draws.
- **`Normal`** — injury/death ON. Full **save & reload** (§4): manual save slots + autosave; you may restore
  any snapshot at any time, **including to un-do a death**.
- **`Hardcore`** — injury/death ON, and **there is no save system at all**: manual saves are disabled, there
  is no restore, ever. The live career file simply plays forward (a quit resumes from it, as normal DB
  persistence already does). **On death the career `.ams2career` file — and any of its snapshots — is
  physically DELETED.** The career is gone; the user can do nothing about it. (Mike, verbatim: "NO RESTORE
  EVER IN HARDCORE AND THE SAVE GETS PHYSICALLY DELETED … HARDCORE DISABLES MANUAL SAVES ENTIRELY.")

**Opt-in per decision A:** enabling death is choosing `Normal`/`Hardcore` (any character career may — incl.
SMGP; SMGP then carries this *on top of* its own LEVEL-D `CareerOver` floor). A career with no character, or
`Off`, has no mortality. Persist the mode on the `career` table (career-wide) **and** mirror it into the start
`PlayerCareerState` so the fold reads it without a DB hop.

⚠ **Hardcore file-deletion is a genuinely destructive, irreversible action** — it must be unmistakably
communicated at creation (a confirm) and it is the one place the app deletes a user's career file. Guard it
carefully (only on a real folded death, only for a Hardcore career).

---

## 3. The accident → d500 → outcome mechanic

### 3.1 Accident severity (a new input)

When the player marks their **own** DNF reason as **accident** (`"a"`), the result screen reveals a
**severity picker: Light / Medium / Heavy** (default Medium). This is a raw player input the sim cannot
re-derive, so it rides the **envelope**:

- `RoundResultEnvelope.CurrentVersion 6 → 7`; add `AccidentSeverity? PlayerAccidentSeverity` (nullable).
  **Null on every older save and on any non-accident DNF** → the fold treats null as "no severity, legacy
  binary behavior" → byte-identical. (`ResultStore.cs`, same pattern as `SmgpRival` v6 / `CalledShot` v5.)
- `enum AccidentSeverity { Light, Medium, Heavy }` (Core).
- **Severity does NOT change scoring.** An accident is still `DnfCause.DriverError` (binned the car →
  scores as grid size in `OpiMath`). Severity feeds **only** the injury roll. This keeps the scoring/OPI
  path untouched and its replay unchanged.
- Result-screen UX: only shown for the player's own accident DNF; the existing `DnfDetail.Text` free-text
  ("hit the wall at the hairpin") still rides alongside for flavour.

### 3.2 The d500 roll (a new DERIVED outcome)

In the round fold, **after** the result is scored, if **the death/injury system is enabled for this
career** AND **this round is the player's accident-DNF with a severity**:

- Draw a **new per-round stream**: `CareerStreams.Accident = "accident"`, keyed
  `(accident, year, round, "player")` — a *round-level* companion to the season-level `injury` stream.
  `int d500 = stream.NextInt(1, 501)` (1..500).
- **Modify the effective roll by the driver's safety profile** (ties the mechanic into the existing
  character stats/perks so investment matters):
  - `durability` meta-stat and any `injury`-stream perk (`ironman`/`iron_constitution`/`safe_hands`
    protect; `glass_cannon`/`hot_head`/`injury_prone` endanger) shift the roll via a **deterministic
    integer offset** (NOT a second draw — the draw count must stay fixed): e.g.
    `effective = d500 + safetyOffset(durability, mods)`, clamped to `[1,500]`. `safetyOffset` is pure and
    quantized.
- **Resolve `effective` against severity bands** (data-driven, tunable — see §3.4) into an
  **`AccidentOutcome`**: `None | MinorInjury(missRaces=N) | SeasonEnding | Death`.

### 3.3 New fold state + journal row

Add to `PlayerCareerState` (all `[JsonIgnore(WhenWritingDefault)]` → default-off careers unchanged):

- `int RaceSuspensionRemaining` — races the driver must still sit out (minor injury). Decrements as the
  driver sits.
- `bool SeasonEndingInjury` — out for the rest of the season; cleared at the season reset (returns next
  season).
- `bool Deceased` — the character died; **terminal**. The career stops accepting rounds (mirrors SMGP's
  `CareerOver`).

Emit a DERIVED journal row (byte-compared) describing the outcome — extend `JournalPhases.PlayerInjury`
or add `player.accident`. Delta carries `{ severity, effectiveRoll, outcome, missRaces }`, `Round4`-safe.
On a hit, apply the state change and (reuse the existing pattern) an optional `news.headline` row.

**Determinism:** the *only* input is the envelope severity (already excluded from the byte-compare by
being a raw envelope field); the roll + outcome are a pure function of `(masterSeed, year, round, state)`
→ re-derives byte-identically. Gated behind the enable flag + a present severity → default careers draw
**zero** from the `accident` stream, so no other stream shifts.

### 3.4 Default probability bands (tunable data — **Open decision B**)

d500, so 1 unit = 0.2%. Grounded in "heavy crashes hurt; deaths are rare even then." Lives in a new
`injury` block in `perks.json` (or a small `injury-rules.json`) so Mike tunes without a rebuild.

| Severity | None | Minor (miss 1) | Minor (miss 2-3) | Season-ending | Death |
|---|---|---|---|---|---|
| **Light** | 1–490 (98%) | 491–500 (2%) | — | — | — |
| **Medium** | 1–410 (82%) | 411–470 (12%) | 471–490 (4%) | 491–497 (1.4%) | 498–500 (0.6%) |
| **Heavy** | 1–250 (50%) | 251–380 (26%) | 381–450 (14%) | 451–485 (7%) | 486–500 (3%) |

**Light is intentionally never fatal or season-ending (decision B, resolved 2026-07-12: "light crashes are
mostly harmless").** A light crash's worst outcome is one missed race — and because its top band is a minor
injury, the safety-offset clamp (which piles a reckless driver's overflow rolls onto the last band) can only
ever cost a fragile driver a race, never their life. Death + season-ending fragility live on Medium/Heavy,
where the clamp amplification is the intended glass-cannon risk.

`safetyOffset` shifts `effective` up (safer) for high durability / protective perks and down for reckless
ones, so a `glass_cannon` heavy shunt is meaningfully deadlier than an `ironman`'s. Exact numbers are
Mike's to set; the table above is the proposed default.

---

## 4. Save & reload — FILE-level snapshots (Normal only)

Because the journal is append-only, **reload is not a fold operation** — it is restoring an entire earlier
career-DB snapshot. Build on the proven `CareerFileStore.Duplicate` (`BackupDatabase`, a consistent
snapshot). **This whole system is `Normal`-mode only** (Hardcore has no saves).

- **Save** = snapshot the working `.ams2career` into a **save slot** — a sibling file under a
  `Saves/<career>/` folder (e.g. `<career>.slotN.ams2save`). A save records a label + the season/round it
  was taken at (read from the DB) for the restore UI.
- **Reload / restore** = close the working DB (the pool-clear at `CareerDatabase.Dispose` already makes
  the file movable), copy the chosen snapshot over the working file, reopen. The career reverts wholesale
  to the saved point — clean, and it never touches the fold/replay contract (each snapshot is itself a
  complete, self-consistent, replay-verifiable career). Restore is allowed **any time in Normal, including
  to un-do a death**.
- **Autosave** (recommended, Normal): snapshot at each **season start** and **before applying an
  accident-DNF round** — so a death always has a very recent restore point.
- **Hardcore:** the save surface is **hidden/disabled entirely** (no slots, no manual save, no restore). A
  Hardcore career just plays forward on its live file. **On a folded death, the `.ams2career` file (and any
  stray snapshots) is deleted** — the one destructive file op, gated on `MortalityMode.Hardcore` + a real
  `Deceased` transition; see §2's warning.
- This is a new `ISaveSlots` surface on `ICareerSession` + the Start gallery, NOT a fold change →
  **replay-safe by construction**, and it ships *before* death exists (Slice 1).

---

## 5. The "sit out a race" fold — AUTO-SIMULATED rounds (resolved with Mike)

When the driver is unavailable at the **start** of a round — `RaceSuspensionRemaining > 0`, or
`SeasonEndingInjury` (deceased ends the career, so no more rounds) — that round is **skipped**, and because
**AMS2 has no spectate for a single-player race, the app must auto-simulate it** (Mike: "there is no way
around this"). The player does not race it in-game.

- **Auto-race generator (new, DETERMINISTIC).** The fold already re-derives the resolved grid + each seat's
  strength from `pack + seed + round` (`SeatStrengthModel` / the expected-finish path). A skipped round
  reuses that: take the resolved grid's **expected finishing order**, apply a **seeded per-driver jitter**
  from a new stream `CareerStreams.AutoRace = "auto-race"` keyed `(auto-race, year, round, "player")`, and
  produce a full field **classification** — with the **player marked DNS** (not classified, zero points,
  OPI-neutral, never a finishing position). Pure function of `(masterSeed, year, round)` → re-derives
  byte-identically; a legacy career (no injury) never draws from the stream.
- The auto-sim is scoped to skipped rounds now, but it is deliberately a **reusable field-result generator**
  (the seed of a future "simulate a race I don't feel like driving" feature).
- **State:** `RaceSuspensionRemaining` decrements as the driver sits (a minor injury heals over the rounds
  missed); `SeasonEndingInjury` skips every remaining round of the season and clears at the season reset
  (returns next season); the round after the last suspension the player enters results normally again.
- **UX:** the result-entry screen detects the folded unavailability and shows **"INJURED — auto-simulating
  round (N remaining)"** / **"SEASON OVER — recovering"** with a single **Continue** that folds the
  auto-simulated round. No manual entry for a skipped round.
- **Envelope:** a skipped round stores a minimal envelope flag (`PlayerDidNotStart`, v7) so a re-entry /
  replay is unambiguous about which rounds were auto-sims; the field classification itself is DERIVED
  (re-generated on replay), not stored, so it stays byte-compared. This is the highest-replay-risk slice →
  its own careful `AssertResimulatesByteIdentically` tests (a career that misses races re-sims identically).

---

## 6. Surfacing (VM / display — Codex binds the GUI)

- `CharacterDossier` gains an **availability** line: Fit / Injured (N races) / Season over / **Deceased**,
  plus an injury history. (`InjuryRisk` already exists — extend it.)
- A **death / "career over" screen** (reuse the SMGP finale full-immersion pattern): an in-world obituary
  + the career's record; in Normal it offers **"Restore last save"**, in Hardcore it is final.
- The living-world **dispatch feed** (Task 4) already has a `Setback` kind — an accident/injury/death makes
  a natural dispatch (a follow-up once this lands).
- All display-only; Codex owns the XAML.

---

## 7. Determinism & replay checklist (every slice honors this)

1. New player *input* (severity, DNS) → **versioned envelope field**, null/false on old saves.
2. New *outcome* (roll, injury, death) → **DERIVED journal row**, `Round4`-quantized, byte-compared.
3. New draw → **`StreamFactory` stream** with a fixed key; **gated** so default careers draw zero.
4. New state field → `PlayerCareerState` with `[JsonIgnore(WhenWritingDefault)]`.
5. Enable flag seeded once at creation, carried via record `with`, survives season reset / rollover.
6. Every slice ships an `AssertResimulatesByteIdentically` test on a career that uses it **and** a
   legacy-gate test proving a feature-off career is byte-identical to before.
7. Save/reload is **file-level**, entirely outside the fold — its own tests (snapshot → mutate → restore
   → identical; Hardcore refuses restore-after-death).

---

## 8. Build order (each slice: test + commit + replay-verify; RC rebuild once a GUI consumer exists)

1. **Slice 1 — `MortalityMode` + Normal Save/reload (the safety net FIRST).** The `MortalityMode
   {Off,Normal,Hardcore}` creation choice (wizard step + `career`-table + start-state mirror); the Normal
   `ISaveSlots` snapshot/restore built on `Duplicate`; autosave hooks; Start-gallery + in-session surface;
   the Hardcore "no save UI" path (deletion wiring stubbed until death exists). **No fold change** →
   replay-safe. Ship before death can happen so there is always a recovery path in Normal.
2. **Slice 2 — Accident severity input.** Envelope v7 `PlayerAccidentSeverity`; result-screen picker;
   `AccidentSeverity` enum. Captured only, no outcome yet. Replay-safe (null = legacy).
3. **Slice 3 — The d500 roll + injury/death state (derived), gated.** `CareerStreams.Accident`; the roll
   + `safetyOffset`; bands data; `RaceSuspensionRemaining` / `SeasonEndingInjury` / `Deceased`; the derived
   journal row; **Hardcore death → physical career-file deletion** (the one destructive op, carefully
   guarded). Byte-compared + gated on `MortalityMode != Off`.
4. **Slice 4 — The auto-simulated skipped-round fold.** The `CareerStreams.AutoRace` deterministic
   field-result generator (reusing seat strength) + `PlayerDidNotStart` envelope flag + result-screen
   sit-out/auto-sim state + suspension healing. The determinism-critical slice.
5. **Slice 5 — Healing/return + the death screen + dossier availability.** VM projections + the career-over
   screen (an obituary; restore-last-save in Normal, "career deleted" in Hardcore). Display-only.
6. **Slice 6 — Perks/durability tuning + a Setback dispatch.** Wire `durability`/injury-perks into
   `safetyOffset`; expose the bands as tunable data; add the accident/injury/death dispatch (Task 4).

Slices 1-2 are pure infra/input (low risk); 3-4 are the determinism-critical core; 5-6 are polish.

---

## 9. Decisions

**Resolved with Mike (2026-07-12):**
- **A — Scope = explicit opt-in.** Enabling death is choosing `Normal`/`Hardcore` at creation (§2); any
  character career may (incl. SMGP, on top of its floor). `Off` = no mortality.
- **C — Hardcore = no saves, no restore ever, and death physically deletes the career file** (§2/§4). Normal
  = full save/reload incl. un-doing a death.
- **D — Skipped rounds are AUTO-SIMULATED** (§5), since AMS2 can't spectate a single-player race — a
  deterministic field-result generator, player DNS.

- **B — Probability bands (RESOLVED 2026-07-12).** Mike: "light crashes are mostly harmless." Light no longer
  has a death or season-ending band — worst case is one missed race (§3.4). Medium/Heavy keep their death
  bands + the safety-offset clamp amplification (a glass_cannon heavy shunt ~7.8% fatal vs an ironman ~0%).
  The whole table stays tunable data (`perks.json` accident block) + the `AccidentModel.DefaultRules` fallback.

**Still open (defaulting; Mike can tune):**
- **E — Save-slot UX (Normal).** Proposed: a few named manual slots + a rolling autosave ring. Low-risk;
  will default and refine.

---

## 10. Files this will touch (map)

- **Core:** `Career/CareerStates.cs` (state fields + `MortalityMode`), new `Character/AccidentModel.cs`
  (severity enum, `safetyOffset`, band resolution), new `Career/AutoRaceModel.cs` (deterministic
  field-result generator from seat strength), `Career/CareerStreams.cs` (`Accident` + `AutoRace` streams +
  `player.accident` phase), `Career/RoundUpdate.cs` (the roll + skipped-round handling),
  `Character/InjuryModel.cs` (reuse/extend).
- **Data:** `ResultStore.cs` (envelope v7: `PlayerAccidentSeverity`, `PlayerDidNotStart`), new
  `SaveSlotStore` + `CareerFileStore` (snapshot/restore/delete), `ReplayService.cs` (thread the new inputs
  into `ComputeRoundFold`; read-back like `PlayerDnfCause`).
- **VM:** `ICareerSession.cs` (severity input, `ISaveSlots`, dossier availability, death screen model),
  `CareerSessionService.cs` (fold wiring, mode, save/restore), `ResultEntry/*` (severity picker + sit-out
  state), wizard (mode step), `CareerRulesData.cs` (band data).
- **Data files:** `data/rules/perks.json` (injury bands) or new `data/rules/injury-rules.json`.
- **Tests:** replay byte-identity per slice + legacy-gate; the band resolver (pure); save→mutate→restore;
  Hardcore-refuses-restore; the DNS fold.
- **Docs:** this file; update `character-system.md` §6/§8.5 to mark injury/death implemented.
