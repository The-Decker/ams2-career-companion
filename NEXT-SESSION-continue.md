# Next session — continue the Career Hub build

Paste the block below into a FRESH thread. Machine facts + build progress auto-load from memory
(`MEMORY.md` → `ams2-hub-build-progress.md`); the repo docs carry the rest.

---

Resume the AMS2 Career Companion in `Z:\Claude Code\ams2-career-companion` (git repo, .NET 10 / WPF).
Build/test from the repo root: `dotnet build src/Companion.App/Companion.App.csproj` and
`dotnet test tests/Companion.Tests/Companion.Tests.csproj` (+ the WPF render harness at
`tests/Companion.RenderHarness.Tests`).

**Current state:** on branch **`hub/increment-2`** (pushed to origin `The-Decker/ams2-career-companion`,
24 commits ahead of main, PR: https://github.com/The-Decker/ams2-career-companion/pull/new/hub/increment-2).
Suite **1105 tests + 15 render-harness green**, app compiles.

**Read first:** `docs/dev/career-hub-build.md` (increment ladder + the Increment-2 "Slice plan +
scoring-engine risk"), `docs/dev/career-hub-design.md` (locked hub design, 23 decisions), skim
`docs/dev/character-system.md`. The memory `ams2-hub-build-progress.md` has the exact per-slice status.

**Done so far:**
- **Increment 1 hub shell** — persistent tab rail, the shipped loop re-homed VERBATIM as the Race tab,
  unified global header + primary loop buttons, News feed (ticker → click-to-expand article + Why?
  chip), EraTheme rail badge, picture-rich career gallery, tear-off News window, window-level number-key
  tab parity.
- **Increment 2 weekend-model foundations** (all byte-identical for single-race, oracle untouched):
  2a pack `PackWeekend` model · 2b.1 `RoundResultEnvelope.QualifyingOrder` (v2→v3) · 2b.2 seam
  `CurrentWeekend()` + `ResultDraft.QualifyingOrder` · 2d.1 qualifying pace-anchor math · 2d.2 that
  anchor wired into the fold (`player.qualiAnchor` emitted ONLY when qualifying present).
- **5 parallel features merged:** 13 audited driver archetypes + a CI perk-balance audit · a
  generative news-article engine (`Companion.Core/News`) · Increment 3 History/Scrapbook tab + records
  book + `CareerTimeline()` seam · era-art gallery images (`data/ams2/era-art/`) · immersion settings
  (`EraThemingEnabled` + `NewsDetail`).

**Next — two workstreams:**

(A) **The SEQUENTIAL sim track — do NOT parallelize (deterministic-fold + f1db-oracle critical):**
- **2b.3** — the result-entry per-session UI flow: `HomeViewModel` gains a qualifying step gated on
  `CurrentWeekend()` (single-race unchanged); reuse `ResultEntryViewModel` for the qualifying order.
  Invasive to the shipped loop — keep every existing loop test green.
- **2c (ORACLE-GATED)** — per-session scoring: a per-session `PointsTableId` on `SessionResult`, and
  move `RoundScore` emission per-session with a sub-key `(Round, SessionIndex)`. MUST preserve today's
  merged result for existing round shapes, and re-run the f1db oracle across 2021–25 sprint fixtures.
  See `career-hub-build.md` "Slice plan + the scoring-engine risk (code mapped 2026-07-05)".

(B) **PARALLEL feature waves (fold-independent) — the user has a 20x sub and wants this cadence.**
The Agent tool's `isolation: "worktree"` FAILS here (the session cwd `Z:\Claude Code` isn't the git
root — the repo is the subfolder). Use MANUAL worktrees:
`git worktree add "Z:\Claude Code\ams2-worktrees\<name>" -b feat/<name>`, launch a background
general-purpose agent told to work ONLY in that path, keep the full suite green, and commit to its
branch; then `git merge --no-ff feat/<name>` and re-run the suite. (A `MigrationsV2Tests` /
`EraSignAndContinueTests` failure in a full parallel run is the KNOWN SQLite-parallel-disposal flake —
re-run confirms green.) Candidate parallel features:
- The full **clickable-everywhere "Why?" inspector** (read-only over the journal; `JournalFor` seam).
- **Robust gallery era** — resolve by the career's STORED season year, not by parsing the name (store
  `SeasonYear` in the `RecentCareers` MRU), plus an **"Open career…" file-picker** on the Start screen
  (older `.ams2career` files are currently unreachable from the UI).
- **More season packs** (content). **Character-creation wizard UI** (scope its state/journal wiring
  carefully — that part is sim-adjacent; keep it on the sequential track or a tightly-scoped agent).

**Hard constraints (never break):** the sim never sets an on-track finishing position; deterministic +
journaled + byte-replayable (single-race careers stay byte-identical); mouse+keyboard parity (decision
8); additive + data-driven. Keep the deterministic fold/scoring on ONE track; fan out only
fold-independent features.

**Assets:** the user (Mike) creates historical era photos and drops them in `data/ams2/era-art/` named
by year (`1967.jpg`, most-specific wins) or era-medium (`telegram.jpg` ≤1979 / `fax.jpg` 1980-93 /
`email.jpg` 1994+); `.jpg`/`.png`, ≈640×360 16:9. Dev builds copy them on BUILD (rebuild after adding);
a released build reads them beside the exe.

---

(Bare-minimum fallback: "resume the AMS2 Career Companion in `Z:\Claude Code\ams2-career-companion`,
read `NEXT-SESSION-continue.md`, and continue.")
