# CONTINUE — Character creator / finish increment 4 (resume prompt)

Resume the **AMS2 Career Companion** character-creator work (`Z:\Claude Code\ams2-career-companion`,
WPF / .NET 10, branch **`hub/increment-4`**). **Mike's want:** work on the character creator and
*finish increment 4*. Two agents share the repo — **Claude = SMGP + the app (this lane)**, **Codex =
the 1967 era** (`era/1967` worktree) — stay out of `data/rules/smgp/**`, `src/**/Smgp/**`,
`packs/f1-1967/**`, `data/rules/news/**` (Codex's data).

## First: orient
1. **Read**: `docs/dev/character-system.md` (the 632-line design spec — the source of truth), then
   `MEMORY.md` → `ams2-hub-build-progress.md` (top blocks), `mike-build-maximally.md`.
2. **Verify the repo**: `git log --oneline -8`, `git status`, `dotnet test Companion.slnx`
   (**1811 + 46** green as of `582292a`).
3. **Discipline**: f1db oracle **77/77 NEVER touched**; **byte-identical replay LOCKED** — perk
   effects fold through the pure sim via `PlayerPerkModifiers` (identity-defaulting), are
   envelope-versioned + per-career gated, and a character-free career must stay byte-identical (the
   whole point of the identity default). Commit + push per slice; republish when the app is CLOSED.

## What's BUILT (the character system is largely done)
- **Design**: `docs/dev/character-system.md` — 5 talent stats (map to `PackDriverRatings`), 2 career
  meta-stats, round-conditional stats, XP/levels/progression (pure function of journaled results,
  no dice), the hybrid balance mechanism + CI-testable audit (§4.1, §11), creation flow (§5), perk
  folding (§6), the JSON schema (§7), **42 perks + 7 archetype presets** (§8), the wiring checklist
  (§9), open questions RESOLVED (§10), and the adversarial balance audit (§11, all checks pass).
- **Core** (`src/Companion.Core/Character/`): `CharacterProfile`, `CharacterRules`, `CharacterSpend`,
  `PerkResolver`, `PerkDescriber`, `PlayerPerkModifiers`, `CharacterRatingWriter`, `CharacterDossier`.
- **Data**: `data/rules/perks.json` (~42 perks + archetypes). **UI**: `CharacterViewModel` (wizard
  character step) + `CharacterView.xaml` (leads with the team player image + the car you'll drive;
  uses Open Sans). Folds via `PerkResolver.Resolve` → `PlayerPerkModifiers` into `RoundUpdate` /
  `SeasonEndPipeline` / the grid patch. XP at season end (`XpMath.PerSeason`).

## FIRST TASK — survey then finish
The system is built; the job is to **find what's rough/incomplete and close it to "finish increment
4."** Do a survey pass FIRST (build the gap list), then pick with Mike:
1. Read `character-system.md §9` (wiring checklist) and `§5` (creation flow) and diff them against
   the actual `CharacterView`/`CharacterViewModel` + `CareerSessionService` wiring — what's checked
   off vs. still stubbed?
2. Drive the create flow in the app (`/run` or launch the RC) and judge the CREATE screen UX: stat
   allocation, perk pick/spend (points budget, the balance audit surfaced to the player), archetype
   presets, the name/portrait/team lead-in, validation. Where is it rough, confusing, or unfinished?
3. Check test coverage: `CharacterSpend`/`PerkResolver`/balance-audit tests, `XpMath`, the wizard
   character-step tests — any gaps?
4. Known deferred: the **per-team CAR SPECS card** (machine/engine/power + ENG-TM-SUS-TIRE-BRA bars
   on the character + rival screens) — blocked on Mike's numbers; wire the UI + data shape so it's
   ready to drop them in.

Then propose a concrete "here's what's left to finish increment 4" list and go. Mike's bar: **fully
immersive** — "all the management content we can imagine," free-choice-viable perks, stats, levels,
everything felt. Keep it deterministic + replay-safe; a no-character career stays byte-identical.
