# CONTINUE — Increment 4 (character/RPG milestone) resume prompt

Resume the **AMS2 Career Companion** (`Z:\Claude Code\ams2-career-companion`, WPF / .NET 10, branch
**`hub/increment-4`**). Two agents share the repo — **Claude = SMGP + the app (this lane)**, **Codex =
the 1967 era** (`era/1967` worktree). Stay out of `packs/f1-1967/**`, `data/rules/news/1960s.json`,
1967 data/docs.

## First: orient
1. **Read** the auto-memory: `MEMORY.md` → `ams2-hub-build-progress.md` (TOP block = current state),
   `mike-build-maximally.md`. Then `docs/dev/career-hub-build.md §"Increment 4"` (the 3-slice plan) and
   `docs/dev/character-system.md` (the design spec; **§12 = shipped reconciliation**, authoritative).
2. **Verify**: `git log --oneline -8`, `git status`, `dotnet test Companion.slnx` (**1827 + 46** green
   as of `1b42819`). Discipline: f1db oracle **77/77 NEVER touched**; **byte-identical replay LOCKED**
   (new fold rows envelope-versioned + per-career gated; a no-character/no-mode career stays identical).
   Commit + push per slice; republish the RC to `dist/` when the app is CLOSED (backup `.old-<ts>` first).

## What's DONE — Increment 4a (creation + progression) is FINISHED + shipped
The character creator is complete: 42 perks + 13 audited archetypes, `PlayerPerkModifiers` threaded
through the sim, XP/levels, injury auto-enable, the **§4.1d/e CI balance guards**, and the creation
screen (archetype cards, free-customize, **Advanced numbers**, **Random balanced build**, **One-Trick
specialism picker**, inline perk info). Three dead perk levers (one_trick / sponsor_magnet /
late_bloomer riseMult) were revived. **Durability** now shifts the offer-market age. The **car-specs
card** (machine/engine/power + ENG-TM-SUS-TIRE-BRA bars) is wired into the character + rival screens,
absent-tolerant, shipping with PLACEHOLDER SMGP numbers. See the memory TOP block for the commit list.

## NEXT — pick up here (Mike's bar: build maximally, fully immersive)
1. **Real car-spec numbers** — Mike drops per-team values into `data/rules/car-specs.json` (a team-id
   key overrides the car-model default). Pure data; the card already renders.
2. **4b — Contracts-as-documents.** (Setup Gamble, the other half of 4b, is ALREADY partly built as the
   "called shot": `ResultDraft.CalledShot` + the `player.call` journal phase, test
   `SetupGamble_CalledShot_...`.) Author era-styled contract `DataTemplate`s; Accept = the skip path.
3. **4c — Normal/Hardcore + life-sim** (the biggest remaining body of work, ~rest of v0.5.0): a
   first-class `CareerMode {Normal,Hardcore}` chosen at creation + journaled (drives injury default,
   the Hardcore honest-nudge carScalar, respec strictness, life-event severity); the **life-sim event
   deck** on the reserved `CareerStreams.Events` (morale/rivalries/life events — modulate INPUTS only,
   never a finishing position); **real form-swing** (add `CareerStreams.FormSwing`/`CharacterGen`
   consts + the per-round jitter for opportunist/superstition/streaky, gated so default careers draw
   nothing). Each slice = its own determinism gate (a character/mode/minigame/life-event career AND its
   skip-everything equivalent both byte-identical) + version bump.

Keep it deterministic + replay-safe; a no-character/no-mode career stays byte-identical.
