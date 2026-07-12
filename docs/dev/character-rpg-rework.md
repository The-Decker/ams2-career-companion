# Character / RPG rework — skill tree + talent points

_2026-07-12 · lead-designer spec · builds on `docs/dev/character-system.md`, `docs/dev/career-hub-design.md`_

## 1. Current state (grounded)

- **One currency.** There is no "talent point" pool. "Talent" means the **5 talent stats** (pace/oneLap/craft/racecraft/adaptability) vs the 2 meta stats (marketability/durability). The only spendable currency is **Character Points (CP)**: at creation CP prices perks; per level each grants 3 CP that buy a new perk OR one +0.05 stat step. `CharacterProgress.AvailableCp = CpUnspent + 3*(level-1) - CpSpent` (`CharacterSpend.cs`).
- **Perks are a FLAT list, not a tree.** 42 perks, 9 categories, each an id-keyed priced node with machine-readable benefit+drawback effects; `PerkResolver.Resolve` folds `PerkIds` into `PlayerPerkModifiers`. No `tier`/`requires`/`unlockLevel` anywhere (grep = 0).
- **The deterministic spine already works.** XP is a pure function of journaled results; level is derived; creation rides `player.character` (INPUT, survives WipeDerived); each between-season buy is a `player.statSpend` INPUT re-applied at the transition via `CharacterProgress.ApplyAll`, cost re-derived server-side in `CareerSessionService.SpendCharacterPoint`.
- **The Driver screen is thin & read-only.** `DossierViewModel` has no Level/XP/CP/spend members; the actual spend UI lives in `SeasonReviewViewModel`. `CharacterDossier` already carries `Availability`/`AvailabilityLabel`, the `Talent` flag per stat, and per-perk `Cost` — none rendered. The header shows only a 6px `LevelProgress` bar.
- **Authored-but-dead data.** `softCapByEra` (era level ceiling), the `respec` block + `milestoneEveryLevels`/`milestoneGrant "respecToken"`, and the `statPoints/perLevel` lever (`StatPointsPerLevelBonus`, set by the resolver but unread) all exist in `perks.json` with ZERO code consumers.
- **Already mode-agnostic.** The creator, `CharacterProfile`, and the journal are era-independent; the wizard gates the character step on data availability, not mode, so SMGP / 1967 / FJ-1960 all get the identical creator. `softCapByEra` is the one intended era knob.

## 2. The design

**Talent-points rework = a clarifying rename, not an economy change.** In-career currency becomes **Skill Points (SP)**, numerically identical to today's `AvailableCp` integer (same 3/level, same formula) — so replay stays byte-identical and `PerkBalanceAuditTests` stays valid. "Talent" = the 5 stats. "CP" = creation-only points-buy. "SP" = earned tree currency. This resolves the naming collision the survey found without touching balance.

**Skill tree = a graph over the existing 42 perks + stat-raise nodes.** Additive `perks.json` schema: optional `tier`/`requires[]`/`unlockLevel`/`branch` on each perk (absent = today's behaviour) + a new `skillTree` block (`branchOrder`, `metaBranches`, `statNodes` = the repeatable +0.05 nodes). **No new perk objects** (audit's pinned 42 + no-free-lunch rule untouched). 9 branches reuse the 9 categories; `physical/business/media` are the meta (fame/longevity) lanes. A node is **Unlockable** iff `level>=unlockLevel && requires⊆owned && SP>=cost && !owned`. `PerkResolver`/`PlayerPerkModifiers` need NO change — a node lands in `PerkIds` and folds identically whether chosen at creation or unlocked at level 12.

**Finish the dead features:** wire `softCapByEra` (clamp derived level by the active pack era → 1967 and 2022 reach comparable ceilings — the carryover fairness knob), consume `StatPointsPerLevelBonus` in the SP formula (enables a "faster progression" capstone), and grant milestone respec tokens (1 per 5 levels; equal-or-lower-cost perk swap journaled as `player.respec`).

