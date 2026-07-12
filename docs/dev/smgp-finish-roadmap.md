# SMGP finish-line roadmap

_Dated 2026-07-12. Synthesis of the built / designed-unbuilt / mike-wants / gui-ux surveys, grounded in the repo + auto-memory. House rule (mike-build-maximally): **1.0 = a playable alpha of every feature**, not a polished ship — build breadth now, polish later._

## Definition of done — "SMGP finished / 1.0 = alpha"

SMGP is done when a fresh SMGP career can be played end-to-end with **every shipped mechanic visible and reachable in the RC exe** — nothing important stuck behind a missing screen. Concretely:

1. **Death/injury is playable, not just folded.** The wizard mortality choice (Off/Normal/Hardcore, Hardcore unmistakably dangerous), the Normal save/reload panel, the result-screen Light/Medium/Heavy severity picker, the injured sit-out auto-sim screen, and the death/permadeath screen (Normal→Restore, Hardcore→final DB-free) all render and bind the already-shipped VMs. **This is the only P0 gap — Codex GUI round 5.**
2. **The RC exe is rebuilt, deployed to `dist/` (old backed up), and pushed** so Mike can alpha-test. It's deliberately un-rebuilt today (no GUI consumer yet).
3. **No rough game-over:** a Level-D floor knock-out or a fatal accident **hard-stops** the career (no more foldable rounds), and the legacy one-line "CAREER OVER" callout is reconciled with the new immersion ending.
4. The core loop, 16-race season, 17-season campaign + locked finale, clean seat-swap, promotion/demotion, rival ladder, seeded per-race DNQ field, Paddock, live stats, dispatches, and news outlet all remain shipping and byte-identical (**already TRUE**).
5. **Determinism preserved:** remaining fold-touching work is envelope-versioned + per-career gated (feature-off ⇒ zero draws); display-only projections never touch the fold; the f1db oracle is never touched.

