# Claude continue-prompts — round 3: the character death & injury system (Slices 1-4)

**State:** `hub/increment-4` @ `d96d96a`. SMGP Tasks 4 (living-world dispatches + rumor) and 5 (tycoon
read-only spine) are done; the **character death & injury PLAN is written** in
`docs/dev/character-death-injury.md` with Mike's three decisions baked in
(`MortalityMode {Off,Normal,Hardcore}` opt-in; Hardcore = no saves + a death physically deletes the career
file; injured rounds auto-simulate). **Codex is on GUI round 4** (news ticker, paddock rumor, tycoon
dashboard shell, top-down grid rework, art) and has a while to work — these 4 Claude prompts build the
death/injury CORE in parallel, entirely in the Claude lane.

**Run them IN ORDER** — each is a self-contained slice. Before each: read
`docs/dev/character-death-injury.md` (the authority — the § references below point into it) + the auto-memory
`ams2-hub-build-progress.md` TOP block. **Claude lane = `Companion.Core` / `Companion.ViewModels` /
`Companion.Data` / `data/**` + tests; do NOT touch `src/Companion.App/**`** (Codex's XAML — for a slice that
needs a picker / sit-out UI, add the VM property + model and leave the XAML for Codex to bind). Test +
commit each slice; **no RC exe rebuild** until a GUI consumer exists (no visible change yet).

