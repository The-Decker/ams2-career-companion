# Grand Prix Dynasty gating + debug menu + Racing Passport — roadmap

_2026-07-17, Claude (Head of Coding). Mike's direction: Dynasty is a chronologically-gated
historical timeline you play forward from the beginning; a debug menu previews everything during
development; Racing Passport activates and holds the historical seasons as threads. Fable builds the
Dynasty tycoon economy in parallel (`docs/dev/fable-tycoon-brief.md`) — this doc is the mode
infrastructure around it (Claude's lane: Core/ViewModels/Data)._

## Content reality (drives the gating)

Faithful packs on disk today: **1967, 1969, 1974, 1978, 1983, 1985, 1986, 1988, 1990–1993, 1995,
1997, 2000, 2005, 2006, 2008, 2010, 2016, 2020** (21). **No pre-1967 / Formula Junior pack exists
yet.** So "start at Formula Junior" is the *timeline concept*; 1967 is the first *playable* season.
Unbuilt years render as preview/"coming soon", never as playable, never synthesized
(career-modes-alpha1.md §3: missing years are unavailable, never fabricated).

---

## Piece 1 — Dynasty chronological gating

**Goal:** in `grandPrixDynasty` you start at the earliest point of the timeline and advance
chronologically. Future seasons are **previewable** (you can look at the pack — calendar, teams,
era) but **not playable** until you reach them in sequence. 1967 is the first playable stop.

- The creation-time `CampaignProgressionPlan.pinnedSeasonSequence` already pins the ordered faithful
  packs (career-modes-alpha1.md §3). Gating rides on top of it: **only the current (earliest
  unfinished) pinned season is playable; earlier are completed history; later are preview-locked.**
  Same three-state shape as the SMGP `CampaignTimeline` (Completed / Current / Locked) — reuse it.
- Add a **preview projection**: a locked future season exposes its *pack-level* identity (year, era,
  series, calendar venues, team list) for a "coming up / preview" screen, but NO play entry. This is
  the deliberate opposite of the SMGP spoiler rule — historical years are real-world known, so
  previewing them is fine; the gate is about *playing in order*, not hiding.
- A **Formula-Junior-era prologue** slot at the head of the timeline: a labelled "Formula Junior →
  1967" pre-championship stretch shown as preview/coming-soon until content exists, so the timeline
  starts where Mike wants even though play starts at 1967.
- Enforce at the session layer: `StartNextSeason` / creation may only pin/enter the sequence head;
  a jump to a later year throws (like SMGP's Season-18 guard). Debug menu (Piece 2) is the sanctioned
  bypass.
- Tests: the sequence is chronological + gap-honest; only the current season enters; a future season
  is preview-only; replay byte-identical.

## Piece 2 — Debug menu (dev-only)

**Goal:** preview/unlock everything while we build — jump to any year/season, any of the three modes,
force money/injury/terminal states, unlock locked seasons, reveal SMGP future lore.

- **Gated so it never reaches players**: behind a settings flag (`Settings.DeveloperMode`, off by
  default and not surfaced in the normal UI) AND/OR `#if DEBUG` for the build-time parts. A shipped
  Release with the flag off shows nothing.
- A `DebugMenuViewModel` exposing dev commands over `ICareerSession` + creation: open any pack as any
  mode, force-advance to a season, set balance/injury/level, toggle the gating unlock, dump the
  journal. Pure ViewModel + a bindable surface; the App lane adds a hidden panel (e.g., a key chord).
- Everything it does routes through the SAME session/fold seams (no back doors that bypass the fold),
  so a debug-created state still replays honestly — it just skips the gating/normal-flow.
- Tests: the menu is invisible/no-op with the flag off; each command hits the real seam.

## Piece 3 — Racing Passport activation

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

---

## Recommended order

1. **Debug menu (Piece 2) first** — it's the enabler: it lets us preview/test Dynasty gating, the
   tycoon economy Fable builds, and Passport threads without grinding through content each time.
   Smallest, self-contained, unblocks everything else.
2. **Dynasty gating (Piece 1)** — builds on the existing pinned-sequence + campaign-timeline shape;
   moderate; makes Dynasty playable-in-order.
3. **Racing Passport (Piece 3)** — largest; the portfolio ledger is already specified, so it's
   well-scoped, but it's the most work. Do it once the debug menu can exercise it.

Fable's tycoon economy (`fable-tycoon-brief.md`) proceeds in parallel with 1–3; it plugs into the
Dynasty fold the gating governs.
