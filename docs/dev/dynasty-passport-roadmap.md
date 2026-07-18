# Grand Prix Dynasty gating + debug menu + Racing Passport — roadmap

_2026-07-17, Claude (Head of Coding). Mike's direction: Dynasty is a chronologically-gated
historical timeline you play forward from the beginning; a debug menu previews everything during
development; Racing Passport activates and holds the historical seasons as threads. Fable builds the
Dynasty tycoon economy in parallel (`docs/dev/fable-tycoon-brief.md`) — this doc is the mode
infrastructure around it (Claude's lane: Core/ViewModels/Data)._

> **Status (2026-07-17):** Piece 2 (debug menu) SHIPPED + deployed. Piece 1 (Dynasty gating)
> SHIPPED. **Piece 3 (Racing Passport) is the one open piece** — the largest of the three.

## Content reality (drives the gating)

Faithful packs on disk today: **1967, 1969, 1974, 1978, 1983, 1985, 1986, 1988, 1990–1993, 1995,
1997, 2000, 2005, 2006, 2008, 2010, 2016, 2020** (21). **No pre-1967 / Formula Junior pack exists
yet.** So "start at Formula Junior" is the *timeline concept*; 1967 is the first *playable* season.
Unbuilt years render as preview/"coming soon", never as playable, never synthesized
(career-modes-alpha1.md §3: missing years are unavailable, never fabricated).

---

## Piece 1 — Dynasty chronological gating — SHIPPED (2026-07-17)

In `grandPrixDynasty` you advance chronologically through the pinned timeline: only the current
(earliest unfinished) pinned season is playable; earlier are completed history; later are
preview-locked. What shipped vs. the original sketch:

- **Head-only entry already held by construction** — creation pins the ordered
  `pinnedSeasonSequence` and enters its head; `NextSeason()`/`StartNextSeason` can only ever target
  `PinnedSeasonSequence[_seasonOrdinal]` (no jump path exists). Proven by
  `DynastyContinuation_IgnoresMutableInterveningPackAndStartsPrePinnedOccurrence` (a tempting
  on-disk 1968 is ignored; BridgedYears honest; replay byte-identical). The debug menu (Piece 2) is
  the sanctioned bypass.
- **Preview projection:** `CampaignSeasonPreview` (year, series, decade `EraLabel`, round count,
  venues, teams) rides `CampaignTimelineEntry.Preview` on LOCKED Dynasty seasons, built once per
  session from the pre-pinned bytes (`CareerSessionService.DynastySeasonPreviews`, display-only,
  never a play entry). Historical years are real-world known, so previewing them is fine; the gate
  is about *playing in order*, not hiding — the deliberate opposite of the SMGP no-spoiler rule,
  whose locked seasons keep `Preview` null (tested).
- **Formula Junior prologue slot:** a synthetic ordinal-0 `CampaignTimelineEntry` (`IsPrologue`,
  Locked, "Formula Junior → 1967", "Pre-championship prologue, coming soon") heads every Dynasty
  timeline whose plan starts ≤1967 — the timeline starts where the story should even though play
  starts at 1967.
- **Tests:** `CampaignTimelineTests` Dynasty section (prologue/current/previewed-locked, the
  advance arc, display-only-never-journals) + the existing creation/continuation coverage.
- **GUI handoff:** the timeline tab binds `Preview`/`IsPrologue` when Codex does the Dynasty wave.

## Piece 2 — Debug menu (dev-only) — SHIPPED (2026-07-17)

The dev-only menu that previews/unlocks everything while we build: jump to any year/season/mode,
force money/injury/terminal states, unlock locked seasons, reveal SMGP future lore. Gated behind
`Settings.DeveloperMode` (off by default, not in the normal UI; Ctrl+Shift+F12 unlocks,
Ctrl+Shift+D opens; `AMS2_DEVMODE=1` env) so a shipped Release with the flag off shows nothing.
Two fold-safe tiers: Tier-1 REAL replay-safe throwaway careers (advanced only through
provenance-excluded input mutators; `DebugCareerResimTests` proves byte-identical resim) and Tier-2
non-persistent preview sessions. Every command routes the same session/fold seams — no back doors.

---

## Piece 3 — Racing Passport activation (OPEN — the largest piece)

**Goal:** turn on `racingPassport` (currently fail-closed at
`CampaignCreationPlanner.cs:90` — "requires its portfolio activity ledger and cannot be created as a
single career file yet"). Passport holds the **historical seasons as independent threads** — the
"original" historical careers, now under one persistent character.

Implements career-modes-alpha1.md §5 (read it fully — the model is already specified):
- One SQLite DB, one **root Passport character** (global XP/level/SP/mastery/DNA), many
  **career threads** (thread-local seat/standings/injury/news), an **activity ledger** (authoritative
  order), and the **portfolio progression** (creditedExperienceKeys, `creditedReferenceProgress`,
  `portfolioPool` gate to 499 SP over 16 credited season-equivalents — the exact math in
  `CharacterProgressionV2Math`/`character-progression-v2.md` §5.4).
- Thread switching only at atomic checkpoints (before a result, or after a result + all derived rows
  commit — never mid-decision).
- Anti-double-credit: a re-simulated or cloned thread records its local result but never awards the
  same global XP credit twice (the credited-key set is the guard).
- This is the largest of the three (cross-thread ledger + switching + one-DB multi-thread) and must
  keep the byte-identical-replay contract per thread.
- Tests: create root + a 1967 thread + an SMGP thread; switch at checkpoints; global XP advances once
  per credited activity; each thread replays byte-identically; no double credit on resim.

The debug menu (Piece 2) is the sanctioned way to exercise Passport threads without grinding.