**Determinism checklist every FOLD slice honors (plan §7):** new player *input* → **versioned envelope**
field (null/false on old saves); new *outcome* → **DERIVED, byte-compared journal row**, `Round4`-quantized,
keyed off a **named `StreamFactory` stream**; new state field → `PlayerCareerState` with
`[JsonIgnore(WhenWritingDefault)]`; **gated on `MortalityMode != Off`** so an `Off`/legacy career draws
**zero** from any new stream (preserving every other stream's sequence). Every slice ships an
`AssertResimulatesByteIdentically` test on a career that uses it **and** a legacy-gate test proving a
feature-off career is byte-identical to before. Key seams the research already located are inlined below.

---

## PROMPT 1 — Slice 1: `MortalityMode` + Normal save/reload (the safety net; NO fold change)

```
Continue the AMS2 Career Companion (Z:\Claude Code\ams2-career-companion, branch hub/increment-4). Read
docs/dev/character-death-injury.md (§2, §4, §8) + the auto-memory ams2-hub-build-progress.md TOP block first.
Claude lane = Core/ViewModels/Data/data + tests; do NOT touch src/Companion.App/** (Codex's XAML). This slice
has NO fold change, so it MUST stay replay-byte-identical. Build in this order, test + commit.

1. MortalityMode { Off, Normal, Hardcore } enum (Companion.Core/Career/). Off = default (0).

2. Creation choice + persistence:
   - Add MortalityMode to the creation request (CareerCreationRequest) with default Off.
   - Persist it (a) on the `career` table via a NEW migration (Companion.Data/Migrations) — a column
     defaulting to 0/Off so every existing career reads Off; AND (b) mirror it into the start
     PlayerCareerState as a new [JsonIgnore(Condition = WhenWritingDefault)] field, seeded ONCE at creation
     (mirror how FormAware is seeded at CareerSessionService.cs ~:477 and Smgp at ~:485) and carried forward
     each round via record `with`.
   - ⚠ VERIFY an Off career's serialized start-state blob is byte-identical to a pre-feature career (that is
     the whole point of WhenWritingDefault) — a test.
   - The wizard MODE step is VM-only here (a selectable Normal/Hardcore/Off, default Off); Codex binds the
     XAML radio. Expose whatever VM property it needs.

3. Normal save/reload — a FILE-level snapshot system (plan §4), NOT a journal edit (the journal is
   append-only). Build on the proven CareerFileStore.Duplicate (Companion.Data/CareerFileStore.cs:37-67 —
   SQLite BackupDatabase = a consistent snapshot):
   - New SaveSlotStore: SAVE = snapshot the working .ams2career into Saves/<career>/<slot>.ams2save,
     recording a label + the season/round (read from the DB) for the restore UI. RESTORE = Dispose the
     working DB (the pool-clear at CareerDatabase.cs:51-60 makes the file movable), copy the chosen snapshot
     over the working file, reopen. DELETE = remove a slot.
   - New ISaveSlots surface on ICareerSession (list / save / restore / delete; additive DEFAULTS so existing
     fakes compile) + the CareerSessionService implementation.
   - AUTOSAVE hook: snapshot at each season start (Normal only).
   - HARDCORE: the save surface is disabled / empty (no slots, no manual save, no restore). The
     death-deletes-the-file wiring is a clearly-marked STUB/TODO until Slice 3 (nothing to delete yet).

4. NO fold change. NO src/Companion.App edits (Codex binds the wizard mode radio + the save/load panel to
   the VM/model you expose).

Tests: (a) legacy gate — an Off career's start state + a full Resimulate is byte-identical to before;
(b) a Normal career round-trips the mode through the DB + start state; (c) SaveSlotStore save → mutate the
working career (apply a round) → restore → the restored career matches the snapshot (open both, compare
summary/standings); (d) slot list + delete. Whole suite green. NB the known SQLite-open parallel flake
(different test each full run, passes isolated) — re-run to confirm.
```

---

## PROMPT 2 — Slice 2: accident severity input (envelope v7; captured, no outcome yet)

```
Continue the AMS2 Career Companion (Z:\Claude Code\ams2-career-companion, branch hub/increment-4). Read
docs/dev/character-death-injury.md (§3.1) + the memory TOP block first. Claude lane only (Core/ViewModels/
Data + tests); do NOT touch src/Companion.App/**. Replay-safe: this captures a new input but NOTHING consumes
it yet, so every career re-sims byte-identically. Test + commit.

1. enum AccidentSeverity { Light, Medium, Heavy } (Companion.Core).

2. Capture it as a raw player input on the result envelope (the sim can't re-derive it), exactly like
   PlayerDnfCause / CalledShot:
   - RoundResultEnvelope (Companion.Data/ResultStore.cs:29-77): bump CurrentVersion 6 -> 7; add a nullable
     AccidentSeverity? PlayerAccidentSeverity. It reads NULL on every older (v<=6) save AND on any
     non-accident DNF -> the fold treats null as "legacy binary behavior".
   - ResultDraft (ICareerSession.cs ~:762) gains PlayerAccidentSeverity (nullable); BuildEnvelope in
     CareerSessionService (~:3471, alongside PlayerDnfCause = PlayerDnfCauseFrom(draft)) threads it in — set
     it ONLY when the player's own DNF reason is accident ("a"); null otherwise.
   - ResultEntryViewModel: add the VM property (a Light/Medium/Heavy selection) shown only for the player's
     own accident DNF; Codex binds the XAML picker. Default Medium when an accident is chosen. The existing
     DnfDetail.Text free-text still rides alongside.

3. NOTHING consumes the severity yet (Slice 3 does). NO src/Companion.App edits.

Tests: (a) envelope v7 round-trips a severity; (b) a v6 save (no field) parses with PlayerAccidentSeverity
== null (RoundResultEnvelope.Parse back-compat, ResultStore.cs:77); (c) a career that records an
accident-with-severity round re-sims BYTE-IDENTICALLY (since nothing folds it) — the legacy gate holds.
Whole suite green.
```

---

## PROMPT 3 — Slice 3: the d500 roll + injury/death state (DERIVED, gated) + Hardcore file-delete

```
Continue the AMS2 Career Companion (Z:\Claude Code\ams2-career-companion, branch hub/increment-4). Read
docs/dev/character-death-injury.md (§3.2, §3.3, §3.4, §7) + the memory TOP block first. Claude lane only; do
NOT touch src/Companion.App/**. This is a FOLD slice — honor the determinism checklist (plan §7). Test +
commit; replay-verify.

1. New stream CareerStreams.Accident = "accident" (Companion.Core/Career/CareerStreams.cs) — a ROUND-level
   companion to the season-level "injury" stream; keyed (accident, year, round, "player").

2. New pure Companion.Core/Character/AccidentModel.cs: the severity->bands resolution + safetyOffset. It
   takes (AccidentSeverity, d500 roll 1..500, durability stat, PlayerPerkModifiers) and returns an
   AccidentOutcome { None, MinorInjury(missRaces=N), SeasonEnding, Death }. safetyOffset is a pure,
   quantized integer shift applied to the roll (NOT a second draw — the draw count stays fixed): high
   durability + protective injury-stream perks (ironman/iron_constitution/safe_hands) shift SAFER; reckless
   ones (glass_cannon/hot_head/injury_prone) shift DEADLIER (reuse InjuryModel.HasInjuryPerk / the injury
   stream, Companion.Core/Character/InjuryModel.cs). The bands live in TUNABLE DATA (extend data/rules/
   perks.json with an "injury"/"accident" block, or a new data/rules/injury-rules.json loaded via
   CareerRulesData) — start with the §3.4 default table; write it UTF8-no-BOM + validate via System.Text.Json
   (the mojibake lesson).

3. New PlayerCareerState state (all [JsonIgnore(WhenWritingDefault)]): RaceSuspensionRemaining (int),
   SeasonEndingInjury (bool), Deceased (bool). SeasonEndingInjury clears at the season reset; Deceased is
   terminal.

4. Fold wiring (the single path — ReplayService.ComputeRoundFold / RoundUpdate.Apply): when MortalityMode !=
   Off AND this round is the player's accident-DNF (PlayerDnfCause == DriverError via "a") AND
   PlayerAccidentSeverity is set, draw the accident stream, roll d500, apply safetyOffset, resolve via
   AccidentModel, apply the state change, and emit a DERIVED journal row (extend JournalPhases.PlayerInjury
   or add player.accident) with delta { severity, effectiveRoll, outcome, missRaces }, Round4-quantized so
   the byte-compare is stable. GATE it so an Off / no-severity career draws ZERO from the accident stream.
   Severity is already a raw envelope input (Slice 2) so it is excluded from the byte-compare by mechanism;
   the roll + outcome ARE byte-compared.

5. Death -> terminal: the career stops accepting rounds (mirror SMGP's CareerOver handling). HARDCORE death
   -> physically DELETE the career .ams2career file (and any snapshots) — wire the SaveSlotStore/
   CareerFileStore delete stubbed in Slice 1. ⚠⚠ THIS IS THE ONE DESTRUCTIVE FILE OP: guard it hard (only
   when MortalityMode == Hardcore AND a real Deceased transition just folded); never in a test against a real
   file (test the delete path in a temp dir). Surface a "career deleted / permadeath" outcome the shell can
   show.

Tests: (a) AccidentModel band resolver — pure, per severity + offset edge cases (a light shunt almost never
kills; a heavy glass_cannon shunt is meaningfully deadlier); (b) a Normal career that folds an accident
injury RE-SIMS BYTE-IDENTICALLY; (c) legacy gate — an Off (or no-character) career is byte-identical to
before (zero accident-stream draws); (d) a folded Death sets Deceased + stops further rounds; (e) the
Hardcore delete path removes a temp career file on death and refuses in Normal. Whole suite green;
replay-verify a multi-round accident career.
```

---

## PROMPT 4 — Slice 4: auto-simulated skipped rounds (the determinism-critical DNS fold)

```
Continue the AMS2 Career Companion (Z:\Claude Code\ams2-career-companion, branch hub/increment-4). Read
docs/dev/character-death-injury.md (§5, §7) + the memory TOP block first. Claude lane only; do NOT touch
src/Companion.App/**. FOLD slice — honor the determinism checklist. This is the highest replay-risk slice:
careful tests. Test + commit; replay-verify.

Context: AMS2 cannot spectate a single-player race, so an injured (unavailable) round must be AUTO-SIMULATED
deterministically (Mike's call), with the player marked DNS.

1. New stream CareerStreams.AutoRace = "auto-race" (keyed (auto-race, year, round, "player")).

2. New pure Companion.Core/Career/AutoRaceModel.cs: a DETERMINISTIC field-result generator. The fold already
   re-derives the resolved grid + each seat's strength from pack+seed+round (SeatStrengthModel / the
   expected-finish path used by CurrentExpectedFinish and OpiMath). Reuse it: take the grid's expected
   finishing order, apply a seeded per-driver jitter from the auto-race stream, and produce a full field
   CLASSIFICATION — with the PLAYER marked DNS (not classified, zero points, OPI-neutral, never a finishing
   position). Pure function of (masterSeed, year, round) -> re-derives byte-identically. Scope it to skipped
   rounds now, but write it as a reusable field-result generator (the seed of a future "sim a race" feature).

3. RoundResultEnvelope (v7 from Slice 2): add a PlayerDidNotStart flag so a re-entry / replay is unambiguous
   about which rounds were auto-sims. The field classification itself is DERIVED (regenerated on replay), NOT
   stored -> stays byte-compared.

4. Fold: when the player is unavailable at the START of a round — RaceSuspensionRemaining > 0 OR
   SeasonEndingInjury (Deceased ends the career, so no more rounds) — the round is auto-simulated (player
   DNS), and RaceSuspensionRemaining DECREMENTS (a minor injury heals over the rounds missed);
   SeasonEndingInjury skips every remaining round of the season and clears at the season reset (returns next
   season). The round AFTER the last suspension enters results normally again.

5. ResultEntryViewModel: detect the folded unavailability and expose a sit-out state ("INJURED —
   auto-simulating round (N remaining)" / "SEASON OVER — recovering") with a single Continue that folds the
   auto-sim round. Codex binds the XAML.

Tests: (a) AutoRaceModel is deterministic (same seed+round -> same field order) + player is DNS; (b) a career
that MISSES races (a minor injury) RE-SIMS BYTE-IDENTICALLY; (c) suspension decrements + heals correctly and
the driver returns; (d) a season-ending injury skips the rest of the season and the driver returns next
season; (e) legacy gate — a career that never gets injured draws zero from the auto-race stream and is
byte-identical to before. Whole suite green; replay-verify a career that misses 2 races then returns.
```

---

### After these four
Slices 5-6 (the death/obituary screen + dossier availability line; perks/durability tuning + a Setback
dispatch for the Task-4 feed) are display/polish and can pair with Codex's GUI once the core lands — write
a round-4 prompt set then. Update `docs/dev/character-death-injury.md`, the Codex brief bind-contract, and the
memory as each slice ships.
