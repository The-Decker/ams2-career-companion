# Racing Passport — pure-racing activation (implementation + preflight record)

_2026-07-18, Claude (Head of Coding), mission `racing-passport-pure-racing` on branch
`mission/racing-passport-pure-racing`. This doc records the mission's preflight answers (mission
§2) and the implementation design. The AUTHORITATIVE product decision is the mission brief:
**Racing Passport is a PURE-RACING mode — no XP, no levels, no SP, no DNA, no skills, no mastery,
no owner economy, no threads, no shared progression.** The shared-progression Passport design in
`career-modes-alpha1.md` §5–§7 is SUPERSEDED (reconciled in the docs pass)._

## Preflight answers (mission §2)

**1. Where Passport is disabled today.**
- `CampaignCreationPlanner.cs:90-92` — throws "Racing Passport requires its portfolio activity
  ledger and cannot be created as a single career file yet."
- `StartViewModel.cs` — the mode card has `IsAvailable = false` with old shared-progression copy.
- `NewCareerWizardViewModel.cs:71-76` — the ctor throws for any explicit mode other than Dynasty
  or SMGP.
- `CharacterCreationInput.cs:51` — validation comment claiming Passport uses portfolio state.

**2. Code paths assuming an explicit Alpha mode requires a progression-v2 character.**
- `CampaignCreationPlanner.Prepare` line 93-94 (throws without a v2 character) +
  `ValidateContextualRacingDnaChoice` (Racing DNA contextual validation).
- `NewCareerWizardViewModel`: `IsProgressionV2` (explicit mode ⇒ v2), `HasCharacterStep`
  (rules-dir driven), `Create()` builds a v2 profile for explicit modes, `LoadSelectedPack`
  resolves a bounded campaign plan for any explicit mode.

**3. Places that could accidentally seed forbidden state — and their gates.**
- XP/level/SP/DNA/skills: all ride `request.Character` → Passport passes `Character = null`, so
  `SeedStartStates` and the `player.character` journal row (line 347) never fire. The wizard's
  character step is skipped (`HasCharacterStep = false`), so no v2 profile is ever built.
- Dynasty economy: seeded only when `request.DynastyEconomy && plan.Mode == grandPrixDynasty`
  (creation line 388). Passport has no Dynasty plan ⇒ inert. Additionally the planner now REJECTS
  `DynastyEconomy = true` for Passport (contradictory input).
- Contracts/offers: `SeasonEndPipeline` step 5 (AI seat market) — gated for Passport (below).
- Mortality/injury: `request.Mortality` defaults Off; the wizard shows no mortality picker for
  Passport; the accident stream is never drawn at Off.
- SMGP state: `SmgpMode` flag + pack style — the Passport picker excludes `careerStyle == "smgp"`,
  and the planner rejects both.
- Campaign plan: `CampaignProgressionPlan` (bounded-campaign shape with XP scale fields) is NOT
  used for Passport; the pinned-pack machinery is reused directly (below).

**4. Safely reusable ordinary historical-career systems.**
Pack discovery + structural validation + community-baseline import; the clean seat-swap
(`ResolvePlayerDriverId`, the replaced driver benched, no cascade); pinned-pack envelope
(`campaign?.DistinctPacks ?? [selectedPin]`, one pack pinned); the entire round fold, weekend
structure, standings engine, qualifying, DNQ behavior, staging, news/history, season review,
replay/oracle; `FormAware`, modded-field and alternate-track opt-ins; mortality-Off default;
`PlayerIdentity()` name resolution (extended by one additive field).

**5. Outdated Passport tests/docs replaced.**
`CareerModeMenuTests` (unavailable assertions → available + new copy), the wizard rejection tests
in `CharacterWizardTests` / `CampaignProgressionCreationTests` (`InvalidVersionTwoModes` member
data drops Passport, replaced by acceptance coverage), `career-modes-alpha1.md` §5–§7,
`dynasty-passport-roadmap.md`, `docs/PROJECT.md`, this doc.

## The design (as implemented)

- **Mode identity:** `CareerExperienceModes.RacingPassport` stays the stable serialized id. A
  Passport save is an ordinary self-contained `.ams2career` with `experienceMode =
  "racingPassport"` persisted on the start player state, exactly one pinned pack (the selected
  season), NO campaign plan, NO character, NO economy, mortality Off.
- **Planner:** `CampaignCreationPlanner.Prepare` gains an explicit Passport branch: rejects
  contradictory input (character present, `DynastyEconomy`, `SmgpMode`, smgp pack), requires NO
  v2 character, performs NO DNA validation and NO bounded-plan resolution, and returns a
  preparation carrying only the selected pin (so the exact pack bytes/version/hash are pinned at
  creation). `CampaignCreationPreparation.Plan`/`.CharacterInput` become nullable.
- **Player name (smallest truthful identity):** additive `CareerCreationRequest.PlayerDisplayName`
  (string?, validated: trimmed, max-length, non-empty-if-present) + additive
  `PlayerCareerState.CustomDisplayName` (`WhenWritingNull`, in Equals/GetHashCode — legacy blobs
  byte-identical). Resolution: `CharacterName() ?? CustomDisplayName ?? (distinct ? default :
  null)` — an unnamed Passport keeps showing the replaced driver's authored name; a custom name
  flows to grids/results/standings/news/history via the existing `PlayerIdentity()` channel. No
  journal row; the start state IS the input (the FormAware pattern).
- **Season behavior:** `NextSeason()` returns null and `StartNextSeason` throws for a Passport
  career (one season IS the campaign; `SeasonComplete` drives the review). `SeasonEndPipeline`
  gains an additive `PureRacingSeason` context flag (default false ⇒ every other career folds
  byte-identically): when set, the AI seat market, offers, aging, and retirement steps are
  skipped; final standings, headlines, records, and the review are produced normally. The
  per-round fold is UNTOUCHED (reputation/OPI/pace anchors are racing-condition trackers, not
  progression; §13's "do not bypass the deterministic fold" is honored).
- **Wizard:** accepts `racingPassport`; pack list already excludes SMGP for explicit modes;
  route = SeasonPick → Verification → SeatPick → Confirm (`HasCharacterStep`/`HasGridStep` both
  false for Passport); `PlayerDisplayName` + `PlayerDisplayNameError` on SeatPick; confirm copy
  per the mission §20 (no progression/economy lines); `Create()` passes `Character = null`,
  `GridSelection = null` (whole faithful field), `PlayerDisplayName`, mortality Off, economy off.
- **Start card:** `IsAvailable = true`, the §6 copy. `StartCareerModeCommand` routes like the
  other two cards.
- **GUI handoff:** `docs/dev/racing-passport-pure-racing-gui-handoff.md` lists the VM surface
  (traversal flags, name field, confirm copy) for the parallel GUI lane; no `src/Companion.App/**`
  edits in this mission.