**Explicitly NOT required for SMGP-1.0-alpha** (post-SMGP arc, parked behind it): the Tycoon Team-Mode **economy** (only a read-only spine + shell exist; it's really the 1967+ semi-historical mode), the dynamic life-sim event deck beyond the shipped Setup Gamble, morale/form swings, the contract-negotiation minigame, the Formula Junior 1960 prologue. Per-race livery staging (Slice 3) and missing round/team art are **finish-the-fantasy P1**, not alpha blockers (clean fallbacks exist).

## Roadmap

| Priority | Epic | Owner | Depends on | Scope |
|---|---|---|---|---|
| **P0** | Death/injury SCREENS (GUI round 5) | Codex | — (backend shipped `89ed505`/`2dcbd78`) | Bind the shipped VMs: wizard `MortalityMode` radio, Normal save/reload panel, ResultEntry severity picker, sit-out screen, death/permadeath screen, dossier availability line. Brief: `docs/dev/codex-gui-round5-brief.md`. |
| **P0** | SMGP CareerOver hard-stop | Claude | — | Gate round-fold entry once floor knock-out or `Deceased`; route to one consistent ending. |
| **P0** | RC rebuild + deploy + push | either | round-5 + hard-stop | Merge `codex/gui-round5`, build, suite green, back up + deploy exe, push `hub/increment-4`. |
| **P1** | Per-race livery staging (DNQ Slice 3) | Claude | — | Stage only the round's ≤26 qualifiers' skins; DNQ tail → base paint. Cosmetic. |
| **P1** | SMGP living-flavour data + celebration hook | Claude | — | Per-round/per-rival quote data files; reshuffle-by-points; CampaignFlawless celebration projection. |
| **P1** | Mike sign-offs + art supply | Mike | round-5 (save UX) | Car-spec numbers; save-slot UX; 9 team photos + 16 round cards. |
| **P1** | Skin-install ownership + launch-direct | either | — | App owns/protects mod files vs RCM; direct AMS2 launch. |
| **P1** | Legacy career-over ↔ death-screen reconcile | Codex | round-5 | One consistent game-over UI. |
| **P2** | Tycoon Team-Mode ECONOMY (big Claude epic) | Claude | SMGP alpha shipped | `era.economy` identity → real Budget-Unit fold; sponsors/prize in, salary/R&D out; hiring; gated so SMGP draws zero. Design doc first. |
| **P2** | Life-sim event deck v1 (Media/Sponsor/Development) | Claude | Tycoon economy | Seeded opt-in cards modulating inputs/scalars, never on-track results. |
| **P2** | Morale/form life-sim + negotiation minigame | Claude | deck/economy | Draw the inert form-swing stream; patience-meter volley over `OfferDocument`. |
| **P2** | Formula Junior 1960 prologue + optional art | Codex | SMGP alpha shipped | Faithful real-1960 F-Junior pack; optional round/banner/sprite art. |

## Claude continue-prompts (paste-ready, priority order)

_Lane: Claude = Core/ViewModels/Data + tests. Codex = `src/Companion.App/**`. Determinism contract: new player input → versioned envelope; new outcome → DERIVED journal row; new draw → keyed `StreamFactory` stream, gated so a feature-off career draws zero; display-only projections never touch the fold; the f1db oracle is never touched._

### 1 — SMGP CareerOver hard-stop + game-over reconciliation (P0)

```
Continue the AMS2 Career Companion (Z:\Claude Code\ams2-career-companion, hub/increment-4). Read the memory
TOP block + docs/dev/smgp-design.md first. Claude lane = Core/ViewModels/Data + tests; do NOT touch
src/Companion.App/** (Codex).

Close memory L129's roughest deferred-tail edge: an SMGP career that has hit the Level-D Zeroforce FLOOR
knock-out OR a fatal accident (PlayerMortalityStatus.Deceased) sets a career-over STATE, but the SMGP fold
path STILL lets another weekend be folded behind it. Make it terminal:
- Gate round-fold entry in CareerSessionService (the SMGP round-fold + CurrentSmgpDemotion/floor path;
  SmgpRules Zeroforce floor) so once the career is over — floor knock-out OR Deceased — no further round
  folds; the session surfaces the ending instead of the next briefing.
- Align the terminal flag/projection with the existing shell routing (HomeViewModel.CareerOver /
  IsSitOutStep) so Codex can point ONE consistent ending screen at both the SMGP floor game-over and the
  mortality death (Codex reconciles the legacy BriefingView SmgpCareerOverPanel — that's their lane).

Determinism: pure control-flow gate over already-folded state — no new draw, no fold row, no envelope. Ship
a byte-identity re-sim test on a floored career AND a dead career proving the gate never perturbs the fold;
an Off/alive career is unaffected. Whole suite green. Commit.
```

### 2 — Per-race SMGP livery staging (DNQ Slice 3) (P1) — ⛔ SUPERSEDED, DO NOT BUILD

> **Superseded 2026-07-12 after seam exploration.** This prompt asked to park the round's non-qualifier
> liveries per race — but that is *exactly* the per-race rotation `CareerSessionService.cs:3036-3044` already
> deliberately rejects: AMS2 loads a car model's custom liveries ONCE at launch, so parking non-qualifiers
> mid-season makes those cars pool-fill with random STOCK drivers and forces a full game restart every round.
> The shipping "activate every SMGP livery that fits the model's slot cap, once, park nothing" is the correct
> solution (the pre-qualifying field is display-only; whatever fits the cap stays painted, no restart). The
> only residual refinement — if a model's cap is < the pack size — would be to make the over-cap *skip* prefer
> the weakest-pace (perennial-DNQ) liveries so qualifiers keep slot priority, a STABLE no-restart tweak; even
> that is optional. Do not implement the prompt below.

```
Continue the AMS2 Career Companion (hub/increment-4). Read the memory TOP block + ams2-next-content-arc
(Slice 3) first. Claude lane = src/**/Smgp/** + src/Companion.Ams2/Skins/** + tests.

Finish the "34 skins vs 26-car cap" fantasy (Mike: swap out the cars that did not qualify, like 1988). The
seeded per-race DNQ FIELD already ships (SmgpDnqField, deterministic per-round starterDriverIds pinned at
CreateCareer). Stage per race ONLY that round's qualifiers' liveries:
- Read the round's SmgpDnqField.starterDriverIds in the skin-staging path (RoundGridResolver already has
  ignoreStarters + weakest-first mapping; VariantOverrideBinder already skips foreign-season variant XMLs).
- Stage only those drivers' customs; the non-qualifying tail floors to base-game paint. Honor Mike's rule
  "inactive skins must not go on the grid".
- Test: for a couple of rounds off a fixed career seed, the staged livery set equals starterDriverIds and
  excludes the DNQ tail.

Determinism: cosmetic staging only — never touches the fold, the oracle, or replay bytes; derives entirely
from the already-seeded SmgpDnqField (no new stream). Whole suite green. Commit.
```

### 3 — SMGP living-flavour data files + campaign celebration hook (P1)

```
Continue the AMS2 Career Companion (hub/increment-4). Read the memory TOP block + docs/dev/smgp-design.md
first. Claude lane = Core/ViewModels/Data + tests; Codex binds the finale XAML.

Finish the SMGP deferred-tail flavour (memory L129):
1. Replace the two hard-coded per-round pit-crew-advice / per-rival quote lines with data-driven corpora —
   mirror data/rules/smgp/rival-quotes.json + the SmgpRivalQuotes loader EXACTLY (load-validated).
2. Confirm/where-missing wire the between-season grid reshuffle-by-standings (seeded, gated, replay-safe).
3. Expose a CampaignFlawless (all-17-titles) celebration projection the SmgpFinaleViewModel can bind, tied
   to the already-present ultimate.jpg unlock (SmgpRules CampaignSeasons=17 / CampaignComplete /
   CampaignFlawless).
Sync data/rules -> dist/data/rules. Tests: corpus load-validation + the flawless projection.

Determinism: quote corpora + celebration projection are DISPLAY-ONLY (no fold input). Any between-season
reshuffle touching the grid must be seeded off the master seed + per-career gated + envelope-versioned;
ship a byte-identity re-sim test proving a reshuffled multi-season career replays identically and a legacy
career is unchanged. Whole suite green. Commit.
```

### 4 — Tycoon Team-Mode ECONOMY: design doc + v1 fold spine (P2, POST-SMGP)

```
Continue the AMS2 Career Companion (hub/increment-4). SEQUENCED AFTER SMGP alpha ships (ams2-next-content-arc:
tycoon is the 1967+ semi-historical mode, parked behind SMGP). Read career-hub-design.md (§12 + decision 19)
first. Claude lane = Core/ViewModels/Data + tests; Codex renders the HubView TycoonDashboardPanel.

Stand up the biggest remaining Claude fold epic — turn the read-only spine + the era.economy IDENTITY seam
into a real Budget-Unit economy.
PHASE 0 (doc, no code): write docs/dev/tycoon-economy.md — the fold model, Budget-Unit accounting (income:
sponsors + prize/appearance money; outgo: driver salaries + R&D/development), hiring/firing, per-season
reconcile, the envelope/journal/stream plan, and the on/off gate (SMGP = economy off).
PHASE 1 (v1 spine): implement the accounting as DERIVED journal rows over new keyed streams feeding the
read-only projections the HubView TycoonDashboardPanel already renders. Do NOT add player fold INPUT beyond
what the design blesses.

Seams: CareerStreams.EraEconomy='era.economy' (currently identity — the reserved seam); CareerStates.cs
budget tier 1-5 + PayBudgetBu (Budget Units/season); OfferDocument.PayBudgetBu; StreamFactory.CreateStream/
CreateSeasonStream; JournalEvent.Phase (new DERIVED phases); ICareerSession.SmgpTeamDashboard() (ef451f2) +
SmgpTeamDashboardEntry as the projection sink. NOTE: the "seed of Tycoon" framing of the SMGP dashboard is
SUPERSEDED — keep it as a display-only SMGP team view.

Determinism: every input envelope-versioned; every outcome a DERIVED journal row; every draw a keyed stream;
GATED so an economy-off career (all SMGP) draws zero + replays byte-identical; read-only projections never
touch the fold; oracle untouched. Ship a byte-identity re-sim proving an SMGP career is unperturbed. Tests
per slice.
```

### 5 — Life-sim event deck v1: Media / Sponsor / Development (P2, POST-SMGP)

```
Continue the AMS2 Career Companion (hub/increment-4). AFTER the Tycoon economy spine (Development Allocation
ties to budgets). Read career-hub-design.md §9 + §12 first. Claude lane = Core/ViewModels/Data + tests;
Codex renders the event VMs.

Grow the life-sim event deck past its single shipped card (Setup Gamble = CalledShotMath / player.call).
Build Media Moment, Sponsor Pitch, Development Allocation as opt-in seeded events that modulate inputs/
car-scalars, NEVER an on-track result. For each: a seeded event drawing its reserved RNG stream, an
envelope-versioned player choice, a DERIVED journal row (sponsor.pitch / dev.alloc phases reserved), a small
data corpus, and a VM the hub surfaces. Mirror the CalledShotMath + JournalPhases.PlayerCall 'player.call'
template. Effects modulate via the perk-modifier path (PlayerPerkModifiers / PerkResolver).

Determinism: each card = keyed StreamFactory stream + envelope-versioned input + DERIVED journal row, GATED
so a declined/absent card draws zero + replays byte-identical; never an on-track result; oracle untouched.
Tests + a byte-identity re-sim proving a career that declines every card draws zero. Commit per card.
```