**Driver-screen expansion:** a **level-up moment** (`LevelUpPending` one-shot, set in `DossierViewModel.Refresh` by comparing prior vs new `Dossier.Level` — Refresh already runs once per applied round), a **skill-tree panel** bound to a new pure Core projection `SkillTree.Build → SkillTreeSnapshot` (mirrors `CharacterDossier.Build`), and **expanded stats/perks** surfacing the already-carried `Availability`, `Talent` split, and per-perk `Cost`.

## 3. Two-Codex lane split + bind contract

- **CODE-Codex** owns Core + ViewModels + Data + `data/rules` + tests: the schema, parsing/validators, gating logic, the `SkillTree` projection, the `DossierViewModel` VM surface, determinism/journaling, respec.
- **GUI-Codex** owns `src/Companion.App/**` (+ the render stand-in): the level-up hero, the tree panel, the expanded stats/perks — binding the named VM members.
- **They work in parallel via a Slice-0 stub commit**: CODE publishes every bind-contract member returning empty/default first, so GUI binds real names immediately. Neither crosses lanes (CODE never edits `src/Companion.App/**`; GUI never edits Core/ViewModels/Data/tests except `DossierViewRenderTests`).
- **Bind contract (exact names):** Core `SkillNodeState{Owned,Unlockable,Locked}`, `SkillNode`, `SkillBranch`, `SkillTreeSnapshot`, `SkillTree.Build(character,level,availableSp,rules)`; `ICareerSession.SkillTree()/RespecTokensAvailable()/RespecNode()` (additive defaults) reusing `AvailableCharacterCp()`+`SpendCharacterPoint()`; `DossierViewModel.{LevelUpPending,LevelsGained,AcknowledgeLevelUp(),SkillPointsAvailable,RespecTokens,SkillTree,UnlockNodeCommand,RespecNodeCommand,TalentStatsView,MetaStatsView,AvailabilityLabel}` + bindable `SkillBranchViewModel`/`SkillNodeViewModel`. **DataContext footgun:** DossierView's ScrollViewer sets inner DataContext to `{Binding Dossier}`, so VM members bind via `RelativeSource AncestorType=UserControl`; Level/XP/Stats bind to the inner `CharacterDossier`.

## 4. Determinism

No new RNG stream (unlocks are level/XP-gated, pure). Unlocks ride the existing `player.statSpend`; cost re-derived server-side, now also validating `unlockLevel`/`requires`. Respec rides a new provenance-excluded `player.respec` phase; tokens derived from level. New `CharacterProfile` fields (if any) `WhenWritingDefault`-omitted + by-value `Equals`. `softCapByEra` clamps derived level only; feature-off careers draw zero and fold byte-identical. The f1db oracle is never touched.

## 5. The two prompts

### code-Codex

