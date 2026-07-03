# Phase 2: shared-memory auto-capture (contract — draft)

Manual entry stays first-class forever (it always works). Auto-capture PRE-FILLS the result
screen; the user reviews and applies. The sim still never decides outcomes — capture is
input assistance, nothing more.

## Source (research §3, verified against primary sources)

- AMS2 exposes the PC2 shared-memory API: memory-mapped file **`$pcars2$`**, struct v14
  (CREST2-AMS2 `SharedMemory.h`). Enable in-game: Options → System → Shared Memory =
  "Project CARS 2" (add this line to the briefing checklist when capture is enabled).
- Read discipline: `mSequenceNumber` is odd mid-write — spin until even, copy, re-check.
- Fields we consume: `mNumParticipants`, `mParticipantInfo[64]` (`mName[64]` — custom-AI
  names DO propagate, `mRacePosition`, `mLapsCompleted`), parallel arrays `mRaceStates`
  (FINISHED / DISQUALIFIED / RETIRED / DNF), `mFastestLapTimes`, `mSessionState`
  (SESSION_RACE...), `mTrackLocation`, `mTrackVariation`, `mLapsInEvent`.
- **End-of-race recipe** (proven by AMS2_SessionLogger): poll 1–10 Hz during SESSION_RACE,
  snapshot every valid frame, COMMIT the last valid snapshot when `mRaceState` →
  RACESTATE_INVALID after a race session (classification wipes on exit to menu — the last
  valid frame IS the result). C# marshalling reference: CrewChiefV4 (open source).
- .NET: `MemoryMappedFile.OpenExisting("$pcars2$")` + a blittable struct mirror. Struct
  version checked at open; mismatch = capture disabled with a clear message, manual entry
  unaffected (game-update churn is a known risk, PLAN Risks).

## Architecture

- `Companion.Ams2.SharedMemory`: struct mirror + `SharedMemoryReader` (open/poll/snapshot,
  version check) + `RaceResultCapture` (the end-of-race state machine). No UI deps; fully
  testable by replaying recorded frame sequences (fixtures from real sessions later; synthetic
  frames for unit tests now).
- `CaptureService` (ViewModels): background poll while the app shows the briefing/result
  screens for the current round; when a committed snapshot arrives, map it to a
  `ResultDraft`:
  - Participant name → grid seat via the round's GridPlan driver names (ordinal match, then
    diacritic-insensitive fallback; unmatched names surface as review items, never guesses).
  - mRacePosition order → Classified list; RETIRED/DNF → DidNotFinish (reason 'o' — shared
    memory carries no cause; the user can refine); DISQUALIFIED → Disqualified.
  - Track sanity check: `mTrackLocation/Variation` vs the round's track id (mismatch = warn,
    still offer the prefill).
- Result screen: a "Captured from AMS2" banner appears with Apply-prefill / Ignore; the
  grammar and drag-and-drop work on TOP of a prefill (corrections are normal edits on the
  same undo stack). Settings toggle: capture on/off (default on when the MMF exists).
- Second Monitor JSON import (alternative source) is a later increment: watcher on
  `Documents\SecondMonitor\Reports`, same ResultDraft mapping, schema is app-internal —
  best-effort with clear failure text.

## Safety & determinism

- Capture writes NOTHING to the journal by itself — only the user's Apply does (the fold is
  unchanged; the envelope gains `source: "captured"` provenance on the raw payload).
- Replay is unaffected: raw payloads are raw payloads regardless of how they were typed.
- The reader never blocks the UI thread; failures degrade to manual entry silently-but-logged.

## Verification

- Unit: sequence-number spin logic, end-of-race commit state machine over synthetic frame
  scripts (mid-race exit, red flag, alt-tab wipe, name mismatches), draft mapping incl.
  26-car grids with shared names absent.
- Integration (needs Mike + a real short race — Gate 3): capture a real session end-to-end,
  compare the prefill against the in-game classification screen.
