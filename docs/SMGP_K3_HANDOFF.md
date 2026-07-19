# SMGP K3 handoff (mission SMGP-COMPLETE-001)

_The resume-anytime state of the SMGP completion mission. Updated 2026-07-18._

## Branch / commit

- Branch: `hub/increment-4` (this is the integration line; `mission/racing-passport-pure-racing`
  was merged in at `85ac707`).
- Last verified commit at handoff: the SMGP completion wave (validator + docs), on top of
  `fc724a7` (Passport nationality) and `d1fe87b` (Dynasty card IN DEVELOPMENT + rail renders).

## Current build / test status

- `dotnet build Companion.slnx`: clean.
- `dotnet test tests/Companion.Tests`: **2,885+ passed** (2,881 + the 4 validator tests),
  0 real failures; 1–2 SQLite-open tests can flake under parallel load and pass isolated
  (documented, not a defect).
- `dotnet test tests/Companion.RenderHarness.Tests`: **246 passed**.
- f1db oracle: **77/77**.

## What this mission has completed so far

- Phase-Zero audit: `docs/SMGP_COMPLETION_AUDIT.md` (verified baseline + feature matrix +
  decided positions + loop order).
- Consolidated 17-season validator: `tests/Companion.Tests/Smgp/SmgpWorldCompletenessTests.cs`
  (4/4 green): pack structure (24 teams / 34 drivers / 16 rounds, resolvable refs, Senna
  pinned), 17 lore entries, 24 logos + banners, 34 portraits + grid cars, all decodable.
- Balance: `docs/SMGP_BALANCE_REPORT.md` (harness evidence: cap rates, attrition, DNQ, arc
  termination, release-evidence run green 2026-07-18).
- Release evidence: `docs/SMGP_TEST_EVIDENCE.md` (suite counts, determinism runs, publish +
  fresh-install boot validation at 14:08, dist deploy at 14:10 boot-verified).
- User guide: `docs/SMGP_USER_GUIDE.md`.

## Open items (in order)

1. **Mike's sign-off on the Pit Wall Command Rail** — the four review frames are in
   `scratchpad/review-frames/` (gitignored): Dark/Light × 1.00/1.50 UI scale. This is the last
   blocker before the SMGP-1.0-alpha RC is formally cut.
2. Final RC publish + deploy after sign-off (the 14:10 dist build already contains everything
   except any further tweaks).
3. SMGP-1.0-alpha declaration + the completion report.

## Exact resume commands

```bash
cd "Z:/Claude Code/ams2-career-companion"
dotnet test Companion.slnx --nologo -v q                                  # full suite
dotnet test tests/Companion.Tests --filter FullyQualifiedName~SmgpWorldCompletenessTests
COMPANION_BALANCE_EVIDENCE=1 dotnet test tests/Companion.Tests --filter FullyQualifiedName~ReleaseEvidence
dotnet publish src/Companion.App -c Release --nologo                      # the RC build
```

If publish fails with `error BG1002 ... .baml cannot be found`: `rm -rf
src/Companion.App/obj/{Debug,Release}` and republish (known incremental-build artifact).