```
You are the CODE-editor Codex on the AMS2 Career Companion (C#/.NET 10 WPF, repo root Z:/Claude Code/ams2-career-companion, solution Companion.slnx — there is no .sln). You are building the RPG/skill-tree rework's BACKEND. A second Codex owns the GUI (src/Companion.App/**) in parallel; you must NOT touch src/Companion.App/**. Your lane is Core + ViewModels + Data + data/rules + tests.

GOAL (Mike): level-up opens a SKILL TREE, TALENT POINTS are reworked, the Driver dossier is expanded, and it all carries over to the coming 1967+ semi-historical mode via the SAME creator. Build the data model, the level-gated unlock logic, the read projections, and the DossierViewModel VM surface the GUI-Codex will bind. Ship as ALPHA: breadth over polish, but every determinism rule below is non-negotiable.

== KEY EXISTING TYPES (read before you start) ==
- data/rules/perks.json — characterPoints{creationBudget 6, maxRefundHeadroom 3, maxPerks 5, statSumCap 4.2}; stats{talentStats[5], metaStats[2]}; levels{xpCurve, xpSources, levelGrants{characterPointsPerLevel 3, statStepValue 0.05, statStepCpCost 1, statCapPerRating 0.99, milestoneEveryLevels 5, milestoneGrant "respecToken"}, softCapByEra{...}}; respec{...}; perks[42] across 9 categories.
- src/Companion.Core/Character/CharacterRules.cs — Perk record (Id/Name/Category/Cost/Description/Stream/Effects), PerkEffect, LevelRules, LevelGrants, XpCurve.LevelForTotalXp/XpForLevel/MaxLevel, PerkById/TryGetPerk. Tolerant parser (unknown JSON props ignored).
- src/Companion.Core/Character/CharacterSpend.cs — CharacterSpend{Kind "stat"|"perk", Target, Cost}; CharacterProgress.AvailableCp(character, level, rules) = CpUnspent + CharacterPointsPerLevel*max(0,level-1) - CpSpent; CharacterProgress.Apply/ApplyAll.
- src/Companion.Core/Character/CharacterDossier.cs — CharacterDossier record (Name, Age, Level, Xp, XpIntoLevel, XpForNextLevel, CpUnspent, Stats:DossierStat{Id,Label,Value,Talent}, Perks:DossierPerk{Id,Name,Category,Description,Cost,Benefits,Drawbacks}, InjuryRisk, Availability, AvailabilityLabel, LevelProgress) built by CharacterDossier.Build(...).
- src/Companion.Core/Character/PerkResolver.cs + PlayerPerkModifiers.cs — the fold over PerkIds; note StatPointsPerLevelBonus and StatSoftCapDelta are SET by the resolver; StatSoftCapDelta is consumed, StatPointsPerLevelBonus is currently UNREAD.
- src/Companion.ViewModels/Services/ICareerSession.cs — has CharacterDossier(), AvailableCharacterCp(), SpendCharacterPoint(CharacterSpend), PurchasablePerks()->PurchasablePerk{Id,Name,Category,Cost,Benefits,Drawbacks}. Concrete impl CareerSessionService.cs SpendCharacterPoint derives AUTHORITATIVE cost from rules (never trusts caller), rejects <=0-cost/owned perks, journals a provenance-excluded player.statSpend row (cause 'development') applied at the next transition via EraTransition (CharacterProgress.ApplyAll).
- src/Companion.ViewModels/Hub/DossierViewModel.cs — thin read-only wrapper; Refresh() re-projects session.CharacterDossier() once per applied round (HubViewModel calls it on Summary change). Has NO Level/XP/CP/spend members today.

== THE DESIGN (implement exactly this) ==
1) TALENT-POINTS REWORK = a clarifying rename with ZERO economy/replay change. In-career currency is "Skill Points" (SP). Numerically SP == today's AvailableCp integer (do NOT change the formula or the grant of 3/level). "Talent" now means only the 5 talent stats. Do NOT introduce a second currency (see Open Decision — defaulted to reuse). Keep CharacterSpend and player.statSpend as-is; the tree unlock IS a CharacterSpend.

2) SKILL TREE = a graph over the EXISTING 42 perks + a small set of stat-raise nodes. Additive perks.json schema:
   - On each perk object (all OPTIONAL, absent = today's behaviour): "tier": int (1..4, default 1), "requires": [perkId,...] (default []), "unlockLevel": int (default 1), "branch": string (default = category).
   - New top-level "skillTree" block: { "branchOrder": [9 category ids in display order], "metaBranches": ["physical","business","media"] (the fame/longevity lanes), "statNodes": [ { "id":"raise_pace_1","stat":"pace","tier":1,"unlockLevel":1,"cost":1 }, ... ] — the repeatable +0.05 stat-raise nodes, tier/unlockLevel-gated so deeper raises need higher levels. Author ~2-3 stat nodes per talent stat.
   - DO NOT add new PERK objects (keeps PerkBalanceAuditTests' pinned ExpectedPerkCount=42 and no-free-lunch rules valid). Stat nodes are NOT perks and are not counted by that audit.
3) LEVEL-GATING: a node is Unlockable iff level>=unlockLevel AND requires ⊆ owned PerkIds AND affordable (SP>=cost) AND not already owned/pending. Otherwise Owned or Locked.
4) FINISH TWO AUTHORED-BUT-DEAD FEATURES: (a) softCapByEra — parse into LevelRules, clamp derived level by the active pack's era key so 1967 and 2022 reach comparable ceilings; (b) milestone respec token grant (milestoneEveryLevels) — see Slice 5.
5) Consume the dead StatPointsPerLevelBonus lever in the SP formula so a tree capstone can grant +1 SP/level.

== VM BIND CONTRACT (the GUI-Codex binds these EXACT names; publish the names in Slice 0 as stubs so GUI can start immediately) ==
New pure Core projection in Companion.Core.Character:
  - enum SkillNodeState { Owned, Unlockable, Locked }
  - record SkillNode { string Id; string Name; string Kind ("perk"|"stat"); int Cost; int Tier; int UnlockLevel; IReadOnlyList<string> Requires; IReadOnlyList<string> Benefits; IReadOnlyList<string> Drawbacks; SkillNodeState State; string LockReason; }
  - record SkillBranch { string Id; string Name; bool IsMeta; IReadOnlyList<SkillNode> Nodes; }
  - record SkillTreeSnapshot { IReadOnlyList<SkillBranch> Branches; }
  - static SkillTreeSnapshot SkillTree.Build(CharacterProfile character, int level, int availableSp, CharacterRules rules) — pure, no session coupling (mirror CharacterDossier.Build). LockReason is a ready-to-show string ("Reach level 8" / "Requires: Slipstream Artist" / "Costs 2 SP").
ICareerSession additions (additive defaults so every fake still compiles):
  - SkillTreeSnapshot? SkillTree() => null;
  - int RespecTokensAvailable() => 0;
  - void RespecNode(string nodeId) => throw new NotSupportedException(...);  // Slice 5
  Reuse existing AvailableCharacterCp() (this IS the SP count) and SpendCharacterPoint(CharacterSpend).
DossierViewModel (Companion.ViewModels.Hub) new members — ALL are VM-level members (the GUI reaches them via RelativeSource AncestorType=UserControl because DossierView's ScrollViewer sets inner DataContext to {Binding Dossier}; keep them as public properties/commands on the VM, NOT on CharacterDossier):
  - bool LevelUpPending { get; }          // one-shot, set true in Refresh() when new Dossier.Level > prior level
  - int LevelsGained { get; }             // how many levels jumped since last ack (usually 1)
  - void AcknowledgeLevelUp();            // clears LevelUpPending (RelayCommand ok)
  - int SkillPointsAvailable { get; }     // == _session.AvailableCharacterCp()
  - int RespecTokens { get; }             // == _session.RespecTokensAvailable()
  - IReadOnlyList<SkillBranchViewModel> SkillTree { get; }   // projected from _session.SkillTree()
  - IRelayCommand<SkillNodeViewModel> UnlockNodeCommand { get; }   // -> _session.SpendCharacterPoint(node.Kind=="stat" ? CharacterSpend.Stat(target,cost) : CharacterSpend.Perk(id,cost)); then Refresh()
  - IRelayCommand<SkillNodeViewModel> RespecNodeCommand { get; }   // Slice 5 -> _session.RespecNode(id)
  - IReadOnlyList<DossierStat> TalentStatsView { get; }  // Dossier.Stats.Where(Talent)
  - IReadOnlyList<DossierStat> MetaStatsView { get; }    // Dossier.Stats.Where(!Talent)
  - string AvailabilityLabel { get; }     // == Dossier?.AvailabilityLabel ?? "Fit"
  New bindable VM types in Companion.ViewModels.Hub:
  - SkillBranchViewModel { string Id; string Name; bool IsMeta; IReadOnlyList<SkillNodeViewModel> Nodes; }
  - SkillNodeViewModel { string Id; string Name; string Kind; int Cost; int Tier; int UnlockLevel; IReadOnlyList<string> RequiresLabels; IReadOnlyList<string> Benefits; IReadOnlyList<string> Drawbacks; SkillNodeState State; bool IsOwned; bool CanUnlock; string LockReason; }
GUI numbers for level/XP bind directly to Dossier.Level / Dossier.XpIntoLevel / Dossier.XpForNextLevel / Dossier.LevelProgress (inner context) — do not duplicate those on the VM.

== DETERMINISM CONTRACT (must hold) ==
- No new RNG stream. Unlocks are gated purely by level/XP (pure functions of journaled results). If you add a 'randomize tree' helper, roll a local Pcg32 and JOURNAL THE RESULT (the CharacterViewModel.RandomBuild pattern) — never a replayed draw.
- Every unlock rides the EXISTING player.statSpend INPUT (kind "perk"/"stat"); cost re-derived server-side in CareerSessionService.SpendCharacterPoint (never trust the caller's Cost — extend the existing derivation to also validate unlockLevel<=level and requires⊆owned, rejecting an ineligible node).
- Respec (Slice 5) rides a NEW provenance-excluded input phase player.respec, re-applied on transition; token count derived from level via milestoneEveryLevels minus journaled respec spends.
- Any new field on CharacterProfile must be WhenWritingDefault-omitted AND included in its by-value Equals/GetHashCode (CharacterProfile.cs) so a pre-rework career's blob stays byte-identical and season-boundary re-derivation does not false-diverge.
- The f1db oracle is NEVER touched (none of this feeds the points engine).
- softCapByEra clamps the DERIVED level only; a feature-off / pre-character career draws zero and folds byte-identical.

== SLICE ORDER ==
Slice 0 (UNBLOCK GUI FIRST, same day): add the stubbed VM surface + Core projection types above returning empty/default (SkillTree()=>empty snapshot, SkillPointsAvailable=>AvailableCharacterCp(), LevelUpPending=>false, empty branch list). Commit so the GUI-Codex can bind every name against real (empty) members. No behavior yet.
Slice 1: perks.json schema — add tier/requires/unlockLevel/branch (leave real perks at defaults for now) + the skillTree block + statNodes. Parse into Perk (new Tier/Requires/UnlockLevel), a new SkillTreeRules type, RespecRules, LevelRules.SoftCapByEra, LevelGrants.MilestoneEveryLevels. Load-time validators: requires-ids exist, no cycles, tiers monotonic along requires-chains. TEST: perks.json loads; a pre-rework career replays byte-identical (all new fields absent-safe).
Slice 2: gating + dead-lever wiring. softCapByEra clamps LevelForTotalXp (add an era-aware overload; feed the active pack era). Consume StatPointsPerLevelBonus in CharacterProgress.AvailableCp. Extend PurchasablePerks + SpendCharacterPoint with unlockLevel/requires predicates. TESTS for each.
Slice 3: SkillTree.Build projection + SkillTreeSnapshot/SkillBranch/SkillNode + session.SkillTree(). Pure unit tests (owned/unlockable/locked states, LockReason strings, meta-branch flag).
Slice 4: DossierViewModel expansion — LevelUpPending (prior-vs-new Level compare in Refresh), SkillPointsAvailable, SkillTree VM projection, UnlockNodeCommand/RaiseStat via node, TalentStatsView/MetaStatsView, AvailabilityLabel. Tests in the style of tests/Companion.Tests/ViewModels/CharacterDossierHubTests.cs. Also EXTEND the render stand-in host expectations if needed (coordinate: the GUI-Codex owns DossierViewRenderTests' DossierHost, but tell them which members it must expose — see bind contract).
Slice 5 (respec, optional/last): parse+consume RespecRules, grant milestone tokens, player.respec phase, session.RespecNode/RespecTokensAvailable, RespecNodeCommand. Replay fixtures (modern + 1967) asserting byte-identical.
Slice 6: determinism fixtures — a tree-unlock career and a 1967-carryover career, both replayed byte-identical; add to the existing replay test suite.

Author real node/branch content in perks.json (period-appropriate names via the existing prose style). Run dotnet build + dotnet test from repo root after each slice; the oracle suite and PerkBalanceAuditTests must stay green (do not change their pinned constants). Coordinate ONLY through the bind-contract member names — do not edit src/Companion.App/**.
```

### GUI-Codex

```
You are the GUI-editor Codex on the AMS2 Career Companion (C#/.NET 10 WPF, repo root Z:/Claude Code/ams2-career-companion, solution Companion.slnx — no .sln). You are building the RPG/skill-tree rework's SCREENS. A second Codex owns the backend (Core/ViewModels/Data/tests) in parallel; you must NOT touch Core, ViewModels, Data, data/rules, or tests except the render-harness. Your lane is src/Companion.App/** (Views/Themes/Styles) plus the render stand-in in tests/Companion.RenderHarness.Tests.

GOAL (Mike): level-up opens a SKILL TREE, the Driver dossier is EXPANDED to show the level-up moment + the tree + richer stats/perks, and it looks alive. Ship as ALPHA: breadth over polish, but real, bindable, laid-out screens. You bind against a fixed VM surface the code-Codex publishes as stubs on day one (Slice 0), so you can build every control before the logic is finished.

== THE CRITICAL DATACONTEXT FOOTGUN (read first) ==
In src/Companion.App/Views/DossierView.xaml the ScrollViewer sets its inner DataContext to {Binding Dossier} (a CharacterDossier). So:
- Bindings to CharacterDossier fields (Level, Xp, XpIntoLevel, XpForNextLevel, LevelProgress, Stats, Perks, Age, AvailabilityLabel) resolve against the INNER context — bind them plainly (e.g. {Binding Level}).
- Bindings to any VIEW-MODEL member (SkillPointsAvailable, SkillTree, UnlockNodeCommand, LevelUpPending, RespecTokens, TalentStatsView, etc.) MUST use RelativeSource AncestorType=UserControl -> DataContext, exactly like the existing TeamLine/PlayerImageKey/Timeline bindings at DossierView.xaml:46,:96-111. Get this wrong and it silently binds against CharacterDossier and shows nothing.

== VM BIND CONTRACT (bind these EXACT names — the code-Codex implements them) ==
On DossierViewModel (reach via RelativeSource=UserControl):
  - bool LevelUpPending — one-shot; show a "LEVEL UP" banner/celebration when true; AcknowledgeLevelUp() dismisses it.
  - int LevelsGained
  - void AcknowledgeLevelUp() (command)
  - int SkillPointsAvailable — label it "Skill Points" (NOT "CP"). This is the tree spend currency.
  - int RespecTokens
  - IReadOnlyList<SkillBranchViewModel> SkillTree
  - IRelayCommand<SkillNodeViewModel> UnlockNodeCommand (button on each Unlockable node; CommandParameter = the node)
  - IRelayCommand<SkillNodeViewModel> RespecNodeCommand
  - IReadOnlyList<DossierStat> TalentStatsView / MetaStatsView (the split stat lists)
  - string AvailabilityLabel
SkillBranchViewModel: { string Id; string Name; bool IsMeta; IReadOnlyList<SkillNodeViewModel> Nodes; }
SkillNodeViewModel: { string Id; string Name; string Kind ("perk"|"stat"); int Cost; int Tier; int UnlockLevel; IReadOnlyList<string> RequiresLabels; IReadOnlyList<string> Benefits; IReadOnlyList<string> Drawbacks; SkillNodeState State (Owned|Unlockable|Locked); bool IsOwned; bool CanUnlock; string LockReason; }
Level/XP numbers bind to the INNER CharacterDossier context: {Binding Level}, {Binding XpIntoLevel}, {Binding XpForNextLevel}, {Binding LevelProgress}, {Binding AvailabilityLabel}, and Stats/Perks item fields (DossierStat{Id,Label,Value,Talent}; DossierPerk{Name,Category,Description,Cost,Benefits,Drawbacks}).

== WHAT TO BUILD (in src/Companion.App/Views/DossierView.xaml + Themes) ==
Slice 1 — LEVEL-UP MOMENT: promote the thin progression row (DossierView.xaml:48-78, currently just "Level n / Age / Xp / points" + a 6px bar) into a proper hero: a large Level badge, an XP-into-level meter using {Binding XpIntoLevel}/{Binding XpForNextLevel}/{Binding LevelProgress} with a real "X / Y XP to next level" label, and a "Skill Points to spend" call-to-action gated on SkillPointsAvailable>0 (use a value converter for the >0 visibility; a CountVisible converter already exists in the codebase). Add a LEVEL-UP banner bound to LevelUpPending (RelativeSource=UserControl) with a dismiss that calls AcknowledgeLevelUp().
Slice 2 — SKILL-TREE PANEL: a NEW region (natural home: between the header Grid and the existing "Stats" section, or replacing the read-only "Perks" list at :199-230). ItemsControl over SkillTree (branches) -> each branch a labelled lane (badge IsMeta lanes differently — fame/longevity vs sim), inner ItemsControl over Nodes -> node cards. Card shows Name, Cost ("N SP"), Benefits (+) / Drawbacks (-), and styles by State: Owned (filled/checked), Unlockable (highlighted + an "Unlock" Button bound to UnlockNodeCommand, CommandParameter={Binding}), Locked (dimmed + LockReason text). Order nodes by Tier within a branch.
Slice 3 — EXPANDED STATS: replace the flat 7-bar Stats block (:180-196) with a talent/meta split (bind two ItemsControls to TalentStatsView and MetaStatsView, or GroupBy the Talent flag), colour talent vs meta, and show the value.
Slice 4 — PERKS + AVAILABILITY: surface AvailabilityLabel as a status line (Fit / Injured — out N races / Season over / Deceased), and add the per-perk Cost chip on the existing perk cards (DossierPerk.Cost is already carried, never shown).
Slice 5 (optional) — respec affordance: a RespecTokens counter + a per-owned-node RespecNodeCommand button.

== RENDER HARNESS (you own this test) ==
tests/Companion.RenderHarness.Tests/DossierViewRenderTests.cs builds a stand-in "DossierHost" that today exposes ONLY a Dossier property. Any new VM-member binding will bind against a host lacking that member and the render test will catch it. EXTEND DossierHost to expose the new VM members you bind (SkillPointsAvailable, SkillTree, LevelUpPending, RespecTokens, TalentStatsView/MetaStatsView, AvailabilityLabel, the commands) with sample data, and extend the sample CharacterDossier to exercise the Talent split + perk Cost, so the new UI actually lays out. Keep the test self-skipping off Windows/STA as it is now. This is the ONLY test file you edit.

== DETERMINISM / LANE ==
You write NO game logic — every number and command comes from the bind contract. Do not add code-behind that computes state; bind only. Do not touch Core/ViewModels/Data/data/rules or any test except DossierViewRenderTests. If a bound member isn't implemented yet, the code-Codex's Slice-0 stub returns empty/default so your layout still renders. Coordinate ONLY through the bind-contract names. Build with dotnet build; run the render harness to verify layout (ActualWidth/Height>0). Match the existing DossierView visual language (Muted/Faint runs, ProgressBar styling, team-coloured accents).
```
